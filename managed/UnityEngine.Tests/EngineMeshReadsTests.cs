using System.Collections.Generic;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The engine's own systems read meshes that scripts cannot (Mesh.EngineReads): a
    /// mesh made no longer readable builds the same navigation mesh and skins the same, and logs
    /// nothing (the Unity 6000.0.71f1 Editor's MeshCollider of such a mesh collides and BakeMesh
    /// of it bakes, tests/projects/meshreadable `*.colliderHit`, `bake.locked`; Unbound's other
    /// engine readers follow them).</summary>
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

        /// <summary>Outside Play mode a script reads and combines a mesh that is not readable, as
        /// Mesh.isReadable documents for the Editor ("Access is always allowed in the Unity Editor
        /// outside of the game and rendering loop"); back in Play mode the same read is refused.</summary>
        [Fact]
        public void EditModeScriptsReadMeshesThatAreNotReadable()
        {
            var mesh = Floor(false);
            mesh.name = "Locked";
            bool playing = Unbound.Runtime.EditorSession.IsPlaying;
            try
            {
                Unbound.Runtime.EditorSession.IsPlaying = false;
                Vector3[] vertices = null;
                var combined = new Mesh();
                Assert.Empty(Logs(() =>
                {
                    vertices = mesh.vertices;
                    combined.CombineMeshes(new[] { new CombineInstance { mesh = mesh, transform = Matrix4x4.identity } });
                }));
                Assert.Equal(4, vertices.Length);
                Assert.Equal(4, combined.vertexCount);
                Assert.False(mesh.isReadable);

                Unbound.Runtime.EditorSession.IsPlaying = true;
                Assert.Equal(new[] { "Error: " + Mesh.NotReadable("vertices", "Locked") }, Logs(() => vertices = mesh.vertices));
                Assert.Empty(vertices);
            }
            finally { Unbound.Runtime.EditorSession.IsPlaying = playing; }
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
        public void SkinningReadsMeshesScriptsCannotReadAndCombiningRefusesThem()
        {
            // CombineMeshes leaves such a source out, warned, and refuses such a target (the Unity
            // 6000.0.71f1 Editor, tests/projects/meshreadable `combine.*`).
            var combined = new Mesh { name = "Combined" };
            var source = Floor(false);
            source.name = "Locked";
            Assert.Equal(new[] { "Warning: Cannot combine mesh that does not allow access: Locked" },
                Logs(() => combined.CombineMeshes(new[] { new CombineInstance { mesh = source }, new CombineInstance { mesh = Floor(true), transform = Matrix4x4.Translate(Vector3.up) } })));
            Assert.Equal(4, combined.vertexCount);
            Assert.Equal(new Vector3(-5f, 1f, -5f), combined.vertices[0]);
            Assert.Equal(new[] { "Error: Cannot combine into mesh that does not allow access: Locked" },
                Logs(() => source.CombineMeshes(new[] { new CombineInstance { mesh = Floor(true) } })));

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
                // Drawing it deforms a copy that keeps the source's readability, logging nothing.
                var camera = new GameObject("camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 10f, -10f);
                var context = new Unbound.Runtime.FrameBuilder.CameraContext
                {
                    Camera = camera, Position = camera.transform.position, Forward = Vector3.forward, View = camera.worldToCameraMatrix,
                };
                var draws = new List<Unbound.Runtime.FrameBuilder.DrawItem>();
                Assert.Empty(Logs(() => skin.CollectDraws(context, draws)));
                Object.DestroyImmediate(camera.gameObject);
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
