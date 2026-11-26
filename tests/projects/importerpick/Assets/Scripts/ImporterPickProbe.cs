using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $IMPORTERPICK_OUT (tests/value_probe.py rows): which importer the Editor
/// picks for files whose .meta names a ScriptedImporter of another extension, of an extension no
/// importer declares, a native importer for a claimed extension, or no importer at all; what each
/// imports to, what opening the project and SetLabels write, and the AssetDatabase importer
/// queries. GUID lines read "guid: (guid)".
/// </summary>
public class ImporterPickProbe : MonoBehaviour
{
    const string Variable = "IMPORTERPICK_OUT";
    const string Folder = "Assets/Data";
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static readonly Regex s_Guid = new Regex("(?m)^guid: [0-9a-f]{32}$");

    static void Meta(string key, string path)
    {
        var meta = path + ".meta";
        Row(key, File.Exists(meta) ? s_Guid.Replace(File.ReadAllText(meta), "guid: (guid)") : "<none>");
    }

    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("ImporterPickProbe").AddComponent<ImporterPickProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static string Names(Type[] types) => types == null ? "null" : "[" + string.Join(",", types.Select(t => t == null ? "null" : t.FullName)) + "]";

    static string Imported(string path)
    {
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        if (main == null) return "null";
        return main.GetType().FullName + (main is TextAsset text ? " '" + text.text.Trim() + "'" : "");
    }

    static string ImporterValues(AssetImporter importer)
    {
        if (importer == null) return "null";
        var s = importer.GetType().FullName;
        var field = importer.GetType().GetField("setting");
        if (field != null) s += " setting " + field.GetValue(importer);
        var label = importer.GetType().GetField("label");
        if (label != null) s += " label '" + label.GetValue(importer) + "'";
        return s;
    }

    static void Run()
    {
        var files = Directory.GetFiles(Folder).Where(p => !p.EndsWith(".meta")).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            Meta("open/" + name, path);
            Try("importer/" + name, () => ImporterValues(AssetImporter.GetAtPath(path)));
            Try("imported/" + name, () => Imported(path));
            Try("type/" + name, () => AssetDatabase.GetImporterType(path)?.FullName ?? "null");
            Try("default/" + name, () => AssetDatabase.GetDefaultImporter(path)?.FullName ?? "null");
            Try("available/" + name, () => Names(AssetDatabase.GetAvailableImporters(path)));
            Try("override/" + name, () => AssetDatabase.GetImporterOverride(path)?.FullName ?? "null");
        }
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            Try("labels/" + name, () =>
            {
                AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath(path), new[] { "probe" });
                return ImporterValues(AssetImporter.GetAtPath(path)) + " | " + Imported(path);
            });
            Meta("labels/" + name + "/meta", path);
        }
    }
}
