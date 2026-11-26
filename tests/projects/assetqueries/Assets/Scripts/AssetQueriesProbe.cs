using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $ASSETQUERIES_OUT (tests/value_probe.py rows): AssetDatabase's answers
/// about paths, folders, main types, scene paths, AssetBundle names, import workers and
/// auto-refresh, for the assets of this project and for paths that are not assets, and the .meta
/// files after asset bundle names are set and removed. Values never contain instance ids.
/// </summary>
public class AssetQueriesProbe : MonoBehaviour
{
    const string Variable = "ASSETQUERIES_OUT";

    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    /// <summary>A value of an action, or what it throws, and (as key.logs) the logs it writes.</summary>
    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static string S(string value) => value == null ? "null" : "\"" + value + "\"";
    static string L(IEnumerable<string> values) => values == null ? "null" : "[" + string.Join(", ", values.Select(S)) + "]";
    static string T(Type type) => type == null ? "null" : type.FullName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("AssetQueriesProbe").AddComponent<AssetQueriesProbe>();
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

    /// <summary>Paths asked about, by row name: assets, folders, a package's, spellings of an
    /// asset's path, and paths that are no asset.</summary>
    static readonly (string Name, string Path)[] Paths =
    {
        ("assets", "Assets"),
        ("data", "Assets/Data"),
        ("data_slash", "Assets/Data/"),
        ("a", "Assets/Data/A.txt"),
        ("a_lower", "assets/data/a.txt"),
        ("a_slash", "Assets/Data/A.txt/"),
        ("a_backslash", "Assets\\Data\\A.txt"),
        ("a_dot", "Assets/./Data/A.txt"),
        ("a_space", " Assets/Data/A.txt"),
        ("missing", "Assets/Data/Missing.txt"),
        ("missing_folder", "Assets/Nothing"),
        ("sub", "Assets/Bundled/Sub"),
        ("packages", "Packages"),
        ("local", "Packages/com.probe.local"),
        ("local_runtime", "Packages/com.probe.local/Runtime"),
        ("local_manifest", "Packages/com.probe.local/package.json"),
        ("local_e", "Packages/com.probe.local/Runtime/E.txt"),
        ("module", "Packages/com.unity.modules.audio"),
        ("module_manifest", "Packages/com.unity.modules.audio/package.json"),
        ("unknown_package", "Packages/com.probe.none"),
        ("project_settings", "ProjectSettings/ProjectSettings.asset"),
        ("library", "Library"),
        ("empty", ""),
        ("relative_other", "Other/A.txt"),
    };

    static void Run()
    {
        var absolute = Path.GetFullPath("Assets/Data/A.txt");
        foreach (var (name, path) in Paths.Concat(new[] { ("absolute", absolute), ("null", (string)null) }))
        {
            Try("meta/" + name, () => S(AssetDatabase.GetTextMetaFilePathFromAssetPath(path)).Replace(absolute, "<absolute>"));
            Try("exists/" + name, () => AssetDatabase.AssetPathExists(path).ToString());
            Try("folder/" + name, () =>
            {
                bool found = AssetDatabase.TryGetAssetFolderInfo(path, out var root, out var immutable);
                return "found " + found + " root " + root + " immutable " + immutable;
            });
        }

        // The main type of an asset by its GUID.
        foreach (var (name, path) in new[] { ("a", "Assets/Data/A.txt"), ("mat", "Assets/Data/Mat.mat"), ("shader", "Assets/Data/Dep.shader"),
                     ("data", "Assets/Data"), ("scene", "Assets/Scenes/Probe.unity"), ("script", "Assets/Scripts/AssetQueriesProbe.cs"),
                     ("local_manifest", "Packages/com.probe.local/package.json"), ("local_e", "Packages/com.probe.local/Runtime/E.txt") })
            Try("main_type/" + name, () => T(AssetDatabase.GetMainAssetTypeFromGUID(new GUID(AssetDatabase.AssetPathToGUID(path)))));
        Try("main_type/unknown", () => T(AssetDatabase.GetMainAssetTypeFromGUID(new GUID("0123456789abcdef0123456789abcdef"))));
        Try("main_type/empty", () => T(AssetDatabase.GetMainAssetTypeFromGUID(default)));

        ScenePaths();
        Bundles();
        Workers();
        AutoRefresh();
        Internals();
    }

