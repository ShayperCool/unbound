using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;
using Object = UnityEngine.Object;

// The file ids of a ScriptedImporter's objects: leaf objects by identifier and class (and the
// .meta's name tables), the hierarchies of added GameObjects by path. Measured in the Unity
// 6000.0.71f1 Editor (tests/projects/scriptedfileids and its HierarchyControl, tests/projects/shadergraph).

namespace UnityEngine.Tests
{
    public class ScriptedImportReferenceTests
    {
        sealed class Data : ScriptableObject { }

        // The .meta's name tables give an added object its file id when the entry names its identifier
        // and its class (matches the Unity 6000.0.71f1 Editor with a texture and a script object,
        // tests/projects/scriptedfileids, and with a Shader Graph's shader, tests/projects/shadergraph).
        [Theory]
        [InlineData("  fileIDToRecycleName:\n    2800000: MainAsset\n", 2800000L, false)]
        [InlineData("  fileIDToRecycleName:\n    11400000: MainAsset\n", 0L, false)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      28: 1234567\n    second: MainAsset\n", 1234567L, false)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      114: 7654321\n    second: MainAsset\n", 0L, false)]
        [InlineData("  internalIDToNameTable: []\n", 0L, false)]
        [InlineData("  fileIDToRecycleName:\n    2800000: Other\n", 0L, false)]
        [InlineData("  fileIDToRecycleName:\n    2800000: MainAsset\n    11400000: MainAsset\n", 11400000L, true)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      114: 7654321\n    second: MainAsset\n", 7654321L, true)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      114: 7654321\n      28: 1234567\n    second: MainAsset\n", 1234567L, false)]
        [InlineData("  internalIDToNameTable:\n  - first:\n      28: \n    second: MainAsset\n", 0L, false)]
        public void TheMetaNameTableGivesFileIdsByIdentifierAndClass(string table, long expected, bool scriptObject)
        {
            var settings = TestYaml.DocumentBody("ScriptedImporter:\n" + table + "  externalObjects: {}\n  userData: \n");
            Object obj = scriptObject ? ScriptableObject.CreateInstance<Data>() : new Texture2D(1, 1);
            var derived = ScriptedImports.FileId("MainAsset", obj);
            Assert.Equal(expected == 0 ? derived : expected, ScriptedImports.FileId("MainAsset", obj, ScriptedImports.NameTable(settings)));
        }

        [Fact]
        public void TheDerivedFileIdIsXxHashOfClassAndIdentifier()
        {
            // The id newer Shader Graph assets are referenced by (no name table in their .meta).
            Assert.Equal(-6465566751694194690L, (long)ScriptedImports.XxHash64(System.Text.Encoding.UTF8.GetBytes("Type:Shader->MainAsset0")));
        }

        // A hierarchy as tests/projects/scriptedfileids HierarchyControl's Deep importer builds it,
        // with the ids the Editor gives each object.
        [Fact]
        public void AnAddedHierarchyTakesIdsByPath()
        {
            GameObject Child(GameObject parent, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent.transform, false);
                return go;
            }
            var tree = new GameObject("Tree");
            var body = tree.AddComponent<Rigidbody>();
            var group = tree.AddComponent<LODGroup>();
            var lod0 = Child(tree, "LOD0");
            var renderer = lod0.AddComponent<MeshRenderer>();
            var leaf = Child(lod0, "Leaf");
            var twin0 = Child(tree, "Twin");
            var twin1 = Child(tree, "Twin");
            var extra = Child(tree, "Extra");
            var capsule = extra.AddComponent<CapsuleCollider>();
            var second = new GameObject("Second");
            var mesh = new Mesh { name = "Mesh" };
            try
            {
                var added = new List<(string, Object)> { ("Top", tree), ("Extra", extra), ("Second", second), ("Mesh", mesh) };
                var ids = new Dictionary<long, Object>();
                var given = ScriptedImports.HierarchyFileIds(added, ids);
                Assert.Same(tree, ids[-5627406255049210624L]);
                Assert.Same(tree.transform, ids[7289620436457526320L]);
                Assert.Same(body, ids[-484973370338017211L]);
                Assert.Same(group, ids[3140244981883374007L]);
                Assert.Same(renderer, ids[1796223597023407233L]);
                Assert.Same(leaf, ids[-6962124031331360859L]);
                // Same-named siblings take the next index in order, their components too.
                Assert.Same(twin0, ids[-5667160776855022005L]);
                Assert.Same(twin1, ids[4441893058563902596L]);
                Assert.Same(twin0.transform, ids[3966244616355061162L]);
                Assert.Same(twin1.transform, ids[-5302503290530769749L]);
                // A child added under its own identifier keeps its path in the hierarchy.
                Assert.Same(extra, ids[-1544261197067733884L]);
                Assert.Same(capsule, ids[-1365718517604799137L]);
                Assert.Same(second, ids[-1440060632856835699L]);
                Assert.Same(second.transform, ids[4593882995610042022L]);
                Assert.Contains(extra, given);
                Assert.DoesNotContain(mesh, given);
                Assert.Equal(18, ids.Count);
                Assert.Equal(-5523277661179574252L, ScriptedImports.FileId("Mesh", mesh));
            }
            finally
            {
                Object.DestroyImmediate(tree);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
