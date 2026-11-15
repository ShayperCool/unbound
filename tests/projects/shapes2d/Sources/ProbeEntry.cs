using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Collider2D.GetShapeBounds and Collider2D.GetShapeHash for every collider type, attached to
/// bodies or not, a composite and a tilemap, with controls for disabled colliders, an output list
/// that already holds bounds, contact offset changes and moves before and after a sync. Each
/// scenario records the per-shape and combined bounds for the four flag combinations and the hash.
/// </summary>
public static class ProbeEntry
{
    static readonly List<Bounds> s_List = new List<Bounds>();

    static string Hex(uint value) => "0x" + value.ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>The bounds of all four flag combinations and the hash. Circle and capsule hashes
    /// differ between Editor processes, so those scenarios record hash relations only.</summary>
    static void Shapes(string prefix, Collider2D collider, bool hash = true)
    {
        foreach (bool radii in new[] { false, true })
        foreach (bool world in new[] { false, true })
        {
            string key = prefix + (radii ? "r1" : "r0") + (world ? "w1" : "w0");
            s_List.Clear();
            var all = collider.GetShapeBounds(s_List, radii, world);
            ProbeLog.Value(key + ".count", s_List.Count);
            for (int i = 0; i < s_List.Count; i++)
            {
                ProbeLog.Value(key + "." + i + ".center", s_List[i].center);
                ProbeLog.Value(key + "." + i + ".extents", s_List[i].extents);
            }
            ProbeLog.Value(key + ".all.center", all.center);
            ProbeLog.Value(key + ".all.extents", all.extents);
        }
        ProbeLog.Value(prefix + "shapeCount", collider.shapeCount);
        if (hash) ProbeLog.Value(prefix + "hash", Hex(collider.GetShapeHash()));
    }

    static readonly PhysicsShapeGroup2D s_Group = new PhysicsShapeGroup2D();

    /// <summary>Each shape's type, radius and vertices as GetShapes reports them (the hashed geometry).</summary>
    static void Geometry(string prefix, Collider2D collider)
    {
        collider.GetShapes(s_Group);
        for (int i = 0; i < s_Group.shapeCount; i++)
        {
            var shape = s_Group.GetShape(i);
            string key = prefix + "shape." + i;
            ProbeLog.Value(key + ".type", shape.shapeType.ToString());
            ProbeLog.Value(key + ".radius", shape.radius);
            for (int v = 0; v < shape.vertexCount; v++)
                ProbeLog.Value(key + ".v" + v, s_Group.GetShapeVertex(i, v));
        }
    }

