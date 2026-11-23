using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The folders and files of packages as the asset database answers them: a built-in package's
/// root folder and file, a registry package's root folder (which has no .meta file), its manifest,
/// a subfolder and a file in it, the Packages folder itself, a package that is not there, and an
/// Assets folder and file to compare: whether there is a GUID and the path it leads back to,
/// IsValidFolder, the main asset's type, name and hide flags, its path (by object and by instance
/// ID), IsMainAsset, Contains, and ProjectWindowUtil.GetAncestors of it (as paths). Recorded in
/// Play mode.
/// </summary>
[InitializeOnLoad]
public static class PackageFoldersProbe
{
    const string Variable = "PACKAGE_FOLDERS_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();

    static PackageFoldersProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) => s_Logs.Add(type + ": " + message.Split('\n')[0]);
            EditorApplication.delayCall += Run;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Run()
    {
        try
        {
            var paths = new[]
            {
                ("builtinRoot", "Packages/com.unity.modules.audio"),
                ("builtinFile", "Packages/com.unity.modules.audio/package.json"),
                ("registryRoot", "Packages/com.unity.ai.navigation"),
                ("registryManifest", "Packages/com.unity.ai.navigation/package.json"),
                ("registryFolder", "Packages/com.unity.ai.navigation/Runtime"),
                ("registryFile", "Packages/com.unity.ai.navigation/Runtime/NavMeshLink.cs"),
                ("packages", "Packages"),
                ("assetsFolder", "Assets/Editor"),
                ("assetsFile", "Assets/Editor/PackageFoldersProbe.cs"),
                ("missingRoot", "Packages/com.unity.missing"),
            };
            foreach (var (key, path) in paths) Query(key, path);
            // A package root folder has no .meta file: its GUID as the asset database gives it.
            Check("guid.builtinRoot", () => AssetDatabase.AssetPathToGUID("Packages/com.unity.modules.audio"));
            Check("guid.registryRoot", () => AssetDatabase.AssetPathToGUID("Packages/com.unity.ai.navigation"));
            // Whether package roots are listed with all paths, and found by a folder search.
            Check("allPaths.roots", () => string.Join("|", AssetDatabase.GetAllAssetPaths().Where(p => p == "Packages/com.unity.modules.audio" || p == "Packages/com.unity.ai.navigation" || p == "Packages").OrderBy(p => p, StringComparer.Ordinal)));
            Check("find.roots", () => string.Join("|", AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Packages/com.unity.ai.navigation" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.IndexOf('/', "Packages/".Length) < 0 || p.Count(c => c == '/') == 2).OrderBy(p => p, StringComparer.Ordinal)));
            Check("find.unscoped", () => string.Join("|", AssetDatabase.FindAssets("navigation").Concat(AssetDatabase.FindAssets("modules.audio")).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Count(c => c == '/') == 1).OrderBy(p => p, StringComparer.Ordinal)));
            Check("find.packagesFolder", () => string.Join("|", AssetDatabase.FindAssets("navigation", new[] { "Packages" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Count(c => c == '/') == 1).OrderBy(p => p, StringComparer.Ordinal)));
            Check("subFolders.packages", () => string.Join("|", AssetDatabase.GetSubFolders("Packages").Where(p => p.Contains("modules.audio") || p.Contains("ai.navigation"))));
            Check("subFolders.order", () => string.Join("|", AssetDatabase.GetSubFolders("Assets/Sub")));
            Check("subFolders.missing", () => AssetDatabase.GetSubFolders("Assets/Missing").Length);
            Check("subFolders.file", () => AssetDatabase.GetSubFolders("Assets/Editor/PackageFoldersProbe.cs").Length);
            Check("subFolders.naturalOrder", () =>
            {
                var folders = AssetDatabase.GetSubFolders("Assets/Sub");
                var sorted = folders.ToArray();
                Array.Sort(sorted, EditorUtility.NaturalCompare);
                return folders.SequenceEqual(sorted);
            });
            // EditorUtility.NaturalCompare's sign for pairs that sort apart by case, digit runs,
            // leading zeros, punctuation, spaces and length.
            var pairs = new[]
            {
                ("a", "B"), ("_c", "A"), ("C9", "C10"), ("a2", "a10"), ("a02", "a2"), ("a002", "a02"), ("a", "a"),
                ("abc", "ABC"), ("ABC", "abc"), ("a b", "a_b"), ("a-b", "a_b"), ("a.b", "a_b"), ("x1y2", "x1y10"),
                ("", "a"), ("a", "ab"), ("10", "9"), ("1.5", "1.10"), ("Z", "a"), ("é", "f"), ("a 1", "a1"),
                ("file10.txt", "file9.txt"), ("0", "00"), ("a01b", "a1a"), ("99999999999999999999", "1"),
                ("É", "é"), ("Ä", "b"),
            };
            foreach (var (a, b) in pairs)
                Check("natural." + a + "|" + b, () => Math.Sign(EditorUtility.NaturalCompare(a, b)));
            Check("subFolders.registryRoot", () => string.Join("|", AssetDatabase.GetSubFolders("Packages/com.unity.ai.navigation")));
        }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); }
        Put("logs", string.Join(" | ", s_Logs));
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Query(string key, string path)
    {
        Check(key + ".hasGuid", () => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)));
        Check(key + ".guidBack", () => AssetDatabase.GUIDToAssetPath(AssetDatabase.AssetPathToGUID(path)));
        Check(key + ".isValidFolder", () => AssetDatabase.IsValidFolder(path));
        Check(key + ".mainType", () => AssetDatabase.GetMainAssetTypeAtPath(path)?.FullName ?? "<null>");
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        Check(key + ".main", () => main == null ? "<null>" : main.GetType().FullName + ":" + main.name + ":" + main.hideFlags);
        if (main == null) return;
        Check(key + ".path", () => AssetDatabase.GetAssetPath(main));
        Check(key + ".pathById", () => AssetDatabase.GetAssetPath(main.GetInstanceID()));
        Check(key + ".isMain", () => AssetDatabase.IsMainAsset(main));
        Check(key + ".contains", () => AssetDatabase.Contains(main));
        Check(key + ".localId", () => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(main, out string _, out long id) ? id.ToString(CultureInfo.InvariantCulture) : "<none>");
        Check(key + ".ancestors", () => "[" + string.Join(",", ProjectWindowUtil.GetAncestors(main.GetInstanceID())
            .Select(a => a == int.MaxValue ? "<int.MaxValue>" : a == 0 ? "<0>" : AssetDatabase.GetAssetPath(a)).OrderBy(p => p, StringComparer.Ordinal)) + "]");
    }

    static void Check(string key, Func<object> read)
    {
        object value;
        try { value = read(); }
        catch (Exception e) { value = "threw " + e.GetType().FullName + ": " + e.Message.Split('\n')[0]; }
        Put(key, value ?? "<null>");
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
