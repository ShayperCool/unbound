using Xunit;

namespace UnityEngine.Tests
{
    public class SkinnedMeshTests
    {
        [Fact]
        public void VerticesFollowTheirBones()
        {
            var root = new GameObject("root");
            var boneA = new GameObject("a").transform;
            var boneB = new GameObject("b").transform;
            boneA.SetParent(root.transform, false);
            boneB.SetParent(root.transform, false);
            boneB.localPosition = new Vector3(0f, 1f, 0f);

            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0.5f, 0f) },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
                },
                // Mesh space to each bone's space at bind time.
                bindposes = new[] { boneA.worldToLocalMatrix, boneB.worldToLocalMatrix },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { boneA, boneB };

            // Bone b moves 2 to the right: its vertex follows, the shared one half as much.
            boneB.localPosition += new Vector3(2f, 0f, 0f);
            Time.s_FrameCount++;
            var baked = new Mesh();
            renderer.BakeMesh(baked);
            var v = baked.vertices;
            Assert.Equal(new Vector3(0f, 0f, 0f), v[0]);
            Assert.Equal(new Vector3(2f, 1f, 0f), v[1]);
            Assert.Equal(new Vector3(1f, 0.5f, 0f), v[2]);

            // One bone per vertex: the shared vertex takes its heaviest (first) bone only.
            renderer.quality = SkinQuality.Bone1;
            Time.s_FrameCount++;
            renderer.BakeMesh(baked);
            Assert.Equal(new Vector3(0f, 0.5f, 0f), baked.vertices[2]);
            Object.DestroyImmediate(root);
        }
    }
}
