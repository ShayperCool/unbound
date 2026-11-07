using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A particle whose trail outlives it (TrailModule.dieWithParticles off): Unity
    /// 6000.0.71f1 keeps the dead particle in the buffer at zero remaining lifetime and at rest while
    /// its trail fades, does not draw it, and removes it with the dead of the first update that starts
    /// with its trail spent (tests/projects/particletrails). Trails are drawn in particle order; the
    /// ratio selects particles by a seeded draw; a collision kill shows zero lifetime for one update.</summary>
    public class ParticleTrailCarrierTests
    {
        /// <summary>One internal step per Simulate call (below Time.maximumParticleDeltaTime).</summary>
        const float Step = .025f;

        /// <summary>Four particles at 0 s leaving a 0.2 circle outwards at 2 units/s, living 0.4 to
        /// 0.9 s, with trails of half their start lifetime (the probe's setup).</summary>
        static ParticleSystem Make(string name, bool dieWithParticles, System.Action<ParticleSystem> configure = null)
        {
            var go = new GameObject(name); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false; ps.randomSeed = 4242u;
            var m = ps.main; m.playOnAwake = false; m.duration = 1f; m.loop = false;
            m.startLifetime = new ParticleSystem.MinMaxCurve(.4f, .9f); m.startSpeed = 2f; m.startSize = .2f;
            var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = .2f;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)4) });
            var t = ps.trails; t.enabled = true; t.lifetime = .5f; t.minVertexDistance = .07f; t.dieWithParticles = dieWithParticles;
            configure?.Invoke(ps);
            go.SetActive(true);
            return ps;
        }

        static ParticleSystem.Particle[] Particles(ParticleSystem ps)
        {
            var buffer = new ParticleSystem.Particle[ps.main.maxParticles];
            return buffer.Take(ps.GetParticles(buffer)).ToArray();
        }

        static void Simulate(ParticleSystem ps, int steps, bool restart = false)
        {
            for (int i = 0; i < steps; i++) ps.Simulate(Step, true, restart && i == 0, false);
        }

        [Fact]
        public void ADeadParticleStaysAtRestUntilTheUpdateAfterItsTrailIsSpent()
        {
            var ps = Make("trail-carrier", dieWithParticles: false);
            try
            {
                Simulate(ps, 1, restart: true);
                uint seed = Particles(ps).OrderBy(p => p.startLifetime).First().randomSeed;
                // Step until the shortest-lived particle has died: it stays, at zero lifetime and at rest.
                while (Particles(ps).First(p => p.randomSeed == seed).remainingLifetime > 0f) Simulate(ps, 1);
                Simulate(ps, 1);
                var carrier = Particles(ps).Single(p => p.randomSeed == seed);
                Assert.True(carrier.IsTrailCarrier);
                Assert.Equal(0f, carrier.remainingLifetime);
                Assert.Equal(Vector3.zero, carrier.velocity);
                var position = carrier.position;
                var trail = ps.m_TrailList[carrier.m_TrailId - 1];

                // It holds its place while the trail fades, and is still there in the update that
                // spends the trail; the next update removes it.
                int guard = 0;
                while (!trail.Spent && guard++ < 80)
                {
                    Simulate(ps, 1);
                    var p = Particles(ps).Single(q => q.randomSeed == seed);
                    Assert.Equal(position, p.position);
                    Assert.Equal(0f, p.remainingLifetime);
                }
                Assert.True(trail.Spent);
                Assert.Empty(trail.Points);
                Simulate(ps, 1);
                Assert.DoesNotContain(Particles(ps), p => p.randomSeed == seed);
                Assert.DoesNotContain(ps.m_TrailList, t => ReferenceEquals(t, trail));
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void WithTrailsDyingWithTheirParticlesADeadParticleLeavesAtOnce()
        {
            var ps = Make("trail-carrier-control", dieWithParticles: true);
            try
            {
                Simulate(ps, 1, restart: true);
                for (int i = 0; i < 40; i++)
                {
                    Simulate(ps, 1);
                    Assert.DoesNotContain(Particles(ps), p => p.IsTrailCarrier);
                    Assert.Equal(ps.particleCount, ps.m_TrailList.Count(t => t != null));
                }
                Assert.Equal(0, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ACarrierIsNotDrawnButItsTrailIs()
        {
            var ps = Make("trail-carrier-draw", dieWithParticles: false);
            var cameraObject = new GameObject("trail-carrier-camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -6);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                Simulate(ps, 1, restart: true);
                while (!Particles(ps).Any(p => p.IsTrailCarrier)) Simulate(ps, 1);
                int live = Particles(ps).Count(p => !p.IsTrailCarrier);
                var mesh = new Mesh();
                renderer.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                Assert.Equal(live * 4, mesh.vertexCount);
                var trails = new Mesh();
                renderer.BakeTrailsMesh(trails, camera, ParticleSystemBakeMeshOptions.Default);
                Assert.Equal(ps.particleCount, ps.RenderTrails().Count(t => t.Points.Count > 0));

                // Only carriers left: no particle draws.
                while (Particles(ps).Any(p => !p.IsTrailCarrier)) Simulate(ps, 1);
                Assert.NotEqual(0, ps.particleCount);
                renderer.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                Assert.Equal(0, mesh.vertexCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); Object.DestroyImmediate(cameraObject); }
        }

        [Fact]
        public void TrailsDrawInTheOrderOfTheirParticles()
        {
            // A death moves the last particle into the dead one's slot; its trail moves with it.
            var ps = Make("trail-order", dieWithParticles: true, s =>
            {
                var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)4), new ParticleSystem.Burst(.3f, (short)2) });
            });
            try
            {
                Simulate(ps, 1, restart: true);
                bool reordered = false;
                for (int i = 0; i < 40; i++)
                {
                    Simulate(ps, 1);
                    var particles = Particles(ps);
                    var links = particles.Select(p => p.m_TrailId).ToList();
                    reordered |= links.Count > 1 && !links.SequenceEqual(links.OrderBy(link => link));
                    Assert.Equal(links.Select(link => ps.m_TrailList[link - 1]), ps.RenderTrails());
                }
                Assert.True(reordered);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // Seeds of Native particles at ratios 0.5 and 0.25 (tests/projects/particletrails
        // pp/ratiobig): with a trail at both, at 0.5 only, at neither.
        static readonly uint[] s_BothRatios = { 2664058130u, 699159166u, 2437877602u, 2836233836u };
        static readonly uint[] s_HalfOnly = { 2539677753u, 4242320660u, 2877603622u, 873098892u };
        static readonly uint[] s_Neither = { 2433260606u, 2341488215u, 621223986u, 2642115131u };

        [Fact]
        public void TheRatioSelectsTheMeasuredParticles()
        {
            Assert.All(s_BothRatios, seed => { Assert.True(ParticleSystem.GetsTrail(seed, .25f)); Assert.True(ParticleSystem.GetsTrail(seed, .5f)); });
            Assert.All(s_HalfOnly, seed => { Assert.False(ParticleSystem.GetsTrail(seed, .25f)); Assert.True(ParticleSystem.GetsTrail(seed, .5f)); });
            Assert.All(s_Neither, seed => { Assert.False(ParticleSystem.GetsTrail(seed, .25f)); Assert.False(ParticleSystem.GetsTrail(seed, .5f)); });
            Assert.All(s_Neither.Concat(s_HalfOnly), seed => Assert.True(ParticleSystem.GetsTrail(seed, 1f)));
            Assert.All(s_BothRatios, seed => Assert.False(ParticleSystem.GetsTrail(seed, 0f)));
        }

        [Fact]
        public void ParticlesWithoutATrailLeaveAtDeathAndTheOthersCarryTheirs()
        {
            var ps = Make("trail-ratio", dieWithParticles: false, s =>
            {
                var m = s.main; m.startLifetime = .5f; m.maxParticles = 200;
                var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)100) });
                var t = s.trails; t.ratio = .5f;
            });
            try
            {
                Simulate(ps, 1, restart: true);
                var born = Particles(ps);
                var withTrail = born.Where(p => p.m_TrailId != 0).Select(p => p.randomSeed).ToHashSet();
                Assert.Equal(born.Where(p => ParticleSystem.GetsTrail(p.randomSeed, .5f)).Select(p => p.randomSeed).ToHashSet(), withTrail);
                Assert.InRange(withTrail.Count, 25, 75);
                Simulate(ps, 22);
                var kept = Particles(ps);
                Assert.All(kept, p => Assert.True(p.IsTrailCarrier));
                Assert.Equal(withTrail, kept.Select(p => p.randomSeed).ToHashSet());
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ACollisionKillShowsZeroLifetimeAndItsBounceForOneUpdate()
        {
            var plane = new GameObject("trail-collision-plane");
            plane.transform.position = new Vector3(0f, -.5f, 0f);
            // Measured with a fading trail, trails dying with their particles, no trails, and a kill
            // by speed (tests/projects/particletrails pp/collision/*).
            foreach (var (die, trails, speedKill) in new[] { (false, true, false), (true, true, false), (true, false, false), (true, false, true) })
            {
                var ps = Make("trail-collision", die, s =>
                {
                    var m = s.main; m.startLifetime = 2f;
                    var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.SetPlane(0, plane.transform);
                    c.bounce = 1f; c.dampen = 0f;
                    if (speedKill) c.minKillSpeed = 3f; else c.lifetimeLoss = 1f;
                    var t = s.trails; t.enabled = trails;
                });
                try
                {
                    Simulate(ps, 1, restart: true);
                    int guard = 0;
                    while (!Particles(ps).Any(p => p.remainingLifetime == 0f) && guard++ < 80) Simulate(ps, 1);
                    var killed = Particles(ps).First(p => p.remainingLifetime == 0f);
                    Assert.False(killed.IsTrailCarrier);
                    Assert.True(killed.velocity.y > 0f, "bounced off the plane");
                    Simulate(ps, 1);
                    var after = Particles(ps).Where(p => p.randomSeed == killed.randomSeed).ToList();
                    if (die) Assert.Empty(after);
                    else
                    {
                        // Unmoved, at rest, carrying its fading trail.
                        var carrier = Assert.Single(after);
                        Assert.True(carrier.IsTrailCarrier);
                        Assert.Equal(killed.position, carrier.position);
                        Assert.Equal(Vector3.zero, carrier.velocity);
                    }
                }
                finally { Object.DestroyImmediate(ps.gameObject); }
            }
            Object.DestroyImmediate(plane);
        }
    }
}
