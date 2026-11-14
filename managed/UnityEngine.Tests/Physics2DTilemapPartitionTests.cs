using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Tilemaps;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class Physics2DTilemapPartitionTests : IDisposable
    {
        readonly Physics2DMeasuredScope release = new Physics2DMeasuredScope();
        readonly List<Object> objects = new List<Object>();

        (Tilemap Map, TilemapCollider2D Collider) Map(GridLayout.CellLayout layout = GridLayout.CellLayout.Rectangle)
        {
            var grid = new GameObject("grid", typeof(Grid)); objects.Add(grid);
            grid.GetComponent<Grid>().cellLayout = layout;
            grid.transform.position = new Vector3(.3f, -.2f, 0);
            grid.transform.rotation = Quaternion.Euler(0, 0, 11);
            grid.transform.localScale = new Vector3(1.4f, .7f, 1);
            var root = new GameObject("map", typeof(Tilemap)); root.transform.SetParent(grid.transform, false);
            root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            return (root.GetComponent<Tilemap>(), root.AddComponent<TilemapCollider2D>());
        }
        Tile MakeTile(params Vector2[][] paths)
        {
            var texture = new Texture2D(16, 16); objects.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 16);
            sprite.m_PhysicsShape = paths; objects.Add(sprite);
            var tile = ScriptableObject.CreateInstance<Tile>(); objects.Add(tile);
            tile.sprite = sprite; tile.colliderType = Tile.ColliderType.Sprite; tile.flags = TileFlags.None;
            return tile;
        }
        static PhysicsShapeGroup2D Shapes(TilemapCollider2D collider)
        {
            Physics2D.SyncTransforms();
            var result = new PhysicsShapeGroup2D(); collider.GetShapes(result); return result;
        }
        static string Counts(PhysicsShapeGroup2D group)
        {
            var counts = new int[group.shapeCount];
            for (int i = 0; i < counts.Length; i++) counts[i] = group.GetShape(i).vertexCount;
            return string.Join(",", counts);
        }
        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.72f1", false)]
        public void RefinementSelectsTheMeasuredCellPartition(string version, bool measured)
        {
            Physics2DWorld.Reset(); ProjectInfo.UnityVersion = version;
            var (map, collider) = Map();
            var tile = MakeTile(new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, 0),
                Vector2.zero, new Vector2(0, .5f), new Vector2(-.5f, .5f) });
            map.SetTile(Vector3Int.zero, tile);
            map.SetTransformMatrix(Vector3Int.zero, Matrix4x4.TRS(new Vector3(.125f, -.0625f, 0),
                Quaternion.Euler(0, 0, 23), new Vector3(1, .8f, 1)));
            collider.extrusionFactor = .1f; collider.ProcessTilemapChanges();
            var plain = Counts(Shapes(collider));
            collider.useDelaunayMesh = true;
            var refined = Counts(Shapes(collider));
            if (measured) { Assert.Equal("4,5,6", plain); Assert.Equal("4,4,7", refined); }
            else Assert.Equal(plain, refined);
        }
        [Fact]
        public void HolesAndIslandsSurviveAFlatPathWithinOneCell()
        {
            var (map, collider) = Map();
            Vector2[] Square(float x, float size) => new[] { new Vector2(x-size,-size), new Vector2(x+size,-size),
                new Vector2(x+size,size), new Vector2(x-size,size) };
            var tile = MakeTile(Square(0, 2), Square(0, 1), Square(4, .5f),
                new[] { new Vector2(10, 0), new Vector2(10, 0), new Vector2(11, 0), new Vector2(12, 0) });
            map.SetTile(Vector3Int.zero, tile); collider.ProcessTilemapChanges(); Shapes(collider);
            Vector2 World(Vector2 local) => map.transform.TransformPoint((Vector3)local + map.GetCellCenterLocal(Vector3Int.zero));
            Assert.False(collider.OverlapPoint(World(Vector2.zero)));
            Assert.True(collider.OverlapPoint(World(new Vector2(1.5f, 0))));
            Assert.True(collider.OverlapPoint(World(new Vector2(4, 0))));
            Assert.False(collider.OverlapPoint(World(new Vector2(11, 0))));
        }
        [Fact]
        public void GridCellsRetainTheirHexagonalOutline()
        {
            var (map, collider) = Map(GridLayout.CellLayout.Hexagon);
            var tile = ScriptableObject.CreateInstance<Tile>(); objects.Add(tile); tile.colliderType = Tile.ColliderType.Grid;
            map.SetTile(Vector3Int.zero, tile); collider.ProcessTilemapChanges();
            var group = Shapes(collider);
            Assert.Equal(1, group.shapeCount); Assert.Equal(6, group.GetShape(0).vertexCount);
            Assert.True(collider.OverlapPoint(map.GetCellCenterWorld(Vector3Int.zero)));
        }
        [Fact]
        public void SpriteTilesTraceEachSpriteOncePerPass()
        {
            // Sprites without a physics shape take their traced outline: a sprite on several cells
            // is traced once per pass, a second sprite once, and a later pass over changed cells
            // traces again (a script may have rewritten the texels between passes).
            var (map, collider) = Map();
            var texture = new Texture2D(16, 16); objects.Add(texture);
            Tile Make(Rect rect)
            {
                var sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 16); objects.Add(sprite);
                var tile = ScriptableObject.CreateInstance<Tile>(); objects.Add(tile);
                tile.sprite = sprite; tile.colliderType = Tile.ColliderType.Sprite;
                return tile;
            }
            var whole = Make(new Rect(0, 0, 16, 16));
            var corner = Make(new Rect(0, 0, 8, 8));
            int before = Sprite.s_ColliderOutlineTraces;
            for (int x = 0; x < 4; x++) map.SetTile(new Vector3Int(x, 0, 0), whole);
            map.SetTile(new Vector3Int(0, 1, 0), corner);
            collider.ProcessTilemapChanges();
            Assert.Equal(2, Sprite.s_ColliderOutlineTraces - before);
            map.SetTile(new Vector3Int(1, 1, 0), whole);
            map.SetTile(new Vector3Int(2, 1, 0), whole);
            collider.ProcessTilemapChanges();
            Assert.Equal(3, Sprite.s_ColliderOutlineTraces - before);
        }

        public void Dispose()
        {
            foreach (var value in objects) if (value) Object.DestroyImmediate(value);
            release.Dispose();
        }
    }
}
