using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;
using static ProbeEntry;

/// <summary>Grid and tilemap scenarios: cell conversions of every layout, tilemap bounds, tile
/// data and flags, the calls tilemaps make into tiles and their events, rule and animated tiles,
/// the scene's painted tilemaps, their colliders and how they render.</summary>
public static class TilemapProbe
{
    static ProbeAssets s_Assets;
    static readonly List<GameObject> s_Objects = new List<GameObject>();

    public static void RunAll()
    {
        s_Assets = UnityEngine.Object.FindFirstObjectByType<ProbeAssets>();
        Scenario("grid.layouts", GridLayouts);
        Scenario("grid.swizzle", GridSwizzle);
        Scenario("tilemap.bounds", Bounds);
        Scenario("tilemap.data", Data);
        Scenario("tilemap.blocks", Blocks);
        Scenario("tilemap.calls", Calls);
        Scenario("tilemap.events", Events);
        Scenario("tilemap.objects", Objects);
        Scenario("tilemap.rule", Rule);
        Scenario("tilemap.animated", Animated);
        Scenario("tilemap.scene", SceneTilemaps);
        Scenario("collider.scene", SceneColliders);
        Scenario("collider.runtime", RuntimeCollider);
        Scenario("collider.composite", RuntimeComposite);
        Scenario("render.scene", RenderScene);
        Scenario("render.changes", RenderChanges);
        Cleanup();
    }

    static void Cleanup()
    {
        foreach (var o in s_Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        s_Objects.Clear();
    }

    static string P(Vector3Int p) => p.x + "," + p.y + "," + p.z;
    static string N(UnityEngine.Object o) => o == null ? "null" : o.name;

    static Tilemap NewTilemap(string name, GridLayout.CellLayout layout = GridLayout.CellLayout.Rectangle)
    {
        var grid = new GameObject(name + "Grid", typeof(Grid));
        grid.GetComponent<Grid>().cellLayout = layout;
        s_Objects.Add(grid);
        var map = new GameObject(name, typeof(Tilemap));
        map.transform.SetParent(grid.transform, false);
        return map.GetComponent<Tilemap>();
    }

    static void Value(string key, Matrix4x4 m)
    {
        for (int r = 0; r < 3; r++)
        for (int c = 0; c < 4; c++)
            ProbeLog.Value(key + ".m" + r + c, m[r, c]);
    }

    static void Value(string key, Color c)
    {
        ProbeLog.Value(key + ".r", c.r);
        ProbeLog.Value(key + ".g", c.g);
        ProbeLog.Value(key + ".b", c.b);
        ProbeLog.Value(key + ".a", c.a);
    }

    static void Value(string key, Vector3Int v) => ProbeLog.Value(key, P(v));

    static void Value(string key, BoundsInt b) => ProbeLog.Value(key, P(b.position) + " size " + P(b.size));

    static void Value(string key, Bounds b)
    {
        ProbeLog.Value(key + ".center", b.center);
        ProbeLog.Value(key + ".size", b.size);
    }

    // ------------------------------------------------------------------ grid

    static void GridLayouts()
    {
        var layouts = new[] { GridLayout.CellLayout.Rectangle, GridLayout.CellLayout.Hexagon, GridLayout.CellLayout.Isometric, GridLayout.CellLayout.IsometricZAsY };
        var go = new GameObject("LayoutGrid", typeof(Grid));
        s_Objects.Add(go);
        go.transform.position = new Vector3(1f, 2f, 0.5f);
        go.transform.rotation = Quaternion.Euler(0, 0, 30);
        go.transform.localScale = new Vector3(2f, 1f, 1f);
        var grid = go.GetComponent<Grid>();
        foreach (var layout in layouts)
        {
            foreach (var (size, gap) in new[] { (new Vector3(1f, 1f, 0f), Vector3.zero), (new Vector3(1.5f, 0.75f, 2f), new Vector3(0.25f, 0.5f, 0f)) })
            {
                grid.cellLayout = layout;
                grid.cellSize = size;
                grid.cellGap = gap;
                string k = layout + "." + size.x + "x" + size.y + (gap.x > 0 ? ".gap" : "");
                ProbeLog.Value(k + ".center", grid.GetLayoutCellCenter());
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    var cell = new Vector3Int(x, y, (x + y) & 1);
                    ProbeLog.Value(k + ".c2l." + P(cell), grid.CellToLocal(cell));
                    ProbeLog.Value(k + ".c2w." + P(cell), grid.CellToWorld(cell));
                    ProbeLog.Value(k + ".center." + P(cell), grid.GetCellCenterLocal(cell));
                }
                Value(k + ".boundsLocal", grid.GetBoundsLocal(new Vector3Int(1, -1, 0)));
                Value(k + ".boundsLocal2", grid.GetBoundsLocal(new Vector3(0.5f, 0.5f, 0f), new Vector3(2f, 3f, 1f)));
                for (float y = -2.1f; y <= 2.2f; y += 0.35f)
                for (float x = -2.1f; x <= 2.2f; x += 0.35f)
                {
                    var local = new Vector3(x, y, 0.3f);
                    string pk = x.ToString("0.00") + "," + y.ToString("0.00");
                    Value(k + ".l2c." + pk, grid.LocalToCell(local));
                    ProbeLog.Value(k + ".l2ci." + pk, grid.LocalToCellInterpolated(local));
                }
                ProbeLog.Value(k + ".c2li", grid.CellToLocalInterpolated(new Vector3(0.5f, -1.25f, 0.5f)));
                Value(k + ".w2c", grid.WorldToCell(new Vector3(2.3f, 3.1f, 0f)));
                ProbeLog.Value(k + ".w2l", grid.WorldToLocal(new Vector3(2.3f, 3.1f, 0f)));
                ProbeLog.Value(k + ".l2w", grid.LocalToWorld(new Vector3(2.3f, 3.1f, 0f)));
            }
        }
    }