    /// <summary>GetAssetOrScenePath beside GetAssetPath for assets, objects of the open scene and
    /// objects made at run time.</summary>
    static void ScenePaths()
    {
        var saved = GameObject.Find("Saved");
        var made = new GameObject("Made");
        var hidden = new GameObject("Hidden") { hideFlags = HideFlags.HideAndDontSave };
        var kept = new GameObject("Kept");
        DontDestroyOnLoad(kept);
        var objects = new (string, Func<UnityEngine.Object>)[]
        {
            ("text", () => AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/A.txt")),
            ("material", () => AssetDatabase.LoadAssetAtPath<Material>("Assets/Data/Mat.mat")),
            ("scene_asset", () => AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Probe.unity")),
            ("folder", () => AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data")),
            ("saved", () => saved),
            ("saved_transform", () => saved != null ? saved.transform : null),
            ("made", () => made),
            ("hidden", () => hidden),
            ("dont_destroy", () => kept),
            ("instance", () => ScriptableObject.CreateInstance<ScriptableObject>()),
            ("new_material", () => new Material(Shader.Find("Probe/Dep"))),
            ("null", () => null),
        };
        foreach (var (name, get) in objects)
        {
            var target = get();
            Try("scene_path/" + name, () => S(AssetDatabase.GetAssetOrScenePath(target)));
            Try("asset_path/" + name, () => S(AssetDatabase.GetAssetPath(target)));
        }
    }

    static string Meta(string path) => File.ReadAllText(path + ".meta");

    /// <summary>AssetBundle names and the assets in them, then names set, cleared and removed.</summary>
    static void Bundles()
    {
        void Names(string key)
        {
            Try(key + "/all", () => L(AssetDatabase.GetAllAssetBundleNames()));
            Try(key + "/unused", () => L(AssetDatabase.GetUnusedAssetBundleNames()));
        }

        Names("bundles");
        foreach (var bundle in new[] { "bundle-a", "bundle-b", "bundle-b.hd", "BUNDLE-A", "folder-bundle", "folder-bundle.sd",
                     "mixed-case", "Mixed-Case", "mixed-case.", "mat-bundle", "shader-bundle", "missing", "" })
            Try("bundle_paths/" + (bundle.Length == 0 ? "empty" : bundle), () => L(AssetDatabase.GetAssetPathsFromAssetBundle(bundle)));
        foreach (var (bundle, asset) in new[] { ("bundle-a", "A"), ("bundle-a", "a"), ("bundle-a", "A.txt"), ("bundle-a", "Assets/Data/A.txt"),
                     ("bundle-a", "assets/data/a.txt"), ("bundle-a", "E"), ("bundle-b.hd", "B"), ("bundle-b", "B"),
                     ("folder-bundle.sd", "C"), ("folder-bundle.sd", "D"), ("missing", "A") })
            Try("bundle_asset/" + bundle + "/" + asset.Replace('/', '_'), () => L(AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(bundle, asset)));
        foreach (var (name, path) in new[] { ("a", "Assets/Data/A.txt"), ("b", "Assets/Data/B.txt"), ("plain", "Assets/Data/Plain.txt"),
                     ("upper", "Assets/Data/Upper.txt"), ("bundled", "Assets/Bundled"), ("c", "Assets/Bundled/C.txt"), ("d", "Assets/Bundled/Sub/D.txt"),
                     ("sub", "Assets/Bundled/Sub"), ("local_e", "Packages/com.probe.local/Runtime/E.txt"), ("missing", "Assets/Bundled/Missing.txt"), ("empty", "") })
        {
            Try("implicit/" + name, () => S(AssetDatabase.GetImplicitAssetBundleName(path)));
            Try("implicit_variant/" + name, () => S(AssetDatabase.GetImplicitAssetBundleVariantName(path)));
        }
        foreach (var bundle in new[] { "mat-bundle", "shader-bundle", "bundle-a", "missing" })
            foreach (var recursive in new[] { false, true })
                Try("dependencies/" + bundle + "/" + recursive, () => L(AssetDatabase.GetAssetBundleDependencies(bundle, recursive)));

        // A name given to an asset, then taken away: unused until removed.
        var plain = AssetImporter.GetAtPath("Assets/Data/Plain.txt");
        Try("set/name", () => { plain.assetBundleName = "Temp-Bundle"; return S(plain.assetBundleName) + " meta " + Meta("Assets/Data/Plain.txt"); });
        Names("set");
        Try("set/paths", () => L(AssetDatabase.GetAssetPathsFromAssetBundle("temp-bundle")));
        Try("cleared/name", () => { plain.assetBundleName = ""; return S(plain.assetBundleName) + " meta " + Meta("Assets/Data/Plain.txt"); });
        Names("cleared");
        Try("remove_unused", () => { AssetDatabase.RemoveUnusedAssetBundleNames(); return "done"; });
        Names("removed_unused");
        Try("remove_used", () => AssetDatabase.RemoveAssetBundleName("bundle-a", false).ToString());
        Try("remove_used/meta", () => Meta("Assets/Data/A.txt"));
        Names("remove_used");
        Try("remove_forced", () => AssetDatabase.RemoveAssetBundleName("bundle-a", true).ToString());
        Try("remove_forced/meta", () => Meta("Assets/Data/A.txt"));
        Try("remove_forced/importer", () => S(AssetImporter.GetAtPath("Assets/Data/A.txt").assetBundleName));
        Try("remove_forced/local_meta", () => Meta("Packages/com.probe.local/Runtime/E.txt"));
        Names("remove_forced");
        Try("remove_missing", () => AssetDatabase.RemoveAssetBundleName("missing", true).ToString());
        Try("remove_variant", () => AssetDatabase.RemoveAssetBundleName("bundle-b.hd", true).ToString());
        Try("remove_variant/meta", () => Meta("Assets/Data/B.txt"));
        Names("remove_variant");
    }

    /// <summary>The import worker counts: AssetDatabase's and the user settings', set and forced.</summary>
    static void Workers()
    {
        Try("workers/default", () => "desired " + AssetDatabase.DesiredWorkerCount + " user " + EditorUserSettings.desiredImportWorkerCount
            + " standby " + EditorUserSettings.standbyImportWorkerCount + " processors " + SystemInfo.processorCount);
        Try("workers/set", () =>
        {
            AssetDatabase.DesiredWorkerCount = 3;
            return "desired " + AssetDatabase.DesiredWorkerCount + " user " + EditorUserSettings.desiredImportWorkerCount;
        });
        Try("workers/user_set", () =>
        {
            EditorUserSettings.desiredImportWorkerCount = 2;
            return "desired " + AssetDatabase.DesiredWorkerCount + " user " + EditorUserSettings.desiredImportWorkerCount;
        });
        Try("workers/negative", () =>
        {
            AssetDatabase.DesiredWorkerCount = -1;
            return "desired " + AssetDatabase.DesiredWorkerCount + " user " + EditorUserSettings.desiredImportWorkerCount;
        });
        // A small count before forcing: forcing a negative one makes the Editor start import
        // workers without end.
        Try("workers/force", () =>
        {
            AssetDatabase.DesiredWorkerCount = 1;
            AssetDatabase.ForceToDesiredWorkerCount();
            return "desired " + AssetDatabase.DesiredWorkerCount;
        });
        Try("workers/user_settings_file", () => File.Exists("UserSettings/EditorUserSettings.asset")
            ? string.Join("\n", File.ReadAllLines("UserSettings/EditorUserSettings.asset").Where(l => l.Contains("ImportWorker")))
            : "absent");
    }

    /// <summary>DisallowAutoRefresh/AllowAutoRefresh, balanced and not.</summary>
    static void AutoRefresh()
    {
        Try("autorefresh/balanced", () => { AssetDatabase.DisallowAutoRefresh(); AssetDatabase.AllowAutoRefresh(); return "done"; });
        Try("autorefresh/twice", () =>
        {
            AssetDatabase.DisallowAutoRefresh(); AssetDatabase.DisallowAutoRefresh();
            AssetDatabase.AllowAutoRefresh(); AssetDatabase.AllowAutoRefresh();
            return "done";
        });
        Try("autorefresh/extra_allow", () => { AssetDatabase.AllowAutoRefresh(); return "done"; });
        Try("autorefresh/after_extra", () => { AssetDatabase.DisallowAutoRefresh(); AssetDatabase.AllowAutoRefresh(); return "done"; });
    }

    /// <summary>Internal members packages call through their own assemblies (by reflection here).</summary>
    static void Internals()
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        object Call(string name, params object[] args) =>
            typeof(AssetDatabase).GetMethod(name, Static, null, args.Select(a => a.GetType()).ToArray(), null).Invoke(null, args);
        Try("internal/v1", () => Call("IsV1Enabled").ToString());
        Try("internal/v2", () => Call("IsV2Enabled").ToString());
        Try("internal/guid_a", () => Call("AssetPathToGUID_Internal", "Assets/Data/A.txt").ToString());
        Try("internal/guid_a_matches", () => (Call("AssetPathToGUID_Internal", "Assets/Data/A.txt").ToString() == AssetDatabase.AssetPathToGUID("Assets/Data/A.txt")).ToString());
        Try("internal/guid_missing", () => Call("AssetPathToGUID_Internal", "Assets/Data/Missing.txt").ToString());
        Try("internal/guid_folder_matches", () => (Call("AssetPathToGUID_Internal", "Assets/Data").ToString() == AssetDatabase.AssetPathToGUID("Assets/Data")).ToString());
    }
}
