#pragma warning disable CS0612 // the obsolete shell shape types still appear in data
using System;
using Unbound.Runtime;
using R = Unbound.Runtime.ParticleRandom;

namespace UnityEngine
{
    // Spaces: a particle lives in its system's simulation space. "Emitter space" is the system
    // transform's frame with the scale its scaling mode selects (hierarchy: lossy scale, local:
    // local scale, shape: none). Local simulation keeps particles in emitter space; world
    // simulation keeps them in world space; custom keeps them in the custom transform's space.
    public sealed partial class ParticleSystem
    {
        /// <summary>World gravity (Physics.gravity / Physics2D.gravity defaults).</summary>
        internal static Vector3 s_Gravity3D = new Vector3(0f, -9.81f, 0f);
        internal static Vector3 s_Gravity2D = new Vector3(0f, -9.81f, 0f);

        Vector3 m_LastPosition;
        bool m_HasLastPosition;
        /// <summary>Where the emitter was when the current frame (or Simulate call) began.</summary>
        Vector3 m_FrameStartPosition;
        /// <summary>The part of the frame's emitter movement the current step covers, and its length:
        /// a particle is emitted where the emitter was at its birth (measured).</summary>
        float m_StepFrom, m_StepTo = 1f, m_StepDt;
        /// <summary>Errors already logged for the current step (Unity logs once per update pass).</summary>
        bool m_LoggedNewbornError;
        /// <summary>The emitter's world velocity (from its movement, or the custom value).</summary>
        internal Vector3 m_EmitterVelocity;
        /// <summary>The emitter's world movement of the current frame, spread over its steps.</summary>
        float m_FrameDistance;
        /// <summary>What the particles of the current step share (spaces, gravity, ...).</summary>
        StepContext m_Context;
        bool m_HasContext;
        /// <summary>The length of the last step.</summary>
        internal float m_LastStepDt;

        internal void FrameUpdate()
        {
            float raw = m_Main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            TrackEmitter(raw);
            if (m_IsPaused) return;
            if (!m_IsPlaying && m_Count == 0 && !HasAliveTrails) return;
            float dt = raw * m_Main.simulationSpeed;
            if (dt <= 0f) return;
            m_CollisionEvents.Clear();
            foreach (var list in m_TriggerParticles) list.Clear();
            m_CollectTriggers = true;
            try { Advance(dt, m_FrameDistance, true); }
            finally { m_CollectTriggers = false; }
        }

        void TrackEmitter(float dt)
        {
            if (m_GameObject == null) return;
            var position = m_GameObject.m_Transform.position;
            if (!m_HasLastPosition)
            {
                m_LastPosition = position;
                m_HasLastPosition = true;
            }
            var delta = position - m_LastPosition;
            m_FrameStartPosition = m_LastPosition;
            m_LastPosition = position;
            m_FrameDistance = delta.magnitude;
            if (m_Main.emitterVelocityMode == ParticleSystemEmitterVelocityMode.Custom)
                m_EmitterVelocity = m_Main.customEmitterVelocity;
            else if (dt > 0f)
                m_EmitterVelocity = delta / dt;
        }

        /// <summary>Advances by <paramref name="dt"/> (simulation speed applied) in equal steps no
        /// longer than Time.maximumParticleDeltaTime: Unity splits 0.05 s into two 0.025 s steps at the
        /// default 0.03 (measured: the system's time accumulates exactly those steps).</summary>
        internal void Advance(float dt, float distance, bool stopWhenPaused, float from = 0f, float to = 1f)
        {
            float max = Time.maximumParticleDeltaTime;
            int steps = max > 0f ? (int)Math.Ceiling(dt / max) : 1;
            steps = Math.Clamp(steps, 1, 100000);
            float step = dt / steps;
            float share = distance / steps;
            try
            {
                for (int i = 0; i < steps; i++)
                {
                    m_StepFrom = from + (to - from) * i / steps;
                    m_StepTo = from + (to - from) * (i + 1) / steps;
                    m_StepDt = step;
                    Step(step, share);
                    if (stopWhenPaused && (m_IsPaused || !m_Registered)) break;
                }
            }
            finally
            {
                m_StepFrom = 0f;
                m_StepTo = 1f;
                m_StepDt = 0f;
            }
        }

        /// <summary>The emitter's world position a particle that has lived <paramref name="age"/>
        /// seconds of the current step was born at.</summary>
        Vector3 EmitterPositionAt(float age)
        {
            var position = m_GameObject.m_Transform.position;
            if (m_StepDt <= 0f || !m_HasLastPosition) return position;
            float f = Mathf.Clamp01(m_StepTo - (m_StepTo - m_StepFrom) * (age / m_StepDt));
            return Vector3.LerpUnclamped(m_FrameStartPosition, position, f);
        }

        /// <summary>One simulation step: existing particles age and move (those whose remaining
        /// lifetime drops below zero die), then the step's new particles are emitted, each advanced by
        /// the part of the step it has lived.</summary>
        internal void Step(float dt, float distance)
        {
            if (m_Trails.enabled || HasAliveTrails) m_TrailTime += dt;
            m_LastStepDt = dt;
            if (m_Noise.enabled) m_NoiseScroll += m_Noise.scrollSpeed.Evaluate(Mathf.Clamp01((m_Time + dt) / Mathf.Max(m_Main.duration, .0001f)), 1f) * dt;
            m_Context = MakeContext();
            m_HasContext = true;
            m_LoggedNewbornError = false;
            m_LoggedUpdateError = false;
            try
            {
                UpdateParticles(dt);
                if (m_IsPlaying)
                    AdvanceEmission(dt, distance);
                if (m_TrailList.Count > 0) UpdateTrails();
            }
            finally { m_HasContext = false; }
            if (m_IsPlaying && m_StopEmitting && m_Count == 0)
            {
                m_IsPlaying = false;
                m_StopEmitting = false;
                OnStopped();
            }
        }

        // ------------------------------------------------------------------ spaces

        internal Vector3 EmitterScale()
        {
            var t = m_GameObject.m_Transform;
            switch (m_Main.scalingMode)
            {
                case ParticleSystemScalingMode.Hierarchy: return t.lossyScale;
                case ParticleSystemScalingMode.Local: return t.localScale;
                default: return Vector3.one;
            }
        }

        /// <summary>Emitter space to world.</summary>
        internal Matrix4x4 EmitterMatrix()
        {
            var t = m_GameObject.m_Transform;
            return Matrix4x4.TRS(t.position, t.rotation, EmitterScale());
        }

        /// <summary>Simulation space to world.</summary>
        internal Matrix4x4 SimulationToWorld()
        {
            switch (m_Main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return Matrix4x4.identity;
                case ParticleSystemSimulationSpace.Custom when m_Main.customSimulationSpace != null:
                    return m_Main.customSimulationSpace.localToWorldMatrix;
                default:
                    return EmitterMatrix();
            }
        }

