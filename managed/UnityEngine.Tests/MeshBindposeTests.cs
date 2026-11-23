using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MeshBindposeTests
    {
        struct CopyPose : IJob
        {
            [ReadOnly] public NativeArray<Matrix4x4> Source;
            public NativeArray<Matrix4x4> Destination;

            public void Execute() => Destination[0] = Source[0];
        }

        [Fact]
        public void ResizedOrDestroyedStorageInvalidatesItsBorrowedViews()
        {
            var mesh = new Mesh { bindposes = new[] { Matrix4x4.identity } };
            try
            {
                var onePose = mesh.GetBindposes();
                Assert.True(onePose.IsCreated);
                mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) };
                Assert.Throws<ObjectDisposedException>(() => onePose[0]);

                var twoPoses = mesh.GetBindposes();
                Assert.Equal(Matrix4x4.Translate(Vector3.right), twoPoses[1]);
                Object.DestroyImmediate(mesh);
                Assert.Throws<ObjectDisposedException>(() => twoPoses[1]);
                Assert.Throws<MissingReferenceException>(() => mesh.GetBindposes());
            }
            finally { if (mesh != null) Object.DestroyImmediate(mesh); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static NativeArray<Matrix4x4> BorrowWithoutRetainingMesh(out WeakReference<Mesh> owner)
        {
            var mesh = new Mesh { bindposes = new[] { Matrix4x4.identity } };
            owner = new WeakReference<Mesh>(mesh);
            return mesh.GetBindposes();
        }

        [Fact]
        public void CollectedMeshReleasesItsBorrowedStorage()
        {
            var view = BorrowWithoutRetainingMesh(out var owner);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            MainThreadContext.Instance.RunPending();
            Assert.False(owner.TryGetTarget(out _));
            Assert.Throws<ObjectDisposedException>(() => view[0]);
        }

        [Fact]
        public void DisposedBorrowCannotSupplyPosesAndLiveBorrowCannotReceiveReadback()
        {
            var mesh = new Mesh { bindposes = new[] { Matrix4x4.identity } };
            try
            {
                var view = mesh.GetBindposes();
                var slice = new NativeSlice<Matrix4x4>(view);
                var safety = NativeSliceUnsafeUtility.GetAtomicSafetyHandle(slice);
                Assert.Throws<InvalidOperationException>(() => Safety.ReserveExternalWrite(safety));
                view.Dispose();
                Assert.Throws<ArgumentException>("poses", () => mesh.SetBindposes(view));
                Assert.Equal(Matrix4x4.identity, mesh.GetBindposes()[0]);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void EmptyBindposeViewRetainsReadOnlySafety()
        {
            var mesh = new Mesh();
            try
            {
                var view = mesh.GetBindposes();
                Assert.False(view.IsCreated);
                Assert.Empty(view.ToArray());
                Assert.True(view.AsReadOnlySpan().IsEmpty);
                Assert.Throws<IndexOutOfRangeException>(() => view[0]);
                Assert.Throws<ArgumentNullException>("bindposes", () => mesh.GetBindposes(null));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void SameSizeBindposeAssignmentWithAnIdleViewDoesNotAllocate()
        {
            var mesh = new Mesh { bindposes = new[] { Matrix4x4.identity } };
            using var poses = new NativeArray<Matrix4x4>(new[] { Matrix4x4.Translate(Vector3.right) }, Allocator.TempJob);
            try
            {
                var view = mesh.GetBindposes();
                mesh.SetBindposes(poses);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 32; i++) mesh.SetBindposes(poses);
                Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
                Assert.Equal(poses[0], view[0]);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ReplacingAndDestroyingStorageCompleteAttachedReaders()
        {
            var mesh = new Mesh { bindposes = new[] { Matrix4x4.identity } };
            using var result = new NativeArray<Matrix4x4>(1, Allocator.TempJob);
            try
            {
                var updated = Matrix4x4.Translate(Vector3.right);
                new CopyPose { Source = mesh.GetBindposes(), Destination = result }.Schedule();
                mesh.bindposes = new[] { updated };
                Assert.Equal(Matrix4x4.identity, result[0]);

                new CopyPose { Source = mesh.GetBindposes(), Destination = result }.Schedule();
                Object.DestroyImmediate(mesh);
                Assert.Equal(updated, result[0]);
            }
            finally { if (mesh != null) Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void GetBindposesListReusesWarmCapacityWithoutAllocation()
        {
            var mesh = new Mesh { bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) } };
            try
            {
                var poses = new List<Matrix4x4>(8);
                mesh.GetBindposes(poses);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 32; i++) mesh.GetBindposes(poses);
                Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
                Assert.Equal(mesh.bindposes, poses);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
