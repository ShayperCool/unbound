using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class StopRecorder : MonoBehaviour
    {
        public int stops;
        void OnParticleSystemStopped() => stops++;
    }

    public class ParticleSystemTests
    {
        /// <summary>A system on a fresh object, configured before it becomes active (and plays on awake).</summary>
        static ParticleSystem Make(Action<ParticleSystem> configure)
        {
            var go = new GameObject("particles");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            configure(ps);
            go.SetActive(true);
            return ps;
        }

        static void Run(ParticleSystem ps, float seconds, float dt = 0.02f)
        {
            int steps = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < steps; i++) ps.Step(dt, 0f);
        }

        static void Done(ParticleSystem ps) => Object.DestroyImmediate(ps.gameObject);

        [Fact]
        public void AddingASystemAddsItsRendererAndPlaysOnAwake()
        {
            var ps = Make(_ => { });
            Assert.NotNull(ps.GetComponent<ParticleSystemRenderer>());
            Assert.True(ps.isPlaying);
            Assert.True(ps.isEmitting);
            Assert.False(ps.isStopped);
            Done(ps);
        }

        [Fact]
        public void RateOverTimeEmitsThatManyParticlesPerSecond()
        {
            var ps = Make(p =>
            {
                var e = p.emission;
                e.rateOverTime = 20f;
            });
            Run(ps, 1f);
            Assert.Equal(20, ps.particleCount);
            Run(ps, 0.5f);
            Assert.Equal(30, ps.particleCount);
            Done(ps);
        }

        [Fact]
        public void BurstsFireAtTheirTimesWithCyclesAndIntervals()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.duration = 2f;
                main.loop = false;
                var e = p.emission;
                e.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, 10),
                    new ParticleSystem.Burst(0.5f, 5, 5, 3, 0.2f),
                });
            });
            Run(ps, 0.1f);
            Assert.Equal(10, ps.particleCount);
            Run(ps, 0.5f); // t = 0.6: the 0.5 cycle fired
            Assert.Equal(15, ps.particleCount);
            Run(ps, 0.2f); // t = 0.8: 0.7 fired
            Assert.Equal(20, ps.particleCount);
            Run(ps, 0.4f); // t = 1.2: 0.9 fired, then the cycles are used up
            Assert.Equal(25, ps.particleCount);
            Run(ps, 1f);
            Assert.Equal(25, ps.particleCount);
            Done(ps);
        }

        [Fact]
        public void BurstsWithRandomCountStayInRangeAndRespectProbability()
        {
            var ps = Make(p =>
            {
                var e = p.emission;
                var never = new ParticleSystem.Burst(0f, 100) { probability = 0f };
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 10), never });
            });
            Run(ps, 0.1f);
            Assert.InRange(ps.particleCount, 8, 10);
            Done(ps);
        }

        [Fact]
        public void LoopingRepeatsBurstsEveryDuration()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.duration = 1f;
                main.loop = true;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            });
            Run(ps, 3.1f);
            Assert.Equal(4, ps.particleCount); // t = 0, 1, 2, 3
            Assert.InRange(ps.time, 0.05f, 0.15f);
            Assert.InRange(ps.totalTime, 3.05f, 3.15f);
            Done(ps);
        }

        [Fact]
        public void ParticlesDieAtTheEndOfTheirLifetimeAndTheSystemStops()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.duration = 0.3f;
                main.loop = false;
                main.startLifetime = 0.5f;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });
            });
            Run(ps, 0.4f);
            Assert.Equal(5, ps.particleCount);
            Assert.False(ps.isEmitting);
            Assert.True(ps.isPlaying);
            Assert.True(ps.IsAlive());
            Run(ps, 0.2f);
            Assert.Equal(0, ps.particleCount);
            Assert.False(ps.isPlaying);
            Assert.True(ps.isStopped);
            Assert.False(ps.IsAlive());
            Done(ps);
        }

        [Fact]
        public void StopLetsParticlesLiveAndClearRemovesThem()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.startLifetime = 1f;
                var e = p.emission;
                e.rateOverTime = 10f;
            });
            Run(ps, 0.5f);
            Assert.Equal(5, ps.particleCount);
            ps.Stop();
            Assert.False(ps.isEmitting);
            Assert.True(ps.isPlaying);
            Run(ps, 0.5f);
            Assert.Equal(5, ps.particleCount);
            Run(ps, 0.6f);
            Assert.Equal(0, ps.particleCount);
            Assert.True(ps.isStopped);

            ps.Play();
            Run(ps, 0.5f);
            Assert.True(ps.particleCount > 0);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Assert.Equal(0, ps.particleCount);
            Assert.True(ps.isStopped);
            Done(ps);
        }

        [Fact]
        public void PauseFreezesAndPlayResumes()
        {
            var ps = Make(p =>
            {
                var e = p.emission;
                e.rateOverTime = 10f;
            });
            Run(ps, 0.5f);
            ps.Pause();
            Assert.True(ps.isPaused);
            Assert.False(ps.isPlaying);
            ps.FrameUpdate();
            Assert.Equal(5, ps.particleCount);
            ps.Play();
            Assert.True(ps.isPlaying);
            Assert.False(ps.isPaused);
            Done(ps);
        }

        [Fact]
        public void StartDelayHoldsEmissionBack()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.startDelay = 0.5f;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            });
            Run(ps, 0.4f);
            Assert.Equal(0, ps.particleCount);
            Assert.Equal(0f, ps.time);
            Run(ps, 0.2f);
            Assert.Equal(3, ps.particleCount);
            Done(ps);
        }

        [Fact]
        public void PrewarmStartsLoopingSystemsInTheirSteadyState()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.duration = 1f;
                main.loop = true;
                main.prewarm = true;
                main.startLifetime = 1f;
                var e = p.emission;
                e.rateOverTime = 10f;
            });
            Assert.InRange(ps.particleCount, 9, 11);
            Assert.Equal(0f, ps.time);
            Done(ps);
        }

        [Fact]
        public void MaxParticlesCapsEmissionUnlessTheRingBufferReplaces()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.maxParticles = 5;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
            });
            Run(ps, 0.1f);
            Assert.Equal(5, ps.particleCount);
            Done(ps);

            ps = Make(p =>
            {
                var main = p.main;
                main.maxParticles = 3;
                main.startLifetime = 0.2f;
                main.ringBufferMode = ParticleSystemRingBufferMode.PauseUntilReplaced;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            });
            Run(ps, 1f);
            Assert.Equal(3, ps.particleCount); // they stay at the end of their life
            ps.Emit(1);
            Assert.Equal(3, ps.particleCount);
            Done(ps);
        }

        [Fact]
        public void StopActionCallbackSendsTheMessage()
        {
            StopRecorder recorder = null;
            var ps = Make(p =>
            {
                recorder = p.gameObject.AddComponent<StopRecorder>();
                var main = p.main;
                main.duration = 0.1f;
                main.loop = false;
                main.startLifetime = 0.1f;
                main.stopAction = ParticleSystemStopAction.Callback;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            });
            Run(ps, 0.5f);
            Assert.Equal(1, recorder.stops);
            Done(ps);
        }

        [Fact]
        public void StopActionDisableDeactivatesTheObject()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.duration = 0.1f;
                main.loop = false;
                main.startLifetime = 0.1f;
                main.stopAction = ParticleSystemStopAction.Disable;
            });
            Run(ps, 0.5f);
            Assert.False(ps.gameObject.activeSelf);
            Done(ps);
        }

        [Fact]
        public void PlayStopAndClearReachChildren()
        {
            var parent = Make(_ => { });
            var child = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
            });
            child.transform.SetParent(parent.transform, false);
            Assert.False(child.isPlaying);
            parent.Stop();
            parent.Play();
            Assert.True(child.isPlaying);
            parent.Play(false);
            parent.Stop(false);
            Assert.True(child.isEmitting);
            parent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Assert.True(child.isStopped);
            Done(parent);
        }

        [Fact]
        public void EmitWorksWhileStoppedAndParticlesStillMove()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                main.startSpeed = 2f;
                var shape = p.shape;
                shape.enabled = false;
            });
            ps.Emit(3);
            Assert.Equal(3, ps.particleCount);
            Assert.True(ps.isStopped);
            Run(ps, 0.5f);
            var particles = new ParticleSystem.Particle[8];
            Assert.Equal(3, ps.GetParticles(particles));
            Assert.True((particles[0].position - new Vector3(0, 0, 1)).magnitude < 1e-3f, particles[0].position.ToString());
            Done(ps);
        }

        [Fact]
        public void EmitParamsOverrideTheStartValues()
        {
            var ps = Make(p => { var main = p.main; main.playOnAwake = false; });
            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(1, 2, 3),
                velocity = new Vector3(0, 1, 0),
                startColor = new Color32(255, 0, 0, 255),
                startSize = 0.25f,
                startLifetime = 2f,
                rotation = 90f,
            };
            ps.Emit(emit, 2);
            var particles = new ParticleSystem.Particle[2];
            Assert.Equal(2, ps.GetParticles(particles));
            Assert.Equal(new Vector3(1, 2, 3), particles[0].position);
            Assert.Equal(new Vector3(0, 1, 0), particles[1].velocity);
            Assert.Equal(new Color32(255, 0, 0, 255), particles[0].startColor);
            Assert.Equal(0.25f, particles[0].startSize);
            Assert.Equal(2f, particles[0].startLifetime);
            Assert.True(Mathf.Abs(particles[0].rotation - 90f) < 1e-3f);
            Done(ps);
        }

        [Fact]
        public void SetParticlesReplacesTheParticles()
        {
            var ps = Make(p => { var main = p.main; main.playOnAwake = false; });
            ps.Emit(4);
            var particles = new ParticleSystem.Particle[4];
            ps.GetParticles(particles);
            particles[1].position = new Vector3(5, 5, 5);
            ps.SetParticles(particles, 2);
            Assert.Equal(2, ps.particleCount);
            var back = new ParticleSystem.Particle[4];
            Assert.Equal(2, ps.GetParticles(back));
            Assert.Equal(new Vector3(5, 5, 5), back[1].position);
            Done(ps);
        }

        [Fact]
        public void ModulesWriteThroughAndCurvesAreCopies()
        {
            var ps = Make(_ => { });
            var main = ps.main;
            main.startSpeed = 3f;
            Assert.Equal(3f, ps.main.startSpeed.constant);
            Assert.Equal(3f, ps.main.startSpeedMultiplier);
            main.startSpeedMultiplier = 4f;
            Assert.Equal(4f, ps.main.startSpeed.constant);

            var curve = AnimationCurve.Linear(0, 0, 1, 1);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(2f, curve);
            curve.AddKey(0.5f, 10f); // not seen by the system: it keeps its own copy
            var read = ps.sizeOverLifetime.size;
            Assert.Equal(ParticleSystemCurveMode.Curve, read.mode);
            Assert.Equal(2, read.curve.length);
            Assert.Equal(1f, read.Evaluate(0.5f), 3);
            read.curve.AddKey(0.25f, 5f);
            Assert.Equal(2, ps.sizeOverLifetime.size.curve.length);
            Done(ps);
        }

        [Fact]
        public void MinMaxCurveEvaluatesEachMode()
        {
            var constant = new ParticleSystem.MinMaxCurve(5f);
            Assert.Equal(5f, constant.Evaluate(0.3f, 0.1f));
            var two = new ParticleSystem.MinMaxCurve(2f, 4f);
            Assert.Equal(ParticleSystemCurveMode.TwoConstants, two.mode);
            Assert.Equal(3f, two.Evaluate(0f, 0.5f));
            Assert.Equal(4f, two.Evaluate(0f));
            var curve = new ParticleSystem.MinMaxCurve(10f, AnimationCurve.Linear(0, 0, 1, 1));
            Assert.Equal(5f, curve.Evaluate(0.5f), 4);
            var curves = new ParticleSystem.MinMaxCurve(2f, AnimationCurve.Constant(0, 1, 1), AnimationCurve.Constant(0, 1, 3));
            Assert.Equal(4f, curves.Evaluate(0.5f, 0.5f), 4);
            ParticleSystem.MinMaxCurve implicitCurve = 7f;
            Assert.Equal(7f, implicitCurve.constant);

            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            Assert.Equal(Color.red, new ParticleSystem.MinMaxGradient(Color.red).Evaluate(0.7f));
            var mid = new ParticleSystem.MinMaxGradient(g).Evaluate(0.5f);
            Assert.True(Mathf.Abs(mid.r - 0.5f) < 0.01f);
            var twoColors = new ParticleSystem.MinMaxGradient(Color.black, Color.white).Evaluate(0f, 0.25f);
            Assert.True(Mathf.Abs(twoColors.g - 0.25f) < 1e-4f);
            var random = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
            Assert.True(Mathf.Abs(random.Evaluate(0f, 0.75f).b - 0.75f) < 0.01f);
        }

        [Fact]
        public void SizeAndColorOverLifetimeShapeTheCurrentValues()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                main.startLifetime = 1f;
                main.startSize = 2f;
                var size = p.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                var color = p.colorOverLifetime;
                color.enabled = true;
                color.color = g;
            });
            ps.Emit(1);
            Run(ps, 0.5f, 0.05f);
            var particles = new ParticleSystem.Particle[1];
            ps.GetParticles(particles);
            Assert.True(Mathf.Abs(particles[0].GetCurrentSize(ps) - 1f) < 0.02f, particles[0].GetCurrentSize(ps).ToString());
            Assert.InRange(particles[0].GetCurrentColor(ps).a, 120, 135);
            Done(ps);
        }

        [Fact]
        public void VelocityGravityAndLimitMoveParticles()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                main.gravityModifier = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var shape = p.shape;
                shape.enabled = false;
                var velocity = p.velocityOverLifetime;
                velocity.enabled = true;
                velocity.x = 1f;
                velocity.space = ParticleSystemSimulationSpace.World;
            });
            ps.Emit(1);
            Run(ps, 1f, 0.01f);
            var particles = new ParticleSystem.Particle[1];
            ps.GetParticles(particles);
            Assert.True(Mathf.Abs(particles[0].position.x - 1f) < 0.02f, particles[0].position.ToString());
            Assert.True(Mathf.Abs(particles[0].velocity.y + 9.81f) < 0.05f, particles[0].velocity.ToString());
            Assert.True(particles[0].position.y < -4.5f && particles[0].position.y > -5.2f, particles[0].position.ToString());
            Assert.Equal(new Vector3(1, 0, 0), particles[0].animatedVelocity);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 2f;
            limit.dampen = 1f;
            Run(ps, 0.1f, 0.01f);
            ps.GetParticles(particles);
            Assert.True(particles[0].velocity.magnitude <= 2.001f);
            Done(ps);
        }

        [Fact]
        public void ShapesPlaceParticlesOnTheirShape()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                main.startSpeed = 1f;
                var shape = p.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 2f;
                shape.radiusThickness = 0f;
            });
            ps.Emit(50);
            var particles = new ParticleSystem.Particle[50];
            ps.GetParticles(particles);
            foreach (var particle in particles)
            {
                Assert.True(Mathf.Abs(particle.position.magnitude - 2f) < 1e-3f);
                Assert.True(Mathf.Abs(particle.position.z) < 1e-5f);
                Assert.True(Vector3.Dot(particle.velocity.normalized, particle.position.normalized) > 0.999f);
            }

            var box = ps.shape;
            box.shapeType = ParticleSystemShapeType.Box;
            box.scale = new Vector3(2, 4, 6);
            ps.Clear();
            ps.Emit(50);
            ps.GetParticles(particles);
            foreach (var particle in particles)
            {
                Assert.True(Mathf.Abs(particle.position.x) <= 1f && Mathf.Abs(particle.position.y) <= 2f && Mathf.Abs(particle.position.z) <= 3f);
                Assert.Equal(Vector3.forward, particle.velocity);
            }

            var cone = ps.shape;
            cone.shapeType = ParticleSystemShapeType.Cone;
            cone.angle = 30f;
            cone.radius = 1f;
            cone.radiusThickness = 1f;
            cone.scale = Vector3.one;
            ps.Clear();
            ps.Emit(50);
            ps.GetParticles(particles);
            foreach (var particle in particles)
            {
                Assert.True(particle.position.magnitude <= 1.0001f && Mathf.Abs(particle.position.z) < 1e-5f);
                float angle = Vector3.Angle(Vector3.forward, particle.velocity);
                Assert.True(angle <= 30.01f, angle.ToString());
            }
            Done(ps);
        }

        [Fact]
        public void LocalSpaceParticlesFollowTheTransform()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                var shape = p.shape;
                shape.enabled = false;
            });
            ps.Emit(1);
            ps.transform.position = new Vector3(10, 0, 0);
            var world = ps.SimulationToWorld().MultiplyPoint3x4(ps.m_Particles[0].m_Position);
            Assert.Equal(new Vector3(10, 0, 0), world);

            var main2 = ps.main;
            main2.simulationSpace = ParticleSystemSimulationSpace.World;
            ps.Clear();
            ps.Emit(1);
            ps.transform.position = Vector3.zero;
            world = ps.SimulationToWorld().MultiplyPoint3x4(ps.m_Particles[0].m_Position);
            Assert.Equal(new Vector3(10, 0, 0), world);
            Done(ps);
        }

        [Fact]
        public void DeathSubEmittersEmitTheirBurstsWhereParticlesDie()
        {
            var child = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });
                var shape = p.shape;
                shape.enabled = false;
            });
            var parent = Make(p =>
            {
                var main = p.main;
                main.startLifetime = 0.2f;
                main.startSpeed = 0f;
                var shape = p.shape;
                shape.enabled = false;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
                var subs = p.subEmitters;
                subs.enabled = true;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            });
            child.transform.SetParent(parent.transform, false);
            parent.transform.position = new Vector3(0, 3, 0);
            Run(parent, 0.3f);
            Assert.Equal(0, parent.particleCount);
            Assert.Equal(8, child.particleCount);
            var particles = new ParticleSystem.Particle[8];
            child.GetParticles(particles);
            Assert.Equal(new Vector3(0, 3, 0), particles[0].position);
            Done(parent);
        }

        [Fact]
        public void InstantiateCopiesTheSettings()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.startSize = new ParticleSystem.MinMaxCurve(1f, 2f);
                main.duration = 3f;
                p.emission.SetBursts(new[] { new ParticleSystem.Burst(0.5f, 7) });
                var r = p.GetComponent<ParticleSystemRenderer>();
            });
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Stretch;
            var clone = Object.Instantiate(ps.gameObject).GetComponent<ParticleSystem>();
            Assert.Equal(3f, clone.main.duration);
            Assert.Equal(ParticleSystemCurveMode.TwoConstants, clone.main.startSize.mode);
            Assert.Equal(7, clone.emission.GetBurst(0).maxCount);
            Assert.Equal(ParticleSystemRenderMode.Stretch, clone.GetComponent<ParticleSystemRenderer>().renderMode);
            Assert.Single(clone.GetComponents<ParticleSystemRenderer>());
            var main2 = clone.main;
            main2.loop = false;
            Assert.True(ps.main.loop);
            Done(ps);
            Object.DestroyImmediate(clone.gameObject);
        }

        [Fact]
        public void ReadsARealParticleSystemDocument()
        {
            var go = new GameObject("coins");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var resolver = new NullResolver();
            ps.ReadSerialized(TestYaml.DocumentBody(ParticleFixtures.CoinsSystem), resolver);

            var main = ps.main;
            Assert.Equal(1f, main.duration);
            Assert.False(main.loop);
            Assert.True(main.playOnAwake);
            Assert.Equal(ParticleSystemSimulationSpace.Local, main.simulationSpace);
            Assert.Equal(ParticleSystemScalingMode.Hierarchy, main.scalingMode);
            Assert.Equal(ParticleSystemCullingMode.AlwaysSimulate, main.cullingMode);
            Assert.Equal(200, main.maxParticles);
            Assert.Equal(0.5f, main.flipRotation);
            Assert.Equal(ParticleSystemCurveMode.TwoConstants, main.startLifetime.mode);
            Assert.Equal(0.35f, main.startLifetime.constantMin);
            Assert.Equal(0.45f, main.startLifetime.constantMax);
            Assert.Equal(950f, main.startSpeed.constantMin);
            Assert.Equal(1500f, main.startSpeed.constantMax);
            Assert.Equal(20f, main.startSize.constantMin);
            Assert.Equal(30f, main.startSize.constantMax);
            Assert.Equal(6.283185f, main.startRotation.constantMax);
            Assert.Equal(2f, main.gravityModifier.constant);
            Assert.Equal(ParticleSystemGradientMode.Color, main.startColor.mode);
            Assert.Equal(Color.white, main.startColor.color);

            var shape = ps.shape;
            Assert.Equal(ParticleSystemShapeType.Circle, shape.shapeType);
            Assert.Equal(40f, shape.radius);
            Assert.Equal(90f, shape.arc);
            Assert.Equal(1f, shape.radiusThickness);

            var emission = ps.emission;
            Assert.True(emission.enabled);
            Assert.Equal(0f, emission.rateOverTime.constant);
            Assert.Equal(1, emission.burstCount);
            var burst = emission.GetBurst(0);
            Assert.Equal(0f, burst.time);
            Assert.Equal(8, burst.minCount);
            Assert.Equal(10, burst.maxCount);
            Assert.Equal(1, burst.cycleCount);
            Assert.Equal(1f, burst.probability);

            var size = ps.sizeOverLifetime;
            Assert.True(size.enabled);
            Assert.Equal(ParticleSystemCurveMode.Curve, size.size.mode);
            Assert.Equal(4, size.size.curve.length);
            Assert.True(Mathf.Abs(size.size.Evaluate(0.5f) - 1f) < 0.05f);
            Assert.Equal(0f, size.size.Evaluate(1f), 3);

            var rotation = ps.rotationOverLifetime;
            Assert.True(rotation.enabled);
            Assert.Equal(10.471975f, rotation.z.constant);

            var color = ps.colorOverLifetime;
            Assert.True(color.enabled);
            Assert.Equal(ParticleSystemGradientMode.Gradient, color.color.mode);
            Assert.Equal(3, color.color.gradient.alphaKeys.Length);
            Assert.Equal(1f, color.color.Evaluate(0.5f).a, 3);
            Assert.Equal(0f, color.color.Evaluate(1f).a, 3);

            var limit = ps.limitVelocityOverLifetime;
            Assert.True(limit.enabled);
            Assert.Equal(0.15f, limit.dampen);
            Assert.Equal(1f, limit.limit.constant);
            Assert.False(ps.textureSheetAnimation.enabled);
            Assert.False(ps.noise.enabled);

            // Played like the prefab plays: one burst of 8 to 10 particles that live 0.35 to 0.45 s.
            go.SetActive(true);
            Run(ps, 0.1f);
            Assert.InRange(ps.particleCount, 8, 10);
            var particles = new ParticleSystem.Particle[16];
            int n = ps.GetParticles(particles);
            for (int i = 0; i < n; i++)
            {
                Assert.InRange(particles[i].startLifetime, 0.35f, 0.45f);
                Assert.InRange(particles[i].startSize, 20f, 30f);
                float distance = new Vector2(particles[i].position.x, particles[i].position.y).magnitude;
                Assert.True(distance > 40f, distance.ToString()); // on the circle, then moving outward
                float angle = Mathf.Atan2(particles[i].position.y, particles[i].position.x) * Mathf.Rad2Deg;
                Assert.InRange(angle, -0.01f, 90.01f);
            }
            Run(ps, 0.5f);
            Assert.Equal(0, ps.particleCount);
            Assert.True(ps.isPlaying); // until the end of its one-second duration
            Run(ps, 0.5f);
            Assert.True(ps.isStopped);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void ReadsARealRendererDocument()
        {
            var go = new GameObject("coins");
            go.SetActive(false);
            var renderer = go.AddComponent<ParticleSystemRenderer>();
            var resolver = new NullResolver();
            renderer.ReadSerialized(TestYaml.DocumentBody(ParticleFixtures.CoinsRenderer), resolver);
            Assert.False(renderer.enabled);
            Assert.Equal(ParticleSystemRenderMode.Billboard, renderer.renderMode);
            Assert.Equal(ParticleSystemSortMode.YoungestInFront, renderer.sortMode);
            Assert.Equal(1f, renderer.maxParticleSize);
            Assert.Equal(2f, renderer.lengthScale);
            Assert.Equal(22, renderer.sortingOrder);
            Assert.Equal(-890204995, renderer.sortingLayerID);
            Assert.Equal(ParticleSystemRenderSpace.View, renderer.alignment);
            Assert.Equal(0, renderer.meshCount);
            Assert.Equal(2, renderer.sharedMaterials.Length);
            Assert.Contains(resolver.Requests, r => r.Guid == "18028a8d06240e84b2642535a97cd9c4");
            var streams = new List<ParticleSystemVertexStream>();
            renderer.GetActiveVertexStreams(streams);
            Assert.Equal(4, streams.Count);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void RendererBuildsCameraFacingQuadsWithSheetUvs()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                main.startSize = 2f;
                main.startColor = new Color(1f, 0f, 0f, 1f);
                var shape = p.shape;
                shape.enabled = false;
                var sheet = p.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = 2;
                sheet.numTilesY = 2;
                sheet.frameOverTime = 0f; // frame 0: the top-left tile
            });
            var emit = new ParticleSystem.EmitParams { position = new Vector3(0, 0, 5) };
            ps.Emit(emit, 1);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var geometry = new ParticleSystemRenderer.Geometry();
            // A camera at the origin looking down +Z (its view matrix flips Z).
            var view = ParticleSystemRenderer.View.FromMatrix(Matrix4x4.Scale(new Vector3(1, 1, -1)), Vector3.zero, false, null);
            Assert.Equal(Vector3.forward, view.Forward);
            Assert.True(geometry.Build(renderer, ps, view));
            Assert.Equal(4, geometry.VertexCount);
            Assert.Equal(6, geometry.IndexCount);
            Assert.Equal(new Vector3(-1, -1, 5), geometry.Positions[0]);
            Assert.Equal(new Vector3(1, 1, 5), geometry.Positions[2]);
            Assert.Equal(new Color32(255, 0, 0, 255), geometry.Colors[0]);
            Assert.Equal(new Vector4(0f, 0.5f, 0f, 0f), geometry.Uvs[0]);
            Assert.Equal(new Vector4(0.5f, 1f, 0f, 0f), geometry.Uvs[2]);

            var mesh = new Mesh();
            geometry.WriteTo(mesh, Matrix4x4.identity);
            Assert.Equal(4, mesh.vertexCount);
            Assert.Equal(new[] { 0, 1, 2, 2, 3, 0 }, mesh.triangles);
            Object.DestroyImmediate(mesh);
            Done(ps);
        }

        [Fact]
        public void RendererSortsByAge()
        {
            var ps = Make(p =>
            {
                var main = p.main;
                main.playOnAwake = false;
                var shape = p.shape;
                shape.enabled = false;
            });
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ps.Emit(new ParticleSystem.EmitParams { position = new Vector3(0, 0, 5), startColor = new Color32(1, 0, 0, 255) }, 1);
            Run(ps, 0.1f);
            ps.Emit(new ParticleSystem.EmitParams { position = new Vector3(0, 0, 5), startColor = new Color32(2, 0, 0, 255) }, 1);
            var view = ParticleSystemRenderer.View.FromMatrix(Matrix4x4.Scale(new Vector3(1, 1, -1)), Vector3.zero, false, null);
            var geometry = new ParticleSystemRenderer.Geometry();
            renderer.sortMode = ParticleSystemSortMode.OldestInFront;
            geometry.Build(renderer, ps, view);
            Assert.Equal(1, geometry.Colors[4].r); // the oldest is drawn last
            renderer.sortMode = ParticleSystemSortMode.YoungestInFront;
            geometry.Build(renderer, ps, view);
            Assert.Equal(2, geometry.Colors[4].r);
            Done(ps);
        }
    }
}
