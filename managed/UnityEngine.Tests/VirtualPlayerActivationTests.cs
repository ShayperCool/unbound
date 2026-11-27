using System;
using Unbound.Runtime;
using UnityEditor;
using Xunit;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace UnityEngine.Tests
{
    public sealed class VirtualPlayerActivationTests : IDisposable
    {
        public VirtualPlayerActivationTests()
        {
            VirtualPlayerActivation.Configure(null);
            PackageInfo.Register(Array.Empty<PackageInfo>());
            ProjectInfo.ReadOnly = false;
        }

        public void Dispose()
        {
            VirtualPlayerActivation.Configure(null);
            PackageInfo.Register(Array.Empty<PackageInfo>());
            ProjectInfo.ReadOnly = false;
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
            var scene = new SceneData { Path = "Assets/Test.unity", Name = "Test", IsLoaded = true };
            var root = new GameObject("PrefabRoot");
            try
            {
                var prefabFile = new LoadedFile { Path = "Assets/Test.prefab", MainObject = root };
                prefabFile.Objects[1] = root;
                var sample = default(UnityEditor.PackageManager.UI.Sample);

                // Control: CanSave accepts a loaded Assets/*.unity scene when not read-only.
                Assert.True(SceneFiles.CanSave(scene, "Assets/Test.unity"));

                ProjectInfo.ReadOnly = true;
                try
                {
                    Assert.False(SceneFiles.CanSave(scene, "Assets/Test.unity"));
                    Assert.False(PrefabAssets.Save(prefabFile));
                    Assert.Null(ProjectWindowUtil.CreateScriptAssetWithContent("Assets/NewScript.cs", "// test"));
                    Assert.False(sample.Import());
                    // SaveFileIfDirty returns before OnWillSaveAssets when read-only.
                    AssetFiles.SaveFileIfDirty("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                }
                finally
                {
                    ProjectInfo.ReadOnly = false;
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
