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
            // A command for no pass (a shader without forward passes) counts nothing.
            RenderStats.NextDrawPasses(0);
            GfxStream.Draw(1, 0, 9, GfxStream.ForwardPasses, Matrix4x4.identity, Color.white);
            GfxStream.Flush();
            ProfilerCore.EndFrame();
            Assert.Equal(0, draws.LastValue);
        }

        sealed class Collect : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
            public void LogException(System.Exception exception, Object context) => Lines.Add(exception.Message);
        }

        /// <summary>Every pass of a shader under a render pipeline: the renderer's pass list counts,
        /// read without the public passCount (whose SubShader selection under a pipeline is
        /// reported as unmeasured) and without a log line.</summary>
        [Fact]
        public void AllPassesUnderARenderPipelineCountTheRenderersPassesQuietly()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/RenderStats/TwoPass\" { SubShader { Pass { } Pass { } } }");
            var material = new Material(shader);
            var pipeline = Shader.globalRenderPipeline;
            var handler = Debug.unityLogger.logHandler;
            var collect = new Collect();
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            try
            {
                ShaderPassInfo.ResetForTests();
                ShaderPassInfo.s_CallForTests = name => System.Text.Json.JsonSerializer.Serialize(new
                {
                    shader = name, found = true, tags = new System.Collections.Generic.Dictionary<string, string>(),
                    passes = new[] { new { name = "A", tags = new System.Collections.Generic.Dictionary<string, string>() },
                        new { name = "B", tags = new System.Collections.Generic.Dictionary<string, string>() } },
                });
                Shader.globalRenderPipeline = "UniversalPipeline";
                Debug.unityLogger.logHandler = collect;
                ProfilerCore.EndFrame();
                RenderStats.NextDrawAllPasses(material);
                GfxStream.Draw(1, 0, 9, GfxStream.AllPasses, Matrix4x4.identity, Color.white);
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                Assert.Equal(2, draws.LastValue);
                Assert.Empty(collect.Lines);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                Shader.globalRenderPipeline = pipeline;
                ShaderPassInfo.ResetForTests();
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }
    }
}
