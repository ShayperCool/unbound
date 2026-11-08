using System;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Collision module's contact and bounce. The 6000.0 Manual (Particle System
    /// Collision module) describes planes as "infinite" and the module's Dampen ("the fraction of
    /// a particle's speed that it loses after a collision"), Bounce ("the fraction of a particle's
    /// speed that it keeps after a collision") and Radius Scale; it gives no contact rule. The rules
    /// asserted here are those Unity 6000.0.71f1 shows in tests/projects/particlerender collide/*
    /// and inherit/collision/*: planes are half-spaces tested where the step ended, the first plane
    /// of the list wins, the rest of the movement and the whole velocity bounce from the contact,
    /// the radius is the size before the step, world sweeps use a slightly smaller sphere, and a
    /// sphere starting inside a collider is pushed out of it.</summary>
    public class ParticleCollisionContactTests
    {
        const float Step = .025f;

        static ParticleSystem Make(Action<ParticleSystem> configure)
        {
            var go = new GameObject("particles");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 10f; main.startSpeed = 0f; main.startSize = .2f;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            var c = ps.collision; c.enabled = true; c.bounce = 1f; c.dampen = 0f; c.lifetimeLoss = 0f;
            configure(ps);
            go.SetActive(true);
            ps.Simulate(0f, true, true, false);
            return ps;
        }

        static Transform Plane(float y)
        {
            var plane = new GameObject("collision plane").transform;
            plane.position = new Vector3(0f, y, 0f);
            return plane;
        }

        static ParticleSystem Planes(params Transform[] planes) => Make(system =>
        {
            var c = system.collision; c.type = ParticleSystemCollisionType.Planes;
            foreach (var plane in planes) c.AddPlane(plane);
        });

        /// <summary>Sets one size-.2 particle (radius .1) and simulates one step.</summary>
        static ParticleSystem.Particle StepOne(ParticleSystem ps, Vector3 position, Vector3 velocity, float age = 0f)
        {
            ps.SetParticles(new[]
            {
                new ParticleSystem.Particle
                {
                    position = position, velocity = velocity, startLifetime = 1f, remainingLifetime = 1f - age,
                    startSize = .2f, startColor = Color.white, randomSeed = 7,
                },
            });
            ps.Simulate(Step, false, false, false);
            var particles = new ParticleSystem.Particle[2];
            Assert.Equal(1, ps.GetParticles(particles));
            return particles[0];
        }

        static void Done(params Component[] objects)
        {
            foreach (var o in objects) if (o != null) Object.DestroyImmediate(o.gameObject);
        }

        static void Near(Vector3 expected, Vector3 actual)
        {
            Assert.True((expected - actual).magnitude < 1e-4f, $"expected {expected:F5}, got {actual:F5}");
        }

        [Fact]
        public void APlaneIsAHalfSpaceThatPushesAParticleBehindItToItsFront()
        {
            var plane = Plane(0f);
            var ps = Planes(plane);
            var c = ps.collision; c.bounce = .5f;
            try
            {
                // Behind the plane and moving away from it: it ends the step at -1.05, its sphere
                // reaches into the plane, and it is put back along its velocity to where the
                // sphere touches the plane from the front (y .1), the rest of the movement (1.15)
                // bouncing up at half its normal part.
                var p = StepOne(ps, new Vector3(0f, -1f, 0f), new Vector3(0f, -2f, 0f));
                Near(new Vector3(0f, .675f, 0f), p.position);
                Near(new Vector3(0f, 1f, 0f), p.velocity);
            }
            finally { Done(ps, plane); }
        }

        [Fact]
        public void TheFirstPlaneOfTheListThatTheParticleReachesWinsNotTheNearest()
        {
            var low = Plane(-1f);
            var high = Plane(-.9f);
            var lowFirst = Planes(low, high);
            var highFirst = Planes(high, low);
            try
            {
                // From -.5 at 20/s the step ends at -1, reaching into both planes.
                Near(new Vector3(0f, -.8f, 0f), StepOne(lowFirst, new Vector3(0f, -.5f, 0f), new Vector3(0f, -20f, 0f)).position);
                Near(new Vector3(0f, -.6f, 0f), StepOne(highFirst, new Vector3(0f, -.5f, 0f), new Vector3(0f, -20f, 0f)).position);
            }
            finally { Done(lowFirst, highFirst, low, high); }
        }

        [Fact]
        public void AParticleMovingAlongAPlaneDoesNotHitIt()
        {
            var plane = Plane(0f);
            var ps = Planes(plane);
            try
            {
                var p = StepOne(ps, new Vector3(0f, .05f, 0f), new Vector3(4f, 0f, 0f));
                Near(new Vector3(.1f, .05f, 0f), p.position);
                Near(new Vector3(4f, 0f, 0f), p.velocity);
            }
            finally { Done(ps, plane); }
        }

        [Fact]
        public void TheWholeVelocityBouncesAndTheAnimatedPartStays()
        {
            var plane = Plane(-1f);
            var ps = Planes(plane);
            var c = ps.collision; c.bounce = .5f;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true;
            velocity.x = 3f; velocity.y = -4f; velocity.z = 0f;
            try
            {
                // Moving at (3, -24) in all, from -.5 to (.075, -1.1); the contact is where the
                // sphere touches the plane on that line (.05, -.9), and the rest of the
                // movement and the whole velocity bounce: (3, 12) in all, the particle's own
                // velocity (0, 16) once the animated (3, -4) is taken off.
                var p = StepOne(ps, new Vector3(0f, -.5f, 0f), new Vector3(0f, -20f, 0f));
                Near(new Vector3(0f, 16f, 0f), p.velocity);
                Near(new Vector3(.075f, -.8f, 0f), p.position);
            }
            finally { Done(ps, plane); }
        }

        [Fact]
        public void TheRadiusIsTheSizeTheParticleHadBeforeTheStep()
        {
            var plane = Plane(-1f);
            var ps = Planes(plane);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            try
            {
                // Size .2 (radius .1) before the step, .195 after it: ending .099 above the plane,
                // the particle hits it with the radius it had before.
                var p = StepOne(ps, new Vector3(0f, -.401f, 0f), new Vector3(0f, -20f, 0f));
                Assert.True(p.velocity.y > 0f);
                // At a distance of .101 nothing is hit.
                var q = StepOne(ps, new Vector3(0f, -.399f, 0f), new Vector3(0f, -20f, 0f));
                Assert.Equal(-20f, q.velocity.y, 4);
            }
            finally { Done(ps, plane); }
        }

        [Fact]
        public void KillSpeedsTakeTheSpeedTheParticleArrivesWithIncludingTheSpeedModifier()
        {
            var plane = Plane(-1f);
            var slowed = Planes(plane);
            var plain = Planes(plane);
            foreach (var ps in new[] { slowed, plain }) { var c = ps.collision; c.minKillSpeed = 3f; }
            var velocity = slowed.velocityOverLifetime; velocity.enabled = true; velocity.speedModifier = .5f;
            try
            {
                // Stored at 8, arriving at 4 with the modifier: alive; at 8 without it: alive too.
                Assert.True(StepOne(slowed, new Vector3(0f, -.9f, 0f), new Vector3(0f, -8f, 0f)).remainingLifetime > 0f);
                // Stored at 4: arriving at 2 with the modifier dies (left at zero life), at 4 without it lives.
                Assert.Equal(0f, StepOne(slowed, new Vector3(0f, -.9f, 0f), new Vector3(0f, -4f, 0f)).remainingLifetime);
                Assert.True(StepOne(plain, new Vector3(0f, -.9f, 0f), new Vector3(0f, -4f, 0f)).remainingLifetime > 0f);
            }
            finally { Done(slowed, plain, plane); }
        }

        static BoxCollider Box(Vector3 center, Vector3 size)
        {
            var box = new GameObject("collision box").AddComponent<BoxCollider>();
            box.transform.position = center;
            box.size = size;
            Physics.SyncTransforms();
            return box;
        }

        static ParticleSystem World() => Make(system =>
        {
            var c = system.collision; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = ParticleSystemCollisionQuality.High;
        });

        [Fact]
        public void ASlantedWorldContactIsSlightlyDeeperThanTheSpheresOwn()
        {
            // The box's top face at y .2; the particle goes from (0, .5) to (.5, 0) at 45 degrees.
            // The sweep's smaller sphere touches the face .2151 lower, and the contact lies .0151
            // before that along the way: y .295576, not the full sphere's .3.
            var box = Box(new Vector3(0f, -.8f, 0f), new Vector3(4f, 2f, 4f));
            var ps = World();
            try
            {
                var p = StepOne(ps, new Vector3(0f, .5f, 0f), new Vector3(20f, -20f, 0f));
                Near(new Vector3(.5f, .591152f, 0f), p.position);
                Near(new Vector3(20f, 20f, 0f), p.velocity);
            }
            finally { Done(ps, box); }
        }

        [Fact]
        public void ANonUniformlyScaledSystemBouncesOffThePlaneNormalCarriedInLikeADirection()
        {
            // As tests/projects/particlerender collide/planes/scaled_y: a Local system turned 90
            // degrees about x and scaled (1, 2.5, 1), a plane turned 20 degrees about x. The
            // plane's up carried into simulation space and normalized is (0, .14407, -.98957); a
            // particle moving at (0, 0, 3) there bounces (0.6) to (0, .684, -1.700). Only the
            // velocity is asserted: the Editor places such planes elsewhere (unresolved).
            var plane = new GameObject("collision plane").transform;
            plane.SetPositionAndRotation(new Vector3(0f, -.5f, 0f), Quaternion.Euler(20f, 0f, 0f));
            var ps = Planes(plane);
            var c = ps.collision; c.bounce = .6f;
            ps.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.Euler(90f, 0f, 0f));
            ps.transform.localScale = new Vector3(1f, 2.5f, 1f);
            try
            {
                // In simulation space, in front of the plane and ending within its reach.
                var p = StepOne(ps, new Vector3(0f, 0f, 1.4f), new Vector3(0f, 0f, 3f));
                Assert.Equal(0f, p.velocity.x, 3);
                Assert.Equal(.684f, p.velocity.y, 2);
                Assert.Equal(-1.700f, p.velocity.z, 2);
            }
            finally { Done(ps, plane); }
        }

        [Fact]
        public void AParticleStartingInsideAColliderReportsANormalPointingIntoIt()
        {
            var box = Box(Vector3.zero, new Vector3(2f, 2f, 2f));
            var ps = World();
            var c = ps.collision; c.sendCollisionMessages = true;
            try
            {
                StepOne(ps, new Vector3(0f, 0f, .6f), new Vector3(0f, -1f, 0f));
                var events = new System.Collections.Generic.List<ParticleCollisionEvent>();
                Assert.Equal(1, ps.GetCollisionEvents(box.gameObject, events));
                Near(new Vector3(0f, 0f, -1f), events[0].normal);
                Near(new Vector3(0f, 0f, 1.1f), events[0].intersection);
            }
            finally { Done(ps, box); }
        }

        [Fact]
        public void AParticleStartingInsideAColliderIsPushedOutOfItsNearestFace()
        {
            var box = Box(Vector3.zero, new Vector3(2f, 2f, 2f));
            var ps = World();
            var c = ps.collision; c.bounce = .5f;
            try
            {
                // Inside the box at z .6, moving down: pushed to touch the z = 1 face from outside
                // (z 1.1), the rest of the movement turned back out with half its normal part.
                var p = StepOne(ps, new Vector3(0f, 0f, .6f), new Vector3(0f, -1f, 0f));
                Near(new Vector3(0f, -.025f, 1.35f), p.position);
                Near(new Vector3(0f, -1f, 0f), p.velocity);
            }
            finally { Done(ps, box); }
        }

        [Fact]
        public void ANewbornAtTheEndOfItsStepTouchingAPlaneDoesNotBounce()
        {
            // A rate of one particle every two steps: the first is due exactly where the second
            // step ends, and has no time in it (as tests/projects/particles collision_planes_kill).
            // A plane facing +z, .1 behind the emitter: the newborn's sphere touches it.
            var plane = Plane(0f);
            plane.SetPositionAndRotation(new Vector3(0f, 0f, -.1f), Quaternion.Euler(90f, 0f, 0f));
            var ps = Make(system =>
            {
                var main = system.main; main.startSpeed = -4f;
                var c = system.collision; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane);
                var emission = system.emission; emission.rateOverTime = 1f / (2f * Step);
            });
            try
            {
                ps.Simulate(Step, false, true, false);
                ps.Simulate(Step, false, false, false);
                var particles = new ParticleSystem.Particle[2];
                Assert.Equal(1, ps.GetParticles(particles));
                Assert.Equal(-4f, particles[0].velocity.z, 4);
            }
            finally { Done(ps, plane); }
        }
    }
}
