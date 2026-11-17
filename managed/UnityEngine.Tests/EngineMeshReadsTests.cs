using System.Collections.Generic;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The engine's own systems read meshes that scripts cannot (Mesh.EngineReads): a
    /// mesh made no longer readable builds the same navigation mesh, skins and combines the same,
    /// and logs nothing (the Unity 6000.0.71f1 Editor's MeshCollider of such a mesh collides,
    /// tests/projects/meshreadable `*.colliderHit`; Unbound's other engine readers follow it;
    /// CombineMeshes and BakeMesh of such meshes are unmeasured).</summary>
    public class EngineMeshReadsTests
    {
        static Mesh Floor(bool readable)
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-5f, 0f, -5f), new Vector3(-5f, 0f, 5f), new Vector3(5f, 0f, 5f), new Vector3(5f, 0f, -5f) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.RecalculateNormals();
            if (!readable) mesh.UploadMeshData(true);
            return mesh;
        }

        static List<string> Logs(System.Action action)
        {
            var logs = new List<string>();
            Application.LogCallback log = (condition, _, type) => logs.Add(type + ": " + condition);
            Application.logMessageReceived += log;
            try { action(); }
            finally { Application.logMessageReceived -= log; }
            return logs;
        }

        [Fact]
        public void NavigationMeshesBuildFromMeshesScriptsCannotRead()
        {
            NavMeshData Build(Mesh mesh) => NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = Matrix4x4.identity, area = 0 } },
                new Bounds(Vector3.zero, new Vector3(12f, 4f, 12f)), Vector3.zero, Quaternion.identity);
            var readable = Build(Floor(true));
            NavMeshData locked = null;
            Assert.Empty(Logs(() => locked = Build(Floor(false))));
            Assert.NotEmpty(readable.m_Tiles);
            Assert.Equal(readable.m_Tiles.Count, locked.m_Tiles.Count);
            Assert.Equal(readable.sourceBounds, locked.sourceBounds);
        }

        [Fact]
        public void CombiningAndSkinningReadMeshesScriptsCannotRead()
        {
            var combined = new Mesh();
            var source = Floor(false);
            Assert.Empty(Logs(() => combined.CombineMeshes(new[] { new CombineInstance { mesh = source, transform = Matrix4x4.Translate(Vector3.up) } })));
            Assert.Equal(4, combined.vertexCount);
            Assert.Equal(6u, combined.GetIndexCount(0));
            Assert.Equal(new Vector3(-5f, 1f, -5f), combined.vertices[0]);

            var go = new GameObject("locked skin");
            var baked = new Mesh();
            try
            {
                var skinned = Floor(true);
                skinned.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 } };
                skinned.bindposes = new[] { Matrix4x4.identity };
                skinned.UploadMeshData(true);
                var skin = go.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = skinned;
                skin.bones = new[] { go.transform };
                go.transform.position = new Vector3(0f, 2f, 0f);
                Vector3[] positions = null;
                Assert.Empty(Logs(() =>
                {
                    skin.BakeMesh(baked, false);
                    Assert.True(skin.SkinnedPose(out positions, out _));
                }));
                Assert.Equal(4, baked.vertexCount);
                Assert.Equal(new Vector3(-5f, 0f, -5f), baked.vertices[0]);
                Assert.Equal(new Vector3(-5f, 2f, -5f), positions[0]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
