using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Which canvas renderers the canvas leaves out for transparency, as the Unity 6000.0.71f1 Editor
    /// draws them (tests/projects/uitransparentcull): a renderer whose alpha, its own times its
    /// CanvasGroups', is zero or less is never drawn, with or without cullTransparentMesh; the flag
    /// adds the test for a mesh whose vertices are all transparent. Asserted on the draws the canvas
    /// collects.
    /// </summary>
    public class CanvasTransparentCullTests : IDisposable
    {
        static int s_Made;
        readonly List<Object> m_Owned = new List<Object>();
        readonly Dictionary<Shader, string> m_Names = new Dictionary<Shader, string>();
        readonly Canvas m_Canvas;

        public CanvasTransparentCullTests()
        {
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.ResetForTests();
            var go = Own(new GameObject("cull canvas", typeof(RectTransform)));
            m_Canvas = go.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        public void Dispose()
        {
            for (int i = m_Owned.Count - 1; i >= 0; i--)
                if (m_Owned[i]) Object.DestroyImmediate(m_Owned[i], true);
        }

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        /// <summary>A renderer with a quad whose vertices have alpha byte `vertexAlpha`.</summary>
        CanvasRenderer Renderer(string name, Transform parent, bool cull, byte vertexAlpha = 255)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<CanvasRenderer>();
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 10, 0), new Vector3(10, 10, 0), new Vector3(10, 0, 0) };
            mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, vertexAlpha), 4).ToArray();
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
            renderer.SetMesh(mesh);
            // Each renderer draws with a shader of its own, which names its draws.
            var shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/CanvasCull/S" + (++s_Made) + "\" {\n"
                + "SubShader { Tags { \"Queue\"=\"Transparent\" } Pass { } } }"));
            m_Names[shader] = name;
            renderer.materialCount = 1;
            renderer.SetMaterial(Own(new Material(shader)), 0);
            renderer.cullTransparentMesh = cull;
            return renderer;
        }

        /// <summary>The names of this test's renderers the canvas draws, in drawing order.</summary>
        List<string> Drawn()
        {
            var all = new List<FrameBuilder.DrawItem>();
            CanvasSystem.Instance.CollectOverlayDraws(all);
            return all.Where(d => d.Material != null && m_Names.ContainsKey(d.Material.shader)).Select(d => m_Names[d.Material.shader]).ToList();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ARendererAtAlphaZeroOrLessIsNeverDrawn(bool cull)
        {
            Renderer("zero", m_Canvas.transform, cull).SetAlpha(0f);
            Renderer("negative", m_Canvas.transform, cull).SetAlpha(-0.1f);
            Renderer("tiny", m_Canvas.transform, cull).SetAlpha(1e-7f);
            Assert.Equal(new[] { "tiny" }, Drawn());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ACanvasGroupAtZeroHidesItsRenderersEitherWay(bool cull)
        {
            var hidden = Own(new GameObject("hidden group", typeof(RectTransform)));
            hidden.transform.SetParent(m_Canvas.transform, false);
            hidden.AddComponent<CanvasGroup>().alpha = 0f;
            var faint = Own(new GameObject("faint group", typeof(RectTransform)));
            faint.transform.SetParent(m_Canvas.transform, false);
            faint.AddComponent<CanvasGroup>().alpha = 1e-3f;
            Renderer("in hidden", hidden.transform, cull);
            Renderer("in faint", faint.transform, cull);
            Assert.Equal(new[] { "in faint" }, Drawn());
        }

        [Fact]
        public void OnlyTheFlagLeavesOutAllTransparentVertices()
        {
            Renderer("culled", m_Canvas.transform, cull: true, vertexAlpha: 0);
            Renderer("kept", m_Canvas.transform, cull: false, vertexAlpha: 0);
            Renderer("one byte", m_Canvas.transform, cull: true, vertexAlpha: 1);
            Assert.Equal(new[] { "kept", "one byte" }, Drawn());
        }

        [Fact]
        public void ARendererLeftOutAtAlphaZeroStillHasItsDepth()
        {
            var first = Renderer("first", m_Canvas.transform, cull: false);
            first.SetAlpha(0f);
            var second = Renderer("second", m_Canvas.transform, cull: false);
            Drawn();
            CanvasSystem.UpdateDepths();
            Assert.Equal(first.absoluteDepth + 1, second.absoluteDepth);
            Assert.True(first.absoluteDepth >= 0);
        }
    }
}
