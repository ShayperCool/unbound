using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl into $MODEL_FILE_IDS_OUT (tests/value_probe.py): the local file id of
/// every GameObject and component of Nested.fbx and Single.fbx, and what the prefabs overriding
/// objects below a model's root (Resources/NestedVariant, Resources/Outer) give each renderer,
/// GameObject, Transform and the LODGroup when instantiated.
/// </summary>
public class ModelFileIdProbe : MonoBehaviour
{
    static StreamWriter output;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
        return parts.Count == 0 ? "." : string.Join("/", parts);
    }

    static void Ids(string model)
    {
        var path = "Assets/Models/" + model + ".fbx";
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        // Objects sharing a key (meshes of nodes with the same name) are listed by sorted id: the
        // order AssetDatabase lists them in is not part of what the probe measures.
        var rows = new SortedDictionary<string, List<long>>(StringComparer.Ordinal);
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            string key;
            if (obj is GameObject go) key = PathOf(go.transform, root.transform) + "/GameObject";
            else if (obj is Component c) key = PathOf(c.transform, root.transform) + "/" + c.GetType().Name;
            else key = obj.GetType().Name + ":" + obj.name;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long id);
            var k = "ids/" + model + "/" + key;
            if (!rows.TryGetValue(k, out var ids)) rows[k] = ids = new List<long>();
            ids.Add(id);
        }
        foreach (var kv in rows)
        {
            kv.Value.Sort();
            Row(kv.Key, string.Join(",", kv.Value));
        }
    }

    static void State(string prefab)
    {
        var instance = Instantiate(Resources.Load<GameObject>(prefab));
        foreach (var t in instance.GetComponentsInChildren<Transform>(true))
        {
            var key = "state/" + prefab + "/" + PathOf(t, instance.transform);
            Row(key + "/active", t.gameObject.activeSelf.ToString());
            Row(key + "/local", t.localPosition.ToString("F3") + " " + t.localScale.ToString("F3"));
            var r = t.GetComponent<MeshRenderer>();
            if (r != null)
                Row(key + "/renderer", $"{r.enabled} {r.shadowCastingMode} {r.receiveShadows} {(r.sharedMaterial != null ? r.sharedMaterial.name : "null")}");
            var lod = t.GetComponent<LODGroup>();
            if (lod != null) Row(key + "/lodgroup", $"{lod.fadeMode} {lod.lodCount}");
        }
        DestroyImmediate(instance);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var dir = Environment.GetEnvironmentVariable("MODEL_FILE_IDS_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            using (output = new StreamWriter(Path.Combine(dir, "values.jsonl")))
            {
                Ids("Nested");
                Ids("Single");
                Ids("NamedTable");
                Ids("OldMeta");
                Ids("OldMetaGen2");
                State("NestedVariant");
                State("Outer");
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
