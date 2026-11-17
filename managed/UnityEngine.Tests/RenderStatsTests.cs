using Unbound.Runtime;
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
            // An instancing call of three instances through the stream (each instance a draw command
            // after its DrawSetup) is one draw call; its forward-add draws for a light, one per
            // instance likewise, another.
            GfxStream.Flush();
            for (uint i = 0; i < 3; i++)
            {
                GfxStream.DrawSetup(new DrawSetup { Instances = 1, MeshInstance = i, MeshInstanceCount = 3 });
                GfxStream.Draw(1, 0, 9, 0, Matrix4x4.identity, Color.white);
            }
            for (uint i = 0; i < 3; i++)
            {
                GfxStream.DrawSetup(new DrawSetup { Instances = 1, MeshInstance = i, MeshInstanceCount = 3 });
                GfxStream.Draw(1, 0, 9, 1, Matrix4x4.identity, Color.white);
            }
            GfxStream.Flush();
            ProfilerCore.EndFrame();
            Assert.Equal(2, draws.LastValue);
            Assert.Equal(2, batches.LastValue);
            Assert.Equal(2, setPasses.LastValue);
            // A command for a two-pass shader's forward passes is two draws and two SetPass calls,
            // and the same material's next single pass is a SetPass call again.
            RenderStats.NextDrawPasses(2);
            GfxStream.Draw(1, 0, 9, GfxStream.ForwardPasses, Matrix4x4.identity, Color.white);
            GfxStream.Draw(1, 0, 9, 0, Matrix4x4.identity, Color.white);
            GfxStream.Flush();
            ProfilerCore.EndFrame();
            Assert.Equal(3, draws.LastValue);
            Assert.Equal(3, setPasses.LastValue);
        }
    }
}