    static void GridSwizzle()
    {
        var go = new GameObject("SwizzleGrid", typeof(Grid));
        s_Objects.Add(go);
        var grid = go.GetComponent<Grid>();
        grid.cellSize = new Vector3(1f, 2f, 3f);
        foreach (GridLayout.CellSwizzle swizzle in Enum.GetValues(typeof(GridLayout.CellSwizzle)))
        {
            ProbeLog.Value("swizzle." + swizzle, Grid.Swizzle(swizzle, new Vector3(1f, 2f, 3f)));
            ProbeLog.Value("inverse." + swizzle, Grid.InverseSwizzle(swizzle, new Vector3(1f, 2f, 3f)));
            foreach (var layout in new[] { GridLayout.CellLayout.Rectangle, GridLayout.CellLayout.Hexagon, GridLayout.CellLayout.Isometric })
            {
                grid.cellLayout = layout;
                grid.cellSwizzle = swizzle;
                ProbeLog.Value(layout + "." + swizzle + ".c2l", grid.CellToLocal(new Vector3Int(1, 2, 3)));
                Value(layout + "." + swizzle + ".l2c", grid.LocalToCell(new Vector3(1.2f, 2.7f, 3.4f)));
                ProbeLog.Value(layout + "." + swizzle + ".center", grid.GetLayoutCellCenter());
                Value(layout + "." + swizzle + ".bounds", grid.GetBoundsLocal(new Vector3Int(1, 2, 3)));
            }
        }
    }

    // ------------------------------------------------------------------ tilemap

    static void State(string key, Tilemap map)
    {
        Value(key + ".origin", map.origin);
        Value(key + ".size", map.size);
        Value(key + ".cellBounds", map.cellBounds);
        Value(key + ".localBounds", map.localBounds);
        ProbeLog.Value(key + ".used", map.GetUsedTilesCount());
    }

    static void Bounds()
    {
        var map = NewTilemap("Bounds");
        var red = s_Assets.Tile("red");
        State("new", map);
        map.SetTile(new Vector3Int(2, 3, 0), red);
        State("one", map);
        map.SetTile(new Vector3Int(-1, -2, 0), red);
        State("two", map);
        map.SetTile(new Vector3Int(0, 0, 2), red);
        State("z", map);
        map.SetTile(new Vector3Int(2, 3, 0), null);
        State("removed", map);
        map.CompressBounds();
        State("compressed", map);
        map.origin = new Vector3Int(-5, -5, 0);
        State("originSet", map);
        map.size = new Vector3Int(3, 3, 1);
        State("sizeSet", map);
        map.ResizeBounds();
        State("resized", map);
        map.SetTile(new Vector3Int(10, 0, 0), red);
        State("far", map);
        map.ClearAllTiles();
        State("cleared", map);
        map.tileAnchor = new Vector3(0.25f, 0.75f, 0f);
        map.SetTile(new Vector3Int(1, 1, 0), red);
        State("anchored", map);
        ProbeLog.Value("centerLocal", map.GetCellCenterLocal(new Vector3Int(1, 1, 0)));
        ProbeLog.Value("centerWorld", map.GetCellCenterWorld(new Vector3Int(1, 1, 0)));
        map.transform.parent.position = new Vector3(3f, 1f, 0f);
        ProbeLog.Value("centerWorldMoved", map.GetCellCenterWorld(new Vector3Int(1, 1, 0)));
        Value("layoutGrid", map.layoutGrid != null ? Vector3Int.one : Vector3Int.zero);
        ProbeLog.Value("orientation", map.orientation.ToString());
        Value("orientationMatrix", map.orientationMatrix);
        foreach (Tilemap.Orientation o in Enum.GetValues(typeof(Tilemap.Orientation)))
        {
            if (o == Tilemap.Orientation.Custom) continue;
            map.orientation = o;
            Value("orientationMatrix" + o, map.orientationMatrix);
        }
        map.orientationMatrix = Matrix4x4.Scale(new Vector3(2f, 1f, 1f));
        ProbeLog.Value("orientationCustom", map.orientation.ToString());
        Value("color", map.color);
        ProbeLog.Value("frameRate", map.animationFrameRate);
    }

