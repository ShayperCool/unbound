using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>How the Animator samples an authored clip curve: each key pair as a single-precision
    /// cubic in the time since the first key (Unity 6000.0.71f1: tests/projects/humanfirstpose,
    /// animculling, humanoid), with weighted, stepped and very short segments and times outside
    /// the keys left to AnimationCurve.Evaluate.</summary>
    public class ClipCurveSegmentsTests
    {
        static ClipCurve Of(AnimationCurve curve) => new ClipCurve { Curve = curve };

        /// <summary>The expected cubic in the same single-precision operation order (the
        /// rounding itself is checked against the Editor by the probes named above).</summary>
        static float Cubic(Keyframe k0, Keyframe k1, float time)
        {
            float dx = k1.time - k0.time;
            float inv = 1f / dx;
            float dy = k1.value - k0.value;
            float d1 = k0.outTangent * dx, d2 = k1.inTangent * dx;
            float a = (d1 + d2 - dy - dy) * (inv * inv) * inv;
            float b = (dy + dy + dy - d1 - d1 - d2) * (inv * inv);
            float t = time - k0.time;
            return ((a * t + b) * t + k0.outTangent) * t + k0.value;
        }

        static AnimationCurve Muscle() => new AnimationCurve(
            new Keyframe(0f, 0.2f, 0.7f, 0.7f),
            new Keyframe(1f / 30f, 0.231f, 1.3f, 1.3f),
            new Keyframe(2f / 30f, 0.29f, -0.4f, -0.4f),
            new Keyframe(0.1f, 0.25f, -2.1f, -2.1f));

        [Fact]
        public void UnweightedSegmentsEvaluateAsTheCubicInTheTimeSinceTheirFirstKey()
        {
            var curve = Muscle();
            var clipCurve = Of(curve);
            for (int i = 0; i <= 300; i++)
            {
                float time = 0.1f * i / 300f;
                int s = time >= 2f / 30f ? 2 : time >= 1f / 30f ? 1 : 0;
                float value = clipCurve.Evaluate(time);
                if (time < curve[3].time)
                    Assert.Equal(Cubic(curve[s], curve[s + 1], time), value);
                // The same Hermite curve the script API gives, up to rounding.
                Assert.Equal(curve.Evaluate(time), value, 1e-6f);
            }
            for (int k = 0; k < curve.length; k++) Assert.Equal(curve[k].value, clipCurve.Evaluate(curve[k].time));
        }

        [Fact]
        public void TheDefaultWeightsOfUnweightedKeysDoNotChangeTheirEvaluation()
        {
            var keys = Muscle().keys;
            for (int i = 0; i < keys.Length; i++) { keys[i].inWeight = 1f / 3f; keys[i].outWeight = 1f / 3f; }
            var withWeights = Of(new AnimationCurve(keys));
            var plain = Of(Muscle());
            for (int i = 0; i <= 50; i++)
            {
                float time = 0.1f * i / 50f;
                Assert.Equal(plain.Evaluate(time), withWeights.Evaluate(time));
            }
        }

        [Fact]
        public void WeightedSegmentsAndTimesOutsideTheKeysUseTheCurvesOwnEvaluation()
        {
            var keys = Muscle().keys;
            keys[1].weightedMode = WeightedMode.Out;
            keys[1].outWeight = 0.8f;
            var curve = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong };
            var clipCurve = Of(curve);
            foreach (float time in new[] { 0.04f, 0.05f, 0.06f, -0.03f, 0.13f, 0.27f })
                Assert.Equal(curve.Evaluate(time), clipCurve.Evaluate(time));
            // The unweighted neighbours keep the cubic.
            Assert.Equal(Cubic(keys[0], keys[1], 0.02f), clipCurve.Evaluate(0.02f));
            Assert.Equal(Cubic(keys[2], keys[3], 0.08f), clipCurve.Evaluate(0.08f));
        }

        [Fact]
        public void SteppedAndVeryShortSegmentsUseTheCurvesOwnEvaluation()
        {
            foreach (var curve in new[]
            {
                new AnimationCurve(new Keyframe(0f, 1f, 0f, float.PositiveInfinity), new Keyframe(1f, 3f, 0f, 0f)),
                new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 3f, float.NegativeInfinity, 0f)),
                new AnimationCurve(new Keyframe(0f, 1f, 0f, 2f), new Keyframe(5e-5f, 3f, 4f, 0f), new Keyframe(1f, 0f)),
                new AnimationCurve(new Keyframe(0f, 1f, 0f, 3e38f), new Keyframe(1f / 30f, 3f, 0f, 0f), new Keyframe(1f, 0f)),
            })
            {
                var clipCurve = Of(curve);
                // Times on the stepped or very short first segment and on the last key.
                foreach (float time in new[] { 0f, 2e-5f, curve.length == 2 ? 0.5f : 4e-5f, 1f })
                    Assert.Equal(curve.Evaluate(time), clipCurve.Evaluate(time));
            }
        }

        [Fact]
        public void CoincidentKeysAndSingleKeysStayFinite()
        {
            var coincident = Of(new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(0.5f, 1f, 5f, 5f),
                new Keyframe(0.5f, 2f, -5f, -5f), new Keyframe(1f, 0f)));
            for (int i = 0; i <= 20; i++) Assert.True(float.IsFinite(coincident.Evaluate(i / 20f)));
            Assert.Equal(2f, coincident.Evaluate(0.5f));
            Assert.Equal(4f, Of(new AnimationCurve(new Keyframe(0.3f, 4f))).Evaluate(0.7f));
        }

        [Fact]
        public void AReplacedCurveIsSampledFromItsOwnKeys()
        {
            var clipCurve = Of(AnimationCurve.Linear(0f, 0f, 1f, 1f));
            Assert.Equal(0.5f, clipCurve.Evaluate(0.5f), 1e-6f);
            clipCurve.Curve = AnimationCurve.Linear(0f, 2f, 1f, 4f);
            Assert.Equal(3f, clipCurve.Evaluate(0.5f), 1e-6f);
        }
    }
}
