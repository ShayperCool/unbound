using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $DCCMODELS_OUT (tests/value_probe.py rows): what the model files of
/// the DCC formats import as. For each file under Assets/Models: its main asset and sub-assets,
/// the hierarchy (names, local transforms, components, materials) and each mesh (vertices,
/// normals, first UVs, sub-meshes, bounds), and its ModelImporter's scale settings.
/// </summary>
public class DccModelsProbe : MonoBehaviour
{
    const string Variable = "DCCMODELS_OUT";
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value ?? "null") + "\"}\n");

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    static void Number(string key, float value) => output.Write("{\"k\":\"" + key + "\",\"v\":" + F(value) + "}\n");

    static void Vector(string key, Vector3 v) { Number(key + ".x", v.x); Number(key + ".y", v.y); Number(key + ".z", v.z); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("DccModelsProbe").AddComponent<DccModelsProbe>();
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

#if UNITY_EDITOR
    static void Run()
    {
        var paths = UnityEditor.AssetDatabase.FindAssets("", new[] { "Assets/Models" })
            .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var k = "model." + Path.GetFileName(path);
            var importer = UnityEditor.AssetImporter.GetAtPath(path);
            Row(k + ".importer", importer == null ? "null" : importer.GetType().Name);
            if (importer is UnityEditor.ModelImporter model)
            {
                Number(k + ".globalScale", model.globalScale);
                Row(k + ".useFileScale", model.useFileScale.ToString());
                Number(k + ".fileScale", model.fileScale);
            }
            var main = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
            Row(k + ".main", main == null ? "null" : main.GetType().Name + ":" + main.name);
            var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)
                .Where(o => o != null)
                .Select(o => o.GetType().Name + ":" + o.name)
                .OrderBy(s => s, StringComparer.Ordinal);
            Row(k + ".assets", string.Join(",", all.ToArray()));
            if (main is GameObject root)
                Node(k + ".node", root.transform);
            foreach (var mesh in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderBy(m => m.name, StringComparer.Ordinal))
                Mesh(k + ".mesh." + mesh.name, mesh);
        }
    }

    static void Node(string key, Transform t)
    {
        var k = key + "." + t.name;
        Vector(k + ".position", t.localPosition);
        Vector(k + ".euler", t.localRotation.eulerAngles);
        Vector(k + ".scale", t.localScale);
        Row(k + ".components", string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name).ToArray()));
        var renderer = t.GetComponent<Renderer>();
        if (renderer != null)
            Row(k + ".materials", string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "null" : m.name).ToArray()));
        var filter = t.GetComponent<MeshFilter>();
        if (filter != null)
            Row(k + ".mesh", filter.sharedMesh == null ? "null" : filter.sharedMesh.name);
        Row(k + ".children", string.Join(",", Enumerable.Range(0, t.childCount).Select(i => t.GetChild(i).name).ToArray()));
        for (int i = 0; i < t.childCount; i++)
            Node(k, t.GetChild(i));
    }

    static void Mesh(string k, Mesh mesh)
    {
        Row(k + ".counts", mesh.vertexCount + "," + mesh.subMeshCount);
        for (int s = 0; s < mesh.subMeshCount; s++)
            Row(k + ".submesh." + s, mesh.GetTopology(s) + "," + string.Join(" ", mesh.GetIndices(s).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()));
        Vector(k + ".bounds.center", mesh.bounds.center);
        Vector(k + ".bounds.size", mesh.bounds.size);
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;
        Row(k + ".channels", normals.Length + "," + uvs.Length + "," + mesh.colors32.Length + "," + mesh.tangents.Length);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector(k + ".v" + i, vertices[i]);
            if (i < normals.Length) Vector(k + ".n" + i, normals[i]);
            if (i < uvs.Length) { Number(k + ".uv" + i + ".x", uvs[i].x); Number(k + ".uv" + i + ".y", uvs[i].y); }
        }
    }
#else
    static void Run() { }
#endif
}
