using System;
using static UnityEngine.Tests.AllocationMeasurement;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Building particle and trail geometry runs per camera every frame: once its buffers
    /// have grown, a build allocates nothing.</summary>
    public class ParticleGeometryAllocationTests
    {
        static readonly List<ParticleSystemVertexStream> s_Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        };

        static ParticleSystem Make(Action<ParticleSystem> configure)
        {
            var go = new GameObject("allocation");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 5u;
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 5f;
            main.startSpeed = 1f;
            var emission = ps.emission;
            emission.rateOverTime = 40f;
            configure(ps);
            return ps;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference UnloadAssembly()
        {
            var context = new AssemblyLoadContext("particle allocation control", isCollectible: true);
            using var image = File.OpenRead(typeof(ParticleGeometryAllocationTests).Assembly.Location);
            context.LoadFromStream(image);
            var weak = new WeakReference(context);
            context.Unload();
            return weak;
        }

        static void PrepareWindow(Action warm, bool unloadAssembly)
        {
            var context = unloadAssembly ? UnloadAssembly() : null;
            // Collections can finish collectible assembly unloads and clear CoreCLR's shared
            // type-cast cache. Finish that cleanup before warming the measured operation.
            for (int i = 0; i < 2; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            if (context != null) Assert.False(context.IsAlive);
            warm();
            warm();
            EmptyAllocationContext();
        }

        // Keep assertion type tests in the caller outside both allocation-counter reads.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static long Allocated(Action build, bool unloadAssembly = false)
        {
            PrepareWindow(build, unloadAssembly);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4; i++) build();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        static ParticleSystemRenderer.View View() =>
            ParticleSystemRenderer.View.FromMatrix(Matrix4x4.TRS(new Vector3(0f, 0f, 10f), Quaternion.identity, new Vector3(1f, 1f, -1f)),
                new Vector3(0f, 0f, -10f), false, null);

        [Theory]
        [InlineData(ParticleSystemTrailMode.Ribbon, 1, false)]
        [InlineData(ParticleSystemTrailMode.Ribbon, 3, false)]
        [InlineData(ParticleSystemTrailMode.PerParticle, 1, false)]
        [InlineData(ParticleSystemTrailMode.Ribbon, 1, true)]
        [InlineData(ParticleSystemTrailMode.Ribbon, 3, true)]
        [InlineData(ParticleSystemTrailMode.PerParticle, 1, true)]
        public void TrailGeometryBuildsWithoutAllocating(ParticleSystemTrailMode mode, int ribbonCount, bool unloadAssembly)
        {
            var ps = Make(s =>
            {
                var trails = s.trails;
                trails.enabled = true;
                trails.mode = mode;
                trails.ribbonCount = ribbonCount;
                trails.minVertexDistance = 0f;
            });
            try
            {
                for (int i = 0; i < 10; i++) ps.Simulate(0.05f, true, i == 0, false);
                var geometry = new ParticleSystemRenderer.Geometry();
                var view = View();
                Assert.True(geometry.BuildTrails(ps, view, s_Streams));
                Assert.True(geometry.VertexCount > 0);
                Assert.Equal(0L, Allocated(() => geometry.BuildTrails(ps, view, s_Streams), unloadAssembly));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(ParticleSystemRenderMode.Billboard, false, false)]
        [InlineData(ParticleSystemRenderMode.Stretch, false, false)]
        [InlineData(ParticleSystemRenderMode.Mesh, false, false)]
        [InlineData(ParticleSystemRenderMode.Mesh, true, false)]
        [InlineData(ParticleSystemRenderMode.Billboard, false, true)]
        [InlineData(ParticleSystemRenderMode.Stretch, false, true)]
        [InlineData(ParticleSystemRenderMode.Mesh, false, true)]
        [InlineData(ParticleSystemRenderMode.Mesh, true, true)]
        public void ParticleGeometryBuildsWithoutAllocating(ParticleSystemRenderMode mode, bool customStreams, bool unloadAssembly)
        {
            var ps = Make(_ => { });
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = mode;
                renderer.SetMeshes(new[] { null, cube.GetComponent<MeshFilter>().sharedMesh });
                if (customStreams) renderer.SetActiveVertexStreams(s_Streams);
                for (int i = 0; i < 4; i++) ps.Simulate(0.05f, true, i == 0, false);
                var geometry = new ParticleSystemRenderer.Geometry();
                var view = View();
                Assert.True(geometry.Build(renderer, ps, view));
                Assert.Equal(0L, Allocated(() => geometry.Build(renderer, ps, view), unloadAssembly));
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(cube);
            }
        }

        /// <summary>A draw mesh rebuilt every frame keeps capacity-sized arrays and uploads the built
        /// counts: once grown, rebuilding it with fewer or as many particles allocates nothing.</summary>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void StreamedDrawMeshesAllocateNothingOnceGrown(bool positionAndUvOnly, bool unloadAssembly)
        {
            // Includes an empty system, and streams without normals and colors.
            int[] counts = { 40, 3, 17, 0, 1, 25 };
            var systems = new ParticleSystem[counts.Length];
            var mesh = new Mesh();
            try
            {
                for (int i = 0; i < counts.Length; i++)
                {
                    systems[i] = Make(s => { var e = s.emission; e.rateOverTime = 0f; });
                    systems[i].Emit(counts[i]);
                    if (positionAndUvOnly)
                        systems[i].GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.UV });
                }
                var geometry = new ParticleSystemRenderer.Geometry();
                var view = View();
                void Draw(ParticleSystem ps)
                {
                    geometry.Build(ps.GetComponent<ParticleSystemRenderer>(), ps, view);
                    geometry.WriteTo(mesh, Matrix4x4.identity, streaming: true);
                    mesh.Sync();
                }
                foreach (var ps in systems) Draw(ps);
                Draw(systems[0]);
                Assert.Equal(160, mesh.m_StreamVertexCount);
                Assert.Equal(240, mesh.m_StreamIndexCount);
                Assert.Equal(0L, Allocated(() =>
                {
                    for (int round = 0; round < 3; round++)
                        foreach (var ps in systems) Draw(ps);
                }, unloadAssembly));
                Assert.Equal(100, mesh.m_StreamVertexCount);
                // An edit through the API makes its arrays the mesh again.
                mesh.vertices = new Vector3[3];
                Assert.Equal(-1, mesh.m_StreamVertexCount);
            }
            finally
            {
                foreach (var ps in systems) if (ps != null) Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Fact]
        public void TheWindowStillCountsAllocationsAfterAnAssemblyUnloads()
        {
            byte[] last = null;
            Assert.True(Allocated(() => last = new byte[64], unloadAssembly: true) >= 4 * 64);
            Assert.Equal(64, last.Length);
            GC.KeepAlive(last);
        }
    }
}
