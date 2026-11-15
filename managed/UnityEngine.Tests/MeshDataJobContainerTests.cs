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

        struct OneJob : IJob
        {
            public Mesh.MeshData mesh;
            public void Execute() { }
        }

        struct WriteOne : IJob
        {
            public Mesh.MeshData mesh;
            public void Execute()
            {
                var vertices = mesh.GetVertexData<Vector3>();
                vertices[0] = Vector3.one;
            }
        }

        [Fact]
        public void MeshDataArrayIsAContainerAndMeshDataHandlesAreDetached()
        {
            var array = JobLayout.For(typeof(ArrayJob));
            Assert.Null(array.Error);
            var container = Assert.Single(array.Containers);
            Assert.True(container.SupportsMinMax);
            Assert.Single(container.Handles);
            var one = JobLayout.For(typeof(OneJob));
            Assert.Null(one.Error);
            Assert.Empty(one.Containers);
            Assert.Single(one.Detached);
        }

        [Fact]
        public void TheArraysSafetyGovernsItsMeshesAndRangesAreRestricted()
        {
            var data = Mesh.AllocateWritableMeshData(2);
            var first = data[0];
            first.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position));
            // The views share the array's handle.
            Assert.Equal(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(first.GetVertexData<Vector3>()).versionNode,
                NativeArrayUnsafeUtility.GetAtomicSafetyHandle(data[1].GetVertexData<Vector3>()).versionNode);
            // A job holding a single MeshData reads but does not write, and is no dependency.
            var handle = new WriteOne { mesh = first }.Schedule();
            var vertices = first.GetVertexData<Vector3>();
            vertices[1] = Vector3.up;
            handle.Complete();
            Assert.Equal(Vector3.zero, first.GetVertexData<Vector3>()[0]);
            var copy = data;
            data.Dispose();
            var error = Assert.Throws<ObjectDisposedException>(() => copy[0]);
            Assert.StartsWith("The UNKNOWN_OBJECT_TYPE has been deallocated", error.Message);
        }
    }
}
