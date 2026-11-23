using Unity.Collections;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class MeshDataSkinWeightTests
    {
        [Fact]
        public void UnmeasuredReleaseReadsReplacementWeights()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var source = new Mesh
            {
                vertices = new[] { Vector3.right },
                boneWeights = new[] { new BoneWeight { boneIndex0 = 7, weight0 = 1 } }
            };
            var target = new Mesh
            {
                vertices = new[] { Vector3.zero },
                boneWeights = new[] { new BoneWeight { boneIndex0 = 3, weight0 = 1 } }
            };
            try
            {
                Assert.Equal(3, target.GetAllBoneWeights()[0].boneIndex);
                Mesh.ApplyAndDisposeWritableMeshData(Mesh.AllocateWritableMeshData(source), target);
                Assert.Equal(7, target.GetAllBoneWeights()[0].boneIndex);
                Assert.Equal((byte)1, target.GetBonesPerVertex()[0]);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        [Fact]
        public void ResizedPackedSnapshotCanStillReplaceDestinationMeshes()
        {
            var source = new Mesh { vertices = new[] { Vector3.right } };
            var first = new Mesh();
            var second = new Mesh();
            try
            {
                using var counts = new NativeArray<byte>(new byte[] { 5 }, Allocator.Temp);
                using var weights = new NativeArray<BoneWeight1>(new[]
                {
                    new BoneWeight1 { boneIndex = 0, weight = .5f },
                    new BoneWeight1 { boneIndex = 1, weight = .25f },
                    new BoneWeight1 { boneIndex = 2, weight = .125f },
                    new BoneWeight1 { boneIndex = 3, weight = .0625f },
                    new BoneWeight1 { boneIndex = 4, weight = .0625f },
                }, Allocator.Temp);
                source.SetBoneWeights(counts, weights);
                var data = Mesh.AllocateWritableMeshData(new[] { source, source });
                try
                {
                    data[1].SetVertexBufferParams(2, new VertexAttributeDescriptor(VertexAttribute.Position));
                    Mesh.ApplyAndDisposeWritableMeshData(data, new[] { first, second });
                }
                catch
                {
                    data.Dispose();
                    throw;
                }
                Assert.Equal(1, first.vertexCount);
                Assert.Equal(2, second.vertexCount);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
