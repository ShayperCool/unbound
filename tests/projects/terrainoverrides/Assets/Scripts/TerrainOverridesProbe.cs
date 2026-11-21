using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.TerrainUtils;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what TerrainData's level-of-detail overrides do to the values the
/// getters answer, and how TerrainUtility's TerrainMap factories place and validate terrains.
/// </summary>
static class TerrainOverridesProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly List<Object> s_Made = new List<Object>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("TERRAIN_OVERRIDES_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);

        RecordOverrides();
        RecordMaps();

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        UnityEditor.EditorApplication.Exit(0);
    }

    // ---------------------------------------------------------------- level-of-detail overrides

    static TerrainData Data()
    {
        var data = new TerrainData { heightmapResolution = 33, size = new Vector3(100, 50, 100) };
        var heights = new float[33, 33];
        for (int z = 0; z < 33; z++)
            for (int x = 0; x < 33; x++)
                heights[z, x] = 0.25f + 0.5f * Mathf.Sin(x * 0.3f) * Mathf.Cos(z * 0.2f) * 0.5f;
        data.SetHeights(0, 0, heights);
        s_Made.Add(data);
        return data;
    }

    static string Errors(TerrainData data) => "errors=" + string.Join(",", data.GetMaximumHeightError().Select(F));

    static string Extents(TerrainData data) => "extents=" + string.Join(",", data.GetPatchMinMaxHeights().Select(e => F(e.min) + "/" + F(e.max)));

    static string State(TerrainData data) => Errors(data) + "|" + Extents(data) + "|bounds=" + data.bounds.center.ToString("F4") + data.bounds.size.ToString("F4");

    static void RecordOverrides()
    {
        Check("lod.base", () => State(Data()));
        Check("lod.errorOverride", () =>
        {
            var data = Data();
            int count = data.GetMaximumHeightError().Length;
            var logs = Capture(() => data.OverrideMaximumHeightError(Enumerable.Range(0, count).Select(i => 0.01f * (i + 1)).ToArray()), out var thrown);
            return Outcome(thrown, logs) + "|" + State(data);
        });
        Check("lod.errorOverrideShort", () =>
        {
            var data = Data();
            var logs = Capture(() => data.OverrideMaximumHeightError(new[] { 0.5f, 0.25f }), out var thrown);
            return Outcome(thrown, logs) + "|" + State(data);
        });
        Check("lod.errorOverrideLong", () =>
        {
            var data = Data();
            int count = data.GetMaximumHeightError().Length;
            var logs = Capture(() => data.OverrideMaximumHeightError(Enumerable.Repeat(0.125f, count + 3).ToArray()), out var thrown);
            return Outcome(thrown, logs) + "|" + State(data);
        });
        Check("lod.errorOverrideNull", () =>
        {
            var data = Data();
            var logs = Capture(() => data.OverrideMaximumHeightError(null), out var thrown);
            return Outcome(thrown, logs) + "|" + State(data);
        });
        Check("lod.errorOverrideThenHeights", () =>
        {
            var data = Data();
            int count = data.GetMaximumHeightError().Length;
            data.OverrideMaximumHeightError(Enumerable.Repeat(0.3f, count).ToArray());
            data.SetHeights(0, 0, new float[,] { { 0.9f } });
            return State(data);
        });
        Check("lod.extentsOverride", () =>
        {
            var data = Data();
            int count = data.GetPatchMinMaxHeights().Length;
            var extents = Enumerable.Range(0, count).Select(i => new PatchExtents { min = 0.1f * i, max = 0.2f + 0.1f * i }).ToArray();
            var logs = Capture(() => data.OverrideMinMaxPatchHeights(extents), out var thrown);
            return Outcome(thrown, logs) + "|" + State(data);
        });
        Check("lod.extentsOverrideShort", () =>
        {
            var data = Data();
            var logs = Capture(() => data.OverrideMinMaxPatchHeights(new[] { new PatchExtents { min = 0f, max = 1f } }), out var thrown);
            return Outcome(thrown, logs) + "|" + State(data);
        });
        Check("lod.extentsOverrideNull", () =>
        {
            var data = Data();
            var logs = Capture(() => data.OverrideMinMaxPatchHeights(null), out var thrown);
            return Outcome(thrown, logs) + "|" + State(data);
        });
        Check("lod.extentsOverrideThenHeights", () =>
        {
            var data = Data();
            int count = data.GetPatchMinMaxHeights().Length;
            data.OverrideMinMaxPatchHeights(Enumerable.Repeat(new PatchExtents { min = 0.05f, max = 0.06f }, count).ToArray());
            data.SetHeights(0, 0, new float[,] { { 0.9f } });
            return State(data);
        });
        Check("lod.delayedHeights", () =>
        {
            var data = Data();
            int count = data.GetMaximumHeightError().Length;
            data.OverrideMaximumHeightError(Enumerable.Repeat(0.3f, count).ToArray());
            data.SetHeightsDelayLOD(0, 0, new float[,] { { 0.9f } });
            var delayed = State(data);
            data.SyncHeightmap();
            return "delayed:" + delayed + "|synced:" + State(data);
        });
    }

    // ---------------------------------------------------------------- terrain maps

    static Terrain Tile(string name, Vector3 position, Vector3 size, int group = 0, int resolution = 33)
    {
        var data = new TerrainData { heightmapResolution = resolution, size = size };
        s_Made.Add(data);
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = name;
        go.transform.position = position;
        var terrain = go.GetComponent<Terrain>();
        terrain.groupingID = group;
        s_Made.Add(go);
        return terrain;
    }

    static string Map(TerrainMap map) => map == null ? "<null>"
        : string.Join(",", map.terrainTiles.OrderBy(p => p.Key.tileZ).ThenBy(p => p.Key.tileX)
            .Select(p => p.Key.tileX + ":" + p.Key.tileZ + "=" + (p.Value != null ? p.Value.name : "<null>")));

    static void MapCase(string key, Func<Terrain[]> make, Func<Terrain[], bool, TerrainMap> build)
    {
        foreach (var validation in new[] { true, false })
        {
            Check(key + (validation ? ".validated" : ".unvalidated"), () =>
            {
                var terrains = make();
                TerrainMap map = null;
                var logs = Capture(() => map = build(terrains, validation), out var thrown);
                var described = Map(map);
                foreach (var terrain in terrains) Object.DestroyImmediate(terrain.gameObject);
                return Outcome(thrown, logs) + "|map=" + described;
            });
        }
    }

    static void RecordMaps()
    {
        var size = new Vector3(100, 50, 100);
        MapCase("map.grid", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("B", new Vector3(100, 0, 0), size),
            Tile("C", new Vector3(0, 0, 100), size), Tile("D", new Vector3(100, 0, 100), size),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], null, v));
        MapCase("map.overlap", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("B", new Vector3(100, 0, 0), size), Tile("B2", new Vector3(100, 0, 0), size),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], null, v));
        MapCase("map.sizeMismatch", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("Big", new Vector3(100, 0, 0), new Vector3(200, 50, 200)),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], null, v));
        MapCase("map.resolutionMismatch", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("Fine", new Vector3(100, 0, 0), size, 0, 65),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], null, v));
        MapCase("map.offGrid", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("Shifted", new Vector3(100, 0, 30), size),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], null, v));
        MapCase("map.heightOffset", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("Raised", new Vector3(100, 20, 0), size),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], null, v));
        MapCase("map.otherGroup", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("Other", new Vector3(100, 0, 0), size, 7),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], null, v));
        MapCase("map.filterAll", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("Other", new Vector3(100, 0, 0), size, 7),
        }, (t, v) => TerrainMap.CreateFromPlacement(t[0], _ => true, v));
        MapCase("map.gridOrigin", () => new[]
        {
            Tile("A", Vector3.zero, size), Tile("B", new Vector3(100, 0, 0), size),
        }, (t, v) => TerrainMap.CreateFromPlacement(new Vector2(50, 0), new Vector2(100, 100), null, v));
        MapCase("map.noneMatch", () => new[] { Tile("A", Vector3.zero, size) },
            (t, v) => TerrainMap.CreateFromPlacement(t[0], _ => false, v));
        MapCase("map.connected", () =>
        {
            var t = new[] { Tile("A", Vector3.zero, size), Tile("B", new Vector3(100, 0, 0), size), Tile("C", new Vector3(500, 0, 500), size) };
            t[0].SetNeighbors(null, null, t[1], null);
            t[1].SetNeighbors(t[0], t[2], null, null);
            t[2].SetNeighbors(null, null, null, t[1]);
            return t;
        }, (t, v) => TerrainMap.CreateFromConnectedNeighbors(t[0], null, v));
        MapCase("map.connectedClash", () =>
        {
            var t = new[] { Tile("A", Vector3.zero, size), Tile("B", new Vector3(100, 0, 0), size), Tile("C", new Vector3(0, 0, 100), size) };
            // B is right of A and C above A; B's top is A's right: B ends up at (1,0), its top at (1,1),
            // and C at (0,1), whose right is B again.
            t[0].SetNeighbors(null, t[2], t[1], null);
            t[1].SetNeighbors(t[0], t[0], null, null);
            t[2].SetNeighbors(null, null, t[1], t[0]);
            return t;
        }, (t, v) => TerrainMap.CreateFromConnectedNeighbors(t[0], null, v));
        Check("map.nullOrigin", () =>
        {
            TerrainMap map = null;
            var logs = Capture(() => map = TerrainMap.CreateFromPlacement((Terrain)null), out var thrown);
            return Outcome(thrown, logs) + "|map=" + Map(map);
        });
        Check("map.nullConnectedOrigin", () =>
        {
            TerrainMap map = null;
            var logs = Capture(() => map = TerrainMap.CreateFromConnectedNeighbors(null), out var thrown);
            return Outcome(thrown, logs) + "|map=" + Map(map);
        });
    }

    // ---------------------------------------------------------------- output

    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    static string Outcome(Exception thrown, string logs) =>
        "throws=" + (thrown == null ? "<none>" : thrown.GetType().Name + ":" + thrown.Message.Replace("\n", "\\n")) + "|logs=" + logs;

    static string Capture(Action action, out Exception thrown)
    {
        var logs = new List<string>();
        Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition.Replace("\n", "\\n"));
        Application.logMessageReceived += handler;
        thrown = null;
        try { action(); }
        catch (Exception e) { thrown = e; }
        finally { Application.logMessageReceived -= handler; }
        return logs.Count == 0 ? "<none>" : string.Join("||", logs);
    }

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

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