        /// <summary>The rotation from world to simulation space (directions only).</summary>
        Quaternion WorldToSimulationRotation()
        {
            switch (m_Main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return Quaternion.identity;
                case ParticleSystemSimulationSpace.Custom when m_Main.customSimulationSpace != null:
                    return Quaternion.Inverse(m_Main.customSimulationSpace.rotation);
                default:
                    return Quaternion.Inverse(m_GameObject.m_Transform.rotation);
            }
        }

        /// <summary>A module vector given in `space` (local or world) expressed in simulation space.</summary>
        Vector3 ModuleToSimulation(Vector3 v, ParticleSystemSimulationSpace space, Quaternion worldToSim)
        {
            bool simWorld = m_Main.simulationSpace == ParticleSystemSimulationSpace.World;
            if (space == ParticleSystemSimulationSpace.World)
                return simWorld ? v : worldToSim * v;
            // Local module space: the emitter's axes.
            return simWorld ? m_GameObject.m_Transform.rotation * v : (m_Main.simulationSpace == ParticleSystemSimulationSpace.Local ? v : worldToSim * (m_GameObject.m_Transform.rotation * v));
        }

        // ------------------------------------------------------------------ emission over time

        /// <summary>Advances the playback time by a step and emits what falls in it. The time wraps at
        /// the duration when looping; otherwise emission ends there. Sub-emitters' time runs without
        /// emitting (their parent emits for them).</summary>
        void AdvanceEmission(float dt, float distance)
        {
            if (m_DelayRemaining > 0f)
            {
                float d = Mathf.Min(dt, m_DelayRemaining);
                m_DelayRemaining -= d;
                m_TotalTime += d;
                distance *= dt > 0f ? (dt - d) / dt : 0f;
                dt -= d;
                if (dt <= 0f) return;
            }
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            // The time keeps running after Stop (measured); only emission ends.
            bool emit = m_SubEmitterParent == null && !m_StopEmitting && m_Emission.enabled;
            float step = dt;
            float t0 = m_Time;
            float t1 = t0 + dt;
            m_TotalTime += dt;
            int guard = 0;
            while (t1 >= duration && guard++ < 10000)
            {
                if (!m_Main.loop)
                {
                    if (emit)
                    {
                        EmitRate(t0, duration, false, 0f, duration);
                        EmitOver(t0, duration, 0f, step, distance * ((duration - t0) / step));
                    }
                    m_Time = duration;
                    m_StopEmitting = true;
                    return;
                }
                float rest = t1 - duration;
                if (emit)
                {
                    EmitRate(t0, duration, false, rest, duration);
                    EmitOver(t0, duration, rest, step, distance * ((duration - t0) / step));
                }
                m_Loop++;
                CarryEmissionPhase(duration);
                Array.Clear(m_BurstCycles, 0, m_BurstCycles.Length);
                t0 = 0f;
                t1 = rest;
            }
            if (emit)
            {
                EmitRate(t0, t1, true, 0f, duration);
                EmitOver(t0, t1, 0f, step, distance * ((t1 - t0) / step));
            }
            m_Time = t1;
        }

        /// <summary>The emission count carried into the current loop: rate × time + phase counts its
        /// particles. A loop's end is exclusive and the next loop's start inclusive, so a particle
        /// due exactly at the end starts the next loop, and the rest of a particle carries over
        /// (measured: births evenly spaced across loops, the one at the end taking the next loop's
        /// start values).</summary>
        internal float m_EmitPhase;
        /// <summary>The next rate segment starts a loop (its start is inclusive).</summary>
        internal bool m_LoopStart;

        void CarryEmissionPhase(float duration)
        {
            var rate = m_Emission.rateOverTime;
            m_LoopStart = true;
            if (rate.mode != ParticleSystemCurveMode.Constant || !(rate.constant > 0f))
            {
                m_EmitPhase = 0f;
                return;
            }
            float due = rate.constant * duration + m_EmitPhase;
            m_EmitPhase = due - ((float)Math.Ceiling(due) - 1f);
        }

        /// <summary>Restarts the loop count at the current time (prewarm resets the playback time
        /// but keeps the emission's phase).</summary>
        internal void KeepEmissionPhase()
        {
            var rate = m_Emission.rateOverTime;
            if (rate.mode != ParticleSystemCurveMode.Constant || !(rate.constant > 0f) || m_LoopStart && m_Time == 0f) return;
            float due = rate.constant * m_Time + m_EmitPhase;
            m_EmitPhase = due - (float)Math.Floor(due);
            m_LoopStart = false;
        }

        /// <summary>Rate emission for the playback time (a, b] (b excluded at a loop's end). A
        /// particle is due when rate × time + phase reaches a whole number and has lived from then
        /// to b, plus <paramref name="extraAge"/> (the step's time after b); its start speed is taken
        /// at its birth, the other start values at b (measured).</summary>
        void EmitRate(float a, float b, bool endIncluded, float extraAge, float duration)
        {
            var rateCurve = m_Emission.rateOverTime;
            if (!(b > a)) return;
            bool startIncluded = m_LoopStart;
            m_LoopStart = false;
            if (rateCurve.mode == ParticleSystemCurveMode.Constant)
            {
                float rate = rateCurve.constant;
                if (!(rate > 0f)) return;
                float phase = m_EmitPhase;
                float ea = rate * a + phase;
                float eb = rate * b + phase;
                int m0 = startIncluded ? (int)Math.Ceiling(ea) : (int)Math.Floor(ea) + 1;
                int m1 = endIncluded ? (int)Math.Floor(eb) : (int)Math.Ceiling(eb) - 1;
                for (int m = m0; m <= m1; m++)
                {
                    m_SpeedNt = Mathf.Clamp01((m - phase) / rate / duration);
                    EmitParticles(1, (eb - m) / rate + extraAge, b, default, false, m - m0, m1 - m0 + 1);
                }
            }
            else
            {
                float rate = rateCurve.Evaluate(Mathf.Clamp01(a / duration), NextFloat());
                if (!(rate > 0f)) return;
                m_RateAccumulator += rate * (b - a);
                int n = (int)m_RateAccumulator;
                for (int k = 1; k <= n; k++)
                {
                    float age = (m_RateAccumulator - k) / rate;
                    m_SpeedNt = Mathf.Clamp01((b - age) / duration);
                    EmitParticles(1, age + extraAge, b, default, false, k - 1, n);
                }
                m_RateAccumulator -= n;
            }
            m_SpeedNt = -1f;
        }

