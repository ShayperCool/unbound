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

namespace UnityEngine.Tests
{
    public class DefaultAssetTests
    {
        [Fact]
        public void DefaultAssetCreateAndForFolderSetPersistentAssetAndHideFlags()
        {
            var folder = DefaultAsset.ForFolder("Folder.WithDot");
            var file = DefaultAsset.Create("Multi.part");
            var plugin = DefaultAsset.Create("", HideFlags.None);
            try
            {
                Assert.Equal(102900000L, DefaultAsset.FileId);
                Assert.Equal(DefaultAsset.FileId, DefaultAsset.FolderFileId);

                Assert.Equal("Folder.WithDot", folder.name);
                Assert.True(folder.IsPersistentAsset);
                Assert.Equal(HideFlags.NotEditable, folder.hideFlags);

                Assert.Equal("Multi.part", file.name);
                Assert.True(file.IsPersistentAsset);
                Assert.Equal(HideFlags.NotEditable, file.hideFlags);

                Assert.Equal("", plugin.name);
                Assert.True(plugin.IsPersistentAsset);
                Assert.Equal(HideFlags.None, plugin.hideFlags);
            }
            finally
            {
                Object.DestroyImmediate(folder);
                Object.DestroyImmediate(file);
                Object.DestroyImmediate(plugin);
            }
        }

        [Fact]
        public void AssetsRootFolderResolvesGuidObjectAndImporter()
        {
            Assert.Equal(AssetDatabase.AssetsRootGuid, AssetDatabase.AssetPathToGUID("Assets"));
            Assert.Equal(AssetDatabase.AssetsRootGuid, AssetDatabase.AssetPathToGUID("Assets/"));
            Assert.Equal("Assets", AssetDatabase.GUIDToAssetPath(AssetDatabase.AssetsRootGuid));
            Assert.True(AssetDatabase.IsValidFolder("Assets"));

            var asObj = AssetDatabase.LoadAssetAtPath<Object>("Assets");
            var asDefault = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
            var asMain = AssetDatabase.LoadMainAssetAtPath("Assets");
            Assert.NotNull(asObj);
            Assert.IsType<DefaultAsset>(asObj);
            Assert.Same(asObj, asDefault);
            Assert.Same(asObj, asMain);
            Assert.Equal("Assets", asObj.name);
            Assert.Equal(HideFlags.NotEditable, asObj.hideFlags);
            Assert.Equal(typeof(DefaultAsset), AssetDatabase.GetMainAssetTypeAtPath("Assets"));
            Assert.Empty(AssetDatabase.LoadAllAssetsAtPath("Assets"));
            Assert.Empty(AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets"));
            Assert.True(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asObj, out var guid, out var localId));
            Assert.Equal(AssetDatabase.AssetsRootGuid, guid);
            Assert.Equal(DefaultAsset.FileId, localId);
            Assert.True(AssetDatabase.IsMainAsset(asObj));
            Assert.False(AssetDatabase.IsSubAsset(asObj));
            Assert.False(AssetDatabase.IsNativeAsset(asObj));
            Assert.False(AssetDatabase.IsForeignAsset(asObj));
            Assert.True(AssetDatabase.Contains(asObj));
            Assert.Equal("Assets", AssetDatabase.GetAssetPath(asObj));

            var importer = AssetImporter.GetAtPath("Assets");
            Assert.NotNull(importer);
            Assert.Equal(typeof(AssetImporter), importer.GetType());
        }

        [Fact]
        public void FindAssetsMatchesDefaultAssetKindsAndDotNamedFolders()
        {
            AssetDatabase.List("""
                [
                  {"guid":"11111111111111111111111111111111","path":"Assets/Folder.WithDot","kind":"folder","labels":[]},
                  {"guid":"22222222222222222222222222222222","path":"Assets/Data/Blob.xyz","kind":"unknown","labels":[]},
                  {"guid":"33333333333333333333333333333333","path":"Assets/Data/Probe.dll","kind":"plugin","labels":[]},
                  {"guid":"44444444444444444444444444444444","path":"Assets/Scenes/Main.unity","kind":"scene","labels":[]},
                  {"guid":"55555555555555555555555555555555","path":"Assets/Data/Plain.txt","kind":"text","labels":[]},
                  {"guid":"66666666666666666666666666666666","path":"Assets/StreamingAssets/note.txt","kind":"unknown","labels":[]},
                  {"guid":"77777777777777777777777777777777","path":"Assets/StreamingAssets/SubDir","kind":"folder","labels":[]}
                ]
                """);
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
            var atlas = new SpriteAtlas();
            var rootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
            var nonFolder = DefaultAsset.Create("Blob", HideFlags.NotEditable);
            var warnings = new List<string>();
            Application.LogCallback capture = (msg, _, type) =>
            {
                if (type == LogType.Warning) warnings.Add(msg);
            };
            Application.logMessageReceived += capture;
            try
            {
                atlas.Add(new Object[] { rootFolder, nonFolder });
                Assert.Equal(new Object[] { rootFolder }, atlas.GetPackables());
                Assert.Equal(new[]
                {
                    "Attempted to add invalid object into a Sprite Atlas. Blob was a DefaultAsset. Only Folder, Texture and Sprite are valid."
                }, warnings);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                Object.DestroyImmediate(nonFolder);
                Object.DestroyImmediate(atlas);
            }
        }

        [Fact]
        public void UnmeasuredReleaseControlPreservesDefaultAssetContracts()
        {
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var asDefault = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
                Assert.NotNull(asDefault);
                Assert.Equal("Assets", asDefault.name);
                Assert.Equal(HideFlags.NotEditable, asDefault.hideFlags);
                Assert.Empty(AssetDatabase.LoadAllAssetsAtPath("Assets"));
            }
        }
    }
}
