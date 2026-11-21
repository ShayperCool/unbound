using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Xunit;
using Object = UnityEngine.Object;

namespace ScriptPlugin
{
    /// <summary>Classes named as those of tests/projects/defaultasset's ScriptPlugin.dll.</summary>
    public class PluginOuter
    {
        public class PluginNested : MonoBehaviour { }
    }

    public class PluginBehaviourForTest : MonoBehaviour { }

    public class PluginGenericForTest<T> : MonoBehaviour { }
}

namespace UnityEngine.Tests
{
    /// <summary>DefaultAsset, the Assets folder and native/foreign assets as the Unity 6000.0.71f1
    /// Editor has them (tests/projects/defaultasset).</summary>
    public class DefaultAssetTests
    {
        const string RootGuid = "00000000000000001000000000000000";

        /// <summary>The asset table as the engine lists it: the Assets folder is one of its entries.</summary>
        const string Table = """
            [
              {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"11111111111111111111111111111111","path":"Assets/Folder.WithDot","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"22222222222222222222222222222222","path":"Assets/Data/Blob.xyz","kind":"unknown","importer":"DefaultImporter","labels":[]},
              {"guid":"33333333333333333333333333333333","path":"Assets/Data/Probe.dll","kind":"plugin","importer":"PluginImporter","labels":[]},
              {"guid":"44444444444444444444444444444444","path":"Assets/Scenes/Main.unity","kind":"scene","importer":"DefaultImporter","labels":[]},
              {"guid":"55555555555555555555555555555555","path":"Assets/Data/Plain.txt","kind":"text","importer":"TextScriptImporter","labels":[]},
              {"guid":"66666666666666666666666666666666","path":"Assets/StreamingAssets/note.txt","kind":"unknown","importer":"DefaultImporter","labels":[]},
              {"guid":"77777777777777777777777777777777","path":"Assets/StreamingAssets/SubDir","kind":"folder","importer":"DefaultImporter","labels":[]},
              {"guid":"88888888888888888888888888888888","path":"Assets/Native/Clip.anim","kind":"yaml","importer":"NativeFormatImporter","labels":[]},
              {"guid":"99999999999999999999999999999999","path":"Assets/Native/Thing.prefab","kind":"prefab","importer":"PrefabImporter","labels":[]},
              {"guid":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","path":"Assets/Native/Atlas.spriteatlasv2","kind":"yaml","importer":"SpriteAtlasImporter","labels":[]}
            ]
            """;

        [Fact]
        public void DefaultAssetsAreNamedAndFlaggedByWhatTheyStandFor()
        {
            var file = DefaultAsset.Create("Multi.part", HideFlags.NotEditable);
            var plugin = DefaultAsset.Create("", HideFlags.None);
            try
            {
                Assert.Equal(102900000L, DefaultAsset.FileId);
                Assert.Equal("Multi.part", file.name);
                Assert.True(file.IsPersistentAsset);
                Assert.Equal(HideFlags.NotEditable, file.hideFlags);
                Assert.Equal("", plugin.name);
                Assert.Equal(HideFlags.None, plugin.hideFlags);
                // The inspector shows no message for any of them (inspector.*).
                Assert.Equal("", file.message);
                Assert.False(file.isWarning);
            }
            finally
            {
                Object.DestroyImmediate(file);
                Object.DestroyImmediate(plugin);
            }
        }

