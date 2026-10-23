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
    }
}
