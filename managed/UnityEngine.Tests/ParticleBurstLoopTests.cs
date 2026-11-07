using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Bursts at and around the end of the loop (Unity 6000.0.71f1,
    /// tests/projects/particleburstloop; expected values are that reference's): a burst whose time
    /// is the duration fires in a step crossing the loop end of a looping system; bursts due in one
    /// step share the youngest of their ages; across the loop end each part of the step stands for
    /// the whole step, the part before the end measured to 0.0001 s past it, and every burst of the
    /// step takes its start values at the step's end.</summary>
    public class ParticleBurstLoopTests
    {
        /// <summary>A looping 1 s system with the given bursts: particles live 4 s, do not move, and
        /// their start size runs from 1 at the loop's start to 3 at its end.</summary>
        static ParticleSystem Make(string name, bool loop, params ParticleSystem.Burst[] bursts)
        {
            var go = new GameObject(name); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false; ps.randomSeed = 4242u;
            var m = ps.main; m.playOnAwake = false; m.duration = 1f; m.loop = loop; m.maxParticles = 500;
            m.startLifetime = 4f; m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f, 2f, 2f), new Keyframe(1f, 3f, 2f, 2f)));
            var s = ps.shape; s.enabled = false;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(bursts);
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Burst Burst(float time, short count, int cycles = 1, float interval = .01f) =>
            new ParticleSystem.Burst(time, count) { cycleCount = cycles, repeatInterval = interval };

        static ParticleSystem.Particle[] Run(ParticleSystem ps, params float[] calls)
        {
            for (int i = 0; i < calls.Length; i++) ps.Simulate(calls[i], true, i == 0, false);
            var buffer = new ParticleSystem.Particle[ps.main.maxParticles];
            return buffer.Take(ps.GetParticles(buffer)).ToArray();
        }

        /// <summary>The particle probes' bound: 0.0002 absolute plus 0.0002 relative.</summary>
        static void Near(float expected, float actual) =>
            Assert.True(System.Math.Abs(expected - actual) <= 2e-4f + 2e-4f * System.Math.Abs(expected), $"expected {expected}, got {actual}");

        static void Check(ParticleSystem.Particle[] particles, params (float size, float remaining)[] expected)
        {
            Assert.Equal(expected.Length, particles.Length);
            for (int i = 0; i < expected.Length; i++) { Near(expected[i].size, particles[i].startSize); Near(expected[i].remaining, particles[i].remainingLifetime); }
        }

        static void Destroy(ParticleSystem ps) => Object.DestroyImmediate(ps.gameObject);

        [Fact]
        public void ABurstAtTheDurationFiresWhenAStepCrossesTheLoopEnd()
        {
            var ps = Make("burst-end-cross", true, Burst(1f, 2));
            try
            {
                Assert.Empty(Run(ps, .9f));
                // 0.9 to 1.1 in seven internal steps: the one from 0.986 to 1.014 crosses the end.
                Check(Run(ps, .9f, .2f), (1.02857065f, 3.914087f), (1.02857065f, 3.914087f));
            }
            finally { Destroy(ps); }
        }

        [Fact]
        public void ABurstAtTheDurationFiresWithFixedStepsThatEndJustBeforeIt()
        {
            var ps = Make("burst-end-fixed", true, Burst(1f, 2));
            try
            {
                for (int i = 0; i < 3; i++) ps.Simulate(i == 0 ? .25f : .5f, true, i == 0, true);
                var buffer = new ParticleSystem.Particle[8];
                Check(buffer.Take(ps.GetParticles(buffer)).ToArray(), (1.03999853f, 3.76015377f), (1.03999853f, 3.76015377f));
            }
            finally { Destroy(ps); }
        }

        [Fact]
        public void ANonLoopingSystemNeverEmitsABurstAtItsDuration()
        {
            var ps = Make("burst-end-once", false, Burst(1f, 2), Burst(0f, 1));
            try { Check(Run(ps, .9f, .2f, .2f), (1.06f, 2.70000052f)); }
            finally { Destroy(ps); }
        }

        [Fact]
        public void ARepeatLandingOnTheDurationDoesNotFire()
        {
            var ps = Make("burst-repeat-end", true, Burst(.5f, 1, 3, .25f));
            try { Check(Run(ps, .3f, .3f, .3f, .3f), (2.0363636f, 3.30000019f), (2.52727222f, 3.5363636f)); }
            finally { Destroy(ps); }
        }

        [Fact]
        public void AnEndlessBurstAtTheLoopStartIsAgedTheWholeCrossingStep()
        {
            var ps = Make("burst-endless", true, Burst(0f, 1, 0, .5f));
            try { Check(Run(ps, .3f, .3f, .3f, .3f), (1.0545454f, 2.80000019f), (2.0363636f, 3.29090929f), (1.01818085f, 3.78181815f)); }
            finally { Destroy(ps); }
        }

        [Fact]
        public void BurstsDueInOneStepShareTheYoungestAge()
        {
            var ps = Make("burst-pair", true, Burst(.295f, 1), Burst(.305f, 2));
            try
            {
                var p = Run(ps, .29f, .02f);
                Check(p, (1.61999989f, 3.99500012f), (1.61999989f, 3.99500012f), (1.61999989f, 3.99500012f));
            }
            finally { Destroy(ps); }
        }

        [Fact]
        public void AStepStartingJustBeforeTheEndAgesItsBurstsByHalfTheStepAtTheMargin()
        {
            // The 0.02 s step starts 0.0001 s before the end, as far as the margin reaches past it: a
            // burst at the duration is aged 0.02 * 0.0001 / (0.0001 + 0.0001) = 0.01 s, and the burst
            // at the new loop's start, aged the whole step, shares that younger age.
            var ps = Make("burst-margin", true, Burst(0f, 1), Burst(.9995f, 3), Burst(1f, 2));
            try
            {
                var p = Run(ps, .9999f, .02f);
                Assert.Equal(7, p.Length);
                foreach (var n in p.Skip(4)) { Near(1.03979945f, n.startSize); Near(3.990012f, n.remainingLifetime); }
            }
            finally { Destroy(ps); }
        }

        [Fact]
        public void ChangingTheBurstsKeepsTheCyclesAlreadyDueAsFired()
        {
            var ps = Make("burst-change", true, Burst(.2f, 1));
            try
            {
                Assert.Single(Run(ps, .5f));
                // Set at 0.5 s: the burst at 0.3 s has passed and waits for the next loop (Unbound's
                // choice), the one at 0.7 s is ahead and fires (particlecurves sim/*/c measures it at
                // the duration).
                var e = ps.emission;
                e.SetBursts(new[] { Burst(.3f, 2), Burst(.7f, 1) });
                ps.Simulate(.3f, true, false, false);
                Assert.Equal(2, ps.particleCount);

                // An endless burst every 0.13 s, set at 0.8 s and set again, fires none of its
                // repeats up to 0.78 s; the one at 0.91 s comes.
                e.SetBursts(new[] { Burst(0f, 1, 0, .13f) });
                e.SetBurst(0, Burst(0f, 1, 0, .13f));
                int before = ps.particleCount;
                ps.Simulate(.15f, true, false, false);
                Assert.Equal(before + 1, ps.particleCount);
            }
            finally { Destroy(ps); }
        }

        [Fact]
        public void ChangingTheBurstsOnTheTimeOfARepeatDoesNotFireItAgain()
        {
            // 15 steps of 0.02 s end at 0.29999998, where the repeat every 0.06 s (5 x 0.06 in
            // float, the same value) has just fired.
            var ps = Make("burst-change-on-repeat", true, Burst(0f, 1, 0, .06f));
            try
            {
                for (int i = 0; i < 15; i++) ps.Simulate(.02f, true, i == 0, false);
                int fired = ps.particleCount;
                var e = ps.emission;
                e.SetBurst(0, Burst(0f, 1, 0, .06f));
                ps.Simulate(.02f, true, false, false);
                Assert.Equal(fired, ps.particleCount);
                // The next repeat, at 0.36 s, comes once.
                ps.Simulate(.06f, true, false, false);
                Assert.Equal(fired + 1, ps.particleCount);
            }
            finally { Destroy(ps); }
        }

        [Fact]
        public void ABurstThatEmitsNothingDoesNotSetTheStepsAge()
        {
            // Unbound's choice (only emitting bursts are measured): the empty burst at 0.305 s
            // leaves the burst at 0.295 s its own age, 0.015 s.
            var ps = Make("burst-empty", true, Burst(.295f, 2), Burst(.305f, 0));
            try
            {
                var p = Run(ps, .29f, .02f);
                Assert.Equal(2, p.Length);
                foreach (var n in p) Near(3.985f, n.remainingLifetime);
            }
            finally { Destroy(ps); }
        }

        [Fact]
        public void TheLoopEndRuleDoesNotDependOnTheDurationOrTheStep()
        {
            // A control outside the probe: a 2.5 s loop, a 0.1 s call from 2.44 s split into four
            // 0.025 s internal steps, the third (2.49 to 2.515) crossing the end 0.01 s after it starts.
            var go = new GameObject("burst-end-control"); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            try
            {
                ps.useAutoRandomSeed = false; ps.randomSeed = 7u;
                var m = ps.main; m.playOnAwake = false; m.duration = 2.5f; m.loop = true; m.startLifetime = 4f; m.startSpeed = 0f;
                var s = ps.shape; s.enabled = false;
                var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { Burst(2.5f, 3) });
                go.SetActive(true);
                var p = Run(ps, 2.44f, .1f);
                Assert.Equal(3, p.Length);
                // Its age at the crossing step's end: 0.025 * 0.0001 / (0.01 + 0.0001); then the last
                // internal step (0.025 s).
                float expected = .025f * .0001f / .0101f + .025f;
                foreach (var n in p) Near(4f - expected, n.remainingLifetime);
                Assert.Equal(1, ps.m_Loop);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
