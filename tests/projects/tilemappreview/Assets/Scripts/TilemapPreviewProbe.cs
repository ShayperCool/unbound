using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>A tile that logs callbacks made by Tilemap during normal and editor-preview operations.</summary>
class PreviewLogTile : Tile
{
    public readonly List<string> log = new List<string>();
    public bool refreshRightNeighbour;

    public override void RefreshTile(Vector3Int position, ITilemap tilemap)
    {
        var seen = tilemap.GetTile(position);
        log.Add($"Refresh({position.x},{position.y},{position.z})[it={TileName(seen)}]");
        base.RefreshTile(position, tilemap);
        if (refreshRightNeighbour)
            tilemap.RefreshTile(position + Vector3Int.right);
    }

    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        var seen = tilemap.GetTile(position);
        log.Add($"Data({position.x},{position.y},{position.z})[it={TileName(seen)}]");
        base.GetTileData(position, tilemap, ref tileData);
    }

    public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go)
    {
        log.Add($"StartUp({position.x},{position.y},{position.z})[go={(go != null ? go.name : "null")}]");
        return base.StartUp(position, tilemap, go);
    }

    public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData)
    {
        log.Add($"Anim({position.x},{position.y},{position.z})");
        return base.GetTileAnimationData(position, tilemap, ref tileAnimationData);
    }

    static string TileName(TileBase t) => t == null ? "null" : t.name;
}

