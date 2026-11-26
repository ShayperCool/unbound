using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $REFRESHCHANGED_OUT (tests/value_probe.py rows): which import callbacks
/// AssetDatabase.Refresh runs for assets whose files changed, loaded before or not (a text, a
/// texture, a scripted importer's asset), what the loaded objects and later loads give, and the
/// same for ImportAsset as a control.
/// </summary>
public class RefreshChangedProbe : MonoBehaviour
{
    const string Variable = "REFRESHCHANGED_OUT";

    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
    }

    /// <summary>The callbacks noted since the last call, sorted (the import order is the Editor's queue's).</summary>
    static string Events()
    {
        var events = RefreshEvents.Events.OrderBy(e => e, StringComparer.Ordinal).ToList();
        RefreshEvents.Events.Clear();
        return events.Count == 0 ? "<none>" : string.Join("\n", events);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("RefreshChangedProbe").AddComponent<RefreshChangedProbe>();
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
        EditorApplication.Exit(0);
#endif
    }

    const string Data = "Assets/Data/";

    static void Change(string name)
    {
        File.WriteAllText(Data + name + ".txt", "v2\n");
        File.Copy("Assets/Source/Wide.png", Data + name + ".png", true);
        File.WriteAllText(Data + name + ".count", "2\n");
        var later = DateTime.UtcNow.AddSeconds(5);
        foreach (var ext in new[] { ".txt", ".png", ".count" }) File.SetLastWriteTimeUtc(Data + name + ext, later);
    }

    static string Describe(UnityEngine.Object o) => o == null ? "null" : o is TextAsset t ? "text " + t.text.Trim() : o is Texture2D x ? "texture " + x.width : o.GetType().Name;

    static void Run()
    {
        // What the project's opening imported is not this probe's (the Editor's domain reload at
        // Play forgets it).
        RefreshEvents.Events.Clear();
        var loadedTxt = AssetDatabase.LoadAssetAtPath<TextAsset>(Data + "Loaded.txt");
        var loadedPng = AssetDatabase.LoadAssetAtPath<Texture2D>(Data + "Loaded.png");
        var loadedCount = AssetDatabase.LoadMainAssetAtPath(Data + "Loaded.count");
        Try("loaded/before", () => Describe(loadedTxt) + " | " + Describe(loadedPng) + " | " + Describe(loadedCount));
        Try("loaded/events", Events);

        Change("Loaded");
        Change("Unloaded");
        // A model whose file does not import, changed in the same Refresh (outside Assets/Data).
        File.WriteAllText("Assets/Broken/Broken.fbx", "still not a model\n");
        File.SetLastWriteTimeUtc("Assets/Broken/Broken.fbx", DateTime.UtcNow.AddSeconds(5));
        Try("refresh", () => { AssetDatabase.Refresh(); return "done"; });
        Try("refresh/events", () =>
        {
            // The same without the texts' OnPreprocessAsset (a row of its own: refresh.events.imports).
            var imports = RefreshEvents.Events.Where(e => !(e.StartsWith("preprocess ") && e.EndsWith(".txt"))).OrderBy(e => e, StringComparer.Ordinal).ToList();
            Row("refresh/events.imports", string.Join("\n", imports));
            return Events();
        });
        Try("loaded/after", () => Describe(loadedTxt) + " | " + Describe(loadedPng) + " | " + Describe(loadedCount));
        Try("loaded/fresh", () => Describe(AssetDatabase.LoadAssetAtPath<TextAsset>(Data + "Loaded.txt")) + " | "
            + Describe(AssetDatabase.LoadAssetAtPath<Texture2D>(Data + "Loaded.png")) + " | " + Describe(AssetDatabase.LoadMainAssetAtPath(Data + "Loaded.count")));
        Try("loaded/fresh/events", Events);
        foreach (var (name, ext) in new[] { ("txt", ".txt"), ("png", ".png"), ("count", ".count") })
        {
            Try("unloaded/" + name, () => Describe(AssetDatabase.LoadMainAssetAtPath(Data + "Unloaded" + ext)));
            Try("unloaded/" + name + "/events", Events);
        }

        // A second Refresh with nothing changed.
        Try("refresh2", () => { AssetDatabase.Refresh(); return "done"; });
        Try("refresh2/events", Events);

        // The control: the same changes imported by ImportAsset, one at a time.
        Change("Imported");
        foreach (var ext in new[] { ".txt", ".png", ".count" })
        {
            Try("import/" + ext.Substring(1), () => { AssetDatabase.ImportAsset(Data + "Imported" + ext); return "done"; });
            Try("import/" + ext.Substring(1) + "/events", Events);
            Try("import/" + ext.Substring(1) + "/load", () => Describe(AssetDatabase.LoadMainAssetAtPath(Data + "Imported" + ext)));
        }
    }
}
