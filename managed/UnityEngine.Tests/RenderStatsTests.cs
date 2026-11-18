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
            uint call = RenderStats.NewInstancingCall();
            for (uint i = 0; i < 3; i++)
            {
                GfxStream.DrawSetup(new DrawSetup { Instances = 1, MeshInstance = i, MeshInstanceCount = 3, MeshInstanceCall = call });
                GfxStream.Draw(1, 0, 9, 0, Matrix4x4.identity, Color.white);
            }
            for (uint i = 0; i < 3; i++)
            {
                GfxStream.DrawSetup(new DrawSetup { Instances = 1, MeshInstance = i, MeshInstanceCount = 3, MeshInstanceCall = call });
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

        static void Instance(uint call, uint index, int pass, Light light = null)
        {
            if (light != null) RenderStats.NextDrawLight(light);
            GfxStream.DrawSetup(new DrawSetup { Instances = 1, MeshInstance = index, MeshInstanceCount = 6, MeshInstanceCall = call });
            GfxStream.Draw(1, 0, 9, pass, Matrix4x4.identity, Color.white);
        }

        /// <summary>A run of one instancing call's instances with one material, pass and light
        /// counts once, in the order the forward renderer draws them (each instance's base draw,
        /// then its forward-add draw for each light reaching it), across other draws; a camera
        /// rendering the call again (its instances from the first) counts it again, and an
        /// instance without a call counts on its own.</summary>
        [Fact]
        public void AnInstancingCallsRunCountsOncePerPassAndLight()
        {
            var lightA = new GameObject("light a").AddComponent<Light>();
            var lightB = new GameObject("light b").AddComponent<Light>();
            try
            {
                using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                // Six instances, light A reaching instances 2-5 and light B instances 4-5, drawn
                // instance by instance: a base run and one run per light, three draws.
                uint call = RenderStats.NewInstancingCall();
                for (uint i = 0; i < 6; i++)
                {
                    Instance(call, i, 0);
                    if (i >= 2) Instance(call, i, 1, lightA);
                    if (i >= 4) Instance(call, i, 1, lightB);
                }
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                Assert.Equal(3, draws.LastValue);
                // Another draw between its instances, then a second camera's render of the call:
                // the other draw and the call's base run again, three.
                call = RenderStats.NewInstancingCall();
                Instance(call, 0, 0);
                GfxStream.Draw(1, 0, 9, 0, Matrix4x4.identity, Color.white);
                Instance(call, 1, 0);
                Instance(call, 0, 0);
                Instance(call, 1, 0);
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                Assert.Equal(3, draws.LastValue);
                // Two calls one after the other, and instances without a call: two and two.
                for (int c = 0; c < 2; c++)
                {
                    call = RenderStats.NewInstancingCall();
                    for (uint i = 0; i < 2; i++) Instance(call, i, 0);
                }
                for (uint i = 0; i < 2; i++) Instance(0, i, 0);
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                Assert.Equal(4, draws.LastValue);
            }
            finally
            {
                Object.DestroyImmediate(lightA.gameObject);
                Object.DestroyImmediate(lightB.gameObject);
            }
        }

        /// <summary>Every run of a frame is kept, however many lights reach a call (a table of
        /// sixteen runs counted a call lit by more than fifteen lights again and again), and a
        /// camera render starts runs of its own even where it draws only a later instance.</summary>
        [Fact]
        public void EveryLightsRunCountsOnceAndEachRenderHasItsOwnRuns()
        {
            var lights = new Light[20];
            for (int l = 0; l < lights.Length; l++) lights[l] = new GameObject("light " + l).AddComponent<Light>();
            try
            {
                using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                // Three instances, each drawn with its base pass and a forward-add pass per light.
                uint call = RenderStats.NewInstancingCall();
                for (uint i = 0; i < 3; i++)
                {
                    Instance(call, i, 0);
                    foreach (var light in lights) Instance(call, i, 1, light);
                }
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                Assert.Equal(1 + lights.Length, draws.LastValue);
                // A render of instances 0 and 1, then another render of instance 2 alone: two runs.
                call = RenderStats.NewInstancingCall();
                RenderStats.BeginRender();
                Instance(call, 0, 0);
                Instance(call, 1, 0);
                RenderStats.BeginRender();
                Instance(call, 2, 0);
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                Assert.Equal(2, draws.LastValue);
                // Control: within one render the later instance continues the run.
                call = RenderStats.NewInstancingCall();
                RenderStats.BeginRender();
                Instance(call, 0, 0);
                Instance(call, 1, 0);
                Instance(call, 2, 0);
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                Assert.Equal(1, draws.LastValue);
            }
            finally
            {
                foreach (var light in lights) Object.DestroyImmediate(light.gameObject);
            }
        }

        /// <summary>Through a camera's forward render: an instancing call of three instances
        /// reached by twenty pixel lights counts its base run and one run per light's forward-add
        /// pass (FrameBuilder's NextDrawLight before each light's draw).</summary>
        [Fact]
        public void ACameraCountsAnInstancingCallsRunPerForwardAddLight()
        {
            FrameBuilder.EnsureRegistered();
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset(
                "Shader \"Tests/RenderStats/Lit\" { SubShader { Pass { Tags { \"LightMode\"=\"ForwardBase\" } } Pass { Tags { \"LightMode\"=\"ForwardAdd\" } } } }");
            var material = new Material(shader) { enableInstancing = true };
            var mesh = new Mesh { vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f) }, triangles = new[] { 0, 1, 2 } };
            mesh.RecalculateBounds();
            var lights = new Light[20];
            for (int l = 0; l < lights.Length; l++)
            {
                lights[l] = new GameObject("point " + l).AddComponent<Light>();
                lights[l].type = LightType.Point;
                lights[l].range = 20f;
                lights[l].transform.position = new Vector3(0.1f * l - 1f, 0.5f, -1f);
            }
            var go = new GameObject("render stats camera");
            go.transform.position = new Vector3(0f, 0f, -5f);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            var target = new RenderTexture(8, 8, 16);
            camera.targetTexture = target;
            int pixelLights = QualitySettings.pixelLightCount;
            try
            {
                ShaderPassInfo.ResetForTests();
                ShaderPassInfo.s_CallForTests = name => System.Text.Json.JsonSerializer.Serialize(new
                {
                    shader = name, found = true, tags = new System.Collections.Generic.Dictionary<string, string>(),
                    passes = new[]
                    {
                        new { name = "BASE", tags = new System.Collections.Generic.Dictionary<string, string> { ["LightMode"] = "ForwardBase" } },
                        new { name = "ADD", tags = new System.Collections.Generic.Dictionary<string, string> { ["LightMode"] = "ForwardAdd" } },
                    },
                });
                QualitySettings.pixelLightCount = lights.Length;
                using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                GfxStream.Flush();
                ProfilerCore.EndFrame();
                var matrices = new[] { Matrix4x4.Translate(new Vector3(-1f, 0f, 0f)), Matrix4x4.identity, Matrix4x4.Translate(new Vector3(1f, 0f, 0f)) };
                long Render()
                {
                    Graphics.RenderMeshInstanced(new RenderParams(material) { camera = camera }, mesh, 0, matrices);
                    camera.Render();
                    GfxStream.Flush();
                    ProfilerCore.EndFrame();
                    return draws.LastValue;
                }
                // The camera's draws without the lights, then one more run per light.
                foreach (var light in lights) light.enabled = false;
                long unlit = Render();
                foreach (var light in lights) light.enabled = true;
                Assert.Equal(unlit + lights.Length, Render());
            }
            finally
            {
                QualitySettings.pixelLightCount = pixelLights;
                ShaderPassInfo.ResetForTests();
                foreach (var light in lights) Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(shader);
            }
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
