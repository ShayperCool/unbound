using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The normalized time a clip reads (ClipSampling.ClipNormalized and
    /// StateClipNormalized); the values against the Editor are checked by tests/projects/humanoid
    /// longState, longStateB and longClipA/B.</summary>
    public class ClipNormalizedTimeTests
    {
        [Fact]
        public void AQuotientBetweenTwoSinglesRoundsUp()
        {
            Assert.Equal(70000.3046875f, ClipSampling.ClipNormalized(70000.3, 1f));
            Assert.Equal(70000.3359375f, ClipSampling.ClipNormalized(70000.333333335, 1f));
            float tenth = ClipSampling.ClipNormalized(0.1, 1f);
            Assert.True(tenth >= 0.1 && MathF.BitDecrement(tenth) < 0.1);
        }

        [Fact]
        public void AnExactQuotientStays()
        {
            Assert.Equal(0.5f, ClipSampling.ClipNormalized(0.25, 0.5f));
            Assert.Equal(3f, ClipSampling.ClipNormalized(3.0, 1f));
            Assert.Equal(0f, ClipSampling.ClipNormalized(0.0, 0.75f));
            Assert.Equal(-0.5f, ClipSampling.ClipNormalized(-0.25, 0.5f));
        }

        [Fact]
        public void AClipWithoutLengthReadsTheTimeItself()
        {
            Assert.Equal(0.3f, ClipSampling.ClipNormalized(0.3, 0f));
            Assert.Equal(1.25, ClipSampling.StateClipNormalized(1.25, 0f));
        }

        /// <summary>A state's time in seconds on a clip of 2/3 s is a single: at 30000 cycles it
        /// may round onto the next normalized single (longStateB).</summary>
        [Fact]
        public void AStateReadsItsTimeInSecondsOnTheClip()
        {
            float length = 2f / 3f;
            Assert.Equal(30000.455078125, ClipSampling.StateClipNormalized(30000.453125, length));
            Assert.Equal(30000.3515625, ClipSampling.StateClipNormalized(30000.3515625, length));
            Assert.Equal(30000.404296875, ClipSampling.StateClipNormalized(30000.40234375, length));
            // A whole cycle stays whole; small times move by one single at most.
            Assert.Equal(2.0, ClipSampling.StateClipNormalized(2.0, length));
            double small = ClipSampling.StateClipNormalized(0.3f, length);
            Assert.True(small == 0.3f || small == MathF.BitIncrement(0.3f));
            Assert.Equal(20000.5f, ClipSampling.StateClipNormalized(20000.5f, 1f));
        }
    }
}
