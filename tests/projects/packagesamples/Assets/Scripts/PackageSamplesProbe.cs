using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $PACKAGESAMPLES_OUT (tests/value_probe.py rows): what
/// PackageManager.UI.Sample answers for an embedded package's samples (FindByPackage with its
/// version, another, none and for packages without samples or missing) and what Import writes
/// into Assets/Samples, with each option, beside an earlier version's import.
/// </summary>
public class PackageSamplesProbe : MonoBehaviour
{
    const string Variable = "PACKAGESAMPLES_OUT";

    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    /// <summary>The package's folder read "<package>" and the project's absolute path "<project>"
    /// (they differ between captures and runs).</summary>
    static string Relative(string text)
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.probe.samples")?.resolvedPath;
        if (text != null && !string.IsNullOrEmpty(package)) text = text.Replace(package, "<package>");
        return text?.Replace(Path.GetFullPath("."), "<project>");
    }

    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, Relative(result));
        if (logs.Count > 0) Row(key + ".logs", Relative(string.Join("\n---\n", logs)));
    }

    static string S(string value) => value == null ? "null" : "\"" + value + "\"";

    static string D(Sample s) => "displayName " + S(s.displayName) + " description " + S(s.description) + " resolvedPath " + S(s.resolvedPath)
        + " importPath " + S(s.importPath) + " interactiveImport " + s.interactiveImport + " isImported " + s.isImported;

    static string Find(string name, string version)
    {
        var samples = Sample.FindByPackage(name, version);
        if (samples == null) return "null";
        var list = samples.ToList();
        return "count " + list.Count + string.Concat(list.Select(s => "\n" + D(s)));
    }

    /// <summary>The files and folders under Assets/Samples, relative, sorted.</summary>
    static string Files()
    {
        const string root = "Assets/Samples";
        if (!Directory.Exists(root)) return "<none>";
        return string.Join("\n", Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(p => p.Replace('\\', '/') + (Directory.Exists(p) ? "/" : "")).OrderBy(p => p, StringComparer.Ordinal));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("PackageSamplesProbe").AddComponent<PackageSamplesProbe>();
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

    static Sample Named(string displayName) => Sample.FindByPackage("com.probe.samples", "").First(s => s.displayName == displayName);

    static void Run()
    {
        foreach (var (name, package, version) in new[]
                 {
                     ("version", "com.probe.samples", "1.2.3"), ("empty", "com.probe.samples", ""), ("null", "com.probe.samples", null),
                     ("other", "com.probe.samples", "1.0.0"), ("plain", "com.probe.plain", ""), ("module", "com.unity.modules.audio", ""),
                     ("missing", "com.probe.missing", ""), ("missingVersion", "com.probe.missing", "1.0.0"), ("nullName", null, ""),
                 })
            Try("find/" + name, () => Find(package, version));

        Try("files/before", Files);
        Try("import/basic", () => Named("Basic Sample").Import().ToString());
        Try("files/basic", Files);
        Try("imported/basic", () => Named("Basic Sample").isImported.ToString());
        Try("import/again", () => Named("Basic Sample").Import().ToString());
        // An earlier version's import of the same sample, then an import that overrides it.
        Try("old/create", () =>
        {
            Directory.CreateDirectory("Assets/Samples/Probe_ Samples/0.9.0/Basic Sample");
            File.WriteAllText("Assets/Samples/Probe_ Samples/0.9.0/Basic Sample/Old.txt", "old\n");
            AssetDatabase.Refresh();
            return "done";
        });
        Try("files/old", Files);
        Try("import/override", () => Named("Basic Sample").Import(Sample.ImportOptions.OverridePreviousImports).ToString());
        Try("files/override", Files);
        Try("import/inter", () => Named("Inter: Active?").Import(Sample.ImportOptions.HideImportWindow).ToString());
        Try("import/gone", () => Named("Gone").Import().ToString());
        Try("files/end", Files);
        Try("find/after", () => Find("com.probe.samples", ""));
    }
}
