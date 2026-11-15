using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Tilemaps;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Collider2D.GetShapeBounds and GetShapeHash (values from the Unity 6000.0.71f1
    /// Editor reference in tests/projects/shapes2d).</summary>
    public sealed class Physics2DShapeBoundsAndHashTests : IDisposable
    {
        readonly Physics2DMeasuredScope release = new Physics2DMeasuredScope();
        readonly List<Object> objects = new List<Object>();

        public Physics2DShapeBoundsAndHashTests()
        {
            Physics2D.autoSyncTransforms = false;
            Physics2D.defaultContactOffset = .01f;
        }

        public void Dispose()
        {
            foreach (var o in objects) if (o) Object.DestroyImmediate(o);
            Physics2D.defaultContactOffset = .01f;
            release.Dispose();
        }

        T Add<T>(string name, Vector3 position) where T : Component
        {
            var go = new GameObject(name); objects.Add(go);
            go.transform.position = position;
            return go.AddComponent<T>();
        }

        static void Near(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 5);
            Assert.Equal(expected.y, actual.y, 5);
            Assert.Equal(expected.z, actual.z, 5);
        }

        [Theory]
        [InlineData("", 0u, 0x02cc5d05u)]
        [InlineData("abc", 0u, 0x32d153ffu)]
        [InlineData("Nobody inspects the spammish repetition", 0u, 0xe2293b2fu)]
        [InlineData("abc", 0x8f37154bu, 0xbe5f11d1u)]
        public void XxHash32MatchesTheReferenceAlgorithm(string text, uint seed, uint expected) =>
            Assert.Equal(expected, XxHash32.Hash(System.Text.Encoding.ASCII.GetBytes(text), seed));

        [Fact]
        public void ABoxHashesItsRadiusAndHullVerticesFromTheSeed()
        {
            var box = Add<BoxCollider2D>("box", Vector3.zero);
            Assert.Equal(0xd9845b03u, box.GetShapeHash());
            box.size = new Vector2(1, 2);
            Assert.Equal(0xf7ab0ce7u, box.GetShapeHash());
            box.size = Vector2.one;
            Assert.Equal(0xd9845b03u, box.GetShapeHash());
            // The same square on a moved body, at another depth or as a polygon hashes the same.
            var body = Add<Rigidbody2D>("body", new Vector3(-3, 7, 4));
            body.transform.rotation = Quaternion.Euler(0, 0, 70);
            Assert.Equal(0xd9845b03u, body.gameObject.AddComponent<BoxCollider2D>().GetShapeHash());
            var polygon = Add<PolygonCollider2D>("polygon", Vector3.zero);
            polygon.SetPath(0, new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, .5f), new Vector2(-.5f, .5f) });
            Assert.Equal(0xd9845b03u, polygon.GetShapeHash());
            box.enabled = false;
            Assert.Equal(Collider2D.ShapeHashSeed, box.GetShapeHash());
        }

        [Fact]
        public void CircleHashesFollowTheirGeometry()
        {
            var a = Add<CircleCollider2D>("a", new Vector3(2, 2, 0));
            var b = Add<CircleCollider2D>("b", new Vector3(2, 2, 3));
            Assert.Equal(a.GetShapeHash(), b.GetShapeHash());
            uint before = a.GetShapeHash();
            a.radius = .75f;
            Assert.NotEqual(before, a.GetShapeHash());
            a.radius = .5f;
            Assert.Equal(before, a.GetShapeHash());
            Assert.NotEqual(before, Add<CapsuleCollider2D>("capsule", new Vector3(2, 2, 0)).GetShapeHash());
        }

        [Fact]
        public void WithoutShapesTheListStaysAndTheTransformPositionReturns()
        {
            var box = Add<BoxCollider2D>("box", new Vector3(1, 2, 3));
            box.enabled = false;
            var list = new List<Bounds> { new Bounds(new Vector3(7, 8, 9), Vector3.one), new Bounds() };
            var all = box.GetShapeBounds(list, true, true);
            Assert.Equal(2, list.Count);
            Near(new Vector3(7, 8, 9), list[0].center);
            Near(new Vector3(1, 2, 3), all.center);
            Near(Vector3.zero, all.extents);
            box.enabled = true;
            all = box.GetShapeBounds(list, false, false);
            Assert.Single(list);
            Near(new Vector3(1, 2, 3), all.center);
            Near(new Vector3(.5f, .5f, 0), all.extents);
            Near(new Vector3(.51f, .51f, 0), box.GetShapeBounds(null, true, true).extents);
        }

        [Fact]
        public void LocalBoundsUseTheBodySpaceAndPendingMovesWaitForASync()
        {
            var body = Add<Rigidbody2D>("body", new Vector3(1, 1, 1));
            body.gravityScale = 0;
            var child = new GameObject("box"); objects.Add(child);
            child.transform.SetParent(body.transform, false);
            child.transform.localPosition = new Vector3(.5f, 0, 0);
            var box = child.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            body.transform.SetPositionAndRotation(new Vector3(4, -2, 6), Quaternion.Euler(0, 0, 90));
            Near(new Vector3(1.5f, 1, 6), box.GetShapeBounds(null, false, true).center);
            Physics2D.SyncTransforms();
            Near(new Vector3(.5f, 0, 6), box.GetShapeBounds(null, false, false).center);
            Near(new Vector3(4, -1.5f, 6), box.GetShapeBounds(null, false, true).center);
        }

        [Fact]
        public void PolygonRadiiCountBeyondTheContactOffsetAndCirclesAlwaysCount()
        {
            var box = Add<BoxCollider2D>("box", Vector3.zero);
            Physics2D.defaultContactOffset = .05f;
            Near(new Vector3(.51f, .51f, 0), box.GetShapeBounds(null, true, false).extents);
            box.enabled = false; box.enabled = true;
            Near(new Vector3(.55f, .55f, 0), box.GetShapeBounds(null, true, false).extents);
            Physics2D.defaultContactOffset = .01f;
            Near(new Vector3(.54f, .54f, 0), box.GetShapeBounds(null, true, false).extents);
            // A new edge radius takes the current offset; the same radius again keeps the skin.
            box.edgeRadius = .005f;
            Near(new Vector3(.505f, .505f, 0), box.GetShapeBounds(null, true, false).extents);
            Physics2D.defaultContactOffset = .02f;
            box.edgeRadius = .005f;
            Near(new Vector3(.515f, .515f, 0), box.GetShapeBounds(null, true, false).extents);
            var circle = Add<CircleCollider2D>("circle", new Vector3(2, 2, 0));
            Near(new Vector3(.5f, .5f, 0), circle.GetShapeBounds(null, false, false).extents);
        }

        Tile GridTile(Tile.ColliderType type = Tile.ColliderType.Grid, Sprite sprite = null)
        {
            if (sprite == null)
            {
                var texture = new Texture2D(4, 4); objects.Add(texture);
                sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4); objects.Add(sprite);
            }
            var tile = ScriptableObject.CreateInstance<Tile>(); objects.Add(tile);
            tile.sprite = sprite; tile.colliderType = type;
            return tile;
        }

        (Tilemap Map, TilemapCollider2D Collider) Tilemap(Tile tile, IEnumerable<Vector3Int> cells)
        {
            var grid = new GameObject("grid", typeof(Grid)); objects.Add(grid);
            var layer = new GameObject("layer", typeof(Tilemap)); layer.transform.SetParent(grid.transform, false);
            var map = layer.GetComponent<Tilemap>();
            foreach (var cell in cells) map.SetTile(cell, tile);
            var collider = layer.AddComponent<TilemapCollider2D>();
            collider.ProcessTilemapChanges();
            return (map, collider);
        }

        static List<Vector3Int> Block()
        {
            var cells = new List<Vector3Int>();
            for (int x = 0; x < 3; x++) for (int y = 0; y < 2; y++) cells.Add(new Vector3Int(x, y, 0));
            return cells;
        }

        [Fact]
        public void TileChangesCarryTheHashAndAFullRebuildStartsAgain()
        {
            var tile = GridTile();
            var (map, collider) = Tilemap(tile, Block());
            Assert.Equal(0xc2280293u, collider.GetShapeHash());
            map.SetTile(new Vector3Int(2, 1, 0), null);
            map.SetTile(new Vector3Int(0, 0, 0), null);
            collider.ProcessTilemapChanges();
            Assert.Equal(0x6f595818u, collider.GetShapeHash());
            // A tile with the same sprite, collider type and transform changes nothing.
            uint removed = collider.GetShapeHash();
            map.SetTile(new Vector3Int(1, 0, 0), GridTile(Tile.ColliderType.Grid, tile.sprite));
            collider.ProcessTilemapChanges();
            Assert.Equal(removed, collider.GetShapeHash());
            // The same cells built whole hash from the seed in z, y, x order, as a re-enabled collider does.
            map.SetTile(new Vector3Int(1, 0, 0), null);
            collider.ProcessTilemapChanges();
            map.SetTile(new Vector3Int(1, 0, 0), tile);
            collider.ProcessTilemapChanges();
            uint carried = collider.GetShapeHash();
            var cells = new List<Vector3Int> { new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0) };
            uint fresh = Tilemap(tile, cells).Collider.GetShapeHash();
            Assert.NotEqual(fresh, carried);
            collider.enabled = false; collider.enabled = true;
            Assert.Equal(fresh, collider.GetShapeHash());
        }

        [Fact]
        public void AFullRebuildAfterTileChangesStartsAgainWithoutARead()
        {
            var tile = GridTile();
            var (map, collider) = Tilemap(tile, Block());
            uint fresh = collider.GetShapeHash();
            map.SetTile(new Vector3Int(1, 0, 0), null);
            collider.ProcessTilemapChanges();
            map.SetTile(new Vector3Int(1, 0, 0), tile);
            collider.ProcessTilemapChanges();
            // No read before the toggle: the re-enabled collider is built whole, in z, y, x order.
            collider.enabled = false; collider.enabled = true;
            Assert.Equal(fresh, collider.GetShapeHash());
            var group = new PhysicsShapeGroup2D();
            collider.GetShapes(group);
            Assert.Equal(new Vector2(2, 0), group.GetShapeVertex(1, 0));
            // The same holds for a geometry setting changed after a pass.
            map.SetTile(new Vector3Int(1, 0, 0), null);
            collider.ProcessTilemapChanges();
            map.SetTile(new Vector3Int(1, 0, 0), tile);
            collider.ProcessTilemapChanges();
            collider.offset = Vector2.zero;
            collider.useDelaunayMesh = true;
            Assert.Equal(fresh, collider.GetShapeHash());
        }

        [Fact]
        public void ATilemapOnABodyCarriesTheHashToo()
        {
            var tile = GridTile();
            var (map, collider) = Tilemap(tile, Block());
            collider.gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Physics2D.SyncTransforms();
            uint before = collider.GetShapeHash();
            map.SetTile(new Vector3Int(1, 1, 0), GridTile(Tile.ColliderType.Sprite, tile.sprite));
            collider.ProcessTilemapChanges();
            Assert.Equal(6, collider.shapeCount);
            Assert.NotEqual(before, collider.GetShapeHash());
            Assert.NotEqual(Tilemap(tile, Block()).Collider.GetShapeHash(), collider.GetShapeHash());
        }
    }
}
