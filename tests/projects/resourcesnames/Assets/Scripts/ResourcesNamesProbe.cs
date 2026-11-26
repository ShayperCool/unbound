using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>What Resources.Load and LoadAll give for paths that name several files (extensions
/// differ) of different kinds; see README.md.</summary>
public class ResourcesNamesProbe : MonoBehaviour
{
    static readonly List<string> s_Lines = new List<string>();

    static void Add(string key, object value)
    {
        string json = value is string s ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : value is bool b ? (b ? "true" : "false")
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        s_Lines.Add("{\"k\": \"" + key + "\", \"v\": " + json + "}");
    }

    static string Describe(Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    static string List(IEnumerable<Object> objects) => string.Join(",", objects.Select(Describe));

    static string Sorted(IEnumerable<Object> objects) => string.Join(",", objects.Select(Describe).OrderBy(x => x, StringComparer.Ordinal));

    void Start()
    {
        foreach (var name in new[] { "a", "b", "c", "d", "e", "f", "g", "f/inner", "missing" })
        {
            string path = "pair/" + name;
            Add(name + ".load", Describe(Resources.Load(path)));
            Add(name + ".loadTextAsset", Describe(Resources.Load<TextAsset>(path)));
            Add(name + ".loadTexture2D", Describe(Resources.Load<Texture2D>(path)));
            Add(name + ".loadGameObject", Describe(Resources.Load<GameObject>(path)));
            // LoadAll's own order, and its set.
            var all = Resources.LoadAll(path);
            Add(name + ".loadAll", List(all));
            Add(name + ".loadAllSorted", Sorted(all));
            Add(name + ".loadAllTextAsset", Sorted(Resources.LoadAll<TextAsset>(path)));
#if UNITY_EDITOR
            Add(name + ".loadDefaultAsset", Describe(Resources.Load<UnityEditor.DefaultAsset>(path)));
            Add(name + ".loadAllDefaultAsset", Sorted(Resources.LoadAll<UnityEditor.DefaultAsset>(path)));
#endif
        }
        var folder = Resources.LoadAll("pair");
        Add("pair.loadAll", List(folder));
        Add("pair.loadAllSorted", Sorted(folder));
        Add("complete", true);

        string dir = Environment.GetEnvironmentVariable("RESOURCES_NAMES_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), s_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
