using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

[RequireComponent(typeof(SphereCollider))]
public sealed class GameObjectMemory : MonoBehaviour
{
    [Serializable]
    sealed class Row
    {
        public string k, v;
    }

    static readonly List<string> Rows = new List<string>();
    static bool RecordRequiredAwake;

    void Awake()
    {
        if (RecordRequiredAwake) Read("required.awake", gameObject);
    }

    static void Put(string key, string value)
        => Rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));

    static void Read(string key, GameObject go)
    {
        int count = go.GetComponents<Component>().Length;
        long memory = Profiler.GetRuntimeMemorySizeLong(go);
        Put(key, $"{count}/{memory}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        string output = Environment.GetEnvironmentVariable("GAME_OBJECT_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;

        Put("unity", Application.unityVersion);
        var go = new GameObject("Memory");
        Read("created", go);
        var added = new List<Component>();
        for (int i = 1; i <= 9; i++)
        {
            added.Add(go.AddComponent<BoxCollider>());
            Read("add." + i, go);
        }

        while (added.Count > 2)
        {
            int last = added.Count - 1;
            Object.DestroyImmediate(added[last]);
            added.RemoveAt(last);
            Read("remove." + last, go);
        }
        var clone = Object.Instantiate(go);
        Read("clone.after.remove", clone);
        Read("source.after.clone", go);
        Read("clone.new.component", Add(clone, typeof(BoxCollider)));

        var groups = new[] { new[] { typeof(BoxCollider), typeof(AudioSource) },
            new[] { typeof(BoxCollider), typeof(MeshFilter), typeof(Light), typeof(MeshRenderer) } };
        foreach (var types in groups)
        {
            var withTypes = new GameObject("Typed memory", types);
            Read("constructor." + types.Length, withTypes);
            var copy = Object.Instantiate(withTypes);
            Read("constructor.clone." + types.Length, copy);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(withTypes);
        }

        var dependencies = new GameObject("Required component");
        RecordRequiredAwake = true;
        try { Read("required", Add(dependencies, typeof(GameObjectMemory))); }
        finally { RecordRequiredAwake = false; }
        Object.DestroyImmediate(dependencies);
        Object.DestroyImmediate(clone);
        Object.DestroyImmediate(go);
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    static GameObject Add(GameObject go, Type type)
    {
        go.AddComponent(type);
        return go;
    }
}
