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
    /// collects, not on pixels; what the documentation leaves open (Clear, a null alpha texture,
    /// batching) is as the Unity 6000.0.71f1 Editor draws it (tests/projects/uialphaborders canvas).
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

        Texture2D MakeTexture(string name)
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
            var alpha = MakeTexture("alpha");
            var renderer = Renderer("graphic", MakeTexture("main"));
            renderer.SetAlphaTexture(alpha);
            var draw = Assert.Single(Draws());
            Assert.Same(alpha, AlphaOf(draw));
        }

        [Fact]
        public void APopInstructionDrawCarriesTheAlphaTextureToo()
        {
            // Pop draws carry the renderer's texture as its own draws do; the alpha texture follows it.
            var alpha = MakeTexture("alpha");
            var renderer = Renderer("mask", MakeTexture("main"));
            renderer.popMaterialCount = 1;
            renderer.SetPopMaterial(m_Material, 0);
            renderer.SetAlphaTexture(alpha);
            var draws = Draws();
            Assert.Equal(2, draws.Count);
            Assert.All(draws, d => Assert.Same(alpha, AlphaOf(d)));
        }

        [Fact]
        public void ANullAlphaTexturePassesNone()
        {
            var alpha = MakeTexture("alpha");
            var renderer = Renderer("graphic", MakeTexture("main"));
            m_Material.SetTexture("_AlphaTex", MakeTexture("material alpha"));
            renderer.SetAlphaTexture(alpha);
            Assert.Same(alpha, AlphaOf(Assert.Single(Draws())));
            renderer.SetAlphaTexture(null);
            // Nothing over the material's: its own _AlphaTex (else the shader's default) is sampled.
            var draw = Assert.Single(Draws());
            Assert.False(draw.Properties != null && draw.Properties.ContainsKey(s_AlphaTex));
        }

        [Fact]
        public void ClearDropsTheAlphaTexture()
        {
            // The API says only "Remove all cached vertices"; Clear also drops the texture, and the
            // alpha texture with it, until they are set again (uialphaborders canvas etc1.cleared).
            var main = MakeTexture("main");
            var renderer = Renderer("graphic", main);
            renderer.SetAlphaTexture(MakeTexture("alpha"));
            renderer.Clear();
            Assert.Null(renderer.m_AlphaTexture);
            Fill(renderer, main);
            var draw = Assert.Single(Draws());
            Assert.Null(AlphaOf(draw));
        }

        /// <summary>Renderers drawn one after another with one material and texture are one batch,
        /// drawn with the first one's alpha texture (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/uialphaborders canvas shared.half and shared2.none).</summary>
        [Fact]
        public void AdjacentRenderersOfOneMaterialAndTextureTakeTheFirstAlphaTexture()
        {
            var main = MakeTexture("main");
            var a = MakeTexture("alpha a");
            Renderer("first", main).SetAlphaTexture(a);
            Renderer("second", main).SetAlphaTexture(MakeTexture("alpha b"));
            Renderer("third", main);
            var draws = Draws();
            Assert.Equal(3, draws.Count);
            Assert.All(draws, d => Assert.Same(a, AlphaOf(d)));
        }

        /// <summary>Control: a renderer with other rect clipping keeps its own alpha texture
        /// (renderers under different clip rects are not measured).</summary>
        [Fact]
        public void OtherRectClippingStartsARunWithItsOwnAlphaTexture()
        {
            var main = MakeTexture("main");
            var a = MakeTexture("alpha a");
            var b = MakeTexture("alpha b");
            Renderer("first", main).SetAlphaTexture(a);
            var clipped = Renderer("second", main);
            clipped.SetAlphaTexture(b);
            clipped.EnableRectClipping(new Rect(0f, 0f, 5f, 5f));
            var draws = Draws();
            Assert.Equal(2, draws.Count);
            Assert.Same(a, AlphaOf(draws[0]));
            Assert.Same(b, AlphaOf(draws[1]));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnotherClipRectOrSoftnessStartsARunWithItsOwnAlphaTexture(bool softness)
        {
            var main = MakeTexture("main");
            var a = MakeTexture("alpha a");
            var b = MakeTexture("alpha b");
            var first = Renderer("first", main);
            first.SetAlphaTexture(a);
            first.EnableRectClipping(new Rect(0f, 0f, 5f, 5f));
            var second = Renderer("second", main);
            second.SetAlphaTexture(b);
            second.EnableRectClipping(softness ? new Rect(0f, 0f, 5f, 5f) : new Rect(1f, 0f, 5f, 5f));
            if (softness) second.clippingSoftness = new Vector2(2f, 2f);
            var draws = Draws();
            Assert.Equal(2, draws.Count);
            Assert.Same(a, AlphaOf(draws[0]));
            Assert.Same(b, AlphaOf(draws[1]));
        }

        /// <summary>Control: another texture starts a batch of its own with its own alpha texture.</summary>
        [Fact]
        public void AnotherTextureStartsABatchWithItsOwnAlphaTexture()
        {
            var a = MakeTexture("alpha a");
            var b = MakeTexture("alpha b");
            Renderer("first", MakeTexture("main a")).SetAlphaTexture(a);
            Renderer("second", MakeTexture("main b")).SetAlphaTexture(b);
            var draws = Draws();
            Assert.Equal(2, draws.Count);
            Assert.Same(a, AlphaOf(draws[0]));
            Assert.Same(b, AlphaOf(draws[1]));
        }

        [Fact]
        public void RenderersWithTheSameAlphaTextureDrawAsBefore()
        {
            // One draw per renderer and material slot, as without an alpha texture.
            var main = MakeTexture("main");
            var alpha = MakeTexture("alpha");
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
            var main = MakeTexture("main");
            var renderer = Renderer("graphic", main);
            ulong handle = TextureHandles.Of(main);
            Assert.NotEqual(0UL, handle);
            var plain = Assert.Single(Draws());
            Assert.Equal(handle, plain.MainTexture);
            Assert.False(plain.Properties != null && plain.Properties.ContainsKey(s_MainTex));
            renderer.SetAlphaTexture(MakeTexture("alpha"));
            var split = Assert.Single(Draws());
            Assert.Equal(handle, split.MainTexture);
            Assert.DoesNotContain(s_MainTex, split.Properties.Keys);
        }

        /// <summary>A draw of another material between two renderers of one material and texture
        /// does not end their batch: the second still takes the first's alpha texture (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/uialphaborders batches apart.* and over.*).</summary>
        [Fact]
        public void AnotherDrawBetweenKeepsTheFirstAlphaTexture()
        {
            var main = MakeTexture("main");
            var a = MakeTexture("alpha a");
            var other = MakeTexture("alpha other");
            Renderer("first", main).SetAlphaTexture(a);
            var between = Renderer("between", main);
            between.SetMaterial(Own(new Material(m_Shader)), 0);
            between.SetAlphaTexture(other);
            Renderer("third", main).SetAlphaTexture(MakeTexture("alpha b"));
            var draws = Draws();
            Assert.Equal(3, draws.Count);
            Assert.Same(a, AlphaOf(draws[0]));
            Assert.Same(other, AlphaOf(draws[1]));
            Assert.Same(a, AlphaOf(draws[2]));
        }

        /// <summary>Canvas.GetETC1SupportedCanvasMaterial (tests/projects/uialphaborders etc1Material*).</summary>
        [Fact]
        public void TheEtc1CanvasMaterialIsItsOwn()
        {
            // The test domain has no built-in shaders: stand-ins under the built-in names, removed after.
            Shader standInEtc1 = null, standInDefault = null;
            if (Shader.Find("UI/DefaultETC1") == null)
                Shader.Register(standInEtc1 = ShaderUtil.CreateShaderAsset("Shader \"UI/DefaultETC1\" { SubShader { Pass { } } }"));
            if (Shader.Find("UI/Default") == null)
                Shader.Register(standInDefault = ShaderUtil.CreateShaderAsset("Shader \"UI/Default\" { SubShader { Pass { } } }"));
            try
            {
                var etc1 = Canvas.GetETC1SupportedCanvasMaterial();
                Assert.Equal("ETC1 Supported UI Material", etc1.name);
                Assert.Equal("UI/DefaultETC1", etc1.shader.name);
                Assert.Same(etc1, Canvas.GetETC1SupportedCanvasMaterial());
                Assert.NotSame(Canvas.GetDefaultCanvasMaterial(), etc1);
            }
            finally
            {
                Shader.Unregister(standInEtc1);
                Shader.Unregister(standInDefault);
            }
        }
    }
}
