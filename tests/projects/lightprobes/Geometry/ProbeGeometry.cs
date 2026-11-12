using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

// Diagnostic mesh controls. The complete index order is retained even where it differs.
public static class ProbeGeometry
{
    static readonly StringBuilder Output = new StringBuilder();
    static bool IsEngine => typeof(LightProbes).Assembly.GetType("UnityEngine.ProbeSet") != null;
    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string Positions(Vector3[] points) => string.Join(" ", points.Select(p => F(p.x) + " " + F(p.y) + " " + F(p.z)));
    static void Put(string key, string value) => Output.Append("{\"key\":\"").Append(key).Append("\",\"value\":\"")
        .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");

    public static IEnumerable<KeyValuePair<string, Vector3[]>> Layouts()
    {
        var tetra = new[] { Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0), new Vector3(0, 0, 2) };
        yield return new KeyValuePair<string, Vector3[]>("tetra", tetra);
        yield return new KeyValuePair<string, Vector3[]>("duplicate", tetra.Concat(new[] { Vector3.zero }).ToArray());
        yield return new KeyValuePair<string, Vector3[]>("coplanar", new[] {
            Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 0, 2), new Vector3(2, 0, 2), new Vector3(1, 0, 1) });
        yield return new KeyValuePair<string, Vector3[]>("thin", new[] {
            Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 0, 2), new Vector3(2, 0.0001f, 2), new Vector3(1, 0.1f, 1) });
        yield return new KeyValuePair<string, Vector3[]>("outside", tetra.Concat(new[] { new Vector3(4, 1, 1), new Vector3(-1, 0.5f, 0.25f) }).ToArray());
    }

    static void Mesh(Vector3[] points, out int[] indices, out Vector3[] positions)
    {
        if (!IsEngine)
        {
            UnityEditor.Lightmapping.Tetrahedralize(points, out indices, out positions);
            return;
        }
        // This diagnostic uses the engine's mesh builder directly, separately from baked data.
        var assembly = typeof(LightProbes).Assembly;
        var setType = assembly.GetType("UnityEngine.ProbeSet", true);
        var builder = assembly.GetType("UnityEngine.ProbeTetrahedralization", true);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var set = Activator.CreateInstance(setType, true);
        setType.GetField("Positions", fields).SetValue(set, points);
        builder.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { set });
        var cells = (Array)setType.GetField("Cells", fields).GetValue(set);
        var result = new List<int>();
        foreach (var cell in cells)
        {
            var type = cell.GetType();
            int d = (int)type.GetField("I3", fields).GetValue(cell);
            if (d < 0) continue;
            foreach (var name in new[] { "I0", "I1", "I2", "I3" }) result.Add((int)type.GetField(name, fields).GetValue(cell));
        }
        indices = result.ToArray();
        positions = points;
    }

    static void Faces()
    {
        var points = new List<Vector3> { Vector3.zero, new Vector3(2, 0, 0), new Vector3(0, 2, 0), new Vector3(0, 0, 2) };
        foreach (float x in new[] { 0.25f, 0.5f, 0.75f })
            foreach (float y in new[] { 0.25f, 0.5f })
                foreach (float z in new[] { 0.25f, 0.5f }) points.Add(new Vector3(x, y, z));
        points.Add(new Vector3(1, 0.25f, 0.25f));
        points.Add(new Vector3(0.25f, 1, 0.25f));
        var coefficients = points.Select(p =>
        {
            SphericalHarmonicsL2 sh = default;
            sh[0, 0] = p.x + 2f * p.y + 4f * p.z;
            return sh;
        }).ToArray();
        if (!IsEngine)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var probes = LightmapSettings.lightProbes;
            if (!probes.SetPositionsSelf(points.ToArray(), false)) throw new Exception("positions rejected");
            LightProbes.Tetrahedralize();
            probes.bakedProbes = coefficients;
        }
        else
        {
            var setType = typeof(LightProbes).Assembly.GetType("UnityEngine.ProbeSet", true);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var set = Activator.CreateInstance(setType, true);
            setType.GetField("Positions", fields).SetValue(set, points.ToArray());
            setType.GetField("Probes", fields).SetValue(set, coefficients);
            typeof(LightProbes).Assembly.GetType("UnityEngine.ProbeTetrahedralization", true)
                .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { set });
            typeof(LightProbes).GetField("m_Set", fields).SetValue(LightmapSettings.lightProbes, set);
        }
        var samples = new[] { new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0.000001f),
            new Vector3(0.5f, 0.5f, -0.000001f), new Vector3(0.5f, 0.5f, 0.25f),
            new Vector3(1, 1, 1), new Vector3(2.01f, 1, 1) };
        using (var query = new LightProbesQuery(Allocator.TempJob))
        {
            for (int i = 0; i < samples.Length; i++)
                foreach (int seed in new[] { -1, 0, 1, int.MaxValue })
                {
                    int index = seed;
                    query.CalculateInterpolatedLightAndOcclusionProbe(samples[i], ref index, out var sh, out _);
                    Put("face " + i + " seed " + seed, index + " " + F(sh[0, 0]));
                }
        }
        var renderer = new GameObject("Probe hint", typeof(MeshRenderer)).GetComponent<MeshRenderer>();
        for (int i = 0; i < samples.Length; i++)
        {
            LightProbes.GetInterpolatedProbe(samples[i], renderer, out var hinted);
            LightProbes.GetInterpolatedProbe(samples[i], null, out var plain);
            Put("renderer " + i, F(hinted[0, 0]) + " " + F(plain[0, 0]));
        }
        UnityEngine.Object.DestroyImmediate(renderer.gameObject);
    }

    public static void Record()
    {
        foreach (var layout in Layouts())
        {
            Mesh(layout.Value, out var indices, out var positions);
            Put(layout.Key + " positions", Positions(positions));
            Put(layout.Key + " indices", string.Join(" ", indices));
            Put(layout.Key + " cells", (indices.Length / 4).ToString(CultureInfo.InvariantCulture));
        }
        Faces();
        Put("complete", "true");
        File.WriteAllText(Environment.GetEnvironmentVariable("LIGHT_PROBES_GEOMETRY_OUT"), Output.ToString());
        if (IsEngine) Application.Quit();
        else UnityEditor.EditorApplication.Exit(0);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() { if (IsEngine) Record(); }
}