        /// <summary>Distance and burst emission for the playback time (a, b), plus
        /// <paramref name="extraAge"/> (the step's time after b). A burst fires in the step its time
        /// falls in, its first cycle aged from that time, its repeats aged the whole step (measured).</summary>
        void EmitOver(float a, float b, float extraAge, float step, float distance)
        {
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            float perDistance = m_Emission.rateOverDistance.Evaluate(Mathf.Clamp01(a / duration), NextFloat());
            if (perDistance > 0f && distance > 0f)
            {
                // Particles are due along the path, aged from where the emitter passed them.
                float before = m_DistanceAccumulator;
                float gained = perDistance * distance;
                m_DistanceAccumulator += gained;
                int n = (int)m_DistanceAccumulator;
                float span = b - a;
                for (int k = 1; k <= n; k++)
                {
                    float at = Mathf.Clamp01((k - before) / gained);
                    float age = (1f - at) * span + extraAge;
                    m_SpeedNt = Mathf.Clamp01((a + at * span) / duration);
                    EmitParticles(1, age, b, default, false, k - 1, n);
                }
                m_DistanceAccumulator -= n;
            }

            var bursts = m_Emission.bursts;
            if (bursts.Count > 0)
            {
                if (m_BurstCycles.Length != bursts.Count) Array.Resize(ref m_BurstCycles, bursts.Count);
                for (int i = 0; i < bursts.Count; i++)
                {
                    var burst = bursts[i];
                    int cycles = burst.cycleCount; // 0: forever
                    float interval = Mathf.Max(burst.m_RepeatInterval, 0.0001f);
                    int c = m_BurstCycles[i];
                    while (cycles == 0 || c < cycles)
                    {
                        float at = burst.m_Time + c * interval;
                        if (at >= b || at >= duration) break;
                        if (burst.probability >= 1f || NextFloat() < burst.probability)
                        {
                            int count = BurstCount(burst.m_Count, at / duration);
                            float age = (c == 0 ? b - at : step) + extraAge;
                            m_SpeedNt = Mathf.Clamp01(at / duration);
                            if (count > 0) EmitParticles(count, age, b, default, false);
                        }
                        c++;
                        if (cycles == 0 && c > 100000) break;
                    }
                    m_BurstCycles[i] = c;
                }
            }
            m_SpeedNt = -1f;
        }

        int BurstCount(MinMaxCurve count, float nt)
        {
            float r = NextFloat();
            if (count.mode == ParticleSystemCurveMode.TwoConstants)
            {
                float min = Mathf.Min(count.constantMin, count.constantMax), max = Mathf.Max(count.constantMin, count.constantMax);
                return Mathf.Clamp((int)Math.Floor(Mathf.Lerp(min, max + 1f, r)), (int)min, (int)max);
            }
            return Mathf.Max(0, Mathf.RoundToInt(count.Evaluate(nt, r)));
        }

        // ------------------------------------------------------------------ emitting particles

        /// <summary>Emits <paramref name="count"/> particles born at playback time
        /// <paramref name="birthTime"/> (their start values are taken there) that have already lived
        /// <paramref name="preAge"/> seconds.</summary>
        internal void EmitParticles(int count, float preAge, float birthTime, EmitParams overrides, bool hasOverrides, int spreadIndex = -1, int spreadCount = 0)
        {
            if (m_GameObject == null) return;
            var t = m_GameObject.m_Transform;
            var emitter = Matrix4x4.TRS(EmitterPositionAt(preAge), t.rotation, EmitterScale());
            float nt = Mathf.Clamp01(birthTime / Mathf.Max(m_Main.duration, 0.0001f));
            if (!m_HasContext) m_Context = MakeContext();
            m_Newborn = true;
            m_ShapeTime = m_TotalTime - preAge;
            for (int k = 0; k < count; k++)
            {
                int index = Allocate();
                if (index < 0) break;
                int si = spreadIndex >= 0 ? spreadIndex : k;
                int sn = spreadIndex >= 0 ? spreadCount : count;
                if (!InitParticle(ref m_Particles[index], emitter, nt, hasOverrides, ref overrides, si, sn, null) ||
                    !Integrate(ref m_Particles[index], preAge, ref m_Context))
                    RemoveAt(index);
                else StartTrail(ref m_Particles[index]);
            }
            m_Newborn = false;
        }

        /// <summary>Integrating newborns (they share one error report per step).</summary>
        bool m_Newborn;
        /// <summary>The total time a particle being emitted was born at (the shape's arc and edge
        /// modes move with it).</summary>
        float m_ShapeTime;
        /// <summary>The normalized time the start speed is taken at: the start of the emitting step
        /// (measured: the other start values are taken at its end).</summary>
        float m_SpeedNt = -1f;

        /// <summary>A new particle with the main and shape modules' start values. `origin` (world)
        /// replaces the emitter's position (sub-emitters emit from their parent particle).</summary>
        bool InitParticle(ref Particle p, Matrix4x4 emitter, float nt, bool hasOverrides, ref EmitParams o, int spreadIndex, int spreadCount, Vector3? origin)
        {
            uint seed = hasOverrides && o.m_RandomSeedSet ? o.m_Particle.m_RandomSeed : NextUInt();
            var M = m_Main;

            var noiseSum = p.m_NoiseSum;
            p = default;
            p.m_NoiseSum = noiseSum;
            p.m_RandomSeed = seed;
            float lifetime = M.startLifetime.Evaluate(nt, R.Value(seed, R.Lifetime));
            if (m_LifetimeByEmitterSpeed.enabled)
            {
                var range = m_LifetimeByEmitterSpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, m_EmitterVelocity.magnitude);
                lifetime *= m_LifetimeByEmitterSpeed.curve.Evaluate(s, R.Value(seed, R.Inherit + 100));
            }
            float speed = M.startSpeed.Evaluate(m_SpeedNt >= 0f ? m_SpeedNt : nt, R.Value(seed, R.Speed));
            Vector3 size;
            if (M.startSize3D)
                size = new Vector3(M.startSizeX.Evaluate(nt, R.Value(seed, R.SizeX)), M.startSizeY.Evaluate(nt, R.Value(seed, R.SizeY)), M.startSizeZ.Evaluate(nt, R.Value(seed, R.SizeZ)));
            else
            {
                float s = M.startSizeX.Evaluate(nt, R.Value(seed, R.SizeX));
                size = new Vector3(s, s, s);
            }
            Vector3 rotation = M.startRotation3D
                ? new Vector3(M.startRotationX.Evaluate(nt, R.Value(seed, R.RotX)), M.startRotationY.Evaluate(nt, R.Value(seed, R.RotY)), M.startRotationZ.Evaluate(nt, R.Value(seed, R.RotZ)))
                : new Vector3(0f, 0f, M.startRotationZ.Evaluate(nt, R.Value(seed, R.RotZ)));
            if (M.flipRotation > 0f && R.Value(seed, R.Flip) < M.flipRotation)
            {
                rotation = -rotation;
                p.m_Flags |= Particle.FlagFlipRotation;
            }
            var color = M.startColor.Evaluate(nt, R.Value(seed, R.Color));

