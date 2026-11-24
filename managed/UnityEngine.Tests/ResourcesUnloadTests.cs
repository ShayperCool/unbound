using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    public class ResourcesUnloadTests
    {
        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        static List<string> Errors(System.Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error) errors.Add(message); };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return errors;
        }

        [Fact]
        public void AMaterialSharingItsFileIsNotReadAgain()
        {
            var material = new Material { name = "shared", color = Color.red, IsPersistentAsset = true };
            var texture = new Texture2D(2, 2) { name = "atlas", IsPersistentAsset = true };
            material.mainTexture = texture;
            var file = new LoadedFile { Guid = "0123456789abcdef0123456789abcdef", Path = "Assets/Shared.asset", MainObject = material };
            file.Objects[1] = material;
            file.Objects[2] = texture;
            AssetDatabaseRuntime.AddLoaded(file);
            const string member = "Resources.UnloadAsset(Material)";
            int before = Hits(member);
            try
            {
                var errors = Errors(() => Resources.UnloadAsset(material));
                Assert.Empty(errors);
                Assert.Equal(before + 1, Hits(member));
                // Neither reverted nor its sibling destroyed.
                Assert.Equal(Color.red, material.color);
                Assert.True(texture != null);
                Assert.Same(texture, material.mainTexture);
                Assert.Same(material, AssetDatabaseRuntime.Load(file.Guid, 1, typeof(Material)));
                Assert.Same(texture, AssetDatabaseRuntime.Load(file.Guid, 2, typeof(Texture2D)));
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(file.Guid);
                AssetDatabaseRuntime.ForgetPersistentIds(file.Guid);
                Object.DestroyImmediate(material, true);
                Object.DestroyImmediate(texture, true);
            }
        }

        [Fact]
        public void ARuntimeCustomRenderTextureIsRefusedAsANonAsset()
        {
            var crt = new CustomRenderTexture(8, 8) { m_Loaded = true };
            int unloaded = 0;
            System.Action<CustomRenderTexture> onUnloaded = t => { if (t == crt) unloaded++; };
            CustomRenderTextureManager.textureUnloaded += onUnloaded;
            try
            {
                var errors = Errors(() => Resources.UnloadAsset(crt));
                Assert.Equal(new[] { "UnloadAsset can only be used on assets;" }, errors);
                Assert.Equal(0, unloaded);
                var all = new List<CustomRenderTexture>();
                CustomRenderTextureManager.GetAllCustomRenderTextures(all);
                Assert.Contains(crt, all);
            }
            finally
            {
                CustomRenderTextureManager.textureUnloaded -= onUnloaded;
                CustomRenderTextureManager.Unregister(crt);
                Object.DestroyImmediate(crt);
            }
        }

        [Fact]
        public void ACustomRenderTextureAssetIsUnloaded()
        {
            var crt = new CustomRenderTexture(8, 8) { m_Loaded = true, IsPersistentAsset = true };
            int unloaded = 0;
            System.Action<CustomRenderTexture> onUnloaded = t => { if (t == crt) unloaded++; };
            CustomRenderTextureManager.textureUnloaded += onUnloaded;
            try
            {
                var errors = Errors(() => Resources.UnloadAsset(crt));
                Assert.Empty(errors);
                Assert.Equal(1, unloaded);
                var all = new List<CustomRenderTexture>();
                CustomRenderTextureManager.GetAllCustomRenderTextures(all);
                Assert.DoesNotContain(crt, all);
            }
            finally
            {
                CustomRenderTextureManager.textureUnloaded -= onUnloaded;
                Object.DestroyImmediate(crt, true);
            }
        }

        [Fact]
        public void PersistentAssetIdSurvivesUnloadAndReassignsOnReloadUntilForgotten()
        {
            const string guid = "fedcba9876543210fedcba9876543210";
            const string matGuid = "fedcba9876543210fedcba9876543211";
            var first = ScriptableObject.CreateInstance<ScriptableObject>();
            first.IsPersistentAsset = true;
            var file = new LoadedFile { Guid = guid, Path = "Assets/UnloadReload.asset", MainObject = first };
            file.Objects[11400000] = first;
            AssetDatabaseRuntime.AddLoaded(file);
            int id = first.GetInstanceID();

            var runtime = ScriptableObject.CreateInstance<ScriptableObject>();
            int runtimeId = runtime.GetInstanceID();
            Object.DestroyImmediate(runtime);
            Assert.False(Resources.InstanceIDIsValid(runtimeId));
            Assert.Null(Resources.InstanceIDToObject(runtimeId));
            Assert.Null(UnityEditor.EditorUtility.InstanceIDToObject(runtimeId));

            var dispatcher = new ObjectDispatcher();
            try
            {
                Assert.True(Resources.InstanceIDIsValid(id));
                Assert.Same(first, Resources.InstanceIDToObject(id));

                Resources.UnloadAsset(first);
                Assert.True(first == null);

                // Without a native host, querying the unloaded ID must not throw.
                Assert.False(Resources.InstanceIDIsValid(id));
                Assert.Null(Resources.InstanceIDToObject(id));

                // When the asset exists on disk, 6000.0 keeps the persistent ID valid while unloaded
                // and resolves metadata without loading; an unmeasured release family does not.
                AssetDatabaseRuntime.AssetExistsOverride = g => g == guid;
                try
                {
                    using (UnityEngine.Tests.TestRelease.Pin(UnityEngine.Tests.TestRelease.Measured))
                    {
                        Assert.True(Resources.InstanceIDIsValid(id));
                        Assert.True(Object.DoesObjectWithInstanceIDExist(id));
                        Assert.True(UnityEditor.AssetDatabase.Contains(id));
                        Assert.True(UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(id, out string outGuid, out long outFileId));
                        Assert.Equal(guid, outGuid);
                        Assert.Equal(11400000L, outFileId);
                    }
                    using (UnityEngine.Tests.TestRelease.Pin(UnityEngine.Tests.TestRelease.Unmeasured))
                    {
                        Assert.False(Resources.InstanceIDIsValid(id));
                        Assert.False(UnityEditor.AssetDatabase.Contains(id));
                    }
                }
                finally
                {
                    AssetDatabaseRuntime.AssetExistsOverride = null;
                }

                // Reloading a new object into the same (guid, fileId) slot reuses the stable ID
                // and updates active ObjectDispatcher queues.
                using (UnityEngine.Tests.TestRelease.Pin(UnityEngine.Tests.TestRelease.Measured))
                {
                    dispatcher.EnableTypeTracking<ScriptableObject>(ObjectDispatcher.TypeTrackingFlags.Assets);
                    dispatcher.ClearTypeChanges<ScriptableObject>();

                    var reloaded = ScriptableObject.CreateInstance<ScriptableObject>();
                    reloaded.IsPersistentAsset = true;
                    var reloadedFile = new LoadedFile { Guid = guid, Path = "Assets/UnloadReload.asset", MainObject = reloaded };
                    reloadedFile.Objects[11400000] = reloaded;
                    AssetDatabaseRuntime.AddLoaded(reloadedFile);

                    Assert.Equal(id, reloaded.GetInstanceID());
                    Assert.Same(reloaded, Resources.InstanceIDToObject(id));
                    Assert.Same(reloaded, UnityEditor.EditorUtility.InstanceIDToObject(id));
                    Assert.True(Resources.InstanceIDIsValid(id));

                    using (var dispatch = dispatcher.GetTypeChangesAndClear<ScriptableObject>(Unity.Collections.Allocator.Temp))
                    {
                        bool foundInDispatch = false;
                        for (int i = 0; i < dispatch.changedID.Length; i++)
                            if (dispatch.changedID[i] == id) foundInDispatch = true;
                        Assert.True(foundInDispatch);
                    }

                    // Direct DestroyImmediate(asset, true) forgets the persistent ID and returns C# null.
                    Object.DestroyImmediate(reloaded, true);
                    Assert.False(Resources.InstanceIDIsValid(id));
                    Assert.True(ReferenceEquals(Resources.InstanceIDToObject(id), null));
                    Assert.True(ReferenceEquals(UnityEditor.EditorUtility.InstanceIDToObject(id), null));
                    Assert.False(UnityEditor.AssetDatabase.Contains(id));

                    // Same for a persistent Material destroyed directly while its file is loaded.
                    var mat = new Material { name = "mat", IsPersistentAsset = true };
                    var matFile = new LoadedFile { Guid = matGuid, Path = "Assets/DestroyedMat.mat", MainObject = mat };
                    matFile.Objects[2100000] = mat;
                    AssetDatabaseRuntime.AddLoaded(matFile);
                    int matId = mat.GetInstanceID();
                    Object.DestroyImmediate(mat, true);
                    Assert.False(Resources.InstanceIDIsValid(matId));
                    Assert.True(ReferenceEquals(Resources.InstanceIDToObject(matId), null));
                    Assert.False(UnityEditor.AssetDatabase.Contains(matId));
                }
            }
            finally
            {
                dispatcher.Dispose();
                AssetDatabaseRuntime.AssetExistsOverride = null;
                AssetDatabaseRuntime.Invalidate(guid);
                AssetDatabaseRuntime.ForgetPersistentIds(guid);
                AssetDatabaseRuntime.Invalidate(matGuid);
                AssetDatabaseRuntime.ForgetPersistentIds(matGuid);
            }
        }
    }
}
