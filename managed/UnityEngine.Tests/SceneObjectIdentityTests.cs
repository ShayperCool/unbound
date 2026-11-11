using System;
using System.Linq;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public class SceneObjectIdentityTests
    {
        static readonly List<string> FocusRecords = new List<string>();

        public class FocusReceiver : MonoBehaviour
        {
            void OnApplicationFocus(bool focused) => FocusRecords.Add(name);
        }

        // Preserve Unbound's existing traversal and messages reference; cross-object callback
        // order is not a documented Unity guarantee.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FileIdSignDoesNotReverseExistingApplicationCallbackTraversal(bool positiveFileIds)
        {
            var previousScene = SceneRegistry.Active;
            var scene = SceneRegistry.Create("IdentityCallbacks", "Assets/IdentityCallbacks.unity", -1);
            var objects = new List<GameObject>();
            FocusRecords.Clear();
            try
            {
                scene.IsLoaded = true;
                SceneRegistry.Loaded.Add(scene);
                SceneRegistry.Active = scene;
                GameObject New(string name)
                {
                    var go = new GameObject(name);
                    objects.Add(go);
                    return go;
                }
                using (positiveFileIds ? ObjectRegistry.AsAssets() : default)
                {
                    scene.FileIds[New("FileFirst")] = 10;
                    scene.FileIds[New("FileSecond")] = 20;
                }
                foreach (var go in objects) go.AddComponent<FocusReceiver>();
                New("RuntimeOlder").AddComponent<FocusReceiver>();
                New("RuntimeNewer").AddComponent<FocusReceiver>().enabled = false;
                var messages = ScriptMessages.For(typeof(FocusReceiver));
                Lifecycle.BroadcastByObject(m => ReferenceEquals(m, messages) ? m.OnApplicationFocus : null, true);
                Assert.Equal(new[] { "RuntimeNewer", "RuntimeOlder", "FileFirst", "FileSecond" }, FocusRecords);
                // Source-scene metadata is retired after its surviving objects leave that scene.
                // The objects keep their creation order even when they no longer have a file marker.
                scene.FileIds.Clear();
                FocusRecords.Clear();
                Lifecycle.BroadcastByObject(m => ReferenceEquals(m, messages) ? m.OnApplicationFocus : null, false);
                Assert.Equal(new[] { "RuntimeNewer", "RuntimeOlder", "FileSecond", "FileFirst" }, FocusRecords);
            }
            finally
            {
                foreach (var go in objects) if (go != null) Object.DestroyImmediate(go);
                SceneRegistry.Forget(scene);
                SceneRegistry.Active = previousScene;
                FocusRecords.Clear();
            }
        }

        [Theory]
        [InlineData("6000.0.71f1", 1)]
        [InlineData("2022.3.62f1", -1)]
        public void FileIdentityIsSeparateFromPersistenceAndNewObjectIdentity(string release, int sceneSign)
        {
            string previousRelease = ProjectInfo.UnityVersion;
            var scene = SceneRegistry.Create("Identity", "Assets/Identity.unity", -1);
            LoadedFile file = null;
            GameObject runtime = null, clone = null;
            try
            {
                ProjectInfo.UnityVersion = release;
                const string yaml = "--- !u!1 &10\nGameObject:\n  m_Name: FileObject\n  m_IsActive: 1\n  m_Component:\n  - component: {fileID: 11}\n"
                    + "--- !u!4 &11\nTransform:\n  m_GameObject: {fileID: 10}\n  m_Father: {fileID: 0}\n  m_Children: []\n";
                file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), scene.Path, null, ObjectGraphBuilder.Kind.Scene, scene);
                var loaded = (GameObject)file.Objects[10];
                var transform = file.Objects[11];
                Assert.Equal(sceneSign, Math.Sign(loaded.GetInstanceID()));
                Assert.Equal(sceneSign, Math.Sign(transform.GetInstanceID()));
                Assert.False(loaded.IsPersistentAsset);
                Assert.Throws<ArgumentException>(() => new LazyLoadReference<GameObject>(loaded));
                var reference = new LazyLoadReference<GameObject>(loaded.GetInstanceID());
                Assert.False(reference.isBroken);
                Assert.Same(loaded, reference.asset);
                Assert.Same(loaded, Resources.InstanceIDToObject(loaded.GetInstanceID()));
                runtime = new GameObject("NewObject");
                clone = Object.Instantiate(loaded);
                Assert.True(runtime.GetInstanceID() < 0);
                Assert.True(clone.GetInstanceID() < 0);
                Assert.True(clone.transform.GetInstanceID() < 0);
                var ids = new[] { loaded.GetInstanceID(), transform.GetInstanceID(), runtime.GetInstanceID(), clone.GetInstanceID(), clone.transform.GetInstanceID() };
                Assert.DoesNotContain(0, ids);
                Assert.Equal(ids.Length, ids.Distinct().Count());
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                if (runtime != null) Object.DestroyImmediate(runtime);
                if (file != null) foreach (var root in file.Roots) Object.DestroyImmediate(root);
                SceneRegistry.Forget(scene);
                ProjectInfo.UnityVersion = previousRelease;
            }
        }
    }
}
