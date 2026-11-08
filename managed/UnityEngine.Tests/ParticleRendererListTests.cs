using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A renderer list builds a particle system's geometry only when it keeps the draw: a
    /// list whose queue range or LightModes leave the material out (an opaque or depth list for a
    /// transparent system) builds none, while the system still requests the streams its renderer
    /// asks for; the list that keeps the draw builds and draws it.</summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleRendererListTests : IDisposable
    {
        readonly List<Object> m_Objects = new List<Object>();
        readonly bool m_Playing = EditorSession.IsPlaying;

        public ParticleRendererListTests()
        {
            EditorSession.IsPlaying = true;
            ShaderPassInfo.ResetForTests();
            // The test shader's one pass, as the renderer lists it.
            ShaderPassInfo.s_CallForTests = name => JsonSerializer.Serialize(new
            {
                shader = name, found = true, tags = new Dictionary<string, string>(),
                passes = new[] { new { name = "Unlit", tags = new Dictionary<string, string> { ["LightMode"] = "SRPDefaultUnlit" } } },
            });
        }

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            ShaderPassInfo.ResetForTests();
            EditorSession.IsPlaying = m_Playing;
        }

        (ParticleSystem, ParticleSystemRenderer, Camera) Make(int queue)
        {
            var shader = ShaderUtil.CreateShaderAsset(
                "Shader \"Tests/ParticleList\" { SubShader { Pass { Tags { \"LightMode\"=\"SRPDefaultUnlit\" } } } }");
            m_Objects.Add(shader);
            var material = new Material(shader) { renderQueue = queue };
            m_Objects.Add(material);
            var go = new GameObject("particles");
            go.SetActive(false);
            m_Objects.Add(go);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 5f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.NoiseSumXYZ,
            });
            go.SetActive(true);
            ps.Play();
            ps.Emit(3);
            var cameraObject = new GameObject("camera");
            m_Objects.Add(cameraObject);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            return (ps, renderer, cameraObject.AddComponent<Camera>());
        }

        static int Draw(Camera camera, Renderer renderer, RenderQueueRange range, string lightMode)
        {
            var data = new CullingData { Camera = camera };
            data.Visible.Add(renderer);
            var tags = new List<ShaderTagId> { new ShaderTagId(lightMode) };
            var drawing = new DrawingSettings(tags[0], new SortingSettings(camera));
            var filtering = new FilteringSettings(range);
            int before = FrameBuilder.s_DrawCount;
            FrameBuilder.DrawRendererList(data, drawing, filtering, tags);
            return FrameBuilder.s_DrawCount - before;
        }

        [Fact]
        public void AListThatLeavesTheMaterialOutBuildsNoGeometry()
        {
            var (ps, renderer, camera) = Make(3000);
            Assert.Equal(0, Draw(camera, renderer, RenderQueueRange.opaque, "SRPDefaultUnlit"));
            Assert.Equal(0, renderer.m_Geometry.VertexCount);
            Assert.True(ps.m_NoiseSumEnabled);
            // A list in the queue range without the material's LightMode leaves it out too.
            Assert.Equal(0, Draw(camera, renderer, RenderQueueRange.transparent, "DepthOnly"));
            Assert.Equal(0, renderer.m_Geometry.VertexCount);

            Assert.Equal(1, Draw(camera, renderer, RenderQueueRange.transparent, "SRPDefaultUnlit"));
            Assert.Equal(12, renderer.m_Geometry.VertexCount);
        }

        [Fact]
        public void OutsideARendererListTheGeometryIsBuilt()
        {
            var (_, renderer, camera) = Make(3000);
            var draws = new List<FrameBuilder.DrawItem>();
            renderer.CollectDraws(new FrameBuilder.CameraContext { Camera = camera, Position = camera.transform.position, Forward = Vector3.forward }, draws);
            Assert.Single(draws);
            Assert.Equal(12, renderer.m_Geometry.VertexCount);
        }
    }
}
