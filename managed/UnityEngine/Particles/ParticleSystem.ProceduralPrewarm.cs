using Unbound.Runtime;

namespace UnityEngine
{
    // Prewarm of a system in procedural mode (ParticleSystem.proceduralSimulationSupported), as
    // measured in the Unity 6000.0.71f1 Editor (tests/projects/particlerender prewarm/nosub*,
    // prewarm/proc_*): the emission runs over the prewarm's span in steps of Time.fixedDeltaTime,
    // a burst dated at the start of the step it falls in and a rate birth at its own time; no
    // particle is simulated step by step. Each particle then moves over its whole age at once
    // (under constant gravity, ½ g t²), keeps its place in the buffer when it is older than its
    // own start lifetime (with no lifetime left; the next update removes it), and draws its start
    // lifetime before its random seed. The clock ends just before the loop's end.
    public sealed partial class ParticleSystem
    {
        /// <summary>A prewarm in procedural mode is running: particles are aged without moving,
        /// their age in seconds kept in <see cref="Particle.m_AgePercent"/> until it ends.</summary>
        bool m_ProceduralPrewarm;

        /// <summary>Intervals the trajectory of a procedurally prewarmed particle is integrated
        /// over: exact for constant rates (a linear integrand), close for module curves.</summary>
        const int ProceduralIntervals = 64;
        /// <summary>How long before the loop's end a procedural prewarm's clock stops: a few float
        /// steps of a loop's clock, far below anything a record resolves.</summary>
        const float ProceduralEndMargin = 1e-5f;

        /// <summary>Whether a prewarm runs in procedural mode.</summary>
        bool PrewarmsProcedurally => Compat.ParticlePrewarmMeasured && !m_ProceduralLost && ProceduralSettings();

        void ProceduralPrewarm(float span, float duration)
        {
            float step = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            m_Prewarming = true;
            m_ProceduralPrewarm = true;
            try
            {
                float remaining = span;
                for (int guard = 0; remaining > step + ProceduralEndMargin && guard < 1000000; guard++)
                {
                    Step(step, 0f);
                    remaining -= step;
                }
                // The last step stops just before the loop's end, so a birth due there comes with
                // the next update (as do its random draws).
                float last = Mathf.Min(remaining, duration - m_Time) - ProceduralEndMargin;
                if (last > 0f) Step(last, 0f);
            }
            finally
            {
                m_ProceduralPrewarm = false;
                m_Prewarming = false;
            }
            var context = MakeContext();
            for (int i = 0; i < m_Count; i++)
            {
                BeginModuleSample(i, m_Particles[i].m_RandomSeed);
                MoveOverAge(ref m_Particles[i], m_Particles[i].m_AgePercent, ref context);
            }
        }

        /// <summary>During a procedural prewarm, ages the particles by a step without moving them.</summary>
        void AgeProcedurally(float dt)
        {
            for (int i = 0; i < m_Count; i++) m_Particles[i].m_AgePercent += dt;
        }

        /// <summary>Moves a newborn particle that has lived <paramref name="age"/> seconds over
        /// its whole age: its velocity and position follow the gravity, force and velocity
        /// modules over the age (trapezoids of <see cref="ProceduralIntervals"/>), its rotation
        /// the rotation module. Its life is spent at most.</summary>
        void MoveOverAge(ref Particle p, float age, ref StepContext c)
        {
            p.m_AgePercent = 0f;
            p.InitializeAge();
            float h = age / ProceduralIntervals;
            var v0 = p.m_Velocity;
            var velocity = v0;
            var position = p.m_Position;
            var turned = Vector3.zero;
            RatesAt(ref p, 0f, ref c, out var accel, out var animated, out float speedModifier, out var angular);
            var previousRate = (velocity + animated) * speedModifier;
            var previousAccel = accel;
            var previousAngular = angular;
            for (int k = 1; k <= ProceduralIntervals; k++)
            {
                float t = h * k;
                RatesAt(ref p, t, ref c, out accel, out animated, out speedModifier, out angular);
                velocity += (previousAccel + accel) * (0.5f * h);
                var rate = (velocity + animated) * speedModifier;
                position += (previousRate + rate) * (0.5f * h);
                turned += (previousAngular + angular) * (0.5f * h);
                previousRate = rate;
                previousAccel = accel;
                previousAngular = angular;
            }
            p.m_Position = position;
            p.m_Velocity = velocity;
            p.m_AnimatedVelocity = animated;
            p.m_AngularVelocity = angular;
            p.m_Rotation += turned;
            p.m_AgePercent = Mathf.Min(age * p.m_InvStartLifetime * 100f, 100f);
            p.ExportLifetime();
        }

        /// <summary>A particle's rates at <paramref name="age"/> seconds: the acceleration of its
        /// own velocity (gravity and force modules), its animated velocity, the speed modifier
        /// and the angular velocity, through the same module rules as a step.</summary>
        void RatesAt(ref Particle p, float age, ref StepContext c, out Vector3 accel, out Vector3 animated, out float speedModifier, out Vector3 angular)
        {
            var probe = p;
            probe.m_Velocity = Vector3.zero;
            float age01 = Mathf.Clamp01(age * p.m_InvStartLifetime);
            // A step of one second from rest: the velocity it ends with is the acceleration.
            speedModifier = Move(ref probe, 1f, ref c, age01, out var moving);
            accel = probe.m_Velocity;
            animated = probe.m_AnimatedVelocity;
            angular = m_Rotation.enabled || m_RotationBySpeed.enabled ? AngularVelocity(in p, age01, moving) : p.m_AngularVelocity;
        }
    }
}
