using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class MinMaxCurveFields : MonoBehaviour
    {
        public ParticleSystem.MinMaxCurve value;
        public AnimationCurve curve;
    }

    /// <summary>
    /// ParticleSystem.MinMaxCurve keeps its six values apart, while a module property stores one
    /// scalar per value (Unity 6000.0.71f1, tests/projects/particlecurves): the setter keeps what the
    /// mode uses, the getter returns only that, the *Multiplier shortcut is the stored scalar, and
    /// every way in keeps the scalars within the property's range.
    /// </summary>
    public class ParticleMinMaxCurveTests
    {
        static AnimationCurve CurveA() => new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f));
        static AnimationCurve CurveB() => new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(1f, 2f));

        static ParticleSystem Make()
        {
            var go = new GameObject("minmaxcurve");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.randomSeed = 4242u;
            var main = ps.main;
            main.playOnAwake = false;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.rateOverTime = 0f;
            go.SetActive(true);
            return ps;
        }

        static void AssertCurve(ParticleSystem.MinMaxCurve c, ParticleSystemCurveMode mode, float constantMin, float constantMax, float multiplier)
        {
            Assert.Equal(mode, c.mode);
            Assert.Equal(constantMin, c.constantMin);
            Assert.Equal(constantMax, c.constantMax);
            Assert.Equal(constantMax, c.constant);
            Assert.Equal(multiplier, c.curveMultiplier);
        }

        [Fact]
        public void ConstructorsLeaveTheUnusedValuesZero()
        {
            AssertCurve(new ParticleSystem.MinMaxCurve(2f), ParticleSystemCurveMode.Constant, 0f, 2f, 0f);
            AssertCurve(5f, ParticleSystemCurveMode.Constant, 0f, 5f, 0f);
            AssertCurve(new ParticleSystem.MinMaxCurve(1f, 4f), ParticleSystemCurveMode.TwoConstants, 1f, 4f, 0f);
            var curve = new ParticleSystem.MinMaxCurve(3f, CurveA());
            AssertCurve(curve, ParticleSystemCurveMode.Curve, 0f, 0f, 3f);
            Assert.Null(curve.curveMin);
            var two = new ParticleSystem.MinMaxCurve(1.5f, CurveA(), CurveB());
            AssertCurve(two, ParticleSystemCurveMode.TwoCurves, 0f, 0f, 1.5f);
            Assert.NotNull(two.curveMin);
            AssertCurve(default, ParticleSystemCurveMode.Constant, 0f, 0f, 0f);
        }

        [Fact]
        public void TheStructsSettersAndModeSwitchesKeepEveryValue()
        {
            var c = new ParticleSystem.MinMaxCurve(3f, CurveA());
            c.constant = 7f;
            c.constantMin = -1f;
            AssertCurve(c, ParticleSystemCurveMode.Curve, -1f, 7f, 3f);
            Assert.Equal(1.5f, c.Evaluate(1f));
            c.mode = ParticleSystemCurveMode.Constant;
            Assert.Equal(7f, c.Evaluate(0.5f));
            c.mode = ParticleSystemCurveMode.TwoConstants;
            Assert.Equal(3f, c.Evaluate(0.5f, 0.5f));
            c.mode = ParticleSystemCurveMode.Curve;
            Assert.Equal(2.25f, c.Evaluate(0.5f));
            c.curveMultiplier = 9f;
            Assert.Equal(7f, c.constant);
            Assert.Equal(6.75f, c.Evaluate(0.5f));
        }

        [Fact]
        public void EvaluateUsesTheModesValuesAndClampsTheLerp()
        {
            var two = new ParticleSystem.MinMaxCurve(1f, 4f);
            Assert.Equal(1.75f, two.Evaluate(0.5f, 0.25f));
            Assert.Equal(4f, two.Evaluate(0.5f, 1.5f));
            Assert.Equal(1f, two.Evaluate(0.5f, -0.5f));
            var curves = new ParticleSystem.MinMaxCurve(1.5f, CurveA(), CurveB());
            // Lerp(curveMin, curveMax) times the multiplier; curve mode ignores the lerp.
            Assert.Equal(1.265625f, curves.Evaluate(0.5f, 0.25f), 6);
            Assert.Equal(1.6875f, curves.Evaluate(0.5f, 2f), 6);
            var one = new ParticleSystem.MinMaxCurve(3f, CurveA());
            Assert.Equal(one.Evaluate(0.5f, 0f), one.Evaluate(0.5f, 1f));
            // A curve mode without its curve throws, as Unity's does.
            var missing = new ParticleSystem.MinMaxCurve(2f) { mode = ParticleSystemCurveMode.Curve };
            Assert.Throws<NullReferenceException>(() => missing.Evaluate(0.5f));
        }

        [Fact]
        public void AModuleReturnsOnlyWhatTheModeUses()
        {
            var ps = Make();
            try
            {
                var main = ps.main;
                var c = new ParticleSystem.MinMaxCurve(2f) { curveMultiplier = 9f, curve = CurveB() };
                main.startSize = c;
                var read = main.startSize;
                AssertCurve(read, ParticleSystemCurveMode.Constant, 0f, 2f, 1f);
                Assert.Null(read.curveMax);
                Assert.Null(read.curveMin);

                c = new ParticleSystem.MinMaxCurve(3f, CurveA()) { constant = 7f, constantMin = 0.5f, curveMin = CurveB() };
                main.startSize = c;
                read = main.startSize;
                AssertCurve(read, ParticleSystemCurveMode.Curve, 0f, 0f, 3f);
                Assert.Equal(new[] { 1f, 0.5f }, Values(read.curveMax));
                Assert.Null(read.curveMin);
                Assert.NotSame(read.curveMax, main.startSize.curveMax);

                main.startSize = new ParticleSystem.MinMaxCurve(1.5f, CurveA(), CurveB()) { constantMax = 8f };
                read = main.startSize;
                AssertCurve(read, ParticleSystemCurveMode.TwoCurves, 0f, 0f, 1.5f);
                Assert.Equal(new[] { 1f, 0.5f }, Values(read.curveMin));
                Assert.Equal(new[] { 0.25f, 2f }, Values(read.curveMax));

                main.startSize = new ParticleSystem.MinMaxCurve(1f, 4f) { curveMultiplier = 6f };
                AssertCurve(main.startSize, ParticleSystemCurveMode.TwoConstants, 1f, 4f, 1f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        static float[] Values(AnimationCurve curve)
        {
            var v = new float[curve.length];
            for (int i = 0; i < v.Length; i++) v[i] = curve[i].value;
            return v;
        }

        [Fact]
        public void ConstantAndMultiplierShareTheModulesScalarAcrossModeSwitches()
        {
            var ps = Make();
            try
            {
                var main = ps.main;
                // A constant set on a curve-mode value is not stored: switching the read value to
                // Constant gives 0, and a constant's curve multiplier reads 1.
                main.startSize = new ParticleSystem.MinMaxCurve(3f, CurveA()) { constant = 7f };
                var s = main.startSize;
                s.mode = ParticleSystemCurveMode.Constant;
                main.startSize = s;
                AssertCurve(main.startSize, ParticleSystemCurveMode.Constant, 0f, 0f, 1f);

                // A curve does not survive a pass through Constant: a curve mode without a curve
                // stores the constant curve 1.
                main.startSize = new ParticleSystem.MinMaxCurve(3f, CurveB());
                main.startSize = 2f;
                s = main.startSize;
                s.mode = ParticleSystemCurveMode.TwoCurves;
                main.startSize = s;
                var read = main.startSize;
                AssertCurve(read, ParticleSystemCurveMode.TwoCurves, 0f, 0f, 1f);
                Assert.Equal(new[] { 1f }, Values(read.curveMax));
                Assert.Equal(new[] { 1f }, Values(read.curveMin));
                Assert.Equal(0f, read.curveMax[0].time);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(ParticleSystemCurveMode.Constant)]
        [InlineData(ParticleSystemCurveMode.TwoConstants)]
        [InlineData(ParticleSystemCurveMode.Curve)]
        [InlineData(ParticleSystemCurveMode.TwoCurves)]
        public void TheMultiplierShortcutIsTheStoredScalarInEveryMode(ParticleSystemCurveMode mode)
        {
            var ps = Make();
            try
            {
                var emission = ps.emission;
                emission.rateOverTime = mode switch
                {
                    ParticleSystemCurveMode.Constant => new ParticleSystem.MinMaxCurve(2f),
                    ParticleSystemCurveMode.TwoConstants => new ParticleSystem.MinMaxCurve(1f, 4f),
                    ParticleSystemCurveMode.Curve => new ParticleSystem.MinMaxCurve(3f, CurveA()),
                    _ => new ParticleSystem.MinMaxCurve(1.5f, CurveA(), CurveB()),
                };
                float stored = mode switch
                {
                    ParticleSystemCurveMode.Constant => 2f,
                    ParticleSystemCurveMode.TwoConstants => 4f,
                    ParticleSystemCurveMode.Curve => 3f,
                    _ => 1.5f,
                };
                Assert.Equal(stored, emission.rateOverTimeMultiplier);
                emission.rateOverTimeMultiplier = 6f;
                var read = emission.rateOverTime;
                Assert.Equal(mode, read.mode);
                bool curves = mode == ParticleSystemCurveMode.Curve || mode == ParticleSystemCurveMode.TwoCurves;
                Assert.Equal(curves ? 6f : 1f, read.curveMultiplier);
                Assert.Equal(curves ? 0f : 6f, read.constantMax);
                Assert.Equal(mode == ParticleSystemCurveMode.TwoConstants ? 1f : 0f, read.constantMin);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void EachWayInKeepsTheScalarsWithinThePropertysRange()
        {
            var ps = Make();
            try
            {
                var main = ps.main;
                main.startSize = new ParticleSystem.MinMaxCurve(-3f, -1f);
                AssertCurve(main.startSize, ParticleSystemCurveMode.TwoConstants, 0f, 0f, 1f);
                // Bounded on their own, not reordered.
                main.startSize = new ParticleSystem.MinMaxCurve(4f, 1f);
                AssertCurve(main.startSize, ParticleSystemCurveMode.TwoConstants, 4f, 1f, 1f);
                main.startSizeMultiplier = -6f;
                Assert.Equal(0f, main.startSizeMultiplier);
                main.startLifetime = new ParticleSystem.MinMaxCurve(-3f, CurveA());
                Assert.Equal(ParticleSystem.ModuleCurve.MinimumStartLifetime, main.startLifetime.curveMultiplier);
                var collision = ps.collision;
                collision.bounce = 1000f;
                Assert.Equal(2f, collision.bounceMultiplier);
                collision.dampenMultiplier = 5f;
                Assert.Equal(1f, collision.dampen.constant);
                var sheet = ps.textureSheetAnimation;
                sheet.frameOverTimeMultiplier = 3f;
                Assert.Equal(ParticleSystem.ModuleCurve.LastFrame, sheet.frameOverTime.curveMultiplier);
                var trails = ps.trails;
                trails.lifetime = new ParticleSystem.MinMaxCurve(-1f, 1.5f);
                AssertCurve(trails.lifetime, ParticleSystemCurveMode.TwoConstants, 0f, 1f, 1f);
                // Unbounded properties keep negative values.
                var force = ps.forceOverLifetime;
                force.y = new ParticleSystem.MinMaxCurve(-3f, -1f);
                AssertCurve(force.y, ParticleSystemCurveMode.TwoConstants, -3f, -1f, 1f);
                var emission = ps.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(-2f)) });
                var bursts = new ParticleSystem.Burst[1];
                emission.GetBursts(bursts);
                AssertCurve(bursts[0].count, ParticleSystemCurveMode.Constant, 0f, 0f, 1f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ABurstKeepsItsCountAsGivenUntilTheModuleStoresIt()
        {
            var burst = new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(-2f));
            AssertCurve(burst.count, ParticleSystemCurveMode.Constant, 0f, -2f, 0f);
            var ps = Make();
            try
            {
                var emission = ps.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(3f, CurveA())) });
                var read = emission.GetBurst(0).count;
                AssertCurve(read, ParticleSystemCurveMode.Curve, 0f, 0f, 3f);
                Assert.NotSame(read.curveMax, emission.GetBurst(0).count.curveMax);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void SceneDataIsBoundedAndKeepsOnlyWhatTheModeUses()
        {
            const string yaml = @"ParticleSystem:
  InitialModule:
    startSize:
      serializedVersion: 2
      minMaxState: 3
      scalar: -2
      minScalar: -3
    startSpeed:
      serializedVersion: 2
      minMaxState: 0
      scalar: 1.5
      minScalar: 0.7
      maxCurve:
        serializedVersion: 2
        m_Curve:
        - serializedVersion: 3
          time: 0
          value: 2
          inSlope: 0
          outSlope: 0
          tangentMode: 0
          weightedMode: 0
          inWeight: 0
          outWeight: 0
        m_PreInfinity: 2
        m_PostInfinity: 2
        m_RotationOrder: 4
    gravityModifier:
      serializedVersion: 2
      minMaxState: 1
      scalar: -4
      minScalar: 0
  CollisionModule:
    m_Bounce:
      serializedVersion: 2
      minMaxState: 0
      scalar: 5
      minScalar: 0
";
            var go = new GameObject("authored");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            try
            {
                ps.ReadSerialized(TestYaml.DocumentBody(yaml), new NullResolver());
                AssertCurve(ps.main.startSize, ParticleSystemCurveMode.TwoConstants, 0f, 0f, 1f);
                var speed = ps.main.startSpeed;
                AssertCurve(speed, ParticleSystemCurveMode.Constant, 0f, 1.5f, 1f);
                Assert.Null(speed.curveMax);
                // A curve mode without serialized curves stores the constant curve 1.
                var gravity = ps.main.gravityModifier;
                AssertCurve(gravity, ParticleSystemCurveMode.Curve, 0f, 0f, -4f);
                Assert.Equal(new[] { 1f }, Values(gravity.curveMax));
                Assert.Equal(2f, ps.collision.bounceMultiplier);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void ForceFieldsAndCustomDataStoreLikeModules()
        {
            var go = new GameObject("field");
            try
            {
                var field = go.AddComponent<ParticleSystemForceField>();
                AssertCurve(field.gravity, ParticleSystemCurveMode.Constant, 0f, 1f, 1f);
                AssertCurve(field.vectorFieldSpeed, ParticleSystemCurveMode.Constant, 0f, 1f, 1f);
                AssertCurve(field.drag, ParticleSystemCurveMode.Constant, 0f, 0f, 1f);
                field.rotationAttraction = new ParticleSystem.MinMaxCurve(-3f, CurveA()) { constant = 4f };
                AssertCurve(field.rotationAttraction, ParticleSystemCurveMode.Curve, 0f, 0f, -3f);
                var ps = go.AddComponent<ParticleSystem>();
                var custom = ps.customData;
                custom.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(-3f, -1f) { curveMultiplier = 5f });
                AssertCurve(custom.GetVector(ParticleSystemCustomData.Custom1, 0), ParticleSystemCurveMode.TwoConstants, -3f, -1f, 1f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>A control outside the probe's Simulate setup: Emit while playing, with the start
        /// speed in each mode. The particles move at the stored scalar's value.</summary>
        [Fact]
        public void EmittedParticlesUseTheStoredScalar()
        {
            var ps = Make();
            try
            {
                var main = ps.main;
                main.startLifetime = 10f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, AnimationCurve.Constant(0f, 1f, 1.5f)) { constant = 7f };
                ps.Play();
                ps.Emit(1);
                Assert.Equal(3f, Speed(ps, 0), 5);
                var s = main.startSpeed;
                s.mode = ParticleSystemCurveMode.Constant;
                main.startSpeed = s;
                ps.Emit(1);
                Assert.Equal(0f, Speed(ps, 1), 5);
                main.startSpeedMultiplier = 4f;
                ps.Emit(1);
                Assert.Equal(4f, Speed(ps, 2), 5);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        /// <summary>A new component's null curves stay null; a copy made by Instantiate has empty
        /// curves, as after loading (6000.0.71f1, particlecurves fields/fresh, fields/freshclone).</summary>
        [Fact]
        public void InstantiateGivesNullSerializedCurvesEmptyCurves()
        {
            var go = new GameObject("fields");
            GameObject copy = null;
            try
            {
                var fields = go.AddComponent<MinMaxCurveFields>();
                Assert.Null(fields.value.curveMax);
                Assert.Null(fields.curve);
                fields.value = new ParticleSystem.MinMaxCurve(5f, CurveA()) { constant = 11f };
                copy = Object.Instantiate(go);
                var cloned = copy.GetComponent<MinMaxCurveFields>();
                Assert.Equal(11f, cloned.value.constant);
                Assert.Equal(5f, cloned.value.curveMultiplier);
                Assert.Equal(new[] { 1f, 0.5f }, Values(cloned.value.curveMax));
                Assert.NotNull(cloned.value.curveMin);
                Assert.Equal(0, cloned.value.curveMin.length);
                Assert.NotNull(cloned.curve);
                Assert.Equal(0, cloned.curve.length);
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        static float Speed(ParticleSystem ps, int index)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            ps.GetParticles(particles);
            return particles[index].velocity.magnitude;
        }
    }
}
