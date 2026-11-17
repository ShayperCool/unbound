using System;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;
using static UnityEngine.Tests.VisualEffectSimulationTests;

namespace UnityEngine.Tests
{
    /// <summary>What a visual effect's expressions read of meshes, textures and buffers.</summary>
    public class VisualEffectMeshTests
    {
        const uint None = VFXMeshFunctions.None;

        /// <summary>Five vertices in two streams: positions, normals (floats) and tangents
        /// (signed 16-bit norms); colours (bytes), first texture coordinates (halves) and second
        /// ones (unsigned 16-bit norms). Three triangles of 16-bit indices.</summary>
        static Mesh Mixed()
        {
            var mesh = new Mesh();
            mesh.SetVertexBufferParams(5,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm16, 4, 0),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 1),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2, 1),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm16, 2, 1));
            var first = new byte[5 * 32];
            var second = new byte[5 * 12];
            for (int v = 0; v < 5; v++)
            {
                BitConverter.TryWriteBytes(first.AsSpan(v * 32), (float)v);
                BitConverter.TryWriteBytes(first.AsSpan(v * 32 + 4), -v * 0.5f);
                BitConverter.TryWriteBytes(first.AsSpan(v * 32 + 24), (short)(v == 2 ? -32768 : 16384));
                second[v * 12] = (byte)(v * 50);
                second[v * 12 + 3] = 255;
                BitConverter.TryWriteBytes(second.AsSpan(v * 12 + 4), (Half)(v + 0.5f));
                BitConverter.TryWriteBytes(second.AsSpan(v * 12 + 8), (ushort)(v * 1000));
            }
            mesh.SetVertexBufferData(first, 0, 0, first.Length, 0);
            mesh.SetVertexBufferData(second, 0, 0, second.Length, 1);
            mesh.SetIndexBufferParams(9, IndexFormat.UInt16);
            mesh.SetIndexBufferData(new ushort[] { 0, 1, 2, 2, 1, 3, 3, 1, 4 }, 0, 0, 9);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 9));
            return mesh;
        }

        [Fact]
        public void AChannelsLayoutIsPackedIntoWordsOfFourBytes()
        {
            var mesh = Mixed();
            try
            {
                Assert.Equal(5u, VFXMeshFunctions.VertexCount(mesh));
                Assert.Equal(9u, VFXMeshFunctions.IndexCount(mesh));
                Assert.Equal(0u, VFXMeshFunctions.IndexFormat(mesh));
                Assert.Equal(1u << 16 | 4u << 8 | (uint)VertexAttributeFormat.UNorm8, VFXMeshFunctions.ChannelInfos(mesh, (uint)VertexAttribute.Color));
                Assert.Equal((uint)VertexAttributeFormat.SNorm16 | 4u << 8, VFXMeshFunctions.ChannelInfos(mesh, (uint)VertexAttribute.Tangent));
                Assert.Equal(6u, VFXMeshFunctions.ChannelOffset(mesh, (uint)VertexAttribute.Tangent));
                Assert.Equal(2u, VFXMeshFunctions.ChannelOffset(mesh, (uint)VertexAttribute.TexCoord1));
                Assert.Equal(8u, VFXMeshFunctions.VertexStride(mesh, (uint)VertexAttribute.Position));
                Assert.Equal(3u, VFXMeshFunctions.VertexStride(mesh, (uint)VertexAttribute.Color));
                // A channel the mesh does not have, one past the last, and no mesh.
                Assert.Equal(None, VFXMeshFunctions.ChannelInfos(mesh, (uint)VertexAttribute.TexCoord2));
                Assert.Equal(None, VFXMeshFunctions.VertexStride(mesh, 14));
                Assert.Equal(None, VFXMeshFunctions.ChannelOffset(null, 0));
                Assert.Equal(0u, VFXMeshFunctions.VertexCount(null));
                Assert.Equal(0u, VFXMeshFunctions.IndexCount(null));
                Assert.Equal(None, VFXMeshFunctions.IndexFormat(null));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void VerticesAreDecodedFromTheirFormats()
        {
            var mesh = Mixed();
            try
            {
                Span<float> sampled = stackalloc float[4];
                uint Offset(VertexAttribute channel, int vertex) =>
                    (uint)vertex * VFXMeshFunctions.VertexStride(mesh, (uint)channel) + VFXMeshFunctions.ChannelOffset(mesh, (uint)channel);
                uint Infos(VertexAttribute channel) => VFXMeshFunctions.ChannelInfos(mesh, (uint)channel);
                VFXMeshFunctions.Sample(mesh, Offset(VertexAttribute.Position, 3), Infos(VertexAttribute.Position), sampled.Slice(0, 3));
                Assert.Equal(new[] { 3f, -1.5f, 0f }, sampled.Slice(0, 3).ToArray());
                // Fewer components read than the channel has: the first ones.
                VFXMeshFunctions.Sample(mesh, Offset(VertexAttribute.Position, 3), Infos(VertexAttribute.Position), sampled.Slice(0, 1));
                Assert.Equal(3f, sampled[0]);
                // More than it has: the rest zero (not one).
                VFXMeshFunctions.Sample(mesh, Offset(VertexAttribute.TexCoord0, 2), Infos(VertexAttribute.TexCoord0), sampled);
                Assert.Equal(new[] { 2.5f, 0f, 0f, 0f }, sampled.ToArray());
                VFXMeshFunctions.Sample(mesh, Offset(VertexAttribute.Tangent, 2), Infos(VertexAttribute.Tangent), sampled);
                Assert.Equal(-1f, sampled[0]);
                VFXMeshFunctions.Sample(mesh, Offset(VertexAttribute.TexCoord1, 4), Infos(VertexAttribute.TexCoord1), sampled.Slice(0, 2));
                Assert.Equal(4000f / 65535f, sampled[0]);
                // A colour's red, green and blue from sRGB to linear; its alpha as it is.
                VFXMeshFunctions.SampleColor(mesh, Offset(VertexAttribute.Color, 3), Infos(VertexAttribute.Color), sampled);
                Assert.Equal(Mathf.GammaToLinearSpace(150f / 255f), sampled[0]);
                Assert.Equal(1f, sampled[3]);
                // Past the last vertex, and without a channel: zeros.
                VFXMeshFunctions.Sample(mesh, Offset(VertexAttribute.Position, 5), Infos(VertexAttribute.Position), sampled);
                Assert.Equal(new float[4], sampled.ToArray());
                VFXMeshFunctions.Sample(mesh, 0, None, sampled);
                Assert.Equal(new float[4], sampled.ToArray());
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void IndicesAreReadInTheFormatAsked()
        {
            var narrow = Mixed();
            var wide = new Mesh();
            try
            {
                wide.SetVertexBufferParams(4, new VertexAttributeDescriptor(VertexAttribute.Position));
                wide.SetIndexBufferParams(3, IndexFormat.UInt32);
                wide.SetIndexBufferData(new uint[] { 3, 0x10002, 1 }, 0, 0, 3, MeshUpdateFlags.DontValidateIndices);
                Assert.Equal(3u, VFXMeshFunctions.Index(narrow, 5, 0));
                Assert.Equal(1u, VFXMeshFunctions.IndexFormat(wide));
                Assert.Equal(0x10002u, VFXMeshFunctions.Index(wide, 1, 1));
                // The buffer's bytes read as the other format.
                Assert.Equal(2u | 2u << 16, VFXMeshFunctions.Index(narrow, 1, 1));
                Assert.Equal(2u, VFXMeshFunctions.Index(wide, 2, 0));
                Assert.Equal(1u, VFXMeshFunctions.Index(wide, 3, 0));
                Assert.Equal(None, VFXMeshFunctions.Index(null, 0, 0));
            }
            finally
            {
                Object.DestroyImmediate(narrow);
                Object.DestroyImmediate(wide);
            }
        }

        [Fact]
        public void AMeshWhoseDataWasGivenUpHasCountsButNoLayoutNorVertices()
        {
            var mesh = Mixed();
            try
            {
                mesh.UploadMeshData(true);
                Span<float> sampled = stackalloc float[4];
                Assert.Equal(5u, VFXMeshFunctions.VertexCount(mesh));
                Assert.Equal(None, VFXMeshFunctions.IndexFormat(mesh));
                Assert.Equal(None, VFXMeshFunctions.ChannelInfos(mesh, (uint)VertexAttribute.Position));
                VFXMeshFunctions.Sample(mesh, 0, (uint)VertexAttributeFormat.Float32 | 3u << 8, sampled);
                Assert.Equal(new float[4], sampled.ToArray());
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void TheProgramsReadAMeshThroughViewsOfItsBuffersThatAreKept()
        {
            var mesh = Mixed();
            try
            {
                var infos = VFXMeshFunctions.ChannelInfos(mesh, (uint)VertexAttribute.Color);
                var view = VFXMeshFunctions.VertexBuffer(mesh, infos, null);
                Assert.Equal(12, view.stride);
                Assert.Equal(GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured, mesh.vertexBufferTarget);
                Assert.Same(view, VFXMeshFunctions.VertexBuffer(mesh, infos, view));
                var indices = VFXMeshFunctions.IndexBuffer(mesh, null);
                Assert.Equal(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, mesh.indexBufferTarget);
                Assert.Same(indices, VFXMeshFunctions.IndexBuffer(mesh, indices));
                // Another layout is another buffer.
                mesh.SetVertexBufferParams(5, new VertexAttributeDescriptor(VertexAttribute.Position), new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 1));
                Assert.NotSame(view, VFXMeshFunctions.VertexBuffer(mesh, infos, view));
                Assert.Null(VFXMeshFunctions.VertexBuffer(null, infos, null));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ATexturesDepthAndABuffersStride()
        {
            var volume = new Texture3D(4, 2, 3, TextureFormat.RGBA32, false);
            var cubes = new CubemapArray(4, 2, TextureFormat.RGBA32, false);
            var flat = new Texture2D(2, 2);
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 12);
            try
            {
                var data = Data(new[]
                {
                    Value(VFXValueType.Texture3D), Op(VFXExpressionOperation.TextureDepth, 0),
                    Value(VFXValueType.TextureCubeArray), Op(VFXExpressionOperation.TextureDepth, 2),
                    Value(VFXValueType.Texture2D), Op(VFXExpressionOperation.TextureDepth, 4),
                    Value(VFXValueType.Buffer), Op(VFXExpressionOperation.BufferStride, 6),
                }, Array.Empty<VFXExpressionValueContainerDesc>());
                var values = new VisualEffectValues(data);
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
                Assert.Equal(new[] { 0u, 0u, 0u, 0u }, new[] { values.GetWord(1), values.GetWord(3), values.GetWord(5), values.GetWord(7) });
                values.SetObject(0, volume);
                values.SetObject(2, cubes);
                values.SetObject(4, flat);
                values.SetObject(6, buffer);
                // A buffer released while it is set keeps its stride.
                buffer.Release();
                VFXExpressionEvaluator.Evaluate(data, values, 0, data.Operations.Length, new VisualEffectState(), null);
                Assert.Equal(new[] { 3u, 12u, 1u, 12u }, new[] { values.GetWord(1), values.GetWord(3), values.GetWord(5), values.GetWord(7) });
            }
            finally
            {
                Object.DestroyImmediate(volume);
                Object.DestroyImmediate(cubes);
                Object.DestroyImmediate(flat);
            }
        }
    }
}
