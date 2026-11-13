using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>GameObjects a project's importer makes or copies belong to the asset (Unbound's
    /// model: in no scene, never awake), and can be put together into one hierarchy.</summary>
    public sealed class ImportedGameObjectTests
    {
        sealed class AwakeCounter : MonoBehaviour
        {
            internal static int Awakes;
            void Awake() => Awakes++;
        }

        [Fact]
        public void ObjectsMadeOrInstantiatedDuringAnImportAreAssetsOutsideScenes()
        {
            var template = new GameObject("template");
            template.AddComponent<AwakeCounter>();
            int awakes = AwakeCounter.Awakes;
            GameObject root, copy, child;
            using (ScriptedImports.Scope())
            {
                root = new GameObject("root");
                root.AddComponent<AwakeCounter>();
                copy = Object.Instantiate(template);
                copy.transform.SetParent(root.transform);
                child = Object.Instantiate(template, root.transform);
            }
            foreach (var go in new[] { root, copy, child })
            {
                Assert.True(go.m_IsPrefabAsset, go.name);
                Assert.False(go.scene.IsValid(), go.name);
            }
            Assert.Same(root.transform, copy.transform.parent);
            Assert.Same(root.transform, child.transform.parent);
            Assert.Equal(awakes, AwakeCounter.Awakes);
            Assert.DoesNotContain(root, template.scene.GetRootGameObjects());
            // Outside an import a copy is an ordinary scene object again.
            var outside = Object.Instantiate(template);
            Assert.False(outside.m_IsPrefabAsset);
            Assert.Equal(awakes + 1, AwakeCounter.Awakes);
            foreach (var go in new[] { outside, template }) Object.DestroyImmediate(go);
            Object.DestroyImmediate(root, true);
        }
    }
}
