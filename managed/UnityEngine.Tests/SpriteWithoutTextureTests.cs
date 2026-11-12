using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Sprites made the way the SVG importer makes them (tests/projects/svgsprites): the
    /// internal Create without a texture, OverrideGeometry, then colour and setting-index channels.</summary>
    public sealed class SpriteWithoutTextureTests
    {
        static Sprite VectorSprite()
        {
            var sprite = Sprite.Create(new Rect(0, 0, 64, 32), new Vector2(.5f, .5f), 0f, null);
            sprite.OverrideGeometry(new[] { new Vector2(0, 0), new Vector2(64, 0), new Vector2(0, 32), new Vector2(64, 32) },
                new ushort[] { 0, 2, 1, 1, 2, 3 });
            return sprite;
        }

        [Fact]
        public void TheInternalCreateMakesAnEmptyTightSpriteAndOverrideGeometryKeepsZeroUvsWithoutATexture()
        {
            var empty = Sprite.Create(new Rect(0, 0, 64, 32), new Vector2(.25f, .5f), -1f, null);
            Assert.Equal(100f, empty.pixelsPerUnit);
            Assert.Equal(new Vector2(16, 16), empty.pivot);
            Assert.Empty(empty.vertices);
            Assert.Empty(empty.triangles);
            Assert.Null(empty.texture);
            Object.DestroyImmediate(empty);

            var sprite = VectorSprite();
            Assert.Equal(new Vector2(-.32f, -.16f), sprite.vertices[0]);
            Assert.All(sprite.uv, uv => Assert.Equal(Vector2.zero, uv));
            Object.DestroyImmediate(sprite);
        }

        [Fact]
        public void ColourAndSettingIndexChannelsSurviveACopyAndReachTheRenderMesh()
        {
            var sprite = VectorSprite();
            using (var colors = new NativeArray<Color32>(Enumerable.Repeat(new Color32(32, 64, 128, 255), 4).ToArray(), Allocator.Temp))
                sprite.SetVertexAttribute(VertexAttribute.Color, colors);
            using (var settings = new NativeArray<Vector2>(new[] { new Vector2(1, 0), new Vector2(1, 0), new Vector2(2, 0), new Vector2(2, 0) }, Allocator.Temp))
                sprite.SetVertexAttribute(VertexAttribute.TexCoord2, settings);
            var copy = Object.Instantiate(sprite);
            Assert.True(copy.HasVertexAttribute(VertexAttribute.TexCoord2));
            Assert.Equal(new Vector2(2, 0), copy.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord2)[3]);
            var mesh = copy.RenderMesh();
            Assert.Equal(new Color32(32, 64, 128, 255), mesh.colors32[0]);
            var uv2 = new System.Collections.Generic.List<Vector2>();
            mesh.GetUVs(2, uv2);
            Assert.Equal(new Vector2(2, 0), uv2[3]);
            Object.DestroyImmediate(mesh);

            // New geometry drops the channels that described the old vertices.
            sprite.OverrideGeometry(new[] { new Vector2(0, 0), new Vector2(64, 0), new Vector2(0, 32) }, new ushort[] { 0, 2, 1 });
            Assert.False(sprite.HasVertexAttribute(VertexAttribute.Color));
            Assert.False(sprite.HasVertexAttribute(VertexAttribute.TexCoord2));
            Assert.Equal(3, sprite.GetVertexCount());
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(sprite);
        }

        [Fact]
        public void ASpriteMadeWithoutATextureIsDrawnInSimpleModeOnly()
        {
            var sprite = VectorSprite();
            var go = new GameObject("vector");
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            Assert.True(renderer.MaskShape(out var mesh, out _, out var texture));
            Assert.Null(texture);
            Assert.Equal(4, mesh.vertexCount);
            renderer.drawMode = SpriteDrawMode.Sliced;
            Assert.False(renderer.MaskShape(out _, out _, out _));
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(sprite);
        }

        [Fact]
        public void SpriteIdsAndEditorHooksAreKept()
        {
            var sprite = VectorSprite();
            var id = GUID.Generate();
            sprite.SetSpriteID(id);
            Assert.Equal(id, sprite.GetSpriteID());
            System.Action<EditorWindow, HyperLinkClickedEventArgs> listener = (_, _) => { };
            EditorGUI.hyperLinkClicked += listener;
            EditorGUI.hyperLinkClicked -= listener;
            Object.DestroyImmediate(sprite);
        }
    }
}
