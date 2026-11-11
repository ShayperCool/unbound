using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public class SceneObjectIdentityTests
    {
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