            SampleShape(spreadIndex, spreadCount, out var pos, out var dir);
            var shapeOffset = pos;
            var velocity = dir * speed;
            if (m_Shape.enabled && m_Shape.alignToDirection && dir.sqrMagnitude > 1e-12f)
            {
                // The particle's 3D rotation turns its forward axis to its start direction.
                var e = Quaternion.LookRotation(dir).eulerAngles;
                rotation += new Vector3(Signed(e.x), Signed(e.y), Signed(e.z)) * Mathf.Deg2Rad;
            }

            // Emitter space to simulation space.
            var simSpace = M.simulationSpace;
            if (simSpace == ParticleSystemSimulationSpace.Custom && M.customSimulationSpace == null) simSpace = ParticleSystemSimulationSpace.Local;
            if (origin.HasValue)
            {
                // Emitting from a world point with this system's orientation and scale.
                var t = m_GameObject.m_Transform;
                var frame = Matrix4x4.TRS(origin.Value, t.rotation, EmitterScale());
                var worldPos = frame.MultiplyPoint3x4(pos);
                var worldVel = frame.MultiplyVector(velocity);
                FromWorld(simSpace, emitter, worldPos, worldVel, out pos, out velocity);
            }
            else if (simSpace != ParticleSystemSimulationSpace.Local)
            {
                // The emitter's scale places particles but does not change their speed (measured).
                var worldVelocity = m_GameObject.m_Transform.rotation * velocity;
                FromWorld(simSpace, emitter, emitter.MultiplyPoint3x4(pos), worldVelocity, out pos, out velocity);
            }

            if (m_InheritVelocity.enabled && m_InheritVelocity.mode == ParticleSystemInheritVelocityMode.Initial && simSpace != ParticleSystemSimulationSpace.Local)
            {
                var inherited = m_EmitterVelocity * m_InheritVelocity.curve.Evaluate(nt, R.Value(seed, R.Inherit));
                velocity += simSpace == ParticleSystemSimulationSpace.World ? inherited : WorldToSimulationRotation() * inherited;
            }

            if (hasOverrides)
            {
                if (o.m_PositionSet)
                {
                    if (o.m_ApplyShapeToPosition)
                    {
                        var offset = simSpace == ParticleSystemSimulationSpace.Local ? shapeOffset : emitter.MultiplyVector(shapeOffset);
                        pos = o.m_Particle.m_Position + offset;
                    }
                    else pos = o.m_Particle.m_Position;
                }
                if (o.m_VelocitySet) velocity = o.m_Particle.m_Velocity;
                if (o.m_StartLifetimeSet) lifetime = o.m_Particle.m_StartLifetime;
                if (o.m_StartSizeSet) size = o.m_Particle.m_StartSize;
                if (o.m_RotationSet) rotation = o.m_Particle.m_Rotation;
                if (o.m_AngularVelocitySet) p.m_AngularVelocity = o.m_Particle.m_AngularVelocity;
                if (o.m_AxisOfRotationSet) p.m_AxisOfRotation = o.m_Particle.m_AxisOfRotation;
                if (o.m_StartColorSet) color = o.m_Particle.m_StartColor;
                if (o.m_MeshIndexSet)
                {
                    p.m_MeshIndex = o.m_Particle.m_MeshIndex;
                    p.m_Flags |= Particle.FlagMeshIndexSet;
                }
            }
            if (!(hasOverrides && o.m_AxisOfRotationSet)) p.m_AxisOfRotation = Vector3.forward;

            p.m_Position = pos;
            p.m_Velocity = velocity;
            p.m_InitialVelocity = velocity;
            p.m_StartSize = size;
            p.m_Rotation = rotation;
            p.m_StartColor = color;
            p.m_StartLifetime = lifetime;
            p.m_Lifetime = lifetime;
            p.m_InvStartLifetime = 1f / lifetime;
            p.m_AgePercent = 0f;
            p.m_Flags |= Particle.FlagAgeInitialized;
            return lifetime > 0f;
        }

