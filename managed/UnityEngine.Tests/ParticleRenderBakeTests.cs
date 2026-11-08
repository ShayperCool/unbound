using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Stretched billboards, BakeTexture, Camera.velocity and procedural mode as Unity
    /// 6000.0.71f1 measures them (tests/projects/particlerender stretch/*, bake/*, camvel/*, proc/*).
    /// The camera velocity test sets the global clock, so these run with the culling frames.</summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleRenderBakeTests : IDisposable
    {
        readonly List<Object> m_Objects = new List<Object>();
        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly int m_FrameCount = Time.s_FrameCount;

        public void Dispose()
        {
            foreach (var o in m_Objects) if (o) Object.DestroyImmediate(o);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_FrameCount = m_FrameCount;
        }

        T Track<T>(T o) where T : Object { m_Objects.Add(o); return o; }

        /// <summary>A camera at (0, 0, -10) looking along +z, 4:3.</summary>
        Camera FrontCamera()
        {
            var camera = Track(new GameObject("bake-camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.aspect = 4f / 3f;
            return camera;
        }

        /// <summary>A system that only holds particles it is given.</summary>
        ParticleSystem MakeSystem(Action<ParticleSystemRenderer> configure = null)
        {
            var go = Track(new GameObject("bake-system"));
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            go.SetActive(true);
            configure?.Invoke(ps.GetComponent<ParticleSystemRenderer>());
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size) =>
            ps.Emit(new ParticleSystem.EmitParams { position = position, velocity = velocity, startSize = size, startLifetime = 5f }, 1);

        static Vector3[] Bake(ParticleSystem ps, Camera camera, out Vector2[] uv, out Vector3[] normals)
        {
            var mesh = new Mesh();
            try
            {
                ps.GetComponent<ParticleSystemRenderer>().BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                uv = mesh.uv;
                normals = mesh.normals;
                return mesh.vertices;
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        static void Near(Vector3 expected, Vector3 actual) =>
            Assert.True((expected - actual).magnitude < 1e-4f, $"expected {expected}, got {actual}");

        static ParticleSystemRenderer Stretched(ParticleSystemRenderer r)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2f;
            r.velocityScale = 0f;
            return r;
        }

        [Fact]
        public void AStretchedQuadTrailsBackFromItsParticleAcrossItsScreenDirection()
        {
            var camera = FrontCamera();
            var ps = MakeSystem(r => Stretched(r));
            Emit(ps, new Vector3(-0.8f, 0.5f, 0f), new Vector3(2f, 0f, 0f), 0.3f);
            var v = Bake(ps, camera, out var uv, out var normals);
            Near(new Vector3(-0.8f, 0.35f, 0f), v[0]);
            Near(new Vector3(-1.4f, 0.35f, 0f), v[1]);
            Near(new Vector3(-1.4f, 0.65f, 0f), v[2]);
            Near(new Vector3(-0.8f, 0.65f, 0f), v[3]);
            Assert.Equal(new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) }, uv);
            Near(new Vector3(0f, 0f, -1f), normals[0]);
        }

        [Fact]
        public void AStretchedQuadComingTowardsTheCameraKeepsItsWidthInTheCameraPlane()
        {
            var camera = FrontCamera();
            var ps = MakeSystem(r => Stretched(r));
            // Moving almost straight away from the camera: the length runs in depth, the width
            // across the projected direction, with no depth.
            Emit(ps, new Vector3(-0.97f, -0.48f, 0.3f), new Vector3(0.3f, 0.2f, 3f), 0.25f);
            var v = Bake(ps, camera, out _, out _);
            Assert.Equal(v[0].z, v[3].z, 4);
            Assert.True(Mathf.Abs((v[0] - v[1]).magnitude - 0.5f) < 1e-3f);
            Assert.True(Mathf.Abs((v[0] - v[3]).magnitude - 0.25f) < 1e-3f);
        }

        [Fact]
        public void AStretchedParticleAtRestCollapsesAndANegativeLengthStretchesForwards()
        {
            var camera = FrontCamera();
            var ps = MakeSystem(r => { Stretched(r); r.lengthScale = -1f; r.velocityScale = 0.1f; });
            Emit(ps, new Vector3(1f, -0.5f, 0f), Vector3.zero, 0.5f);
            Emit(ps, new Vector3(-0.8f, 0.5f, 0f), new Vector3(2f, 0f, 0f), 0.3f);
            var v = Bake(ps, camera, out _, out _);
            Assert.All(v.Take(4), p => Near(new Vector3(1f, -0.5f, 0f), p));
            // -0.3 + 2 × 0.1 = -0.1: the tail lies 0.1 ahead.
            Near(new Vector3(-0.7f, 0.65f, 0f), v[5]);
        }

        [Fact]
        public void ThePivotMovesAStretchedQuadAlongBySizesAndAcrossByTwoSizes()
        {
            var camera = FrontCamera();
            var ps = MakeSystem(r => { Stretched(r); r.lengthScale = 1f; r.velocityScale = 0.3f; r.pivot = new Vector3(0.5f, 0.25f, 0f); });
            Emit(ps, new Vector3(-0.8f, 0.5f, 0f), new Vector3(2f, 0f, 0f), 0.3f);
            var v = Bake(ps, camera, out _, out _);
            Near(new Vector3(-0.65f, 0.2f, 0f), v[0]);
            Near(new Vector3(-1.55f, 0.2f, 0f), v[1]);
        }

        [Fact]
        public void CameraVelocityIsTheMovementSinceItsLastRenderAndTurnsTheStretch()
        {
            var camera = FrontCamera();
            Time.s_DeltaTime = 0.05f;
            camera.UpdateVelocity();
            Assert.Equal(Vector3.zero, camera.velocity);
            Time.s_FrameCount++;
            camera.transform.position = new Vector3(0.1f, 0.05f, -10f);
            camera.UpdateVelocity();
            Near(new Vector3(2f, 1f, 0f), camera.velocity);
            // A second render in the same frame changes nothing.
            camera.UpdateVelocity();
            Near(new Vector3(2f, 1f, 0f), camera.velocity);

            var ps = MakeSystem(r => { Stretched(r); r.lengthScale = 1f; r.cameraVelocityScale = 0.5f; });
            Emit(ps, new Vector3(-0.8f, 0.5f, 0f), new Vector3(2f, 0f, 0f), 0.3f);
            var v = Bake(ps, camera, out _, out _);
            // Along (2, 0) - 0.5 × (2, 1) = (1, -0.5), 0.3 long.
            var head = (v[0] + v[3]) * 0.5f;
            var tail = (v[1] + v[2]) * 0.5f;
            Near(new Vector3(-0.8f, 0.5f, 0f), head);
            Near(new Vector3(1f, -0.5f, 0f).normalized * 0.3f, head - tail);
        }

        static float[] Texels(Texture2D t)
        {
            var raw = t.GetRawTextureData<byte>().ToArray();
            var floats = new float[raw.Length / 4];
            Buffer.BlockCopy(raw, 0, floats, 0, raw.Length);
            return floats;
        }

        [Fact]
        public void BakeTextureWritesTheVertexAndIndexBuffersAsFloats()
        {
            var camera = FrontCamera();
            var ps = MakeSystem(r => r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV }));
            for (int i = 0; i < 4; i++) Emit(ps, new Vector3(i, 0f, 0f), Vector3.zero, 0.3f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            Texture2D vertices = null, indices = null;
            int n = renderer.BakeTexture(ref vertices, ref indices, camera, ParticleSystemBakeTextureOptions.Default);
            Track(vertices); Track(indices);
            Assert.Equal(24, n);
            // Room for 65 records of 9 floats, rows of whole records.
            Assert.Equal((27, 22, TextureFormat.RFloat), (vertices.width, vertices.height, vertices.format));
            Assert.Equal((12, 11, TextureFormat.RFloat), (indices.width, indices.height, indices.format));
            var mesh = Track(new Mesh());
            renderer.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
            var texels = Texels(vertices);
            var colors = mesh.colors32;
            for (int v = 0; v < mesh.vertexCount; v++)
            {
                Assert.Equal(mesh.vertices[v].x, texels[v * 9]);
                Assert.Equal(mesh.normals[v].z, texels[v * 9 + 5]);
                var c = colors[v];
                Assert.Equal(c.r | c.g << 8 | c.b << 16 | c.a << 24, BitConverter.SingleToInt32Bits(texels[v * 9 + 6]));
                Assert.Equal(mesh.uv[v].y, texels[v * 9 + 8]);
            }
            Assert.Equal(mesh.triangles.Select(i => (float)i), Texels(indices).Take(24));

            // One record per particle and each particle's own indices with its index.
            Texture2D perParticle = null, particleIndices = null;
            renderer.BakeTexture(ref perParticle, ref particleIndices, camera,
                ParticleSystemBakeTextureOptions.PerParticle | ParticleSystemBakeTextureOptions.IncludeParticleIndices);
            Track(perParticle); Track(particleIndices);
            Assert.Equal(TextureFormat.RGFloat, particleIndices.format);
            var records = Texels(perParticle);
            for (int k = 0; k < 4; k++) Assert.Equal(mesh.vertices[k * 4].x, records[k * 9]);
            var pairs = Texels(particleIndices);
            Assert.Equal(new float[] { 0, 0, 1, 0, 2, 0, 2, 0, 3, 0, 0, 0, 0, 1, 1, 1 }, pairs.Take(16));
        }

        [Fact]
        public void BakeTextureKeepsATexturePassedInAndBakesNothingFromAnEmptySystem()
        {
            var camera = FrontCamera();
            var ps = MakeSystem();
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var given = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            var kept = given;
            Texture2D none = null;
            Assert.Equal(0, renderer.BakeTexture(ref kept, ref none, camera, ParticleSystemBakeTextureOptions.Default));
            Assert.Same(given, kept);
            Assert.Null(none);
            Assert.Equal(2, kept.width);

            Emit(ps, Vector3.zero, Vector3.zero, 0.3f);
            Assert.Equal(6, renderer.BakeTexture(ref kept, camera, ParticleSystemBakeTextureOptions.Default));
            Assert.Same(given, kept);
            Assert.Equal(TextureFormat.RGBA32, kept.format);
            Assert.True(kept.width * kept.height >= 4 * 9);
        }

        [Fact]
        public void APerParticleBakeOfManyMeshParticlesKeepsEveryRecord()
        {
            var camera = FrontCamera();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(cube);
            var ps = MakeSystem(r =>
            {
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = mesh;
                r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                    { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            });
            var main = ps.main;
            main.maxParticles = 200;
            // 100 records of 13 floats (1300) outgrow room for 129 vertex records of 9 (1161).
            for (int i = 0; i < 100; i++) Emit(ps, new Vector3(i * 0.1f, 0f, 0f), Vector3.zero, 0.3f);
            Texture2D records = null, indices = null;
            // Mesh particles bake only with an indices texture (the overload without one throws).
            Assert.Throws<InvalidOperationException>(() => ps.GetComponent<ParticleSystemRenderer>().BakeTexture(ref records, camera, ParticleSystemBakeTextureOptions.PerParticle));
            ps.GetComponent<ParticleSystemRenderer>().BakeTexture(ref records, ref indices, camera, ParticleSystemBakeTextureOptions.PerParticle);
            Track(records);
            Track(indices);
            var texels = Texels(records);
            // 13 floats a particle (transform, centre, color): the last particle's centre is there.
            Assert.True(texels.Length >= 100 * 13);
            Assert.Equal(99 * 0.1f, texels[99 * 13 + 9], 4);
        }

        [Fact]
        public void ProceduralModeIsDecidedWhenAPlaybackStartsAndLostByAPropertySetWhilePlaying()
        {
            var ps = MakeSystem();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            Assert.True(ps.proceduralSimulationSupported);
            ps.Play();
            Assert.False(ps.proceduralSimulationSupported);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            ps.Play();
            Assert.True(ps.proceduralSimulationSupported);
            main.startSize = 0.6f;
            Assert.False(ps.proceduralSimulationSupported);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Assert.False(ps.proceduralSimulationSupported);
            ps.Play();
            Assert.True(ps.proceduralSimulationSupported);
            var limit = ps.limitVelocityOverLifetime;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            limit.enabled = true;
            ps.Play();
            Assert.False(ps.proceduralSimulationSupported);
        }

        [Fact]
        public void LimitVelocityDampenDefaultsToZero()
        {
            var ps = MakeSystem();
            Assert.Equal(0f, ps.limitVelocityOverLifetime.dampen);
        }

        /// <summary>Mesh particles turn a z rotation about their axis of rotation: forward for births,
        /// up for particles a system held when it switched to Mesh mode; setting Mesh again, or
        /// reading the particles, keeps the axes (6000.0.71f1, particlerender bake/mesh, meshrot/*).</summary>
        /// <summary>A system that starts with a Mesh-mode renderer and a prewarm: its prewarmed
        /// particles turn about forward and keep it.</summary>
        [Fact]
        public void APrewarmedMeshSystemKeepsForwardAxes()
        {
            var go = new GameObject("prewarmed mesh");
            go.SetActive(false);
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main; main.playOnAwake = true; main.loop = true; main.prewarm = true; main.startLifetime = 2f;
                var emission = ps.emission; emission.rateOverTime = 5f;
                go.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Mesh;
                go.SetActive(true);
                var particles = new ParticleSystem.Particle[64];
                int n = ps.GetParticles(particles);
                Assert.True(n > 0);
                for (int i = 0; i < n; i++) Assert.Equal(Vector3.forward, particles[i].axisOfRotation);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void MeshModeGivesHeldParticlesAnAxisOnceAndKeepsItAfterwards()
        {
            var ps = MakeSystem();
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            Emit(ps, Vector3.zero, Vector3.zero, 0.3f);
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            Emit(ps, Vector3.one, Vector3.zero, 0.3f);
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            var particles = new ParticleSystem.Particle[2];
            Assert.Equal(2, ps.GetParticles(particles));
            Assert.Equal(Vector3.up, particles[0].axisOfRotation);
            Assert.Equal(Vector3.forward, particles[1].axisOfRotation);
        }
    }
}
