using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $IMPORTSELFLOAD_OUT (tests/value_probe.py rows): what loading an asset
/// gave from its own import callbacks at the project's first import (the lines the import wrote),
/// and again after a forced reimport in Play mode; each asset's main type and importer.
/// </summary>
public class ImportSelfLoadProbe : MonoBehaviour
{
    const string Variable = "IMPORTSELFLOAD_OUT";
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();
    static readonly string[] Assets = { "Assets/Textures/Sprite.png", "Assets/Data/Thing.gen", "Assets/Data/Mismatch.json", "Assets/Data/Override.ovr2", "Assets/Data/Plain.ovr2", "Assets/Data/Own.over" };

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("ImportSelfLoadProbe").AddComponent<ImportSelfLoadProbe>();
    }

    /// <summary>The import lines written since <paramref name="from"/>, as rows
    /// <paramref name="prefix"/>/&lt;callback&gt; &lt;path&gt; (the order of imports is not
    /// recorded: the Editor's import queue decides it), each with how often it was written.</summary>
    static int Lines(string dir, string prefix, int from)
    {
        var file = Path.Combine(dir, "imports.txt");
        var lines = File.Exists(file) ? File.ReadAllLines(file) : Array.Empty<string>();
        foreach (var group in lines.Skip(from).GroupBy(l => l.Substring(0, l.IndexOf(" | ", StringComparison.Ordinal))).OrderBy(g => g.Key, StringComparer.Ordinal))
            Row(prefix + "/" + group.Key, string.Join(" || ", group.Select(l => l.Substring(l.IndexOf(" | ", StringComparison.Ordinal) + 3)).Distinct())
                + (group.Count() > 1 ? " x" + group.Count() : ""));
        return lines.Length;
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try
        {
            int seen = Lines(dir, "first", 0);
            foreach (var path in Assets)
            {
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                var importer = AssetImporter.GetAtPath(path);
                Row("asset/" + path, "main " + (main == null ? "null" : main.GetType().Name + " " + main.name)
                    + " importer " + (importer == null ? "null" : importer.GetType().Name));
            }
            logs.Clear();
            foreach (var path in new[] { "Assets/Textures/Sprite.png", "Assets/Data/Thing.gen" })
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Lines(dir, "reimport", seen);
            Row("reimport/logs", string.Join("\n", logs.Where(l => !l.StartsWith("Log:", StringComparison.Ordinal))));
            // An override importer's and another extension's importer's .meta, imported again now
            // that the importers are compiled.
            foreach (var path in new[] { "Assets/Data/Override.ovr2", "Assets/Data/Mismatch.json" })
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                var importer = AssetImporter.GetAtPath(path);
                Row("forced/" + path, "main " + (main == null ? "null" : main.GetType().Name + " " + main.name)
                    + " importer " + (importer == null ? "null" : importer.GetType().Name));
            }
            // The override set through the API: what the asset then is, and its .meta.
            try
            {
                // OverImporter is in the Editor assembly.
                var over = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("OverImporter")).First(t => t != null);
                typeof(AssetDatabase).GetMethods().First(m => m.Name == "SetImporterOverride" && m.IsGenericMethodDefinition)
                    .MakeGenericMethod(over).Invoke(null, new object[] { "Assets/Data/Plain.ovr2" });
                var main = AssetDatabase.LoadMainAssetAtPath("Assets/Data/Plain.ovr2");
                Row("set_override/main", main == null ? "null" : main.GetType().Name + " " + main.name);
                Row("set_override/importer", AssetImporter.GetAtPath("Assets/Data/Plain.ovr2")?.GetType().Name ?? "null");
                Row("set_override/get", AssetDatabase.GetImporterOverride("Assets/Data/Plain.ovr2")?.Name ?? "null");
                Row("set_override/meta", System.Text.RegularExpressions.Regex.Replace(File.ReadAllText("Assets/Data/Plain.ovr2.meta"), "(?m)^guid: .*$", "guid: (guid)"));
            }
            catch (Exception e) { Row("set_override", "throws " + e.GetType().Name + ": " + e.Message); }
        }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }
}
