using System;
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
        public void TargetsKeepRequiredBitsAndMeshWritesAreExplicitlyUnsupported()
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
                Assert.Throws<NotSupportedException>(() => vertices.SetData(new Vector3[3]));
                Assert.Throws<NotSupportedException>(() => Graphics.CopyBuffer(vertices, vertices));
                var old = vertices.GetNativeBufferPtr();
                mesh.vertexBufferTarget = GraphicsBuffer.Target.Structured;
                using var changed = mesh.GetVertexBuffer(0);
                Assert.False(vertices.IsValid());
                Assert.NotEqual(old, changed.GetNativeBufferPtr());
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
