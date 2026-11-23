using System;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using Unity.Collections;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class MeshBoneWeightBufferTests
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static GraphicsBuffer BufferWithoutRetainingMesh(out WeakReference<Mesh> owner)
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            using var counts = new NativeArray<byte>(new byte[] { 1 }, Allocator.Temp);
            using var weights = new NativeArray<BoneWeight1>(new[] { new BoneWeight1 { boneIndex = 3, weight = 1f } }, Allocator.Temp);
            mesh.SetBoneWeights(counts, weights);
            owner = new WeakReference<Mesh>(mesh);
            return mesh.GetBoneWeightBuffer(SkinWeights.OneBone);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CollectedMeshReleasesStorageWhenItsLastViewIsDisposed(bool disposeFirst)
        {
            using var view = BufferWithoutRetainingMesh(out var owner);
            ulong handle = view.Handle;
            if (disposeFirst) view.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            MainThreadContext.Instance.RunPending();
            Assert.False(owner.TryGetTarget(out _));

            if (!disposeFirst)
            {
                Assert.True(view.IsValid());
                Assert.NotNull(GpuBufferStorage.Find(handle));
                view.Dispose();
            }
            Assert.Null(GpuBufferStorage.Find(handle));
        }
    }
}
