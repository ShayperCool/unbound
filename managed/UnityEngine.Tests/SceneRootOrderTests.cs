using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xunit;
using UObject = UnityEngine.Object;

namespace Unbound.Tests
{
    [Collection("Frames")]
    public sealed class SceneRootOrderTests
    {
        static string Node(int id, string name, int parent = 0, int instance = 0) =>
            $"--- !u!1 &{id}\nGameObject:\n  m_Name: {name}\n  m_IsActive: 1\n  m_Component:\n  - component: {{fileID: {id + 1}}}\n"
            + $"--- !u!4 &{id + 1}\nTransform:\n  m_GameObject: {{fileID: {id}}}\n  m_Father: {{fileID: {parent}}}\n  m_PrefabInstance: {{fileID: {instance}}}\n  m_Children: []\n";

        static string RootList(string entries) =>
            "--- !u!1660057539 &9000\nSceneRoots:\n  m_Roots:\n" + entries;

        [Theory]
        [InlineData("  - {fileID: 100}\n  - {fileID: 11}\n", "Prefab,Plain,Unlisted")]
        [InlineData("  - {fileID: 11}\n  - {fileID: 100}\n", "Plain,Prefab,Unlisted")]
        public void PrefabInstanceAndTransformReferencesShareTheAuthoredRootList(string entries, string expected)
        {
            // The child shares the instance reference and precedes its root in the input.
            // It must neither replace that root nor become an additional root.
            Check(Node(30, "Child", parent: 21, instance: 100) + Node(10, "Plain")
                + Node(20, "Prefab", instance: 100) + Node(40, "Unlisted") + RootList(entries), expected);
        }

        [Fact]
        public void ExpandedTransformReferencesNeedNoPrefabInstanceAlias()
        {
            Check(Node(10, "Plain") + Node(20, "Prefab", instance: 100)
                + RootList("  - {fileID: 21}\n  - {fileID: 11}\n"), "Prefab,Plain");
        }

        [Fact]
        public void ScenesWithoutARootListRetainTheirInputOrder()
        {
            Check(Node(10, "Plain") + Node(20, "Prefab", instance: 100), "Plain,Prefab");
        }

        static void Check(string yaml, string expected)
        {
            var data = SceneRegistry.Create("RootOrder", "Assets/RootOrder.unity", -1);
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), data.Path, null, ObjectGraphBuilder.Kind.Scene, data);
            try
            {
                data.Roots.AddRange(file.SceneRoots ?? file.Roots);
                data.IsLoaded = true;
                data.LoadingState = Scene.LoadingState.Loaded;
                var scene = new Scene(data.Handle);
                Assert.Equal(expected.Split(','), scene.GetRootGameObjects().Select(r => r.name));
                Assert.Equal(Enumerable.Range(0, data.Roots.Count), data.Roots.Select(r => r.transform.GetSiblingIndex()));
                Assert.Equal(data.Roots.Count, scene.rootCount);
                if (file.Objects.TryGetValue(30, out var child))
                    Assert.Equal("Prefab", ((GameObject)child).transform.parent.name);
            }
            finally
            {
                foreach (var root in file.Roots) UObject.DestroyImmediate(root);
                SceneRegistry.Forget(data);
            }
        }
    }
}
