using System;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleShapeRandomTests
    {
        static ParticleSystem Make(uint seed = 12345)
        {
            var go = new GameObject("shape-random");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed;
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1f; shape.radiusThickness = 1f;
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Particle[] Read(ParticleSystem ps)
        {
            var p = new ParticleSystem.Particle[ps.particleCount];
            Assert.Equal(p.Length, ps.GetParticles(p));
            return p;
        }

        static void SamePositions(ParticleSystem.Particle[] expected, ParticleSystem.Particle[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i].position, actual[i].position);
        }

        [Fact]
        public void SphereKeepsTheMeasuredFourthPositionAcrossLaterSteps()
        {
            var ps = Make();
            try
            {
                var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 40f;
                // Existing particles reference: random_sphere particle 3, steps 0..4.
                const float expected = .6186267f;
                for (int step = 0; step < 5; step++)
                {
                    ps.Simulate(.1f, false, step == 0, false);
                    float actual = Read(ps)[3].position.x;
                    Assert.InRange(Math.Abs(actual - expected), 0f, .001f + .001f * Math.Max(Math.Abs(expected), Math.Abs(actual)));
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReusablePlaybackSnapshotRestoresAllShapeLanesByValue(bool initialized)
        {
            var ps = Make();
            try
            {
                if (initialized) ps.Emit(5);
                var saved = ps.GetPlaybackState();
                ps.Clear(false); ps.Emit(8);
                var expected = Read(ps);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    ps.SetPlaybackState(saved);
                    ps.Clear(false); ps.Emit(8);
                    SamePositions(expected, Read(ps));
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ResetAndSeedReplacementReplayFreshShapeState()
        {
            var ps = Make(); var fresh = Make(305419896);
            try
            {
                ps.Emit(5); ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = 305419896;
                ps.Emit(8); fresh.Emit(8);
                var expected = Read(fresh);
                SamePositions(expected, Read(ps));
                ps.Simulate(0f, false, true, false);
                ps.Emit(8);
                SamePositions(expected, Read(ps));
            }
            finally { Object.DestroyImmediate(ps.gameObject); Object.DestroyImmediate(fresh.gameObject); }
        }

        [Fact]
        public void ShapeDoesNotUseInitializationOrEmissionRandomStateOrGlobalRandom()
        {
            var control = Make(); var varied = Make(); var global = Random.state;
            try
            {
                var m = varied.main; m.startSize3D = true; m.startRotation3D = true;
                control.Emit(4); varied.Emit(4);
                control.m_RandomState = 1; varied.m_RandomState = 987654321;
                control.Clear(false); varied.Clear(false);
                control.Emit(8); varied.Emit(new ParticleSystem.EmitParams { randomSeed = 77 }, 8);
                SamePositions(Read(control), Read(varied));
                Assert.Equal(global, Random.state);
            }
            finally { Object.DestroyImmediate(control.gameObject); Object.DestroyImmediate(varied.gameObject); }
        }

        [Fact]
        public void PackedShapeGroupsAdvanceUnusedLanes()
        {
            var partial = Make(); var complete = Make();
            try
            {
                partial.Emit(1); complete.Emit(4);
                partial.Clear(false); complete.Clear(false);
                partial.Emit(8); complete.Emit(8);
                SamePositions(Read(complete), Read(partial));
            }
            finally { Object.DestroyImmediate(partial.gameObject); Object.DestroyImmediate(complete.gameObject); }
        }

        [Fact]
        public void DisabledShapeDoesNotAdvanceItsStream()
        {
            var ps = Make(); var fresh = Make();
            try
            {
                var shape = ps.shape; shape.enabled = false;
                ps.Emit(8); ps.Clear(false);
                shape.enabled = true;
                ps.Emit(8); fresh.Emit(8);
                SamePositions(Read(fresh), Read(ps));
            }
            finally { Object.DestroyImmediate(ps.gameObject); Object.DestroyImmediate(fresh.gameObject); }
        }
    }
}
