using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A renderer's own color in vertices and per-draw constants, in both color spaces
    /// (measured in the Unity 6000.0.71f1 Editor, tests/projects/vertexcolors).</summary>
    public class VertexColorTests : IDisposable
    {
        readonly List<Object> m_Made = new List<Object>();
        readonly ColorSpace m_ColorSpace = QualitySettings.s_ColorSpace;
        readonly RenderPipelineAsset m_Pipeline = GraphicsSettings.defaultRenderPipeline;

        /// <summary>Channels on both sides of the sRGB knee, outside 0..1, and an alpha below one.</summary>
        static readonly Color Tint = new Color(0.657f, 0.02f, 1.25f, 0.5f);
        static readonly Color Negative = new Color(-0.1f, 0.9f, 0.3f, 1f);

        public void Dispose()
        {
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.ResetForTests();
            QualitySettings.s_ColorSpace = m_ColorSpace;
            GraphicsSettings.defaultRenderPipeline = m_Pipeline;
            foreach (var o in m_Made) if (o) Object.DestroyImmediate(o);
        }

        T Own<T>(T o) where T : Object
        {
            m_Made.Add(o);
            return o;
        }

        Material Material() => Own(new Material(Shader.Create("Probe/Tint", 3000)));

        Sprite WhiteSprite(float pixelsPerUnit = 4f)
        {
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            return Own(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), pixelsPerUnit));
        }

        Camera Camera()
        {
            var eye = Own(new GameObject("eye"));
            eye.transform.position = new Vector3(0f, 0f, -10f);
            var camera = eye.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            return camera;
        }

        static List<FrameBuilder.DrawItem> Draws(Renderer renderer, Camera camera)
        {
            var draws = new List<FrameBuilder.DrawItem>();
            renderer.CollectDraws(FrameBuilder.ContextOf(camera), draws);
            return draws;
        }

        static void Near(Color expected, Color actual)
        {
            for (int i = 0; i < 4; i++)
                Assert.True(Mathf.Abs(expected[i] - actual[i]) < 1e-6f, $"{expected} vs {actual}");
        }

        [Fact]
        public void VertexBytesClampEachChannelAndRoundHalfUp()
        {
            Assert.Equal(new Color32(168, 5, 255, 128), VertexColors.Bytes(Tint));
            Assert.Equal(new Color32(0, 230, 77, 255), VertexColors.Bytes(Negative));
            // 0.7 * 255 is 178.5 in single precision: up, where Color32's conversion rounds to even.
            Assert.Equal(179, VertexColors.Byte(0.7f));
            Assert.Equal(178, ((Color32)new Color(0.7f, 0f, 0f, 0f)).r);
        }

        [Fact]
        public void ARenderersColorIsLinearInALinearProjectOutsideZeroToOneToo()
        {
            QualitySettings.s_ColorSpace = ColorSpace.Gamma;
            Assert.Equal(Tint, VertexColors.InColorSpace(Tint));
            QualitySettings.s_ColorSpace = ColorSpace.Linear;
            var linear = VertexColors.InColorSpace(Tint);
            Near(new Color(Mathf.GammaToLinearSpace(0.657f), 0.02f / 12.92f, Mathf.Pow(1.25f, 2.2f), 0.5f), linear);
            Assert.Equal(-0.1f / 12.92f, VertexColors.InColorSpace(Negative).r, 6);
        }

        [Fact]
        public void ASpriteRendererDrawsItsColorInTheColorSpaceForTheVertices()
        {
            var camera = Camera();
            var go = Own(new GameObject("sprite"));
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = WhiteSprite();
            sprite.sharedMaterial = Material();
            sprite.color = Tint;
            foreach (var space in new[] { ColorSpace.Gamma, ColorSpace.Linear })
            {
                QualitySettings.s_ColorSpace = space;
                var draw = Assert.Single(Draws(sprite, camera));
                Assert.Equal(DrawTint.Vertices, draw.Tint);
                Near(space == ColorSpace.Linear ? Tint.linear : Tint, draw.Color);
            }
        }

        static readonly int s_RendererColor = Shader.PropertyToID("_RendererColor");

        /// <summary>Shader declarations whose keyword space is <paramref name="keywords"/>.</summary>
        static string Space(params string[] keywords) =>
            "{\"found\":true,\"builtin\":false,\"subshaders_known\":true,\"keywords_known\":true,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":["
            + string.Join(",", Array.ConvertAll(keywords, k => "{\"name\":\"" + k + "\",\"local\":false,\"dynamic\":false}")) + "]}";

        [Fact]
        public void ASpriteSetsItsRendererColorWhiteUnlessTheBuiltinPipelineInstancesIt()
        {
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_CallForTests = (method, request) => request.Contains("Probe/Instanced") ? Space("INSTANCING_ON") : Space("PIXELSNAP_ON");
            var camera = Camera();
            var go = Own(new GameObject("sprite"));
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = WhiteSprite();
            var instancing = Own(new Material(Shader.Create("Probe/Instanced", 3000)) { enableInstancing = true });
            var plain = Own(new Material(Shader.Create("Probe/Plain", 3000)) { enableInstancing = true });
            sprite.color = Tint;
            QualitySettings.s_ColorSpace = ColorSpace.Linear;
            GraphicsSettings.defaultRenderPipeline = null;
            // Without instancing: the color in the vertices, `_RendererColor` white.
            sprite.sharedMaterial = Material();
            var draw = Assert.Single(Draws(sprite, camera));
            Assert.Equal((DrawTint.Vertices, Color.white), (draw.Tint, (Color)draw.Properties[s_RendererColor]));
            Near(Tint.linear, draw.Color);
            // Instanced: the color as it is (neither linear nor clamped) in `_RendererColor`, white vertices.
            sprite.sharedMaterial = instancing;
            draw = Assert.Single(Draws(sprite, camera));
            Assert.Equal((DrawTint.None, Color.white, Tint), (draw.Tint, draw.Color, (Color)draw.Properties[s_RendererColor]));
            // A draw collected before the color changes keeps its color.
            sprite.color = Negative;
            Assert.Equal(Tint, (Color)draw.Properties[s_RendererColor]);
            Assert.Equal(Negative, (Color)Assert.Single(Draws(sprite, camera)).Properties[s_RendererColor]);
            // A material that does not enable instancing, a shader without an instancing variant,
            // or a render pipeline: the color in the vertices.
            instancing.enableInstancing = false;
            Assert.Equal(DrawTint.Vertices, Assert.Single(Draws(sprite, camera)).Tint);
            instancing.enableInstancing = true;
            sprite.sharedMaterial = plain;
            Assert.Equal(DrawTint.Vertices, Assert.Single(Draws(sprite, camera)).Tint);
            sprite.sharedMaterials = new[] { plain, instancing };
            var both = Draws(sprite, camera);
            Assert.Equal(new[] { DrawTint.Vertices, DrawTint.None }, both.ConvertAll(d => d.Tint).ToArray());
            GraphicsSettings.defaultRenderPipeline = Own(ScriptableObject.CreateInstance<Pipeline2D>());
            Assert.All(Draws(sprite, camera), d => Assert.Equal(DrawTint.Vertices, d.Tint));
        }

        Tilemap TilemapOf(TilemapRenderer.Mode mode, out TilemapRenderer renderer)
        {
            var go = Own(new GameObject("grid"));
            go.AddComponent<Grid>();
            var holder = new GameObject("tilemap");
            holder.transform.SetParent(go.transform, false);
            var tilemap = holder.AddComponent<Tilemap>();
            renderer = holder.AddComponent<TilemapRenderer>();
            renderer.mode = mode;
            renderer.sharedMaterial = Material();
            tilemap.color = new Color(0.5f, 0.7f, 0.9f, 0.6f);
            var tile = Own(ScriptableObject.CreateInstance<Tile>());
            tile.sprite = WhiteSprite();
            tile.flags = TileFlags.None;
            tilemap.SetTile(Vector3Int.zero, tile);
            tilemap.SetColor(Vector3Int.zero, Tint);
            tilemap.SetTile(Vector3Int.right, tile);
            tilemap.SetColor(Vector3Int.right, Negative);
            return tilemap;
        }

        static Mesh FirstBatch(TilemapRenderer renderer) => renderer.BatchMesh(0);

        [Fact]
        public void TilemapChunksCarryTheirCellColorsAsBytesOfTheLinearProduct()
        {
            QualitySettings.s_ColorSpace = ColorSpace.Linear;
            var camera = Camera();
            var tilemap = TilemapOf(TilemapRenderer.Mode.Chunk, out var renderer);
            var draw = Assert.Single(Draws(renderer, camera));
            Assert.Equal(DrawTint.None, draw.Tint);
            Assert.Equal(Color.white, draw.Color);
            var colors = FirstBatch(renderer).colors32;
            Assert.Equal(VertexColors.Bytes((Tint * tilemap.color).linear), colors[0]);
            Assert.Equal(VertexColors.Bytes((Negative * tilemap.color).linear), colors[colors.Length - 1]);
            // The product is converted, not each color (they differ below the knee).
            Assert.NotEqual(VertexColors.Bytes(Tint.linear * tilemap.color.linear), colors[0]);

            // In the gamma color space the bytes are the product's own.
            QualitySettings.s_ColorSpace = ColorSpace.Gamma;
            Draws(renderer, camera);
            Assert.Equal(VertexColors.Bytes(Tint * tilemap.color), FirstBatch(renderer).colors32[0]);
        }

        [Fact]
        public void TilemapCellsDrawnOneByOneCarryTheirColorAsTheDrawsOwn()
        {
            QualitySettings.s_ColorSpace = ColorSpace.Linear;
            var camera = Camera();
            var tilemap = TilemapOf(TilemapRenderer.Mode.Individual, out var renderer);
            var draws = Draws(renderer, camera);
            Assert.Equal(2, draws.Count);
            Assert.All(draws, d => Assert.Equal(DrawTint.Vertices, d.Tint));
            Near((Tint * tilemap.color).linear, draws[0].Color);
            Near((Negative * tilemap.color).linear, draws[1].Color);
        }

        SpriteShapeRenderer Shape(Color color, Material material)
        {
            var go = Own(new GameObject("shape"));
            var r = go.AddComponent<SpriteShapeRenderer>();
            r.sharedMaterials = new[] { material };
            r.color = color;
            var segments = r.GetSegments(1);
            segments[0] = new SpriteShapeSegment { geomIndex = 0, indexCount = 3, vertexCount = 3, spriteIndex = -1 };
            r.GetChannels(3, out var indices, out var positions, out _);
            positions[0] = new Vector3(-0.5f, -0.5f, 0f);
            positions[1] = new Vector3(0.5f, -0.5f, 0f);
            positions[2] = new Vector3(0f, 0.5f, 0f);
            indices[0] = 0;
            indices[1] = 2;
            indices[2] = 1;
            r.SetLocalAABB(new Bounds(Vector3.zero, Vector3.one));
            r.Prepare(default, new SpriteShapeParameters { fillTexture = WhiteSprite().texture }, new Sprite[0]);
            return r;
        }

        [Fact]
        public void ASpriteShapesColorIsAConstantAndTheBuiltInRendererColorItsBytes()
        {
            QualitySettings.s_ColorSpace = ColorSpace.Linear;
            var camera = Camera();
            var shape = Shape(Tint, Material());
            var draw = Assert.Single(Draws(shape, camera));
            Assert.Equal(DrawTint.Constant, draw.Tint);
            Near(Tint.linear, draw.Color);
            Assert.Equal((Color)VertexColors.Bytes(Tint.linear), draw.Properties[Shader.PropertyToID("_RendererColor")]);
            // The same properties while the color stays; new ones once it changes.
            var again = Assert.Single(Draws(shape, camera));
            Assert.Same(draw.Properties, again.Properties);
            shape.color = Negative;
            var changed = Assert.Single(Draws(shape, camera));
            Assert.Equal((Color)VertexColors.Bytes(Negative.linear), changed.Properties[Shader.PropertyToID("_RendererColor")]);
            // A draw collected before the change keeps its color.
            Assert.Equal((Color)VertexColors.Bytes(Tint.linear), draw.Properties[Shader.PropertyToID("_RendererColor")]);
            // The vertices keep no color channel of the renderer's.
            Assert.Empty(((Mesh)typeof(SpriteShapeRenderer).GetField("m_Mesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(shape)).colors32);
        }

        sealed class Pipeline2D : RenderPipelineAsset
        {
            internal Material Default2D;
            protected override RenderPipeline CreatePipeline() => null;
            public override Material default2DMaterial => Default2D;
        }

        [Fact]
        public void RenderersAddedByAScriptTakeThePipelinesDefault2DMaterial()
        {
            var pipeline = Own(ScriptableObject.CreateInstance<Pipeline2D>());
            pipeline.Default2D = Material();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var go = Own(new GameObject("renderers"));
            Assert.Same(pipeline.Default2D, go.AddComponent<SpriteRenderer>().sharedMaterial);
            var shapes = go.AddComponent<SpriteShapeRenderer>().sharedMaterials;
            Assert.Equal(new[] { pipeline.Default2D, pipeline.Default2D }, shapes);
            var holder = Own(new GameObject("tilemap"));
            holder.AddComponent<Tilemap>();
            Assert.Same(pipeline.Default2D, holder.AddComponent<TilemapRenderer>().sharedMaterial);

            // A renderer read from a scene keeps what the pipeline does not decide.
            var read = Own(new GameObject("read"));
            var deserialized = (SpriteRenderer)ComponentFactory.Create(typeof(SpriteRenderer), read, deserializing: true);
            Assert.NotSame(pipeline.Default2D, deserialized.sharedMaterial);

            // A pipeline without a default 2D material leaves the built-in one.
            pipeline.Default2D = null;
            var plain = Own(new GameObject("plain")).AddComponent<SpriteRenderer>();
            Assert.Equal(deserialized.sharedMaterial, plain.sharedMaterial);
        }
    }
}
