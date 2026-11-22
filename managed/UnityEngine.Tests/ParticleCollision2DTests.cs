using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Collision module's World type in Collision2D mode. The 6000.0 Manual (Particle
    /// System Collision module) says only that 2D mode collides with 2D colliders; the rules asserted
    /// here are those Unity 6000.0.71f1 shows in tests/projects/particlerender collide/world2d/*: the
    /// particle's circle meets a shape at Box2D's time of impact (its target separation leaves the
    /// circle 0.005 inside the shape's skin), Box2D's contact at that time pushes it back by
    /// Physics2D.baumgarteScale times its depth past the 0.005 slop, at most
    /// Physics2D.maxLinearCorrection, along the normal out of the shape; a particle that starts
    /// inside meets the shape at once. Triggers, other layers, Dynamic bodies without
    /// enableDynamicColliders, maxCollisionShapes 0 and radii up to 0.0001 collide with nothing.</summary>
    public class ParticleCollision2DTests : IDisposable
    {
        const float Step = .025f;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly float m_Baumgarte = Physics2D.baumgarteScale, m_Correction = Physics2D.maxLinearCorrection;

        public ParticleCollision2DTests()
        {
            Physics2D.defaultContactOffset = 0.01f;
            Physics2D.simulationMode = SimulationMode2D.Script;
        }

        public void Dispose()
        {
            Physics2D.baumgarteScale = m_Baumgarte;
            Physics2D.maxLinearCorrection = m_Correction;
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            Physics2D.Simulate(.02f);
        }

        ParticleSystem Make(Action<ParticleSystem.CollisionModule> configure = null)
        {
            var go = new GameObject("particles");
            m_Made.Add(go);
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 10f; main.startSpeed = 0f; main.startSize = .2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision2D; c.quality = ParticleSystemCollisionQuality.High;
            c.bounce = 1f; c.dampen = 0f; c.lifetimeLoss = 0f;
            configure?.Invoke(c);
            go.SetActive(true);
            ps.Simulate(0f, true, true, false);
            return ps;
        }

        T Collider<T>(Vector3 position, float angle = 0f) where T : Collider2D
        {
            var go = new GameObject("collider");
            m_Made.Add(go);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            return go.AddComponent<T>();
        }

        BoxCollider2D Box(Vector3 position, Vector2 size)
        {
            var box = Collider<BoxCollider2D>(position);
            box.size = size;
            return box;
        }

        /// <summary>One size-.2 particle (radius .1), one step.</summary>
        static ParticleSystem.Particle StepOne(ParticleSystem ps, Vector3 position, Vector3 velocity)
        {
            ps.SetParticles(new[]
            {
                new ParticleSystem.Particle
                {
                    position = position, velocity = velocity, startLifetime = 1f, remainingLifetime = 1f,
                    startSize = .2f, startColor = Color.white, randomSeed = 7,
                },
            });
            ps.Simulate(Step, false, false, false);
            var particles = new ParticleSystem.Particle[2];
            Assert.Equal(1, ps.GetParticles(particles));
            return particles[0];
        }

        static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.True((expected - actual).magnitude < tolerance, $"expected {expected:F5}, got {actual:F5}");
        }

        [Fact]
        public void ABoxIsMetWhereTheCircleIsInsideItsSkinThenPushedBack()
        {
            // Top face at y .1, skin .01: the time of impact leaves the circle's center .095 above
            // the face; the contact's separation -.015 pushes it back by .2 * .01 to .097 above.
            Box(new Vector3(0f, 0f, 3f), new Vector2(4f, .2f));
            var ps = Make();
            var p = StepOne(ps, new Vector3(0f, .3f, 0f), new Vector3(0f, -10f, 0f));
            float contact = .1f + .097f;
            Near(new Vector3(0f, contact + (contact - .05f), 0f), p.position, 2e-4f);
            Near(new Vector3(0f, 10f, 0f), p.velocity);
        }

        [Fact]
        public void ACircleColliderIsMetAtTheSumOfTheRadiiLessTheSlop()
        {
            // Circle .5 and particle .1: impact at .585 between centers, pushed back by .002.
            var circle = Collider<CircleCollider2D>(Vector3.zero);
            circle.radius = .5f;
            var ps = Make();
            var p = StepOne(ps, new Vector3(0f, .9f, 0f), new Vector3(0f, -20f, 0f));
            Near(new Vector3(0f, .587f + (.587f - .4f), 0f), p.position, 2e-4f);
        }

        [Fact]
        public void AParticleInsideABoxIsPushedOutThroughItsNearestFaceKeepingItsVelocity()
        {
            // Nearest face x = 1, .5 away: separation -.61, pushed .2 * .605 along +x; the bounce
            // reflects that push (bounce .5) and leaves the velocity along the face as it is.
            Box(Vector3.zero, new Vector2(2f, 2f));
            var ps = Make(c => c.bounce = .5f);
            var p = StepOne(ps, new Vector3(.5f, 0f, 0f), new Vector3(0f, -1f, 0f));
            Near(new Vector3(.5f + .121f * 1.5f, -1f * Step, 0f), p.position);
            Near(new Vector3(0f, -1f, 0f), p.velocity);
        }

        [Fact]
        public void ThePushTakesTheBaumgarteScaleUpToTheMaximumCorrection()
        {
            // .2 inside the face x = 1: separation -.31, pushed .4 * .305 (bounce 0: once).
            Box(Vector3.zero, new Vector2(2f, 2f));
            var ps = Make(c => c.bounce = 0f);
            Physics2D.baumgarteScale = .4f;
            Near(new Vector3(.8f + .122f, -1f * Step, 0f), StepOne(ps, new Vector3(.8f, 0f, 0f), new Vector3(0f, -1f, 0f)).position);
            Physics2D.maxLinearCorrection = .05f;
            Near(new Vector3(.8f + .05f, -1f * Step, 0f), StepOne(ps, new Vector3(.8f, 0f, 0f), new Vector3(0f, -1f, 0f)).position);
        }

        [Fact]
        public void TriggersOtherLayersExcludedDynamicBodiesAndTinyCirclesAreNotHit()
        {
            var from = new Vector3(0f, .3f, 0f);
            var velocity = new Vector3(0f, -10f, 0f);
            var passed = from + velocity * Step;

            var trigger = Box(Vector3.zero, new Vector2(4f, .2f));
            trigger.isTrigger = true;
            Near(passed, StepOne(Make(), from, velocity).position);
            trigger.isTrigger = false;
            Assert.NotEqual(passed.y, StepOne(Make(), from, velocity).position.y, 3);

            trigger.gameObject.layer = 2;
            Near(passed, StepOne(Make(c => c.collidesWith = ~(1 << 2)), from, velocity).position);
            trigger.gameObject.layer = 0;

            var body = trigger.gameObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            Near(passed, StepOne(Make(c => c.enableDynamicColliders = false), from, velocity).position);
            Assert.NotEqual(passed.y, StepOne(Make(), from, velocity).position.y, 3);
            body.bodyType = RigidbodyType2D.Kinematic;
            Assert.NotEqual(passed.y, StepOne(Make(c => c.enableDynamicColliders = false), from, velocity).position.y, 3);

            Near(passed, StepOne(Make(c => c.maxCollisionShapes = 0), from, velocity).position);
            // Size .2 times .0009 halved: radius .00009.
            Near(passed, StepOne(Make(c => c.radiusScale = .0009f), from, velocity).position);
        }

        [Fact]
        public void TheQueryAllocatesNothingOnceWarm()
        {
            Box(new Vector3(0f, 0f, 3f), new Vector2(4f, .2f));
            Collider<CircleCollider2D>(new Vector3(3f, 0f, 0f)).radius = .5f;
            var capsule = Collider<CapsuleCollider2D>(new Vector3(-3f, 0f, 0f), 30f);
            capsule.size = new Vector2(.4f, 2f);
            Collider<EdgeCollider2D>(new Vector3(6f, 0f, 0f)).points = new[] { new Vector2(-1f, 0f), new Vector2(1f, .3f) };
            var owner = new GameObject("owner");
            m_Made.Add(owner);
            var scratch = new Unbound.Runtime.Physics2DParticleQuery.Scratch();
            var xs = new[] { 0f, 3f, -3f, 6f, .5f };
            void Run()
            {
                foreach (float x in xs)
                    Unbound.Runtime.Physics2DParticleQuery.Contact(owner, new Vector2(x, .8f), new Vector2(x, -.05f), .1f, -1, true, 256, scratch,
                        out _, out _, out _, out _, out _);
            }
            Run();
            AllocationMeasurement.EmptyAllocationContext();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++) Run();
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }

        [Fact]
        public void ColliderForcePushesADynamicBodyAlongTheParticlesMovement()
        {
            var box = Box(Vector3.zero, new Vector2(4f, .2f));
            var body = box.gameObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.useAutoMass = false;
            body.mass = 2f;
            var ps = Make(c => { c.colliderForce = 3f; c.multiplyColliderForceByParticleSpeed = true; });
            StepOne(ps, new Vector3(0f, .3f, 0f), new Vector3(0f, -10f, 0f));
            Physics2D.Simulate(.02f);
            // Force 3 * speed 10 for one .02 s step on mass 2, head-on at the center: no spin.
            Assert.Equal(-.3f, body.linearVelocity.y, 4);
            Assert.Equal(0f, body.linearVelocity.x, 5);
            Assert.Equal(0f, body.angularVelocity, 3);
        }
    }
}
