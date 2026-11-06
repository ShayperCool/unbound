using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class MaterialSlotsProbe : MonoBehaviour
{
    static readonly string[] Assets = { "Declared", "DeclaredObj", "FirstUse", "FirstUseObj", "Sparse", "SparseObj",
        "Instances", "ObjectsObj", "SharedNames", "SharedNamesReverse", "Multi" };
    static readonly Color[] Colors = { Color.red, Color.green, Color.blue, Color.yellow };
    readonly StringBuilder output = new StringBuilder();
    string directory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MATERIAL_SLOTS_OUT")))
            new GameObject("MaterialSlotsProbe").AddComponent<MaterialSlotsProbe>();
    }

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string L<T>(IEnumerable<T> values, Func<T, string> f) => "[" + string.Join(",", values.Select(f)) + "]";
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string C(Color c) => $"[{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}]";
    void Add(string key, string value) => output.Append("{\"k\":").Append(Q(key)).Append(",\"v\":").Append(value).Append("}\n");
    static string PathOf(Transform t, Transform root) => t == root ? "." :
        (t.parent == root ? t.name : PathOf(t.parent, root) + "/" + t.name);

    void Start()
    {
        directory = Environment.GetEnvironmentVariable("MATERIAL_SLOTS_OUT");
        Directory.CreateDirectory(directory);
        foreach (var asset in Assets)
        foreach (var mode in new[] { "Import", "Calculate", "None" })
        {
            var name = asset + mode;
            var model = Resources.Load<GameObject>("Slots/" + name);
            if (model == null) { Add(name + "/missing", "true"); continue; }
            var instance = Instantiate(model);
            var renderers = instance.GetComponentsInChildren<Renderer>(true)
                .OrderBy(r => PathOf(r.transform, instance.transform), StringComparer.Ordinal).ToArray();
            Add(name + "/rendererCount", renderers.Length.ToString(CultureInfo.InvariantCulture));
            var owned = new List<Material>();
            var materialIds = new Dictionary<Material, int>();
            for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
            {
                var renderer = renderers[rendererIndex];
                // Asset plus renderer index keeps OBJ hierarchy naming outside this measurement.
                var key = name + "/" + rendererIndex;
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
                Add(key + "/subMeshCount", mesh.subMeshCount.ToString(CultureInfo.InvariantCulture));
                Add(key + "/vertices", L(mesh.vertices, V));
                Add(key + "/normals", L(mesh.normals, V));
                Add(key + "/indices", L(Enumerable.Range(0, mesh.subMeshCount), i => L(mesh.GetIndices(i), x => x.ToString(CultureInfo.InvariantCulture))));
                var bindings = renderer.sharedMaterials;
                Add(key + "/materials", L(bindings, m => m == null ? "null" : Q(m.name)));
                Add(key + "/colors", L(bindings, m => m == null ? "null" : C(m.color)));
                Add(key + "/materialIds", L(bindings, m =>
                {
                    if (m == null) return "-1";
                    if (!materialIds.TryGetValue(m, out int id))
                    {
                        id = materialIds.Count;
                        materialIds.Add(m, id);
                    }
                    return id.ToString(CultureInfo.InvariantCulture);
                }));
                // Use the imported colors with an unlit shader to isolate bindings from lighting.
                renderer.sharedMaterials = bindings.Select(m =>
                {
                    var material = new Material(Shader.Find("Unlit/Color")) { color = m == null ? Color.white : m.color };
                    owned.Add(material);
                    return material;
                }).ToArray();
            }
            if (mode == "Import" && asset != "Multi" && renderers.Length > 0)
            {
                // Draw mesh-local geometry and its renderer bindings. Imported axis/pivot
                // transforms are an independent compatibility question.
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                for (int i = 0; i < renderers.Length; ++i)
                {
                    renderers[i].transform.localPosition = Vector3.up * (i * 2);
                    renderers[i].transform.localRotation = Quaternion.identity;
                    renderers[i].transform.localScale = Vector3.one;
                }
                Image(name + "-bindings", renderers);
                foreach (var renderer in renderers)
                {
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; ++i) slots[i].color = Colors[i % Colors.Length];
                }
                Image(name + "-override", renderers);
            }
            DestroyImmediate(instance);
            foreach (var material in owned) DestroyImmediate(material);
        }
        File.WriteAllText(Path.Combine(directory, "slots.jsonl"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Image(string name, Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        var go = new GameObject("SlotCamera");
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(bounds.size.y * 0.6f, bounds.size.x * 0.6f / 2.5f);
        camera.transform.position = bounds.center + Vector3.forward * 10;
        camera.transform.rotation = Quaternion.Euler(0, 180, 0);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 100;
        var rt = new RenderTexture(160, 64, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(160, 64, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 160, 64), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        DestroyImmediate(texture);
        rt.Release();
        DestroyImmediate(rt);
        DestroyImmediate(go);
    }
}