    static void TileState(string key, Tilemap map, Vector3Int p)
    {
        ProbeLog.Value(key + ".tile", N(map.GetTile(p)));
        ProbeLog.Value(key + ".has", map.HasTile(p));
        ProbeLog.Value(key + ".sprite", N(map.GetSprite(p)));
        Value(key + ".color", map.GetColor(p));
        Value(key + ".matrix", map.GetTransformMatrix(p));
        ProbeLog.Value(key + ".flags", map.GetTileFlags(p).ToString());
        ProbeLog.Value(key + ".collider", map.GetColliderType(p).ToString());
        ProbeLog.Value(key + ".object", N(map.GetObjectToInstantiate(p)));
    }

    static void Data()
    {
        var map = NewTilemap("Data");
        var p = new Vector3Int(1, 2, 0);
        TileState("empty", map, p);
        map.SetTile(p, s_Assets.Tile("red"));
        TileState("red", map, p);
        map.SetColor(p, Color.green);
        TileState("redColorLocked", map, p);
        map.SetTransformMatrix(p, Matrix4x4.Translate(new Vector3(0.5f, 0f, 0f)));
        TileState("redMatrix", map, p);
        map.RemoveTileFlags(p, TileFlags.LockColor);
        TileState("unlocked", map, p);
        map.SetColor(p, Color.green);
        TileState("green", map, p);
        map.RefreshTile(p);
        TileState("refreshed", map, p);
        map.SetTileFlags(p, TileFlags.LockTransform);
        map.SetTransformMatrix(p, Matrix4x4.Scale(new Vector3(2f, 2f, 1f)));
        TileState("lockedTransform", map, p);
        map.AddTileFlags(p, TileFlags.LockColor);
        TileState("added", map, p);
        map.SetColliderType(p, Tile.ColliderType.Sprite);
        TileState("colliderSet", map, p);

        var q = new Vector3Int(-3, 0, 0);
        map.SetTile(q, s_Assets.Tile("white"));
        TileState("white", map, q);
        map.SetTile(q, s_Assets.Tile("rotated"));
        TileState("rotated", map, q);
        map.SetTile(q, s_Assets.Tile("tinted"));
        TileState("tinted", map, q);
        map.SetColor(q, Color.blue);
        TileState("tintedSet", map, q);
        map.SetTile(new TileChangeData(q, s_Assets.Tile("red"), Color.yellow, Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))), false);
        TileState("change", map, q);
        map.SetTile(new TileChangeData(q, s_Assets.Tile("red"), Color.yellow, Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))), true);
        TileState("changeIgnoreLocks", map, q);
        map.SetTile(new TileChangeData(q, s_Assets.Tile("white"), Color.cyan, Matrix4x4.identity), false);
        TileState("changeWhite", map, q);
        map.SetTiles(new[]
        {
            new TileChangeData(new Vector3Int(5, 5, 0), s_Assets.Tile("green"), Color.red, Matrix4x4.identity),
            new TileChangeData(new Vector3Int(6, 5, 0), null, Color.red, Matrix4x4.identity),
        }, true);
        TileState("changes0", map, new Vector3Int(5, 5, 0));
        TileState("changes1", map, new Vector3Int(6, 5, 0));
        ProbeLog.Value("usedTiles", map.GetUsedTilesCount());
        ProbeLog.Value("usedSprites", map.GetUsedSpritesCount());
        var used = new TileBase[map.GetUsedTilesCount()];
        map.GetUsedTilesNonAlloc(used);
        Array.Sort(used, (a, b) => string.CompareOrdinal(a.name, b.name));
        ProbeLog.Value("usedNames", string.Join(" ", Array.ConvertAll(used, N)));
        var sprites = new Sprite[map.GetUsedSpritesCount()];
        map.GetUsedSpritesNonAlloc(sprites);
        Array.Sort(sprites, (a, b) => string.CompareOrdinal(a.name, b.name));
        ProbeLog.Value("usedSpriteNames", string.Join(" ", Array.ConvertAll(sprites, N)));
        ProbeLog.Value("containsRed", map.ContainsTile(s_Assets.Tile("red")));
        ProbeLog.Value("containsBlue", map.ContainsTile(s_Assets.Tile("blue")));
        map.SwapTile(s_Assets.Tile("red"), s_Assets.Tile("blue"));
        TileState("swapped", map, p);
        ProbeLog.Value("containsRedAfter", map.ContainsTile(s_Assets.Tile("red")));
    }

    static string Names(TileBase[] tiles) => string.Join(" ", Array.ConvertAll(tiles, N));

    static void Blocks()
    {
        var map = NewTilemap("Blocks");
        var red = s_Assets.Tile("red");
        var green = s_Assets.Tile("green");
        var blue = s_Assets.Tile("blue");
        map.SetTilesBlock(new BoundsInt(-1, -1, 0, 3, 2, 1), new TileBase[] { red, green, blue, null, red, green });
        State("block", map);
        ProbeLog.Value("get", Names(map.GetTilesBlock(new BoundsInt(-2, -2, 0, 4, 4, 1))));
        var buffer = new TileBase[20];
        ProbeLog.Value("nonAlloc", map.GetTilesBlockNonAlloc(new BoundsInt(-1, -1, 0, 3, 2, 1), buffer));
        ProbeLog.Value("nonAllocNames", Names(buffer));
        ProbeLog.Value("rangeCount", map.GetTilesRangeCount(new Vector3Int(-2, -2, 0), new Vector3Int(2, 2, 0)));
        var positions = new Vector3Int[10];
        var tiles = new TileBase[10];
        int n = map.GetTilesRangeNonAlloc(new Vector3Int(-2, -2, 0), new Vector3Int(2, 2, 0), positions, tiles);
        ProbeLog.Value("range", n);
        for (int i = 0; i < n; i++) ProbeLog.Value("range." + i, P(positions[i]) + " " + N(tiles[i]));
        map.SetTiles(new[] { new Vector3Int(4, 0, 0), new Vector3Int(4, 1, 0), new Vector3Int(4, 0, 0) }, new TileBase[] { red, blue, green });
        ProbeLog.Value("setTiles", N(map.GetTile(new Vector3Int(4, 0, 0))) + " " + N(map.GetTile(new Vector3Int(4, 1, 0))));
        foreach (var (position, sx, sy, ex, ey) in new[] { (new Vector3Int(0, 0, 0), 0, 0, 2, 2), (new Vector3Int(1, 1, 0), 0, 0, 2, 2), (new Vector3Int(5, 5, 0), 0, 0, 2, 2), (new Vector3Int(0, 0, 0), 2, 2, 0, 0), (new Vector3Int(0, 1, 0), -1, -1, 1, 3) })
        {
            var box = NewTilemap("Box");
            box.SetTile(new Vector3Int(1, 0, 0), red);
            box.BoxFill(position, green, sx, sy, ex, ey);
            ProbeLog.Value($"box.{P(position)}.{sx},{sy},{ex},{ey}", P(box.cellBounds.position) + " size " + P(box.cellBounds.size) + ": " + Names(box.GetTilesBlock(new BoundsInt(-2, -2, 0, 8, 8, 1))));
        }
        map.BoxFill(new Vector3Int(0, 3, 0), blue, -1, 2, 2, 4);
        State("boxFill", map);
        ProbeLog.Value("boxFillTiles", Names(map.GetTilesBlock(new BoundsInt(-2, 1, 0, 6, 5, 1))));
        var flood = NewTilemap("Flood");
        for (int x = 0; x < 5; x++) { flood.SetTile(new Vector3Int(x, 0, 0), red); flood.SetTile(new Vector3Int(x, 4, 0), red); }
        for (int y = 0; y < 5; y++) { flood.SetTile(new Vector3Int(0, y, 0), red); flood.SetTile(new Vector3Int(4, y, 0), red); }
        flood.FloodFill(new Vector3Int(2, 2, 0), green);
        State("flood", flood);
        ProbeLog.Value("floodTiles", Names(flood.GetTilesBlock(new BoundsInt(-1, -1, 0, 7, 7, 1))));
        flood.FloodFill(new Vector3Int(0, 0, 0), blue);
        ProbeLog.Value("floodBorder", Names(flood.GetTilesBlock(new BoundsInt(-1, -1, 0, 7, 7, 1))));
        flood.InsertCells(new Vector3Int(2, 2, 0), 1, 0, 0);
        State("inserted", flood);
        ProbeLog.Value("insertedTiles", Names(flood.GetTilesBlock(flood.cellBounds)));
        flood.DeleteCells(new Vector3Int(1, 1, 0), 0, 2, 0);
        State("deleted", flood);
        ProbeLog.Value("deletedTiles", Names(flood.GetTilesBlock(flood.cellBounds)));
    }

    static LoggingTile Logging(string name, Sprite sprite, bool neighbours = false)
    {
        var t = ScriptableObject.CreateInstance<LoggingTile>();
        t.name = name;
        t.sprite = sprite;
        t.refreshNeighbours = neighbours;
        return t;
    }

    static void Flush(string key)
    {
        for (int i = 0; i < LoggingTile.Log.Count; i++) ProbeLog.Value(key + "." + i, LoggingTile.Log[i]);
        ProbeLog.Value(key + ".count", LoggingTile.Log.Count);
        LoggingTile.Log.Clear();
    }

    static void Calls()
    {
        var map = NewTilemap("Calls");
        var a = Logging("A", s_Assets.sprites[0]);
        var b = Logging("B", s_Assets.sprites[1], neighbours: true);
        LoggingTile.Log.Clear();
        map.SetTile(new Vector3Int(0, 0, 0), a);
        Flush("setA");
        map.SetTile(new Vector3Int(0, 0, 0), a);
        Flush("setASame");
        map.SetTile(new Vector3Int(1, 0, 0), b);
        Flush("setB");
        map.SetTile(new Vector3Int(0, 0, 0), b);
        Flush("replaceAB");
        map.SetTile(new Vector3Int(1, 0, 0), null);
        Flush("remove");
        map.RefreshTile(new Vector3Int(0, 0, 0));
        Flush("refresh");
        map.RefreshTile(new Vector3Int(5, 5, 0));
        Flush("refreshEmpty");
        map.SetTiles(new[] { new Vector3Int(3, 0, 0), new Vector3Int(4, 0, 0) }, new TileBase[] { a, b });
        Flush("setTiles");
        map.RefreshAllTiles();
        Flush("refreshAll");
        map.SetColor(new Vector3Int(3, 0, 0), Color.red);
        Flush("setColor");
        ProbeLog.Value("sprite", N(map.GetSprite(new Vector3Int(0, 0, 0))));
        map.SetTilesBlock(new BoundsInt(0, 2, 0, 2, 1, 1), new TileBase[] { a, a });
        Flush("block");
        map.BoxFill(new Vector3Int(0, 5, 0), a, 0, 5, 1, 5);
        Flush("boxFill");
        map.SwapTile(a, b);
        Flush("swap");
        map.ClearAllTiles();
        Flush("clear");
        UnityEngine.Object.DestroyImmediate(a);
        UnityEngine.Object.DestroyImmediate(b);
    }

    static void Events()
    {
        var map = NewTilemap("Events");
        var log = new List<string>();
        Action<Tilemap, Tilemap.SyncTile[]> changed = (m, tiles) =>
        {
            var b = new StringBuilder("changed " + m.name + ":");
            foreach (var t in tiles) b.Append(' ').Append(P(t.position)).Append('=').Append(N(t.tile)).Append('/').Append(N(t.tileData.sprite)).Append('/').Append(t.tileData.color);
            log.Add(b.ToString());
        };
        Action<Tilemap, NativeArray<Vector3Int>> positions = (m, ps) =>
        {
            var b = new StringBuilder("positions " + m.name + ":");
            foreach (var p in ps) b.Append(' ').Append(P(p));
            log.Add(b.ToString());
        };
        Tilemap.tilemapTileChanged += changed;
        Tilemap.tilemapPositionsChanged += positions;
        try
        {
            void Step(string key, Action action)
            {
                log.Clear();
                action();
                for (int i = 0; i < log.Count; i++) ProbeLog.Value(key + "." + i, log[i]);
                ProbeLog.Value(key + ".count", log.Count);
            }
            var red = s_Assets.Tile("red");
            var white = s_Assets.Tile("white");
            Step("set", () => map.SetTile(new Vector3Int(0, 0, 0), red));
            Step("setSame", () => map.SetTile(new Vector3Int(0, 0, 0), red));
            Step("setTiles", () => map.SetTiles(new[] { new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0) }, new TileBase[] { white, red }));
            Step("color", () => map.SetColor(new Vector3Int(1, 0, 0), Color.red));
            Step("colorLocked", () => map.SetColor(new Vector3Int(0, 0, 0), Color.red));
            Step("matrix", () => map.SetTransformMatrix(new Vector3Int(1, 0, 0), Matrix4x4.Scale(Vector3.one * 2)));
            Step("flags", () => map.SetTileFlags(new Vector3Int(1, 0, 0), TileFlags.None));
            Step("refresh", () => map.RefreshTile(new Vector3Int(0, 0, 0)));
            Step("remove", () => map.SetTile(new Vector3Int(2, 0, 0), null));
            Step("removeEmpty", () => map.SetTile(new Vector3Int(9, 0, 0), null));
            Step("block", () => map.SetTilesBlock(new BoundsInt(0, 1, 0, 2, 1, 1), new TileBase[] { red, null }));
            Step("clear", () => map.ClearAllTiles());
        }
        finally
        {
            Tilemap.tilemapTileChanged -= changed;
            Tilemap.tilemapPositionsChanged -= positions;
        }
    }

    static void Objects()
    {
        var map = NewTilemap("Objects");
        map.transform.parent.position = new Vector3(10f, 0f, 0f);
        var tile = s_Assets.Tile("withObject");
        map.SetTile(new Vector3Int(1, 2, 0), tile);
        var go = map.GetInstantiatedObject(new Vector3Int(1, 2, 0));
        ProbeLog.Value("instantiated", N(go));
        if (go != null)
        {
            ProbeLog.Value("parent", N(go.transform.parent));
            ProbeLog.Value("local", go.transform.localPosition);
            ProbeLog.Value("world", go.transform.position);
            ProbeLog.Value("active", go.activeSelf);
            ProbeLog.Value("children", map.transform.childCount);
        }
        ProbeLog.Value("toInstantiate", N(map.GetObjectToInstantiate(new Vector3Int(1, 2, 0))));
        var logging = Logging("L", s_Assets.sprites[0]);
        logging.instanced = s_Assets.tilePrefab;
        LoggingTile.Log.Clear();
        map.SetTile(new Vector3Int(3, 0, 0), logging);
        Flush("logging");
        var lgo = map.GetInstantiatedObject(new Vector3Int(3, 0, 0));
        ProbeLog.Value("loggingObject", N(lgo));
        if (lgo != null) ProbeLog.Value("loggingLocal", lgo.transform.localPosition);
        map.SetTransformMatrix(new Vector3Int(3, 0, 0), Matrix4x4.Translate(Vector3.up));
        if (lgo != null) ProbeLog.Value("loggingLocalMoved", lgo.transform.localPosition);
        map.SetTile(new Vector3Int(1, 2, 0), null);
        ProbeLog.Value("removedAlive", go != null);
        ProbeLog.Value("childrenAfterRemove", map.transform.childCount);
        map.SetTile(new Vector3Int(3, 0, 0), s_Assets.Tile("red"));
        Flush("replaced");
        ProbeLog.Value("loggingAlive", lgo != null);
    }

    static void Rule()
    {
        var map = NewTilemap("Rule");
        var rule = s_Assets.rule;
        var cells = new List<Vector3Int>();
        for (int x = 0; x < 4; x++) cells.Add(new Vector3Int(x, 0, 0));
        for (int y = 1; y < 4; y++) cells.Add(new Vector3Int(1, y, 0));
        cells.Add(new Vector3Int(6, 6, 0));
        foreach (var c in cells) map.SetTile(c, rule);
        void Record(string key)
        {
            foreach (var c in cells)
            {
                if (!map.HasTile(c)) continue;
                ProbeLog.Value(key + "." + P(c) + ".sprite", N(map.GetSprite(c)));
                Value(key + "." + P(c) + ".matrix", map.GetTransformMatrix(c));
                ProbeLog.Value(key + "." + P(c) + ".collider", map.GetColliderType(c).ToString());
            }
        }
        Record("painted");
        map.SetTile(new Vector3Int(1, 2, 0), null);
        Record("removed");
        map.SetTile(new Vector3Int(1, 2, 0), rule);
        Record("restored");
    }

    static void Animated()
    {
        var map = NewTilemap("Animated");
        var p = new Vector3Int(0, 0, 0);
        map.SetTile(p, s_Assets.animated);
        ProbeLog.Value("frameCount", map.GetAnimationFrameCount(p));
        ProbeLog.Value("frame", map.GetAnimationFrame(p));
        ProbeLog.Value("sprite", N(map.GetSprite(p)));
        ProbeLog.Value("flags", map.GetTileAnimationFlags(p).ToString());
        map.SetAnimationFrame(p, 2);
        ProbeLog.Value("frameSet", map.GetAnimationFrame(p));
        ProbeLog.Value("spriteSet", N(map.GetSprite(p)));
        map.SetAnimationTime(p, 0.75f);
        ProbeLog.Value("timeSet", map.GetAnimationTime(p));
        ProbeLog.Value("frameAfterTime", map.GetAnimationFrame(p));
        map.AddTileAnimationFlags(p, TileAnimationFlags.PauseAnimation | TileAnimationFlags.LoopOnce);
        ProbeLog.Value("flagsAdded", map.GetTileAnimationFlags(p).ToString());
        map.RemoveTileAnimationFlags(p, TileAnimationFlags.LoopOnce);
        ProbeLog.Value("flagsRemoved", map.GetTileAnimationFlags(p).ToString());
        ProbeLog.Value("plainFrameCount", map.GetAnimationFrameCount(new Vector3Int(5, 5, 0)));
        map.SetTile(new Vector3Int(1, 0, 0), s_Assets.Tile("red"));
        ProbeLog.Value("redFrameCount", map.GetAnimationFrameCount(new Vector3Int(1, 0, 0)));
    }

    // ------------------------------------------------------------------ scene

    static void SceneTilemaps()
    {
        foreach (var map in UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (map.gameObject.scene.name != "Probe" || s_Objects.Contains(map.transform.parent.gameObject)) continue;
        }
        foreach (var name in new[] { "Ground", "Rules", "Tint", "Hex", "Iso" })
        {
            var map = GameObject.Find(name).GetComponent<Tilemap>();
            State(name, map);
            Value(name + ".color", map.color);
            ProbeLog.Value(name + ".anchor", map.tileAnchor);
            ProbeLog.Value(name + ".orientation", map.orientation.ToString());
            var renderer = map.GetComponent<TilemapRenderer>();
            ProbeLog.Value(name + ".mode", renderer.mode.ToString());
            ProbeLog.Value(name + ".sortOrder", renderer.sortOrder.ToString());
            Value(name + ".chunkSize", renderer.chunkSize);
            ProbeLog.Value(name + ".sortingOrder", renderer.sortingOrder);
            ProbeLog.Value(name + ".material", N(renderer.sharedMaterial));
            Value(name + ".bounds", renderer.bounds);
            Value(name + ".localBounds", renderer.localBounds);
            var b = map.cellBounds;
            foreach (var p in b.allPositionsWithin)
            {
                if (!map.HasTile(p)) continue;
                ProbeLog.Value(name + "." + P(p), N(map.GetTile(p)) + " " + N(map.GetSprite(p)) + " " + map.GetColor(p) + " " + map.GetTileFlags(p) + " " + map.GetColliderType(p));
            }
            ProbeLog.Value(name + ".children", map.transform.childCount);
            for (int i = 0; i < map.transform.childCount; i++)
            {
                var child = map.transform.GetChild(i);
                ProbeLog.Value(name + ".child" + i, child.name + " " + child.localPosition);
            }
        }
    }

    static void Shapes(string key, Collider2D collider)
    {
        ProbeLog.Value(key + ".shapeCount", collider.shapeCount);
        Value(key + ".bounds", collider.bounds);
        ProbeLog.Value(key + ".errorState", collider.errorState.ToString());
        var group = new PhysicsShapeGroup2D();
        int n = collider.GetShapes(group);
        ProbeLog.Value(key + ".shapes", n);
        var vertices = new List<Vector2>();
        var lines = new List<string>();
        for (int i = 0; i < group.shapeCount; i++)
        {
            var shape = group.GetShape(i);
            group.GetShapeVertices(i, vertices);
            var b = new StringBuilder(shape.shapeType + " r" + shape.radius.ToString("0.####"));
            foreach (var v in vertices) b.Append(' ').Append(v.x.ToString("0.####")).Append(',').Append(v.y.ToString("0.####"));
            lines.Add(b.ToString());
        }
        for (int i = 0; i < lines.Count; i++) ProbeLog.Value(key + ".shape" + i, lines[i]);
        lines.Sort(string.CompareOrdinal);
        for (int i = 0; i < lines.Count; i++) ProbeLog.Value(key + ".sorted" + i, lines[i]);
    }

    static void Paths(string key, CompositeCollider2D composite)
    {
        ProbeLog.Value(key + ".pathCount", composite.pathCount);
        ProbeLog.Value(key + ".pointCount", composite.pointCount);
        var points = new List<Vector2>();
        for (int i = 0; i < composite.pathCount; i++)
        {
            composite.GetPath(i, points);
            var b = new StringBuilder();
            foreach (var v in points) b.Append(v.x.ToString("0.####")).Append(',').Append(v.y.ToString("0.####")).Append(' ');
            ProbeLog.Value(key + ".path" + i, b.ToString().TrimEnd());
        }
    }

    static void Overlaps(string key, Rect area, float step)
    {
        for (float y = area.yMax; y >= area.yMin; y -= step)
        {
            var row = new StringBuilder();
            for (float x = area.xMin; x <= area.xMax; x += step)
            {
                var hits = Physics2D.OverlapPointAll(new Vector2(x, y));
                char c = '.';
                foreach (var h in hits)
                {
                    char hc = h is CompositeCollider2D ? 'C' : h.name[0];
                    c = c == '.' ? hc : '*';
                }
                row.Append(c);
            }
            ProbeLog.Value(key + "." + y.ToString("0.00"), row.ToString());
        }
    }

    static void SceneColliders()
    {
        var ground = GameObject.Find("Ground");
        Shapes("groundTiles", ground.GetComponent<TilemapCollider2D>());
        var composite = ground.GetComponent<CompositeCollider2D>();
        Shapes("composite", composite);
        Paths("composite", composite);
        ProbeLog.Value("composite.geometry", composite.geometryType.ToString());
        ProbeLog.Value("composite.generation", composite.generationType.ToString());
        ProbeLog.Value("composite.vertexDistance", composite.vertexDistance);
        ProbeLog.Value("composite.offsetDistance", composite.offsetDistance);
        ProbeLog.Value("composite.edgeRadius", composite.edgeRadius);
        Shapes("rules", GameObject.Find("Rules").GetComponent<TilemapCollider2D>());
        Overlaps("overlap", new Rect(-7f, -4f, 14f, 8f), 0.25f);
        for (float x = -6.5f; x <= 6f; x += 0.5f)
        {
            var hit = Physics2D.Raycast(new Vector2(x, 5f), Vector2.down, 20f);
            ProbeLog.Value("ray." + x.ToString("0.0"), hit.collider == null ? "none" : N(hit.collider) + " " + hit.point.y.ToString("0.####") + " " + hit.normal.x.ToString("0.###") + "," + hit.normal.y.ToString("0.###"));
        }
    }

    static void RuntimeCollider()
    {
        var map = NewTilemap("RuntimeCollider");
        map.transform.parent.position = new Vector3(20f, 0f, 0f);
        var collider = map.gameObject.AddComponent<TilemapCollider2D>();
        Shapes("empty", collider);
        map.SetTile(new Vector3Int(0, 0, 0), s_Assets.Tile("red"));
        ProbeLog.Value("changes", collider.hasTilemapChanges);
        ProbeLog.Value("countBeforeProcess", collider.shapeCount);
        collider.ProcessTilemapChanges();
        ProbeLog.Value("changesAfter", collider.hasTilemapChanges);
        Shapes("one", collider);
        map.SetTile(new Vector3Int(1, 0, 0), s_Assets.Tile("green"));
        map.SetTile(new Vector3Int(2, 0, 0), s_Assets.Tile("tri"));
        map.SetTile(new Vector3Int(3, 0, 0), s_Assets.Tile("blue"));
        map.SetTile(new Vector3Int(4, 0, 0), s_Assets.Tile("rotated"));
        map.SetTile(new Vector3Int(5, 0, 0), s_Assets.Tile("ell"));
        Physics2D.Simulate(0.02f);
        ProbeLog.Value("changesAfterStep", collider.hasTilemapChanges);
        Shapes("several", collider);
        map.SetTransformMatrix(new Vector3Int(5, 0, 0), Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one));
        map.SetColliderType(new Vector3Int(1, 0, 0), Tile.ColliderType.Grid);
        collider.ProcessTilemapChanges();
        Shapes("changed", collider);
        collider.offset = new Vector2(0.5f, 0.25f);
        collider.extrusionFactor = 0.1f;
        ProbeLog.Value("extrusion", collider.extrusionFactor);
        ProbeLog.Value("maxChanges", (int)collider.maximumTileChangeCount);
        ProbeLog.Value("delaunay", collider.useDelaunayMesh);
        Shapes("offset", collider);
        map.transform.parent.GetComponent<Grid>().cellSize = new Vector3(2f, 1f, 0f);
        collider.ProcessTilemapChanges();
        Shapes("cellSize", collider);
    }

    static void RuntimeComposite()
    {
        var map = NewTilemap("RuntimeComposite");
        map.transform.parent.position = new Vector3(30f, 0f, 0f);
        var body = map.gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        var collider = map.gameObject.AddComponent<TilemapCollider2D>();
        collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        var composite = map.gameObject.AddComponent<CompositeCollider2D>();
        var red = s_Assets.Tile("red");
        for (int x = 0; x < 4; x++) for (int y = 0; y < 3; y++) map.SetTile(new Vector3Int(x, y, 0), red);
        map.SetTile(new Vector3Int(1, 1, 0), null);
        map.SetTile(new Vector3Int(6, 0, 0), s_Assets.Tile("tri"));
        map.SetTile(new Vector3Int(7, 0, 0), red);
        collider.ProcessTilemapChanges();
        Shapes("outlines", composite);
        Paths("outlines", composite);
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        Shapes("polygons", composite);
        Paths("polygons", composite);
        composite.offsetDistance = 0.1f;
        composite.GenerateGeometry();
        Paths("offset", composite);
        ProbeLog.Value("bodyShapes", body.attachedColliderCount);
    }

    // ------------------------------------------------------------------ rendering

    static Camera s_Camera;

    static Camera ProbeCamera()
    {
        if (s_Camera != null) return s_Camera;
        var go = new GameObject("ProbeCamera");
        s_Objects.Add(go);
        go.transform.position = new Vector3(0f, 0f, -10f);
        s_Camera = go.AddComponent<Camera>();
        s_Camera.enabled = false;
        s_Camera.orthographic = true;
        s_Camera.orthographicSize = 5f;
        s_Camera.clearFlags = CameraClearFlags.SolidColor;
        s_Camera.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
        return s_Camera;
    }

    public static void Image(string key, Camera camera, int width, int height)
    {
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = rt;
        camera.Render();
        camera.targetTexture = null;
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        var pixels = texture.GetPixels32();
        var b = new StringBuilder(width + "x" + height + ":");
        foreach (var p in pixels) b.Append(p.r.ToString("x2")).Append(p.g.ToString("x2")).Append(p.b.ToString("x2"));
        ProbeLog.Value(key + ".image", b.ToString());
        UnityEngine.Object.DestroyImmediate(texture);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
    }

    static void PauseAnimations()
    {
        var ground = GameObject.Find("Ground").GetComponent<Tilemap>();
        var p = new Vector3Int(-3, -1, 0);
        ground.SetTileAnimationFlags(p, TileAnimationFlags.PauseAnimation);
        ground.SetAnimationFrame(p, 1);
    }

    static void RenderScene()
    {
        // Hide the runtime scenarios' objects.
        foreach (var o in s_Objects) if (o) o.SetActive(false);
        PauseAnimations();
        var camera = ProbeCamera();
        camera.gameObject.SetActive(true);
        Image("main", camera, 160, 160);
        camera.transform.position = new Vector3(3.5f, 1f, -10f);
        camera.orthographicSize = 2.5f;
        Image("zoomed", camera, 160, 160);
        camera.orthographicSize = 5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
    }

    static void RenderChanges()
    {
        PauseAnimations();
        var camera = ProbeCamera();
        camera.gameObject.SetActive(true);
        var ground = GameObject.Find("Ground").GetComponent<Tilemap>();
        var rules = GameObject.Find("Rules").GetComponent<Tilemap>();
        var tint = GameObject.Find("Tint").GetComponent<Tilemap>();
        ground.SetTile(new Vector3Int(-5, 2, 0), s_Assets.Tile("white"));
        ground.SetColor(new Vector3Int(-5, 2, 0), new Color(0.2f, 1f, 0.2f, 0.5f));
        ground.SetTransformMatrix(new Vector3Int(-5, 2, 0), Matrix4x4.TRS(new Vector3(0.25f, 0f, 0f), Quaternion.Euler(0, 0, 45), new Vector3(0.75f, 0.75f, 1f)));
        ground.SetTile(new Vector3Int(0, -3, 0), null);
        rules.SetTile(new Vector3Int(2, 4, 0), s_Assets.rule);
        rules.SetTile(new Vector3Int(-1, 3, 0), null);
        tint.color = new Color(1f, 1f, 1f, 0.5f);
        tint.orientation = Tilemap.Orientation.YX;
        GameObject.Find("Grid").GetComponent<Grid>().cellGap = new Vector3(0.0625f, 0f, 0f);
        Image("changed", camera, 160, 160);
        var renderer = rules.GetComponent<TilemapRenderer>();
        renderer.sortingOrder = -1;
        ground.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Individual;
        Image("resorted", camera, 160, 160);
    }
}
