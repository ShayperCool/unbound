using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $UVMETRICS_OUT (tests/value_probe.py rows): Mesh.GetUVDistributionMetric
/// of meshes built by script, before and after RecalculateUVDistributionMetrics /
/// RecalculateUVDistributionMetric (quads of other sizes and UV ranges, triangles of different
/// ratios together, a triangle whose UVs have no area, the area threshold, a second UV set, a mesh
/// without UVs, a rotated mesh, an index out of range), and the Editor's
/// Collab.IsCollabEnabledForCurrentProject (an internal class, called by reflection as packages do).
/// Floats are written with "R".
/// </summary>
public class UvMetricsProbe : MonoBehaviour
{
    const string Variable = "UVMETRICS_OUT";
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName; }
        Row(key, result);
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("UvMetricsProbe").AddComponent<UvMetricsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    /// <summary>A mesh of triangles given as (position, uv) triples.</summary>
    static Mesh Make(string name, Vector3[] positions, Vector2[] uvs, Vector2[] uv2 = null)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices = positions;
        if (uvs != null) mesh.uv = uvs;
        if (uv2 != null) mesh.uv2 = uv2;
        var indices = new int[positions.Length];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        mesh.triangles = indices;
        return mesh;
    }

    static Vector3[] Quad(float size) => new[]
    {
        new Vector3(0, 0, 0), new Vector3(0, size, 0), new Vector3(size, size, 0),
        new Vector3(0, 0, 0), new Vector3(size, size, 0), new Vector3(size, 0, 0),
    };

    static Vector2[] QuadUv(float size) => new[]
    {
        new Vector2(0, 0), new Vector2(0, size), new Vector2(size, size),
        new Vector2(0, 0), new Vector2(size, size), new Vector2(size, 0),
    };

    static void Metrics(string key, Mesh mesh)
    {
        for (int set = 0; set < 2; set++)
        {
            int s = set;
            Try(key + ".uv" + s, () => F(mesh.GetUVDistributionMetric(s)));
        }
    }

    /// <summary>A mesh of right triangles (world legs, UV legs), each apart, recalculated.</summary>
    static void Aggregate(string key, (float World, float Uv)[] triangles)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (int i = 0; i < triangles.Length; i++)
        {
            var (w, u) = triangles[i];
            var o = new Vector3(i * 10, 0, 0);
            positions.Add(o); positions.Add(o + new Vector3(0, w, 0)); positions.Add(o + new Vector3(w, 0, 0));
            uvs.Add(Vector2.zero); uvs.Add(new Vector2(0, u)); uvs.Add(new Vector2(u, 0));
        }
        var mesh = Make(key, positions.ToArray(), uvs.ToArray());
        mesh.RecalculateUVDistributionMetrics();
        Try(key, () => F(mesh.GetUVDistributionMetric(0)));
    }

    static void Run()
    {
        var unit = Make("Unit", Quad(1), QuadUv(1));
        Metrics("unit.before", unit);
        unit.RecalculateUVDistributionMetrics();
        Metrics("unit", unit);

        var big = Make("Big", Quad(2), QuadUv(1));
        big.RecalculateUVDistributionMetrics();
        Metrics("big", big);

        var half = Make("HalfUv", Quad(1), QuadUv(0.5f));
        half.RecalculateUVDistributionMetrics();
        Metrics("halfUv", half);

        var odd = Make("Odd", Quad(3), QuadUv(0.25f));
        odd.RecalculateUVDistributionMetrics();
        Metrics("odd", odd);

        // Two triangles of world/UV area ratios 1 and 4 (each world area 0.5).
        var mixed = Make("Mixed",
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(2, 0, 0), new Vector3(2, 1, 0), new Vector3(3, 1, 0) },
            new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f) });
        mixed.RecalculateUVDistributionMetrics();
        Metrics("mixed", mixed);

        // Ratios 1 and 4 with world areas 0.5 and 2 (the second scaled).
        var weighted = Make("Weighted",
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(2, 0, 0), new Vector3(2, 2, 0), new Vector3(4, 2, 0) },
            new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) });
        weighted.RecalculateUVDistributionMetrics();
        Metrics("weighted", weighted);

        // A triangle whose UVs have no area, with one whose ratio is 1.
        var flat = Make("FlatUv",
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(2, 0, 0), new Vector3(2, 1, 0), new Vector3(3, 1, 0) },
            new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0) });
        flat.RecalculateUVDistributionMetrics();
        Metrics("flatUv", flat);

        // Only a triangle whose UVs have no area.
        var allFlat = Make("AllFlat", new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) },
            new[] { new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0) });
        allFlat.RecalculateUVDistributionMetrics();
        Metrics("allFlat", allFlat);

        // The threshold: a UV area of 0.125 per triangle, thresholds below and above it.
        var small = Make("SmallUv", Quad(1), QuadUv(0.5f));
        small.RecalculateUVDistributionMetrics(0.1f);
        Metrics("threshold.below", small);
        small.RecalculateUVDistributionMetrics(0.2f);
        Metrics("threshold.above", small);
        var thresholdMixed = Make("ThresholdMixed",
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(2, 0, 0), new Vector3(2, 1, 0), new Vector3(3, 1, 0) },
            new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f) });
        thresholdMixed.RecalculateUVDistributionMetrics(0.2f);
        Metrics("threshold.mixed", thresholdMixed);

        // A second UV set; each set recalculated alone.
        var two = Make("TwoSets", Quad(1), QuadUv(1), QuadUv(0.5f));
        Metrics("twoSets.before", two);
        two.RecalculateUVDistributionMetric(1);
        Metrics("twoSets.second", two);
        two.RecalculateUVDistributionMetric(0);
        Metrics("twoSets.both", two);
        Try("twoSets.index8", () => { two.RecalculateUVDistributionMetric(8); return "done"; });
        Try("twoSets.get8", () => F(two.GetUVDistributionMetric(8)));
        Try("twoSets.getNegative", () => F(two.GetUVDistributionMetric(-1)));

        var none = Make("NoUv", Quad(1), null);
        none.RecalculateUVDistributionMetrics();
        Metrics("noUv", none);

        // A rotated, then moved copy of the unit quad.
        var rotated = Make("Rotated", Array.ConvertAll(Quad(1), p => Quaternion.Euler(30, 40, 50) * p + new Vector3(5, -2, 7)), QuadUv(1));
        rotated.RecalculateUVDistributionMetrics();
        Metrics("rotated", rotated);

        // Changing the vertices afterwards keeps the metric until it is recalculated.
        unit.vertices = Quad(4);
        Metrics("unit.afterVertices", unit);
        unit.RecalculateUVDistributionMetrics();
        Metrics("unit.recalculated", unit);

        // How the triangles' ratios combine: right triangles of world legs a and UV legs u (ratio
        // a²/u²), each set listed as (world leg, UV leg).
        Aggregate("agg.1-2-4", new[] { (1f, 1f), (1.41421356f, 1f), (2f, 1f) });
        Aggregate("agg.1-1-4", new[] { (1f, 1f), (1f, 1f), (2f, 1f) });
        Aggregate("agg.1-4-4", new[] { (1f, 1f), (2f, 1f), (2f, 1f) });
        Aggregate("agg.1-2-3-4", new[] { (1f, 1f), (1.41421356f, 1f), (1.73205081f, 1f), (2f, 1f) });
        Aggregate("agg.1to5", new[] { (1f, 1f), (1.41421356f, 1f), (1.73205081f, 1f), (2f, 1f), (2.23606798f, 1f) });
        var ten = new (float, float)[10];
        for (int i = 0; i < 10; i++) ten[i] = (Mathf.Sqrt(i + 1), 1f);
        Aggregate("agg.1to10", ten);
        // Ratios 1 and 4, the ratio-1 triangle ten times larger in the world and in UV space.
        Aggregate("agg.bigLow", new[] { (3.16227766f, 3.16227766f), (2f, 1f) });
        // Ratios 1 and 4, the ratio-4 triangle with the larger UV area.
        Aggregate("agg.bigHighUv", new[] { (1f, 1f), (6f, 3f) });
        // Ratios 4 and 1, the order of the triangles swapped.
        Aggregate("agg.4-1", new[] { (2f, 1f), (1f, 1f) });
        // Ratios 0.25 and 1.
        Aggregate("agg.quarter-1", new[] { (0.5f, 1f), (1f, 1f) });

        // Ratios 1..n (n 2 to 10), each triangle of the same UV area (world legs grow), then of the
        // same world area (UV legs shrink).
        for (int n = 2; n <= 10; n++)
        {
            var sameUv = new (float, float)[n];
            var sameWorld = new (float, float)[n];
            for (int i = 0; i < n; i++)
            {
                sameUv[i] = (Mathf.Sqrt(i + 1), 1f);
                sameWorld[i] = (1f, 1f / Mathf.Sqrt(i + 1));
            }
            Aggregate("seq.sameUv." + n, sameUv);
            Aggregate("seq.sameWorld." + n, sameWorld);
        }
        // Twenty triangles of ratio 1 and one of ratio 100; and the reverse.
        var many = new (float, float)[21];
        for (int i = 0; i < 20; i++) many[i] = (1f, 1f);
        many[20] = (10f, 1f);
        Aggregate("outlier.high", many);
        var manyLow = new (float, float)[21];
        for (int i = 0; i < 20; i++) manyLow[i] = (10f, 1f);
        manyLow[20] = (1f, 1f);
        Aggregate("outlier.low", manyLow);

        // The Editor's Collab, as packages call it.
        var collab = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Collaboration.Collab");
        Row("collab.type", collab == null ? "missing" : collab.FullName);
        if (collab != null)
        {
            var instanceProperty = collab.GetProperty("instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Row("collab.instanceProperty", instanceProperty == null ? "missing" : "present");
            object instance = null;
            Try("collab.instance", () => { instance = instanceProperty?.GetValue(null); return instance == null ? "null" : "an object"; });
            var method = collab.GetMethod("IsCollabEnabledForCurrentProject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Try("collab.IsCollabEnabledForCurrentProject", () => instance == null || method == null ? "unavailable" : method.Invoke(instance, null).ToString());
        }
    }
}
