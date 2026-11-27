using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using Xunit;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class VirtualPlayerActivationTests : IDisposable
    {
        static readonly List<string> s_WillSavePaths = new List<string>();

        sealed class RecordingSaveProcessor : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                s_WillSavePaths.AddRange(paths);
                return paths;
            }
        }

        sealed class CaptureLogHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;
            internal CaptureLogHandler(ILogHandler inner, List<string> logs) { m_Inner = inner; m_Logs = logs; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                m_Logs.Add(args == null || args.Length == 0
                    ? format
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
                m_Inner.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        public VirtualPlayerActivationTests()
        {
            VirtualPlayerActivation.Configure(null);
            PackageInfo.Register(Array.Empty<PackageInfo>());
            ProjectInfo.ReadOnly = false;
            AssetModificationProcessorInternal.ProcessorsForTests = null;
            s_WillSavePaths.Clear();
        }

        public void Dispose()
        {
            VirtualPlayerActivation.Configure(null);
            PackageInfo.Register(Array.Empty<PackageInfo>());
            ProjectInfo.ReadOnly = false;
            AssetModificationProcessorInternal.ProcessorsForTests = null;
            s_WillSavePaths.Clear();
        }

        [Fact]
        public void NoRequestedPlayersDoesNothing()
        {
            VirtualPlayerActivation.Configure(Array.Empty<string>());
            VirtualPlayerActivation.ActivateRequested();
        }

        [Fact]
        public void MissingPackageFailsLoudly()
        {
            VirtualPlayerActivation.Configure(new[] { "2" });
            var ex = Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ActivateRequested());
            Assert.Contains("com.unity.multiplayer.playmode", ex.Message);
            Assert.Contains("not installed", ex.Message);
        }

        [Fact]
        public void UnknownPackageVersionFailsLoudly()
        {
            PackageInfo.Register(new[]
            {
                PackageInfo.Create(VirtualPlayerActivation.PackageName, "9.9.9", PackageSource.Registry,
                    "/tmp/fake-mppm", "MPPM", "", "", "", Array.Empty<string>(), true),
            });
            VirtualPlayerActivation.Configure(new[] { "2" });
            var ex = Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ActivateRequested());
            Assert.Contains("9.9.9", ex.Message);
            Assert.Contains("does not support", ex.Message);
        }

        [Fact]
        public void ResolveTargetMatchesByIndexAndNameAndRejectsMainOrInvalid()
        {
            var p1 = new object();
            var p2 = new object();
            var p3 = new object();
            var entries = new[]
            {
                new VirtualPlayerActivation.PlayerEntry(1, "Main Editor", true, p1),
                new VirtualPlayerActivation.PlayerEntry(2, "Player 2", false, p2),
                new VirtualPlayerActivation.PlayerEntry(3, "Player 3", false, p3),
            };

            Assert.Same(p2, VirtualPlayerActivation.ResolveTarget("2", entries).Instance);
            Assert.Same(p3, VirtualPlayerActivation.ResolveTarget("player 3", entries).Instance);
            Assert.Same(p2, VirtualPlayerActivation.ResolveTarget("  Player 2 ", entries).Instance);

            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("1", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("Main Editor", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("5", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("0", entries));
            Assert.Throws<InvalidOperationException>(() => VirtualPlayerActivation.ResolveTarget("Ghost", entries));
        }

        [Fact]
        public void ResolveAllTargetsValidatesAllBeforeActivationAndDeduplicates()
        {
            var p1 = new object();
            var p2 = new object();
            var p3 = new object();
            var entries = new[]
            {
                new VirtualPlayerActivation.PlayerEntry(1, "Main Editor", true, p1),
                new VirtualPlayerActivation.PlayerEntry(2, "Player 2", false, p2),
                new VirtualPlayerActivation.PlayerEntry(3, "Player 3", false, p3),
            };

            // A mixed valid + invalid list fails in ResolveAllTargets before any player is activated.
            Assert.Throws<InvalidOperationException>(() =>
                VirtualPlayerActivation.ResolveAllTargets(new[] { "2", "1" }, entries));
            Assert.Throws<InvalidOperationException>(() =>
                VirtualPlayerActivation.ResolveAllTargets(new[] { "2", "Ghost" }, entries));

            // Duplicate specs (by index and by name) collapse to one entry in first-seen order.
            var resolved = VirtualPlayerActivation.ResolveAllTargets(new[] { "3", "Player 2", "2", "player 3" }, entries);
            Assert.Equal(2, resolved.Count);
            Assert.Same(p3, resolved[0].Instance);
            Assert.Same(p2, resolved[1].Instance);
        }

        [Fact]
        public void ReadOnlyProjectSuppressesSpriteFirstImportMetaWrite()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var writeCalls = 0;
            const string singleWithoutId = "TextureImporter:\n  textureType: 8\n  spriteMode: 1\n";
            var meta = new System.Collections.Generic.List<Unbound.Runtime.YDocument>
            {
                new Unbound.Runtime.YDocument
                {
                    TypeName = "TextureImporter",
                    Body = TestYaml.DocumentBody(singleWithoutId),
                },
            };
            var importer = new TextureImporter { m_Settings = TestYaml.DocumentBody(singleWithoutId) };

            ProjectInfo.ReadOnly = true;
            try
            {
                SpriteImports.Imported(
                    new[] { "Assets/Single.png" },
                    _ => "texture",
                    _ => "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    _ => meta,
                    _ => importer,
                    _ => { writeCalls++; return true; });
                Assert.Equal(0, writeCalls);
            }
            finally
            {
                ProjectInfo.ReadOnly = false;
            }

            // Control: when not read-only, the same texture writes its spriteID.
            SpriteImports.Imported(
                new[] { "Assets/Single.png" },
                _ => "texture",
                _ => "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                _ => meta,
                _ => importer,
                _ => { writeCalls++; return true; });
            Assert.Equal(1, writeCalls);
        }

        [Fact]
        public void ReadOnlyProjectSuppressesScenePrefabScriptSampleAndSaveFileIfDirtyWrites()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var tempProject = Path.Combine(Path.GetTempPath(), "unbound-readonly-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempProject, "Assets"));
            var previousProject = ProjectInfo.ProjectPath;
            var previousHandler = Debug.unityLogger.logHandler;
            var logs = new List<string>();
            var guid = Guid.NewGuid().ToString("N");
            const string prefabAssetPath = "Assets/Test.prefab";
            var prefabDiskPath = Path.Combine(tempProject, prefabAssetPath);
            var scene = new SceneData { Path = "Assets/Test.unity", Name = "Test", IsLoaded = true };
            GameObject root = null;
            try
            {
                ProjectInfo.ProjectPath = tempProject;
                Debug.unityLogger.logHandler = new CaptureLogHandler(previousHandler, logs);
                AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(RecordingSaveProcessor) };

                var prefabFile = new LoadedFile { Path = prefabAssetPath, Guid = guid };
                using (ObjectRegistry.AsAssets())
                {
                    root = new GameObject("Test", bare: true) { m_IsPrefabAsset = true, IsPersistentAsset = true };
                    var t = (Transform)ComponentFactory.Create(typeof(Transform), root, deserializing: true);
                    t.IsPersistentAsset = true;
                    root.m_Transform = t;
                    root.m_Components.Add(t);
                    prefabFile.Objects[100] = root;
                    prefabFile.Objects[101] = t;
                }
                prefabFile.MainObject = root;
                prefabFile.Roots.Add(root);
                AssetDatabaseRuntime.AddLoaded(prefabFile);
                EditorUtility.SetDirty(root);

                var sample = default(UnityEditor.PackageManager.UI.Sample);

                // Control: CanSave accepts a loaded Assets/*.unity scene when not read-only.
                Assert.True(SceneFiles.CanSave(scene, "Assets/Test.unity"));

                ProjectInfo.ReadOnly = true;
                try
                {
                    Assert.False(SceneFiles.CanSave(scene, "Assets/Test.unity"));

                    logs.Clear();
                    Assert.False(PrefabAssets.Save(prefabFile));
                    Assert.False(File.Exists(prefabDiskPath));
                    Assert.Contains(logs, l => l.Contains("is not saved: the project is read-only."));

                    Assert.Null(ProjectWindowUtil.CreateScriptAssetWithContent("Assets/NewScript.cs", "// test"));
                    Assert.False(sample.Import());

                    // SaveFileIfDirty must return before OnWillSaveAssets and leave disk untouched.
                    s_WillSavePaths.Clear();
                    AssetFiles.SaveFileIfDirty(guid);
                    Assert.Empty(s_WillSavePaths);
                    Assert.False(File.Exists(prefabDiskPath));

                    // SaveDirty must also leave the dirty prefab unwritten when read-only.
                    AssetFiles.SaveDirty(writePrefabs: true);
                    Assert.False(File.Exists(prefabDiskPath));
                }
                finally
                {
                    ProjectInfo.ReadOnly = false;
                }

                // Controls when not read-only: SaveFileIfDirty calls OnWillSaveAssets and writes,
                // and PrefabAssets.Save writes the prefab file on disk.
                s_WillSavePaths.Clear();
                AssetFiles.SaveFileIfDirty(guid);
                Assert.Equal(new[] { prefabAssetPath }, s_WillSavePaths);
                Assert.True(File.Exists(prefabDiskPath));

                File.Delete(prefabDiskPath);
                Assert.True(PrefabAssets.Save(prefabFile));
                Assert.True(File.Exists(prefabDiskPath));
            }
            finally
            {
                if (root != null)
                {
                    EditorUtility.ClearDirty(root);
                    Object.DestroyImmediate(root, true);
                }
                AssetDatabaseRuntime.Invalidate(guid);
                AssetModificationProcessorInternal.ProcessorsForTests = null;
                Debug.unityLogger.logHandler = previousHandler;
                ProjectInfo.ProjectPath = previousProject;
                ProjectInfo.ReadOnly = false;
                try { Directory.Delete(tempProject, true); } catch (IOException) { }
            }
        }
    }
}
