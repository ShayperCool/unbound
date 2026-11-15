using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>When the custom data module writes its streams (Unity 6000.0.71f1,
    /// tests/projects/particles custom_data and custom_data_runs): each run writes every live
    /// particle's colors but only the vectors of the particles it is for. It runs before a step
    /// ages the particles and again for the particles the step's emission (or Emit) starts.</summary>
    public class ParticleCustomDataTests : System.IDisposable
    {
        const float Lifetime = 1.013f;

        // The measured release and step settings (the probe's project defaults).
        readonly string m_Version = ProjectInfo.UnityVersion;
        readonly float m_MaximumDelta = Time.maximumParticleDeltaTime, m_FixedDelta = Time.fixedDeltaTime;

        public ParticleCustomDataTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Time.maximumParticleDeltaTime = 0.03f;
            Time.fixedDeltaTime = 0.02f;
        }

        public void Dispose()
        {
            ProjectInfo.UnityVersion = m_Version;
            Time.maximumParticleDeltaTime = m_MaximumDelta;
            Time.fixedDeltaTime = m_FixedDelta;
        }

        /// <summary>Custom1 is a one-component vector, Custom2 a white-to-black color: both read
        /// back the normalized age they were evaluated at (the color as 1 - r).</summary>
        static ParticleSystem Make(float rate, params ParticleSystem.Burst[] bursts) => Make(null, rate, bursts);

        static ParticleSystem Make(Transform parent, float rate, params ParticleSystem.Burst[] bursts)
        {
            var go = new GameObject("customdata"); go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
            var main = ps.main; main.playOnAwake = false; main.startLifetime = Lifetime; main.startSpeed = 2f;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.rateOverTime = rate; emission.SetBursts(bursts);
            var cd = ps.customData; cd.enabled = true;
            cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
            cd.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)));
            cd.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Color);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            cd.SetColor(ParticleSystemCustomData.Custom2, g);
            go.SetActive(true);
            return ps;
        }

        /// <summary>Per particle: its age in seconds, and the ages in seconds its vector and its
        /// color were evaluated at.</summary>
        static (float age, float vector, float color)[] Read(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            int n = ps.GetParticles(particles);
            var vectors = new List<Vector4>(); var colors = new List<Vector4>();
            ps.GetCustomParticleData(vectors, ParticleSystemCustomData.Custom1);
            ps.GetCustomParticleData(colors, ParticleSystemCustomData.Custom2);
            var result = new (float, float, float)[n];
            for (int i = 0; i < n; i++)
                result[i] = ((particles[i].startLifetime - particles[i].remainingLifetime),
                    vectors[i].x * Lifetime, (1f - colors[i].x) * Lifetime);
            return result;
        }

        static void Near(float expected, float actual) => Assert.InRange(actual, expected - 2e-5f, expected + 2e-5f);

        [Fact]
        public void WithoutBirthsBothStreamsHoldTheAgeBeforeTheLastStep()
        {
            var ps = Make(0f, new ParticleSystem.Burst(0f, 3));
            try
            {
                // 0.1 s runs as four 0.025 s steps; only the first one starts particles.
                ps.Simulate(0.1f, true, true, false);
                var read = Read(ps);
                Assert.Equal(3, read.Length);
                foreach (var (age, vector, color) in read)
                {
                    Near(0.1f, age);
                    Near(0.075f, vector);
                    Near(0.075f, color);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void EmitRewritesEveryColorAtTheCurrentAgeAndNoVector()
        {
            var ps = Make(0f, new ParticleSystem.Burst(0f, 3));
            try
            {
                ps.Simulate(0.1f, true, true, false);
                ps.Emit(1);
                var read = Read(ps);
                Assert.Equal(4, read.Length);
                for (int i = 0; i < 3; i++)
                {
                    Near(0.1f, read[i].age);
                    Near(0.075f, read[i].vector);
                    Near(0.1f, read[i].color);
                }
                Near(0f, read[3].age);
                Near(0f, read[3].vector);
                Near(0f, read[3].color);
                // The next step's run (before it ages them) writes every color again.
                ps.Simulate(0.02f, true, false, false);
                foreach (var (age, vector, color) in Read(ps))
                {
                    Near(age - 0.02f, vector);
                    Near(age - 0.02f, color);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void BirthsWithinAStepTakeTheirColorAtBirthAndTheOthersAtTheirCurrentAge()
        {
            // 70 per second in single 0.02 s steps: births in the middle of most steps.
            var ps = Make(70f);
            try
            {
                for (int step = 0; step < 4; step++)
                {
                    ps.Simulate(0.02f, true, step == 0, false);
                    foreach (var (age, vector, color) in Read(ps))
                    {
                        bool newborn = age < 0.02f - 1e-5f;
                        Near(newborn ? 0f : age - 0.02f, vector);
                        Near(newborn ? 0f : age, color);
                    }
                }
                Assert.True(ps.particleCount >= 5);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void AStepWithoutBirthsAfterOneWithBirthsLagsAgain()
        {
            // 7 per second over 0.1 s calls (four 0.025 s steps): a birth falls in the third step
            // of the second call, and its fourth step starts nothing.
            var ps = Make(7f);
            try
            {
                ps.Simulate(0.1f, true, true, false);
                ps.Simulate(0.1f, true, false, false);
                var read = Read(ps);
                Assert.Single(read);
                Near(0.025f, read[0].age - read[0].color);
                Near(0.025f, read[0].age - read[0].vector);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void SubEmitterChildrenAreEvaluatedBeforeEachCatchUpStep()
        {
            // Two parents die within the second call; each emission's children are caught up to the
            // call's end in fixed 0.02 s steps, and each emission and catch-up step is a run.
            var parentGo = new GameObject("parent"); parentGo.SetActive(false);
            var parent = parentGo.AddComponent<ParticleSystem>();
            parent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            parent.useAutoRandomSeed = false; parent.randomSeed = 12345;
            var main = parent.main; main.playOnAwake = false; main.startLifetime = 0.13f; main.startSpeed = 1f;
            var shape = parent.shape; shape.enabled = false;
            var emission = parent.emission; emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            var child = Make(parent.transform, 0f, new ParticleSystem.Burst(0f, 2));
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            parentGo.SetActive(true);
            try
            {
                parent.Simulate(0.1f, true, true, false);
                Assert.Equal(0, child.particleCount);
                parent.Simulate(0.1f, true, false, false);
                var read = Read(child);
                Assert.Equal(4, read.Length);
                int current = 0, lagging = 0;
                foreach (var (age, vector, color) in read)
                {
                    Near(0.06f, age);
                    Near(0.04f, vector);
                    if (System.Math.Abs(color - 0.06f) < 2e-5f) current++;
                    else { Near(0.04f, color); lagging++; }
                }
                // The second emission's catch-up ran after the first emission's children had
                // finished theirs.
                Assert.Equal(2, current);
                Assert.Equal(2, lagging);
                // The child's next step runs over all of them before it ages them.
                parent.Simulate(0.1f, true, false, false);
                foreach (var (age, vector, color) in Read(child))
                {
                    Near(age - 0.025f, vector);
                    Near(age - 0.025f, color);
                }
            }
            finally { Object.DestroyImmediate(parentGo); }
        }
    }
}
