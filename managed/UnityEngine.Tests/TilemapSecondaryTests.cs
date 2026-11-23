using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Tilemaps;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A tilemap draws its tiles' sprites with their secondary textures and the sprite vertex
    /// frame turned with each cell, as the cells' sprite renderers would (measured in the Unity
    /// 6000.0.71f1 Editor, tests/projects/tilemapsecondary).</summary>
    public class TilemapSecondaryTests : IDisposable
    {
        readonly List<Object> m_Made = new List<Object>();
        static readonly int s_NormalMap = Shader.PropertyToID("_NormalMap");
        static readonly int s_MaskTex = Shader.PropertyToID("_MaskTex");

        public void Dispose()
        {
            foreach (var o in m_Made) if (o) Object.DestroyImmediate(o);
        }

        T Own<T>(T o) where T : Object
        {
            m_Made.Add(o);
            return o;
        }

        Texture2D Texture() => Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));

        Sprite SpriteOf(Texture2D texture, params SecondarySpriteTexture[] secondary) =>
            Own(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect,
                Vector4.zero, false, secondary));

        Tile TileOf(Sprite sprite)
        {
            var tile = Own(ScriptableObject.CreateInstance<Tile>());
            tile.sprite = sprite;
            tile.flags = TileFlags.None;
            return tile;
        }

        (Tilemap Tilemap, TilemapRenderer Renderer) Map()
        {
            var go = Own(new GameObject("grid"));
            go.AddComponent<Grid>();
            var holder = new GameObject("tilemap");
            holder.transform.SetParent(go.transform, false);
            var tilemap = holder.AddComponent<Tilemap>();
            var renderer = holder.AddComponent<TilemapRenderer>();
            renderer.sharedMaterial = Own(new Material(Shader.Create("Probe/Tiles", 3000)));
            return (tilemap, renderer);
        }

        List<FrameBuilder.DrawItem> Draws(Renderer renderer)
        {
            var eye = Own(new GameObject("eye"));
            eye.transform.position = new Vector3(0f, 0f, -10f);
            var camera = eye.AddComponent<Camera>();
            camera.orthographic = true;
            var draws = new List<FrameBuilder.DrawItem>();
            renderer.CollectDraws(FrameBuilder.ContextOf(camera), draws);
            return draws;
        }

        [Fact]
        public void ChunksCarryTheirTilesSecondaryTexturesAndSplitWhereTheSetChanges()
        {
            var main = Texture();
            var normals = Texture();
            var mask = Texture();
            var otherNormals = Texture();
            var withBoth = SpriteOf(main, new SecondarySpriteTexture { name = "_NormalMap", texture = normals },
                new SecondarySpriteTexture { name = "_MaskTex", texture = mask });
            // Another sprite of the same texture and the same secondary textures, listed the other way round.
            var alike = SpriteOf(main, new SecondarySpriteTexture { name = "_MaskTex", texture = mask },
                new SecondarySpriteTexture { name = "_NormalMap", texture = normals });
            var other = SpriteOf(main, new SecondarySpriteTexture { name = "_NormalMap", texture = otherNormals });
            var plain = SpriteOf(main);
            var (tilemap, renderer) = Map();
            tilemap.SetTile(new Vector3Int(0, 0, 0), TileOf(withBoth));
            tilemap.SetTile(new Vector3Int(1, 0, 0), TileOf(alike));
            tilemap.SetTile(new Vector3Int(2, 0, 0), TileOf(other));
            tilemap.SetTile(new Vector3Int(3, 0, 0), TileOf(plain));

            var draws = Draws(renderer);
            Assert.Equal(3, draws.Count);
            Assert.Same(normals, draws[0].Properties[s_NormalMap]);
            Assert.Same(mask, draws[0].Properties[s_MaskTex]);
            Assert.Equal(8, renderer.BatchMesh(0).vertexCount);
            Assert.Same(otherNormals, draws[1].Properties[s_NormalMap]);
            Assert.False(draws[1].Properties.ContainsKey(s_MaskTex));
            // A tile without secondary textures sets none (the material's own stay).
            Assert.Null(draws[2].Properties);
        }

        [Fact]
        public void IndividualCellsCarryEachTheirOwnSecondaryTextures()
        {
            var main = Texture();
            var first = Texture();
            var second = Texture();
            var (tilemap, renderer) = Map();
            renderer.mode = TilemapRenderer.Mode.Individual;
            tilemap.SetTile(Vector3Int.zero, TileOf(SpriteOf(main, new SecondarySpriteTexture { name = "_NormalMap", texture = first })));
            tilemap.SetTile(Vector3Int.right, TileOf(SpriteOf(main, new SecondarySpriteTexture { name = "_NormalMap", texture = second })));
            var draws = Draws(renderer);
            Assert.Equal(2, draws.Count);
            Assert.Same(first, draws[0].Properties[s_NormalMap]);
            Assert.Same(second, draws[1].Properties[s_NormalMap]);
        }

        [Fact]
        public void AnXzTilemapTurnsTheFrameWithItsOrientation()
        {
            var (tilemap, renderer) = Map();
            tilemap.orientation = Tilemap.Orientation.XZ;
            tilemap.SetTile(Vector3Int.zero, TileOf(SpriteOf(Texture())));
            Draws(renderer);
            var mesh = renderer.BatchMesh(0);
            var expectedNormal = tilemap.orientationMatrix.MultiplyVector(new Vector3(0, 0, -1)).normalized;
            Assert.True((expectedNormal - new Vector3(0, 0, -1)).magnitude > 0.5f);
            Assert.All(mesh.normals, n => Assert.True((n - expectedNormal).magnitude < 1e-5f));
            Assert.All(mesh.tangents, t => Assert.Equal(-1f, t.w));
        }

        [Fact]
        public void TileVerticesTakeTheSpriteFrameTurnedWithTheirCell()
        {
            var sprite = SpriteOf(Texture());
            var (tilemap, renderer) = Map();
            tilemap.SetTile(Vector3Int.zero, TileOf(sprite));
            tilemap.SetTile(Vector3Int.up, TileOf(SpriteOf(Texture())));
            tilemap.SetTransformMatrix(Vector3Int.up, Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)));
            Draws(renderer);

            var still = renderer.BatchMesh(0);
            Assert.All(still.normals, n => Assert.Equal(new Vector3(0, 0, -1), n));
            Assert.All(still.tangents, t => Assert.Equal(new Vector4(1, 0, 0, -1), t));
            var turned = renderer.BatchMesh(1);
            Assert.All(turned.normals, n => Assert.True((n - new Vector3(0, 0, -1)).magnitude < 1e-5f));
            Assert.All(turned.tangents, t => Assert.True((t - new Vector4(0, 1, 0, -1)).magnitude < 1e-5f));
        }
    }
}
