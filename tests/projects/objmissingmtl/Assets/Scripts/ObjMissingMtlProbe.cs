using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>What OBJ models whose material library is missing import as; see README.md.</summary>
public class ObjMissingMtlProbe : MonoBehaviour
{
    static readonly string[] Models = { "Missing0", "Missing1", "Missing2", "Present0", "Present1", "Present2", "Derived", "Undefined1", "Mixed1" };
    readonly List<string> m_Lines = new List<string>();

    void Add(string key, object value)
    {
        string json = value is string s ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : value is bool b ? (b ? "true" : "false")
            : Convert.ToString(value, CultureInfo.InvariantCulture);
        m_Lines.Add("{\"k\": \"" + key + "\", \"v\": " + json + "}");
    }

    static string Color(Color c) => string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4},{3:F4}", c.r, c.g, c.b, c.a);

    void Start()
    {
        foreach (var name in Models)
        {
            string at = name + ".";
            var root = Resources.Load<GameObject>("Models/" + name);
            Add(at + "loaded", root != null);
            if (root == null) continue;
            // Every object the model file holds, by type and name (their order is not specified).
            var all = Resources.LoadAll("Models/" + name).Select(o => o.GetType().Name + ":" + o.name).OrderBy(s => s, StringComparer.Ordinal);
            Add(at + "objects", string.Join("|", all));
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Add(at + "renderers", renderers.Length);
            for (int r = 0; r < renderers.Length; r++)
            {
                string rk = at + "r" + r + ".";
                var renderer = renderers[r];
                Add(rk + "name", renderer.name);
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Add(rk + "mesh", mesh != null ? mesh.name : "null");
                if (mesh != null)
                {
                    Add(rk + "vertices", mesh.vertexCount);
                    Add(rk + "subMeshes", mesh.subMeshCount);
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        Add(rk + "indices" + s, string.Join(",", mesh.GetIndices(s)));
                }
                var materials = renderer.sharedMaterials;
                Add(rk + "materials", materials.Length);
                for (int m = 0; m < materials.Length; m++)
                {
                    string mk = rk + "m" + m + ".";
                    var material = materials[m];
                    Add(mk + "name", material != null ? material.name : "null");
                    if (material == null) continue;
                    Add(mk + "shader", material.shader != null ? material.shader.name : "null");
                    Add(mk + "color", material.HasProperty("_Color") ? Color(material.GetColor("_Color")) : "none");
                }
            }
        }
        Add("complete", true);
        string dir = Environment.GetEnvironmentVariable("OBJ_MISSING_MTL_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), m_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
