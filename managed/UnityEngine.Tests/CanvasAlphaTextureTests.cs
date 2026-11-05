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
    /// CanvasRenderer.SetAlphaTexture, from the Unity 6000.0 Scripting API: "The Alpha Texture that
    /// will be passed to the Shader under the _AlphaTex property". Asserted on the draws the canvas
    /// collects, not on pixels. Facts named "Unmeasured" guard a choice the documentation does not
    /// make (Clear, a null alpha texture); they are not a claim about Unity.
    /// </summary>
    public class CanvasAlphaTextureTests : IDisposable
    {
        static readonly int s_AlphaTex = Shader.PropertyToID("_AlphaTex");
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        static int s_Made;
        readonly List<Object> m_Owned = new List<Object>();
        readonly Shader m_Shader;
        readonly Material m_Material;
        readonly Canvas m_Canvas;

        public CanvasAlphaTextureTests()
        {
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.ResetForTests();
            // A split-alpha UI shader's properties (the release's UI/DefaultETC1 declares both).
            m_Shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/CanvasAlpha/S" + (++s_Made) + "\" {\n"
                + "Properties { _MainTex (\"Sprite Texture\", 2D) = \"white\" {} _AlphaTex (\"Sprite Alpha Texture\", 2D) = \"white\" {} }\n"
                + "SubShader { Tags { \"Queue\"=\"Transparent\" } Pass { } } }"));
            m_Material = Own(new Material(m_Shader));
            var go = Own(new GameObject("alpha canvas", typeof(RectTransform)));
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

        Texture2D Texture(string name)
        {
            var texture = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name });
            texture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
            texture.Apply();
            return texture;
        }

        CanvasRenderer Renderer(string name, Texture main)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(m_Canvas.transform, false);
            var renderer = go.GetComponent<CanvasRenderer>();
            Fill(renderer, main);
            return renderer;
        }

        /// <summary>What a graphic gives its renderer: a quad, one material, the texture.</summary>
        void Fill(CanvasRenderer renderer, Texture main)
        {
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 10, 0), new Vector3(10, 10, 0), new Vector3(10, 0, 0) };
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
            renderer.SetMesh(mesh);
            renderer.materialCount = 1;
            renderer.SetMaterial(m_Material, 0);
            renderer.SetTexture(main);
        }

        /// <summary>This test's canvas draws, in drawing order.</summary>
        List<FrameBuilder.DrawItem> Draws()
        {
            var all = new List<FrameBuilder.DrawItem>();
            CanvasSystem.Instance.CollectOverlayDraws(all);
            return all.Where(d => d.Material != null && d.Material.shader == m_Shader).ToList();
        }

        static object AlphaOf(FrameBuilder.DrawItem draw) =>
            draw.Properties != null && draw.Properties.TryGetValue(s_AlphaTex, out var value) ? value : null;

        [Fact]
        public void TheAlphaTextureReachesTheDrawAsAlphaTex()
        {
            var alpha = Texture("alpha");
            var renderer = Renderer("graphic", Texture("main"));
            renderer.SetAlphaTexture(alpha);
            var draw = Assert.Single(Draws());
            Assert.Same(alpha, AlphaOf(draw));
        }

        [Fact]
        public void APopInstructionDrawCarriesTheAlphaTextureToo()
        {
            // Pop draws carry the renderer's texture as its own draws do; the alpha texture follows it.
            var alpha = Texture("alpha");
            var renderer = Renderer("mask", Texture("main"));
            renderer.popMaterialCount = 1;
            renderer.SetPopMaterial(m_Material, 0);
            renderer.SetAlphaTexture(alpha);
            var draws = Draws();
            Assert.Equal(2, draws.Count);
            Assert.All(draws, d => Assert.Same(alpha, AlphaOf(d)));
        }

        [Fact]
        public void Unmeasured_ANullAlphaTexturePassesNone()
        {
            var alpha = Texture("alpha");
            var renderer = Renderer("graphic", Texture("main"));
            m_Material.SetTexture("_AlphaTex", Texture("material alpha"));
            renderer.SetAlphaTexture(alpha);
            Assert.Same(alpha, AlphaOf(Assert.Single(Draws())));
            renderer.SetAlphaTexture(null);
            // Nothing over the material's: its own _AlphaTex (else the shader's default) is sampled.
            var draw = Assert.Single(Draws());
            Assert.False(draw.Properties != null && draw.Properties.ContainsKey(s_AlphaTex));
        }

        [Fact]
        public void Unmeasured_ClearDropsTheAlphaTexture()
        {
            // The API says only "Remove all cached vertices"; Unbound's Clear also drops the
            // texture, and the alpha texture with it, until the graphic sets them again.
            var main = Texture("main");
            var renderer = Renderer("graphic", main);
            renderer.SetAlphaTexture(Texture("alpha"));
            renderer.Clear();
            Assert.Null(renderer.m_AlphaTexture);
            Fill(renderer, main);
            var draw = Assert.Single(Draws());
            Assert.Null(AlphaOf(draw));
        }

        [Fact]
        public void RenderersWithDifferentAlphaTexturesAreNotDrawnTogether()
        {
            var main = Texture("main");
            var a = Texture("alpha a");
            var b = Texture("alpha b");
            Renderer("first", main).SetAlphaTexture(a);
            Renderer("second", main).SetAlphaTexture(b);
            var draws = Draws();
            Assert.Equal(2, draws.Count);
            Assert.Same(a, AlphaOf(draws[0]));
            Assert.Same(b, AlphaOf(draws[1]));
        }

        [Fact]
        public void RenderersWithTheSameAlphaTextureDrawAsBefore()
        {
            // One draw per renderer and material slot, as without an alpha texture.
            var main = Texture("main");
            var alpha = Texture("alpha");
            Renderer("first", main).SetAlphaTexture(alpha);
            Renderer("second", main).SetAlphaTexture(alpha);
            var draws = Draws();
            Assert.Equal(2, draws.Count);
            Assert.All(draws, d => Assert.Same(alpha, AlphaOf(d)));
            Assert.Equal(draws[0].MainTexture, draws[1].MainTexture);
        }

        [Fact]
        public void SetTextureStillGivesTheMainTexture()
        {
            // Guard: the renderer's texture stays the draw's main texture, not a property, with or
            // without an alpha texture.
            var main = Texture("main");
            var renderer = Renderer("graphic", main);
            ulong handle = TextureHandles.Of(main);
            Assert.NotEqual(0UL, handle);
            var plain = Assert.Single(Draws());
            Assert.Equal(handle, plain.MainTexture);
            Assert.False(plain.Properties != null && plain.Properties.ContainsKey(s_MainTex));
            renderer.SetAlphaTexture(Texture("alpha"));
            var split = Assert.Single(Draws());
            Assert.Equal(handle, split.MainTexture);
            Assert.False(split.Properties.ContainsKey(s_MainTex));
        }
    }
}
