using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// What the Editor writes into a project, and when: the .meta files of new assets, importer
/// settings and labels saved into them, AssetDatabase file operations, settings changed through
/// the API and the package lock. Each step records the files it may touch right after its call
/// (see README.md); run.py adds what is on disk after the Editor quit. The steps run in Play mode
/// (`Record`, keys `play.*`) and in Edit mode (the EditMode test ProjectWritesEditTests, `edit.*`).
/// </summary>
[InitializeOnLoad]
public static class ProjectWritesProbe
{
    const string Variable = "PROJECT_WRITES_OUT";
    // A 4x4 RGBA PNG, the same bytes in every run.
    const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAL0lEQVR42hXKMQEAMBACMYQhjBFR+PtepyyR1Fq94FByz26DQymMMHD5Y4wxcPgAuL0fObgpuyQAAAAASUVORK5CYII=";
    const string Obj = "o Quad\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nvn 0 0 -1\nf 1/1/1 2/2/1 3/3/1\nf 1/1/1 3/3/1 4/4/1\n";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();
    static readonly Dictionary<string, string> s_Initial = new Dictionary<string, string>();
    static readonly Regex s_Guid = new Regex("\\b[0-9a-f]{32}\\b");
    static readonly Regex s_Time = new Regex("timeCreated: [0-9]+");
    static readonly Regex s_FileId = new Regex("(?<=&|fileID: )-?[0-9]{5,}");
    // A per-platform settings entry of a platform module (Android, iOS, ...): which ones a new
    // .meta lists is a fact of the Editor installation, checked separately (Platforms).
    static readonly Regex s_PlatformEntry = new Regex("(?m)^( *)- serializedVersion: [0-9]+\n\\1  buildTarget: (?!DefaultTexturePlatform\\b|Standalone\\b)(\\S+)\n(?:\\1  .*\n)*");
    static readonly List<string> s_Platforms = new List<string>();
    static int s_Frames;
    static string s_LockBefore;
    static string s_Mode = "play";

    static readonly string[] s_Settings =
    {
        "ProjectSettings/ProjectSettings.asset", "ProjectSettings/EditorSettings.asset",
        "ProjectSettings/EditorBuildSettings.asset", "Packages/manifest.json", "Packages/packages-lock.json",
    };

    static ProjectWritesProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += () =>
            {
                // Once per run: a session that starts again (Unbound starts the session again to
                // compile new defines) finds the values written and quits.
                if (Done()) { EditorApplication.Exit(0); return; }
                Steps("play");
                EditorApplication.update += WaitForResolve;
            };
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    // ------------------------------------------------------------------ recording

    static void Put(string key, object value)
    {
        var text = value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            string[] a => "[" + string.Join(",", a) + "]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        s_Out.Append("{\"k\":\"").Append(s_Mode).Append('.').Append(key).Append("\",\"v\":\"")
            .Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"))
            .Append("\"}\n");
    }

    static string Read(string path) => System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;

    /// <summary>A file's text as both hosts can compare it: GUIDs as the asset path they name now,
    /// a creation time as a placeholder, and local file ids as their order in the file.</summary>
    static string Normalize(string text)
    {
        text = s_Guid.Replace(text, m =>
        {
            var path = AssetDatabase.GUIDToAssetPath(m.Value);
            return "<guid:" + (string.IsNullOrEmpty(path) ? "?" : path) + ">";
        });
        text = s_Time.Replace(text, "timeCreated: <time>");
        text = s_PlatformEntry.Replace(text, m => { s_Platforms.Add(m.Groups[2].Value); return ""; });
        text = MainDocumentFirst(text);
        var ids = new Dictionary<string, string>();
        return s_FileId.Replace(text, m =>
        {
            if (m.Value == "11400000" || m.Value == "11500000" || m.Value == "10900000") return m.Value;
            if (!ids.TryGetValue(m.Value, out var id)) ids[m.Value] = id = "<id" + (ids.Count + 1) + ">";
            return id;
        });
    }

    /// <summary>A file's documents with the main object's first: the others' file ids are random,
    /// and the Editor sorts documents by file id (IdsAscending records that order).</summary>
    static string MainDocumentFirst(string text)
    {
        bool ends = text.EndsWith("\n", StringComparison.Ordinal);
        var parts = (ends ? text.Substring(0, text.Length - 1) : text).Split(new[] { "\n--- " }, StringSplitOptions.None);
        if (parts.Length < 3) return text;
        var documents = parts.Skip(1).ToList();
        var main = documents.FindIndex(d => d.StartsWith("!u!114 &11400000\n", StringComparison.Ordinal));
        if (main <= 0) return text;
        var first = documents[main];
        documents.RemoveAt(main);
        documents.Insert(0, first);
        return parts[0] + string.Concat(documents.Select(d => "\n--- " + d)) + (ends ? "\n" : "");
    }

