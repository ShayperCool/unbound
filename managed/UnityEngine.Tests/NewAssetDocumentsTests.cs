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

        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source), "..", ".."));

        static string Reference(string path) => System.IO.File.ReadAllText(System.IO.Path.Combine(Repo(), "tests", "projects", "scenesave", "Reference", "unity-6000.0.71f1", path));

        /// <summary>The meshes and the texture the Editor wrote (create-assets Tri, engine-assets
        /// Skinned, Shaped, Custom, Alpha), made the same way and written byte for byte alike.</summary>
        [Fact]
        public void MeshesAndTexturesAreWrittenAsTheEditorWroteThem()
        {
            var tri = Own(new Mesh { name = "Tri" });
            tri.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            tri.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            tri.triangles = new[] { 0, 1, 2 };
            tri.RecalculateNormals();
            tri.RecalculateBounds();
            Assert.Equal(Reference("create-assets/files/Assets/Created/Tri.asset"), Write(tri, 4300000));

            var skinned = Own(new Mesh { name = "Skinned" });
            skinned.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            skinned.triangles = new[] { 0, 1, 2 };
            skinned.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
                new BoneWeight { boneIndex0 = 1, weight0 = 0.75f, boneIndex1 = 0, weight1 = 0.25f },
            };
            skinned.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(0f, -1f, 0f)) };
            skinned.RecalculateBounds();
            Assert.Equal(Reference("engine-assets/files/Assets/Engine/Skinned.asset"), Write(skinned, 4300000));

            var shaped = Own(new Mesh { name = "Shaped" });
            shaped.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            shaped.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            shaped.triangles = new[] { 0, 1, 2 };
            shaped.AddBlendShapeFrame("Up", 50f, new[] { Vector3.up * 0.5f, Vector3.up * 0.5f, Vector3.zero }, null, null);
            shaped.AddBlendShapeFrame("Up", 100f, new[] { Vector3.up, Vector3.up, Vector3.zero }, null, null);
            shaped.AddBlendShapeFrame("Out", 100f, new[] { Vector3.zero, Vector3.zero, Vector3.forward }, new[] { Vector3.up, Vector3.zero, Vector3.zero }, null);
            shaped.RecalculateBounds();
            Assert.Equal(Reference("engine-assets/files/Assets/Engine/Shaped.asset"), Write(shaped, 4300000));

            var custom = Own(new Mesh { name = "Custom" });
            custom.SetVertexBufferParams(3,
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttributeFormat.Float16, 4, 0),
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Normal, UnityEngine.Rendering.VertexAttributeFormat.Float32, 3, 1));
            ushort H(float f) => Mathf.FloatToHalf(f);
            custom.SetVertexBufferData(new ushort[] { H(0), H(0), H(0), H(1), H(1), H(0), H(0), H(1), H(0), H(1), H(0), H(1) }, 0, 0, 12, 0);
            custom.SetVertexBufferData(new[] { Vector3.back, Vector3.back, Vector3.back }, 0, 0, 3, 1);
            custom.SetIndexBufferParams(3, UnityEngine.Rendering.IndexFormat.UInt16);
            custom.SetIndexBufferData(new ushort[] { 0, 1, 2 }, 0, 0, 3);
            custom.subMeshCount = 1;
            custom.SetSubMesh(0, new UnityEngine.Rendering.SubMeshDescriptor(0, 3, MeshTopology.Triangles));
            custom.RecalculateBounds();
            Assert.Equal(Reference("engine-assets/files/Assets/Engine/Custom.asset"), Write(custom, 4300000));

            var alpha = Own(new Texture2D(2, 2, TextureFormat.Alpha8, false) { name = "Alpha" });
            alpha.SetPixels32(new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 64), new Color32(0, 0, 0, 128), new Color32(0, 0, 0, 255) });
            alpha.Apply();
            Assert.Equal(Reference("engine-assets/snapshots/alpha.file"), Write(alpha, 2800000));
        }

        /// <summary>A mesh or texture document is kept as read when its file is saved unless its
        /// object was edited (dirty); an edited one writes its data into the document
        /// (tests/projects/scenesave native-saves).</summary>
        [Fact]
        public void MeshAndTextureDocumentsReadFromFilesAreKeptAsReadUntilEdited()
        {
            var text = Reference("create-assets/files/Assets/Created/Tri.asset");
            var doc = UnityYamlText.Parse(text)[0];
            var mesh = Own(new Mesh { name = "Tri" });
            mesh.vertices = new[] { Vector3.zero, Vector3.right * 2f, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            int unlocated = 0;
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [mesh] = 4300000 };
            Assert.False(NativeDocuments.Write(doc, mesh, ids, new AssetDatabaseRuntime.Locations(), ref unlocated));
            Assert.Equal(text, UnityYamlText.Write(new List<YTextDocument> { doc }));
            var texture = Own(new Texture2D(2, 2));
            Assert.False(NativeDocuments.Write(UnityYamlText.Parse(Reference("engine-assets/snapshots/alpha.file"))[0], texture, ids, new AssetDatabaseRuntime.Locations(), ref unlocated));

            EditorUtility.SetDirty(mesh);
            Assert.True(NativeDocuments.Write(doc, mesh, ids, new AssetDatabaseRuntime.Locations(), ref unlocated));
            Assert.Contains("m_Center: {x: 1, y: 0.5, z: 0}", UnityYamlText.Write(new List<YTextDocument> { doc }));
            // A document without the vertex data Unbound reads is kept as read.
            var bare = UnityYamlText.Parse("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!43 &4300000\nMesh:\n  m_Name: Tri\n  m_Unread: 7\n")[0];
            Assert.False(NativeDocuments.Write(bare, mesh, ids, new AssetDatabaseRuntime.Locations(), ref unlocated));
        }

        /// <summary>RecalculateBounds as documented for 6000.0 (the mesh and its sub-meshes, from
        /// Float32 positions only) and as the Editor's files show (blend shapes at full weight).</summary>
        [Fact]
        public void RecalculateBoundsTakesSubMeshesAndBlendShapesAndOnlyFloatPositions()
        {
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.vertices = new[] { Vector3.zero, Vector3.right * 4f, Vector3.up * 4f };
            // The vertices changed: the sub-mesh's bounds are stale until recalculated (engine-assets
            // BoundsStale, BoundsRecalculated).
            Assert.Equal(new Vector3(0.5f, 0.5f, 0f), mesh.GetSubMesh(0).bounds.extents);
            mesh.RecalculateBounds();
            Assert.Equal(new Vector3(2f, 2f, 0f), mesh.GetSubMesh(0).bounds.extents);
            Assert.Equal(new Vector3(2f, 2f, 0f), mesh.bounds.extents);
            mesh.AddBlendShapeFrame("Out", 100f, new[] { Vector3.zero, Vector3.zero, Vector3.forward * 2f }, null, null);
            mesh.RecalculateBounds();
            Assert.Equal(new Vector3(2f, 2f, 1f), mesh.bounds.extents);
            Assert.Equal(new Vector3(2f, 2f, 0f), mesh.GetSubMesh(0).bounds.extents);

            var half = Own(new Mesh());
            half.SetVertexBufferParams(3, new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttributeFormat.Float16, 4));
            half.SetVertexBufferData(new ushort[] { 0, 0, 0, 0x3c00, 0x3c00, 0, 0, 0x3c00, 0, 0x3c00, 0, 0x3c00 }, 0, 0, 12);
            half.SetIndexBufferParams(3, UnityEngine.Rendering.IndexFormat.UInt16);
            half.SetIndexBufferData(new ushort[] { 0, 1, 2 }, 0, 0, 3);
            half.SetSubMesh(0, new UnityEngine.Rendering.SubMeshDescriptor(0, 3));
            half.bounds = new Bounds(Vector3.one, Vector3.one);
            half.RecalculateBounds();
            Assert.Equal(default, half.bounds);
            Assert.Equal(default, half.GetSubMesh(0).bounds);
            Assert.Equal(3, half.GetSubMesh(0).vertexCount);
        }

        /// <summary>A texture made by script ignores the mipmap limit (Unity's Texture2D constructors).</summary>
        [Fact]
        public void ATextureMadeByScriptIgnoresTheMipmapLimit()
        {
            Assert.True(Own(new Texture2D(2, 2)).ignoreMipmapLimit);
            Assert.True(Own(new Texture2D(2, 2, TextureFormat.RGB24, true)).ignoreMipmapLimit);
            Assert.True(Own(new Texture2D(2, 2, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.None)).ignoreMipmapLimit);
        }

        /// <summary>The edits of mesh, texture and physics material assets dirty them, whether made
        /// this session or read from their files; reading does not, SetPixel alone does not, and an
        /// object that is no asset is not dirtied (tests/projects/scenesave native-saves mesh.dirty,
        /// texture.dirtySetPixel, texture.dirty, physic.dirty).</summary>
        [Fact]
        public void EditsOfAssetsReadFromFilesDirtyThem()
        {
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { Vector3.zero };
            Assert.False(EditorUtility.IsDirty(mesh));
            mesh.IsPersistentAsset = true;
            mesh.ReadSerialized(UnityYamlText.Parse(Reference("create-assets/files/Assets/Created/Tri.asset"))[0].Body.ToNode(), null);
            Assert.False(EditorUtility.IsDirty(mesh));
            mesh.vertices = new[] { Vector3.zero, Vector3.one, Vector3.up };
            Assert.True(EditorUtility.IsDirty(mesh));
            var texture = Own(new Texture2D(2, 2));
            texture.IsPersistentAsset = true;
            texture.SetPixel(0, 0, Color.black);
            Assert.False(EditorUtility.IsDirty(texture));
            texture.Apply();
            Assert.True(EditorUtility.IsDirty(texture));
            var physics = Own(new PhysicsMaterial());
            physics.bounciness = 0.5f;
            Assert.False(EditorUtility.IsDirty(physics));
            physics.IsPersistentAsset = true;
            physics.bounciness = 0.9f;
            Assert.True(EditorUtility.IsDirty(physics));
        }

        [Fact]
        public void ClassesWithoutANewFileWriterAreNotWritten()
        {
            var animator = Own(new AnimationClip());
            animator.SetCurve("", typeof(Animator), "Speed", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            Assert.False(UnityYamlWriter.WritesNew(animator));
            var unreadable = Own(new Texture2D(2, 2));
            unreadable.Apply(false, true);
            Assert.False(UnityYamlWriter.WritesNew(unreadable));
            Assert.False(UnityYamlWriter.WritesNew(Own(new Texture2D(4, 4, TextureFormat.DXT1Crunched, false))));
            Assert.False(UnityYamlWriter.WritesNew(Own(new Texture2D(4, 4, TextureFormat.DXT1, false))));
            Assert.True(UnityYamlWriter.WritesNew(Own(new Texture2D(2, 2))));
            Assert.True(UnityYamlWriter.WritesNew(Own(new Mesh())));
            Assert.True(UnityYamlWriter.WritesNew(Own(ScriptableObject.CreateInstance<NewAssetTestData>())));
            Assert.Throws<NotSupportedException>(() => Write(Own(new LightingSettings()), 7400000));
        }
    }

    public class NewAssetTestData : ScriptableObject
    {
        public int count;
    }
}
