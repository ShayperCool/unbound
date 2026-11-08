using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// With <c>SMOOTHING_OUT</c> set, records every model of Resources/Smoothing: each mesh's vertices,
/// normals, submesh triangles and material names and colours, and each imported clip's length and frame rate,
/// as one JSON line per value in <c>smoothing.jsonl</c>, then quits.
/// </summary>
public class SmoothingProbe : MonoBehaviour
{
    readonly StringBuilder output = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMOOTHING_OUT")))
            new GameObject("SmoothingProbe").AddComponent<SmoothingProbe>();
    }

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string L<T>(IEnumerable<T> values, Func<T, string> f) => "[" + string.Join(",", values.Select(f)) + "]";
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string C(Color c) => $"[{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}]";
    void Add(string key, string value) => output.Append("{\"k\":").Append(Q(key)).Append(",\"v\":").Append(value).Append("}\n");

    void Start()
    {
        var directory = Environment.GetEnvironmentVariable("SMOOTHING_OUT");
        Directory.CreateDirectory(directory);
        foreach (var model in Resources.LoadAll<GameObject>("Smoothing").OrderBy(m => m.name, StringComparer.Ordinal))
        {
            // Meshes by their order in the hierarchy: an OBJ mesh's name is a separate question.
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            for (var index = 0; index < filters.Length; index++)
            {
                var filter = filters[index];
                var mesh = filter.sharedMesh;
                var key = model.name + "/" + index;
                if (mesh == null) { Add(key + "/mesh", "null"); continue; }
                Add(key + "/vertices", L(mesh.vertices, V));
                Add(key + "/normals", L(mesh.normals, V));
                Add(key + "/triangles", L(Enumerable.Range(0, mesh.subMeshCount), i => L(mesh.GetTriangles(i), x => x.ToString())));
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Add(key + "/materials", L(renderer.sharedMaterials, m => m == null ? "null" : Q(m.name)));
                    Add(key + "/colors", L(renderer.sharedMaterials, m => m == null || !m.HasProperty("_Color") ? "null" : C(m.color)));
                }
            }
        }
        foreach (var clip in Resources.LoadAll<AnimationClip>("Smoothing")
                     .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                     .OrderBy(c => c.name, StringComparer.Ordinal))
        {
            Add("clip/" + clip.name + "/length", F(clip.length));
            Add("clip/" + clip.name + "/frameRate", F(clip.frameRate));
        }
        File.WriteAllText(Path.Combine(directory, "smoothing.jsonl"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
