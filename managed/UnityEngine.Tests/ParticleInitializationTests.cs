using System;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleInitializationTests
    {
        // Public-call inputs and expected values are measured in particleseedaxis.
        static ParticleSystem Make()
        {
            var go = new GameObject("initialization");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 12345u;
            var m = ps.main;
            m.playOnAwake = false;
            m.startLifetime = 2.013f;
            m.startSpeed = 1f;
            m.startSize = .75f;
            m.startRotation = .3f;
            m.startColor = (Color)new Color32(192, 128, 224, 200);
            var shape = ps.shape; shape.enabled = false;
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Particle[] Read(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            Assert.Equal(particles.Length, ps.GetParticles(particles));
            return particles;
        }

        [Theory]
        [InlineData(2, 1622567041u)]
        [InlineData(5, 3251230716u)]
        public void BulkEmissionRetainsPackedLanes(int count, uint next)
        {
            var ps = Make();
            try
            {
                Assert.Empty(Read(ps));
                ps.Emit(count);
                var first = Read(ps);
                uint[] seeds = { 692788716u, 601952211u, 495557956u, 337019225u, 1622567041u };
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal(seeds[i], first[i].randomSeed);
                    Assert.Equal(Vector3.up, first[i].axisOfRotation);
                }
                ps.Emit(1);
                var second = Read(ps);
                Assert.Equal(next, second[count].randomSeed);
                Assert.Equal(Vector3.forward, second[count].axisOfRotation);
                Assert.Equal(Vector3.up, second[0].axisOfRotation);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void FirstNonemptyReadMaterializesEveryExistingAxis()
        {
            var ps = Make();
            try
            {
                Assert.Empty(Read(ps));
                ps.Emit(1); ps.Emit(1); ps.Emit(1);
                var particles = Read(ps);
                uint[] seeds = { 692788716u, 1622567041u, 3251230716u };
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(seeds[i], particles[i].randomSeed);
                    Assert.Equal(Vector3.up, particles[i].axisOfRotation);
                }
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.randomSeed = 305419896u;
                ps.Emit(2);
                particles = Read(ps);
                Assert.Equal(1131246930u, particles[0].randomSeed);
                Assert.Equal(1556539901u, particles[1].randomSeed);
                Assert.Equal(Vector3.forward, particles[0].axisOfRotation);
                Assert.Equal(Vector3.forward, particles[1].axisOfRotation);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(true, false, 2613184690u)]
        [InlineData(false, true, 2613184690u)]
        [InlineData(true, true, 1812774966u)]
        public void ThreeDimensionalSettingsConsumeTheirOwnSamples(bool size, bool rotation, uint next)
        {
            var ps = Make();
            try
            {
                var m = ps.main; m.startSize3D = size; m.startRotation3D = rotation;
                ps.Emit(2); ps.Emit(1);
                Assert.Equal(next, Read(ps)[2].randomSeed);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void RandomizedFieldsUseTheirInitializationConsumers()
        {
            var ps = Make();
            var global = Random.state;
            try
            {
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                m.startSize = new ParticleSystem.MinMaxCurve(.5f, 1.5f);
                m.startRotation = new ParticleSystem.MinMaxCurve(.1f, .9f);
                m.startColor = new ParticleSystem.MinMaxGradient(Color.black, Color.white);
                m.flipRotation = .5f;
                ps.Emit(2); ps.Emit(1);
                var p = Read(ps);
                Assert.Equal(1622567041u, p[2].randomSeed);
                Assert.InRange(Math.Abs(p[0].startLifetime - 3.799121f), 0f, .00001f);
                Assert.InRange(Math.Abs(p[0].startSize - 1.03251648f), 0f, .00001f);
                Assert.InRange(Math.Abs(p[0].velocity.z - 1.876556f), 0f, .00001f);
                Assert.InRange(Math.Abs(p[2].rotation + 21.9408073f), 0f, .0001f);
                Assert.Equal(new Color32(66, 66, 66, 255), p[0].startColor);
                Assert.Equal(global, Random.state);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ExplicitSeedSkipsSeedGenerationAndImportedAxisPersists()
        {
            var ps = Make();
            var imported = Make();
            try
            {
                ps.Emit(new ParticleSystem.EmitParams { randomSeed = 77u }, 2);
                ps.Emit(1);
                var p = Read(ps);
                Assert.Equal(77u, p[0].randomSeed);
                Assert.Equal(77u, p[1].randomSeed);
                Assert.Equal(589364589u, p[2].randomSeed);
                imported.SetParticles(new[] { new ParticleSystem.Particle {
                    startLifetime = 4.026f, remainingLifetime = 2.013f,
                    randomSeed = 77u, axisOfRotation = Vector3.right } }, 1);
                imported.Emit(1);
                p = Read(imported);
                Assert.Equal(Vector3.right, p[0].axisOfRotation);
                Assert.Equal(Vector3.forward, p[1].axisOfRotation);
                Assert.Equal(692788716u, p[1].randomSeed);
            }
            finally { Object.DestroyImmediate(ps.gameObject); Object.DestroyImmediate(imported.gameObject); }
        }
    }
}
