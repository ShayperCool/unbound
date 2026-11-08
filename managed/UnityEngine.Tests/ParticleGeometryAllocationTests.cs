using System;
using System.Collections.Generic;
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

        static long Allocated(Action build)
        {
            build();
            build();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4; i++) build();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        static ParticleSystemRenderer.View View() =>
            ParticleSystemRenderer.View.FromMatrix(Matrix4x4.TRS(new Vector3(0f, 0f, 10f), Quaternion.identity, new Vector3(1f, 1f, -1f)),
                new Vector3(0f, 0f, -10f), false, null);

        [Theory]
        [InlineData(ParticleSystemTrailMode.Ribbon, 1)]
        [InlineData(ParticleSystemTrailMode.Ribbon, 3)]
        [InlineData(ParticleSystemTrailMode.PerParticle, 1)]
        public void TrailGeometryBuildsWithoutAllocating(ParticleSystemTrailMode mode, int ribbonCount)
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
                Assert.Equal(0L, Allocated(() => geometry.BuildTrails(ps, view, s_Streams)));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(ParticleSystemRenderMode.Billboard, false)]
        [InlineData(ParticleSystemRenderMode.Stretch, false)]
        [InlineData(ParticleSystemRenderMode.Mesh, false)]
        [InlineData(ParticleSystemRenderMode.Mesh, true)]
        public void ParticleGeometryBuildsWithoutAllocating(ParticleSystemRenderMode mode, bool customStreams)
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
                Assert.Equal(0L, Allocated(() => geometry.Build(renderer, ps, view)));
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(cube);
            }
        }
    }
}
