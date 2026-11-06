using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Lightmap UVs of imported models: every Mesh of every Resources/&lt;variant&gt; folder (the same
/// models under different ModelImporter unwrap settings, see Tools/make_project.py), with its
/// vertex count, sub-mesh triangles, vertices, normals, UV and UV2. Writes one JSON line per mesh
/// into <c>LIGHTMAP_UV_PROBE_OUT</c>, then quits.
/// </summary>
public class LightmapUvProbe : MonoBehaviour
{
    static readonly string[] Variants = { "Default", "Hard30", "Distort", "Manual", "Calc", "Off", "Raw", "RawOff" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("LightmapUvProbe").AddComponent<LightmapUvProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string L<T>(IEnumerable<T> items, Func<T, string> f) => "[" + string.Join(",", items.Select(f)) + "]";
    static string V2(Vector2 v) => $"[{F(v.x)},{F(v.y)}]";
    static string V3(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";

    void Start()
    {
        var output = new StringBuilder();
        foreach (var variant in Variants)
        {
            foreach (var mesh in Resources.LoadAll<Mesh>(variant).OrderBy(m => m.name, StringComparer.Ordinal))
            {
                var uv2 = new List<Vector2>();
                mesh.GetUVs(1, uv2);
                output.Append("{\"variant\":").Append(Q(variant))
                    .Append(",\"mesh\":").Append(Q(mesh.name))
                    .Append(",\"vertexCount\":").Append(mesh.vertexCount)
                    .Append(",\"subMeshes\":").Append(L(Enumerable.Range(0, mesh.subMeshCount), i => L(mesh.GetTriangles(i), t => t.ToString(CultureInfo.InvariantCulture))))
                    .Append(",\"vertices\":").Append(L(mesh.vertices, V3))
                    .Append(",\"normals\":").Append(L(mesh.normals, V3))
                    .Append(",\"uv\":").Append(L(mesh.uv, V2))
                    .Append(",\"uv2\":").Append(L(uv2, V2))
                    .Append("}\n");
            }
        }
        var path = Environment.GetEnvironmentVariable("LIGHTMAP_UV_PROBE_OUT");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path + ".partial", output.ToString());
            File.Move(path + ".partial", path);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
