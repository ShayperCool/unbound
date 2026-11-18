using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>
/// The packages the Package Manager resolved for a project without Packages/packages-lock.json,
/// recorded in Play mode: each registered package's version, source and whether the project asks
/// for it directly. See README.md.
/// </summary>
[InitializeOnLoad]
public static class PackageResolveProbe
{
    const string Variable = "PACKAGE_RESOLVE_OUT";

    public static void Record() => EditorApplication.EnterPlaymode();

    static PackageResolveProbe()
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
        // Built-in engine modules (com.unity.modules.*) are left out: the Editor registers every
        // module whatever the project asks for.
        var packages = PackageInfo.GetAllRegisteredPackages()
            .Where(p => !p.name.StartsWith("com.unity.modules.", StringComparison.Ordinal))
            .OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
        Put(s, "names", string.Join(" ", packages.Select(p => p.name)));
        foreach (var p in packages)
            Put(s, "package." + p.name, $"{p.version} source {p.source} direct {p.isDirectDependency}");
        s.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s.ToString());
        EditorApplication.Exit(0);
    }
}
