using Unbound.Runtime.Humanoid;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A Humanoid model clip has no curves of the body parts its body mask turns off; the
    /// others keep their values (6000.0.71f1 Editor, tests/projects/modelclips HumanMask*: the
    /// root's motion curves with Root, the spine's muscles with Body, the head's, a leg's or an
    /// arm's with their part, the fingers with the hand's, a goal's curves with its IK part).</summary>
    public class HumanClipBodyMaskTests
    {
        static (float[][] values, float[][] slopes) Curves()
        {
            var values = new float[HumanClipImport.CurveCount][];
            var slopes = new float[HumanClipImport.CurveCount][];
            for (int c = 0; c < HumanClipImport.CurveCount; c++)
            {
                values[c] = new[] { c * 0.5f, c * 0.25f };
                slopes[c] = new[] { 0f, 0f };
            }
            return (values, slopes);
        }

        static bool Has(float[][] values, string curve) => values[HumanClipImport.CurveIndex(curve)] != null;

        [Fact]
        public void TheCurvesOfThePartsOffAreLeftOut()
        {
            var (values, slopes) = Curves();
            // Head, LeftArm, LeftFingers and LeftHandIK off (HumanMaskBody).
            HumanClipImport.DropPartsOff(values, slopes, 1 << 2 | 1 << 5 | 1 << 7 | 1 << 11, false);
            Assert.False(Has(values, "Neck Nod Down-Up"));
            Assert.False(Has(values, "Jaw Close"));
            Assert.False(Has(values, "Left Arm Down-Up"));
            Assert.False(Has(values, "Left Hand In-Out"));
            Assert.False(Has(values, "LeftHand.Index.1 Stretched"));
            Assert.False(Has(values, "LeftHandT.x"));
            Assert.False(Has(values, "LeftHandQ.w"));
            Assert.True(Has(values, "Right Arm Down-Up"));
            Assert.True(Has(values, "RightHand.Index.1 Stretched"));
            Assert.True(Has(values, "RightHandT.x"));
            Assert.True(Has(values, "Spine Front-Back"));
            Assert.True(Has(values, "RootT.x"));
            int kept = 0;
            for (int c = 0; c < HumanClipImport.HumanEnd; c++)
                if (values[c] != null)
                {
                    kept++;
                    Assert.Equal(c * 0.5f, values[c][0]);
                    Assert.NotNull(slopes[c]);
                }
                else Assert.Null(slopes[c]);
            // 130 curves less the head's 12, the left arm's 9, the left fingers' 20 and the left
            // hand's goal's 7 (82 in the Editor).
            Assert.Equal(82, kept - (HumanClipImport.HumanEnd - 130));
        }

        [Fact]
        public void TheRootAndTheBodyAreParts()
        {
            var (values, slopes) = Curves();
            HumanClipImport.DropPartsOff(values, slopes, 1 << 0, false);
            Assert.False(Has(values, "RootT.x"));
            Assert.False(Has(values, "RootQ.w"));
            Assert.True(Has(values, "Spine Front-Back"));
            Assert.True(Has(values, "LeftFootT.x"));

            (values, slopes) = Curves();
            HumanClipImport.DropPartsOff(values, slopes, 1 << 1 | 1 << 9, false);
            Assert.True(Has(values, "RootT.x"));
            Assert.False(Has(values, "Spine Front-Back"));
            Assert.False(Has(values, "UpperChest Twist Left-Right"));
            Assert.False(Has(values, "LeftFootQ.x"));
            Assert.True(Has(values, "RightFootQ.x"));
            Assert.True(Has(values, "Neck Nod Down-Up"));
        }

        [Fact]
        public void NoPartOffKeepsEveryCurve()
        {
            var (values, slopes) = Curves();
            HumanClipImport.DropPartsOff(values, slopes, 0, true);
            for (int c = 0; c < HumanClipImport.CurveCount; c++) Assert.NotNull(values[c]);
        }
    }
}
