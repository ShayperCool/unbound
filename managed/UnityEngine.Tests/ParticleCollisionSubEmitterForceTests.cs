using System;
using System.Linq;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Collision and Trigger sub-emitters and CollisionModule.colliderForce (6000.0
    /// Scripting API: ParticleSystemSubEmitterType.Collision "Spawns new particles when particles
    /// from the parent system collide with something", Trigger "... when particles from the
    /// parent system pass conditions in the Trigger Module"; SubEmittersModule.collision0/1 spawn
    /// "at the locations of the collision"; AddSubEmitter's emitProbability "0 is never and 1 is
    /// always"; colliderForce "How much force is applied to a Collider when hit by particles",
    /// with no formula, force mode or direction, so Unbound reports it instead of guessing one).
    /// Nothing here assumes an order of emission: Unity does not document one.</summary>
    public class ParticleCollisionSubEmitterForceTests
    {
        const string ColliderForce = "ParticleSystem.CollisionModule.colliderForce";
        static readonly Color32 White = new Color32(255, 255, 255, 255), Red = new Color32(255, 0, 0, 255);

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        /// <summary>A still, non-emitting system on a fresh object, configured before it becomes
        /// active.</summary>
        static ParticleSystem Make(Action<ParticleSystem> configure)
        {
            var go = new GameObject("particles");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 10f; main.startSpeed = 0f; main.startSize = .2f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            configure(ps);
            go.SetActive(true);
            return ps;
        }

        /// <summary>A child that emits one size-1 white particle per event, from its burst.</summary>
        static ParticleSystem Child() => Make(system =>
        {
            var main = system.main; main.startSize = 1f; main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        });

        static ParticleSystem.Particle At(Vector3 position, Vector3 velocity, Color32 color) => new ParticleSystem.Particle
        {
            position = position, velocity = velocity, startLifetime = 10f, remainingLifetime = 10f,
            startSize = .2f, startColor = color, randomSeed = 7,
        };

        static ParticleSystem.Particle[] Live(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount + 1];
            int n = ps.GetParticles(particles);
            return particles.Take(n).ToArray();
        }

        static void Done(params Object[] objects)
        {
            foreach (var o in objects) if (o != null) Object.DestroyImmediate(o);
        }

        /// <summary>A plane at y -1 and a system colliding with it, the child parented under it
        /// as a sub-emitter of <paramref name="addSub"/>. Its one particle, size .2 at y -.5
        /// moving down at 20/s, reaches the plane after .4 of a .025 s step, its centre at y -.9
        /// (the plane plus its radius .1).</summary>
        static (ParticleSystem ps, ParticleSystem child, Transform plane) FallOntoPlane(Action<ParticleSystem.SubEmittersModule, ParticleSystem> addSub)
        {
            var plane = new GameObject("collision plane").transform;
            plane.position = new Vector3(0f, -1f, 0f);
            var child = Child();
            var ps = Make(system =>
            {
                var c = system.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane);
                var subs = system.subEmitters; subs.enabled = true;
                addSub(subs, child);
            });
            child.transform.SetParent(ps.transform, false);
            ps.Simulate(0f, true, true, false);
            ps.SetParticles(new[] { At(new Vector3(0f, -.5f, 0f), new Vector3(0f, -20f, 0f), Red) });
            return (ps, child, plane);
        }

        [Fact]
        public void ACollisionSubEmitterEmitsAtTheCollisionPointWithTheInheritedColorAndSize()
        {
            var (ps, child, plane) = FallOntoPlane((subs, c) => subs.AddSubEmitter(c, ParticleSystemSubEmitterType.Collision,
                ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize));
            try
            {
                Assert.Equal(0, child.particleCount);
                ps.Simulate(.025f, false, false, false);
                // The collision happened: the parent bounced off the plane.
                var parent = Assert.Single(Live(ps));
                Assert.True(parent.velocity.y > 0f);
                var emitted = Assert.Single(Live(child));
                Assert.Equal(0f, emitted.position.x, 4);
                Assert.Equal(-.9f, emitted.position.y, 4);
                Assert.Equal(0f, emitted.position.z, 4);
                // InheritColor: white × the parent's red; InheritSize: 1 × the parent's .2.
                Assert.Equal(Red, emitted.startColor);
                Assert.Equal(.2f, emitted.startSize3D.x, 5);
                Assert.Equal(.2f, emitted.startSize3D.y, 5);
                Assert.Equal(.2f, emitted.startSize3D.z, 5);
            }
            finally { Done(ps.gameObject, plane.gameObject); }
        }

        [Fact]
        public void ACollisionSubEmitterWithEmitProbabilityZeroEmitsNothing()
        {
            var (ps, child, plane) = FallOntoPlane((subs, c) => subs.AddSubEmitter(c, ParticleSystemSubEmitterType.Collision,
                ParticleSystemSubEmitterProperties.InheritNothing, 0f));
            try
            {
                ps.Simulate(.025f, false, false, false);
                Assert.True(Assert.Single(Live(ps)).velocity.y > 0f);
                Assert.Equal(0, child.particleCount);
            }
            finally { Done(ps.gameObject, plane.gameObject); }
        }

        /// <summary>A radius-.5 sphere trigger at z 1.5 (as in the trigger tests) and a system
        /// whose one still particle sits at its centre, with the given Inside action and a
        /// Trigger sub-emitter.</summary>
        static (ParticleSystem ps, ParticleSystem child, GameObject sphere) InsideTrigger(ParticleSystemOverlapAction inside)
        {
            var center = new Vector3(0f, 0f, 1.5f);
            var sphere = new GameObject("trigger sphere");
            sphere.transform.position = center;
            sphere.AddComponent<SphereCollider>().isTrigger = true;
            Physics.SyncTransforms();
            var child = Child();
            var ps = Make(system =>
            {
                var trigger = system.trigger; trigger.enabled = true; trigger.AddCollider(sphere.GetComponent<Collider>());
                trigger.inside = inside;
                trigger.outside = trigger.enter = trigger.exit = ParticleSystemOverlapAction.Ignore;
                var subs = system.subEmitters; subs.enabled = true;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Trigger, ParticleSystemSubEmitterProperties.InheritNothing);
            });
            child.transform.SetParent(ps.transform, false);
            ps.Simulate(0f, true, true, false);
            ps.SetParticles(new[] { At(center, Vector3.zero, White) });
            return (ps, child, sphere);
        }

        [Fact]
        public void ATriggerSubEmitterEmitsWhereTheParticleMeetsACallbackCondition()
        {
            var (ps, child, sphere) = InsideTrigger(ParticleSystemOverlapAction.Callback);
            try
            {
                ps.Simulate(.025f, false, false, false);
                Assert.Single(Live(ps));
                var emitted = Assert.Single(Live(child));
                Assert.Equal(0f, emitted.position.x, 4);
                Assert.Equal(0f, emitted.position.y, 4);
                Assert.Equal(1.5f, emitted.position.z, 4);
            }
            finally { Done(ps.gameObject, sphere); }
        }

        [Fact]
        public void ATriggerSubEmitterDoesNotEmitForAnIgnoredCondition()
        {
            var (ps, child, sphere) = InsideTrigger(ParticleSystemOverlapAction.Ignore);
            try
            {
                ps.Simulate(.025f, false, false, false);
                Assert.Single(Live(ps));
                Assert.Equal(0, child.particleCount);
            }
            finally { Done(ps.gameObject, sphere); }
        }

        /// <summary>A unit box collider centred at x 1 (its face at x .5), with or without a
        /// Rigidbody, and a World-collision system whose one size-.2 particle at x .2 moves
        /// towards it at 20/s and meets it within a .025 s step.</summary>
        static (ParticleSystem ps, GameObject box) TowardsBox(bool body, bool kinematic, float colliderForce)
        {
            var box = new GameObject("collision box");
            box.transform.position = new Vector3(1f, 0f, 0f);
            box.AddComponent<BoxCollider>();
            if (body)
            {
                var rb = box.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = kinematic;
            }
            Physics.SyncTransforms();
            var ps = Make(system =>
            {
                var c = system.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World;
                c.mode = ParticleSystemCollisionMode.Collision3D; c.quality = ParticleSystemCollisionQuality.High;
                c.colliderForce = colliderForce;
            });
            ps.Simulate(0f, true, true, false);
            ps.SetParticles(new[] { At(new Vector3(.2f, 0f, 0f), new Vector3(20f, 0f, 0f), White) });
            return (ps, box);
        }

        [Fact]
        public void AColliderForceCollisionWithAPushableRigidbodyIsReported()
        {
            var (ps, box) = TowardsBox(body: true, kinematic: false, colliderForce: 5f);
            try
            {
                int before = Hits(ColliderForce);
                ps.Simulate(.025f, false, false, false);
                Assert.True(Assert.Single(Live(ps)).velocity.x < 0f);
                Assert.Equal(before + 1, Hits(ColliderForce));
            }
            finally { Done(ps.gameObject, box); }
        }

        [Theory]
        [InlineData(false, false, 5f)]
        [InlineData(true, true, 5f)]
        [InlineData(true, false, 0f)]
        public void NoColliderForceIsReportedWithoutABodyItWouldPush(bool body, bool kinematic, float colliderForce)
        {
            var (ps, box) = TowardsBox(body, kinematic, colliderForce);
            try
            {
                int before = Hits(ColliderForce);
                ps.Simulate(.025f, false, false, false);
                Assert.True(Assert.Single(Live(ps)).velocity.x < 0f);
                Assert.Equal(before, Hits(ColliderForce));
            }
            finally { Done(ps.gameObject, box); }
        }
    }
}
