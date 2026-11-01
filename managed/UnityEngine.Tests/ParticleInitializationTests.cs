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
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PlaybackSnapshotRewindsEveryLaneAndCanBeReused(bool beforeFirstEmission)
        {
            var ps = Make();
            try
            {
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                m.startSize3D = true;
                m.startSizeX = new ParticleSystem.MinMaxCurve(.5f, 1.5f);
                m.startSizeY = new ParticleSystem.MinMaxCurve(1f, 2f);
                m.startSizeZ = new ParticleSystem.MinMaxCurve(2f, 3f);
                if (!beforeFirstEmission) ps.Emit(1);
                var saved = ps.GetPlaybackState();
                ps.Clear(false);
                ps.Emit(4);
                var expected = Read(ps);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    ps.SetPlaybackState(saved);
                    ps.Clear(false);
                    ps.Emit(4);
                    var actual = Read(ps);
                    for (int lane = 0; lane < 4; lane++)
                    {
                        Assert.Equal(expected[lane].randomSeed, actual[lane].randomSeed);
                        Assert.Equal(expected[lane].startLifetime, actual[lane].startLifetime);
                        Assert.Equal(expected[lane].startSize3D, actual[lane].startSize3D);
                    }
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void PlaybackSnapshotReplaysTheNextSingleBirth()
        {
            var ps = Make();
            try
            {
                ps.Emit(1);
                var saved = ps.GetPlaybackState();
                ps.Emit(1);
                ps.SetPlaybackState(saved);
                ps.Emit(1);
                var p = Read(ps);
                Assert.Equal(1622567041u, p[1].randomSeed);
                Assert.Equal(p[1].randomSeed, p[2].randomSeed);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ObsoleteParticleImportMaterializesAxisBeforeCopying(bool existingParticle)
        {
            var ps = Make();
            try
            {
                if (existingParticle) ps.Emit(1);
#pragma warning disable CS0618
                ps.Emit(new ParticleSystem.Particle {
                    startLifetime = 2f, remainingLifetime = 2f,
                    axisOfRotation = Vector3.right, randomSeed = 77u });
#pragma warning restore CS0618
                var p = Read(ps);
                if (existingParticle) Assert.Equal(Vector3.up, p[0].axisOfRotation);
                Assert.Equal(Vector3.right, p[existingParticle ? 1 : 0].axisOfRotation);
                ps.Emit(1);
                Assert.Equal(Vector3.forward, Read(ps)[existingParticle ? 2 : 1].axisOfRotation);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(10000000)]
        [InlineData(int.MaxValue)]
        public void HugeRejectedTailDoesNotAllocateRequestSizedStorage(int requested)
        {
            var ps = Make();
            try
            {
                var m = ps.main; m.maxParticles = 1;
                // Warm both normal initialization and the jump tables before measuring.
                ps.Emit(129); ps.Clear(false); ps.randomSeed = 12345u;
                long before = GC.GetAllocatedBytesForCurrentThread();
                ps.Emit(requested);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                var p = Read(ps);
                Assert.Single(p);
                Assert.Equal(692788716u, p[0].randomSeed);
                Assert.InRange(allocated, 0L, 65536L);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void CapacityTruncationKeepsRequestedBatchDrawsAndAdmittedFields(bool external, bool threeD)
        {
            var limited = Make(); var complete = Make();
            try
            {
                var lm = limited.main; lm.maxParticles = 1;
                var cm = complete.main; cm.maxParticles = 256;
                lm.startSize3D = cm.startSize3D = threeD;
                lm.startRotation3D = cm.startRotation3D = threeD;
                lm.startLifetime = cm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                lm.startColor = cm.startColor = new ParticleSystem.MinMaxGradient(Color.black, Color.white);
                var ep = new ParticleSystem.EmitParams { startSize = .625f };
                if (external) { limited.Emit(ep, 129); complete.Emit(ep, 129); }
                else { limited.Emit(129); complete.Emit(129); }
                var first = Read(limited)[0]; var expected = Read(complete)[0];
                Assert.Equal(expected.randomSeed, first.randomSeed);
                Assert.Equal(expected.startLifetime, first.startLifetime);
                Assert.Equal(expected.startColor, first.startColor);
                limited.Clear(false); complete.Clear(false);
                lm.maxParticles = 4;
                limited.Emit(4); complete.Emit(4);
                var actual = Read(limited); var next = Read(complete);
                for (int lane = 0; lane < 4; lane++) Assert.Equal(next[lane].randomSeed, actual[lane].randomSeed);
            }
            finally { Object.DestroyImmediate(limited.gameObject); Object.DestroyImmediate(complete.gameObject); }
        }

        [Theory]
        [InlineData(132, false)]
        [InlineData(516, true)]
        public void LargeBatchAdvancesLikeRepeatedCompletePackedGroups(int count, bool threeD)
        {
            var bulk = Make(); var groups = Make();
            try
            {
                var bm = bulk.main; var gm = groups.main;
                bm.maxParticles = gm.maxParticles = count + 4;
                bm.startSize3D = gm.startSize3D = threeD;
                bm.startRotation3D = gm.startRotation3D = threeD;
                bm.startLifetime = gm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                bulk.Emit(count);
                for (int i = 0; i < count; i += 4) groups.Emit(4);
                bulk.Emit(4); groups.Emit(4);
                var expected = Read(groups); var actual = Read(bulk);
                for (int i = 0; i < count + 4; i++)
                {
                    Assert.Equal(expected[i].randomSeed, actual[i].randomSeed);
                    Assert.Equal(expected[i].startLifetime, actual[i].startLifetime);
                }
            }
            finally { Object.DestroyImmediate(bulk.gameObject); Object.DestroyImmediate(groups.gameObject); }
        }
    }
}
