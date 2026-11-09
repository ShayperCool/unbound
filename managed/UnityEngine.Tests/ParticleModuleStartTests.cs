using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Start-time module rules measured in Unity 6000.0.71f1: Inherit Velocity (constant
    /// modes once at birth, curve modes over the life), Lifetime by Emitter Speed's factor and its
    /// floor, a custom simulation space keeping the start speed, and the playback time of a system
    /// whose start delay is still running (tests/projects/particlerandom inherit/*, lbes/*;
    /// particlegravity custom/*, delay/*).</summary>
    public sealed class ParticleModuleStartTests : IDisposable
    {
        readonly string m_Version = ProjectInfo.UnityVersion;

        public ParticleModuleStartTests() => ProjectInfo.UnityVersion = "6000.0.71f1";

        public void Dispose() => ProjectInfo.UnityVersion = m_Version;

        static ParticleSystem Make(Action<ParticleSystem> configure) => ParticleEventSubEmitterTests.Make(ps =>
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = 12345u;
            var m = ps.main; m.maxParticles = 64; m.startLifetime = 10f; m.startSpeed = 2f;
            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)6) });
            configure(ps);
        });

        static ParticleSystem.Particle[] Read(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            ps.GetParticles(particles);
            return particles;
        }

        static readonly Vector3 EmitterVelocity = new Vector3(2f, 1f, -3f);

        static ParticleSystem Inheriting(ParticleSystemInheritVelocityMode mode, ParticleSystem.MinMaxCurve curve) => Make(ps =>
        {
            var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = EmitterVelocity;
            var iv = ps.inheritVelocity; iv.enabled = true; iv.mode = mode; iv.curve = curve;
        });

        [Fact]
        public void InitialConstantsAddToTheStartVelocityOnce()
        {
            var ps = Inheriting(ParticleSystemInheritVelocityMode.Initial, new ParticleSystem.MinMaxCurve(0.2f, 1.5f));
            try
            {
                ps.Simulate(0.05f, true, true, false);
                foreach (var p in Read(ps))
                {
                    var inherited = p.velocity - new Vector3(0f, 0f, 2f);
                    float k = inherited.magnitude / EmitterVelocity.magnitude;
                    Assert.InRange(k, 0.2f - 1e-4f, 1.5f + 1e-4f);
                    Assert.True(Vector3.Cross(inherited, EmitterVelocity).magnitude < 1e-4f, p.velocity.ToString());
                    Assert.Equal(Vector3.zero, p.animatedVelocity);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void InitialCurvesScaleTheBirthVelocityOverTheLife()
        {
            var curve = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0.5f));
            var ps = Inheriting(ParticleSystemInheritVelocityMode.Initial, curve);
            try
            {
                ps.Simulate(0.05f, true, true, false);
                // The emitter stops: the birth velocity stays with the particles.
                var main = ps.main; main.emitterVelocity = Vector3.zero;
                ps.Simulate(0.05f, true, false, false);
                foreach (var p in Read(ps))
                {
                    Assert.Equal(new Vector3(0f, 0f, 2f), p.velocity);
                    Assert.True((p.animatedVelocity - EmitterVelocity * 0.5f).magnitude < 1e-5f, p.animatedVelocity.ToString());
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        static ParticleSystem Colliding(Transform plane, Action<ParticleSystem.CollisionModule> configure) => Make(ps =>
        {
            var m = ps.main; m.startSpeed = 2f; m.startSize = 0.02f; m.startLifetime = 2f;
            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)6) });
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.SetPlane(0, plane);
            configure(c);
        });

        static Transform Plane()
        {
            var plane = new GameObject("plane").transform;
            plane.SetPositionAndRotation(new Vector3(0f, 0f, 0.3f), Quaternion.Euler(-90f, 0f, 0f));
            return plane;
        }

        [Fact]
        public void HitsOfOnePassTakeTheCollisionLanesInOrder()
        {
            // Six identical particles hit in the same pass: hit j bounces with the first draw of
            // lane j mod 4 (lanes seeded with the system seed plus 367 per lane), the fifth and
            // sixth with the lanes' second draw (a second group of four).
            var plane = Plane();
            var ps = Colliding(plane, c => { c.bounce = new ParticleSystem.MinMaxCurve(0.2f, 1f); c.dampen = 0f; c.lifetimeLoss = 0f; });
            try
            {
                for (int i = 0; i < 8; i++) ps.Simulate(0.025f, true, i == 0, false);
                var lanes = new Random.State[4];
                for (int l = 0; l < 4; l++) lanes[l] = Random.State.FromSeed(unchecked((int)(12345u + 367u * (uint)l)));
                var first = new float[4];
                for (int l = 0; l < 4; l++) first[l] = lanes[l].NextValue();
                var second = new float[4];
                for (int l = 0; l < 4; l++) second[l] = lanes[l].NextValue();
                var particles = Read(ps);
                Assert.Equal(6, particles.Length);
                for (int j = 0; j < 6; j++)
                {
                    float r = j < 4 ? first[j] : second[j - 4];
                    Assert.Equal(-2f * (0.2f + 0.8f * r), particles[j].velocity.z, 4);
                }
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(plane.gameObject);
            }
        }

        [Fact]
        public void CollisionCurvesAreReadAtTheirStart()
        {
            // A dampen curve rising over the life reads 0 at its start: a late hit keeps its speed.
            var plane = Plane();
            var ps = Colliding(plane, c => { c.bounce = 1f; c.dampen = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)); c.lifetimeLoss = 0f; });
            try
            {
                for (int i = 0; i < 8; i++) ps.Simulate(0.025f, true, i == 0, false);
                foreach (var p in Read(ps)) Assert.Equal(-2f, p.velocity.z, 4);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(plane.gameObject);
            }
        }

        [Fact]
        public void LifetimeByEmitterSpeedKeepsAtLeastItsFloor()
        {
            var ps = Make(ps =>
            {
                var m = ps.main; m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = EmitterVelocity;
                var l = ps.lifetimeByEmitterSpeed; l.enabled = true; l.curve = 0f; l.range = new Vector2(1f, 1f);
            });
            try
            {
                ps.Emit(4);
                var particles = Read(ps);
                Assert.Equal(4, particles.Length);
                foreach (var p in particles)
                {
                    Assert.False(float.IsNaN(p.startLifetime));
                    Assert.Equal(10f * 0.0001f, p.startLifetime, 6);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void ACustomEmitterVelocityPlacesNewbornsFromTheCurrentPosition()
        {
            // The transform moves, the custom velocity is zero: every newborn sits on the
            // emitter's current position, not along the path it moved.
            var ps = Make(ps =>
            {
                var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startSpeed = 0f;
                m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = Vector3.zero;
                ps.emission.SetBursts(new ParticleSystem.Burst[0]);
                var e = ps.emission; e.rateOverTime = 80f;
            });
            try
            {
                ps.Simulate(0.05f, true, true, false);
                int before = ps.particleCount;
                ps.transform.position = new Vector3(0.5f, 0f, 0f);
                ps.Simulate(0.05f, true, false, false);
                var particles = Read(ps);
                Assert.True(particles.Length > before);
                for (int i = before; i < particles.Length; i++) Assert.Equal(new Vector3(0.5f, 0f, 0f), particles[i].position);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void AScaledCustomSpaceKeepsTheStartSpeed()
        {
            var space = new GameObject("space").transform;
            space.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(-20f, 70f, 5f));
            space.localScale = new Vector3(4f, 0.5f, 2f);
            var ps = Make(ps => { var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.Custom; m.customSimulationSpace = space; m.startSpeed = 3f; });
            try
            {
                ps.Simulate(0.001f, true, true, false);
                foreach (var p in Read(ps)) Assert.Equal(3f, p.velocity.magnitude, 3);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(space.gameObject);
            }
        }

        [Fact]
        public void EmitDuringTheStartDelayKeepsThePlaybackTimeAtItsStart()
        {
            var ps = Make(ps =>
            {
                var m = ps.main; m.startDelay = 0.2f; m.loop = true;
                m.gravityModifier = new ParticleSystem.MinMaxCurve(2f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            });
            try
            {
                ps.Simulate(0.05f, true, true, false);
                ps.Emit(2);
                for (int i = 0; i < 20; i++) ps.Simulate(0.05f, true, false, false);
                var particles = Read(ps);
                Assert.Equal(2, particles.Length); // no burst: the delay never ends
                foreach (var p in particles) Assert.Equal(new Vector3(0f, 0f, 2f), p.velocity); // the curve stays at 0
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void APlayingDelayedSystemReadsCurvesFromTheEndOfItsDelay()
        {
            // Control: without Emit the delay passes, the burst fires and gravity follows the curve.
            var ps = Make(ps =>
            {
                var m = ps.main; m.startDelay = 0.2f; m.loop = true;
                m.gravityModifier = new ParticleSystem.MinMaxCurve(2f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            });
            try
            {
                for (int i = 0; i < 10; i++) ps.Simulate(0.05f, true, i == 0, false);
                var particles = Read(ps);
                Assert.Equal(6, particles.Length);
                foreach (var p in particles) Assert.True(p.velocity.y < -1e-3f, p.velocity.ToString());
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
    }
}
