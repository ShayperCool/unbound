using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Without version control: what AssetDatabase.MakeEditable, CanOpenForEdit and
/// IsMetaFileOpenForEdit answer (with IsOpenForEdit beside them) for asset, meta, folder, missing,
/// built-in and registry package, settings, Library, absolute and outside paths, read-only files, objects and lists, and
/// whether MakeEditable changes a read-only file. With the processors of tests/projects/openforedithooks
/// in the project, which AssetModificationProcessor callbacks each call reaches and how their
/// answers are used. Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class OpenForEditProbe
{
    const string Variable = "OPEN_FOR_EDIT_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static string s_Project;

    static OpenForEditProbe()
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

    /// <summary>The calls the hook processors saw (tests/projects/openforedithooks), or null
    /// without them.</summary>
    static List<string> Calls => (List<string>)Type.GetType("OpenForEditHooks, Assembly-CSharp-Editor")?.GetField("Calls").GetValue(null);

    static void Run()
    {
        try
        {
            s_Project = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            if (Calls == null) Plain();
            else if (Type.GetType("OpenForEditThirdProcessor, Assembly-CSharp-Editor") != null) Order();
            else Hooks();
        }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); }
        Put("logs", string.Join(" | ", s_Logs));
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static readonly (string Key, string Path)[] s_Paths =
    {
        ("asset", "Assets/Probe.txt"),
        ("meta", "Assets/Probe.txt.meta"),
        ("folder", "Assets"),
        ("missing", "Assets/Missing.asset"),
        ("builtinFile", "Packages/com.unity.modules.audio/package.json"),
        ("builtinMeta", "Packages/com.unity.modules.audio/package.json.meta"),
        ("builtinFolder", "Packages/com.unity.modules.audio"),
        ("registryFile", "Packages/com.unity.ai.navigation/package.json"),
        ("registryFolder", "Packages/com.unity.ai.navigation"),
        ("manifest", "Packages/manifest.json"),
        ("settings", "ProjectSettings/ProjectSettings.asset"),
        ("library", "Library/missing"),
        ("backslash", "Assets\\Probe.txt"),
        ("outside", "/tmp/unbound-openforedit-missing"),
        ("empty", ""),
        ("null", null),
    };

    static void Plain()
    {
        var paths = new List<(string Key, string Path)>(s_Paths)
        {
            ("absolute", Application.dataPath + "/Probe.txt"),
            // What the Input System's check-out helper passes for a path in Assets.
            ("absoluteFolder", Application.dataPath + "/"),
        };
        foreach (var (key, path) in paths)
        {
            Check("make." + key, () => AssetDatabase.MakeEditable(path));
            Check("can." + key, () => { bool yes = AssetDatabase.CanOpenForEdit(path, out var message); return yes + ":" + Clean(message); });
            Check("can.force." + key, () => AssetDatabase.CanOpenForEdit(path, StatusQueryOptions.ForceUpdate));
            Check("is." + key, () => { bool yes = AssetDatabase.IsOpenForEdit(path, out var message); return yes + ":" + Clean(message); });
        }

        // A read-only file, imported and not: what the queries say, and whether MakeEditable
        // makes it writable.
        foreach (var (key, file) in new[] { ("readOnlyAsset", "Assets/Probe.txt"), ("readOnlyFile", "Assets/ReadOnly.txt") })
        {
            if (!File.Exists(file)) File.WriteAllText(file, "read only\n");
            var attributes = File.GetAttributes(file);
            try
            {
                File.SetAttributes(file, attributes | FileAttributes.ReadOnly);
                Check(key + ".can", () => { bool yes = AssetDatabase.CanOpenForEdit(file, out var message); return yes + ":" + Clean(message); });
                Check(key + ".is", () => { bool yes = AssetDatabase.IsOpenForEdit(file, out var message); return yes + ":" + Clean(message); });
                Check(key + ".make", () => AssetDatabase.MakeEditable(file));
                Check(key + ".readOnlyAfter", () => (File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
                var list = new List<string> { "stale" };
                File.SetAttributes(file, attributes | FileAttributes.ReadOnly);
                Check(key + ".makeList", () => AssetDatabase.MakeEditable(new[] { file }, "probe", list) + ":" + string.Join("|", list));
                Check(key + ".makeList.readOnlyAfter", () => (File.GetAttributes(file) & FileAttributes.ReadOnly) != 0);
            }
            finally { File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly); }
        }
        File.Delete("Assets/ReadOnly.txt");

        // Objects: an asset, a package asset, a scene object, one in memory, none.
        var probe = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Probe.txt");
        var package = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.unity.modules.audio/package.json");
        var sceneObject = new GameObject("Probe Scene Object");
        var memory = new Texture2D(2, 2);
        foreach (var (key, o) in new (string, Object)[] { ("asset", probe), ("package", package), ("sceneObject", sceneObject), ("memory", memory), ("null", null) })
        {
            Check("object.loaded." + key, () => o != null);
            Check("object.can." + key, () => { bool yes = AssetDatabase.CanOpenForEdit(o, out var message); return yes + ":" + Clean(message); });
            Check("object.can.force." + key, () => AssetDatabase.CanOpenForEdit(o, StatusQueryOptions.ForceUpdate));
            Check("object.meta." + key, () => { bool yes = AssetDatabase.IsMetaFileOpenForEdit(o, out var message); return yes + ":" + Clean(message); });
            Check("object.meta.plain." + key, () => AssetDatabase.IsMetaFileOpenForEdit(o));
            Check("object.is." + key, () => { bool yes = AssetDatabase.IsOpenForEdit(o, out var message); return yes + ":" + Clean(message); });
        }
        Object.DestroyImmediate(sceneObject);
        Object.DestroyImmediate(memory);

        // Lists: results, what an earlier list content becomes, and null arguments.
        var mixed = new[] { "Assets/Probe.txt", "Packages/com.unity.modules.audio/package.json", "Assets/Missing.asset", "Packages/manifest.json", "Packages/com.unity.modules.audio/package.json.meta" };
        var stale = new List<string> { "stale" };
        Check("make.bulk", () => AssetDatabase.MakeEditable(mixed, "probe", stale) + ":" + string.Join("|", stale));
        stale = new List<string> { "stale" };
        Check("make.bulk.editable", () => AssetDatabase.MakeEditable(new[] { "Assets/Probe.txt", "Assets/Missing.asset" }, "probe", stale) + ":" + string.Join("|", stale));
        stale = new List<string> { "stale" };
        Check("make.bulk.empty", () => AssetDatabase.MakeEditable(new string[0], "probe", stale) + ":" + string.Join("|", stale));
        Check("make.bulk.noList", () => AssetDatabase.MakeEditable(mixed));
        Check("make.bulk.noList.editable", () => AssetDatabase.MakeEditable(new[] { "Assets/Probe.txt" }));
        Check("make.bulk.nullPaths", () => AssetDatabase.MakeEditable((string[])null));
        stale = new List<string> { "stale" };
        Check("make.bulk.nullEntry", () => AssetDatabase.MakeEditable(new[] { null, "Assets/Probe.txt", "Packages/com.unity.modules.audio/package.json" }, null, stale) + ":" + string.Join("|", stale));
        stale = new List<string> { "stale" };
        Check("can.bulk", () => { AssetDatabase.CanOpenForEdit(mixed, stale); return string.Join("|", stale); });
        Check("can.bulk.force", () => { AssetDatabase.CanOpenForEdit(mixed, stale, StatusQueryOptions.ForceUpdate); return string.Join("|", stale); });
        stale = new List<string> { "stale" };
        Check("can.bulk.empty", () => { AssetDatabase.CanOpenForEdit(new string[0], stale); return stale.Count; });
        Check("can.bulk.nullPaths", () => { AssetDatabase.CanOpenForEdit(null, stale); return "ok"; });
        Check("can.bulk.nullList", () => { AssetDatabase.CanOpenForEdit(mixed, null); return "ok"; });
        stale = new List<string> { "stale" };
        Check("can.bulk.nullEntry", () => { AssetDatabase.CanOpenForEdit(new[] { null, "Assets/Probe.txt", "Packages/com.unity.modules.audio/package.json" }, stale); return string.Join("|", stale); });
    }

    /// <summary>With the hook processors: per call, its answer and the callbacks it reached.
    /// The processors refuse paths naming "Locked" (list IsOpenForEdit), "Held" (single-path
    /// IsOpenForEdit, with a message), "Mute" (single-path IsOpenForEdit, with a null message),
    /// "NoCan" (list CanOpenForEdit), "Barred" (single-path CanOpenForEdit, with a message) and
    /// "NoMake" (MakeEditable); no two processors have the same callback, so no row depends on
    /// their order.</summary>
    static void Hooks()
    {
        var hookPaths = new[]
        {
            ("asset", "Assets/Probe.txt"),
            ("locked", "Assets/Locked.txt"),
            ("held", "Assets/Held.txt"),
            ("lockedNoCan", "Assets/LockedNoCan.txt"),
            ("heldNoCan", "Assets/HeldNoCan.txt"),
            ("heldBarred", "Assets/HeldBarred.txt"),
            ("mute", "Assets/Mute.txt"),
            ("noCan", "Assets/NoCan.txt"),
            ("noMake", "Assets/NoMake.txt"),
            ("builtinFile", "Packages/com.unity.modules.audio/package.json"),
            ("empty", ""),
            // Paths outside Assets: which callbacks they reach.
            ("library", "Library/Held.txt"),
            ("settings", "ProjectSettings/Held.asset"),
            ("manifest", "Packages/manifest.json"),
            ("absolute", Application.dataPath + "/Held.txt"),
            ("outside", "/tmp/Held.txt"),
            ("backslash", "Assets\\Held.txt"),
            ("userSettings", "UserSettings/Held.asset"),
            ("assetsRoot", "Assets"),
            ("settingsRoot", "ProjectSettings"),
            ("packagesRoot", "Packages"),
            ("libraryNoMake", "Library/NoMake.txt"),
            ("outsideNoMake", "/tmp/NoMake.txt"),
        };
        foreach (var (key, path) in hookPaths)
        {
            Hooked("is." + key, () => { bool yes = AssetDatabase.IsOpenForEdit(path, out var message); return yes + ":" + Clean(message); });
            Hooked("can." + key, () => { bool yes = AssetDatabase.CanOpenForEdit(path, out var message); return yes + ":" + Clean(message); });
            Hooked("make." + key, () => AssetDatabase.MakeEditable(path));
        }
        var all = new[] { "Assets/Probe.txt", "Assets/Locked.txt", "Assets/Held.txt", "Assets/LockedNoCan.txt", "Assets/NoCan.txt", "Assets/NoMake.txt", "Packages/com.unity.modules.audio/package.json" };
        var list = new List<string> { "stale" };
        Hooked("is.bulk", () => { AssetDatabase.IsOpenForEdit(all, list); return string.Join("|", list); });
        list = new List<string> { "stale" };
        Hooked("can.bulk", () => { AssetDatabase.CanOpenForEdit(all, list); return string.Join("|", list); });
        list = new List<string> { "stale" };
        Hooked("make.bulk", () => AssetDatabase.MakeEditable(all, "probe prompt", list) + ":" + string.Join("|", list));
        Hooked("make.bulk.noList", () => AssetDatabase.MakeEditable(all));
        list = new List<string> { "stale" };
        Hooked("make.bulk.blanks", () => AssetDatabase.MakeEditable(new[] { null, "", "Assets/NoMake.txt" }, "", list) + ":" + string.Join("|", list));
        list = new List<string> { "stale" };
        Hooked("is.bulk.blanks", () => { AssetDatabase.IsOpenForEdit(new[] { null, "", "Assets/Locked.txt" }, list); return string.Join("|", list); });
        list = new List<string> { "stale" };
        Hooked("can.bulk.blanks", () => { AssetDatabase.CanOpenForEdit(new[] { null, "", "Assets/LockedNoCan.txt" }, list); return string.Join("|", list); });
        // Lists no list callback refuses: the single-path callbacks for each path.
        var held = new[] { "Assets/Probe.txt", "Assets/Held.txt", "Assets/HeldNoCan.txt", "Assets/HeldBarred.txt", "Assets/Mute.txt" };
        list = new List<string> { "stale" };
        Hooked("is.bulk.held", () => { AssetDatabase.IsOpenForEdit(held, list); return string.Join("|", list); });
        list = new List<string> { "stale" };
        Hooked("can.bulk.held", () => { AssetDatabase.CanOpenForEdit(held, list); return string.Join("|", list); });
        var barred = new[] { "Assets/Held.txt", "Assets/HeldBarred.txt", "Assets/Probe.txt" };
        list = new List<string> { "stale" };
        Hooked("can.bulk.barred", () => { AssetDatabase.CanOpenForEdit(barred, list); return string.Join("|", list); });
        var kinds = new[] { "Library/Held.txt", "/tmp/Held.txt", "ProjectSettings/Held.asset", "Assets/Probe.txt" };
        list = new List<string> { "stale" };
        Hooked("is.bulk.kinds", () => { AssetDatabase.IsOpenForEdit(kinds, list); return string.Join("|", list); });
        list = new List<string> { "stale" };
        Hooked("make.bulk.editable", () => AssetDatabase.MakeEditable(new[] { "Assets/Probe.txt" }, null, list) + ":" + string.Join("|", list));
        var probe = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Probe.txt");
        Hooked("object.is", () => AssetDatabase.IsOpenForEdit(probe));
        Hooked("object.can", () => AssetDatabase.CanOpenForEdit(probe));
        Hooked("object.meta", () => { bool yes = AssetDatabase.IsMetaFileOpenForEdit(probe, out var message); return yes + ":" + Clean(message); });
        // Objects whose asset has no .meta file (the Assets folder), a folder that has one and one
        // in memory. (A scene object is left out: the batch Editor plays an untitled scene.)
        var assetsFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
        var editorFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Editor");
        var memory = new Texture2D(2, 2);
        foreach (var (key, o) in new (string, Object)[] { ("assetsFolder", assetsFolder), ("editorFolder", editorFolder), ("memory", memory) })
        {
            Check("object.loaded." + key, () => o != null);
            Hooked("object.meta." + key, () => { bool yes = AssetDatabase.IsMetaFileOpenForEdit(o, out var message); return yes + ":" + Clean(message); });
            Hooked("object.is." + key, () => AssetDatabase.IsOpenForEdit(o));
        }
        Object.DestroyImmediate(memory);
    }

    /// <summary>With two processors having the same callbacks (tests/projects/openforeditorder),
    /// each refusing its own path: the answers (which do not depend on the order between them)
    /// and whether the calls follow the order TypeCache lists the processors in up to the first
    /// that refuses (Unity documents no order between processors, so neither the order nor the
    /// number of calls, which depends on it, is recorded).</summary>
    static void Order()
    {
        var order = new List<string>();
        foreach (var type in TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>())
            if (type.Name == "OpenForEditSecondProcessor") order.Add("second");
            else if (type.Name == "OpenForEditThirdProcessor") order.Add("third");
        void Ordered(string key, string[] paths, Func<object> read)
        {
            Calls.Clear();
            Check(key, read);
            var expected = new List<string>();
            foreach (var name in order)
            {
                expected.Add(name);
                if (Array.Exists(paths, p => p != null && p.Contains(name == "second" ? "Second" : "Third"))) break;
            }
            var called = Calls.ConvertAll(c => c.Substring(0, c.IndexOf('.')));
            Put(key + ".typeCacheOrder", string.Join(",", called) == string.Join(",", expected));
        }
        foreach (var (key, path) in new[] { ("asset", "Assets/Probe.txt"), ("second", "Assets/Second.txt"), ("third", "Assets/Third.txt") })
        {
            Ordered("is." + key, new[] { path }, () => { bool yes = AssetDatabase.IsOpenForEdit(path, out var message); return yes + ":" + Clean(message); });
            Ordered("make." + key, new[] { path }, () => AssetDatabase.MakeEditable(path));
        }
        var second = new[] { "Assets/Probe.txt", "Assets/Second.txt" };
        var list = new List<string> { "stale" };
        Ordered("is.bulk.second", second, () => { AssetDatabase.IsOpenForEdit(second, list); return string.Join("|", list); });
        var third = new[] { "Assets/Third.txt", "Assets/Probe.txt" };
        list = new List<string> { "stale" };
        Ordered("is.bulk.third", third, () => { AssetDatabase.IsOpenForEdit(third, list); return string.Join("|", list); });
        var makeSecond = new[] { "Assets/Second.txt", "Assets/Probe.txt" };
        list = new List<string> { "stale" };
        Ordered("make.bulk.second", makeSecond, () => AssetDatabase.MakeEditable(makeSecond, "probe", list) + ":" + string.Join("|", list));
        var accepted = new[] { "Assets/Probe.txt" };
        list = new List<string> { "stale" };
        Ordered("make.bulk.accepted", accepted, () => AssetDatabase.MakeEditable(accepted, "probe", list) + ":" + string.Join("|", list));
    }

    static void Hooked(string key, Func<object> read)
    {
        Calls.Clear();
        Check(key, read);
        Put(key + ".calls", Clean(string.Join(" ; ", Calls)));
    }

    /// <summary>The project's own directory as "&lt;project&gt;" (the runs use private copies).</summary>
    internal static string Clean(string text) => text == null ? "<null>" : s_Project == null ? text : text.Replace(s_Project, "<project>");

    static void Check(string key, Func<object> read)
    {
        object value;
        try { value = read(); }
        catch (Exception e)
        {
            value = "threw " + e.GetType().FullName + ": " + Clean(e.Message.Split('\n')[0])
                + (e is ArgumentException argument ? " (" + (argument.ParamName ?? "<null>") + ")" : "");
        }
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
