using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Tests that set the project's release (ProjectInfo.UnityVersion), which every
    /// Compat answer reads: they run alone.</summary>
    [CollectionDefinition("Project release", DisableParallelization = true)]
    public class ProjectReleaseCollection { }

    /// <summary>The rendering rules measured on exactly 6000.0.71f1 hang off one release flag;
    /// every other release, including other 6000.0 patches, takes the unmeasured answer listed in
    /// docs/COMPATIBILITY_GAPS.md.</summary>
    [Collection("Project release")]
    public class CompatRenderingTests
    {
        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.70f1", false)]
        [InlineData("6000.1.2f1", false)]
        [InlineData("2022.3.10f1", false)]
        [InlineData("not a version", false)]
        public void MeasuredRenderingRulesApplyToTheMeasuredReleaseOnly(string release, bool measured)
        {
            string version = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = release;
                Assert.Equal(measured, Compat.RenderingMeasured);
                Assert.Equal(measured ? 2 : 0, Compat.GraphicsFenceFrames);
                Assert.Equal(measured, Compat.ZeroIndexedInstancesDrawOne);
                Assert.Equal(measured, Compat.SkyAmbientWithoutLightColor);
                Assert.Equal(measured, Compat.ShaderKeywordsMeasured);
                Assert.Equal(measured, Compat.SingleSubpassClearFillsOutputs);
                Assert.Equal(measured ? new Vector4(511, 1023, 65536, 0) : Vector4.zero, Compat.MeshInstanceBatchLimits);
            }
            finally { ProjectInfo.UnityVersion = version; }
        }

        /// <summary>The parsed release follows a change of the project's version string.</summary>
        [Fact]
        public void TheParsedReleaseFollowsTheVersionString()
        {
            string version = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Assert.Equal((6000, 0, 71, 'f', 1), Compat.Release);
                ProjectInfo.UnityVersion = "2022.3.10b2";
                Assert.Equal((2022, 3, 10, 'b', 2), Compat.Release);
                ProjectInfo.UnityVersion = "6000.0";
                Assert.Null(Compat.Release);
                ProjectInfo.UnityVersion = null;
                Assert.Null(Compat.Release);
            }
            finally { ProjectInfo.UnityVersion = version; }
        }
    }
}
