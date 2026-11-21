using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what the Unity 6000.0 Editor answers for DefaultAsset kinds
/// (folders, unknown/unclaimed extensions, extensionless files, .rsp, .modulecompilationtrigger,
/// StreamingAssets / WebGLTemplates / WebPlayerTemplates files) and non-DefaultAsset controls
/// (.txt, .json, .png, .unity, .cs, .dll, missing path, root Assets).
/// </summary>
static class DefaultAssetProbe
{
    static readonly (string Key, string Path)[] Cases = new[]
    {
        ("folder.simple", "Assets/SampleFolder"),
        ("folder.withDot", "Assets/Folder.WithDot"),
        ("folder.streamingRoot", "Assets/StreamingAssets"),
        ("folder.streamingSub", "Assets/StreamingAssets/SubDir"),
        ("folder.assetsRoot", "Assets"),
        ("unknown.xyz", "Assets/Data/Blob.xyz"),
        ("unknown.bin", "Assets/Data/Raw.bin"),
        ("unknown.multiDot", "Assets/Data/Multi.part.dat"),
        ("unknown.noExt", "Assets/Data/NoExtension"),
        ("rsp", "Assets/Data/csc.rsp"),
        ("trigger", "Assets/Data/Trigger.modulecompilationtrigger"),
        ("streaming.txt", "Assets/StreamingAssets/note.txt"),
        ("streaming.json", "Assets/StreamingAssets/config.json"),
        ("streaming.png", "Assets/StreamingAssets/img.png"),
        ("streaming.bin", "Assets/StreamingAssets/raw.bin"),
        ("streaming.nestedTxt", "Assets/StreamingAssets/SubDir/nested.txt"),
        ("webglTemplate.html", "Assets/WebGLTemplates/Custom/index.html"),
        ("webplayerTemplate.txt", "Assets/WebPlayerTemplates/Legacy/readme.txt"),
        ("nonRootStreaming.txt", "Assets/Data/StreamingAssets/nested_not_root.txt"),
        ("plugin.dll", "Assets/Data/ProbeSame.dll"),
        ("control.txt", "Assets/Data/Plain.txt"),
        ("control.json", "Assets/Data/Data.json"),
        ("control.png", "Assets/Sprites/Single.png"),
        ("control.scene", "Assets/Scenes/Probe.unity"),
        ("control.script", "Assets/Scripts/DefaultAssetProbe.cs"),
        ("control.missing", "Assets/Data/Missing.xyz"),
    };

    static readonly string[] SearchFolders = new[]
    {
        "Assets/SampleFolder",
        "Assets/Folder.WithDot",
        "Assets/Data",
        "Assets/StreamingAssets",
        "Assets/WebGLTemplates",
        "Assets/WebPlayerTemplates",
        "Assets/Sprites",
        "Assets/Scenes",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("DEFAULT_ASSET_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);

        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key))
            .Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (Exception e) { Put(key, "throws " + e.GetType().Name); }
        }

        string CaptureLogs(Action action, out Exception thrown)
        {
            var logs = new List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) =>
                logs.Add(type + ":" + condition.Replace("\r", "").Replace("\n", "\\n"));
            Application.logMessageReceived += handler;
            thrown = null;
            try { action(); }
            catch (Exception e) { thrown = e; }
            finally { Application.logMessageReceived -= handler; }
            return logs.Count == 0 ? "<none>" : string.Join("||", logs);
        }

        foreach (var entry in Cases)
        {
            string key = entry.Key;
            string path = entry.Path;

            Check("asset." + key, () =>
            {
                var asObj = AssetDatabase.LoadAssetAtPath<Object>(path);
                var asDefault = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
                var asMain = AssetDatabase.LoadMainAssetAtPath(path);
                var all = AssetDatabase.LoadAllAssetsAtPath(path);
                var reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
                var mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                var guid = AssetDatabase.AssetPathToGUID(path);
                string locGuid = "";
                long localId = 0;
                bool hasLoc = asObj != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asObj, out locGuid, out localId);
                var importer = AssetImporter.GetAtPath(path);

                return string.Join("|", new[]
                {
                    "obj=" + Describe(asObj),
                    "hideFlags=" + (asObj != null ? asObj.hideFlags.ToString() : "<null>"),
                    "asDefault=" + (asDefault != null) + ":" + ReferenceEquals(asObj, asDefault),
                    "mainSame=" + ReferenceEquals(asObj, asMain),
                    "mainType=" + (mainType != null ? mainType.FullName : "<null>"),
                    "all=" + DescribeList(all),
                    "reps=" + DescribeList(reps),
                    "locOk=" + hasLoc,
                    "locGuidMatch=" + (hasLoc && string.Equals(locGuid, guid, StringComparison.OrdinalIgnoreCase)),
                    "localId=" + localId,
                    "validFolder=" + AssetDatabase.IsValidFolder(path),
                    "isMain=" + (asObj != null && AssetDatabase.IsMainAsset(asObj)),
                    "isSub=" + (asObj != null && AssetDatabase.IsSubAsset(asObj)),
                    "isNative=" + (asObj != null && AssetDatabase.IsNativeAsset(asObj)),
                    "isForeign=" + (asObj != null && AssetDatabase.IsForeignAsset(asObj)),
                    "contains=" + (asObj != null && AssetDatabase.Contains(asObj)),
                    "assetPath=" + (asObj != null ? AssetDatabase.GetAssetPath(asObj) : ""),
                    "importer=" + (importer != null ? importer.GetType().FullName : "<null>"),
                });
            });
        }

        // FindAssets queries
        string FindPaths(string filter, string[] folders) =>
            string.Join(",", AssetDatabase.FindAssets(filter, folders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

        Check("find.tDefaultAsset", () => FindPaths("t:DefaultAsset", SearchFolders));
        Check("find.tFolder", () => FindPaths("t:Folder", SearchFolders));
        Check("find.tTextAsset", () => FindPaths("t:TextAsset", SearchFolders));
        Check("find.tTexture2D", () => FindPaths("t:Texture2D", SearchFolders));
        Check("find.tSceneAsset", () => FindPaths("t:SceneAsset", SearchFolders));
        Check("find.nameBlob", () => FindPaths("Blob", SearchFolders));
        Check("find.nameNote", () => FindPaths("note", SearchFolders));
        Check("find.nameNoExt", () => FindPaths("NoExtension", SearchFolders));
        Check("find.nameWithDot", () => FindPaths("WithDot", new[] { "Assets" }));

        // SpriteAtlasExtensions.Add folder DefaultAsset vs non-folder DefaultAsset control
        Check("atlas.addDefaultAssets", () =>
        {
            var atlas = new SpriteAtlas();
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/SampleFolder");
            var streamingSub = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/StreamingAssets/SubDir");
            var xyz = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/Data/Blob.xyz");
            var streamingTxt = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/StreamingAssets/note.txt");

            var logs = CaptureLogs(() => atlas.Add(new Object[] { folder, xyz, streamingTxt, streamingSub }), out var ex);
            var packables = atlas.GetPackables();
            Object.DestroyImmediate(atlas);
            return $"throws={(ex != null ? ex.GetType().Name : "<none>")}|count={packables.Length}|items={DescribeList(packables)}|logs={logs}";
        });

        Check("folder.assetsRoot.guid", () => AssetDatabase.AssetPathToGUID("Assets"));

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string Describe(Object o) => o == null ? "<null>" : $"{o.GetType().FullName}:{o.name}";

    static string DescribeList(Object[] items)
    {
        if (items == null) return "<null>";
        if (items.Length == 0) return "[]";
        return "[" + string.Join(",", items.Select(Describe)) + "]";
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
