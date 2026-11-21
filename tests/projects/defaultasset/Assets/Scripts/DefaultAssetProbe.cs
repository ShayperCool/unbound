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
        // The WebPlayerTemplates root spelled in lower case: whether the special roots match by case.
        ("webplayerTemplate.txt", "Assets/webplayertemplates/Legacy/readme.txt"),
        ("nonRootStreaming.txt", "Assets/Data/StreamingAssets/nested_not_root.txt"),
        ("plugin.dll", "Assets/Data/ProbeSame.dll"),
        ("plugin.so", "Assets/PluginKinds/libnative.so"),
        ("plugin.a", "Assets/PluginKinds/libstatic.a"),
        ("plugin.dylib", "Assets/PluginKinds/libmac.dylib"),
        ("plugin.jar", "Assets/PluginKinds/library.jar"),
        ("plugin.aar", "Assets/PluginKinds/library.aar"),
        ("plugin.jslib", "Assets/PluginKinds/bridge.jslib"),
        ("plugin.cpp", "Assets/PluginKinds/Source.cpp"),
        ("plugin.h", "Assets/PluginKinds/Header.h"),
        ("plugin.mm", "Assets/PluginKinds/Bridge.mm"),
        ("plugin.java", "Assets/PluginKinds/Native.java"),
        ("plugin.swift", "Assets/PluginKinds/Native.swift"),
        ("plugin.bundle", "Assets/PluginKinds/Kit.bundle"),
        ("plugin.framework", "Assets/PluginKinds/Kit.framework"),
        ("plugin.androidlib", "Assets/PluginKinds/Lib.androidlib"),
        ("plugin.nativeDll", "Assets/PluginKinds/native.dll"),
        ("plugin.c", "Assets/PluginKinds/Source.c"),
        ("plugin.m", "Assets/PluginKinds/Bridge.m"),
        ("plugin.cc", "Assets/PluginKinds/Source.cc"),
        ("plugin.kt", "Assets/PluginKinds/Native.kt"),
        ("plugin.jspre", "Assets/PluginKinds/prelude.jspre"),
        ("plugin.xcframework", "Assets/PluginKinds/Kit.xcframework"),
        ("plugin.plugin", "Assets/PluginKinds/Kit.plugin"),
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
        "Assets/webplayertemplates",
        "Assets/Sprites",
        "Assets/Scenes",
    };

    /// <summary>Files of the Editor's own formats and their importers' neighbours, for
    /// IsNativeAsset / IsForeignAsset of the main object and of the other objects of the file.</summary>
    static readonly (string Key, string Path)[] NativeCases = new[]
    {
        ("asset", "Assets/Native/Data.asset"),
        ("mat", "Assets/Native/Plain.mat"),
        ("prefab", "Assets/Native/Thing.prefab"),
        ("anim", "Assets/Native/Clip.anim"),
        ("controller", "Assets/Native/Controller.controller"),
        ("overrideController", "Assets/Native/Override.overrideController"),
        ("mask", "Assets/Native/Mask.mask"),
        ("mixer", "Assets/Native/Mixer.mixer"),
        ("renderTexture", "Assets/Native/Target.renderTexture"),
        ("physicMaterial", "Assets/Native/Bouncy.physicMaterial"),
        ("physicsMaterial2D", "Assets/Native/Bouncy2D.physicsMaterial2D"),
        ("spriteatlas", "Assets/Native/Atlas.spriteatlas"),
        ("spriteatlasv2", "Assets/Native/AtlasV2.spriteatlasv2"),
        ("lighting", "Assets/Native/Settings.lighting"),
        ("flare", "Assets/Native/Glow.flare"),
        ("cubemap", "Assets/Native/Sky.cubemap"),
        ("terrainlayer", "Assets/Native/Ground.terrainlayer"),
        ("shadervariants", "Assets/Native/Variants.shadervariants"),
        ("giparams", "Assets/Native/Params.giparams"),
        ("settings", "ProjectSettings/TagManager.asset"),
        ("texture", "Assets/Sprites/Single.png"),
        ("text", "Assets/Data/Plain.txt"),
        ("script", "Assets/Scripts/ProbeData.cs"),
        ("scene", "Assets/Scenes/Probe.unity"),
        ("folder", "Assets/SampleFolder"),
        ("unknown", "Assets/Data/Blob.xyz"),
        ("plugin", "Assets/Plugins/ScriptPlugin.dll"),
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

        // DefaultAsset's internal inspector message and warning flag.
        var message = typeof(DefaultAsset).GetProperty("message", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var isWarning = typeof(DefaultAsset).GetProperty("isWarning", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var entry in Cases)
        {
            if (!(AssetDatabase.LoadMainAssetAtPath(entry.Path) is DefaultAsset asset)) continue;
            Check("inspector." + entry.Key, () => "message=" + message.GetValue(asset) + "|isWarning=" + isWarning.GetValue(asset));
        }

        // Native and foreign assets: the main object and every other object of the file.
        foreach (var entry in NativeCases)
        {
            string path = entry.Path;
            Check("native." + entry.Key, () =>
            {
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                var others = AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o != main).ToArray();
                string Set(Func<Object, bool> f) => others.Length == 0 ? "-" : string.Join("/", others.Select(f).Distinct().OrderBy(b => b));
                var importer = AssetImporter.GetAtPath(path);
                return string.Join("|", new[]
                {
                    "main=" + (main != null ? main.GetType().FullName : "<null>"),
                    "importer=" + (importer != null ? importer.GetType().FullName : "<null>"),
                    "isNative=" + (main != null && AssetDatabase.IsNativeAsset(main)),
                    "isForeign=" + (main != null && AssetDatabase.IsForeignAsset(main)),
                    "othersNative=" + Set(AssetDatabase.IsNativeAsset),
                    "othersForeign=" + Set(AssetDatabase.IsForeignAsset),
                });
            });
        }
        Check("native.inMemory", () =>
        {
            var created = ScriptableObject.CreateInstance<ProbeData>();
            var result = "isNative=" + AssetDatabase.IsNativeAsset(created) + "|isForeign=" + AssetDatabase.IsForeignAsset(created);
            Object.DestroyImmediate(created);
            return result;
        });
        Check("native.null", () => "isNative=" + AssetDatabase.IsNativeAsset((Object)null));
        Check("foreign.null", () => "isForeign=" + AssetDatabase.IsForeignAsset((Object)null));

        // The scripts of a precompiled managed plug-in.
        const string scriptDll = "Assets/Plugins/ScriptPlugin.dll";
        Check("dllScripts.objects", () => string.Join(";", AssetDatabase.LoadAllAssetsAtPath(scriptDll)
            .OrderBy(o => o.name, StringComparer.Ordinal)
            .Select(o =>
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long id);
                var cls = (o as MonoScript)?.GetClass();
                return string.Join(",", new[]
                {
                    Describe(o), "id=" + id, "hideFlags=" + o.hideFlags,
                    "class=" + (cls != null ? cls.FullName : "<null>"),
                    "isMain=" + AssetDatabase.IsMainAsset(o), "isSub=" + AssetDatabase.IsSubAsset(o),
                    "isNative=" + AssetDatabase.IsNativeAsset(o), "isForeign=" + AssetDatabase.IsForeignAsset(o),
                    "path=" + AssetDatabase.GetAssetPath(o),
                });
            })));
        // The order of a plug-in's scripts is not documented: lists are sorted by name.
        Check("dllScripts.main", () =>
        {
            var main = AssetDatabase.LoadMainAssetAtPath(scriptDll);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(main, out string _, out long id);
            string Sorted(Object[] items) => DescribeList(items.OrderBy(o => o.name, StringComparer.Ordinal).ToArray());
            var all = AssetDatabase.LoadAllAssetsAtPath(scriptDll);
            return string.Join("|", new[]
            {
                "obj=" + Describe(main), "hideFlags=" + main.hideFlags, "localId=" + id,
                "inAll=" + all.Contains(main), "all=" + Sorted(all),
                "reps=" + Sorted(AssetDatabase.LoadAllAssetRepresentationsAtPath(scriptDll)),
                "importer=" + AssetImporter.GetAtPath(scriptDll)?.GetType().FullName,
            });
        });
        Check("dllScripts.loadMonoScript", () =>
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptDll);
            return "type=" + (script != null ? script.GetType().FullName : "<null>")
                + "|listed=" + AssetDatabase.LoadAllAssetsAtPath(scriptDll).Contains(script);
        });
        Check("dllScripts.fromBehaviour", () =>
        {
            var go = new GameObject("probe");
            var script = MonoScript.FromMonoBehaviour(go.AddComponent<ScriptPlugin.PluginBehaviour>());
            Object.DestroyImmediate(go);
            var listed = AssetDatabase.LoadAllAssetsAtPath(scriptDll).Contains(script);
            return Describe(script) + "|path=" + (script != null ? AssetDatabase.GetAssetPath(script) : "") + "|listed=" + listed;
        });
        Check("dllScripts.fromScriptableObject", () =>
        {
            var data = ScriptableObject.CreateInstance<ScriptPlugin.PluginData>();
            var script = MonoScript.FromScriptableObject(data);
            Object.DestroyImmediate(data);
            var listed = AssetDatabase.LoadAllAssetsAtPath(scriptDll).Contains(script);
            return Describe(script) + "|path=" + (script != null ? AssetDatabase.GetAssetPath(script) : "") + "|listed=" + listed;
        });

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
        // Whether the root folder is listed like other assets, and how its path matches.
        Check("folder.assetsRoot.listed", () =>
        {
            var root = AssetDatabase.AssetPathToGUID("Assets");
            var paths = AssetDatabase.GetAllAssetPaths();
            return "allPaths=" + paths.Contains("Assets") + "/" + paths.Contains("Packages")
                + "|findFolder=" + AssetDatabase.FindAssets("t:Folder").Contains(root)
                + "|findName=" + AssetDatabase.FindAssets("Assets").Contains(root)
                + "|findInAssets=" + AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Assets" }).Contains(root);
        });
        Check("folder.assetsRoot.case", () => "guid=" + AssetDatabase.AssetPathToGUID("assets")
            + "|validFolder=" + AssetDatabase.IsValidFolder("assets")
            + "|load=" + Describe(AssetDatabase.LoadMainAssetAtPath("assets"))
            + "|child=" + (AssetDatabase.AssetPathToGUID("assets/SampleFolder") == AssetDatabase.AssetPathToGUID("Assets/SampleFolder")));

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
