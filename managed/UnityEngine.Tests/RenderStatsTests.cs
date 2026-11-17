using Unbound.Runtime.Profiling;
using Unity.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The render counters scripts read through ProfilerRecorder count the last frame's
    /// draw commands (tests/projects/renderstats).</summary>
    [Collection("Frames")]
    public class RenderStatsTests
    {
        [Fact]
        public void RecordersReadTheLastFramesDrawsAndMaterialChanges()
        {
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPasses = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            Assert.True(draws.Valid && batches.Valid && setPasses.Valid);
            ProfilerCore.EndFrame();
            // Material 7 pass 0 twice, then pass 1, then material 9: four draws, three SetPass calls.
            RenderStats.Draw(7, 0);
            RenderStats.Draw(7, 0);
            RenderStats.Draw(7, 1);
            RenderStats.Draw(9, 1);
            ProfilerCore.EndFrame();
            Assert.Equal(4, draws.LastValue);
            Assert.Equal(4, batches.LastValue);
            Assert.Equal(3, setPasses.LastValue);
            // A frame without draws reads 0, and the first draw of a frame is a SetPass call even
            // with the last frame's material.
            ProfilerCore.EndFrame();
            Assert.Equal(0, draws.LastValue);
            RenderStats.Draw(9, 1);
            ProfilerCore.EndFrame();
            Assert.Equal(1, setPasses.LastValue);
        }
    }
}
