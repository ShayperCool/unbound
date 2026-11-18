using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.ParticleSystemJobs;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The particle job API over the frame's particle update (6000.0.71f1, tests/projects/particlejobs):
    /// OnParticleUpdateJobScheduled goes to the scripts of each system the update stepped; jobs may be
    /// scheduled only while it runs; a job sees the particles as arrays and its writes reach them once
    /// it has run, at the latest when the update ends. Controls outside the probe: more particles
    /// than the arrays first held, a count that grows between frames, parallel ranges over many
    /// particles, a system disabled with a job pending.
    /// </summary>
    [Collection("Particle culling frames")]
    public sealed class ParticleJobTests : IDisposable
    {
        const float Dt = 0.02f;
        static readonly Vector3 kFar = new Vector3(-3000f, 0f, 0f);

        readonly float m_DeltaTime = Time.s_DeltaTime;
        readonly float m_UnscaledDeltaTime = Time.s_UnscaledDeltaTime;
        readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
        readonly int m_FrameCount = Time.s_FrameCount;
        readonly List<Object> m_Objects = new List<Object>();

        public ParticleJobTests()
        {
            Time.s_DeltaTime = Dt;
            Time.s_UnscaledDeltaTime = Dt;
            Time.s_InFixedTimeStep = false;
            JobScript.OnMessage = null;
        }

        public void Dispose()
        {
            JobScript.OnMessage = null;
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_UnscaledDeltaTime = m_UnscaledDeltaTime;
            Time.s_InFixedTimeStep = m_InFixedTimeStep;
            Time.s_FrameCount = m_FrameCount;
        }

        public sealed class JobScript : MonoBehaviour
        {
            internal static Action<JobScript> OnMessage;
            internal int Messages;

            void OnParticleUpdateJobScheduled()
            {
                Messages++;
                OnMessage?.Invoke(this);
            }
        }

        struct WriteIndexJob : IJobParticleSystem
        {
            public void Execute(ParticleSystemJobData d)
            {
                var positions = d.positions;
                var seeds = d.randomSeeds;
                for (int i = 0; i < d.count; i++)
                {
                    positions[i] = new Vector3(i, 2f * i, 0f);
                    seeds[i] = (uint)(1000 + i);
                }
            }
        }

        struct HitJob : IJobParticleSystemParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<int> Hits;

            public void Execute(ParticleSystemJobData d, int index) => Hits[index] = Hits[index] + 1;
        }

        struct BatchJob : IJobParticleSystemParallelForBatch
        {
            [NativeDisableParallelForRestriction] public NativeArray<int> Covered;

            public void Execute(ParticleSystemJobData d, int startIndex, int count)
            {
                for (int i = startIndex; i < startIndex + count; i++) Covered[i] = Covered[i] + 1;
            }
        }

        struct KillFirstJob : IJobParticleSystem
        {
            public void Execute(ParticleSystemJobData d)
            {
                var alive = d.aliveTimePercent;
                alive[0] = 100f;
            }
        }

        (ParticleSystem System, JobScript Script) Make(int particles)
        {
            var go = new GameObject("jobs");
            m_Objects.Add(go);
            go.SetActive(false);
            go.transform.position = kFar;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.maxParticles = 10000;
            main.playOnAwake = true;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var script = go.AddComponent<JobScript>();
            go.SetActive(true);
            if (particles > 0) ps.Emit(particles);
            return (ps, script);
        }

        /// <summary>The frame's particle update to its end, then the next frame.</summary>
        static void Frame()
        {
            ParticleSystemManager.UpdateAll();
            ParticleSystemManager.EndUpdateAll();
            Time.s_FrameCount++;
        }

        static ParticleSystem.Particle[] ParticlesOf(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            ps.GetParticles(particles);
            return particles;
        }

        [Fact]
        public void SchedulingOutsideTheMessageThrows()
        {
            var (ps, _) = Make(3);
            var e = Assert.Throws<InvalidOperationException>(() => new WriteIndexJob().Schedule(ps));
            Assert.Equal("Particle System jobs can only be scheduled in MonoBehaviour.OnParticleUpdateJobScheduled()", e.Message);
            using var hits = new NativeArray<int>(4, Allocator.Persistent);
            Assert.Throws<InvalidOperationException>(() => new HitJob { Hits = hits }.Schedule(ps, 1));
            Assert.Throws<InvalidOperationException>(() => new BatchJob { Covered = hits }.ScheduleBatch(ps, 1));
        }

        [Fact]
        public void WritesReachEveryParticleAsTheCountGrows()
        {
            // More particles than the arrays' first size, then more again.
            var (ps, script) = Make(40);
            JobScript.OnMessage = s => new WriteIndexJob().Schedule(ps);
            Frame();
            var particles = ParticlesOf(ps);
            Assert.Equal(40, particles.Length);
            for (int i = 0; i < particles.Length; i++)
            {
                Assert.Equal(new Vector3(i, 2f * i, 0f), particles[i].position);
                Assert.Equal((uint)(1000 + i), particles[i].randomSeed);
            }
            ps.Emit(300);
            Frame();
            particles = ParticlesOf(ps);
            Assert.Equal(340, particles.Length);
            Assert.Equal(new Vector3(339f, 678f, 0f), particles[339].position);
            Assert.Equal(2, script.Messages);
        }

        [Fact]
        public void ParallelJobsCoverEveryParticleOnce()
        {
            var (ps, _) = Make(1000);
            using var hits = new NativeArray<int>(1000, Allocator.Persistent);
            using var covered = new NativeArray<int>(1000, Allocator.Persistent);
            JobHandle handle = default;
            JobScript.OnMessage = s =>
            {
                var first = new HitJob { Hits = hits }.Schedule(ps, 7);
                handle = new BatchJob { Covered = covered }.ScheduleBatch(ps, 13, first);
            };
            Frame();
            handle.Complete();
            for (int i = 0; i < 1000; i++)
            {
                Assert.Equal(1, hits[i]);
                Assert.Equal(1, covered[i]);
            }
        }

        [Fact]
        public void AReadSeesTheJobsWritesOnlyOnceTheyHaveRun()
        {
            var (ps, _) = Make(2);
            JobHandle handle = default;
            JobScript.OnMessage = s => handle = new WriteIndexJob().Schedule(ps);
            ParticleSystemManager.UpdateAll();
            Vector3 before = ParticlesOf(ps)[1].position;
            Assert.NotEqual(new Vector3(1f, 2f, 0f), before);
            handle.Complete();
            Assert.Equal(new Vector3(1f, 2f, 0f), ParticlesOf(ps)[1].position);
            ParticleSystemManager.EndUpdateAll();
            Assert.Equal(new Vector3(1f, 2f, 0f), ParticlesOf(ps)[1].position);
        }

        [Fact]
        public void AJobKillsAParticleAtTheNextUpdate()
        {
            var (ps, _) = Make(3);
            JobScript.OnMessage = s => new KillFirstJob().Schedule(ps);
            Frame();
            JobScript.OnMessage = null;
            var particles = ParticlesOf(ps);
            Assert.Equal(3, particles.Length);
            Assert.Equal(0f, particles[0].remainingLifetime);
            Frame();
            Assert.Equal(2, ps.particleCount);
        }

        [Fact]
        public void OnlySystemsTheUpdateStepsGetTheMessage()
        {
            var (playing, playingScript) = Make(1);
            var (paused, pausedScript) = Make(1);
            paused.Pause();
            var (empty, emptyScript) = Make(0);
            Frame();
            // Stopped by a script: the frame's update still sends it the message, the next does not.
            empty.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Frame();
            Frame();
            Assert.Equal(3, playingScript.Messages);
            Assert.Equal(0, pausedScript.Messages);
            Assert.Equal(2, emptyScript.Messages);
        }

        [Fact]
        public void DisablingASystemWithAPendingJobFinishesIt()
        {
            var (ps, _) = Make(5);
            JobHandle handle = default;
            JobScript.OnMessage = s => handle = new WriteIndexJob().Schedule(ps);
            ParticleSystemManager.UpdateAll();
            ps.gameObject.SetActive(false);
            Assert.True(handle.IsCompleted);
            ParticleSystemManager.EndUpdateAll();
            Assert.Equal(0, ps.particleCount);
        }

        [Fact]
        public void SimulateSendsTheMessageAndFinishesItsJobs()
        {
            var (ps, script) = Make(2);
            ps.Pause();
            JobScript.OnMessage = s => new WriteIndexJob().Schedule(ps);
            ps.Simulate(0.1f, true, false, true);
            Assert.Equal(1, script.Messages);
            Assert.Equal(new Vector3(1f, 2f, 0f), ParticlesOf(ps)[1].position);
        }

        [Fact]
        public void SimulateFinishesAPendingJobFirstAndLeavesOtherSystemsJobs()
        {
            var (ps, _) = Make(2);
            var (other, _) = Make(2);
            JobScript.OnMessage = s => new WriteIndexJob().Schedule(s.gameObject == ps.gameObject ? ps : other);
            ParticleSystemManager.UpdateAll();
            JobScript.OnMessage = null;
            ps.Simulate(0.1f, true, false, true);
            // The simulated system's job wrote before the call stepped it (no velocity: positions stay).
            Assert.Equal(new Vector3(1f, 2f, 0f), ParticlesOf(ps)[1].position);
            Assert.True(other.m_JobsPending);
            ParticleSystemManager.EndUpdateAll();
            Assert.Equal(new Vector3(1f, 2f, 0f), ParticlesOf(other)[1].position);
        }
    }
}
