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
    static readonly string[] Assets = { "Assets/Textures/Sprite.png", "Assets/Data/Thing.gen", "Assets/Data/Mismatch.json", "Assets/Data/Override.ovr2", "Assets/Data/Plain.ovr2", "Assets/Data/Own.over", "Assets/Data/Second.ovr2", "Assets/Data/Preset.ovr2", "Assets/Textures/Painted.png" };

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

    static string Describe(UnityEngine.Object o) => o == null ? "null" : o.GetType().Name + " " + o.name;

    static string Importers(string path)
    {
        var available = AssetDatabase.GetAvailableImporters(path).Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal);
        return "default " + (AssetDatabase.GetDefaultImporter(path)?.FullName ?? "null") + " available [" + string.Join(",", available)
            + "] override " + (AssetDatabase.GetImporterOverride(path)?.FullName ?? "null");
    }

    static string Meta(string path) =>
        File.Exists(path + ".meta") ? System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path + ".meta"), "(?m)^guid: .*$", "guid: (guid)") : "missing";

    /// <summary>The logs since the last call (Log lines left out), then forgotten.</summary>
    static string Logs()
    {
        var text = string.Join("\n", logs.Where(l => !l.StartsWith("Log:", StringComparison.Ordinal)));
        logs.Clear();
        return text;
    }

    /// <summary>A class of the Editor assembly, which this one does not see.</summary>
    static Type EditorType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(name)).First(t => t != null);

    static void SetOverride(Type importer, string path) =>
        typeof(AssetDatabase).GetMethods().First(m => m.Name == "SetImporterOverride" && m.IsGenericMethodDefinition)
            .MakeGenericMethod(importer).Invoke(null, new object[] { path });

    /// <summary>What the asset is after an override change: main object, importer, override, .meta,
    /// the import lines written and the logs.</summary>
    static int OverrideRows(string dir, string prefix, string path, int seen)
    {
        Row(prefix + "/main", Describe(AssetDatabase.LoadMainAssetAtPath(path)));
        Row(prefix + "/importer", AssetImporter.GetAtPath(path)?.GetType().Name ?? "null");
        Row(prefix + "/get", AssetDatabase.GetImporterOverride(path)?.FullName ?? "null");
        Row(prefix + "/meta", Meta(path));
        seen = Lines(dir, prefix + "/lines", seen);
        Row(prefix + "/logs", Logs());
        return seen;
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
            // A .meta that sets an override from the start: whether its first import rewrote it.
            Row("meta/Assets/Data/Preset.ovr2", Meta("Assets/Data/Preset.ovr2"));
            foreach (var path in Assets)
                Row("importers/" + path, Importers(path));
            Logs();
            // An unchanged asset imported without ForceUpdate: the probed ones (whose imports load
            // them) and the quiet ones (whose imports do not).
            foreach (var path in new[] { "Assets/Textures/Sprite.png", "Assets/Data/Thing.gen", "Assets/Quiet/Quiet.png", "Assets/Quiet/Quiet.gen", "Assets/Textures/Painted.png" })
                AssetDatabase.ImportAsset(path);
            int plain = Lines(dir, "plain", seen);
            Row("plain/lines", (plain - seen).ToString());
            Row("plain/logs", Logs());
            seen = plain;
            // The objects loaded before a forced reimport, and the .meta, to compare after it.
            var forced = new[] { "Assets/Textures/Sprite.png", "Assets/Data/Thing.gen", "Assets/Quiet/Quiet.png", "Assets/Quiet/Quiet.gen", "Assets/Textures/Painted.png" };
            var before = forced.ToDictionary(p => p, p => AssetDatabase.LoadAllAssetsAtPath(p)
                .Select(o => (Object: o, Id: o.GetInstanceID(), Type: o.GetType(), Name: o.name)).OrderBy(e => e.Type.Name, StringComparer.Ordinal).ToArray());
            int width = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Data/Thing.gen").width;
            var spriteMeta = Meta("Assets/Textures/Sprite.png");
            // The painted texture, its first texel the postprocessor's, then set by a script: what
            // the reference loaded before has after the reimport.
            var painted = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Painted.png");
            int imported = painted.GetPixels32()[0].r;
            painted.SetPixel(0, 0, new Color32(5, 0, 0, 255));
            painted.Apply();
            Row("paint/before", "imported r " + imported + " set r " + painted.GetPixels32()[0].r);
            foreach (var path in forced)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            seen = Lines(dir, "reimport", seen);
            Row("reimport/logs", Logs());
            foreach (var path in forced)
            {
                var after = AssetDatabase.LoadAllAssetsAtPath(path);
                Row("reimport/count " + path, before[path].Length + " -> " + after.Length);
                foreach (var old in before[path])
                {
                    var fresh = after.FirstOrDefault(o => o != null && o.GetType() == old.Type && o.name == old.Name);
                    Row("kept/" + path + " " + old.Type.Name + " " + old.Name, "alive " + (old.Object != null) + " equal " + (old.Object == fresh)
                        + " same " + ReferenceEquals(old.Object, fresh) + " id " + (fresh == null ? "none" : fresh.GetInstanceID() == old.Id ? "same" : "changed"));
                }
            }
            var kept = (Texture2D)before["Assets/Data/Thing.gen"].First(e => e.Object is Texture2D).Object;
            Row("kept/Assets/Data/Thing.gen width", "before " + width + " old " + (kept != null ? kept.width.ToString() : "dead")
                + " fresh " + AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Data/Thing.gen").width);
            Row("paint/after", "old r " + (painted != null ? painted.GetPixels32()[0].r.ToString() : "dead")
                + " fresh r " + AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Painted.png").GetPixels32()[0].r);
            Row("reimport/meta Assets/Textures/Sprite.png", Meta("Assets/Textures/Sprite.png") == spriteMeta ? "unchanged" : Meta("Assets/Textures/Sprite.png"));
            // The quiet assets imported without ForceUpdate once more.
            foreach (var path in new[] { "Assets/Quiet/Quiet.png", "Assets/Quiet/Quiet.gen" })
                AssetDatabase.ImportAsset(path);
            int again = Lines(dir, "plain2", seen);
            Row("plain2/lines", (again - seen).ToString());
            seen = again;
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
            seen = Lines(dir, "forced/lines", seen);
            Row("forced/logs", Logs());
            // The override set through the API: what the asset then is, and its .meta.
            try
            {
                var over = EditorType("OverImporter");
                SetOverride(over, "Assets/Data/Plain.ovr2");
                seen = OverrideRows(dir, "set_override", "Assets/Data/Plain.ovr2", seen);
                Row("set_override/importers", Importers("Assets/Data/Plain.ovr2"));
                // An importer of a namespace.
                SetOverride(EditorType("ProbeImporters.SpacedImporter"), "Assets/Data/Second.ovr2");
                seen = OverrideRows(dir, "spaced", "Assets/Data/Second.ovr2", seen);
                // The same override again.
                SetOverride(over, "Assets/Data/Plain.ovr2");
                seen = OverrideRows(dir, "again", "Assets/Data/Plain.ovr2", seen);
                // An importer that does not import the file; a path without an asset.
                SetOverride(over, "Assets/Data/Mismatch.json");
                seen = OverrideRows(dir, "invalid", "Assets/Data/Mismatch.json", seen);
                SetOverride(over, "Assets/Data/Missing.ovr2");
                Row("missing/logs", Logs());
                Row("missing/get", AssetDatabase.GetImporterOverride("Assets/Data/Missing.ovr2")?.FullName ?? "null");
                // The extension's own importer named as the override.
                SetOverride(over, "Assets/Data/Own.over");
                seen = OverrideRows(dir, "own_default", "Assets/Data/Own.over", seen);
                AssetDatabase.ClearImporterOverride("Assets/Data/Second.ovr2");
                seen = OverrideRows(dir, "clear", "Assets/Data/Second.ovr2", seen);
                // Cleared once its .meta holds the override importer's section.
                AssetDatabase.ClearImporterOverride("Assets/Data/Plain.ovr2");
                seen = OverrideRows(dir, "clear_plain", "Assets/Data/Plain.ovr2", seen);
                // Cleared where none is set; a .meta that set one from the start.
                AssetDatabase.ClearImporterOverride("Assets/Data/Own.over");
                seen = OverrideRows(dir, "clear_none", "Assets/Data/Own.over", seen);
                AssetDatabase.ClearImporterOverride("Assets/Data/Preset.ovr2");
                seen = OverrideRows(dir, "clear_preset", "Assets/Data/Preset.ovr2", seen);
            }
            catch (Exception e) { Row("set_override", "throws " + e.GetType().Name + ": " + (e.InnerException ?? e).Message); }
        }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }
}