        void FromWorld(ParticleSystemSimulationSpace simSpace, Matrix4x4 emitter, Vector3 worldPos, Vector3 worldVel, out Vector3 pos, out Vector3 vel)
        {
            switch (simSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    pos = worldPos;
                    vel = worldVel;
                    return;
                case ParticleSystemSimulationSpace.Custom:
                {
                    var inv = m_Main.customSimulationSpace.worldToLocalMatrix;
                    pos = inv.MultiplyPoint3x4(worldPos);
                    vel = inv.MultiplyVector(worldVel);
                    return;
                }
                default:
                {
                    var inv = emitter.inverse;
                    pos = inv.MultiplyPoint3x4(worldPos);
                    vel = inv.MultiplyVector(worldVel);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ the shape module

        /// <summary>A start position and direction in emitter space.</summary>
        void SampleShape(int spreadIndex, int spreadCount, out Vector3 pos, out Vector3 dir)
        {
            var S = m_Shape;
            if (!S.enabled)
            {
                pos = Vector3.zero;
                dir = Vector3.forward;
                return;
            }
            float radius = S.radius;
            float thickness = Mathf.Clamp01(S.radiusThickness);
            switch (S.shapeType)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.SphereShell:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.HemisphereShell:
                {
                    dir = RandomUnitVector();
                    if (S.shapeType == ParticleSystemShapeType.Hemisphere || S.shapeType == ParticleSystemShapeType.HemisphereShell)
                        dir.z = Mathf.Abs(dir.z);
                    bool shell = S.shapeType == ParticleSystemShapeType.SphereShell || S.shapeType == ParticleSystemShapeType.HemisphereShell;
                    float r = shell ? radius : radius * Mathf.Lerp(1f - thickness, 1f, Mathf.Pow(NextFloat(), 1f / 3f));
                    pos = dir * r;
                    break;
                }
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeShell:
                case ParticleSystemShapeType.ConeVolume:
                case ParticleSystemShapeType.ConeVolumeShell:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    bool shell = S.shapeType == ParticleSystemShapeType.ConeShell || S.shapeType == ParticleSystemShapeType.ConeVolumeShell;
                    float r01 = shell ? 1f : Mathf.Lerp(1f - thickness, 1f, Mathf.Sqrt(NextFloat()));
                    float angle = Mathf.Clamp(S.angle, 0f, 90f) * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
                    // Directions fan out with the distance from the axis, reaching the cone angle at the rim.
                    float tilt = radius > 1e-4f ? angle * r01 : angle * Mathf.Sqrt(NextFloat());
                    dir = new Vector3(cos * Mathf.Sin(tilt), sin * Mathf.Sin(tilt), Mathf.Cos(tilt));
                    pos = new Vector3(cos * radius * r01, sin * radius * r01, 0f);
                    if (S.shapeType == ParticleSystemShapeType.ConeVolume || S.shapeType == ParticleSystemShapeType.ConeVolumeShell)
                    {
                        float along = NextFloat() * S.length;
                        pos += dir * (along / Mathf.Max(dir.z, 1e-4f));
                    }
                    break;
                }
                case ParticleSystemShapeType.Box:
                    pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, NextFloat() - 0.5f);
                    dir = Vector3.forward;
                    break;
                case ParticleSystemShapeType.BoxShell:
                {
                    pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, NextFloat() - 0.5f);
                    int axis = (int)(NextFloat() * 3f) % 3;
                    pos[axis] = NextFloat() < 0.5f ? -0.5f : 0.5f;
                    dir = Vector3.forward;
                    break;
                }
                case ParticleSystemShapeType.BoxEdge:
                {
                    pos = new Vector3(NextFloat() < 0.5f ? -0.5f : 0.5f, NextFloat() < 0.5f ? -0.5f : 0.5f, NextFloat() < 0.5f ? -0.5f : 0.5f);
                    int axis = (int)(NextFloat() * 3f) % 3;
                    pos[axis] = NextFloat() - 0.5f;
                    dir = Vector3.forward;
                    break;
                }
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.CircleEdge:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    float r01 = S.shapeType == ParticleSystemShapeType.CircleEdge ? 1f : Mathf.Lerp(1f - thickness, 1f, Mathf.Sqrt(NextFloat()));
                    dir = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0f);
                    pos = dir * (radius * r01);
                    break;
                }
                case ParticleSystemShapeType.SingleSidedEdge:
                {
                    // Along the edge, the loop modes move 2 × speed units a second (measured).
                    float travel = S.radiusSpeed.Evaluate(0f, 1f) * m_ShapeTime / Mathf.Max(radius, 1e-4f);
                    float u;
                    switch (S.radiusMode)
                    {
                        case ParticleSystemShapeMultiModeValue.Loop: u = Frac(travel); break;
                        case ParticleSystemShapeMultiModeValue.PingPong: u = Mathf.PingPong(travel, 1f); break;
                        case ParticleSystemShapeMultiModeValue.BurstSpread: u = spreadCount > 1 ? spreadIndex / (float)(spreadCount - 1) : 0f; break;
                        default: u = NextFloat(); break;
                    }
                    if (S.radiusSpread > 0f) u = Mathf.Floor(u / S.radiusSpread) * S.radiusSpread;
                    pos = new Vector3(Mathf.Lerp(-radius, radius, u), 0f, 0f);
                    dir = Vector3.up;
                    break;
                }
                case ParticleSystemShapeType.Donut:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    float theta = NextFloat() * Mathf.PI * 2f;
                    float tube = S.donutRadius * Mathf.Lerp(1f - thickness, 1f, Mathf.Sqrt(NextFloat()));
                    var ring = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0f);
                    dir = ring * Mathf.Cos(theta) + Vector3.forward * Mathf.Sin(theta);
                    pos = ring * radius + dir * tube;
                    break;
                }
                case ParticleSystemShapeType.Rectangle:
                    pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, 0f);
                    dir = Vector3.forward;
                    break;
                default:
                    SampleMeshShape(out pos, out dir);
                    break;
            }

            if (S.randomPositionAmount > 0f) pos += RandomUnitVector() * (S.randomPositionAmount * Mathf.Pow(NextFloat(), 1f / 3f));

            var rot = Quaternion.Euler(S.rotation);
            var shapeMatrix = Matrix4x4.TRS(S.position, rot, S.scale);
            pos = shapeMatrix.MultiplyPoint3x4(pos);
            var d = shapeMatrix.MultiplyVector(dir);
            dir = d.sqrMagnitude > 1e-12f ? d.normalized : rot * dir;

            if (S.sphericalDirectionAmount > 0f)
            {
                var outward = (pos - S.position).sqrMagnitude > 1e-12f ? (pos - S.position).normalized : dir;
                dir = Vector3.Lerp(dir, outward, S.sphericalDirectionAmount).normalized;
            }
            if (S.randomDirectionAmount > 0f)
            {
                var random = Vector3.Lerp(dir, RandomUnitVector(), S.randomDirectionAmount);
                dir = random.sqrMagnitude > 1e-12f ? random.normalized : dir;
            }
            if (m_Main.scalingMode == ParticleSystemScalingMode.Shape)
                pos = Vector3.Scale(pos, m_GameObject.m_Transform.lossyScale);
        }

        /// <summary>The angle (degrees) around the arc for a new particle, by the arc's mode.</summary>
        float ArcAngle(int index, int count)
        {
            var S = m_Shape;
            float arc = S.arc;
            float angle;
            switch (S.arcMode)
            {
                // Speed is in turns per second, cycling over the arc (measured).
                case ParticleSystemShapeMultiModeValue.Loop:
                    angle = Mathf.Repeat(S.arcSpeed.Evaluate(0f, 1f) * m_ShapeTime * 360f, Mathf.Max(arc, 1e-4f));
                    break;
                case ParticleSystemShapeMultiModeValue.PingPong:
                    angle = Mathf.PingPong(S.arcSpeed.Evaluate(0f, 1f) * m_ShapeTime * 360f, arc);
                    break;
                case ParticleSystemShapeMultiModeValue.BurstSpread:
                    angle = count > 0 ? index / (float)count * arc : 0f;
                    break;
                default:
                    angle = NextFloat() * arc;
                    break;
            }
            if (S.arcSpread > 0f)
            {
                float spread = S.arcSpread * arc;
                angle = Mathf.Floor(angle / spread) * spread;
            }
            return angle;
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        static float Signed(float degrees) => degrees > 180f ? degrees - 360f : degrees;

        /// <summary>Mesh, renderer and sprite shapes: a random vertex of the mesh (with its normal),
        /// or a point of the sprite's rectangle; a unit box when there is nothing to sample.</summary>
        void SampleMeshShape(out Vector3 pos, out Vector3 dir)
        {
            var S = m_Shape;
            Mesh mesh = S.mesh;
            if (S.shapeType == ParticleSystemShapeType.MeshRenderer && S.meshRenderer != null)
            {
                var filter = S.meshRenderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.m_Mesh : null;
            }
            if ((S.shapeType == ParticleSystemShapeType.Mesh || S.shapeType == ParticleSystemShapeType.MeshRenderer) && mesh != null && mesh.m_Vertices.Length > 0)
            {
                int i = Math.Min((int)(NextFloat() * mesh.m_Vertices.Length), mesh.m_Vertices.Length - 1);
                pos = mesh.m_Vertices[i];
                dir = mesh.m_Normals != null && i < mesh.m_Normals.Length && mesh.m_Normals[i].sqrMagnitude > 0f ? mesh.m_Normals[i].normalized : Vector3.forward;
                pos += dir * S.normalOffset;
                return;
            }
            Sprite sprite = S.shapeType == ParticleSystemShapeType.SpriteRenderer && S.spriteRenderer != null ? S.spriteRenderer.sprite : S.sprite;
            if ((S.shapeType == ParticleSystemShapeType.Sprite || S.shapeType == ParticleSystemShapeType.SpriteRenderer) && sprite != null)
            {
                var b = sprite.bounds;
                pos = new Vector3(Mathf.Lerp(b.min.x, b.max.x, NextFloat()), Mathf.Lerp(b.min.y, b.max.y, NextFloat()), 0f);
                dir = Vector3.forward;
                return;
            }
            pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, NextFloat() - 0.5f);
            dir = Vector3.forward;
        }

        Vector3 RandomUnitVector()
        {
            float z = NextFloat() * 2f - 1f;
            float a = NextFloat() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        // ------------------------------------------------------------------ updating particles

        /// <summary>What every particle of a step shares.</summary>
        struct StepContext
        {
            public float Nt;
            public Quaternion WorldToSim;
            public Vector3 Gravity;
            public bool HasGravity;
            public Vector3 Center;
            public float ForceRandom;
            public float NoiseTime;
            public bool InheritCurrent;
            public bool BirthSubs;
        }

        StepContext MakeContext()
        {
            var c = new StepContext();
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            c.Nt = Mathf.Clamp01(m_Time / duration);
            c.WorldToSim = WorldToSimulationRotation();
            var gravityWorld = m_Main.gravitySource == ParticleSystemGravitySource.Physics2D ? s_Gravity2D : s_Gravity3D;
            c.HasGravity = m_Main.gravityModifier.mode != ParticleSystemCurveMode.Constant || m_Main.gravityModifier.constant != 0f;
            bool simWorld = m_Main.simulationSpace == ParticleSystemSimulationSpace.World;
            c.Gravity = simWorld ? gravityWorld : c.WorldToSim * gravityWorld;
            c.Center = simWorld ? m_GameObject.m_Transform.position : Vector3.zero;
            if (m_Main.simulationSpace == ParticleSystemSimulationSpace.Custom && m_Main.customSimulationSpace != null)
                c.Center = m_Main.customSimulationSpace.InverseTransformPoint(m_GameObject.m_Transform.position);
            c.InheritCurrent = m_InheritVelocity.enabled && m_InheritVelocity.mode == ParticleSystemInheritVelocityMode.Current && simWorld;
            c.ForceRandom = m_Force.enabled && m_Force.randomized ? NextFloat() : 0f;
            c.NoiseTime = m_NoiseScroll;
            c.BirthSubs = m_SubEmitters.enabled && HasSubEmitters(ParticleSystemSubEmitterType.Birth);
            return c;
        }

        /// <summary>Ages and moves the particles by a step; a particle whose remaining lifetime drops
        /// below zero dies and the last particle takes its place (Unity keeps one at exactly zero
        /// until the next step).</summary>
        void UpdateParticles(float dt)
        {
            if (m_Count == 0) return;
            bool deathSubs = m_SubEmitters.enabled && HasSubEmitters(ParticleSystemSubEmitterType.Death);
            for (int i = 0; i < m_Count;)
            {
                if ((m_Particles[i].m_Flags & Particle.FlagOrphanTrail) != 0) { i++; continue; }
                if (Integrate(ref m_Particles[i], dt, ref m_Context))
                {
                    i++;
                    continue;
                }
                if (deathSubs) TriggerSubEmitters(ParticleSystemSubEmitterType.Death, ref m_Particles[i]);
                if (KeepOrphanTrail(ref m_Particles[i])) { i++; continue; }
                EndTrail(ref m_Particles[i]);
                RemoveAt(i);
            }
        }

        /// <summary>Advances one particle by <paramref name="dt"/>; false when it dies. The lifetime
        /// curves are evaluated at the particle's age before the step (measured: velocity over
        /// lifetime moves a newborn not at all in its first step).</summary>
        bool Integrate(ref Particle p, float dt, ref StepContext c)
        {
            if ((p.m_Flags & Particle.FlagAgeInitialized) == 0) p.InitializeAge();
            if (dt == 0f) return true;
            float age = p.Age01;
            float ageBefore = p.m_StartLifetime - p.m_Lifetime;
            // Native stores age in percent, not a repeatedly subtracted remaining lifetime.
            // Exact boundaries such as .5 seconds in .025 steps otherwise die one step early.
            if (p.m_AgePercent <= 100f)
                p.m_AgePercent = Mathf.Min(p.m_AgePercent + p.m_InvStartLifetime * (dt * 100f), 100.00000762939453f);
            p.ExportLifetime();
            var ring = m_Main.ringBufferMode;
            if (ring == ParticleSystemRingBufferMode.LoopUntilReplaced && p.m_StartLifetime > 0f)
            {
                var range = m_Main.ringBufferLoopRange;
                float start = Mathf.Clamp01(Mathf.Min(range.x, range.y)), end = Mathf.Clamp01(Mathf.Max(range.x, range.y));
                if (end > start && p.Age01 >= end)
                {
                    p.m_AgePercent = start * 100f;
                    p.ExportLifetime();
                }
            }
            if (p.m_AgePercent > 100f)
            {
                if (ring != ParticleSystemRingBufferMode.PauseUntilReplaced) return false;
                p.m_AgePercent = 100f;
                p.m_Lifetime = 0f;
                return true;
            }
            uint seed = p.m_RandomSeed;
            var v = p.m_Velocity;
            var V = m_Velocity;
            var F = m_Force;

            if (c.HasGravity)
                v += c.Gravity * (m_Main.gravityModifier.Evaluate(c.Nt, R.Value(seed, R.Gravity)) * dt);

            if (F.enabled)
            {
                // The axes are evaluated in the first axis' mode: a constant reads a curve's
                // multiplier (Unity shares one scalar between them).
                float r = F.randomized ? c.ForceRandom : R.Value(seed, R.Force);
                var mode = F.x.mode;
                var f = new Vector3(InMode(F.x, mode).Evaluate(age, r), InMode(F.y, mode).Evaluate(age, r), InMode(F.z, mode).Evaluate(age, r));
                v += ModuleToSimulation(f, F.space, c.WorldToSim) * dt;
            }

            if (m_ExternalForces.enabled)
                v += ForceFieldAcceleration(ref p, age, c.WorldToSim) * dt;

            var animated = Vector3.zero;
            float speedModifier = 1f;
            if (V.enabled)
            {
                if (V.x.mode == V.y.mode && V.y.mode == V.z.mode)
                {
                    var lin = new Vector3(V.x.Evaluate(age, R.Value(seed, R.VelX)), V.y.Evaluate(age, R.Value(seed, R.VelY)), V.z.Evaluate(age, R.Value(seed, R.VelZ)));
                    if (lin != Vector3.zero) animated += ModuleToSimulation(lin, V.space, c.WorldToSim);
                }
                else ReportOncePerPass("Particle Velocity curves must all be in the same mode");
                speedModifier = V.speedModifier.Evaluate(age, R.Value(seed, R.SpeedMod));
                float ro = R.Value(seed, R.Orbital);
                var orbital = new Vector3(V.orbitalX.Evaluate(age, ro), V.orbitalY.Evaluate(age, ro), V.orbitalZ.Evaluate(age, ro));
                float radial = V.radial.Evaluate(age, R.Value(seed, R.Radial));
                if ((orbital != Vector3.zero || radial != 0f) && dt > 1e-5f)
                {
                    // The offset from the center turns by orbital × dt and moves out by radial × dt
                    // (both times the speed modifier); the velocity reported is that movement over
                    // the step without the modifier (measured exactly).
                    var offset = new Vector3(V.orbitalOffsetX.Evaluate(age, ro), V.orbitalOffsetY.Evaluate(age, ro), V.orbitalOffsetZ.Evaluate(age, ro));
                    var rel = p.m_Position - (c.Center + offset);
                    float scaled = dt * speedModifier;
                    var turned = orbital != Vector3.zero ? Quaternion.Euler(orbital * (scaled * Mathf.Rad2Deg)) * rel : rel;
                    if (radial != 0f && turned.sqrMagnitude > 1e-12f) turned += turned.normalized * (radial * scaled);
                    if (speedModifier != 0f) animated += (turned - rel) / scaled;
                }
            }
            if (m_Noise.enabled) animated += NoiseVelocity(ref p, age, c.NoiseTime, dt);
            if (c.InheritCurrent)
                animated += m_EmitterVelocity * m_InheritVelocity.curve.Evaluate(age, R.Value(seed, R.Inherit));

            // The limit applies to the whole velocity and takes the excess off the particle's own.
            if (m_LimitVelocity.enabled) LimitVelocity(ref v, animated, ref p, age, c.WorldToSim, dt);
            p.m_Velocity = v;

            p.m_AnimatedVelocity = animated;
            var before = p.m_Position;
            p.m_Position += (v + animated) * (speedModifier * dt);
            if (m_Collision.enabled && !Collide(ref p, before, age, dt, speedModifier)) return false;
            if (m_Trigger.enabled && !Trigger(ref p)) return false;

            var RO = m_Rotation;
            var RS = m_RotationBySpeed;
            if (RO.enabled || RS.enabled)
            {
                // The rotation modules set the angular velocity (GetParticles reports it).
                var w = Vector3.zero;
                if (RO.enabled)
                {
                    float r = R.Value(seed, R.RotLife);
                    w += RO.separateAxes ? new Vector3(RO.x.Evaluate(age, r), RO.y.Evaluate(age, r), RO.z.Evaluate(age, r)) : new Vector3(0f, 0f, RO.z.Evaluate(age, r));
                }
                if (RS.enabled)
                {
                    float s = Mathf.InverseLerp(RS.range.x, RS.range.y, (v + animated).magnitude);
                    float r = R.Value(seed, R.RotSpeed);
                    w += RS.separateAxes ? new Vector3(RS.x.Evaluate(s, r), RS.y.Evaluate(s, r), RS.z.Evaluate(s, r)) : new Vector3(0f, 0f, RS.z.Evaluate(s, r));
                }
                if ((p.m_Flags & Particle.FlagFlipRotation) != 0) w = -w;
                p.m_AngularVelocity = w;
            }
            p.m_Rotation += p.m_AngularVelocity * dt;

            if (c.BirthSubs) BirthSubEmitters(ref p, ageBefore, p.m_StartLifetime - p.m_Lifetime, dt);
            return true;
        }

        static MinMaxCurve InMode(MinMaxCurve curve, ParticleSystemCurveMode mode)
        {
            curve.mode = mode;
            return curve;
        }

        void ReportOncePerPass(string message)
        {
            if (m_Newborn)
            {
                if (m_LoggedNewbornError) return;
                m_LoggedNewbornError = true;
            }
            else
            {
                if (m_LoggedUpdateError) return;
                m_LoggedUpdateError = true;
            }
            Debug.LogError(message);
        }

        bool m_LoggedUpdateError;

        /// <summary>Speed limit, dampen and drag. Above the limit the excess decays by (1 − dampen)
        /// every 1/30 s; drag slows by drag × (π r² of the particle) × (its speed) per second, as
        /// enabled (measured).</summary>
        void LimitVelocity(ref Vector3 v, Vector3 animated, ref Particle p, float age, Quaternion worldToSim, float dt)
        {
            var L = m_LimitVelocity;
            uint seed = p.m_RandomSeed;
            float r = R.Value(seed, R.Limit);
            float dampen = Mathf.Clamp01(L.dampen);
            float keep = dampen >= 1f ? 0f : Mathf.Pow(1f - dampen, dt * 30f);
            var total = v + animated;
            if (L.separateAxes)
            {
                var limits = new Vector3(L.limitX.Evaluate(age, r), L.limitY.Evaluate(age, r), L.limitZ.Evaluate(age, r));
                // Limits are given in the module's space.
                bool convert = L.space == ParticleSystemSimulationSpace.World != (m_Main.simulationSpace == ParticleSystemSimulationSpace.World);
                var toModule = L.space == ParticleSystemSimulationSpace.World ? Quaternion.Inverse(worldToSim) : worldToSim;
                var local = convert ? toModule * total : total;
                for (int a = 0; a < 3; a++)
                {
                    float lim = Mathf.Abs(limits[a]);
                    float comp = local[a];
                    if (Mathf.Abs(comp) > lim) local[a] = Mathf.Sign(comp) * (lim + (Mathf.Abs(comp) - lim) * keep);
                }
                total = convert ? Quaternion.Inverse(toModule) * local : local;
            }
            else
            {
                float lim = Mathf.Abs(L.limit.Evaluate(age, r));
                float speed = total.magnitude;
                if (speed > lim && speed > 0f) total *= (lim + (speed - lim) * keep) / speed;
            }
            float drag = L.drag.Evaluate(age, R.Value(seed, R.Drag));
            if (drag > 0f)
            {
                if (L.multiplyDragByParticleSize)
                {
                    var size = CurrentSize3D(ref p);
                    float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f;
                    drag *= Mathf.PI * radius * radius;
                }
                if (L.multiplyDragByParticleVelocity) drag *= total.magnitude;
                total *= Mathf.Max(0f, 1f - drag * dt);
            }
            v = total - animated;
        }

        // ------------------------------------------------------------------ noise

        // ------------------------------------------------------------------ sub-emitters

        bool HasSubEmitters(ParticleSystemSubEmitterType type)
        {
            foreach (var e in m_SubEmitters.emitters)
                if (e.type == type && e.system != null) return true;
            return false;
        }

        void TriggerSubEmitters(ParticleSystemSubEmitterType type, ref Particle parent)
        {
            foreach (var e in m_SubEmitters.emitters)
                if (e.type == type) TriggerSubEmitterAt(e, ref parent);
        }

        /// <summary>A death/collision/trigger/manual sub-emitter: the child emits its bursts at the particle.</summary>
        void TriggerSubEmitterAt(SubEmitter sub, ref Particle parent)
        {
            var child = sub.system;
            if (child == null || child == this || child.m_GameObject == null) return;
            if (sub.emitProbability < 1f && NextFloat() >= sub.emitProbability) return;
            int count = 0;
            foreach (var burst in child.m_Emission.bursts)
                count += child.BurstCount(burst.m_Count, 0f) * Math.Max(1, burst.cycleCount);
            if (count > 0) child.EmitFromParent(this, ref parent, sub.properties, count, 0f);
        }

        /// <summary>Birth sub-emitters emit with their own rate and bursts over the parent particle's life.</summary>
        void BirthSubEmitters(ref Particle parent, float ageBefore, float ageAfter, float dt)
        {
            int slot = 0;
            foreach (var e in m_SubEmitters.emitters)
            {
                if (e.type != ParticleSystemSubEmitterType.Birth) continue;
                var child = e.system;
                if (child == null || child == this || child.m_GameObject == null) { slot++; continue; }
                if (e.emitProbability < 1f && ParticleRandom.Value(parent.m_RandomSeed, R.SubEmit + (uint)slot) >= e.emitProbability) { slot++; continue; }
                float age01 = parent.Age01;
                int n = 0;
                if (child.m_Emission.enabled)
                {
                    float rate = child.m_Emission.rateOverTime.Evaluate(age01, child.NextFloat());
                    if (slot == 0)
                    {
                        parent.m_EmitAccumulator0 += rate * dt;
                        n = (int)parent.m_EmitAccumulator0;
                        parent.m_EmitAccumulator0 -= n;
                    }
                    else
                    {
                        parent.m_EmitAccumulator1 += rate * dt;
                        n = (int)parent.m_EmitAccumulator1;
                        parent.m_EmitAccumulator1 -= n;
                    }
                    foreach (var burst in child.m_Emission.bursts)
                    {
                        float at = burst.m_Time;
                        bool first = ageBefore <= 0f && at <= 0f;
                        if (first || (at > ageBefore && at <= ageAfter))
                            n += child.BurstCount(burst.m_Count, age01);
                    }
                }
                if (n > 0) child.EmitFromParent(this, ref parent, e.properties, n, 0f);
                slot++;
            }
        }

        /// <summary>Emits `count` particles at a parent system's particle, inheriting what the
        /// sub-emitter's properties ask for.</summary>
        void EmitFromParent(ParticleSystem parentSystem, ref Particle parent, ParticleSystemSubEmitterProperties properties, int count, float preAge)
        {
            var origin = parentSystem.SimulationToWorld().MultiplyPoint3x4(parent.m_Position);
            var emitter = EmitterMatrix();
            Color parentColor = parentSystem.CurrentColor(ref parent);
            var parentSize = parentSystem.CurrentSize3D(ref parent);
            var parentRotation = parent.m_Rotation;
            float parentLife = parent.m_StartLifetime > 0f ? parent.m_Lifetime / parent.m_StartLifetime : 0f;
            EmitParams none = default;
            var context = MakeContext();
            for (int k = 0; k < count; k++)
            {
                int index = Allocate();
                if (index < 0) return;
                ref var p = ref m_Particles[index];
                if (!InitParticle(ref p, emitter, Mathf.Clamp01(m_Time / Mathf.Max(m_Main.duration, 0.0001f)), false, ref none, k, count, origin))
                {
                    RemoveAt(index);
                    continue;
                }
                p.m_ParentRandomSeed = parent.m_RandomSeed;
                if ((properties & ParticleSystemSubEmitterProperties.InheritColor) != 0)
                    p.m_StartColor = (Color)p.m_StartColor * parentColor;
                if ((properties & ParticleSystemSubEmitterProperties.InheritSize) != 0)
                    p.m_StartSize = Vector3.Scale(p.m_StartSize, parentSize);
                if ((properties & ParticleSystemSubEmitterProperties.InheritRotation) != 0)
                    p.m_Rotation += parentRotation;
                if ((properties & ParticleSystemSubEmitterProperties.InheritLifetime) != 0)
                {
                    p.m_StartLifetime *= parentLife;
                    p.m_Lifetime = p.m_StartLifetime;
                    p.m_InvStartLifetime = 1f / p.m_StartLifetime;
                    p.m_AgePercent = 0f;
                    if (p.m_Lifetime <= 0f)
                    {
                        RemoveAt(index);
                        continue;
                    }
                }
                if (!Integrate(ref p, preAge, ref context)) RemoveAt(index);
                else StartTrail(ref p);
            }
        }

        // ------------------------------------------------------------------ current values

        /// <summary>A particle's size now: start size, size over lifetime, size by speed.</summary>
        internal Vector3 CurrentSize3D(ref Particle p, bool includeNoise = true)
        {
            var size = p.m_StartSize;
            uint seed = p.m_RandomSeed;
            if (m_Size.enabled)
            {
                float age = p.Age01;
                float r = R.Value(seed, R.SizeLife);
                if (m_Size.separateAxes)
                    size = Vector3.Scale(size, new Vector3(m_Size.x.Evaluate(age, r), m_Size.y.Evaluate(age, r), m_Size.z.Evaluate(age, r)));
                else
                    size *= m_Size.x.Evaluate(age, r);
            }
            if (m_SizeBySpeed.enabled)
            {
                var range = m_SizeBySpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, (p.m_Velocity + p.m_AnimatedVelocity).magnitude);
                float r = R.Value(seed, R.SizeSpeed);
                if (m_SizeBySpeed.separateAxes)
                    size = Vector3.Scale(size, new Vector3(m_SizeBySpeed.x.Evaluate(s, r), m_SizeBySpeed.y.Evaluate(s, r), m_SizeBySpeed.z.Evaluate(s, r)));
                else
                    size *= m_SizeBySpeed.x.Evaluate(s, r);
            }
            if (includeNoise && m_Noise.enabled) size = NoiseSize(ref p, size);
            return size;
        }

        /// <summary>A particle's color now: start color, color over lifetime, color by speed.</summary>
        internal Color32 CurrentColor(ref Particle p)
        {
            Color c = p.m_StartColor;
            uint seed = p.m_RandomSeed;
            if (m_Color.enabled)
                c *= m_Color.color.Evaluate(p.Age01, R.Value(seed, R.ColorLife));
            if (m_ColorBySpeed.enabled)
            {
                var range = m_ColorBySpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, (p.m_Velocity + p.m_AnimatedVelocity).magnitude);
                c *= m_ColorBySpeed.color.Evaluate(s, R.Value(seed, R.ColorSpeed));
            }
            return c;
        }

        internal int MeshIndexOf(ref Particle p)
        {
            if ((p.m_Flags & Particle.FlagMeshIndexSet) != 0) return p.m_MeshIndex;
            var renderer = m_GameObject != null ? m_GameObject.GetComponent<ParticleSystemRenderer>() : null;
            int count = renderer != null ? renderer.meshCount : 1;
            if (count <= 1) return 0;
            return Math.Min((int)(R.Value(p.m_RandomSeed, R.MeshIndex) * count), count - 1);
        }
    }
}