    static readonly Regex s_DocumentId = new Regex("(?m)^--- !u![0-9]+ &(-?[0-9]+)");

    /// <summary>Whether a file's documents are in ascending file id order.</summary>
    static bool IdsAscending(string text)
    {
        var ids = s_DocumentId.Matches(text).Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        return ids.SequenceEqual(ids.OrderBy(i => i));
    }

    static string Digest(string text)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
    }

    /// <summary>A file of the project now: absent, or its normalized text.</summary>
    static void Snap(string step, string path)
    {
        var text = Read(path);
        s_Platforms.Clear();
        Put(step + "." + path, text == null ? "<none>" : Normalize(text));
        if (text != null && s_DocumentId.Matches(text).Count > 1) Put(step + "." + path + ".idsAscending", IdsAscending(text));
        if (s_Platforms.Count > 0 || (text != null && text.Contains("platformSettings:")))
            Put(step + "." + path + ".platforms", string.Join(",", s_Platforms) == string.Join(",", Installed()) ? "the installed modules" : "differ: " + string.Join(",", s_Platforms));
    }

    // The same list and entry pattern as tests/projects/assetpostprocessors (PostprocessorProbe.cs); keep them in step.
    /// <summary>The platform modules of this Editor installation, in the build settings' order,
    /// by the names texture settings give them.</summary>
    static IEnumerable<string> Installed()
    {
        var platforms = new (BuildTargetGroup group, BuildTarget target, string name)[]
        {
            (BuildTargetGroup.Android, BuildTarget.Android, "Android"), (BuildTargetGroup.iOS, BuildTarget.iOS, "iOS"),
            (BuildTargetGroup.WebGL, BuildTarget.WebGL, "WebGL"), (BuildTargetGroup.WSA, BuildTarget.WSAPlayer, "Windows Store Apps"),
            (BuildTargetGroup.tvOS, BuildTarget.tvOS, "tvOS"), (BuildTargetGroup.VisionOS, BuildTarget.VisionOS, "VisionOS"),
        };
        return platforms.Where(p => BuildPipeline.IsBuildTargetSupported(p.group, p.target)).Select(p => p.name);
    }

    /// <summary>A settings file now, against its text when the probe started: the lines gone
    /// and the lines added (as multisets), and the digest of the whole normalized text.</summary>
    static void Settings(string step, string path)
    {
        var text = Read(path);
        if (text == null) { Put(step + "." + path, "<none>"); return; }
        var before = s_Initial.TryGetValue(path, out var initial) && initial != null ? Lines(initial) : new List<string>();
        var after = Lines(text);
        var gone = new List<string>(before);
        var added = new List<string>();
        foreach (var line in after)
            if (!gone.Remove(line)) added.Add(line);
        Put(step + "." + path + ".gone", string.Join("\n", gone));
        Put(step + "." + path + ".added", string.Join("\n", added));
        Put(step + "." + path + ".sha256", Digest(Normalize(text)));
        Put(step + "." + path + ".crlf", text.Contains("\r\n"));
    }

    static List<string> Lines(string text) => Normalize(text).Split('\n').ToList();

    static void Guard(string step, Action action)
    {
        try { action(); }
        catch (Exception e) { Put(step + ".threw", e.GetType().FullName + ": " + e.Message); }
        // GUIDs in the logs as the assets they name.
        Put(step + ".logs", s_Guid.Replace(string.Join(" | ", s_Logs), m =>
        {
            var path = AssetDatabase.GUIDToAssetPath(m.Value);
            return "<guid:" + (string.IsNullOrEmpty(path) ? "?" : path) + ">";
        }));
        s_Logs.Clear();
    }

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        System.IO.File.WriteAllText(path, text);
    }

    static void WritePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        System.IO.File.WriteAllBytes(path, Convert.FromBase64String(Png));
    }

    // ------------------------------------------------------------------ steps

    /// <summary>Every step, in the given mode ("play" or "edit"), recorded under its keys.</summary>
    public static void Steps(string mode)
    {
        s_Mode = mode;
        foreach (var path in s_Settings) s_Initial[path] = Read(path);
        Application.logMessageReceived += (condition, stack, type) => s_Logs.Add(type + ": " + condition.Split('\n')[0]);
        // What the Editor wrote when it opened the project: the .meta files of the files run.py
        // left without one, and the package lock run.py removed.
        Guard("00.open", () =>
        {
            foreach (var path in new[] { "Assets/Startup.meta", "Assets/Startup/Tex.png.meta", "Assets/Startup/Startup.cs.meta",
                                         "Assets/Startup/Image.psd.meta", "Packages/packages-lock.json", "Assets/Crlf/Tex.png.meta", "Assets/Crlf/Notes.txt.meta" })
                Snap("00.open", path);
        });
        Guard("01.refresh", NewFilesAndRefresh);
        Guard("02.import", ImportOne);
        Guard("03.folder", CreateFolder);
        Guard("04.create", CreateAsset);
        Guard("05.copy", CopyMoveRename);
        Guard("06.delete", Delete);
        Guard("06.folder", CopyFolder);
        Guard("06.crlf", Crlf);
        Guard("06.readonly", ReadOnlyPackage);
        Guard("07.batch", Batch);
        Guard("08.importer", ImporterChange);
        Guard("09.labels", Labels);
        Guard("10.preprocessor", Preprocessor);
        // New defines compile the scripts again; in Play mode that comes last (WaitForResolve).
        if (mode == "edit") Guard("11.defines", Defines);
        Guard("12.buildsettings", BuildSettings);
        Guard("13.save", Save);
        Guard("14.unsaved", Unsaved);
        Guard("14.remove", RemoveObject);
        Guard("15.manifest", Manifest);
    }

    /// <summary>Whether this run's values are written already.</summary>
    public static bool Done() => System.IO.File.Exists(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"));

    /// <summary>Whether the package lock changed since the manifest step.</summary>
    public static bool LockChanged() => Read("Packages/packages-lock.json") != s_LockBefore;

    /// <summary>Writes the recorded values; the probe is complete.</summary>
    public static void Finish()
    {
        Put("logs.end", string.Join(" | ", s_Logs));
        s_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        System.IO.File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
    }

    static void NewFilesAndRefresh()
    {
        WritePng("Assets/New/Tex.png");
        Write("Assets/New/Model.obj", Obj);
        Write("Assets/New/Notes.txt", "notes\n");
        Directory.CreateDirectory("Assets/New/Empty");
        var paths = new[] { "Assets/New.meta", "Assets/New/Tex.png.meta", "Assets/New/Model.obj.meta", "Assets/New/Notes.txt.meta", "Assets/New/Empty.meta" };
        foreach (var path in paths) Snap("01.refresh.before", path);
        Put("01.refresh.before.guid", AssetDatabase.AssetPathToGUID("Assets/New/Tex.png"));
        AssetDatabase.Refresh();
        foreach (var path in paths) Snap("01.refresh.after", path);
        Put("01.refresh.after.guidmatches", Read("Assets/New/Tex.png.meta")?.Contains("guid: " + AssetDatabase.AssetPathToGUID("Assets/New/Tex.png")));
        Put("01.refresh.after.type", AssetDatabase.GetMainAssetTypeAtPath("Assets/New/Tex.png"));
    }

    static void ImportOne()
    {
        WritePng("Assets/Two/Other.png");
        AssetDatabase.ImportAsset("Assets/Two/Other.png");
        Snap("02.import", "Assets/Two/Other.png.meta");
        Snap("02.import", "Assets/Two.meta");
        Put("02.import.folder", AssetDatabase.IsValidFolder("Assets/Two"));
    }

    static void CreateFolder()
    {
        var guid = AssetDatabase.CreateFolder("Assets", "Made");
        Put("03.folder.returns", AssetDatabase.GUIDToAssetPath(guid));
        Snap("03.folder", "Assets/Made.meta");
        Put("03.folder.again", AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder("Assets", "Made")));
        Snap("03.folder", "Assets/Made 1.meta");
        Put("03.folder.missingParent", AssetDatabase.CreateFolder("Assets/Missing", "Child"));
    }

    static void CreateAsset()
    {
        var data = ScriptableObject.CreateInstance<ProjectWritesData>();
        data.number = 11;
        AssetDatabase.CreateAsset(data, "Assets/Made/Data.asset");
        Snap("04.create", "Assets/Made/Data.asset");
        Snap("04.create", "Assets/Made/Data.asset.meta");
        Put("04.create.path", AssetDatabase.GetAssetPath(data));
        var sub = ScriptableObject.CreateInstance<ProjectWritesData>();
        sub.name = "Sub";
        sub.text = "sub";
        AssetDatabase.AddObjectToAsset(sub, data);
        data.other = sub;
        EditorUtility.SetDirty(data);
        Snap("04.create.added", "Assets/Made/Data.asset");
        Put("04.create.added.subpath", AssetDatabase.GetAssetPath(sub));
        AssetDatabase.SaveAssets();
        Snap("04.create.saved", "Assets/Made/Data.asset");
        Snap("04.create.saved", "Assets/Made/Data.asset.meta");
        Put("04.create.saved.all", string.Join(",", AssetDatabase.LoadAllAssetsAtPath("Assets/Made/Data.asset").Select(o => o.name + ":" + o.GetType().Name).OrderBy(s => s, StringComparer.Ordinal)));
    }

    /// <summary>What an object says of itself as part, or no part, of an asset.</summary>
    static string Membership(UnityEngine.Object o) =>
        "alive=" + (o != null) + (o == null ? "" : "|path=" + AssetDatabase.GetAssetPath(o) + "|contains=" + AssetDatabase.Contains(o) + "|main=" + AssetDatabase.IsMainAsset(o)
            + "|sub=" + AssetDatabase.IsSubAsset(o) + "|persistent=" + EditorUtility.IsPersistent(o) + "|dirty=" + EditorUtility.IsDirty(o) + "|hideFlags=" + o.hideFlags + "|name=" + o.name);

    static string All(string path) =>
        string.Join(",", AssetDatabase.LoadAllAssetsAtPath(path).Select(o => o == null ? "<null>" : o.name + ":" + o.GetType().Name).OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>RemoveObjectFromAsset: an added object taken out again, what the file has before
    /// and after the next save, a reference to the removed object, removing twice, adding it
    /// back, an object that is no part of an asset, and the main object.</summary>
    static void RemoveObject()
    {
        const string path = "Assets/Made/Removal.asset";
        var main = ScriptableObject.CreateInstance<ProjectWritesData>();
        main.number = 21;
        AssetDatabase.CreateAsset(main, path);
        // One added object: the file ids of added objects are random and order the documents.
        var first = ScriptableObject.CreateInstance<ProjectWritesData>();
        first.name = "First";
        AssetDatabase.AddObjectToAsset(first, main);
        main.other = first;
        EditorUtility.SetDirty(main);
        AssetDatabase.SaveAssets();
        Snap("14.remove.before", path);
        Put("14.remove.before.first", Membership(first));

        AssetDatabase.RemoveObjectFromAsset(first);
        Put("14.remove.removed.first", Membership(first));
        Put("14.remove.removed.main", Membership(main));
        Put("14.remove.removed.all", All(path));
        Put("14.remove.removed.reference", main.other == first);
        Snap("14.remove.removed", path);
        AssetDatabase.SaveAssets();
        Snap("14.remove.saved", path);
        Put("14.remove.saved.first", Membership(first));
        Put("14.remove.saved.all", All(path));

        AssetDatabase.RemoveObjectFromAsset(first);
        Put("14.remove.again.first", Membership(first));
        var loose = ScriptableObject.CreateInstance<ProjectWritesData>();
        Put("14.remove.loose.created", Membership(loose));
        loose.name = "Loose";
        Put("14.remove.loose.before", Membership(loose));
        AssetDatabase.RemoveObjectFromAsset(loose);
        Put("14.remove.loose", Membership(loose));
        try { AssetDatabase.RemoveObjectFromAsset(null); Put("14.remove.null", "returns"); }
        catch (Exception e) { Put("14.remove.null", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }

        AssetDatabase.AddObjectToAsset(first, main);
        Put("14.remove.readded.first", Membership(first));
        Put("14.remove.readded.all", All(path));
        AssetDatabase.SaveAssets();
        Snap("14.remove.readded", path);
        Put("14.remove.readded.saved.first", Membership(first));
        Put("14.remove.readded.saved.all", All(path));

        AssetDatabase.RemoveObjectFromAsset(main);
        Put("14.remove.main", Membership(main));
        Put("14.remove.main.first", Membership(first));
        Put("14.remove.main.all", All(path));
        Put("14.remove.main.mainAsset", AssetDatabase.LoadMainAssetAtPath(path) == null ? "null" : AssetDatabase.LoadMainAssetAtPath(path).name);
        Snap("14.remove.main", path);
        AssetDatabase.SaveAssets();
        Snap("14.remove.main.saved", path);
        Put("14.remove.main.saved.first", Membership(first));
        Put("14.remove.main.saved.all", All(path));
        Put("14.remove.main.saved.mainAsset", AssetDatabase.LoadMainAssetAtPath(path) == null ? "null" : AssetDatabase.LoadMainAssetAtPath(path).name);
        Put("14.remove.main.saved.guid", AssetDatabase.AssetPathToGUID(path) == "" ? "none" : "kept");
    }

    static void CopyMoveRename()
    {
        Put("05.copy.returns", AssetDatabase.CopyAsset("Assets/Made/Data.asset", "Assets/Made/Copy.asset"));
        Snap("05.copy", "Assets/Made/Copy.asset");
        Snap("05.copy", "Assets/Made/Copy.asset.meta");
        Put("05.copy.distinct", AssetDatabase.AssetPathToGUID("Assets/Made/Copy.asset") != AssetDatabase.AssetPathToGUID("Assets/Made/Data.asset"));
        var guid = AssetDatabase.AssetPathToGUID("Assets/Made/Copy.asset");
        Put("05.move.returns", AssetDatabase.MoveAsset("Assets/Made/Copy.asset", "Assets/New/Moved.asset"));
        Snap("05.move", "Assets/Made/Copy.asset");
        Snap("05.move", "Assets/Made/Copy.asset.meta");
        Snap("05.move", "Assets/New/Moved.asset.meta");
        Put("05.move.sameguid", AssetDatabase.AssetPathToGUID("Assets/New/Moved.asset") == guid);
        Put("05.move.toMissing", AssetDatabase.MoveAsset("Assets/New/Moved.asset", "Assets/Nowhere/Moved.asset"));
        Put("05.move.onto", AssetDatabase.MoveAsset("Assets/New/Moved.asset", "Assets/Made/Data.asset"));
        Put("05.rename.returns", AssetDatabase.RenameAsset("Assets/New/Moved.asset", "Renamed"));
        Snap("05.rename", "Assets/New/Renamed.asset.meta");
        Snap("05.rename", "Assets/New/Renamed.asset");
        AssetDatabase.CopyAsset("Assets/Made/Data.asset", "Assets/New/Taken.asset");
        Put("05.rename.taken", AssetDatabase.RenameAsset("Assets/New/Renamed.asset", "Taken"));
        Put("05.rename.otherExtension", AssetDatabase.RenameAsset("Assets/New/Taken.asset", "Tex"));
        Snap("05.rename", "Assets/New/Tex.asset.meta");
        Put("05.rename.folder", AssetDatabase.RenameAsset("Assets/Made 1", "Renamed Folder"));
        Snap("05.rename", "Assets/Renamed Folder.meta");
        foreach (var path in new[] { "Assets/Made/Data.asset", "Assets/Made/Fresh.asset", "Assets/Made", "Assets/New/Tex.png", "Assets/Made/Data 1.asset" })
            Put("05.unique." + path, AssetDatabase.GenerateUniqueAssetPath(path));
        // With "Data 1" taken too, and names that end in a number or a space and a number.
        AssetDatabase.CopyAsset("Assets/Made/Data.asset", "Assets/Made/Data 1.asset");
        AssetDatabase.CopyAsset("Assets/Made/Data.asset", "Assets/Made/Data2.asset");
        foreach (var path in new[] { "Assets/Made/Data.asset", "Assets/Made/Data 1.asset", "Assets/Made/Data2.asset", "Assets/made/data.ASSET", "Assets/Nowhere/X.asset", "Made/Data.asset" })
            Put("05.unique2." + path, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.DeleteAsset("Assets/Made/Data 1.asset");
        AssetDatabase.DeleteAsset("Assets/Made/Data2.asset");
    }

    static void Delete()
    {
        var deleted = AssetDatabase.AssetPathToGUID("Assets/New/Renamed.asset");
        Put("06.delete.returns", AssetDatabase.DeleteAsset("Assets/New/Renamed.asset"));
        Snap("06.delete", "Assets/New/Renamed.asset");
        Snap("06.delete", "Assets/New/Renamed.asset.meta");
        // The default options include recently deleted assets; OnlyExistingAssets does not.
        Put("06.delete.guid", AssetDatabase.AssetPathToGUID("Assets/New/Renamed.asset") == deleted ? "the deleted asset's" : AssetDatabase.AssetPathToGUID("Assets/New/Renamed.asset"));
        Put("06.delete.guid.existing", AssetDatabase.AssetPathToGUID("Assets/New/Renamed.asset", AssetPathToGUIDOptions.OnlyExistingAssets));
        Put("06.delete.path", AssetDatabase.GUIDToAssetPath(deleted));
        Put("06.delete.loaded", AssetDatabase.LoadMainAssetAtPath("Assets/New/Renamed.asset") == null ? "null" : "an object");
        Put("06.delete.missing", AssetDatabase.DeleteAsset("Assets/New/Nothing.asset"));
        Put("06.delete.folder", AssetDatabase.DeleteAsset("Assets/Renamed Folder"));
        Snap("06.delete", "Assets/Renamed Folder.meta");
        Put("06.delete.folderExists", Directory.Exists("Assets/Renamed Folder"));
        Put("06.trash.returns", AssetDatabase.MoveAssetToTrash("Assets/New/Notes.txt"));
        Snap("06.trash", "Assets/New/Notes.txt");
        Snap("06.trash", "Assets/New/Notes.txt.meta");
    }

    static void CopyFolder()
    {
        // A folder with a hidden file (as Finder leaves one), a nested folder and an asset.
        Write("Assets/Tree/.hidden", "hidden\n");
        Write("Assets/Tree/Leaf/Note.txt", "leaf\n");
        AssetDatabase.Refresh();
        Put("06.folder.returns", AssetDatabase.CopyAsset("Assets/Tree", "Assets/Tree Copy"));
        foreach (var path in new[] { "Assets/Tree Copy.meta", "Assets/Tree Copy/Leaf.meta", "Assets/Tree Copy/Leaf/Note.txt.meta", "Assets/Tree Copy/.hidden", "Assets/Tree Copy/.hidden.meta" })
            Snap("06.folder", path);
        Put("06.folder.distinct", AssetDatabase.AssetPathToGUID("Assets/Tree Copy/Leaf/Note.txt") != AssetDatabase.AssetPathToGUID("Assets/Tree/Leaf/Note.txt"));
    }

    static void Crlf()
    {
        // run.py stages Assets/Crlf with CRLF .meta files.
        AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath("Assets/Crlf/Notes.txt"), new[] { "crlf" });
        Snap("06.crlf.labels", "Assets/Crlf/Notes.txt.meta");
        Put("06.crlf.labels.crlf", Read("Assets/Crlf/Notes.txt.meta")?.Contains("\r\n"));
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Crlf/Tex.png");
        Put("06.crlf.importer.readable", importer?.isReadable);
        Put("06.crlf.importer.dirty", AssetDatabase.WriteImportSettingsIfDirty("Assets/Crlf/Tex.png"));
        Snap("06.crlf.importer.unchanged", "Assets/Crlf/Tex.png.meta");
        if (importer != null)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        Snap("06.crlf.importer", "Assets/Crlf/Tex.png.meta");
        Put("06.crlf.importer.crlf", Read("Assets/Crlf/Tex.png.meta")?.Contains("\r\n"));
        Put("06.crlf.copy", AssetDatabase.CopyAsset("Assets/Crlf/Notes.txt", "Assets/Crlf/Notes Copy.txt"));
        Snap("06.crlf.copy", "Assets/Crlf/Notes Copy.txt.meta");
        Put("06.crlf.copy.crlf", Read("Assets/Crlf/Notes Copy.txt.meta")?.Contains("\r\n"));
    }

    static void ReadOnlyPackage()
    {
        // The test framework is a built-in package: read-only.
        // (A file the Package Manager does not rewrite in its copy of the package.)
        const string file = "Packages/com.unity.test-framework/LICENSE.md";
        Put("06.readonly.exists", AssetDatabase.AssetPathToGUID(file).Length == 32);
        Put("06.readonly.move", AssetDatabase.MoveAsset(file, "Assets/LICENSE.md"));
        Put("06.readonly.validate", AssetDatabase.ValidateMoveAsset(file, "Assets/LICENSE.md"));
        Put("06.readonly.copy", AssetDatabase.CopyAsset(file, "Packages/com.unity.test-framework/copy.md"));
        var folder = AssetDatabase.CreateFolder("Packages/com.unity.test-framework", "New");
        Put("06.readonly.folder", folder.Length == 32 ? "a GUID" : folder);
        Put("06.readonly.delete", AssetDatabase.DeleteAsset(file));
        Put("06.readonly.stillThere", AssetDatabase.AssetPathToGUID(file, AssetPathToGUIDOptions.OnlyExistingAssets).Length == 32);
        Put("06.readonly.copyOut", AssetDatabase.CopyAsset(file, "Assets/LICENSE copy.md"));
        Snap("06.readonly", "Assets/LICENSE copy.md.meta");
    }

    static void Batch()
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            // A file no importer claims beside an ordinary one in the same batch.
            Write("Assets/New/Odd.xyz", "odd\n");
            Write("Assets/New/Even.txt", "even\n");
            AssetDatabase.ImportAsset("Assets/New/Odd.xyz");
            AssetDatabase.ImportAsset("Assets/New/Even.txt");
            Write("Assets/New/Batch.txt", "batch\n");
            AssetDatabase.ImportAsset("Assets/New/Batch.txt");
            Snap("07.batch.inside", "Assets/New/Batch.txt.meta");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ProjectWritesData>(), "Assets/Made/Batched.asset");
            Snap("07.batch.inside", "Assets/Made/Batched.asset");
            Snap("07.batch.inside", "Assets/Made/Batched.asset.meta");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        Snap("07.batch.after", "Assets/New/Batch.txt.meta");
        Snap("07.batch.after", "Assets/Made/Batched.asset.meta");
        Snap("07.batch.after", "Assets/New/Even.txt.meta");
        Put("07.batch.after.even", AssetDatabase.AssetPathToGUID("Assets/New/Even.txt").Length == 32);
        // Files changed outside the AssetDatabase: Refresh finds a deleted one, a copied one with
        // its .meta, and a .meta whose labels changed.
        var deleted = AssetDatabase.AssetPathToGUID("Assets/New/Batch.txt");
        System.IO.File.Delete("Assets/New/Batch.txt");
        System.IO.File.Delete("Assets/New/Batch.txt.meta");
        System.IO.File.Copy("Assets/New/Even.txt", "Assets/New/Copied.txt");
        System.IO.File.WriteAllText("Assets/New/Copied.txt.meta", "fileFormatVersion: 2\nguid: 9d4f3e0c9b2d4e5f8a7b6c5d4e3f2a10\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        System.IO.File.WriteAllText("Assets/New/Even.txt.meta", Read("Assets/New/Even.txt.meta").Replace("TextScriptImporter:", "labels:\n- outside\nTextScriptImporter:"));
        // A copy whose .meta repeats the original's GUID.
        var even = AssetDatabase.AssetPathToGUID("Assets/New/Even.txt");
        System.IO.File.Copy("Assets/New/Even.txt", "Assets/New/Twin.txt");
        System.IO.File.Copy("Assets/New/Even.txt.meta", "Assets/New/Twin.txt.meta");
        AssetDatabase.Refresh();
        Put("07.outside.twin.original", AssetDatabase.AssetPathToGUID("Assets/New/Even.txt") == even);
        var twin = AssetDatabase.AssetPathToGUID("Assets/New/Twin.txt");
        Put("07.outside.twin", twin.Length == 32 ? (twin == even ? "the original's GUID" : "a GUID of its own") : twin);
        Snap("07.outside", "Assets/New/Twin.txt.meta");
        Put("07.outside.deleted", AssetDatabase.AssetPathToGUID("Assets/New/Batch.txt", AssetPathToGUIDOptions.OnlyExistingAssets));
        Put("07.outside.deletedPath", AssetDatabase.GUIDToAssetPath(deleted));
        Put("07.outside.copied", AssetDatabase.AssetPathToGUID("Assets/New/Copied.txt"));
        Put("07.outside.copiedFound", string.Join(",", AssetDatabase.FindAssets("Copied")));
        Put("07.outside.labels", AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath("Assets/New/Even.txt")));
    }

    static void ImporterChange()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/New/Tex.png");
        importer.isReadable = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        Snap("08.importer.set", "Assets/New/Tex.png.meta");
        importer.SaveAndReimport();
        Snap("08.importer.saved", "Assets/New/Tex.png.meta");
        var model = (ModelImporter)AssetImporter.GetAtPath("Assets/New/Model.obj");
        model.globalScale = 2;
        model.importNormals = ModelImporterNormals.Calculate;
        model.SaveAndReimport();
        Snap("08.importer.model", "Assets/New/Model.obj.meta");
        var other = AssetImporter.GetAtPath("Assets/Two/Other.png");
        other.userData = "user data";
        other.assetBundleName = "Bundle";
        Snap("08.importer.bundle", "Assets/Two/Other.png.meta");
        AssetDatabase.WriteImportSettingsIfDirty("Assets/Two/Other.png");
        Snap("08.importer.written", "Assets/Two/Other.png.meta");
    }

    static void Labels()
    {
        var texture = AssetDatabase.LoadMainAssetAtPath("Assets/New/Tex.png");
        AssetDatabase.SetLabels(texture, new[] { "Zeta", "alpha", "Zeta" });
        Snap("09.labels.set", "Assets/New/Tex.png.meta");
        Put("09.labels.get", AssetDatabase.GetLabels(texture));
        var data = AssetDatabase.LoadMainAssetAtPath("Assets/Made/Data.asset");
        AssetDatabase.SetLabels(data, new[] { "data" });
        Snap("09.labels.asset", "Assets/Made/Data.asset.meta");
        AssetDatabase.ClearLabels(texture);
        Snap("09.labels.cleared", "Assets/New/Tex.png.meta");
        // A script's .meta ends after its GUID; labels YAML would read otherwise.
        AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath("Assets/Startup/Startup.cs"), new[] { "script" });
        Snap("09.labels.script", "Assets/Startup/Startup.cs.meta");
        AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath("Assets/Startup/Notes.txt"), new[] { "#x", "a: b", "[y]", "plain label" });
        Snap("09.labels.quoted", "Assets/Startup/Notes.txt.meta");
        Put("09.labels.quoted.get", AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath("Assets/Startup/Notes.txt")));
    }

    static void Preprocessor()
    {
        WritePng("Assets/New/PreTex.png");
        AssetDatabase.ImportAsset("Assets/New/PreTex.png");
        Snap("10.preprocessor", "Assets/New/PreTex.png.meta");
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/New/PreTex.png");
        Put("10.preprocessor.maxTextureSize", importer.maxTextureSize);
        Put("10.preprocessor.isReadable", importer.isReadable);
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        Snap("10.preprocessor.saved", "Assets/New/PreTex.png.meta");
    }

    static void Defines()
    {
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, "PROBE_B;PROBE_A");
        Settings("11.defines", "ProjectSettings/ProjectSettings.asset");
        Put("11.defines.get", PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone));
    }

    static void BuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Missing.unity", false),
        };
        Settings("12.buildsettings", "ProjectSettings/EditorBuildSettings.asset");
        EditorSettings.projectGenerationRootNamespace = "Probe";
        Settings("12.editorsettings", "ProjectSettings/EditorSettings.asset");
    }

    static void Save()
    {
        AssetDatabase.SaveAssets();
        foreach (var path in s_Settings) Settings("13.save", path);
        Snap("13.save", "Assets/New/Tex.png.meta");
    }

    static void Unsaved()
    {
        // Changed after the last SaveAssets: run.py reads what the Editor wrote when it quit.
        PlayerSettings.companyName = "Probe Company";
        EditorSettings.projectGenerationRootNamespace = "Unsaved";
        Settings("14.unsaved", "ProjectSettings/ProjectSettings.asset");
        Settings("14.unsaved", "ProjectSettings/EditorSettings.asset");
    }

    static void Manifest()
    {
        s_LockBefore = Read("Packages/packages-lock.json");
        var manifest = Read("Packages/manifest.json");
        Write("Packages/manifest.json", new Regex("\"dependencies\"\\s*:\\s*\\{").Replace(manifest, "\"dependencies\": {\n    \"com.unity.modules.ai\": \"1.0.0\",", 1));
        UnityEditor.PackageManager.Client.Resolve();
        Settings("15.manifest.called", "Packages/packages-lock.json");
    }

    static void WaitForResolve()
    {
        // The lock as the Editor left it a few frames after the resolve (the Editor resolves
        // asynchronously; a domain reload may follow, after which nothing more is recorded).
        if (++s_Frames < 30 && !LockChanged()) return;
        EditorApplication.update -= WaitForResolve;
        Put("15.manifest.frames", s_Frames < 30 ? "changed" : "unchanged");
        Settings("15.manifest.later", "Packages/packages-lock.json");
        Finish();
        // In Play mode nothing writes the defines (run.py reads the files after the quit).
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, "PROBE_B;PROBE_A");
        EditorApplication.Exit(0);
    }
}