    static GameObject Body(string name, Vector3 position, float angle, RigidbodyType2D type = RigidbodyType2D.Dynamic)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, angle));
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = type;
        rb.gravityScale = 0;
        return go;
    }

    static GameObject Child(GameObject parent, string name, Vector3 local, float angle, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0, 0, angle);
        go.transform.localScale = scale;
        return go;
    }

    static GameObject Static(string name, Vector3 position, float angle, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, angle));
        go.transform.localScale = scale;
        return go;
    }

    static readonly List<GameObject> s_Created = new List<GameObject>();

    static T Track<T>(T go) where T : UnityEngine.Object
    {
        if (go is GameObject g) s_Created.Add(g);
        return go;
    }

    static void Cleanup()
    {
        foreach (var go in s_Created) if (go) UnityEngine.Object.DestroyImmediate(go);
        s_Created.Clear();
        Physics2D.defaultContactOffset = 0.01f;
    }

    static void Scenario(string name, Action body)
    {
        ProbeLog.Begin(name);
        try { body(); }
        catch (Exception e) { ProbeLog.Error(e); }
        Cleanup();
    }

    static Tile s_Tile;

    static Tile GridTile()
    {
        if (s_Tile != null) return s_Tile;
        var texture = new Texture2D(4, 4);
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        texture.Apply();
        s_Tile = ScriptableObject.CreateInstance<Tile>();
        s_Tile.sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        s_Tile.colliderType = Tile.ColliderType.Grid;
        return s_Tile;
    }

    static Tile s_SpriteTile;

    /// <summary>A tile whose sprite outline is the same square as a grid cell.</summary>
    static Tile SpriteTile()
    {
        if (s_SpriteTile != null) return s_SpriteTile;
        s_SpriteTile = ScriptableObject.CreateInstance<Tile>();
        s_SpriteTile.sprite = GridTile().sprite;
        s_SpriteTile.colliderType = Tile.ColliderType.Sprite;
        return s_SpriteTile;
    }

    static TilemapCollider2D TilemapWith(string name, Vector3 position, IEnumerable<Vector3Int> cells)
    {
        var grid = Track(new GameObject(name));
        grid.transform.position = position;
        grid.AddComponent<Grid>();
        var layer = new GameObject("layer");
        layer.transform.SetParent(grid.transform, false);
        var map = layer.AddComponent<Tilemap>();
        foreach (var cell in cells) map.SetTile(cell, GridTile());
        var collider = layer.AddComponent<TilemapCollider2D>();
        collider.ProcessTilemapChanges();
        return collider;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.autoSyncTransforms = false;
        Physics2D.gravity = Vector2.zero;
        Physics2D.defaultContactOffset = 0.01f;

        Scenario("box-static", () =>
        {
            var box = Track(Static("box", new Vector3(1, 2, 3), 30, new Vector3(1.5f, 0.5f, 1))).AddComponent<BoxCollider2D>();
            box.size = new Vector2(2, 1);
            box.offset = new Vector2(0.5f, -0.25f);
            Shapes("", box);
        });
        Scenario("box-edge-radius", () =>
        {
            var box = Track(Static("box", new Vector3(1, 2, 3), 30, new Vector3(1.5f, 0.5f, 1))).AddComponent<BoxCollider2D>();
            box.size = new Vector2(2, 1);
            box.offset = new Vector2(0.5f, -0.25f);
            box.edgeRadius = 0.25f;
            Shapes("", box);
        });
        Scenario("box-body", () =>
        {
            var body = Track(Body("body", new Vector3(-3, 1, -2), 45));
            var box = Child(body, "box", new Vector3(1, 0.5f, 4), 10, new Vector3(1, 1, 1)).AddComponent<BoxCollider2D>();
            box.size = new Vector2(1, 2);
            Shapes("", box);
        });
        Scenario("circle-body", () =>
        {
            var body = Track(Body("body", new Vector3(2, -1, 5), 45));
            var circle = body.AddComponent<CircleCollider2D>();
            circle.radius = 0.75f;
            circle.offset = new Vector2(0.2f, 0.1f);
            Shapes("", circle, hash: false);
        });
        Scenario("capsule-body", () =>
        {
            var body = Track(Body("body", new Vector3(0.5f, 0.25f, 0), -60));
            var capsule = body.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(1, 3);
            Shapes("", capsule, hash: false);
        });
        Scenario("capsule-horizontal-static", () =>
        {
            var capsule = Track(Static("capsule", new Vector3(0, 4, 1), 15, Vector3.one)).AddComponent<CapsuleCollider2D>();
            capsule.direction = CapsuleDirection2D.Horizontal;
            capsule.size = new Vector2(3, 1);
            Shapes("", capsule, hash: false);
        });
        Scenario("polygon-body", () =>
        {
            var body = Track(Body("body", new Vector3(1, -2, 0.5f), 20));
            var polygon = body.AddComponent<PolygonCollider2D>();
            polygon.pathCount = 2;
            polygon.SetPath(0, new[] { new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 1), new Vector2(1, 1), new Vector2(1, 3), new Vector2(0, 3) });
            polygon.SetPath(1, new[] { new Vector2(5, 0), new Vector2(6, 0), new Vector2(5.5f, 2) });
            Shapes("", polygon);
            Geometry("", polygon);
        });
        Scenario("edge-two", () =>
        {
            var edge = Track(Static("edge", new Vector3(1, 1, 0), 0, Vector3.one)).AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(0, 0), new Vector2(2, 1) };
            edge.edgeRadius = 0.1f;
            Shapes("", edge);
        });
        Scenario("edge-chain-body", () =>
        {
            var body = Track(Body("body", new Vector3(-1, 3, 2), 30, RigidbodyType2D.Kinematic));
            var edge = body.AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(2, 0), new Vector2(3, 1) };
            edge.useAdjacentStartPoint = true;
            edge.adjacentStartPoint = new Vector2(-1, 1);
            Shapes("", edge);
        });
        foreach (var type in new[] { CompositeCollider2D.GeometryType.Polygons, CompositeCollider2D.GeometryType.Outlines })
        {
            Scenario("composite-" + type.ToString().ToLowerInvariant(), () =>
            {
                var body = Track(Body("body", new Vector3(2, 2, 1), 10, RigidbodyType2D.Static));
                var composite = body.AddComponent<CompositeCollider2D>();
                composite.geometryType = type;
                if (type == CompositeCollider2D.GeometryType.Outlines) composite.edgeRadius = 0.05f;
                var a = Child(body, "a", Vector3.zero, 0, Vector3.one).AddComponent<BoxCollider2D>();
                a.size = new Vector2(2, 1);
                a.compositeOperation = Collider2D.CompositeOperation.Merge;
                var b = Child(body, "b", new Vector3(0.5f, 0.5f, 0), 0, Vector3.one).AddComponent<BoxCollider2D>();
                b.size = new Vector2(1, 2);
                b.compositeOperation = Collider2D.CompositeOperation.Merge;
                composite.GenerateGeometry();
                Physics2D.SyncTransforms();
                Shapes("composite.", composite);
                Geometry("composite.", composite);
                Shapes("source.", a);
            });
        }
        Scenario("disabled", () =>
        {
            var box = Track(Static("box", new Vector3(1, 2, 3), 0, Vector3.one)).AddComponent<BoxCollider2D>();
            box.enabled = false;
            s_List.Clear();
            s_List.Add(new Bounds(new Vector3(7, 8, 9), new Vector3(1, 2, 3)));
            s_List.Add(new Bounds(Vector3.one, Vector3.one));
            var all = box.GetShapeBounds(s_List, true, true);
            ProbeLog.Value("kept.count", s_List.Count);
            ProbeLog.Value("kept.0.center", s_List[0].center);
            ProbeLog.Value("kept.all.center", all.center);
            ProbeLog.Value("kept.all.extents", all.extents);
            Shapes("", box);
            box.enabled = true;
            Shapes("enabled.", box);
        });
        Scenario("inactive", () =>
        {
            var go = Track(Static("box", new Vector3(-1, -2, -3), 0, Vector3.one));
            go.SetActive(false);
            var box = go.AddComponent<BoxCollider2D>();
            Shapes("", box);
        });
        Scenario("filled-list", () =>
        {
            var box = Track(Static("box", new Vector3(1, 2, 3), 0, Vector3.one)).AddComponent<BoxCollider2D>();
            s_List.Clear();
            for (int i = 0; i < 3; i++) s_List.Add(new Bounds(new Vector3(i, i, i), Vector3.one));
            var all = box.GetShapeBounds(s_List, false, false);
            ProbeLog.Value("count", s_List.Count);
            ProbeLog.Value("0.center", s_List[0].center);
            ProbeLog.Value("0.extents", s_List[0].extents);
            ProbeLog.Value("all.center", all.center);
        });
        Scenario("null-list", () =>
        {
            var box = Track(Static("box", new Vector3(1, 2, 3), 0, Vector3.one)).AddComponent<BoxCollider2D>();
            box.size = new Vector2(2, 1);
            try
            {
                var all = box.GetShapeBounds(null, true, true);
                ProbeLog.Value("center", all.center);
                ProbeLog.Value("extents", all.extents);
            }
            catch (Exception e) { ProbeLog.Value("throws", e.GetType().Name + ": " + e.Message); }
            box.enabled = false;
            try { ProbeLog.Value("disabled.center", box.GetShapeBounds(null, true, true).center); }
            catch (Exception e) { ProbeLog.Value("disabled.throws", e.GetType().Name + ": " + e.Message); }
        });
        Scenario("contact-offset", () =>
        {
            var box = Track(Static("box", Vector3.zero, 0, Vector3.one)).AddComponent<BoxCollider2D>();
            Shapes("before.", box);
            Physics2D.defaultContactOffset = 0.05f;
            Shapes("changed.", box);
            box.enabled = false;
            box.enabled = true;
            Shapes("recreated.", box);
            Physics2D.defaultContactOffset = 0.01f;
            Shapes("restored.", box);
            box.edgeRadius = 0.005f;
            Shapes("small-edge-radius.", box);
            Physics2D.defaultContactOffset = 0.02f;
            box.edgeRadius = 0.005f;
            Shapes("same-edge-radius.", box);
            box.edgeRadius = 0f;
            Shapes("zero-edge-radius.", box);
        });
        Scenario("body-moved", () =>
        {
            var body = Track(Body("body", new Vector3(1, 1, 1), 0));
            var box = Child(body, "box", new Vector3(0.5f, 0, 0), 0, Vector3.one).AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            Shapes("before.", box);
            body.transform.SetPositionAndRotation(new Vector3(4, -2, 6), Quaternion.Euler(0, 0, 90));
            Shapes("unsynced.", box);
            Physics2D.SyncTransforms();
            Shapes("synced.", box);
            body.GetComponent<Rigidbody2D>().position = new Vector2(-5, 0);
            Shapes("rigidbody-position.", box);
            box.transform.localPosition = new Vector3(1.5f, 0, 0);
            Physics2D.SyncTransforms();
            Shapes("child-moved.", box);
        });
        Scenario("static-moved", () =>
        {
            var box = Track(Static("box", new Vector3(1, 1, 1), 0, Vector3.one)).AddComponent<BoxCollider2D>();
            Shapes("before.", box);
            box.transform.SetPositionAndRotation(new Vector3(3, 2, -1), Quaternion.Euler(0, 0, 30));
            Shapes("unsynced.", box);
            Physics2D.SyncTransforms();
            Shapes("synced.", box);
            box.transform.SetPositionAndRotation(new Vector3(1, 1, 1), Quaternion.identity);
            Physics2D.SyncTransforms();
            Shapes("back.", box);
        });
        Scenario("hash-relations", () =>
        {
            var boxA = Track(Static("a", Vector3.zero, 0, Vector3.one)).AddComponent<BoxCollider2D>();
            var boxB = Track(Static("b", new Vector3(0, 0, 5), 0, Vector3.one)).AddComponent<BoxCollider2D>();
            var polygon = Track(Static("p", Vector3.zero, 0, Vector3.one)).AddComponent<PolygonCollider2D>();
            polygon.SetPath(0, new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) });
            var bodyA = Track(Body("ba", new Vector3(3, 0, 0), 0)).AddComponent<BoxCollider2D>();
            var bodyB = Track(Body("bb", new Vector3(-3, 7, 0), 70)).AddComponent<BoxCollider2D>();
            var circleA = Track(Static("ca", new Vector3(2, 2, 0), 0, Vector3.one)).AddComponent<CircleCollider2D>();
            var circleB = Track(Static("cb", new Vector3(2, 2, 0), 0, Vector3.one)).AddComponent<CircleCollider2D>();
            var circleC = Track(Static("cc", new Vector3(2, 2, 0), 0, Vector3.one)).AddComponent<CircleCollider2D>();
            circleC.radius = 0.75f;
            var capsuleA = Track(Body("pa", new Vector3(1, 0, 0), 0)).AddComponent<CapsuleCollider2D>();
            var capsuleB = Track(Body("pb", new Vector3(9, 0, 0), 0)).AddComponent<CapsuleCollider2D>();
            var edge = Track(Static("e", Vector3.zero, 0, Vector3.one)).AddComponent<EdgeCollider2D>();
            Physics2D.SyncTransforms();
            ProbeLog.Value("box.hash", Hex(boxA.GetShapeHash()));
            ProbeLog.Value("box.z-differs.same", boxA.GetShapeHash() == boxB.GetShapeHash());
            ProbeLog.Value("polygon.hash", Hex(polygon.GetShapeHash()));
            ProbeLog.Value("box-polygon.same", boxA.GetShapeHash() == polygon.GetShapeHash());
            ProbeLog.Value("body.hash", Hex(bodyA.GetShapeHash()));
            ProbeLog.Value("bodies.same", bodyA.GetShapeHash() == bodyB.GetShapeHash());
            ProbeLog.Value("body-static.same", bodyA.GetShapeHash() == boxA.GetShapeHash());
            ProbeLog.Value("circles.same", circleA.GetShapeHash() == circleB.GetShapeHash());
            ProbeLog.Value("circle-radius.same", circleA.GetShapeHash() == circleC.GetShapeHash());
            uint before = circleA.GetShapeHash();
            circleA.radius = 0.75f;
            Physics2D.SyncTransforms();
            ProbeLog.Value("circle-resized.same", before == circleA.GetShapeHash());
            ProbeLog.Value("circle-resized-other.same", circleA.GetShapeHash() == circleC.GetShapeHash());
            circleA.radius = 0.5f;
            Physics2D.SyncTransforms();
            ProbeLog.Value("circle-restored.same", before == circleA.GetShapeHash());
            ProbeLog.Value("capsules.same", capsuleA.GetShapeHash() == capsuleB.GetShapeHash());
            ProbeLog.Value("capsule-circle.same", capsuleA.GetShapeHash() == circleA.GetShapeHash());
            ProbeLog.Value("edge.hash", Hex(edge.GetShapeHash()));
            uint box = boxA.GetShapeHash();
            boxA.size = new Vector2(1, 1);
            ProbeLog.Value("box-same-size.same", box == boxA.GetShapeHash());
            boxA.size = new Vector2(1, 2);
            Physics2D.SyncTransforms();
            ProbeLog.Value("box-resized.hash", Hex(boxA.GetShapeHash()));
            boxA.size = new Vector2(1, 1);
            Physics2D.SyncTransforms();
            ProbeLog.Value("box-restored.same", box == boxA.GetShapeHash());
        });
        Scenario("tilemap", () =>
        {
            var collider = TilemapWith("grid", new Vector3(1, 0, 2), new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0) });
            Shapes("initial.", collider);
            Geometry("initial.", collider);
            collider.GetComponent<Tilemap>().SetTile(new Vector3Int(3, 3, 0), GridTile());
            collider.ProcessTilemapChanges();
            Shapes("added.", collider);
            Geometry("added.", collider);
            uint added = collider.GetShapeHash();
            collider.GetComponent<Tilemap>().SetTile(new Vector3Int(1, 0, 0), null);
            collider.ProcessTilemapChanges();
            Shapes("removed.", collider);
            Geometry("removed.", collider);
            var fresh = TilemapWith("fresh", new Vector3(1, 0, 2), new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(3, 3, 0) });
            ProbeLog.Value("fresh.hash", Hex(fresh.GetShapeHash()));
            ProbeLog.Value("fresh-added.same", fresh.GetShapeHash() == added);
            collider.enabled = false;
            collider.enabled = true;
            Shapes("recreated.", collider);
            Geometry("recreated.", collider);
        });
        Scenario("tilemap-edits", () =>
        {
            var cells = new List<Vector3Int>();
            for (int x = 0; x < 3; x++) for (int y = 0; y < 2; y++) cells.Add(new Vector3Int(x, y, 0));
            var collider = TilemapWith("grid", Vector3.zero, cells);
            var map = collider.GetComponent<Tilemap>();
            ProbeLog.Value("initial.hash", Hex(collider.GetShapeHash()));
            map.SetTile(new Vector3Int(2, 1, 0), null);
            map.SetTile(new Vector3Int(0, 0, 0), null);
            collider.ProcessTilemapChanges();
            Geometry("removed-two.", collider);
            ProbeLog.Value("removed-two.hash", Hex(collider.GetShapeHash()));
            map.SetTile(new Vector3Int(1, 0, 0), SpriteTile());
            collider.ProcessTilemapChanges();
            Geometry("replaced.", collider);
            ProbeLog.Value("replaced.hash", Hex(collider.GetShapeHash()));
            map.SetTile(new Vector3Int(5, 0, 0), GridTile());
            map.SetTile(new Vector3Int(2, 0, 0), null);
            map.SetTile(new Vector3Int(4, 0, 0), GridTile());
            collider.ProcessTilemapChanges();
            Geometry("mixed.", collider);
            ProbeLog.Value("mixed.hash", Hex(collider.GetShapeHash()));
            map.SetTile(new Vector3Int(4, 0, 0), null);
            collider.ProcessTilemapChanges();
            map.SetTile(new Vector3Int(5, 0, 0), null);
            collider.ProcessTilemapChanges();
            Geometry("two-passes.", collider);
            ProbeLog.Value("two-passes.hash", Hex(collider.GetShapeHash()));
            Physics2D.SyncTransforms();
            ProbeLog.Value("synced.hash", Hex(collider.GetShapeHash()));
            collider.transform.parent.position = new Vector3(0.5f, 0, 0);
            Physics2D.SyncTransforms();
            ProbeLog.Value("moved.hash", Hex(collider.GetShapeHash()));
            Geometry("moved.", collider);
            map.SetTile(new Vector3Int(0, 1, 0), null);
            map.SetTile(new Vector3Int(0, 1, 0), GridTile());
            collider.ProcessTilemapChanges();
            Geometry("same-pass.", collider);
            ProbeLog.Value("same-pass.hash", Hex(collider.GetShapeHash()));
            var twin = ScriptableObject.CreateInstance<Tile>();
            twin.sprite = GridTile().sprite;
            twin.colliderType = Tile.ColliderType.Grid;
            map.SetTile(new Vector3Int(0, 1, 0), twin);
            collider.ProcessTilemapChanges();
            Geometry("twin-tile.", collider);
            ProbeLog.Value("twin-tile.hash", Hex(collider.GetShapeHash()));
            UnityEngine.Object.DestroyImmediate(twin);
            map.SetTile(new Vector3Int(1, 0, 0), null);
            collider.ProcessTilemapChanges();
            map.SetTile(new Vector3Int(1, 0, 0), GridTile());
            collider.ProcessTilemapChanges();
            Geometry("reordered.", collider);
            ProbeLog.Value("reordered.hash", Hex(collider.GetShapeHash()));
            collider.enabled = false;
            collider.enabled = true;
            Geometry("toggled.", collider);
            ProbeLog.Value("toggled.hash", Hex(collider.GetShapeHash()));
        });

        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
