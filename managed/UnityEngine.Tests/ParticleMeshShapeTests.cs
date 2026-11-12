using System.Collections.Generic;
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

        // Measured (6000.0.71f1, tests/projects/particlemeshshape loop/vertex/*, pingpong/vertex/*):
        // the Vertex type's Loop and Ping-Pong walk the vertex buffer one vertex per emitted
        // particle, whatever the spawn speed and spread; Ping-Pong turns back at each end and emits
        // from that end twice. Unused vertices are walked too.
        [Theory]
        [InlineData(ParticleSystemShapeMultiModeValue.Loop, 0f, 0f, new[] { 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0 })]
        [InlineData(ParticleSystemShapeMultiModeValue.Loop, 1f, 0.5f, new[] { 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5, 0 })]
        [InlineData(ParticleSystemShapeMultiModeValue.PingPong, 0.7f, 0f, new[] { 0, 1, 2, 3, 4, 5, 5, 4, 3, 2, 1, 0, 0 })]
        public void VertexModeLoopWalksTheVerticesOnePerParticle(ParticleSystemShapeMultiModeValue mode, float speed, float spread, int[] expected)
        {
            var ps = Make(TwoTriangles(), ParticleSystemMeshShapeType.Vertex);
            try
            {
                var shape = ps.shape;
                shape.meshSpawnMode = mode;
                shape.meshSpawnSpeed = speed;
                shape.meshSpawnSpread = spread;
                var vertices = new int[expected.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    ps.Clear();
                    ps.Emit(1);
                    vertices[i] = VertexAt(Read(ps)[0].position);
                }
                Assert.Equal(expected, vertices);
                // A restart starts the walk again.
                ps.Simulate(0f, true, true);
                ps.Emit(1);
                Assert.Equal(0, VertexAt(Read(ps)[0].position));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // Measured (random/vertex/*, grid with a zero-area triangle): Vertex picks a triangle by
        // count, so a degenerate triangle's corners are emitted from too; Triangle and Edge weigh by
        // area and never pick it.
        [Theory]
        [InlineData(ParticleSystemMeshShapeType.Vertex, true)]
        [InlineData(ParticleSystemMeshShapeType.Triangle, false)]
        [InlineData(ParticleSystemMeshShapeType.Edge, false)]
        public void OnlyVertexEmitsFromADegenerateTriangle(ParticleSystemMeshShapeType type, bool reached)
        {
            var far = new Vector3(9f, 9f, 9f);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, far } };
            // Triangle 1 is degenerate: one point used three times.
            mesh.SetTriangles(new[] { 0, 1, 2, 3, 3, 3 }, 0);
            var ps = Make(mesh, type);
            try
            {
                ps.Emit(200);
                bool any = false;
                foreach (var p in Read(ps)) any |= p.position == far;
                Assert.Equal(reached, any);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // Measured (lines/*, points/*, empty/*): a mesh without triangles logs that it is not valid,
        // a surface without area logs so for Edge and Triangle, and the particles start at the
        // shape's origin facing +Z. No mesh at all warns only for Edge and Triangle.
        [Theory]
        [InlineData(ParticleSystemMeshShapeType.Vertex, false, true, false)]
        [InlineData(ParticleSystemMeshShapeType.Triangle, false, true, true)]
        [InlineData(ParticleSystemMeshShapeType.Vertex, true, false, false)]
        [InlineData(ParticleSystemMeshShapeType.Edge, true, false, true)]
        public void MeshesWithoutTrianglesWarnAndEmitAtTheOrigin(ParticleSystemMeshShapeType type, bool noMesh, bool notValid, bool zeroSurface)
        {
            Mesh mesh = null;
            if (!noMesh)
            {
                mesh = new Mesh { vertices = new[] { Vector3.one, Vector3.right, Vector3.up } };
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Points, 0);
            }
            var ps = Make(mesh, type);
            var messages = new System.Collections.Generic.List<string>();
            void Capture(string message, string stack, LogType logType) { if (logType == LogType.Warning) lock (messages) messages.Add(message); }
            Application.logMessageReceived += Capture;
            try
            {
                var main = ps.main; main.startSpeed = 1f;
                ps.Emit(5);
                foreach (var p in Read(ps))
                {
                    Assert.Equal(Vector3.zero, p.position);
                    Assert.Equal(Vector3.forward, p.velocity);
                }
                Assert.Equal(notValid, messages.Exists(m => m.Contains("Mesh used in Particle System Shape Module is not valid")));
                Assert.Equal(zeroSurface, messages.Exists(m => m.Contains("is trying to spawn on a mesh with zero surface area")));
            }
            finally
            {
                Application.logMessageReceived -= Capture;
                Object.DestroyImmediate(ps.gameObject);
            }
        }

        // Measured (meshrenderer/*): without vertex colours a renderer's particle takes the colour of
        // the material of the submesh it came from.
        [Fact]
        public void RendererMaterialColourFollowsTheParticlesSubmesh()
        {
            var target = new GameObject("renderer");
            target.AddComponent<MeshFilter>().sharedMesh = TwoTriangles();
            var renderer = target.AddComponent<MeshRenderer>();
            var shader = Shader.Create("Tests/Mesh Shape Colour", 2000);
            renderer.sharedMaterials = new[] { new Material(shader) { color = Color.red }, new Material(shader) { color = Color.blue } };
            var ps = Make(null, ParticleSystemMeshShapeType.Triangle);
            try
            {
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = renderer;
                ps.Emit(50);
                int red = 0, blue = 0;
                foreach (var p in Read(ps))
                {
                    bool second = p.position.z > 2.5f;
                    Assert.Equal(second ? (Color32)Color.blue : (Color32)Color.red, p.startColor);
                    if (second) blue++; else red++;
                }
                Assert.True(red > 0 && blue > 0);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(target);
            }
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
        public void ATriangleSubmeshPickedBesideLinesKeepsTheTriangleVertexRule()
        {
            // Single Material 0 of a triangle submesh beside a lines submesh: Vertex picks all three
            // corners, as for a mesh of triangles only (the segment rule would never pick the
            // third); tests/projects/particlemeshshape topology/mixed0.
            var mesh = new Mesh { vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(2f, 1f, 0f), new Vector3(0f, 1f, 0f) } };
            mesh.subMeshCount = 2;
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            mesh.SetIndices(new[] { 2, 3, 3, 0 }, MeshTopology.Lines, 1);
            var ps = Make(mesh, ParticleSystemMeshShapeType.Vertex);
            try
            {
                var shape = ps.shape;
                shape.useMeshMaterialIndex = true;
                shape.meshMaterialIndex = 0;
                ps.Emit(60);
                var corners = new HashSet<Vector3>();
                foreach (var p in Read(ps)) corners.Add(p.position);
                Assert.Equal(new HashSet<Vector3> { mesh.vertices[0], mesh.vertices[1], mesh.vertices[2] }, corners);
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

        // Measured (normaloffset/*, align/*, nonormals/*): the direction is the interpolated vertex
        // normal and the offset follows it; without normals both are +Z. Align to Direction adds the
        // pitch and yaw that turn -Z to the direction.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void NormalOffsetAndAlignmentFollowTheVertexNormals(bool normals)
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 2f, 0f), new Vector3(0f, 0f, 2f) },
                triangles = new[] { 0, 1, 2 },
            };
            if (normals) mesh.normals = new[] { Vector3.right, Vector3.right, Vector3.right };
            var ps = Make(mesh, ParticleSystemMeshShapeType.Triangle);
            try
            {
                var shape = ps.shape;
                shape.normalOffset = 0.5f;
                shape.alignToDirection = true;
                ps.Emit(50);
                foreach (var p in Read(ps))
                {
                    if (normals)
                    {
                        Assert.InRange(Mathf.Abs(p.position.x - 0.5f), 0f, 1e-5f);
                        Assert.InRange(Mathf.Abs(p.rotation3D.y - 90f), 0f, 1e-2f);
                    }
                    else
                    {
                        Assert.InRange(Mathf.Abs(p.position.x), 0f, 1e-5f);
                        Assert.InRange(p.position.z, 0.5f - 1e-5f, 2.5f + 1e-5f);
                        // +Z: -Z turned half a turn about Y.
                        Assert.InRange(Mathf.Abs(Mathf.Abs(p.rotation3D.y) - 180f), 0f, 1e-2f);
                    }
                    Assert.InRange(Mathf.Abs(p.rotation3D.x), 0f, 1e-2f);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        /// <summary>Sampling a scaled renderer never writes into a mesh's own vertices, also after
        /// the system sampled another mesh with as many vertices unscaled.</summary>
        [Fact]
        public void ScaledSamplingLeavesEveryMeshsVerticesAlone()
        {
            var meshA = TwoTriangles();
            var meshB = TwoTriangles();
            var target = new GameObject("scaled");
            target.transform.localScale = new Vector3(2f, 2f, 2f);
            target.AddComponent<MeshFilter>().sharedMesh = meshB;
            var renderer = target.AddComponent<MeshRenderer>();
            var ps = Make(meshA, ParticleSystemMeshShapeType.Vertex);
            try
            {
                ps.Emit(10);
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = renderer;
                ps.Emit(10);
                Assert.Equal(s_Vertices, meshA.vertices);
                Assert.Equal(s_Vertices, meshB.vertices);
                // The renderer's particles start at its scaled vertices.
                var scaled = Array.ConvertAll(s_Vertices, v => v * 2f);
                int fromRenderer = 0;
                foreach (var p in Read(ps)) if (Array.IndexOf(scaled, p.position) >= 0 && p.position != Vector3.zero) fromRenderer++;
                Assert.True(fromRenderer > 0);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>A skinned renderer away from the origin (the table keeps the pose in the
        /// renderer's space, in arrays of its own) skinned again within a frame (BakeMesh skins
        /// at once, into the arrays the drawn mesh keeps): the next emission takes that pose.</summary>
        [Fact]
        public void ASkinnedShapeTakesAPoseSkinnedAgainWithinTheFrame()
        {
            var root = new GameObject("root");
            root.transform.position = new Vector3(10f, 0f, 0f);
            var bone = new GameObject("bone").transform;
            bone.SetParent(root.transform, false);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
                triangles = new[] { 0, 1, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                },
                bindposes = new[] { bone.worldToLocalMatrix * root.transform.localToWorldMatrix },
            };
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            var ps = Make(null, ParticleSystemMeshShapeType.Vertex);
            try
            {
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.skinnedMeshRenderer = renderer;
                Time.s_FrameCount++;
                ps.Emit(1);
                var table = ps.m_MeshShapeTable;
                Assert.Contains(new Vector3(1f, 0f, 0f), table.Vertices);

                bone.localPosition = new Vector3(0f, 0f, 3f);
                var baked = new Mesh();
                renderer.BakeMesh(baked);
                Object.DestroyImmediate(baked);
                ps.Emit(1);
                Assert.Contains(new Vector3(1f, 0f, 3f), table.Vertices);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
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
                shape.skinnedMeshRenderer = renderer;
                Assert.Same(renderer, shape.skinnedMeshRenderer);

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

                // A new pose takes the new positions and keeps the triangles and their areas
                // (those of the rest mesh) in the arrays they had.
                var table = ps.m_MeshShapeTable;
                var triangles = table.Triangles;
                var areas = table.AreaEnds;
                float total = table.TotalArea;
                boneB.localPosition += new Vector3(1f, 0f, 0f);
                Time.s_FrameCount++;
                ps.Emit(1);
                Assert.Same(triangles, table.Triangles);
                Assert.Same(areas, table.AreaEnds);
                Assert.Equal(total, table.TotalArea);
                Assert.Contains(new Vector3(3f, 1f, 0f), table.Vertices);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(root);
            }
        }
    }
}
