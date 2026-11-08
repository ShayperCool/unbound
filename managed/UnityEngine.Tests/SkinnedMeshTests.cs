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

        /// <summary>The mesh a skinned renderer draws keeps its arrays from frame to frame (a
        /// frame's skinning allocates none) and takes each frame's pose, normals and tangents
        /// included; the shared mesh keeps its own.</summary>
        [Fact]
        public void TheDrawnMeshFollowsEachFramesPoseInTheSameArrays()
        {
            var root = new GameObject("root");
            var bone = new GameObject("bone").transform;
            bone.SetParent(root.transform, false);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, -1f) },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                },
                bindposes = new[] { bone.worldToLocalMatrix },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            try
            {
                Time.s_FrameCount++;
                var drawn = renderer.DrawGeometry(out _);
                var (positions, normals, tangents) = (drawn.m_Vertices, drawn.m_Normals, drawn.m_Tangents);
                Assert.Equal(new Vector3(1f, 0f, 0f), positions[1]);

                // A quarter turn about z and a move: positions, normals and tangents follow.
                bone.localPosition = new Vector3(0f, 0f, 2f);
                bone.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Time.s_FrameCount++;
                Assert.Same(drawn, renderer.DrawGeometry(out _));
                Assert.Same(positions, drawn.m_Vertices);
                Assert.Same(normals, drawn.m_Normals);
                Assert.Same(tangents, drawn.m_Tangents);
                Assert.True((drawn.m_Vertices[1] - new Vector3(0f, 1f, 2f)).sqrMagnitude < 1e-10f);
                Assert.True((drawn.m_Tangents[2] - new Vector4(0f, 1f, 0f, -1f)).sqrMagnitude < 1e-10f);
                Assert.Equal(Vector3.forward, drawn.m_Normals[0]);
                Assert.True((drawn.bounds.center - new Vector3(-0.5f, 0.5f, 2f)).sqrMagnitude < 1e-10f);
                // The shared mesh is untouched.
                Assert.Equal(new Vector3(1f, 0f, 0f), mesh.vertices[1]);
                Assert.Equal(new Vector4(1f, 0f, 0f, -1f), mesh.tangents[2]);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