        [Fact]
        public void TheAssetsFolderIsAListedAssetNoSearchFinds()
        {
            AssetDatabase.List(Table);
            try
            {
                Assert.Equal(RootGuid, AssetDatabase.AssetPathToGUID("Assets"));
                Assert.Equal(RootGuid, AssetDatabase.AssetPathToGUID("assets"));
                Assert.Equal(RootGuid, AssetDatabase.AssetPathToGUID("Assets/"));
                Assert.Equal("Assets", AssetDatabase.GUIDToAssetPath(RootGuid));
                Assert.True(AssetDatabase.IsValidFolder("Assets"));
                Assert.True(AssetDatabase.IsValidFolder("assets"));
                // Packages is a folder without being an asset.
                Assert.True(AssetDatabase.IsValidFolder("Packages"));
                Assert.Contains("Assets", AssetDatabase.GetAllAssetPaths());
                Assert.DoesNotContain("Packages", AssetDatabase.GetAllAssetPaths());
                Assert.DoesNotContain(RootGuid, AssetDatabase.FindAssets("t:Folder"));
                Assert.DoesNotContain(RootGuid, AssetDatabase.FindAssets("Assets"));
                Assert.DoesNotContain(RootGuid, AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Assets" }));
                Assert.Equal(new[] { "Assets/Folder.WithDot" }, AssetDatabase.GetSubFolders("Assets"));
            }
            finally
            {
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void NativeAssetsAreTheNativeFormatImportersAndForeignAssetsTheOthers()
        {
            AssetDatabase.List(Table);
            var clip = new AnimationClip { name = "Clip" };
            var clipPart = new AnimationClip { name = "Part" };
            var prefab = new GameObject("Thing");
            var atlas = new SpriteAtlas();
            var folder = DefaultAsset.Create("Folder.WithDot", HideFlags.NotEditable);
            var blob = DefaultAsset.Create("Blob", HideFlags.NotEditable);
            var inMemory = new AnimationClip();
            var adopted = new[] { "88888888888888888888888888888888", "99999999999999999999999999999999", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "11111111111111111111111111111111", "22222222222222222222222222222222" };
            try
            {
                var clipFile = AssetDatabaseRuntime.Adopt(adopted[0], "Assets/Native/Clip.anim", clip, 7400000);
                // Every object of a native file is native, not only its main one.
                AssetDatabaseRuntime.AddObject(clipFile, clipPart, 1234);
                AssetDatabaseRuntime.Adopt(adopted[1], "Assets/Native/Thing.prefab", prefab, 100100000);
                AssetDatabaseRuntime.Adopt(adopted[2], "Assets/Native/Atlas.spriteatlasv2", atlas, 4343727234628468602);
                AssetDatabaseRuntime.Adopt(adopted[3], "Assets/Folder.WithDot", folder, DefaultAsset.FileId);
                AssetDatabaseRuntime.Adopt(adopted[4], "Assets/Data/Blob.xyz", blob, DefaultAsset.FileId);

                Assert.True(AssetDatabase.IsNativeAsset(clip));
                Assert.False(AssetDatabase.IsForeignAsset(clip));
                Assert.True(AssetDatabase.IsNativeAsset(clipPart));
                Assert.False(AssetDatabase.IsForeignAsset(clipPart));
                // A prefab is of the PrefabImporter and a V2 atlas of its own importer: foreign.
                Assert.False(AssetDatabase.IsNativeAsset(prefab));
                Assert.True(AssetDatabase.IsForeignAsset(prefab));
                Assert.False(AssetDatabase.IsNativeAsset(atlas));
                Assert.True(AssetDatabase.IsForeignAsset(atlas));
                // A folder is neither; another DefaultAsset is foreign.
                Assert.False(AssetDatabase.IsNativeAsset(folder));
                Assert.False(AssetDatabase.IsForeignAsset(folder));
                Assert.False(AssetDatabase.IsNativeAsset(blob));
                Assert.True(AssetDatabase.IsForeignAsset(blob));
                // Neither for an object that is no asset; null throws.
                Assert.False(AssetDatabase.IsNativeAsset(inMemory));
                Assert.False(AssetDatabase.IsForeignAsset(inMemory));
                Assert.Throws<ArgumentNullException>(() => AssetDatabase.IsNativeAsset(null));
                Assert.Throws<ArgumentNullException>(() => AssetDatabase.IsForeignAsset(null));
                // A folder's or file's placeholder is not among its loadable objects; all of a
                // native file's objects are.
                Assert.Empty(AssetDatabase.LoadAllAssetsAtPath("Assets/Folder.WithDot"));
                Assert.Empty(AssetDatabase.LoadAllAssetsAtPath("Assets/Data/Blob.xyz"));
                Assert.Equal(2, AssetDatabase.LoadAllAssetsAtPath("Assets/Native/Clip.anim").Length);
            }
            finally
            {
                foreach (var guid in adopted) AssetDatabaseRuntime.Invalidate(guid);
                foreach (var o in new Object[] { clip, clipPart, prefab, atlas, folder, blob, inMemory })
                    Object.DestroyImmediate(o);
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void AnAssetListedAgainByAnImportKeepsItsImporter()
        {
            const string guid = "cccccccccccccccccccccccccccccccc";
            AssetDatabase.List(Table);
            var created = new AnimationClip { name = "New" };
            try
            {
                // What an import (CreateAsset, ImportAsset, a refresh) answers for the path.
                AssetDatabase.ApplyRefresh("{\"assets\":[{\"path\":\"Assets/Native/New.anim\",\"guid\":\"" + guid
                    + "\",\"kind\":\"yaml\",\"importer\":\"NativeFormatImporter\",\"labels\":[]}]}");
                AssetDatabaseRuntime.Adopt(guid, "Assets/Native/New.anim", created, 7400000);
                Assert.True(AssetDatabase.IsNativeAsset(created));
                Assert.False(AssetDatabase.IsForeignAsset(created));
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(guid);
                Object.DestroyImmediate(created);
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void APlugInNotLoadedForTheEditorWarnsOnceWhereItsScriptsAreAskedFor()
        {
            const string listed = "dddddddddddddddddddddddddddddd01", typed = "dddddddddddddddddddddddddddddd02";
            AssetDatabase.List("[{\"guid\":\"" + listed + "\",\"path\":\"Assets/Plugins/A.dll\",\"kind\":\"plugin\",\"importer\":\"PluginImporter\"},"
                + "{\"guid\":\"" + typed + "\",\"path\":\"Assets/Plugins/B.dll\",\"kind\":\"plugin\",\"importer\":\"PluginImporter\"}]");
            var a = DefaultAsset.Create("", HideFlags.None);
            var b = DefaultAsset.Create("", HideFlags.None);
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                AssetDatabaseRuntime.Adopt(listed, "Assets/Plugins/A.dll", a, DefaultAsset.FileId).ScriptsUnknown = true;
                AssetDatabaseRuntime.Adopt(typed, "Assets/Plugins/B.dll", b, DefaultAsset.FileId).ScriptsUnknown = true;
                // Its DefaultAsset is measured: loading it says nothing.
                Assert.Same(a, AssetDatabase.LoadMainAssetAtPath("Assets/Plugins/A.dll"));
                Assert.Empty(logs.Lines);
                Assert.Empty(AssetDatabase.LoadAllAssetsAtPath("Assets/Plugins/A.dll"));
                Assert.Empty(AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/Plugins/A.dll"));
                Assert.Null(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Plugins/B.dll"));
                Assert.Equal(new[]
                {
                    "Warning: Unbound: Assets/Plugins/A.dll is not loaded for the Editor, so its scripts are not listed as its sub-assets (not supported yet).",
                    "Warning: Unbound: Assets/Plugins/B.dll is not loaded for the Editor, so its scripts are not listed as its sub-assets (not supported yet).",
                }, logs.Lines);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                AssetDatabaseRuntime.Invalidate(listed);
                AssetDatabaseRuntime.Invalidate(typed);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void ANestedPlugInClassDoesNotTakeATopLevelClasssFileId()
        {
            var map = new Dictionary<long, Type>();
            GameDomain.AddPluginScript(map, 1L, typeof(ScriptPlugin.PluginBehaviourForTest));
            GameDomain.AddPluginScript(map, 1L, typeof(ScriptPlugin.PluginOuter.PluginNested));
            Assert.Same(typeof(ScriptPlugin.PluginBehaviourForTest), map[1L]);
            // Listed first, a nested class gives way to the top-level one; alone, it is listed.
            GameDomain.AddPluginScript(map, 2L, typeof(ScriptPlugin.PluginOuter.PluginNested));
            Assert.Same(typeof(ScriptPlugin.PluginOuter.PluginNested), map[2L]);
            GameDomain.AddPluginScript(map, 2L, typeof(ScriptPlugin.PluginBehaviourForTest));
            Assert.Same(typeof(ScriptPlugin.PluginBehaviourForTest), map[2L]);
        }

        sealed class LogCapture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(logType + ": " + string.Format(format, args));
        }

        [Fact]
        public void APluginsScriptsHaveTheirClassesFileIdsAndNoClassWhenNested()
        {
            // The file IDs of tests/projects/defaultasset's ScriptPlugin.dll classes in the Editor.
            Assert.Equal(961872061L, GameDomain.PluginScriptFileId(null, "GlobalPluginBehaviour"));
            Assert.Equal(-1902389345L, GameDomain.PluginScriptFileId("ScriptPlugin", "PluginData"));
            Assert.Equal(1594108085L, GameDomain.PluginScriptFileId("ScriptPlugin", "PluginGeneric`1"));
            Assert.Equal(1553723163L, GameDomain.PluginScriptFileId(typeof(ScriptPlugin.PluginOuter.PluginNested)));

            const string guid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var top = MonoScript.ForPluginClass(typeof(ScriptPlugin.PluginBehaviourForTest), guid, 5);
            var generic = MonoScript.ForPluginClass(typeof(ScriptPlugin.PluginGenericForTest<>), guid, 6);
            var nested = MonoScript.ForPluginClass(typeof(ScriptPlugin.PluginOuter.PluginNested), guid, 7);
            try
            {
                Assert.Equal("PluginBehaviourForTest", top.name);
                Assert.Same(typeof(ScriptPlugin.PluginBehaviourForTest), top.GetClass());
                Assert.Equal("PluginGenericForTest`1", generic.name);
                Assert.Same(typeof(ScriptPlugin.PluginGenericForTest<>), generic.GetClass());
                Assert.Equal("PluginNested", nested.name);
                Assert.Null(nested.GetClass());
                Assert.Equal(HideFlags.None, top.hideFlags);
                Assert.True(top.IsPersistentAsset);
            }
            finally
            {
                Object.DestroyImmediate(top);
                Object.DestroyImmediate(generic);
                Object.DestroyImmediate(nested);
            }
        }

        [Fact]
        public void FindAssetsMatchesDefaultAssetKindsAndDotNamedFolders()
        {
            AssetDatabase.List(Table);
            try
            {
                string[] Paths(string filter) => AssetDatabase.FindAssets(filter, new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                Assert.Equal(new[]
                {
                    "Assets/Data/Blob.xyz",
                    "Assets/Data/Probe.dll",
                    "Assets/Folder.WithDot",
                    "Assets/Scenes/Main.unity",
                    "Assets/StreamingAssets/note.txt",
                    "Assets/StreamingAssets/SubDir",
                }, Paths("t:DefaultAsset"));

                Assert.Equal(new[]
                {
                    "Assets/Folder.WithDot",
                    "Assets/StreamingAssets/SubDir",
                }, Paths("t:Folder"));

                Assert.Equal(new[] { "Assets/Data/Plain.txt" }, Paths("t:TextAsset"));
                Assert.Equal(new[] { "Assets/Scenes/Main.unity" }, Paths("t:SceneAsset"));
                Assert.Equal(new[] { "Assets/Folder.WithDot" }, Paths("WithDot"));
                Assert.Equal(new[] { "Assets/StreamingAssets/note.txt" }, Paths("note"));
            }
            finally
            {
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void SpriteAtlasAddAcceptsFolderDefaultAssetAndRejectsNonFolderDefaultAsset()
        {
            AssetDatabase.List(Table);
            var atlas = new SpriteAtlas();
            var folder = DefaultAsset.Create("Folder.WithDot", HideFlags.NotEditable);
            var nonFolder = DefaultAsset.Create("Blob", HideFlags.NotEditable);
            var warnings = new List<string>();
            Application.LogCallback capture = (msg, _, type) =>
            {
                if (type == LogType.Warning) warnings.Add(msg);
            };
            Application.logMessageReceived += capture;
            try
            {
                AssetDatabaseRuntime.Adopt("11111111111111111111111111111111", "Assets/Folder.WithDot", folder, DefaultAsset.FileId);
                atlas.Add(new Object[] { folder, nonFolder });
                Assert.Equal(new Object[] { folder }, atlas.GetPackables());
                Assert.Equal(new[]
                {
                    "Attempted to add invalid object into a Sprite Atlas. Blob was a DefaultAsset. Only Folder, Texture and Sprite are valid."
                }, warnings);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                AssetDatabaseRuntime.Invalidate("11111111111111111111111111111111");
                Object.DestroyImmediate(folder);
                Object.DestroyImmediate(nonFolder);
                Object.DestroyImmediate(atlas);
                AssetDatabase.Invalidate();
            }
        }
    }
}