/// <summary>
/// Records, in Play mode, what the Unity 6000.0 Editor answers for Tilemap editor-preview APIs:
/// SetEditorPreviewTile, SetEditorPreviewColor, SetEditorPreviewTransformMatrix,
/// EditorPreviewBoxFill, EditorPreviewFloodFill, ClearAllEditorPreviewTiles, all preview getters,
/// editorPreviewOrigin/editorPreviewSize, lock flags, callbacks, events, and bounds interactions.
/// </summary>
static class TilemapPreviewProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("TILEMAP_PREVIEW_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);

        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key))
            .Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (Exception e) { Put(key, "throws " + e.GetType().Name); }
        }

        int tileChangedEvents = 0;
        int posChangedEvents = 0;
        Action<Tilemap, Tilemap.SyncTile[]> onTileChanged = (_, _) => tileChangedEvents++;
        Action<Tilemap, NativeArray<Vector3Int>> onPosChanged = (_, _) => posChangedEvents++;
        Tilemap.tilemapTileChanged += onTileChanged;
        Tilemap.tilemapPositionsChanged += onPosChanged;

        var spriteA = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Single.png");
        var sheetSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Sheet.png").OfType<Sprite>()
            .OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
        var spriteB = sheetSprites.Length > 0 ? sheetSprites[0] : spriteA;
        var spriteC = sheetSprites.Length > 1 ? sheetSprites[1] : spriteB;
        var dummyPrefab = new GameObject("TileChildPrefab");
        dummyPrefab.SetActive(false);

        var m1 = Matrix4x4.TRS(new Vector3(0.25f, -0.5f, 0f), Quaternion.identity, new Vector3(2f, 3f, 1f));
        var m2 = Matrix4x4.TRS(new Vector3(1f, 2f, 0f), Quaternion.identity, new Vector3(0.5f, 0.5f, 1f));

        Tile MakeTile(string name, Sprite s, Color c, Matrix4x4 m, TileFlags flags)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.name = name;
            t.sprite = s;
            t.color = c;
            t.transform = m;
            t.flags = flags;
            return t;
        }

        PreviewLogTile MakeLogTile(string name, Sprite s, Color c, Matrix4x4 m, TileFlags flags, GameObject go = null)
        {
            var t = ScriptableObject.CreateInstance<PreviewLogTile>();
            t.name = name;
            t.sprite = s;
            t.color = c;
            t.transform = m;
            t.flags = flags;
            t.gameObject = go;
            return t;
        }

        Tilemap NewMap(string name)
        {
            var gridGo = new GameObject(name + "_Grid", typeof(Grid));
            var mapGo = new GameObject(name, typeof(Tilemap));
            mapGo.transform.SetParent(gridGo.transform, false);
            return mapGo.GetComponent<Tilemap>();
        }

        string BoundsSummary(Tilemap map) =>
            $"orig={V3(map.origin)}|size={V3(map.size)}|pOrig={V3(map.editorPreviewOrigin)}|pSize={V3(map.editorPreviewSize)}";

        string PreviewCell(Tilemap map, Vector3Int p) =>
            $"has={map.HasEditorPreviewTile(p)}|tile={TName(map.GetEditorPreviewTile(p))}|typed={TName(map.GetEditorPreviewTile<Tile>(p))}|sprite={OName(map.GetEditorPreviewSprite(p))}|color={Col(map.GetEditorPreviewColor(p))}|mat={Mat(map.GetEditorPreviewTransformMatrix(p))}|flags={map.GetEditorPreviewTileFlags(p)}";

        string ActualCell(Tilemap map, Vector3Int p) =>
            $"has={map.HasTile(p)}|tile={TName(map.GetTile(p))}|sprite={OName(map.GetSprite(p))}|color={Col(map.GetColor(p))}|mat={Mat(map.GetTransformMatrix(p))}|flags={map.GetTileFlags(p)}";

        string ScanPreview(Tilemap map, int x0, int x1, int y0, int y1, int z = 0)
        {
            var items = new List<string>();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector3Int(x, y, z);
                    if (map.HasEditorPreviewTile(p))
                        items.Add($"({x},{y},{z})={TName(map.GetEditorPreviewTile(p))}");
                }
            return items.Count == 0 ? "<none>" : string.Join(",", items);
        }

        var tileA = MakeTile("A", spriteA, new Color(1f, 0f, 0f, 1f), m1, TileFlags.LockColor);
        var tileWall = MakeTile("Wall", spriteC, Color.black, Matrix4x4.identity, TileFlags.LockAll);
        var tileFree = MakeTile("Free", spriteB, new Color(0f, 1f, 0f, 1f), m1, TileFlags.None);
        var tileLockColor = MakeTile("LockColor", spriteB, new Color(0f, 0f, 1f, 1f), Matrix4x4.identity, TileFlags.LockColor);
        var tileLockTransform = MakeTile("LockTransform", spriteB, new Color(1f, 1f, 0f, 1f), m1, TileFlags.LockTransform);
        var tileLockAll = MakeTile("LockAll", spriteB, new Color(0f, 1f, 1f, 1f), m1, TileFlags.LockAll);
        var tilePreview = MakeTile("Prev", spriteB, new Color(0.25f, 0.5f, 0.75f, 1f), Matrix4x4.identity, TileFlags.None);

        // 1. Fresh defaults
        Check("01.fresh", () =>
        {
            var map = NewMap("Fresh");
            return BoundsSummary(map) + "|" + PreviewCell(map, Vector3Int.zero) + "|far=" + PreviewCell(map, new Vector3Int(3, 4, 0));
        });

        // 2. SetEditorPreviewTile: separation from actual tiles, bounds growth, callbacks, child instantiation, events, overlay, replace, null clear
        Check("02.setPreview.basic", () =>
        {
            var map = NewMap("Basic");
            map.SetTile(new Vector3Int(1, 1, 0), tileA);
            string afterActual = BoundsSummary(map);

            var logTile = MakeLogTile("LogPrev", spriteB, new Color(0.2f, 0.4f, 0.6f, 1f), m1, TileFlags.None, dummyPrefab);
            logTile.refreshRightNeighbour = true;
            var logNeighbor = MakeLogTile("LogNbr", spriteC, Color.white, Matrix4x4.identity, TileFlags.None);
            map.SetTile(new Vector3Int(3, 3, 0), logNeighbor);
            logNeighbor.log.Clear();

            int evTile0 = tileChangedEvents, evPos0 = posChangedEvents;
            map.SetEditorPreviewTile(new Vector3Int(2, 3, 0), logTile);
            int evTile1 = tileChangedEvents - evTile0, evPos1 = posChangedEvents - evPos0;

            return $"afterActual=[{afterActual}]|afterPrev=[{BoundsSummary(map)}]|prev=[{PreviewCell(map, new Vector3Int(2, 3, 0))}]|actualAtPrev=[{ActualCell(map, new Vector3Int(2, 3, 0))}]|usedCount={map.GetUsedTilesCount()}|children={map.transform.childCount}|ev={evTile1},{evPos1}|logPrev={string.Join(";", logTile.log)}|logNbr={string.Join(";", logNeighbor.log)}";
        });

        Check("02.setPreview.overlayAndReplace", () =>
        {
            var map = NewMap("Overlay");
            var p = new Vector3Int(1, 1, 0);
            map.SetTile(p, tileA);
            var log1 = MakeLogTile("Log1", spriteB, new Color(0.1f, 0.2f, 0.3f, 1f), m1, TileFlags.None);
            var log2 = MakeLogTile("Log2", spriteC, new Color(0.4f, 0.5f, 0.6f, 1f), m2, TileFlags.LockColor);

            map.SetEditorPreviewTile(p, log1);
            map.SetEditorPreviewColor(p, new Color(0.9f, 0.8f, 0.7f, 1f));
            map.SetEditorPreviewTransformMatrix(p, m2);
            string afterFirst = $"actual=[{ActualCell(map, p)}]|prev=[{PreviewCell(map, p)}]";

            log1.log.Clear();
            map.SetEditorPreviewTile(p, log1); // repeat same tile
            string repeatLog = string.Join(";", log1.log);

            log1.log.Clear();
            map.SetEditorPreviewTile(p, log2); // replace with different tile
            string afterReplace = $"prev=[{PreviewCell(map, p)}]|actual=[{ActualCell(map, p)}]|log1={string.Join(";", log1.log)}|log2={string.Join(";", log2.log)}";

            // Negative & nonzero z
            map.SetEditorPreviewTile(new Vector3Int(-2, -1, 2), tilePreview);
            string negZBounds = BoundsSummary(map);

            // Clear single preview cell and clear non-existent preview cell
            log2.log.Clear();
            int ev0 = tileChangedEvents + posChangedEvents;
            map.SetEditorPreviewTile(p, null);
            string afterNull = $"prev=[{PreviewCell(map, p)}]|actual=[{ActualCell(map, p)}]|bounds=[{BoundsSummary(map)}]|log2={string.Join(";", log2.log)}|ev={tileChangedEvents + posChangedEvents - ev0}";

            map.SetEditorPreviewTile(new Vector3Int(9, 9, 0), null);
            string afterNullMissing = BoundsSummary(map);

            return $"first=({afterFirst})|repeat={repeatLog}|replace=({afterReplace})|negZ=[{negZBounds}]|afterNull=({afterNull})|afterNullMissing=[{afterNullMissing}]";
        });

        // 3. SetEditorPreviewColor & SetEditorPreviewTransformMatrix: empty, actual-only, unlocked, LockColor, LockTransform, LockAll
        Check("03.colorAndTransform", () =>
        {
            var map = NewMap("ColorMat");
            var cNew = new Color(0.75f, 0.25f, 0.5f, 0.5f);
            var ev0 = tileChangedEvents + posChangedEvents;

            // Empty cell
            var pEmpty = new Vector3Int(5, 5, 0);
            map.SetEditorPreviewColor(pEmpty, cNew);
            map.SetEditorPreviewTransformMatrix(pEmpty, m2);
            string emptyRes = PreviewCell(map, pEmpty) + "|" + BoundsSummary(map);

            // Actual-only cell (no preview tile)
            var pAct = new Vector3Int(0, 0, 0);
            map.SetTile(pAct, tileFree);
            map.SetEditorPreviewColor(pAct, cNew);
            map.SetEditorPreviewTransformMatrix(pAct, m2);
            string actRes = $"prev=[{PreviewCell(map, pAct)}]|actual=[{ActualCell(map, pAct)}]";

            // Unlocked preview
            var pFree = new Vector3Int(1, 0, 0);
            map.SetEditorPreviewTile(pFree, tileFree);
            map.SetEditorPreviewColor(pFree, cNew);
            map.SetEditorPreviewTransformMatrix(pFree, m2);
            string freeRes = PreviewCell(map, pFree);

            // LockColor preview
            var pLC = new Vector3Int(2, 0, 0);
            map.SetEditorPreviewTile(pLC, tileLockColor);
            map.SetEditorPreviewColor(pLC, cNew);
            map.SetEditorPreviewTransformMatrix(pLC, m2);
            string lcRes = PreviewCell(map, pLC);

            // LockTransform preview
            var pLT = new Vector3Int(3, 0, 0);
            map.SetEditorPreviewTile(pLT, tileLockTransform);
            map.SetEditorPreviewColor(pLT, cNew);
            map.SetEditorPreviewTransformMatrix(pLT, m2);
            string ltRes = PreviewCell(map, pLT);

            // LockAll preview
            var pLA = new Vector3Int(4, 0, 0);
            map.SetEditorPreviewTile(pLA, tileLockAll);
            map.SetEditorPreviewColor(pLA, cNew);
            map.SetEditorPreviewTransformMatrix(pLA, m2);
            string laRes = PreviewCell(map, pLA);

            return $"empty=({emptyRes})|actOnly=({actRes})|free=({freeRes})|lc=({lcRes})|lt=({ltRes})|la=({laRes})|ev={tileChangedEvents + posChangedEvents - ev0}";
        });

        // 4. EditorPreviewBoxFill
        Check("04.boxFill.emptyAndActualAndWall", () =>
        {
            var map = NewMap("BoxFill1");
            // 4x3 grid: wall at x=2, y=0..2; tileA at (0,0),(0,1),(1,0),(3,0)
            map.SetTile(new Vector3Int(2, 0, 0), tileWall);
            map.SetTile(new Vector3Int(2, 1, 0), tileWall);
            map.SetTile(new Vector3Int(2, 2, 0), tileWall);
            map.SetTile(new Vector3Int(0, 0, 0), tileA);
            map.SetTile(new Vector3Int(0, 1, 0), tileA);
            map.SetTile(new Vector3Int(1, 0, 0), tileA);
            map.SetTile(new Vector3Int(3, 0, 0), tileA);

            int ev0 = tileChangedEvents + posChangedEvents;
            // Fill connected tileA from (0,0) in [0,3]x[0,2], clamped to current preview bounds
            map.EditorPreviewBoxFill(new Vector3Int(0, 0, 0), tilePreview, 0, 0, 3, 2);
            string r1 = ScanPreview(map, -1, 4, -1, 3) + "|" + BoundsSummary(map);

            // Setting a preview tile at (1,0) grows preview bounds and does not block box fill over actual tileA
            map.SetEditorPreviewTile(new Vector3Int(1, 0, 0), tileFree);
            map.EditorPreviewBoxFill(new Vector3Int(0, 0, 0), tileLockColor, 0, 0, 3, 2);
            string r2 = ScanPreview(map, -1, 4, -1, 3);

            // Fill connected empty region from (1,1) in [0,3]x[0,2] -> hits (1,1),(1,2),(0,2), NOT x=3
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewBoxFill(new Vector3Int(1, 1, 0), tilePreview, 0, 0, 3, 2);
            string r3 = ScanPreview(map, -1, 4, -1, 3) + "|actual11=" + TName(map.GetTile(new Vector3Int(1, 1, 0)));

            return $"r1=({r1})|r2=({r2})|r3=({r3})|ev={tileChangedEvents + posChangedEvents - ev0}";
        });

        Check("04.boxFill.edgeCases", () =>
        {
            var map = NewMap("BoxFillEdge");
            map.SetTile(new Vector3Int(0, 0, 0), tileA);
            map.SetTile(new Vector3Int(1, 0, 0), tileA);

            // Inverted start/end -> no-op, no bounds growth
            map.EditorPreviewBoxFill(new Vector3Int(5, 5, 0), tilePreview, 3, 0, 1, 2);
            string invX = ScanPreview(map, 0, 6, 0, 6) + "|" + BoundsSummary(map);

            map.EditorPreviewBoxFill(new Vector3Int(5, 5, 0), tilePreview, 0, 3, 2, 1);
            string invY = ScanPreview(map, 0, 6, 0, 6) + "|" + BoundsSummary(map);

            // target == fill (actual tileA == tileA), even if a different preview tile sits at (0,0)
            map.SetEditorPreviewTile(new Vector3Int(0, 0, 0), tilePreview);
            map.EditorPreviewBoxFill(new Vector3Int(0, 0, 0), tileA, 0, 0, 2, 2);
            string sameTarget = ScanPreview(map, 0, 2, 0, 2) + "|" + BoundsSummary(map);

            // target == fill with both null at far point (7,7,0)
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewBoxFill(new Vector3Int(7, 7, 0), null, 0, 0, 8, 8);
            string bothNull = ScanPreview(map, 0, 8, 0, 8) + "|" + BoundsSummary(map);

            // position outside [start,end] box -> check bounds growth and fill
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewBoxFill(new Vector3Int(4, 4, 0), tilePreview, 0, 0, 2, 2);
            string outOfBox = ScanPreview(map, 0, 5, 0, 5) + "|" + BoundsSummary(map);

            // null fill over actual tileA cells that currently have preview tiles
            map.ClearAllEditorPreviewTiles();
            map.SetEditorPreviewTile(new Vector3Int(0, 0, 0), tilePreview);
            map.SetEditorPreviewTile(new Vector3Int(1, 0, 0), tilePreview);
            map.SetEditorPreviewTile(new Vector3Int(1, 1, 0), tilePreview);
            map.EditorPreviewBoxFill(new Vector3Int(0, 0, 0), null, 0, 0, 2, 2);
            string nullClear = ScanPreview(map, 0, 2, 0, 2);

            // Non-TileBase Object passed to EditorPreviewBoxFill
            map.ClearAllEditorPreviewTiles();
            var tex = new Texture2D(2, 2) { name = "NotATile" };
            string nonTileStatus;
            try
            {
                map.EditorPreviewBoxFill(new Vector3Int(0, 0, 0), tex, 0, 0, 1, 0);
                nonTileStatus = "ok:" + ScanPreview(map, 0, 1, 0, 0) + "|has00=" + map.HasEditorPreviewTile(new Vector3Int(0, 0, 0));
            }
            catch (Exception e)
            {
                nonTileStatus = "throws:" + e.GetType().Name;
            }
            Object.DestroyImmediate(tex);

            // Nonzero Z and negative coordinates
            map.ClearAllEditorPreviewTiles();
            map.SetTile(new Vector3Int(-2, -2, 2), tileA);
            map.SetTile(new Vector3Int(-1, -2, 2), tileA);
            map.EditorPreviewBoxFill(new Vector3Int(-2, -2, 2), tilePreview, -3, -3, 0, 0);
            string negZ = "z2=" + ScanPreview(map, -3, 0, -3, 0, 2) + "|z0=" + ScanPreview(map, -3, 0, -3, 0, 0) + "|" + BoundsSummary(map);

            return $"invX=({invX})|invY=({invY})|same=({sameTarget})|bothNull=({bothNull})|outBox=({outOfBox})|nullClear=({nullClear})|nonTile=({nonTileStatus})|negZ=({negZ})";
        });

        // 5. EditorPreviewFloodFill
        Check("05.floodFill", () =>
        {
            var map = NewMap("FloodFill");
            // Actual 3x3 frame with tileWall at x=1, y=0..2; tileA at (0,0),(0,1); empty at (0,2),(2,0),(2,1),(2,2)
            map.SetTile(new Vector3Int(1, 0, 0), tileWall);
            map.SetTile(new Vector3Int(1, 1, 0), tileWall);
            map.SetTile(new Vector3Int(1, 2, 0), tileWall);
            map.SetTile(new Vector3Int(0, 0, 0), tileA);
            map.SetTile(new Vector3Int(0, 1, 0), tileA);
            map.SetTile(new Vector3Int(2, 2, 0), tileA);

            int ev0 = tileChangedEvents + posChangedEvents;
            // Flood connected tileA from (0,0) within current preview bounds
            map.EditorPreviewFloodFill(new Vector3Int(0, 0, 0), tilePreview);
            string r1 = ScanPreview(map, 0, 2, 0, 2) + "|" + BoundsSummary(map);

            // Setting a preview tile at (0,1) grows preview bounds and does not block flood fill over actual tileA
            map.SetEditorPreviewTile(new Vector3Int(0, 1, 0), tileFree);
            map.EditorPreviewFloodFill(new Vector3Int(0, 0, 0), tileLockColor);
            string r2 = ScanPreview(map, 0, 2, 0, 2);

            // Flood null over connected tileA clears preview tiles on (0,0),(0,1) only
            map.SetEditorPreviewTile(new Vector3Int(2, 2, 0), tilePreview);
            map.EditorPreviewFloodFill(new Vector3Int(0, 0, 0), null);
            string r3 = ScanPreview(map, 0, 2, 0, 2);

            // Flood empty region from (2,0) inside bounds (0..2, 0..2) -> (2,0),(2,1)
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewFloodFill(new Vector3Int(2, 0, 0), tilePreview);
            string r4 = ScanPreview(map, 0, 3, 0, 3) + "|" + BoundsSummary(map);

            // Flood empty from far point (4, 1, 0) grows preview bounds and fills connected empty within grown preview bounds
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewFloodFill(new Vector3Int(4, 1, 0), tilePreview);
            string r5 = ScanPreview(map, 0, 4, 0, 2) + "|" + BoundsSummary(map);

            // Flood with null at far empty point (6, 6, 0) (target == fill == null) -> check if preview bounds grew!
            map.ClearAllEditorPreviewTiles();
            map.EditorPreviewFloodFill(new Vector3Int(6, 6, 0), null);
            string r6 = ScanPreview(map, 0, 6, 0, 6) + "|" + BoundsSummary(map);

            return $"r1=({r1})|r2=({r2})|r3=({r3})|r4=({r4})|r5=({r5})|r6=({r6})|ev={tileChangedEvents + posChangedEvents - ev0}";
        });

        // 6. ClearAllEditorPreviewTiles, CompressBounds, ResizeBounds, ClearAllTiles, SetTile bounds interactions
        Check("06.clearAndBounds", () =>
        {
            var map = NewMap("ClearBounds");
            map.SetTile(new Vector3Int(1, 1, 0), tileA);
            map.SetTile(new Vector3Int(3, 3, 0), tileA);
            map.SetEditorPreviewTile(new Vector3Int(-2, 5, 0), tilePreview);
            string beforeClear = BoundsSummary(map) + "|prev=" + ScanPreview(map, -2, 5, 0, 5);

            int ev0 = tileChangedEvents + posChangedEvents;
            map.ClearAllEditorPreviewTiles();
            string afterClear = BoundsSummary(map) + "|prev=" + ScanPreview(map, -2, 5, 0, 5) + "|actual11=" + TName(map.GetTile(new Vector3Int(1, 1, 0))) + "|ev=" + (tileChangedEvents + posChangedEvents - ev0);

            // CompressBounds while preview tile exists outside actual bounds
            map.SetTile(new Vector3Int(3, 3, 0), null);
            map.SetEditorPreviewTile(new Vector3Int(5, 5, 0), tilePreview);
            map.CompressBounds();
            string afterCompress = BoundsSummary(map) + "|has55=" + map.HasEditorPreviewTile(new Vector3Int(5, 5, 0));

            // SetTile growing actual bounds while preview tile exists
            map.SetTile(new Vector3Int(-3, -2, 0), tileA);
            string afterGrowActual = BoundsSummary(map) + "|has55=" + map.HasEditorPreviewTile(new Vector3Int(5, 5, 0));

            // ResizeBounds with smaller origin/size while preview tile exists outside
            map.origin = new Vector3Int(0, 0, 0);
            map.size = new Vector3Int(2, 2, 1);
            map.ResizeBounds();
            string afterResize = BoundsSummary(map) + "|has55=" + map.HasEditorPreviewTile(new Vector3Int(5, 5, 0)) + "|actualNeg=" + TName(map.GetTile(new Vector3Int(-3, -2, 0)));

            // ClearAllTiles while preview tile exists
            map.SetEditorPreviewTile(new Vector3Int(4, 4, 0), tilePreview);
            map.ClearAllTiles();
            string afterClearAll = BoundsSummary(map) + "|has44=" + map.HasEditorPreviewTile(new Vector3Int(4, 4, 0));

            return $"before=({beforeClear})|afterClear=({afterClear})|compress=({afterCompress})|growActual=({afterGrowActual})|resize=({afterResize})|clearAll=({afterClearAll})";
        });

        Tilemap.tilemapTileChanged -= onTileChanged;
        Tilemap.tilemapPositionsChanged -= onPosChanged;
        Object.DestroyImmediate(dummyPrefab);

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string TName(TileBase t) => t == null ? "<null>" : t.name;
    static string OName(Object o) => o == null ? "<null>" : o.name;
    static string V3(Vector3Int v) => $"({v.x},{v.y},{v.z})";
    static string Col(Color c) => string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##},{3:0.##})", c.r, c.g, c.b, c.a);
    static string Mat(Matrix4x4 m) => m == Matrix4x4.identity
        ? "I"
        : string.Format(CultureInfo.InvariantCulture, "T({0:0.##},{1:0.##},{2:0.##})S({3:0.##},{4:0.##},{5:0.##})",
            m.m03, m.m13, m.m23, m.m00, m.m11, m.m22);

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
