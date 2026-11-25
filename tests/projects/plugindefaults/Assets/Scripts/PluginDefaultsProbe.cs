using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what the PluginImporter answers for plug-ins whose .meta holds only a
/// GUID (run.py stages them; the case list is PluginDefaultsCases.txt in the project folder) and
/// for a few written by hand, which managed ones the Editor compiles against, and the .meta each
/// plug-in gets when it is written again for its labels.
/// </summary>
static class PluginDefaultsProbe
{
    /// <summary>Build target names (BuildPipeline.GetBuildTargetName) and the importer's own two.</summary>
    static readonly string[] Platforms = { "Any", "Editor", "Win", "Win64", "OSXUniversal", "Linux64", "Android", "iOS", "tvOS", "VisionOS", "WebGL", "WindowsStoreApps" };

    /// <summary>The settings keys the platforms' extensions use.</summary>
    static readonly string[] Keys = { "CPU", "OS", "DefaultValueInitialized", "Is16KbAligned", "AddToEmbeddedBinaries", "CompileFlags", "FrameworkDependencies" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("PLUGIN_DEFAULTS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value)).Append("\"}\n");
        string Try(Func<string> read) { try { return read(); } catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message; } }
        string Bool(bool value) => value ? "1" : "0";

        var project = Path.GetDirectoryName(Application.dataPath);
        var cases = File.ReadAllLines(Path.Combine(project, "PluginDefaultsCases.txt")).Where(l => l.Length > 0).ToArray();
        var probed = new HashSet<string>(cases, StringComparer.Ordinal);
        foreach (var path in cases)
        {
            var importer = AssetImporter.GetAtPath(path);
            Put(path + ".importer", importer == null ? "null" : importer.GetType().Name);
            if (!(importer is PluginImporter plugin)) continue;
            Put(path + ".flags", Try(() => "any=" + Bool(plugin.GetCompatibleWithAnyPlatform())
                + " editor=" + Bool(plugin.GetCompatibleWithEditor())
                + " editorTarget=" + Bool(plugin.GetCompatibleWithEditor("OSXUniversal"))
                + " editorGroupTarget=" + Bool(plugin.GetCompatibleWithEditor("Standalone", "Linux64"))
                + " excludeEditor=" + Bool(plugin.GetExcludeEditorFromAnyPlatform())
                + " native=" + Bool(plugin.isNativePlugin)
                + " preloaded=" + Bool(plugin.isPreloaded)
                + " overridable=" + Bool(plugin.GetIsOverridable())
                + " defines=" + string.Join(";", plugin.DefineConstraints)));
            Put(path + ".compatible", Try(() => string.Join(",", Platforms.Where(p => plugin.GetCompatibleWithPlatform(p)))));
            Put(path + ".excluded", Try(() => string.Join(",", Platforms.Where(p => plugin.GetExcludeFromAnyPlatform(p)))));
            Put(path + ".data", Try(() => string.Join(";", Platforms.SelectMany(p => Keys.Select(k => (p, k, v: plugin.GetPlatformData(p, k))))
                .Where(e => !string.IsNullOrEmpty(e.v)).Select(e => e.p + "/" + e.k + "=" + e.v))));
            Put(path + ".editorData", Try(() => string.Join(";", Keys.Select(k => (k, v: plugin.GetEditorData(k))).Where(e => !string.IsNullOrEmpty(e.v)).Select(e => e.k + "=" + e.v))));
        }
        // The plug-ins of each platform: with "Any Platform" on unless excluded, or enabled for it.
        foreach (var platform in new[] { "Editor", "Android", "Win64", "WebGL" })
            Put("importers." + platform, Try(() => string.Join(",", PluginImporter.GetImporters(platform).Select(i => i.assetPath).Where(probed.Contains).OrderBy(p => p, StringComparer.Ordinal))));
        // The managed plug-ins the Editor compiles against.
        Put("precompiled", Try(() => string.Join(",", CompilationPipeline.GetPrecompiledAssemblyNames()
            .Select(n => AssetPath(CompilationPipeline.GetPrecompiledAssemblyPathFromAssemblyName(n), project))
            .Where(p => p != null && probed.Contains(p)).OrderBy(p => p, StringComparer.Ordinal))));
        // Written again for its labels, a plug-in's .meta gets its importer's section.
        foreach (var path in cases)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) { Put(path + ".labelled", "no asset"); continue; }
            AssetDatabase.SetLabels(asset, new[] { "probe" });
            var guid = AssetDatabase.AssetPathToGUID(path);
            Put(path + ".labelled", Try(() => File.ReadAllText(Path.Combine(project, path + ".meta")).Replace(guid, "<guid>")));
        }
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    /// <summary>A plug-in's path as the project names it (Assets/... or Packages/...).</summary>
    static string AssetPath(string path, string project)
    {
        if (string.IsNullOrEmpty(path)) return null;
        path = path.Replace('\\', '/');
        var root = project.Replace('\\', '/') + "/";
        if (path.StartsWith(root, StringComparison.Ordinal)) path = path.Substring(root.Length);
        return path;
    }

    static string Escape(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value)
        {
            if (c == '"' || c == '\\') builder.Append('\\').Append(c);
            else if (c == '\n') builder.Append("\\n");
            else if (c == '\r') builder.Append("\\r");
            else if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
            else builder.Append(c);
        }
        return builder.ToString();
    }
}
