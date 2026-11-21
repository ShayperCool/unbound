using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The documents AssetDatabase.CreateAsset and AddObjectToAsset write for engine objects, as the
    /// Unity 6000.0.71f1 Editor writes them (tests/projects/scenesave create-assets): a material
    /// whole (its file layout with a new file's own fields), a physics material, and the classes not
    /// written (refused, so CreateAsset writes nothing for them).
    /// </summary>
    public class NewAssetDocumentsTests : IDisposable
    {
        readonly List<Object> m_Owned = new List<Object>();

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        public void Dispose()
        {
            foreach (var o in m_Owned) if (o) Object.DestroyImmediate(o, true);
        }

        static string Write(Object o, long id) =>
            UnityYamlWriter.Write(new[] { o }, new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [o] = id });

        [Fact]
        public void ANewMaterialHasTheFileLayoutWithANewFilesOwnFields()
        {
            var shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/NewAsset/Plain\" {\n"
                + "Properties { _Color (\"Color\", Color) = (1, 1, 1, 1) _Value (\"Value\", Float) = 0.5 }\n"
                + "SubShader { Pass { } } }"));
            var material = Own(new Material(shader) { name = "Made" });
            material.SetFloat("_Value", 0.25f);
            var text = Write(material, 2100000);
            Assert.StartsWith(UnityYamlText.Preamble + "--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n  m_ObjectHideFlags: 0\n", text);
            Assert.Contains("  m_Name: Made\n", text);
            // The parent and the modified properties follow the shader.
            Assert.Matches("\n  m_Shader: [^\n]*\n  m_Parent: \\{fileID: 0\\}\n  m_ModifiedSerializedProperties: 0\n  m_ValidKeywords: \\[\\]\n", text);
            Assert.Contains("  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs: []\n    m_Ints: []\n    m_Floats:\n    - _Value: 0.25\n    m_Colors:\n    - _Color: {r: 1, g: 1, b: 1, a: 1}\n", text);
            Assert.EndsWith("  m_BuildTextureStacks: []\n  m_AllowLocking: 1\n", text);
            // An existing file's save keeps its own versions and parent: they are not in the save's body.
            var saved = MaterialTypeTree.FileBody(material, _ => YText.Scalar("{fileID: 0}"));
            Assert.Null(saved.Get("serializedVersion"));
            Assert.Null(saved.Get("m_Parent"));
            Assert.Null(saved.Get("m_AllowLocking"));
        }

        [Fact]
        public void AMaterialVariantIsNotWrittenIntoANewFile()
        {
            var shader = Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/NewAsset/Variant\" { Properties { _Value (\"Value\", Float) = 0.5 } SubShader { Pass { } } }"));
            var parent = Own(new Material(shader));
            var variant = Own(new Material(shader) { parent = parent });
            Assert.True(UnityYamlWriter.WritesNew(parent));
            Assert.False(UnityYamlWriter.WritesNew(variant));
            Assert.Throws<NotSupportedException>(() => Write(variant, 2100000));
        }

        /// <summary>The measured file of a physics material (create-assets Bouncy.physicMaterial),
        /// and a control with other combine modes.</summary>
        [Fact]
        public void APhysicsMaterialIsWrittenAsTheEditorWritesIt()
        {
            var bouncy = Own(new PhysicsMaterial("Bouncy") { bounciness = 0.5f, dynamicFriction = 0.2f, staticFriction = 0.3f });
            Assert.Equal(UnityYamlText.Preamble + "--- !u!134 &13400000\nPhysicsMaterial:\n"
                + "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n"
                + "  m_Name: Bouncy\n  serializedVersion: 2\n  m_DynamicFriction: 0.2\n  m_StaticFriction: 0.3\n  m_Bounciness: 0.5\n"
                + "  m_FrictionCombine: 0\n  m_BounceCombine: 0\n", Write(bouncy, 13400000));
            var combined = Own(new PhysicsMaterial("Combined") { frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Multiply });
            var text = Write(combined, 13400000);
            Assert.Contains("  m_FrictionCombine: " + (int)PhysicsMaterialCombine.Maximum + "\n", text);
            Assert.Contains("  m_BounceCombine: " + (int)PhysicsMaterialCombine.Multiply + "\n", text);
        }

        [Fact]
        public void ClassesWithoutANewFileWriterAreNotWritten()
        {
            Assert.False(UnityYamlWriter.WritesNew(Own(new Mesh())));
            Assert.False(UnityYamlWriter.WritesNew(Own(new AnimationClip())));
            Assert.False(UnityYamlWriter.WritesNew(Own(new Texture2D(2, 2))));
            Assert.True(UnityYamlWriter.WritesNew(Own(ScriptableObject.CreateInstance<PrefabTestData>())));
            Assert.Throws<NotSupportedException>(() => Write(Own(new Mesh()), 4300000));
        }
    }

    public class PrefabTestData : ScriptableObject
    {
        public int count;
    }
}
