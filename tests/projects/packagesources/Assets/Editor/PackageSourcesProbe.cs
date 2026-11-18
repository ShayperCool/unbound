using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>
/// The packages the Package Manager resolved from a scoped registry that needs authentication, a
/// local tarball and a git repository, recorded in Play mode: each registered package's version and
/// source and the text of the `Marker.txt` it ships. See README.md.
/// </summary>
[InitializeOnLoad]
public static class PackageSourcesProbe
{
    const string Variable = "PACKAGE_SOURCES_OUT";

    public static void Record() => EditorApplication.EnterPlaymode();

    static PackageSourcesProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
        };
    }

    static void Put(StringBuilder s, string key, string value) =>
        s.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");

    static void Run()
    {
        var s = new StringBuilder();
        var packages = PackageInfo.GetAllRegisteredPackages()
            .Where(p => p.name.StartsWith("com.probe.", StringComparison.Ordinal))
            .OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
        Put(s, "names", string.Join(" ", packages.Select(p => p.name)));
        foreach (var p in packages)
        {
            var marker = Path.Combine(p.resolvedPath, "Marker.txt");
            Put(s, "package." + p.name, $"{p.version} source {p.source}");
            Put(s, "marker." + p.name, File.Exists(marker) ? File.ReadAllText(marker).Trim() : "<none>");
        }
        s.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s.ToString());
        EditorApplication.Exit(0);
    }
}
