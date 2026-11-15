using System;
using Unbound.Runtime.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // tests/projects/meshdatajobs: two identical Unity 6000.0.71f1 captures.
    public class MeshDataJobContainerTests
    {
        struct ArrayJob : IJob
        {
            public Mesh.MeshDataArray data;
            public void Execute() { }
        }

        struct WriteFirst : IJob
        {
            public Mesh.MeshDataArray data;
            public void Execute()
            {
                var vertices = data[0].GetVertexData<Vector3>();
                vertices[0] = Vector3.one;
            }
        }

        [Fact]
        public void MeshDataArrayIsARangeRestrictedContainer()
        {
            var layout = JobLayout.For(typeof(ArrayJob));
            Assert.Null(layout.Error);
            var container = Assert.Single(layout.Containers);
            Assert.True(container.SupportsMinMax);
            Assert.Single(container.Handles);
            var data = Mesh.AllocateWritableMeshData(3);
            data.m_MinIndex = data.m_MaxIndex = 1;
            var restricted = Assert.Throws<IndexOutOfRangeException>(() => data[-1]);
            Assert.Equal("Index -1 is out of restricted IJobParallelFor range [1...1] in MeshDataArray.\n" +
                "You can only access the element at the job index, unless [NativeDisableParallelForRestriction] is used on the variable.", restricted.Message);
            Assert.Equal("Index 3 is out of range of '3' Length.", Assert.Throws<IndexOutOfRangeException>(() => data[3]).Message);
            data.m_MinIndex = 0;
            data.m_MaxIndex = 2;
            var copy = data;
            data.Dispose();
            // The range is checked before the handle.
            Assert.Throws<IndexOutOfRangeException>(() => copy[5]);
            Assert.StartsWith("The UNKNOWN_OBJECT_TYPE has been deallocated", Assert.Throws<ObjectDisposedException>(() => copy[0]).Message);
            Assert.StartsWith("The UNKNOWN_OBJECT_TYPE has been deallocated", Assert.Throws<ObjectDisposedException>(() => copy.Dispose()).Message);
            Assert.StartsWith("The UNKNOWN_OBJECT_TYPE has been deallocated", Assert.Throws<ObjectDisposedException>(() => default(Mesh.MeshDataArray).Dispose()).Message);
        }

        [Fact]
        public void TheArraysSafetyGovernsItsMeshesAndAcquiredDataIsReadOnly()
        {
            var data = Mesh.AllocateWritableMeshData(2);
            var first = data[0];
            first.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position));
            Assert.Equal(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(first.GetVertexData<Vector3>()).versionNode,
                NativeArrayUnsafeUtility.GetAtomicSafetyHandle(data[1].GetVertexData<Vector3>()).versionNode);
            data.Dispose();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            var acquired = Mesh.AcquireReadOnlyMeshData(mesh);
            try
            {
                Assert.Equal(3, acquired[0].vertexCount);
                var vertices = acquired[0].GetVertexData<Vector3>();
                Assert.Equal("The UNKNOWN_OBJECT_TYPE has been declared as [ReadOnly], but you are writing to it.",
                    Assert.Throws<InvalidOperationException>(() => vertices[0] = Vector3.one).Message);
                var error = Assert.Throws<InvalidOperationException>(() => new WriteFirst { data = acquired }.Schedule().Complete());
                Assert.Equal("The UNKNOWN_OBJECT_TYPE WriteFirst.data must be marked [ReadOnly] in the job MeshDataJobContainerTests:WriteFirst, because the container itself is marked read only.", error.Message);
            }
            finally
            {
                acquired.Dispose();
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
