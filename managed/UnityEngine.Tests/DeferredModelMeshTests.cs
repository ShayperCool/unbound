using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Deferred imported unreadable model meshes keep only metadata on the managed side and upload
    /// directly from the native side on <see cref="Mesh.Sync"/> without allocating managed vertex
    /// or index arrays, while transparently hydrating full CPU geometry on any required CPU read,
    /// clone, edit, or engine access (physics, skinning, MeshData, list index getters, Edit mode).
    /// </summary>
    [Collection("Serial")]
    public class DeferredModelMeshTests
    {
        static string Floats(params float[] values) =>
            Convert.ToBase64String(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

        static string Ints(params int[] values) =>
            Convert.ToBase64String(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

        static string SampleJson()
        {
            var answer = new Dictionary<string, object>
            {
                ["name"] = "DeferredProp",
                ["vertexCount"] = 4,
                ["positions"] = Floats(0, 0, 0, 1, 2, 0, -1, 0.5f, 3, 2, -1, 1),
                ["normals"] = Floats(0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0),
                ["tangents"] = Floats(1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1),
                ["uvs"] = new[]
                {
                    Floats(0, 0, 0.5f, 1, 1, 0, 0.25f, 0.75f),
                    Floats(0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f),
                },
                ["colors"] = Floats(1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 1, 1, 0, 1),
                ["submeshes"] = new[] { Ints(0, 1, 2), Ints(1, 2, 3) },
                ["boundsMin"] = new[] { -1f, -1f, 0f },
                ["boundsMax"] = new[] { 2f, 2f, 3f },
                ["readable"] = false,
            };
            return JsonSerializer.Serialize(answer);
        }

        static YNode Vec3Node(float x, float y, float z) => YNode.MakeMap(
            ("x", YNode.MakeScalar(x.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("y", YNode.MakeScalar(y.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ("z", YNode.MakeScalar(z.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        static YNode DeferredBody(string guid = "0123456789abcdef0123456789abcdef", int index = 0)
        {
            var sub0 = YNode.MakeMap(
                ("indexCount", YNode.MakeScalar("3")),
                ("firstVertex", YNode.MakeScalar("0")),
                ("vertexCount", YNode.MakeScalar("3")),
                ("boundsMin", Vec3Node(-1f, 0f, 0f)),
                ("boundsMax", Vec3Node(1f, 2f, 3f)));
            var sub1 = YNode.MakeMap(
                ("indexCount", YNode.MakeScalar("3")),
                ("firstVertex", YNode.MakeScalar("1")),
                ("vertexCount", YNode.MakeScalar("3")),
                ("boundsMin", Vec3Node(-1f, -1f, 0f)),
                ("boundsMax", Vec3Node(2f, 2f, 3f)));
            return YNode.MakeMap(
                ("m_Name", YNode.MakeScalar("DeferredProp")),
                ("m_UnboundModel", YNode.MakeScalar(guid)),
                ("m_UnboundMesh", YNode.MakeScalar(index.ToString())),
                ("m_UnboundDeferred", YNode.MakeScalar("1")),
                ("m_UnboundVertexCount", YNode.MakeScalar("4")),
                ("m_UnboundChannels", YNode.MakeScalar((1 | 2 | 4 | 8 | 16).ToString())),
                ("m_UnboundBoundsMin", Vec3Node(-1f, -1f, 0f)),
                ("m_UnboundBoundsMax", Vec3Node(2f, 2f, 3f)),
                ("m_UnboundSubmeshes", new YNode { Kind = YKind.Seq, Items = new[] { sub0, sub1 } }));
        }

        static Mesh EagerMesh(string json)
        {
            var mesh = new Mesh { name = "DeferredProp" };
            using var doc = JsonDocument.Parse(json);
            mesh.FillFromModel(doc.RootElement);
            return mesh;
        }

        static IDisposable WithHooks(Func<string, int, string> loadJson, Func<ulong, string, int, bool> upload)
        {
            var prevLoad = Mesh.LoadModelMeshJson;
            var prevUpload = Mesh.UploadDeferredModelMesh;
            Mesh.LoadModelMeshJson = loadJson;
            Mesh.UploadDeferredModelMesh = upload;
            return new Scope(() =>
            {
                Mesh.LoadModelMeshJson = prevLoad;
                Mesh.UploadDeferredModelMesh = prevUpload;
            });
        }

        sealed class Scope : IDisposable
        {
            readonly Action m_Dispose;
            public Scope(Action dispose) => m_Dispose = dispose;
            public void Dispose() => m_Dispose();
        }

        [Fact]
        public void DeferredMeshMatchesEagerMetadataAndSyncsWithoutAllocatingCpuArrays()
        {
            var json = SampleJson();
            var eager = EagerMesh(json);
            int jsonCalls = 0;
            var uploads = new List<(ulong handle, string guid, int index)>();
            using var hooks = WithHooks(
                (guid, idx) => { jsonCalls++; return json; },
                (handle, guid, idx) => { uploads.Add((handle, guid, idx)); return true; });

            var deferred = new Mesh { name = "DeferredProp" };
            deferred.ReadSerialized(DeferredBody(), null);

            Assert.Equal(0, jsonCalls);
            Assert.True(deferred.m_DeferredModel);
            Assert.Empty(deferred.m_Vertices);
            Assert.Null(deferred.m_Normals);
            Assert.Null(deferred.m_Tangents);
            Assert.Null(deferred.m_Colors);
            Assert.Null(deferred.m_UVs[0]);
            Assert.Null(deferred.m_UVs[1]);

            Assert.Equal(eager.isReadable, deferred.isReadable);
            Assert.Equal(eager.vertexCount, deferred.vertexCount);
            Assert.Equal(eager.bounds, deferred.bounds);
            Assert.Equal(eager.indexFormat, deferred.indexFormat);
            Assert.Equal(eager.subMeshCount, deferred.subMeshCount);
            Assert.Equal(eager.blendShapeCount, deferred.blendShapeCount);
            Assert.Equal(eager.vertexAttributeCount, deferred.vertexAttributeCount);
            Assert.Equal(eager.vertexBufferCount, deferred.vertexBufferCount);
            Assert.Equal(eager.GetVertexAttributes(), deferred.GetVertexAttributes());
            Assert.Equal(eager.GetVertexBufferStride(0), deferred.GetVertexBufferStride(0));
            for (int s = 0; s < eager.subMeshCount; s++)
            {
                Assert.Equal(eager.GetTopology(s), deferred.GetTopology(s));
                Assert.Equal(eager.GetIndexCount(s), deferred.GetIndexCount(s));
                Assert.Equal(eager.GetIndexStart(s), deferred.GetIndexStart(s));
                Assert.Equal(eager.GetBaseVertex(s), deferred.GetBaseVertex(s));
                Assert.Equal(eager.GetSubMesh(s), deferred.GetSubMesh(s));
                Assert.Equal(eager.UploadedIndexRange(s), deferred.UploadedIndexRange(s));
            }
            Assert.Equal(MemorySizes.RuntimeSize(eager), MemorySizes.RuntimeSize(deferred));

            // Sync uploads directly from native without touching CPU arrays.
            ulong handle = deferred.Sync();
            eager.Sync();
            Assert.NotEqual(0UL, handle);
            Assert.Single(uploads);
            Assert.Equal((handle, "0123456789abcdef0123456789abcdef", 0), uploads[0]);
            Assert.Equal(0, jsonCalls);
            Assert.True(deferred.m_DeferredModel);
            Assert.Empty(deferred.m_Vertices);
            Assert.False(deferred.m_Dirty);
            Assert.Equal(eager.m_SyncVersion, deferred.m_SyncVersion);
            Assert.Equal(MemorySizes.RuntimeSize(eager), MemorySizes.RuntimeSize(deferred));
            for (int s = 0; s < eager.subMeshCount; s++)
                Assert.Equal(eager.UploadedIndexRange(s), deferred.UploadedIndexRange(s));

            // Second Sync and UploadMeshData(true) remain no-ops for CPU arrays.
            Assert.Equal(handle, deferred.Sync());
            deferred.UploadMeshData(true);
            Assert.Single(uploads);
            Assert.Equal(0, jsonCalls);
            Assert.False(deferred.isReadable);
        }

        [Fact]
        public void PlayModeRefusedScriptReadsDoNotHydrateDeferredMesh()
        {
            var json = SampleJson();
            int jsonCalls = 0;
            using var hooks = WithHooks((g, i) => { jsonCalls++; return json; }, (h, g, i) => true);

            var deferred = new Mesh { name = "DeferredProp" };
            deferred.ReadSerialized(DeferredBody(), null);

            var logs = new List<string>();
            Application.LogCallback cb = (msg, _, type) => logs.Add(type + ": " + msg);
            Application.logMessageReceived += cb;
            try
            {
                Assert.Empty(deferred.vertices);
                Assert.Empty(deferred.normals);
                Assert.Empty(deferred.tangents);
                Assert.Empty(deferred.colors);
                Assert.Empty(deferred.colors32);
                Assert.Empty(deferred.uv);
                Assert.Empty(deferred.triangles);
                Assert.Empty(deferred.GetTriangles(0));
                Assert.Throws<InvalidOperationException>(() => Mesh.AcquireReadOnlyMeshData(deferred));
                Assert.Equal(0, jsonCalls);
                Assert.True(deferred.m_DeferredModel);
                Assert.Equal(8, logs.Count);
            }
            finally
            {
                Application.logMessageReceived -= cb;
            }
        }

        [Fact]
        public void RequiredCpuReadersHydrateOnDemandWithIdenticalDataAndNoSpuriousDirty()
        {
            var json = SampleJson();
            var eager = EagerMesh(json);
            int jsonCalls = 0;
            using var hooks = WithHooks((g, i) => { jsonCalls++; return json; }, (h, g, i) => true);

            // 1. GetTriangles(List<int>, submesh) reads indices even on !isReadable and hydrates transparently.
            var byList = new Mesh { name = "DeferredProp" };
            byList.ReadSerialized(DeferredBody(), null);
            byList.Sync();
            Assert.False(byList.m_Dirty);
            int syncBefore = byList.m_SyncVersion;
            var tris = new List<int>();
            byList.GetTriangles(tris, 1);
            Assert.Equal(new[] { 1, 2, 3 }, tris);
            Assert.Equal(1, jsonCalls);
            Assert.False(byList.m_DeferredModel);
            Assert.False(byList.isReadable);
            Assert.False(byList.m_Dirty);
            Assert.Equal(syncBefore, byList.m_SyncVersion);

            // 2. EngineReads() (Physics / NavMesh / Cloth) hydrates once and exposes identical geometry.
            var byEngine = new Mesh { name = "DeferredProp" };
            byEngine.ReadSerialized(DeferredBody(), null);
            using (Mesh.EngineReads())
            {
                Assert.Equal(eager.vertices, byEngine.vertices);
                Assert.Equal(eager.normals, byEngine.normals);
                Assert.Equal(eager.tangents, byEngine.tangents);
                Assert.Equal(eager.colors32, byEngine.colors32);
                Assert.Equal(eager.uv, byEngine.uv);
                Assert.Equal(eager.uv2, byEngine.uv2);
                Assert.Equal(eager.GetTriangles(0), byEngine.GetTriangles(0));
            }
            Assert.Equal(2, jsonCalls);
            Assert.False(byEngine.isReadable);

            // 3. CloneObject() produces an independent copy with full geometry.
            var src = new Mesh { name = "DeferredProp" };
            src.ReadSerialized(DeferredBody(), null);
            var clone = (Mesh)src.CloneObject();
            Assert.Equal(3, jsonCalls);
            Assert.False(clone.m_DeferredModel);
            Assert.False(clone.isReadable);
            using (Mesh.EngineReads())
                Assert.Equal(eager.vertices, clone.vertices);

            // 4. AllocateWritableMeshData hydrates and packs identical streams.
            var forData = new Mesh { name = "DeferredProp" };
            forData.ReadSerialized(DeferredBody(), null);
            using (var arr = Mesh.AllocateWritableMeshData(forData))
            {
                Assert.Equal(4, jsonCalls);
                Assert.Equal(4, arr[0].vertexCount);
                Assert.Equal(2, arr[0].subMeshCount);
            }
        }

        [Fact]
        public void RevertToAndClearPreserveDeferredReuploadAndLayoutSemantics()
        {
            var json = SampleJson();
            int jsonCalls = 0;
            var uploads = new List<(ulong handle, string guid, int index)>();
            using var hooks = WithHooks(
                (g, i) => { jsonCalls++; return json; },
                (h, g, i) => { uploads.Add((h, g, i)); return true; });

            var mesh = new Mesh { name = "DeferredProp" };
            mesh.ReadSerialized(DeferredBody("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0), null);
            ulong handle = mesh.Sync();
            // Materialize CPU arrays via EngineReads, then unload/reimport via RevertTo a new deferred doc.
            using (Mesh.EngineReads()) Assert.Equal(4, mesh.vertices.Length);
            Assert.Equal(1, jsonCalls);
            Assert.False(mesh.m_DeferredModel);

            var reimported = new Mesh { name = "DeferredProp" };
            reimported.ReadSerialized(DeferredBody("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 2), null);
            mesh.RevertTo(reimported);
            Assert.True(mesh.m_DeferredModel);
            Assert.Empty(mesh.m_Vertices);
            Assert.True(mesh.m_Dirty);
            Assert.Equal(handle, mesh.m_Handle);
            Assert.Equal(handle, mesh.Sync());
            Assert.Equal(2, uploads.Count);
            Assert.Equal((handle, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 2), uploads[1]);
            Assert.Equal(1, jsonCalls);

            // Clear(keepVertexLayout: true/false) works without loading from native.
            var keep = new Mesh { name = "DeferredProp" };
            keep.ReadSerialized(DeferredBody(), null);
            keep.Clear(true);
            Assert.Equal(1, jsonCalls);
            Assert.False(keep.m_DeferredModel);
            Assert.Equal(0, keep.vertexCount);
            Assert.True(keep.HasVertexAttribute(VertexAttribute.Normal));
            Assert.True(keep.HasVertexAttribute(VertexAttribute.TexCoord1));

            var drop = new Mesh { name = "DeferredProp" };
            drop.ReadSerialized(DeferredBody(), null);
            drop.Clear(false);
            Assert.Equal(1, jsonCalls);
            Assert.False(drop.HasVertexAttribute(VertexAttribute.Normal));
            Assert.False(drop.HasVertexAttribute(VertexAttribute.TexCoord0));
        }
    }
}
