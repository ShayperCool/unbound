using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Unity.Collections;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Project release")]
    public class MeshGraphicsBufferTests
    {
        static Mesh Triangle() => new Mesh
        {
            name = "buffer test",
            vertices = new[] {Vector3.zero, Vector3.right, Vector3.up},
            triangles = new[] {0, 1, 2},
        };

        [Fact]
        public void DisposingAViewPreservesItsAliasesAndTheMeshOwner()
        {
            var mesh = Triangle();
            var first = mesh.GetVertexBuffer(0);
            using var second = mesh.GetVertexBuffer(0);
            try
            {
                Assert.NotSame(first, second);
                Assert.Equal(first.GetNativeBufferPtr(), second.GetNativeBufferPtr());
                first.Dispose(); first.Dispose();
                Assert.False(first.IsValid());
                Assert.Throws<NullReferenceException>(() => first.GetData(new byte[36]));
                Assert.Throws<NullReferenceException>(() => first.count);
                Assert.Throws<ArgumentNullException>(() => AsyncGPUReadback.RequestAsync(first));
                Assert.True(second.IsValid());
                using var third = mesh.GetVertexBuffer(0);
                Assert.Equal(second.GetNativeBufferPtr(), third.GetNativeBufferPtr());
                Object.DestroyImmediate(mesh);
                Assert.True(second.IsValid());
            }
            finally { first.Dispose(); Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void SameSizeEditsKeepStorageButLayoutAndSizeEditsInvalidateIt()
        {
            var mesh = Triangle();
            using var first = mesh.GetVertexBuffer(0);
            try
            {
                mesh.vertices = new[] {Vector3.one, Vector3.zero, Vector3.left};
                using var changed = mesh.GetVertexBuffer(0);
                Assert.True(first.IsValid());
                Assert.Equal(first.GetNativeBufferPtr(), changed.GetNativeBufferPtr());
                mesh.normals = new[] {Vector3.up, Vector3.up, Vector3.up};
                using var layout = mesh.GetVertexBuffer(0);
                Assert.False(first.IsValid());
                Assert.Equal(24, layout.stride);
                mesh.vertices = new Vector3[5];
                using var resized = mesh.GetVertexBuffer(0);
                Assert.False(layout.IsValid());
                Assert.Equal(5, resized.count);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void EmptyAndAbsentStreamsGiveTheMeasuredArgumentErrors()
        {
            var mesh = new Mesh {name = "empty"};
            try
            {
                Assert.Equal("Invalid buffer index -1 for Mesh 'empty'", Assert.Throws<ArgumentException>(() => mesh.GetVertexBuffer(-1)).Message);
                Assert.Equal("Invalid buffer index 4 for Mesh 'empty'", Assert.Throws<ArgumentException>(() => mesh.GetVertexBuffer(4)).Message);
                Assert.Equal("Mesh 'empty' vertex buffer #2 is null", Assert.Throws<ArgumentException>(() => mesh.GetVertexBuffer(2)).Message);
                Assert.Equal("Mesh 'empty' index buffer is null", Assert.Throws<ArgumentException>(() => mesh.GetIndexBuffer()).Message);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void OrdinaryImperfectGeometryStillProvidesBuffersAndCpuData()
        {
            var mesh = new Mesh
            {
                vertices = new[] {Vector3.zero, Vector3.zero, new Vector3(float.NaN, 0, 0), Vector3.one},
                triangles = new[] {0, 0, 1, 1, 2, 3},
            };
            try
            {
                using var vertices = mesh.GetVertexBuffer(0);
                using var indices = mesh.GetIndexBuffer();
                Assert.Equal(4, vertices.count);
                Assert.Equal(6, indices.count);
                Assert.True(float.IsNaN(mesh.vertices[2].x));
                Assert.Equal(new[] {0,0,1,1,2,3}, mesh.triangles);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void TargetsKeepRequiredBitsAndMeshWritesKeepCpuChannelsSeparate()
        {
            var mesh = Triangle();
            try
            {
                mesh.vertexBufferTarget = GraphicsBuffer.Target.Raw;
                mesh.indexBufferTarget = GraphicsBuffer.Target.Structured;
                Assert.Equal(GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Raw, mesh.vertexBufferTarget);
                Assert.Equal(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, mesh.indexBufferTarget);
                using var vertices = mesh.GetVertexBuffer(0);
                using var indices = mesh.GetIndexBuffer();
                Assert.Equal(mesh.vertexBufferTarget, vertices.target);
                Assert.Equal(mesh.indexBufferTarget, indices.target);
                Assert.Equal(3, vertices.count);
                var cpu = mesh.vertices;
                vertices.SetData(new Vector3[3]);
                Assert.Equal(cpu, mesh.vertices);
                var old = vertices.GetNativeBufferPtr();
                mesh.vertexBufferTarget = GraphicsBuffer.Target.Structured;
                using var changed = mesh.GetVertexBuffer(0);
                Assert.False(vertices.IsValid());
                Assert.NotEqual(old, changed.GetNativeBufferPtr());
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ResizingPositionsInvalidatesEveryStreamWithoutReplacingIndices()
        {
            var mesh = new Mesh();
            try
            {
                mesh.SetVertexBufferParams(3,
                    new VertexAttributeDescriptor(VertexAttribute.Position),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 1));
                mesh.SetIndices(new[] {0,1,2}, MeshTopology.Triangles, 0);
                using var colors = mesh.GetVertexBuffer(1);
                using var indices = mesh.GetIndexBuffer();
                var old = indices.GetNativeBufferPtr();
                mesh.vertices = new Vector3[4];
                using var resized = mesh.GetVertexBuffer(1);
                Assert.False(colors.IsValid());
                Assert.Equal(4, resized.count);
                using var current = mesh.GetIndexBuffer();
                Assert.Equal(old, current.GetNativeBufferPtr());
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void BufferAccessDoesNotChangeBakedSkinCoordinates()
        {
            var go = new GameObject(); var skin = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = Triangle(); var before = new Mesh(); var after = new Mesh();
            try
            {
                go.transform.position = new Vector3(.7f,2,3); go.transform.localScale = new Vector3(2,3,4);
                mesh.boneWeights = new[] {new BoneWeight {weight0=1},new BoneWeight {weight0=1},new BoneWeight {weight0=1}};
                mesh.bindposes = new[] {Matrix4x4.identity};
                skin.sharedMesh = mesh; skin.bones = new[] {go.transform};
                skin.BakeMesh(before);
                using var buffer = skin.GetVertexBuffer();
                Assert.Equal(12, buffer.stride);
                skin.BakeMesh(after);
                for (int i=0;i<3;i++) Assert.True((before.vertices[i]-after.vertices[i]).sqrMagnitude < 1e-10f);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }

        [Fact]
        public void TypedColorSettersPreserveFloatingValuesAndSelectTheirStorageFormats()
        {
            var mesh = Triangle();
            try
            {
                var color = new Color(2,.3f,.7f,1);
                mesh.colors = new[] {color,color,color};
                Assert.Equal(color,mesh.colors[0]);
                Assert.Equal(VertexAttributeFormat.Float32,mesh.GetVertexAttributeFormat(VertexAttribute.Color));
                using var floats = mesh.GetVertexBuffer(0);
                Assert.Equal(28,floats.stride);
                mesh.vertices = new[] {Vector3.one,Vector3.right,Vector3.up};
                Assert.Equal(color,mesh.colors[0]);
                mesh.colors32 = new[] {new Color32(10,20,30,40),new Color32(10,20,30,40),new Color32(10,20,30,40)};
                using var bytes = mesh.GetVertexBuffer(0);
                Assert.False(floats.IsValid());
                Assert.Equal(16,bytes.stride);
                Assert.Equal(VertexAttributeFormat.UNorm8,mesh.GetVertexAttributeFormat(VertexAttribute.Color));
                Assert.Equal((Color)new Color32(10,20,30,40),mesh.colors[0]);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void RepackingAnUnchangedShapeReusesNativeAndManagedStorage()
        {
            var mesh = Triangle();
            var data = Mesh.MeshDataStore.Of(mesh, true);
            try
            {
                data.Update(mesh);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < 100; frame++)
                {
                    mesh.m_Vertices[0] = new Vector3(frame, 0, 0);
                    data.Update(mesh);
                }
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally { data.Free(); Object.DestroyImmediate(mesh); }
        }

        sealed class Exceptions : ILogHandler
        {
            internal readonly List<Exception> Seen = new List<Exception>();
            readonly ILogHandler m_Previous;
            internal Exceptions(ILogHandler previous) => m_Previous = previous;
            public void LogException(Exception exception, Object context) { Seen.Add(exception); m_Previous.LogException(exception, context); }
            public void LogFormat(LogType type, Object context, string format, params object[] args) => m_Previous.LogFormat(type, context, format, args);
        }

        [Fact]
        public void RecordedCommandsKeepMeshStorageAfterTheTemporaryViewIsDisposed()
        {
            var mesh = Triangle();
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.CopySource;
            using var destination = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopyDestination, 3, 12);
            using var commands = new CommandBuffer();
            var array = new NativeArray<byte>(36, Allocator.Persistent);
            var sliceArray = new NativeArray<byte>(36, Allocator.Persistent);
            var slice = new NativeSlice<byte>(sliceArray);
            var previous = Debug.unityLogger.logHandler;
            var errors = new Exceptions(previous);
            try
            {
                var view = mesh.GetVertexBuffer(0);
                commands.CopyBuffer(view, destination);
                commands.RequestAsyncReadback(view, _ => {});
                commands.RequestAsyncReadbackIntoNativeArray(ref array, view, _ => {});
                commands.RequestAsyncReadbackIntoNativeSlice(ref slice, view, _ => {});
                view.Dispose();
                Debug.unityLogger.logHandler = errors;
                Graphics.ExecuteCommandBuffer(commands);
                AsyncGPUReadback.WaitAllRequests();
                Assert.Empty(errors.Seen);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                array.Dispose();
                sliceArray.Dispose();
                Object.DestroyImmediate(mesh);
            }
        }

        static Mesh PreciseMesh(out float[] colors, out uint[] integers)
        {
            var mesh = new Mesh();
            mesh.SetVertexBufferParams(2,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 1),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UInt32, 4, 2));
            colors = new[] {-1f, .5f, 2f, 4f, .3f, .7f, 8f, -2f};
            integers = new uint[] {0xffffffff, 16777217, 0x80000001, 17, 31, 0x7fffffff, 0xabcdef01, 99};
            mesh.SetVertexBufferData(colors, 0, 0, colors.Length, 1);
            mesh.SetVertexBufferData(integers, 0, 0, integers.Length, 2);
            return mesh;
        }

        static byte[] Stream(Mesh mesh, int stream)
        {
            using var data = Mesh.AcquireReadOnlyMeshData(mesh);
            return data[0].GetVertexData<byte>(stream).ToArray();
        }

        [Fact]
        public void PackedColorsAndUnsignedAttributesRetainTheirExactBytes()
        {
            var mesh = PreciseMesh(out var colors, out var integers);
            try
            {
                var colorBytes = MemoryMarshal.AsBytes(colors.AsSpan()).ToArray();
                var integerBytes = MemoryMarshal.AsBytes(integers.AsSpan()).ToArray();
                Assert.Equal(colorBytes, Stream(mesh, 1));
                Assert.Equal(integerBytes, Stream(mesh, 2));
                mesh.uv = new Vector2[1]; // Rejected input must not discard unrelated packed data.
                mesh.SetIndices(new[] {0,1}, MeshTopology.Points, 0);
                mesh.vertices = new[] {Vector3.right, Vector3.up};
                mesh.normals = new[] {Vector3.forward, Vector3.forward};
                Assert.Equal(colorBytes, Stream(mesh, 1));
                Assert.Equal(integerBytes, Stream(mesh, 2));
                var clone = Object.Instantiate(mesh);
                try { Assert.Equal(colorBytes, Stream(clone, 1)); }
                finally { Object.DestroyImmediate(clone); }
                mesh.vertices = new Vector3[1];
                mesh.vertices = new Vector3[2];
                Array.Clear(colorBytes, 16, 16);
                Assert.Equal(colorBytes, Stream(mesh, 1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void PartialRawWritesPreserveEveryOtherByte()
        {
            var mesh = PreciseMesh(out var colors, out _);
            try
            {
                var expected = MemoryMarshal.AsBytes(colors.AsSpan()).ToArray();
                expected[5] = 0xab; expected[6] = 0xcd;
                mesh.SetVertexBufferData(new byte[] {0xab,0xcd}, 0, 5, 2, 1);
                Assert.Equal(expected, Stream(mesh, 1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void RepeatedRawWritesReuseExactChannelStorageWithoutAllocating()
        {
            var mesh = PreciseMesh(out var colors, out _);
            try
            {
                mesh.SetVertexBufferData(colors, 0, 0, colors.Length, 1);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < 100; frame++) mesh.SetVertexBufferData(colors, 0, 0, colors.Length, 1);
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void PackedMultistreamLayoutAndLargeMeshesRetainCountsAndStrides()
        {
            var mesh = new Mesh();
            try
            {
                mesh.SetVertexBufferParams(100_001,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2, 1));
                using var positions = mesh.GetVertexBuffer(0);
                using var uvs = mesh.GetVertexBuffer(1);
                Assert.Equal(100_001, positions.count);
                Assert.Equal(12, positions.stride);
                Assert.Equal(100_001, uvs.count);
                Assert.Equal(4, uvs.stride);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
