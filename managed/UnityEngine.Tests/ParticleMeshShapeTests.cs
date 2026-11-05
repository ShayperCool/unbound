using System;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The shape module's mesh shapes (Unity 6000.0 ShapeModule.meshShapeType,
    /// meshSpawnMode, useMeshColors, useMeshMaterialIndex, normalOffset, SkinnedMeshRenderer).
    /// These check where particles start, not Unity's random sequence.</summary>
    public class ParticleMeshShapeTests
    {
        // Submesh 0: a right triangle of area 2 at z = 0; submesh 1: one of area 0.5 at z = 5.
        static readonly Vector3[] s_Vertices =
        {
            new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 2f, 0f),
            new Vector3(0f, 0f, 5f), new Vector3(1f, 0f, 5f), new Vector3(0f, 1f, 5f),
        };

        static Mesh TwoTriangles(Color[] colors = null)
        {
            var mesh = new Mesh { vertices = s_Vertices };
            if (colors != null) mesh.colors = colors;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            return mesh;
        }

        static ParticleSystem Make(Mesh mesh, ParticleSystemMeshShapeType type)
        {
            var go = new GameObject("mesh-shape");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 7;
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.mesh = mesh;
            shape.meshShapeType = type;
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Particle[] Read(ParticleSystem ps)
        {
            var p = new ParticleSystem.Particle[ps.particleCount];
            Assert.Equal(p.Length, ps.GetParticles(p));
            return p;
        }

        /// <summary>The barycentric coordinates of <paramref name="p"/> in the triangle, when it lies
        /// in the triangle's plane.</summary>
        static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 weights)
        {
            weights = default;
            var n = Vector3.Cross(b - a, c - a);
            if (Mathf.Abs(Vector3.Dot(p - a, n.normalized)) > 1e-4f) return false;
            float area = n.magnitude;
            float wa = Vector3.Dot(Vector3.Cross(c - b, p - b), n) / (area * area);
            float wb = Vector3.Dot(Vector3.Cross(a - c, p - c), n) / (area * area);
            float wc = 1f - wa - wb;
            weights = new Vector3(wa, wb, wc);
            return wa >= -1e-4f && wb >= -1e-4f && wc >= -1e-4f;
        }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            var ab = b - a;
            t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }

        // Documented (ParticleSystemMeshShapeType.Triangle): emission from the mesh's triangles
        // rather than its vertices. How the triangles are weighted is not documented, so no
        // distribution (such as an area ratio) is asserted.
        [Fact]
        public void TriangleModeEmitsFromTheTriangleSurfaces()
        {
            var ps = Make(TwoTriangles(), ParticleSystemMeshShapeType.Triangle);
            try
            {
                ps.Emit(400);
                var particles = Read(ps);
                Assert.Equal(400, particles.Length);
                int interior = 0;
                foreach (var p in particles)
                {
                    var x = p.position;
                    bool inLarge = InTriangle(x, s_Vertices[0], s_Vertices[1], s_Vertices[2], out var w0);
                    bool inSmall = InTriangle(x, s_Vertices[3], s_Vertices[4], s_Vertices[5], out var w1);
                    Assert.True(inLarge || inSmall, $"{x} is on no triangle");
                    var w = inLarge ? w0 : w1;
                    if (w.x > 1e-3f && w.y > 1e-3f && w.z > 1e-3f) interior++;
                }
                // Points of the surface, not its corners: almost every point is inside a triangle.
                Assert.InRange(interior, 380, 400);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void EdgeModeEmitsAlongTriangleEdges()
        {
            var ps = Make(TwoTriangles(), ParticleSystemMeshShapeType.Edge);
            try
            {
                ps.Emit(300);
                int inside = 0;
                foreach (var p in Read(ps))
                {
                    float best = float.MaxValue, bestT = 0f;
                    for (int tri = 0; tri < 2; tri++)
                        for (int e = 0; e < 3; e++)
                        {
                            float d = SegmentDistance(p.position, s_Vertices[tri * 3 + e], s_Vertices[tri * 3 + (e + 1) % 3], out float t);
                            if (d < best) { best = d; bestT = t; }
                        }
                    Assert.True(best < 1e-4f, $"{p.position} is on no edge");
                    if (bestT > 1e-3f && bestT < 1f - 1e-3f) inside++;
                }
                // Points along the edges, not only their ends: almost no point is a corner. The
                // length weighting is an unmeasured hypothesis and is not asserted.
                Assert.InRange(inside, 285, 300);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        /// <summary>The index of the vertex at <paramref name="p"/>; fails when it is no vertex.</summary>
        static int VertexAt(Vector3 p)
        {
            int index = Array.IndexOf(s_Vertices, p);
            Assert.True(index >= 0, $"{p} is no vertex");
            return index;
        }

        /// <summary>The vertices one particle each is emitted from at the given emission times
        /// (the time the spawn modes move with).</summary>
        static int[] VerticesAt(ParticleSystem ps, float[] times)
        {
            var result = new int[times.Length];
            for (int i = 0; i < times.Length; i++)
            {
                ps.Clear();
                ps.m_TotalTime = times[i];
                ps.Emit(1);
                var particles = Read(ps);
                Assert.Single(particles);
                result[i] = VertexAt(particles[0].position);
            }
            return result;
        }

        /// <summary>Emission times over two seconds, away from the vertex boundaries.</summary>
        static float[] TwoSeconds()
        {
            var times = new float[20];
            for (int i = 0; i < times.Length; i++) times[i] = 0.05f + 0.1f * i;
            return times;
        }

        // meshSpawnSpeed: in the animated modes it sets how quickly the emission position moves
        // across the mesh (Unity 6000.0 ShapeModule.meshSpawnSpeed). Which vertex a position maps to
        // and the travel rate are unmeasured and not asserted.
        [Theory]
        [InlineData(ParticleSystemShapeMultiModeValue.Loop)]
        [InlineData(ParticleSystemShapeMultiModeValue.PingPong)]
        public void VertexModeLoopWithoutSpeedKeepsOneVertex(ParticleSystemShapeMultiModeValue mode)
        {
            var ps = Make(TwoTriangles(), ParticleSystemMeshShapeType.Vertex);
            try
            {
                var shape = ps.shape;
                shape.meshSpawnMode = mode;
                shape.meshSpawnSpeed = 0f;
                var vertices = VerticesAt(ps, TwoSeconds());
                foreach (int v in vertices) Assert.Equal(vertices[0], v);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // meshSpawnSpread: the gap between emission points across the mesh (Unity 6000.0
        // ShapeModule.meshSpawnSpread; Manual: Spread). A spread of 0.5 leaves at most two points.
        [Fact]
        public void VertexModeLoopSnapsToTheSpread()
        {
            var ps = Make(TwoTriangles(), ParticleSystemMeshShapeType.Vertex);
            try
            {
                var shape = ps.shape;
                shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
                shape.meshSpawnSpeed = 1f;
                // Without a spread the moving position reaches more than two vertices.
                Assert.True(new System.Collections.Generic.HashSet<int>(VerticesAt(ps, TwoSeconds())).Count > 2);
                shape.meshSpawnSpread = 0.5f;
                Assert.Equal(2, new System.Collections.Generic.HashSet<int>(VerticesAt(ps, TwoSeconds())).Count);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // Manual (Shape module, Mode): Ping-Pong is like Loop, except that each consecutive loop
        // runs in the opposite direction to the previous one.
        [Fact]
        public void VertexModePingPongReversesEachLoop()
        {
            var ps = Make(TwoTriangles(), ParticleSystemMeshShapeType.Vertex);
            try
            {
                var shape = ps.shape;
                shape.meshSpawnSpeed = 1f;
                shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
                var loop = VerticesAt(ps, TwoSeconds());
                shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.PingPong;
                var pingPong = VerticesAt(ps, TwoSeconds());
                int half = loop.Length / 2;
                Assert.True(new System.Collections.Generic.HashSet<int>(pingPong).Count > 1, "the position does not move");
                for (int i = 0; i < half; i++)
                {
                    // Loop repeats the first loop; PingPong runs it backwards.
                    Assert.Equal(loop[i], loop[half + i]);
                    Assert.Equal(pingPong[i], pingPong[loop.Length - 1 - i]);
                }
                Assert.NotEqual(pingPong[0], pingPong[half]);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void EdgeModeLoopWithoutSpeedKeepsOnePointOnAnEdge()
        {
            var ps = Make(TwoTriangles(), ParticleSystemMeshShapeType.Edge);
            try
            {
                var shape = ps.shape;
                shape.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
                shape.meshSpawnSpeed = 0f;
                ps.Emit(20);
                var particles = Read(ps);
                // A loop that does not move emits from one point of the wire.
                foreach (var p in particles) Assert.Equal(particles[0].position, p.position);
                float best = float.MaxValue;
                for (int tri = 0; tri < 2; tri++)
                    for (int e = 0; e < 3; e++)
                        best = Math.Min(best, SegmentDistance(particles[0].position, s_Vertices[tri * 3 + e], s_Vertices[tri * 3 + (e + 1) % 3], out _));
                Assert.True(best < 1e-4f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(ParticleSystemMeshShapeType.Vertex)]
        [InlineData(ParticleSystemMeshShapeType.Edge)]
        [InlineData(ParticleSystemMeshShapeType.Triangle)]
        public void SingleMaterialEmitsFromThatSubmeshOnly(ParticleSystemMeshShapeType type)
        {
            var ps = Make(TwoTriangles(), type);
            try
            {
                var shape = ps.shape;
                shape.useMeshMaterialIndex = true;
                shape.meshMaterialIndex = 1;
                ps.Emit(100);
                foreach (var p in Read(ps))
                {
                    Assert.InRange(Mathf.Abs(p.position.z - 5f), 0f, 1e-5f);
                    Assert.True(InTriangle(p.position, s_Vertices[3], s_Vertices[4], s_Vertices[5], out _), $"{p.position} is outside submesh 1");
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void MeshColorsMultiplyTheStartColor()
        {
            var colors = new[]
            {
                Color.red, Color.green, Color.blue,
                new Color(1f, 1f, 0f, 1f), new Color(0f, 1f, 1f, 0.5f), Color.white,
            };
            var ps = Make(TwoTriangles(colors), ParticleSystemMeshShapeType.Vertex);
            try
            {
                var main = ps.main; main.startColor = new Color(0.5f, 1f, 1f, 1f);
                var shape = ps.shape;
                ps.Emit(30);
                foreach (var p in Read(ps))
                {
                    // The colour of the vertex the particle starts at.
                    Color expected = new Color(0.5f, 1f, 1f, 1f) * colors[VertexAt(p.position)];
                    Color actual = p.startColor;
                    Assert.InRange(Mathf.Abs(actual.r - expected.r), 0f, 1f / 255f);
                    Assert.InRange(Mathf.Abs(actual.g - expected.g), 0f, 1f / 255f);
                    Assert.InRange(Mathf.Abs(actual.b - expected.b), 0f, 1f / 255f);
                    Assert.InRange(Mathf.Abs(actual.a - expected.a), 0f, 1f / 255f);
                }

                // Without Use Mesh Colors the start colour stays as it is.
                ps.Clear();
                shape.useMeshColors = false;
                ps.Emit(6);
                foreach (var p in Read(ps)) Assert.Equal(new Color32(128, 255, 255, 255), p.startColor);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void TriangleNormalOffsetAndAlignmentFollowTheFaceNormal()
        {
            // A triangle facing +x (no vertex normals: its face normal).
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 2f, 0f), new Vector3(0f, 0f, 2f) },
                triangles = new[] { 0, 1, 2 },
            };
            var ps = Make(mesh, ParticleSystemMeshShapeType.Triangle);
            try
            {
                var shape = ps.shape;
                shape.normalOffset = 0.5f;
                shape.alignToDirection = true;
                ps.Emit(50);
                foreach (var p in Read(ps))
                {
                    Assert.InRange(Mathf.Abs(p.position.x - 0.5f), 0f, 1e-5f);
                    // The forward axis turned to +x.
                    Assert.InRange(Mathf.Abs(p.rotation3D.y - 90f), 0f, 1e-2f);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void SkinnedMeshRendererEmitsFromTheCurrentPose()
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
                bindposes = new[] { boneA.worldToLocalMatrix, boneB.worldToLocalMatrix },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { boneA, boneB };

            var ps = Make(null, ParticleSystemMeshShapeType.Vertex);
            try
            {
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                // The public setter is still a generated stub: set what a scene would load.
                ps.m_Shape.skinnedMeshRenderer = renderer;

                // Bone b moves 2 to the right: its vertex follows, the shared one half as much.
                boneB.localPosition += new Vector3(2f, 0f, 0f);
                Time.s_FrameCount++;
                ps.Emit(60);
                var posed = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 1f, 0f), new Vector3(1f, 0.5f, 0f) };
                var seen = new System.Collections.Generic.HashSet<Vector3>();
                foreach (var p in Read(ps))
                {
                    Assert.Contains(p.position, posed);
                    seen.Add(p.position);
                }
                // Every posed vertex, none of the bind pose's (random vertices: one missing in 60
                // draws has a probability below 1e-10).
                Assert.Equal(3, seen.Count);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(root);
            }
        }
    }
}
