using System;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class AnimationCurveTests
    {
        [Fact]
        public void LinearCurveClampsByDefault()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Near.Equal(0.5f, c.Evaluate(0.5f));
            Near.Equal(0.25f, c.Evaluate(0.25f));
            Assert.Equal(0f, c.Evaluate(-1f));
            Assert.Equal(1f, c.Evaluate(2f));
            Assert.Equal(WrapMode.ClampForever, c.preWrapMode);
            Assert.Equal(WrapMode.ClampForever, c.postWrapMode);
        }

        [Fact]
        public void EmptyAndSingleKeyCurves()
        {
            Assert.Equal(0f, new AnimationCurve().Evaluate(3f));
            Assert.Equal(7f, new AnimationCurve(new Keyframe(2f, 7f)).Evaluate(-100f));
            Assert.Equal(1, AnimationCurve.Linear(1f, 2f, 1f, 5f).length);
        }

        /// <summary>The curve as a Visual Effect Graph samples it: each segment a cubic in the
        /// time from its first key (Unity 6000.0.71f1 Editor, tests/projects/vfx,
        /// `sim/expressions others/inputs/*/c00`).</summary>
        [Fact]
        public void ACurveSampledAsCubicsRoundsAsTheEditorDoes()
        {
            static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);
            var flat = new AnimationCurve(new Keyframe(-1f, 3f), new Keyframe(0f, -1f), new Keyframe(2f, 5f));
            Assert.Equal(0x3e978d54u, Bits(flat.EvaluateCubic(0.6f)));
            Assert.Equal(0x3e978d52u, Bits(flat.Evaluate(0.6f)));
            // Slopes at the keys, keys no power of two apart; ping-pong before, a loop after.
            var sloped = new AnimationCurve(new Keyframe(-1f, 3f, 0f, -2.5f), new Keyframe(0.7f, -1f, 1.25f, 0.6f), new Keyframe(2f, 5f, 3f, 0f))
            {
                preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.Loop,
            };
            Assert.Equal(0xbd8db290u, Bits(sloped.EvaluateCubic(1.04f)));
            // Past the last key the time wraps, and the cubic's time is still taken from the
            // time given; on the last key it wraps onto the first one.
            Assert.Equal(0xbf332e80u, Bits(sloped.EvaluateCubic(3.87f)));
            Assert.Equal(3f, sloped.EvaluateCubic(2f));
            // Where it ping-pongs: the Hermite terms, the second key's value added last.
            Assert.Equal(0x406c0d24u, Bits(sloped.EvaluateCubic(-3.7f)));
            Assert.Equal(0x406c0d23u, Bits(sloped.Evaluate(-3.7f)));
            // Where it clamps, and on a weighted or stepped segment: Evaluate's.
            Assert.Equal(3f, flat.EvaluateCubic(-5f));
            Assert.Equal(5f, flat.EvaluateCubic(2f));
            Assert.Equal(5f, flat.EvaluateCubic(9f));
            var weighted = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f, 0f, 0.8f) { weightedMode = WeightedMode.Out }, new Keyframe(1f, 1f));
            Assert.Equal(Bits(weighted.Evaluate(0.3f)), Bits(weighted.EvaluateCubic(0.3f)));
            var stepped = new AnimationCurve(new Keyframe(0f, 1f, 0f, float.PositiveInfinity), new Keyframe(1f, 4f), new Keyframe(2f, 6f, float.NegativeInfinity, 0f))
            {
                preWrapMode = WrapMode.PingPong,
            };
            Assert.Equal((1f, 6f, 1f), (stepped.EvaluateCubic(0.5f), stepped.EvaluateCubic(1.5f), stepped.EvaluateCubic(-0.5f)));
            // No keys, one key, two keys at one time, a time that is no number.
            Assert.Equal((0f, 7f), (new AnimationCurve().EvaluateCubic(3f), new AnimationCurve(new Keyframe(2f, 7f)).EvaluateCubic(-100f)));
            var upright = new AnimationCurve(new Keyframe(1f, 2f), new Keyframe(1f, 5f)) { postWrapMode = WrapMode.Loop, preWrapMode = WrapMode.Loop };
            Assert.Equal(Bits(upright.Evaluate(1f)), Bits(upright.EvaluateCubic(1f)));
            Assert.Equal(Bits(sloped.Evaluate(float.NaN)), Bits(sloped.EvaluateCubic(float.NaN)));
            Assert.Equal(Bits(sloped.Evaluate(float.PositiveInfinity)), Bits(sloped.EvaluateCubic(float.PositiveInfinity)));
        }

        /// <summary>Times that are no number or infinite (tests/projects/animedges curvetime.*): NaN on
        /// a curve with two keys or more; a clamped side holds its end value for every finite time
        /// and reads NaN at an infinite one; no key and one key keep their constant answers.</summary>
        [Fact]
        public void TimesOutsideTheRealNumbers()
        {
            var stepped = new AnimationCurve(new Keyframe(0f, .285f, 0f, 0f), new Keyframe(1f, .475f, float.PositiveInfinity, float.PositiveInfinity),
                new Keyframe(2f, .65f, float.PositiveInfinity, float.PositiveInfinity));
            Assert.True(float.IsNaN(stepped.Evaluate(float.NaN)));
            Assert.True(float.IsNaN(stepped.Evaluate(float.PositiveInfinity)));
            Assert.True(float.IsNaN(stepped.Evaluate(float.NegativeInfinity)));
            Assert.Equal((.65f, .285f), (stepped.Evaluate(float.MaxValue), stepped.Evaluate(-float.MaxValue)));
            Assert.Equal((.285f, .475f, .65f), (stepped.Evaluate(.5f), stepped.Evaluate(1.5f), stepped.Evaluate(2f)));
            Assert.Equal(3f, new AnimationCurve(new Keyframe(.5f, 3f)).Evaluate(float.NaN));
            Assert.Equal(0f, new AnimationCurve().Evaluate(float.NegativeInfinity));
            // Clamped before the keys, looping after them.
            var mixed = AnimationCurve.Linear(0f, 2f, 3f, 5f);
            mixed.preWrapMode = WrapMode.ClampForever;
            mixed.postWrapMode = WrapMode.Loop;
            Assert.True(float.IsNaN(mixed.Evaluate(float.NegativeInfinity)));
            Assert.Equal(2f, mixed.Evaluate(-1e30f));
            Assert.Equal(mixed.Evaluate(1f), mixed.Evaluate(4f), 5);
            Assert.True(float.IsNaN(mixed.Evaluate(float.PositiveInfinity)));
            // A side that ping-pongs reads the last key at an infinite time, before the keys too.
            var bounce = new AnimationCurve(new Keyframe(1f, 7f), new Keyframe(2f, 2f), new Keyframe(4f, 5f)) { preWrapMode = WrapMode.PingPong };
            Assert.Equal(5f, bounce.Evaluate(float.NegativeInfinity));
            Assert.True(float.IsNaN(bounce.Evaluate(float.PositiveInfinity)));
            Assert.Equal(7f, bounce.Evaluate(-float.MaxValue));
        }

        [Fact]
        public void HermiteWithTangents()
        {
            Near.Equal(0.15625f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f).Evaluate(0.25f));
            Near.Equal(0.5f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f).Evaluate(0.5f));
            var c = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));
            Near.Equal(0.75f, c.Evaluate(0.5f));
            // Tangents are slopes per unit of time: stretching the segment keeps them meaningful.
            var stretched = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1f), new Keyframe(4f, 4f, 1f, 0f));
            Near.Equal(1f, stretched.Evaluate(1f), 1e-5f);
            Near.Equal(3f, stretched.Evaluate(3f), 1e-5f);
        }

        [Fact]
        public void WeightedTangents()
        {
            var plain = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, -1f, 0f));
            var weighted = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2f, 1f / 3f, 1f / 3f),
                new Keyframe(1f, 1f, -1f, 0f, 1f / 3f, 1f / 3f));
            for (float t = 0f; t <= 1f; t += 0.125f)
                Near.Equal(plain.Evaluate(t), weighted.Evaluate(t), 1e-5f);

            // Zero weights collapse the control points onto the keys: a smooth-step in time with
            // control values at the key values, so the midpoint is the average.
            var flat = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 10f, 0f, 0f),
                new Keyframe(1f, 1f, 10f, 0f, 0f, 0f));
            Near.Equal(0.5f, flat.Evaluate(0.5f), 1e-5f);

            // A heavy out-weight pulls the curve along the out tangent for longer.
            var heavy = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f) { weightedMode = WeightedMode.Out, outWeight = 1f },
                new Keyframe(1f, 1f, 0f, 0f));
            Assert.True(heavy.Evaluate(0.5f) < plain.Evaluate(0.5f));
            Assert.True(heavy.Evaluate(0.5f) < 0.5f);
        }

        [Fact]
        public void SteppedKeys()
        {
            var c = new AnimationCurve(
                new Keyframe(0f, 1f, 0f, float.PositiveInfinity),
                new Keyframe(1f, 5f, float.PositiveInfinity, 0f));
            Assert.Equal(1f, c.Evaluate(0.99f));
            Assert.Equal(5f, c.Evaluate(1f));
        }

        [Fact]
        public void WrapModes()
        {
            AnimationCurve c = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            c.postWrapMode = WrapMode.Loop;
            c.preWrapMode = WrapMode.Loop;
            Near.Equal(0.25f, c.Evaluate(1.25f));
            Near.Equal(0.75f, c.Evaluate(-0.25f));
            c.postWrapMode = WrapMode.PingPong;
            Near.Equal(0.75f, c.Evaluate(1.25f));
            Near.Equal(0.25f, c.Evaluate(2.25f));
            c.preWrapMode = WrapMode.PingPong;
            Near.Equal(0.25f, c.Evaluate(-0.25f));
            c.postWrapMode = WrapMode.Clamp;
            Assert.Equal(WrapMode.ClampForever, c.postWrapMode);
            Assert.Equal(1f, c.Evaluate(10f));

            var offset = AnimationCurve.Linear(2f, 0f, 4f, 2f);
            offset.postWrapMode = WrapMode.Loop;
            Near.Equal(0.5f, offset.Evaluate(4.5f));
        }

        [Fact]
        public void KeyEditing()
        {
            var c = new AnimationCurve();
            Assert.Equal(0, c.AddKey(1f, 10f));
            Assert.Equal(0, c.AddKey(0f, 0f));
            Assert.Equal(2, c.AddKey(new Keyframe(2f, 20f)));
            Assert.Equal(-1, c.AddKey(1f, 99f));
            Assert.Equal(3, c.length);
            Assert.Equal(new[] { 0f, 1f, 2f }, Array.ConvertAll(c.keys, k => k.time));

            // MoveKey onto an occupied time keeps the old time.
            int idx = c.MoveKey(2, new Keyframe(0f, 30f));
            Assert.Equal(2, idx);
            Assert.Equal(2f, c[2].time);
            Assert.Equal(30f, c[2].value);

            idx = c.MoveKey(0, new Keyframe(5f, -1f));
            Assert.Equal(2, idx);
            Assert.Equal(5f, c[2].time);

            c.RemoveKey(0);
            Assert.Equal(2, c.length);
            Assert.Throws<IndexOutOfRangeException>(() => c.RemoveKey(5));
            Assert.Throws<IndexOutOfRangeException>(() => c[2]);

            c.keys = new[] { new Keyframe(3f, 3f), new Keyframe(1f, 1f), new Keyframe(2f, 2f) };
            Assert.Equal(new[] { 1f, 2f, 3f }, Array.ConvertAll(c.keys, k => k.time));
            c.ClearKeys();
            Assert.Equal(0, c.length);
        }

        [Fact]
        public void SmoothTangentsAveragesNeighbourSlopes()
        {
            var c = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f), new Keyframe(3f, 2f));
            c.SmoothTangents(1, 0f);
            Near.Equal(0.75f, c[1].inTangent);
            Near.Equal(0.75f, c[1].outTangent);
            c.SmoothTangents(1, 1f);
            Near.Equal(1f, c[1].outTangent);
        }

        [Fact]
        public void EqualityAndCopy()
        {
            AnimationCurve a = AnimationCurve.Linear(0f, 0f, 1f, 2f);
            AnimationCurve b = AnimationCurve.Linear(0f, 0f, 1f, 2f);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            b.postWrapMode = WrapMode.Loop;
            Assert.False(a.Equals(b));
            a.CopyFrom(b);
            Assert.True(a.Equals(b));
            Near.Equal(1f, AnimationCurve.Constant(0f, 1f, 1f).Evaluate(0.3f));
        }
    }

    public class GradientTests
    {
        [Fact]
        public void DefaultGradientIsWhite()
        {
            var g = new Gradient();
            Assert.Equal(Color.white, g.Evaluate(0.3f));
            Assert.Equal(2, g.colorKeys.Length);
            Assert.Equal(GradientMode.Blend, g.mode);
        }

        // Unity 6000.0.71f1, tests/projects/uitkpainter (gradient-roundtrip, gradient-clear-resets).
        [Fact]
        public void KeyTimesReadBackInSixteenBitStepsAndTheColourSpaceStartsUnset()
        {
            var g = new Gradient();
            Assert.Equal(ColorSpace.Uninitialized, g.colorSpace);
            g.SetKeys(new[] { new GradientColorKey(Color.red, 0.25f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.625f) });
            Assert.Equal(16384f / 65535f, g.colorKeys[0].time);
            Assert.Equal(1f, g.colorKeys[1].time);
            Assert.Equal(40959f / 65535f, g.alphaKeys[1].time);
            // The stored steps are what a time is looked up in: the colour at the key is the key's.
            Assert.Equal(Color.red.r, g.Evaluate(0.25f).r);
        }

        private static Gradient RedToBlue()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.blue, 1f), new GradientColorKey(Color.red, 0f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        [Fact]
        public void BlendInterpolatesLinearly()
        {
            Gradient g = RedToBlue();
            Assert.Equal(0f, g.colorKeys[0].time); // sorted
            Near.Equal(new Color(0.5f, 0f, 0.5f, 0.5f), g.Evaluate(0.5f));
            Near.Equal(new Color(1f, 0f, 0f, 1f), g.Evaluate(-1f));
            Near.Equal(new Color(0f, 0f, 1f, 0f), g.Evaluate(2f));
        }

        [Fact]
        public void FixedTakesTheNextKey()
        {
            var g = new Gradient { mode = GradientMode.Fixed };
            g.colorKeys = new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) };
            Assert.Equal(Color.red, g.Evaluate(0f));
            Assert.Equal(Color.green, g.Evaluate(0.25f));
            Assert.Equal(Color.green, g.Evaluate(0.5f));
            Assert.Equal(Color.blue, g.Evaluate(0.75f));
        }

        [Fact]
        public void PerceptualBlendDiffersFromLinear()
        {
            Gradient g = RedToBlue();
            g.mode = GradientMode.PerceptualBlend;
            Near.Equal(Color.red, g.Evaluate(0f) with { a = 1f }, 1e-4f);
            Near.Equal(new Color(0f, 0f, 1f, 0f), g.Evaluate(1f), 1e-4f);
            Color mid = g.Evaluate(0.5f);
            Assert.True(Math.Abs(mid.r - 0.5f) > 0.01f || Math.Abs(mid.b - 0.5f) > 0.01f);
            Near.Equal(0.5f, mid.a);
        }

        [Fact]
        public void AtMostEightKeys()
        {
            var g = new Gradient();
            var keys = new GradientColorKey[9];
            Assert.Throws<ArgumentException>(() => g.colorKeys = keys);
        }

        [Fact]
        public void Equality()
        {
            Assert.True(RedToBlue().Equals(RedToBlue()));
            Gradient other = RedToBlue();
            other.mode = GradientMode.Fixed;
            Assert.False(RedToBlue().Equals(other));
        }

        /// <summary>Key times are 1/65535 steps: keys 0.5 and 0.500005 share one, so times up to
        /// the step after it blend towards the earlier key and later times start from the later
        /// one (Editor values, tests/projects/lines edge.gradient.closeKeys.Blend).</summary>
        [Theory]
        [InlineData(0.5f, 1.52587891E-05f, 0.999984741f, 0f, 0.2000122f)]
        [InlineData(0.500002f, 1.12056732E-05f, 0.9999888f, 0f, 0.200008929f)]
        [InlineData(0.500006f, 3.21865082E-06f, 0.9999968f, 0f, 0.200002551f)]
        [InlineData(0.50001f, 4.768517E-06f, 4.768517E-06f, 1f, 0.899998069f)]
        [InlineData(0.7f, 0.399990857f, 0.399990857f, 1f, 0.740003645f)]
        public void GradientKeysInOneStepAsTheEditorEvaluatesThem(float time, float r, float g, float b, float a)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 0.500005f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.2f, 0.5f), new GradientAlphaKey(0.9f, 0.500005f), new GradientAlphaKey(0.5f, 1f) });
            var c = gradient.Evaluate(time);
            Assert.InRange(c.r, r - 1e-6f, r + 1e-6f);
            Assert.InRange(c.g, g - 1e-6f, g + 1e-6f);
            Assert.InRange(c.b, b - 1e-6f, b + 1e-6f);
            Assert.InRange(c.a, a - 1e-6f, a + 1e-6f);
        }
    }
}
