using Unbound.Runtime.Humanoid;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A mirrored humanoid clip's translation DoF curves: the sides swap and the x flips
    /// (checked against the Editor by tests/projects/humanoid tdof.TDoFMirror).</summary>
    public class HumanTranslationDoFMirrorTests
    {
        static int Mirror(string curve, out bool negated)
        {
            int j = HumanClipImport.MirrorCurve[HumanClipImport.CurveIndex(curve)];
            negated = j < 0;
            return negated ? ~j : j;
        }

        [Theory]
        [InlineData("SpineTDOF.x", "SpineTDOF.x", true)]
        [InlineData("SpineTDOF.y", "SpineTDOF.y", false)]
        [InlineData("HeadTDOF.z", "HeadTDOF.z", false)]
        [InlineData("LeftUpperLegTDOF.x", "RightUpperLegTDOF.x", true)]
        [InlineData("RightToesTDOF.y", "LeftToesTDOF.y", false)]
        [InlineData("LeftShoulderTDOF.z", "RightShoulderTDOF.z", false)]
        [InlineData("RightHandTDOF.x", "LeftHandTDOF.x", true)]
        [InlineData("LeftLowerArmTDOF.y", "RightLowerArmTDOF.y", false)]
        public void SidesSwapAndTheXFlips(string curve, string mirrored, bool negated)
        {
            Assert.Equal(HumanClipImport.CurveIndex(mirrored), Mirror(curve, out bool isNegated));
            Assert.Equal(negated, isNegated);
        }

        [Fact]
        public void EveryTranslationDoFCurveMirrorsBack()
        {
            for (int i = HumanClipImport.TDoFFirst; i < HumanClipImport.CurveCount; i++)
            {
                int j = HumanClipImport.MirrorCurve[i];
                int back = HumanClipImport.MirrorCurve[j < 0 ? ~j : j];
                Assert.Equal(i, back < 0 ? ~back : back);
            }
        }
    }
}
