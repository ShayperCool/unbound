using System;
using System.Collections.Generic;
using System.Collections;
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
    static bool s_Recording;
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
        yield return new KeyValuePair<string, Vector3[]>("duplicate near", tetra.Concat(new[] { new Vector3(0.049f, 0, 0) }).ToArray());
        yield return new KeyValuePair<string, Vector3[]>("duplicate threshold", tetra.Concat(new[] { new Vector3(0.05f, 0, 0) }).ToArray());
        yield return new KeyValuePair<string, Vector3[]>("duplicate beyond", tetra.Concat(new[] { new Vector3(0.051f, 0, 0) }).ToArray());
        yield return new KeyValuePair<string, Vector3[]>("duplicate chain", tetra.Concat(new[] { new Vector3(0.04f, 0, 0), new Vector3(0.08f, 0, 0) }).ToArray());
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

    static void Cycle()
    {
        if (!IsEngine) UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Extra.unity");
        var probes = LightmapSettings.lightProbes;
        var original = probes.bakedProbes;
        var points = probes.positions;
        var affine = points.Select(p =>
        {
            SphericalHarmonicsL2 sh = default;
            sh[0, 0] = p.x + 2f * p.y - p.z;
            return sh;
        }).ToArray();
        probes.bakedProbes = affine;
        var point = new Vector3(5.498985290527344f, 2.438500165939331f, -0.5086919069290161f);
        using (var query = new LightProbesQuery(Allocator.TempJob))
            for (int seed = 0; seed < probes.cellCount; seed++)
            {
                int index = seed;
                query.CalculateInterpolatedLightAndOcclusionProbe(point, ref index, out var sh, out _);
                Put("cycle seed " + seed, index + " " + F(sh[0, 0]));
            }
        probes.bakedProbes = original;
    }

    static void SetPositions(Vector3[] points, SphericalHarmonicsL2[] coefficients)
    {
        if (!IsEngine)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var probes = LightmapSettings.lightProbes;
            if (!probes.SetPositionsSelf(points, false)) throw new Exception("positions rejected");
            LightProbes.Tetrahedralize();
            probes.bakedProbes = coefficients;
            return;
        }
        var setType = typeof(LightProbes).Assembly.GetType("UnityEngine.ProbeSet", true);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var set = Activator.CreateInstance(setType, true);
        setType.GetField("Positions", fields).SetValue(set, points);
        setType.GetField("Probes", fields).SetValue(set, coefficients);
        typeof(LightProbes).Assembly.GetType("UnityEngine.ProbeTetrahedralization", true)
            .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { set });
        typeof(LightProbes).GetField("m_Set", fields).SetValue(LightmapSettings.lightProbes, set);
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
        SetPositions(points.ToArray(), coefficients);
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

    static void RoundedBounds()
    {
        for (int axis = 0; axis < 3; axis++)
        {
            var points = new Vector3[18];
            points[0] = Vector3.zero;
            points[1] = new Vector3(2, 0, 0);
            points[2] = new Vector3(0, 2, 0);
            points[3] = new Vector3(0, 0, 2);
            for (int i = 4; i < points.Length; i++)
                points[i] = new Vector3(0.25f + (i % 3) * 0.1f, 0.25f + (i % 5) * 0.1f, 0.25f + (i % 7) * 0.05f);
            for (int i = 0; i < points.Length; i++)
                points[i][axis] = points[i][axis] == 0f ? 0.14f : points[i][axis] == 2f ? 2.45f : points[i][axis] + 0.14f;
            var coefficients = points.Select(p =>
            {
                SphericalHarmonicsL2 sh = default;
                sh[0, 0] = 8f;
                return sh;
            }).ToArray();
            SetPositions(points, coefficients);
            var inside = points[0];
            inside[axis] += 0.000001f;
            var outside = points[0];
            outside[axis] -= 0.000001f;
            var samples = new[] { points[0], inside, outside, (points[0] + points[1] + points[2] + points[3]) / 4f };
            using (var query = new LightProbesQuery(Allocator.TempJob))
                for (int i = 0; i < samples.Length; i++)
                {
                    int index = -1;
                    query.CalculateInterpolatedLightAndOcclusionProbe(samples[i], ref index, out var sh, out _);
                    Put("bounds axis " + axis + " sample " + i, index + " " + F(sh[0, 0]));
                }
        }
    }

    static void Boundary(string label, int probe)
    {
        var probes = LightmapSettings.lightProbes;
        Mesh(probes.positions, out var indices, out var positions);
        Put(label + " mesh positions", Positions(positions));
        Put(label + " mesh indices", string.Join(" ", indices));
        Put(label + " mesh cells", (indices.Length / 4).ToString(CultureInfo.InvariantCulture));
        var point = probes.positions[probe];
        using (var query = new LightProbesQuery(Allocator.TempJob))
            for (int seed = -1; seed < probes.cellCount; seed++)
            {
                int index = seed;
                query.CalculateInterpolatedLightAndOcclusionProbe(point, ref index, out var sh, out var occ);
                Put(label + " boundary seed " + seed, index + " " + F(sh[0, 0]) + " " +
                    F(occ.x) + " " + F(occ.y) + " " + F(occ.z) + " " + F(occ.w));
            }
        var forward = probes.positions;
        var reverse = forward.Reverse().ToArray();
        foreach (var ordered in new[] { forward, reverse })
        {
            var occ = new Vector4[ordered.Length];
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(ordered, null, occ);
            int slot = ordered == forward ? probe : ordered.Length - 1 - probe;
            Put(label + (ordered == forward ? " forward" : " reverse"),
                F(occ[slot].x) + " " + F(occ[slot].y) + " " + F(occ[slot].z) + " " + F(occ[slot].w));
        }
    }

    static void Meshes()
    {
        foreach (var layout in Layouts())
        {
            Mesh(layout.Value, out var indices, out var positions);
            Put(layout.Key + " positions", Positions(positions));
            Put(layout.Key + " indices", string.Join(" ", indices));
            Put(layout.Key + " cells", (indices.Length / 4).ToString(CultureInfo.InvariantCulture));
        }
    }

    static void Complete()
    {
        Put("complete", "true");
        File.WriteAllText(Environment.GetEnvironmentVariable("LIGHT_PROBES_GEOMETRY_OUT"), Output.ToString());
        if (IsEngine) Application.Quit();
        else UnityEditor.EditorApplication.Exit(0);
    }

    public static void Record()
    {
        if (s_Recording) return;
        s_Recording = true;
        Meshes();
        Cycle();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        Boundary("main", 8);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Extra.unity",
            UnityEditor.SceneManagement.OpenSceneMode.Additive);
        LightProbes.Tetrahedralize();
        Boundary("combined", 22);
        Faces();
        RoundedBounds();
        Complete();
    }

    public static IEnumerator Replay()
    {
        if (s_Recording) yield break;
        s_Recording = true;
        Meshes();
        UnityEngine.SceneManagement.SceneManager.LoadScene("Extra");
        yield return null;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Extra")
            throw new Exception("Extra scene did not finish loading");
        Cycle();
        UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        yield return null;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main")
            throw new Exception("Main scene did not finish loading");
        Boundary("main", 8);
        UnityEngine.SceneManagement.SceneManager.LoadScene("Extra", UnityEngine.SceneManagement.LoadSceneMode.Additive);
        yield return null;
        LightProbes.Tetrahedralize();
        Boundary("combined", 22);
        UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        yield return null;
        Faces();
        RoundedBounds();
        Complete();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (IsEngine && !s_Recording) new GameObject("Probe geometry").AddComponent<ProbeGeometryRunner>();
    }
}

public sealed class ProbeGeometryRunner : MonoBehaviour
{
    void Awake() => UnityEngine.Object.DontDestroyOnLoad(gameObject);
    IEnumerator Start() => ProbeGeometry.Replay();
}
