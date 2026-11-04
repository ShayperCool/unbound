using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>An imported model's Mesh takes its readability from the model's Read/Write Enabled
    /// import setting, carried in the native <c>model-mesh</c> answer's <c>readable</c> field.
    /// Documented (Unity 6000.0, https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-isReadable.html):
    /// "You can set this value using the Read/Write Enabled checkbox when importing a model to Unity"
    /// and "When Unity creates a Mesh from script, this value is initially set to true".
    /// Mesh.AcquireReadOnlyMeshData (same release): "This method will throw an
    /// InvalidOperationException if Mesh.isReadable is false for one or more input meshes."
    /// Not a measured Unity contract here: the Mesh-isReadable note that data arrays error "from script
    /// at runtime" but are "always allowed in the Unity Editor outside of the game and rendering loop";
    /// what Play mode in the Editor does with them is unmeasured, and the one test about it below only
    /// preserves Unbound's current behaviour.</summary>
    public class ImportedMeshReadabilityTests
    {
        static string Floats(params float[] values) => Convert.ToBase64String(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

        static string Ints(params int[] values) => Convert.ToBase64String(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

        /// <summary>Per vertex four bone indices, then four weights (crate::models::mesh_json).</summary>
        static string BoneWeights(params (int bone, float weight)[] perVertex)
        {
            var bytes = new List<byte>();
            foreach (var (bone, weight) in perVertex)
            {
                foreach (var b in new[] { bone, 0, 0, 0 }) bytes.AddRange(BitConverter.GetBytes(b));
                foreach (var w in new[] { weight, 1f - weight, 0f, 0f }) bytes.AddRange(BitConverter.GetBytes(w));
            }
            return Convert.ToBase64String(bytes.ToArray());
        }

        /// <summary>A <c>model-mesh</c> answer as the native side writes it: the geometry fields of
        /// crate::models::mesh_json (one triangle with two submeshes, two uv channels, colours, skin and
        /// a blend shape), plus <c>readable</c> unless <paramref name="readable"/> is null (an answer
        /// from a native side older than the field).</summary>
        static JsonElement Answer(object readable)
        {
            var answer = new Dictionary<string, object>
            {
                ["name"] = "Tri",
                ["vertexCount"] = 3,
                ["positions"] = Floats(0, 0, 0, 0, 1, 0, -1, 0, 0),
                ["normals"] = Floats(0, 0, 1, 0, 0, 1, 0, 0, 1),
                ["tangents"] = Floats(1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1),
                ["uvs"] = new[] { Floats(0, 0, 0, 1, 1, 0), Floats(0.5f, 0.5f, 0.25f, 0.75f, 1, 1) },
                ["colors"] = Floats(1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1),
                ["submeshes"] = new[] { Ints(0, 1, 2), Ints(2, 1, 0) },
                ["boneWeights"] = BoneWeights((0, 1f), (1, 0.75f), (0, 0.5f)),
                ["bindPoses"] = Floats(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 1, 2, 3, 1),
                ["boundsMin"] = new[] { -1f, 0f, 0f },
                ["boundsMax"] = new[] { 0f, 1f, 0f },
                ["blendShapes"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = "Up",
                        ["frames"] = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["weight"] = 100f,
                                ["vertices"] = Floats(0, 0.5f, 0, 0, 0.5f, 0, 0, 0.5f, 0),
                                ["normals"] = Floats(0, 0, 0, 0, 0, 0, 0, 0, 0),
                                ["tangents"] = Floats(0, 0, 0, 0, 0, 0, 0, 0, 0),
                            },
                        },
                    },
                },
            };
            if (readable != null) answer["readable"] = readable;
            return JsonDocument.Parse(JsonSerializer.Serialize(answer)).RootElement;
        }

        static Mesh Imported(object readable)
        {
            var mesh = new Mesh { name = "Tri" };
            mesh.FillFromModel(Answer(readable));
            return mesh;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ImportedMeshesReportTheModelsReadWriteSetting(bool readable)
        {
            Assert.Equal(readable, Imported(readable).isReadable);
        }

        [Fact]
        public void ReadWriteDisabledMeshesRefuseReadOnlyMeshData()
        {
            var off = Imported(false);
            Assert.Throws<InvalidOperationException>(() => Mesh.AcquireReadOnlyMeshData(off));
            var on = Imported(true);
            Assert.Throws<InvalidOperationException>(() => Mesh.AcquireReadOnlyMeshData(new[] { on, off }));
            using (var data = Mesh.AcquireReadOnlyMeshData(on))
            {
                Assert.Equal(1, data.Length);
                Assert.Equal(3, data[0].vertexCount);
            }
        }

        /// <summary>The setting is adopted after the geometry: a Read/Write-disabled mesh holds exactly
        /// what the same answer without the field fills (the data renderers, colliders and skinning
        /// read), and that is the answer's geometry.</summary>
        [Fact]
        public void TheSettingLeavesTheFilledGeometryUnchanged()
        {
            var off = Imported(false);
            var before = Imported(null);
            Assert.False(off.isReadable);
            Assert.True(before.isReadable);

            Assert.Equal(new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(-1, 0, 0) }, before.m_Vertices);
            Assert.Equal(before.m_Vertices, off.m_Vertices);
            Assert.Equal(before.m_Normals, off.m_Normals);
            Assert.Equal(before.m_Tangents, off.m_Tangents);
            Assert.Equal(new Color32[] { new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255) }, before.m_Colors);
            Assert.Equal(before.m_Colors, off.m_Colors);
            for (int channel = 0; channel < 8; channel++)
            {
                Assert.Equal(before.m_UVDimensions[channel], off.m_UVDimensions[channel]);
                Assert.Equal(before.m_UVs[channel], off.m_UVs[channel]);
            }
            Assert.Equal(2, before.m_UVDimensions[1]);

            Assert.Equal(2, before.m_SubMeshes.Count);
            Assert.Equal(before.m_SubMeshes.Count, off.m_SubMeshes.Count);
            for (int i = 0; i < before.m_SubMeshes.Count; i++)
            {
                Assert.Equal(before.m_SubMeshes[i].Indices, off.m_SubMeshes[i].Indices);
                Assert.Equal(before.m_SubMeshes[i].Topology, off.m_SubMeshes[i].Topology);
                Assert.Equal(before.m_SubMeshes[i].Bounds, off.m_SubMeshes[i].Bounds);
            }
            Assert.Equal(new[] { 2, 1, 0 }, before.m_SubMeshes[1].Indices);
            Assert.Equal(before.bounds, off.bounds);
            Assert.Equal(new Vector3(-1, 0, 0), before.bounds.min);
            Assert.Equal(new Vector3(0, 1, 0), before.bounds.max);

            Assert.Equal(3, before.m_BoneWeights.Length);
            Assert.Equal(before.m_BoneWeights, off.m_BoneWeights);
            Assert.Equal(0.75f, before.m_BoneWeights[1].weight0);
            Assert.Equal(2, before.m_Bindposes.Length);
            Assert.Equal(before.m_Bindposes, off.m_Bindposes);

            Assert.Equal(1, before.m_BlendShapes.Count);
            Assert.Equal(before.m_BlendShapes.Count, off.m_BlendShapes.Count);
            Assert.Equal(before.m_BlendShapes[0].Name, off.m_BlendShapes[0].Name);
            var frame = before.m_BlendShapes[0].Frames[0];
            var offFrame = off.m_BlendShapes[0].Frames[0];
            Assert.Equal(frame.Weight, offFrame.Weight);
            Assert.Equal(frame.Vertices, offFrame.Vertices);
            Assert.Equal(frame.Normals, offFrame.Normals);
            Assert.Equal(frame.Tangents, offFrame.Tangents);
            Assert.Equal(new Vector3(0, 0.5f, 0), frame.Vertices[2]);

            // Both still go to the renderer with their geometry.
            Assert.True(off.m_Dirty);
            Assert.Equal(before.m_Dirty, off.m_Dirty);
        }

        /// <summary>An answer without the field (a native side older than it) keeps the value the mesh
        /// was created with, true: how such meshes behaved before, rather than a guessed setting.
        /// A field that is not a boolean is treated the same way.</summary>
        [Fact]
        public void AnswersWithoutTheFieldKeepTheCreatedMeshsReadability()
        {
            Assert.Null(Mesh.ModelReadable(Answer(null)));
            Assert.True(Imported(null).isReadable);
            Assert.Null(Mesh.ModelReadable(Answer(0)));
            Assert.True(Imported(0).isReadable);
            Assert.Null(Mesh.ModelReadable(Answer("false")));
            Assert.True(Mesh.ModelReadable(Answer(false)) is false);
            Assert.True(Mesh.ModelReadable(Answer(true)) is true);
        }

        [Fact]
        public void ScriptCreatedMeshesStartReadable()
        {
            var mesh = new Mesh();
            Assert.True(mesh.isReadable);
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
            mesh.triangles = new[] { 0, 1, 2 };
            Assert.True(mesh.isReadable);
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                Assert.Equal(3, data[0].vertexCount);
            // Importing another mesh as unreadable does not touch this one.
            Assert.False(Imported(false).isReadable);
            Assert.True(mesh.isReadable);
        }

        /// <summary>Preservation only, not a Unity contract: Unbound plays in the Editor, where the
        /// documented data-array error "from script at runtime" and the "outside of the game and
        /// rendering loop" exemption leave Play mode unmeasured. Until that is measured, a
        /// Read/Write-disabled imported mesh keeps answering its data arrays as before.</summary>
        [Fact]
        public void ReadWriteDisabledMeshesKeepAnsweringDataArrays()
        {
            var off = Imported(false);
            Assert.Equal(3, off.vertices.Length);
            Assert.Equal(new[] { 0, 1, 2 }, off.GetTriangles(0));
            Assert.Equal(3, off.boneWeights.Length);
        }
    }
}
