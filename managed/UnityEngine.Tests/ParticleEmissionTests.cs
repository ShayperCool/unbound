using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    // Expectations come from the independent Unity 6000.0.71f1 emission recordings.
    public class ParticleEmissionTests
    {
        static ParticleSystem Make(bool enabled, int source = 0, bool loop = false, float delay = 0, bool live = false)
        {
            var go = new GameObject("emission-control"); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = loop;
            main.startDelay = delay; main.startLifetime = 2; main.startSpeed = 0;
            var emission = ps.emission; emission.enabled = enabled;
            emission.rateOverTime = source == 1 ? 4 : 0; emission.rateOverDistance = source == 2 ? 4 : 0;
            if (source == 3) emission.SetBursts(new[] { new ParticleSystem.Burst(.25f, (short)2) });
            var shape = ps.shape; shape.enabled = false;
            go.SetActive(true);
            if (live) ps.SetParticles(new[] { new ParticleSystem.Particle { startLifetime = 2, remainingLifetime = 2 } });
            return ps;
        }

        static void State(ParticleSystem ps, bool playing, bool emitting, bool paused, bool stopped, bool alive)
        {
            Assert.Equal(playing, ps.isPlaying);
            Assert.Equal(emitting, ps.isEmitting);
            Assert.Equal(paused, ps.isPaused);
            Assert.Equal(stopped, ps.isStopped);
            Assert.Equal(alive, ps.IsAlive(false));
        }

        public static IEnumerable<object[]> Configurations()
        {
            foreach (bool enabled in new[] { false, true })
            for (int source = 0; source < 4; source++)
            foreach (bool loop in new[] { false, true })
            foreach (float delay in new[] { 0f, .25f })
            foreach (bool live in new[] { false, true })
                yield return new object[] { enabled, source, loop, delay, live };
        }

        [Theory]
        [MemberData(nameof(Configurations))]
        public void ModuleTogglesPreservePlaybackRegardlessOfEmissionSource(bool enabled, int source, bool loop, float delay, bool live)
        {
            var ps = Make(enabled, source, loop, delay, live);
            try
            {
                State(ps, false, false, false, true, live);
                var emission = ps.emission;
                ps.Play(false);
                State(ps, true, enabled, false, false, true);
                int count = ps.particleCount;
                foreach (bool value in new[] { false, false, true, true, false, true })
                {
                    emission.enabled = value;
                    State(ps, true, value, false, false, true);
                    Assert.Equal(count, ps.particleCount);
                    Assert.Equal(0f, ps.time);
                }
                ps.Pause(false);
                foreach (bool value in new[] { false, false, true, true })
                {
                    emission.enabled = value;
                    State(ps, false, false, true, false, true);
                }
                ps.Play(false);
                State(ps, true, true, false, false, true);
                ps.Emit(1);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                foreach (bool value in new[] { false, false, true, true })
                {
                    emission.enabled = value;
                    State(ps, true, false, false, false, true);
                }
                ps.Clear(false);
                State(ps, false, false, false, true, false);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZeroEmitDoesNotStartPlaybackAndManualParticlesDoNotStartEmission(bool enabled)
        {
            var ps = Make(enabled);
            try
            {
                ps.Emit(0);
                State(ps, false, false, false, true, false);
                ps.Emit(1);
                State(ps, true, false, false, false, true);
                var emission = ps.emission; emission.enabled = !enabled; emission.enabled = enabled;
                State(ps, true, false, false, false, true);
                ps.Pause(false);
                ps.Emit(new ParticleSystem.EmitParams { startLifetime = 2 }, 1);
                Assert.Equal(2, ps.particleCount);
                State(ps, true, false, false, false, true);
                ps.Clear(false);
                State(ps, false, false, false, true, false);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(1, false, false, 3)]
        [InlineData(1, true, false, 9)]
        [InlineData(3, false, false, 2)]
        [InlineData(3, true, false, 6)]
        [InlineData(1, false, true, 3)]
        [InlineData(1, true, true, 9)]
        [InlineData(3, false, true, 2)]
        [InlineData(3, true, true, 6)]
        public void SubdivisionRemainderKeepsParticlesAtTheirLifetimeBoundary(int source, bool loop, bool live, int count)
        {
            var ps = Make(true, source, loop, 0, live);
            try
            {
                ps.Play(false);
                foreach (float dt in new[] { 0f, .125f, .125f, .25f, .5f, .25f, 1f })
                {
                    ps.Simulate(dt, false, false, false);
                    var saved = ps.GetPlaybackState();
                    ps.Play(false);
                    ps.SetPlaybackState(saved);
                }
                Assert.Equal(count, ps.particleCount);
                ps.Simulate(.0001f, false, false, false);
                Assert.Equal(count - (source == 3 ? 2 : 1), ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(0, 0f, 0, .250001162f)]
        [InlineData(1, 0f, 8, .250001162f)]
        [InlineData(3, 0f, 4, .250001162f)]
        [InlineData(0, .25f, 0, .000000834465f)]
        [InlineData(1, .25f, 8, .000000834465f)]
        [InlineData(3, .25f, 4, .000000834465f)]
        public void PositiveSubdivisionRemainderCrossesTheLoopBoundary(int source, float delay, int count, float time)
        {
            var ps = Make(true, source, true, delay);
            try
            {
                ps.Play(false);
                foreach (float dt in new[] { 0f, .125f, .125f, .25f, .5f, .25f, 1f, 2f })
                {
                    ps.Simulate(dt, false, false, false);
                    var saved = ps.GetPlaybackState();
                    ps.Play(false);
                    ps.SetPlaybackState(saved);
                }
                Assert.Equal(count, ps.particleCount);
                Assert.InRange(Mathf.Abs(ps.time - time), 0f, 2e-4f);
                State(ps, true, true, false, false, true);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(1e-6f, 0f)]
        [InlineData(5e-6f, 0f)]
        [InlineData(9.999999e-6f, 0f)]
        [InlineData(1e-5f, 1e-5f)]
        [InlineData(1.0000001e-5f, 1.0000001e-5f)]
        [InlineData(2e-5f, 2e-5f)]
        public void NonFixedSimulationHonorsTheMeasuredMinimumCallTime(float dt, float time)
        {
            var ps = Make(false);
            try
            {
                var main = ps.main; main.duration = 10; main.startLifetime = 10; main.gravityModifier = 1;
                ps.Emit(new ParticleSystem.EmitParams { velocity = Vector3.right, startLifetime = 10, randomSeed = 42 }, 1);
                ps.Simulate(dt, false, false, false);
                var particles = new ParticleSystem.Particle[1];
                Assert.Equal(1, ps.GetParticles(particles));
                Assert.Equal(time, ps.time);
                Assert.Equal(time, particles[0].position.x);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void LongSimulationConsumesTheFinalSubdivisionRemainder()
        {
            var ps = Make(true, 1, true);
            try
            {
                var main = ps.main; main.duration = 5; main.startLifetime = 1.013f;
                var emission = ps.emission; emission.rateOverTime = 20;
                ps.Simulate(.3f, false, true, false);
                Assert.Equal(5, ps.particleCount);
                ps.Simulate(.7f, false, true, false);
                Assert.Equal(13, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(1f, 4f, 5)]
        [InlineData(1f, 10f, 4)]
        [InlineData(1f, 20f, 4)]
        [InlineData(.03f, 4f, 5)]
        [InlineData(.03f, 10f, 5)]
        [InlineData(.03f, 20f, 5)]
        public void ConstantRateCarriesEachSubdivisionFraction(float maximum, float rate, int count)
        {
            float saved = Time.maximumParticleDeltaTime;
            Time.maximumParticleDeltaTime = maximum;
            var ps = Make(true, 1, true);
            try
            {
                var main = ps.main; main.duration = 5; main.startLifetime = 10;
                var emission = ps.emission; emission.rateOverTime = rate;
                ps.Play(false);
                for (int i = 0; i < 5; i++) ps.Simulate(1f / rate, false, false, false);
                Assert.Equal(count, ps.particleCount);
            }
            finally { Time.maximumParticleDeltaTime = saved; Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false, 9)]
        [InlineData(true, 8)]
        public void ContinuedRateEmissionPreservesTheIntervalAfterParticleWrites(bool write, int count)
        {
            var ps = Make(true, 1, true);
            try
            {
                var main = ps.main; main.duration = 5; main.startLifetime = 1.013f; main.startSpeed = 2;
                var emission = ps.emission; emission.rateOverTime = 20;
                ps.Simulate(.3f, false, true, false);
                Assert.Equal(5, ps.particleCount);
                if (write)
                {
                    var particles = new ParticleSystem.Particle[ps.particleCount]; int n = ps.GetParticles(particles);
                    for (int i = 0; i < n; i++)
                    {
                        particles[i].position += Vector3.up; particles[i].velocity = Vector3.right;
                        if (i == 0) particles[i].remainingLifetime = -1;
                    }
                    ps.SetParticles(particles, n);
                }
                ps.Simulate(.1f, false, false, false);
                ps.Simulate(.1f, false, false, false);
                Assert.Equal(count, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(.03f, 4f, 4)]
        [InlineData(.03f, 10f, 12)]
        [InlineData(.03f, 20f, 24)]
        [InlineData(.03f, 9.7f, 11)]
        [InlineData(1f, 4f, 4)]
        [InlineData(1f, 10f, 12)]
        [InlineData(1f, 20f, 24)]
        [InlineData(1f, 9.7f, 11)]
        public void LoopCrossingsCarryTheEmissionFraction(float maximum, float rate, int count)
        {
            float saved = Time.maximumParticleDeltaTime;
            Time.maximumParticleDeltaTime = maximum;
            var ps = Make(true, 1, true);
            try
            {
                var main = ps.main; main.duration = .3f; main.startLifetime = 10;
                var emission = ps.emission; emission.rateOverTime = rate;
                ps.Play(false);
                for (int i = 0; i < 12; i++) ps.Simulate(.1f, false, false, false);
                Assert.Equal(count, ps.particleCount);
            }
            finally { Time.maximumParticleDeltaTime = saved; Object.DestroyImmediate(ps.gameObject); }
        }

        [Fact]
        public void DurationBoundaryBirthSamplesTheNextLoopsStartCurves()
        {
            // Native broad reference start_curves/10; the particle born at duration one
            // samples the next loop's first subdivision, rather than its old loop's end.
            var ps = Make(true, 1, true);
            try
            {
                var main = ps.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(4, AnimationCurve.Linear(0, 0, 1, 1));
                main.startSize = new ParticleSystem.MinMaxCurve(2, AnimationCurve.Linear(0, 1, 1, .25f));
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.537f, AnimationCurve.Linear(0, .5f, 1, 1));
                var emission = ps.emission; emission.rateOverTime = 10;
                for (int i = 0; i < 11; i++) ps.Simulate(.1f, false, i == 0, false);
                var particles = new ParticleSystem.Particle[16];
                Assert.Equal(9, ps.GetParticles(particles));
                Assert.InRange(particles[8].startLifetime, .787712157f - 2e-4f, .787712157f + 2e-4f);
                Assert.InRange(particles[8].startSize, 1.96250057f - 2e-4f, 1.96250057f + 2e-4f);
                Assert.InRange(particles[8].velocity.z, -2e-4f, 2e-4f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        public static IEnumerable<object[]> PlaybackStates()
        {
            foreach (bool enabled in new[] { false, true })
            for (int saved = 0; saved < 4; saved++)
            for (int target = 0; target < 4; target++)
                yield return new object[] { enabled, saved, target };
        }

        static void Setup(ParticleSystem ps, int state)
        {
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (state > 0) { ps.Play(false); ps.time = .375f; }
            if (state == 2) ps.Pause(false);
            if (state == 3) { ps.Emit(1); ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
        }

        [Theory]
        [MemberData(nameof(PlaybackStates))]
        public void PlaybackSnapshotRestoresTimeWithoutReplacingCurrentState(bool enabled, int saved, int target)
        {
            var ps = Make(enabled);
            try
            {
                Setup(ps, saved);
                var snapshot = ps.GetPlaybackState();
                float time = ps.time;
                Setup(ps, target);
                ps.SetPlaybackState(snapshot);
                State(ps, target == 1 || target == 3, enabled && target == 1, target == 2, target == 0, target != 0);
                Assert.Equal(target == 3 ? 1 : 0, ps.particleCount);
                Assert.Equal(time, ps.time);
                var emission = ps.emission; emission.enabled = !enabled;
                ps.SetPlaybackState(snapshot);
                Assert.Equal(!enabled, ps.emission.enabled);
                Assert.Equal(!enabled && target == 1, ps.isEmitting);
                ps.Play(false);
                Assert.Equal(target == 2 ? time : 0f, ps.time);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ResumeAfterSimulatedDurationKeepsTimeAndResumesEmission(bool enabled, bool fixedStep)
        {
            var ps = Make(enabled);
            try
            {
                ps.Play(false);
                ps.Simulate(2f, false, false, fixedStep);
                State(ps, false, false, true, false, true);
                float time = ps.time;
                Assert.InRange(time, .9998f, 1.0002f);
                ps.Play(false);
                State(ps, true, enabled, false, false, true);
                Assert.Equal(time, ps.time);
                ps.Clear(false);
                State(ps, true, enabled, false, false, true);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void ParentModuleSwitchDoesNotPropagateWithPlayback(bool enabled, bool withChildren)
        {
            var parent = Make(enabled);
            var child = Make(!enabled);
            child.transform.SetParent(parent.transform, false);
            try
            {
                parent.Play(withChildren);
                State(parent, true, enabled, false, false, true);
                State(child, withChildren, withChildren && !enabled, false, !withChildren, withChildren);
                var emission = parent.emission; emission.enabled = !enabled;
                Assert.Equal(!enabled, child.emission.enabled);
                Assert.Equal(withChildren && !enabled, child.isEmitting);
                parent.Pause(withChildren);
                Assert.False(parent.isEmitting);
                Assert.False(child.isEmitting);
                parent.Play(withChildren);
                parent.Stop(withChildren, ParticleSystemStopBehavior.StopEmitting);
                State(parent, false, false, false, true, false);
                State(child, false, false, false, true, false);
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        public static IEnumerable<object[]> StoppedSimulations()
        {
            foreach (bool enabled in new[] { false, true })
            foreach (bool fixedStep in new[] { false, true })
            foreach (bool loop in new[] { false, true })
            foreach (float delay in new[] { 0f, .25f })
                yield return new object[] { enabled, fixedStep, loop, delay };
        }

        [Theory]
        [MemberData(nameof(StoppedSimulations))]
        public void StoppedSimulationAdvancesOnlyTheExistingClock(bool enabled, bool fixedStep, bool loop, float delay)
        {
            var ps = Make(enabled, 1, loop, delay);
            try
            {
                ps.Simulate(.125f, false, true, fixedStep);
                ps.Play(false);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                State(ps, false, false, false, true, false);
                ps.Simulate(3f, false, false, fixedStep);
                State(ps, false, false, true, false, true);
                Assert.Equal(0, ps.particleCount);
                float expected = delay > 0 ? 0 : loop ? fixedStep ? .12f : .125f : 1;
                Assert.InRange(ps.time, expected - 2e-4f, expected + 2e-4f);
                ps.Play(false);
                State(ps, true, enabled, false, false, true);
                Assert.InRange(ps.time, expected - 2e-4f, expected + 2e-4f);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
        static ParticleSystem MakeStopped(int mode, float rate, float delay, ParticleSystem.Burst[] bursts = null)
        {
            var go = new GameObject("emission-stopped"); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = true;
            main.startDelay = delay; main.startLifetime = 5; main.startSpeed = 0;
            var shape = ps.shape; shape.enabled = false;
            var emission = ps.emission; emission.rateOverTime = rate;
            if (bursts != null) emission.SetBursts(bursts);
            ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
            if (mode == 1) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.SetActive(true);
            if (mode == 2) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (mode == 3) { ps.Play(false); ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }
            return ps;
        }

        // particleemission longCall/false/*, stoppedSimulate/{0,1,3,5}/1/0: any Stop stops emission,
        // also before the system ever played; a system never stopped emits.
        [Theory]
        [InlineData(0, 3)]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(3, 0)]
        public void NonRestartingSimulateEmitsOnlyIfNeverStopped(int mode, int count)
        {
            var ps = MakeStopped(mode, 10, 0);
            try
            {
                ps.Simulate(.3f, false, false, false);
                Assert.Equal(count, ps.particleCount);
                Assert.Equal(.3f, ps.time, 1e-6f);
                // Paused by Simulate, it is alive.
                State(ps, false, false, true, false, true);
                ps.Simulate(.7f, false, false, false);
                ps.Play(false);
                // Play resumes the paused clock and emission.
                State(ps, true, true, false, false, true);
                Assert.Equal(0.9999995f, ps.time);
                int before = ps.particleCount;
                // The sub-microsecond time the two calls left joins this one: the particle due
                // just after the loop end counts (3, not 2, without the carry).
                ps.Simulate(.3f, false, false, false);
                Assert.Equal(before + 3, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // particleemission stoppedSimulate/{1,4}/1/0.25 and stoppedLongStep/*: a stopped system keeps
        // the start delay it has left; a step longer than that advances by the excess.
        [Theory]
        [InlineData(1, .03f, .25f, 0f, 0f, .05f)]
        [InlineData(3, .5f, .1f, .2f, .4f, .6f)]
        [InlineData(3, .5f, .25f, .05f, .1f, .15f)]
        [InlineData(3, .5f, .4f, 0f, 0f, 0f)]
        public void StoppedClockKeepsTheStartDelayLeft(int mode, float maximum, float delay, float first, float second, float resumed)
        {
            float saved = Time.maximumParticleDeltaTime;
            Time.maximumParticleDeltaTime = maximum;
            var ps = MakeStopped(mode, 10, delay);
            try
            {
                ps.Simulate(.3f, false, false, false);
                Assert.Equal(first, ps.time, 1e-6f);
                ps.Simulate(.3f, false, false, false);
                Assert.Equal(second, ps.time, 1e-6f);
                Assert.Equal(0, ps.particleCount);
                ps.Play(false);
                ps.Simulate(.3f, false, false, false);
                Assert.Equal(resumed, ps.time, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Time.maximumParticleDeltaTime = saved;
            }
        }

        // particleemission stoppedLongStep/0.5/0.1/afterPlay: a step that ends the start delay
        // spreads its rate births over the whole step, delay included (age 0.15, not 0.1).
        [Fact]
        public void RateBirthsInTheStepEndingTheDelaySpreadOverTheWholeStep()
        {
            float saved = Time.maximumParticleDeltaTime;
            Time.maximumParticleDeltaTime = .5f;
            var ps = MakeStopped(3, 10, .1f);
            try
            {
                ps.Simulate(.3f, false, false, false);
                ps.Simulate(.3f, false, false, false);
                ps.Play(false);
                ps.Simulate(.3f, false, false, false);
                var particles = new ParticleSystem.Particle[4];
                Assert.Equal(1, ps.GetParticles(particles));
                Assert.Equal(4.85f, particles[0].remainingLifetime, 2e-4f);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Time.maximumParticleDeltaTime = saved;
            }
        }

        // particleemission mixedRemainder/false/0.5/0/true: a fresh playback counts its delay
        // down and spreads the births of the step that ends it over the rest of that step.
        [Fact]
        public void AFreshPlaybackSpreadsBirthsOverThePartAfterItsDelay()
        {
            float saved = Time.maximumParticleDeltaTime;
            Time.maximumParticleDeltaTime = .5f;
            var ps = MakeStopped(0, 10, .1f);
            try
            {
                ps.Play(false);
                ps.Simulate(.5f, false, false, false);
                Assert.Equal(.4f, ps.time, 1e-6f);
                var particles = new ParticleSystem.Particle[8];
                Assert.Equal(4, ps.GetParticles(particles));
                var lives = new float[4];
                for (int i = 0; i < 4; i++) lives[i] = particles[i].remainingLifetime;
                System.Array.Sort(lives);
                for (int i = 0; i < 4; i++) Assert.Equal(4.7f + .1f * i, lives[i], 2e-4f);
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Time.maximumParticleDeltaTime = saved;
            }
        }

        // particleemission mixedRemainder/*: fixed-step and variable-step calls share what the
        // previous call left unstepped.
        [Theory]
        [InlineData(true, .25f, .3f, .24f, .55f)]
        [InlineData(true, .25f, .016f, .24f, .266f)]
        [InlineData(false, .25f, .016f, .25f, .25f)]
        public void FixedAndVariableCallsShareTheUnsteppedTime(bool fixedFirst, float first, float second, float afterFirst, float afterSecond)
        {
            var ps = MakeStopped(0, 10, 0);
            try
            {
                ps.Play(false);
                ps.Simulate(first, false, false, fixedFirst);
                Assert.Equal(afterFirst, ps.time, 1e-6f);
                ps.Simulate(second, false, false, !fixedFirst);
                Assert.Equal(afterSecond, ps.time, 1e-6f);
                if (!fixedFirst)
                {
                    // The fixed call's unstepped 0.016 joins the next variable-step call.
                    ps.Play(false);
                    ps.Simulate(second, false, false, false);
                    Assert.Equal(.282f, ps.time, 1e-6f);
                }
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        // particleemission stoppedSimulate/6/3 and disabledBurst/*: a burst cycle that passes while
        // the system does not emit never fires; later cycles still do.
        [Theory]
        [InlineData(false, false, 0, 2)]
        [InlineData(true, false, 4, 6)]
        [InlineData(false, true, 0, 2)]
        public void BurstCyclesPassedWithoutEmissionDoNotFireLater(bool repeat, bool stop, int resumed, int nextLoop)
        {
            var burst = new ParticleSystem.Burst(.25f, (short)2);
            if (repeat) { burst.cycleCount = 3; burst.repeatInterval = .2f; }
            var ps = MakeStopped(0, 0, 0, new[] { burst });
            try
            {
                ps.Play(false);
                ps.Simulate(.1f, false, false, false);
                var emission = ps.emission;
                if (stop) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                else emission.enabled = false;
                ps.Simulate(.3f, false, false, false);
                Assert.Equal(0, ps.particleCount);
                if (stop) ps.Play(false);
                else emission.enabled = true;
                ps.Simulate(.3f, false, false, false);
                Assert.Equal(resumed, ps.particleCount);
                ps.Simulate(.7f, false, false, false);
                Assert.Equal(nextLoop, ps.particleCount);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }
    }
}

