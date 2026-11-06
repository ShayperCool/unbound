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
        /// <summary>The world gravity a gravity source names: Physics.gravity or Physics2D.gravity
        /// (z = 0), so the project's DynamicsManager / Physics2DSettings and runtime changes, which
        /// a running system uses from its next step (measured).</summary>
        internal static Vector3 SourceGravity(ParticleSystemGravitySource source) =>
            source == ParticleSystemGravitySource.Physics2D ? (Vector3)Physics2D.gravity : Physics.gravity;

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

        // Birth newborns finish their own remaining Simulate call time when emitted,
        // after the child's existing particles have advanced.
        bool m_SimulatingBirths;
        float m_BirthCallRemaining;

        /// <summary>The frame's update; whether it advanced the system. <paramref name="catchUp"/>
        /// is PauseAndCatchup's missed time, simulated first as one step inside the frame's
        /// collision and trigger collection (see CullingFrameUpdate).</summary>
        internal bool FrameUpdate(float catchUp = 0f)
        {
            float raw = m_Main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            TrackEmitter(raw);
            if (m_IsPaused) return false;
            if (!m_IsPlaying && m_Count == 0 && !HasAliveTrails) return false;
            if (WaitsForSubEmitterParent()) return false;
            float dt = raw * m_Main.simulationSpeed;
            if (dt <= 0f) return false;
            m_CollisionEvents.Clear();
            foreach (var list in m_TriggerParticles) list.Clear();
            m_CollectTriggers = true;
            try
            {
                if (catchUp > 0f)
                {
                    Step(catchUp, 0f);
                    if (m_IsPaused || !m_Registered) return true;
                }
                Advance(dt, m_FrameDistance, true);
            }
            finally { m_CollectTriggers = false; }
            return true;
        }

        /// <summary>A sub-emitter that is not playing itself moves its particles only while the system
        /// emitting into it plays or has particles. Measured in 6000.0.71f1 (subemitters probe,
        /// played/stopped): particles that TriggerSubEmitter emits from OnParticleSystemStopped stay
        /// where they are, at their full lifetime, until the parent plays again, and then move on
        /// with it. A paused parent and the frame its last particle dies in are unmeasured.</summary>
        bool WaitsForSubEmitterParent()
        {
            var parent = m_SubEmitterParent;
            if (m_IsPlaying || parent == null || parent.m_Destroyed || parent.m_GameObject == null) return false;
            if (parent.m_IsPaused || !parent.m_IsPlaying && parent.m_Count == 0) return true;
            // Updated with its parent, it plays its particles from now on without emitting more
            // (played/update: TriggerSubEmitter in Update, isPlaying true from the next frame).
            m_IsPlaying = true;
            m_StopEmitting = true;
            return false;
        }

        bool HasSubEmitterSystems()
        {
            if (!m_SubEmitters.enabled) return false;
            foreach (var e in m_SubEmitters.emitters)
                if (e.system != null && e.system != this) return true;
            return false;
        }

        /// <summary>Whether a sub-emitter of this system still has particles (or waits for its own).</summary>
        internal bool SubEmittersHaveParticles()
        {
            foreach (var e in m_SubEmitters.emitters)
            {
                var child = e.system;
                if (child == null || child == this || child.m_Destroyed) continue;
                if (child.m_Count > 0 || child.m_StopPending) return true;
            }
            return false;
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
            float remaining = dt;
            try
            {
                for (int i = 0; remaining > 1e-6f && i < 100000; i++)
                {
                    m_StepFrom = from + (to - from) * Mathf.Min((float)i / steps, 1f);
                    m_StepTo = from + (to - from) * Mathf.Min((float)(i + 1) / steps, 1f);
                    // The Native acceleration controls match subtraction of the remaining
                    // call time, with residuals at or below one microsecond left unstepped.
                    float used = Mathf.Min(step, remaining);
                    m_StepDt = used;
                    Step(used, i < steps ? share : 0f);
                    if (m_SimulatingBirths) m_BirthCallRemaining -= used;
                    remaining -= used;
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
            // Newborns sample the current scroll but only existing particles advance it.
            if (m_Noise.enabled && m_Count > 0)
            {
                float duration = Mathf.Max(m_Main.duration, .0001f);
                float endTime = m_Time + dt;
                if (m_Main.loop) endTime %= duration;
                m_NoiseScroll += m_Noise.scrollSpeed.Evaluate(Mathf.Clamp01(endTime / duration), 1f) * dt;
            }
            m_Context = MakeContext(dt);
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
                // In the frame's update a system with sub-emitters reports itself stopped only once
                // their particles are gone too (6000.0.71f1 subemitters played/finish: isPlaying
                // false at once, IsAlive true, OnParticleSystemStopped in the frame the Death
                // children's last particles die). Simulate keeps the immediate stop (unmeasured).
                if (ParticleSystemManager.Updating && HasSubEmitterSystems()) m_StopPending = true;
                else OnStopped();
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
            m_EmissionRequests.Clear();
            try { AdvanceEmissionStep(dt, distance); }
            finally
            {
                m_CollectRateBatch = false;
                FlushRateBatch();
            }
        }

        void AdvanceEmissionStep(float dt, float distance)
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
            // One time-rate draw per step, shared across the loop boundary's segments.
            float rateRandom = emit ? NextEmissionValue() : 0f;
            float step = dt;
            float t0 = m_Time;
            float t1 = t0 + dt;
            m_TotalTime += dt;
            m_RateBatchStartLoop = m_Loop;
            m_UseRateBatch = Compat.ParticleRateBatchesMeasured && m_Main.ringBufferMode == ParticleSystemRingBufferMode.Disabled;
            bool planned = emit && (m_UseRateBatch ? PlanRate(t0, dt, duration, rateRandom) : PlanConstantRate(t0, dt, duration));
            m_CollectRateBatch = m_UseRateBatch && m_StepRateBirths > 0;
            int guard = 0;
            while (t1 >= duration && guard++ < 10000)
            {
                if (!m_Main.loop)
                {
                    if (emit)
                    {
                        EmitRate(t0, duration, false, 0f, duration, rateRandom);
                        EmitOver(t0, duration, 0f, step, distance * ((duration - t0) / step));
                    }
                    if (planned) m_RateAccumulator = m_StepRateRemainder;
                    m_Time = duration;
                    m_StopEmitting = true;
                    return;
                }
                float rest = t1 - duration;
                if (emit)
                {
                    EmitRate(t0, duration, false, rest, duration, rateRandom);
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
                EmitRate(t0, t1, true, 0f, duration, rateRandom);
                EmitOver(t0, t1, 0f, step, distance * ((t1 - t0) / step));
            }
            if (planned) m_RateAccumulator = m_StepRateRemainder;
            m_Time = t1;
        }

        int m_StepRateBirths;
        float m_StepRateRemainder, m_StepRateInvGain, m_StepRateDt;
        bool m_RateBatchEmitted, m_UseRateBatch;

        // Count with the established float arithmetic, sharing the step's emission draw
        // across loops. Start curves use the final playback time of the whole batch.
        bool PlanRate(float t0, float dt, float duration, float rateRandom)
        {
            var curve = m_Emission.rateOverTime;
            m_RateBatchEmitted = false;
            m_StepRateBirths = 0;
            float counted = m_RateAccumulator, whole = counted, gain = 0f, phase = m_EmitPhase;
            float t1 = t0 + dt;
            int guard = 0;
            while (t1 >= duration && guard++ < 10000)
            {
                AddRateSegment(curve, rateRandom, t0, duration, false, duration, phase, ref counted, ref whole, ref gain);
                if (!m_Main.loop) break;
                float rest = t1 - duration;
                if (curve.mode == ParticleSystemCurveMode.Constant && curve.constant > 0f)
                {
                    float due = curve.constant * duration + phase;
                    phase = due - ((float)Math.Ceiling(due) - 1f);
                }
                else phase = 0f;
                t0 = 0f;
                t1 = rest;
            }
            if (t1 < duration)
                AddRateSegment(curve, rateRandom, t0, t1, true, duration, phase, ref counted, ref whole, ref gain);
            m_StepRateRemainder = whole - m_StepRateBirths;
            m_StepRateInvGain = gain > 0f ? 1f / gain : 0f;
            m_StepRateDt = dt;
            return true;
        }

        void AddRateSegment(MinMaxCurve curve, float random, float a, float b, bool included, float duration,
            float phase, ref float counted, ref float whole, ref float gain)
        {
            if (!(b > a)) return;
            float rate = curve.Evaluate(Mathf.Clamp01(a / duration), random);
            if (!(rate > 0f)) return;
            float segment = rate * (b - a);
            whole += segment;
            gain += segment;
            int count;
            if (curve.mode == ParticleSystemCurveMode.Constant)
            {
                count = ConstantRateBirths(rate, a, b, included, phase, ref counted, out _, out _);
            }
            else
            {
                counted += segment;
                count = (int)counted;
                counted -= count;
            }
            if (count <= 0) return;
            m_StepRateBirths += count;
        }

        /// <summary>The constant-rate births due in the playback time (a, b] (b excluded at a loop's
        /// end), counted from the accumulated rounded rate × span: the established count arithmetic,
        /// shared by planning and emission. <paramref name="last"/> is the birth index of the last
        /// one in the loop's rate × time + phase coordinate.</summary>
        static int ConstantRateBirths(float rate, float a, float b, bool endIncluded, float phase, ref float accumulator, out int last, out float due)
        {
            // Native accumulates the rounded rate * clock span for each subdivision.
            // Whole-clock multiplication loses the carried fraction at exact boundaries.
            accumulator += rate * (b - a);
            int count = endIncluded ? (int)accumulator
                : Math.Max(0, (int)Math.Ceiling(accumulator) - 1);
            accumulator -= count;
            due = rate * b + phase;
            // Keep the established float spawn-age and speed coordinates. Removing the
            // carried fraction identifies the last integral birth in those coordinates.
            last = (int)Math.Round(due - accumulator);
            if (!endIncluded)
            {
                // A birth at the duration belongs to the next loop's start values.
                double interval = 1f / rate;
                int end = (int)Math.Ceiling(b / interval + phase) - 1;
                int pending = Math.Min(count, Math.Max(0, last - end));
                count -= pending;
                last -= pending;
                accumulator += pending;
            }
            return count;
        }

        /// <summary>The loop's float birth-age coordinate: rate × time + phase identifies its
        /// births, while m_RateAccumulator carries their fractional count. A loop's end is exclusive
        /// and the next loop's start inclusive, so a particle
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

        bool PlanConstantRate(float t0, float dt, float duration)
        {
            var curve = m_Emission.rateOverTime;
            m_StepRateBirths = 0;
            if (curve.mode != ParticleSystemCurveMode.Constant || !(curve.constant > 0f)) return false;
            float rate = curve.constant;
            float counted = m_RateAccumulator, whole = m_RateAccumulator, gain = 0f, phase = m_EmitPhase;
            int births = 0;
            float t1 = t0 + dt;
            bool ended = false;
            int guard = 0;
            while (t1 >= duration && guard++ < 10000)
            {
                births += PlanRateSegment(rate, t0, duration, false, phase, ref counted, ref whole, ref gain);
                if (!m_Main.loop)
                {
                    ended = true;
                    break;
                }
                float rest = t1 - duration;
                float due = rate * duration + phase;
                phase = due - ((float)Math.Ceiling(due) - 1f);
                t0 = 0f;
                t1 = rest;
            }
            if (!ended) births += PlanRateSegment(rate, t0, t1, true, phase, ref counted, ref whole, ref gain);
            m_StepRateBirths = births;
            m_StepRateRemainder = whole - births;
            m_StepRateInvGain = gain > 0f ? 1f / gain : 0f;
            m_StepRateDt = dt;
            return true;
        }

        static int PlanRateSegment(float rate, float a, float b, bool endIncluded, float phase, ref float counted, ref float whole, ref float gain)
        {
            if (!(b > a)) return 0;
            float segment = rate * (b - a);
            whole += segment;
            gain += segment;
            return ConstantRateBirths(rate, a, b, endIncluded, phase, ref counted, out _, out _);
        }

        /// <summary>The age of the next constant-rate birth of the current step (see
        /// <see cref="PlanConstantRate"/>); births are emitted oldest first.</summary>
        float NextStepRateAge()
        {
            System.Diagnostics.Debug.Assert(m_StepRateBirths > 0, "a constant-rate birth the step did not plan");
            int later = Math.Max(0, --m_StepRateBirths);
            return m_StepRateDt * Mathf.Max((m_StepRateRemainder + later) * m_StepRateInvGain, 1e-6f);
        }

        void EmitLegacyRate(float a, float b, bool endIncluded, float extraAge, float duration, float rateRandom)
        {
            var rateCurve = m_Emission.rateOverTime;
            if (!(b > a)) return;
            m_LoopStart = false;
            if (rateCurve.mode == ParticleSystemCurveMode.Constant)
            {
                float rate = rateCurve.constant;
                if (!(rate > 0f)) return;
                float phase = m_EmitPhase;
                int count = ConstantRateBirths(rate, a, b, endIncluded, phase, ref m_RateAccumulator, out int last, out _);
                int first = last - count + 1;
                for (int m = first; m <= last; m++)
                {
                    m_SpeedNt = Mathf.Clamp01((m - phase) / rate / duration);
                    EmitParticles(1, NextStepRateAge(), b, default, false, m - first, count);
                }
            }
            else
            {
                float rate = rateCurve.Evaluate(Mathf.Clamp01(a / duration), rateRandom);
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

        struct EmissionRequest
        {
            internal int count, spreadIndex, spreadCount;
            internal float preAge, birthTime, speedNt;
            internal bool rate;
        }
        readonly System.Collections.Generic.List<EmissionRequest> m_EmissionRequests = new();
        bool m_CollectRateBatch;
        int m_RateBatchStartLoop;

        void EmitRate(float a, float b, bool endIncluded, float extraAge, float duration, float rateRandom)
        {
            if (!m_UseRateBatch)
            {
                EmitLegacyRate(a, b, endIncluded, extraAge, duration, rateRandom);
                return;
            }
            m_LoopStart = false;
            if (m_RateBatchEmitted) return;
            m_RateBatchEmitted = true;
            if (m_StepRateBirths > 0)
                m_EmissionRequests.Add(new EmissionRequest { count = m_StepRateBirths, rate = true, spreadIndex = -1 });
        }

        // Unity 6000.0.71f1, tests/projects/particlerandom: initialize the aligned
        // portion first, then the leading partial group. Rate ages increase in
        // sample order. Bursts in that step share the initialization batch.
        void FlushRateBatch()
        {
            if (m_EmissionRequests.Count == 0) return;
            int requested = 0;
            foreach (var request in m_EmissionRequests) requested = checked(requested + request.count);
            int start = m_Count;
            int count = Math.Min(requested, Math.Max(0, m_Main.maxParticles - start));
            if (count <= 0) return;
            int aligned = checked((start + 3) & ~3);
            int lanes = checked((count + 3) & ~3);
            Reserve(checked(aligned + lanes));
            m_Count = aligned + lanes;
            for (int i = aligned; i < m_Count; i++) ClearCustom(i);
            var dead = new bool[lanes];
            var origins = new Vector3[lanes];
            var overrides = default(EmitParams);
            var t = m_GameObject.m_Transform;
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            int requestIndex = 0, requestStart = 0;
            m_Newborn = true;
            for (int j = 0; j < lanes; j++)
            {
                while (requestIndex + 1 < m_EmissionRequests.Count && j >= requestStart + m_EmissionRequests[requestIndex].count)
                    requestStart += m_EmissionRequests[requestIndex++].count;
                var request = m_EmissionRequests[requestIndex];
                int local = j - requestStart;
                float age = request.preAge, birthTime = request.birthTime;
                m_SpeedNt = request.speedNt;
                if (request.rate)
                {
                    age = m_StepRateDt * Mathf.Max((m_StepRateRemainder + local) * m_StepRateInvGain, 1e-6f);
                    birthTime = m_Time;
                    m_SpeedNt = Mathf.Clamp01((birthTime - age) / duration);
                }
                if (j >= count)
                {
                    age = m_StepRateDt * 1e-6f;
                    birthTime = m_Time;
                    m_SpeedNt = Mathf.Clamp01((birthTime - age) / duration);
                }
                int index = aligned + j;
                var emitter = Matrix4x4.TRS(EmitterPositionAt(age), t.rotation, EmitterScale());
                m_ShapeTime = m_TotalTime - age;
                int spread = request.spreadIndex >= 0 ? request.spreadIndex : local;
                int spreadCount = request.spreadIndex >= 0 ? request.spreadCount : request.count;
                if (!InitParticle(ref m_Particles[index], emitter, Mathf.Clamp01(birthTime / duration),
                    false, ref overrides, spread, spreadCount, null, j, requested))
                { dead[j] = true; continue; }
                origins[j] = m_Particles[index].m_Position;
                BeginModuleSample(j, m_Particles[index].m_RandomSeed);
                var context = m_Context;
                if (j >= count)
                {
                    context.BirthSubs = false;
                    context.TemporaryLane = true;
                }
                dead[j] = !Integrate(ref m_Particles[index], age, ref context);
            }
            // Initialize complete groups before removing expired births. The unused
            // final lanes take the minimum age and participate in local compaction.
            // Only the admitted count is retained, including any promoted lanes.
            for (int i = aligned; i < m_Count;)
            {
                int local = i - aligned;
                if (!dead[local]) { i++; continue; }
                RemoveAt(i);
                if (count > 0) count--;
                int last = m_Count - aligned;
                if (local < last) { dead[local] = dead[last]; origins[local] = origins[last]; }
            }
            int prefix = Math.Min(count, aligned - start);
            for (int i = 0; i < prefix; i++)
            {
                int source = aligned + count - prefix + i;
                m_Particles[start + i] = m_Particles[source];
                for (int stream = 0; stream < 2; stream++)
                    if (m_Custom[stream] != null) m_Custom[stream][start + i] = m_Custom[stream][source];
            }
            m_Count = start + count;
            for (int i = start; i < m_Count; i++)
            {
                int origin = i < aligned ? count - prefix + i - start : i - aligned;
                StartTrail(ref m_Particles[i], origins[origin]);
            }
            m_Newborn = false;
            m_SpeedNt = -1f;
        }

        /// <summary>Distance and burst emission for the playback time (a, b), plus
        /// <paramref name="extraAge"/> (the step's time after b). A burst fires in the step its time
        /// falls in, its first cycle aged from that time, its repeats aged the whole step (measured).</summary>
        void EmitOver(float a, float b, float extraAge, float step, float distance)
        {
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
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
                        if (at > b || c == 0 && at >= b || at >= duration) break;
                        if (burst.probability >= 1f || burst.probability > 0f && NextEmissionValue() < burst.probability)
                        {
                            int count = BurstCount(burst.m_Count, at / duration);
                            float age = (c == 0 && !(m_CollectRateBatch && m_Loop > m_RateBatchStartLoop) ? b - at : step) + extraAge;
                            m_SpeedNt = Mathf.Clamp01(at / duration);
                            if (count > 0) EmitParticles(count, age, b, default, false);
                        }
                        c++;
                        if (cycles == 0 && c > 100000) break;
                    }
                    m_BurstCycles[i] = c;
                }
            }
            float perDistance = distance > 0f ? m_Emission.rateOverDistance.Evaluate(Mathf.Clamp01(a / duration), NextEmissionValue()) : 0f;
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

            m_SpeedNt = -1f;
        }

        int BurstCount(MinMaxCurve count, float nt)
        {
            EnsureModuleRandom();
            return BurstCount(count, nt, ref m_EmissionRandom);
        }

        static int BurstCount(MinMaxCurve count, float nt, ref Random.State random)
        {
            if (count.mode == ParticleSystemCurveMode.Constant)
                return Mathf.Max(0, (int)count.constant);
            if (count.mode == ParticleSystemCurveMode.TwoConstants)
            {
                int min = (int)Mathf.Min(count.constantMin, count.constantMax);
                int max = (int)Mathf.Max(count.constantMin, count.constantMax);
                random.NextValue();
                return Mathf.Max(0, min + (int)(random.S3 % ((uint)(max - min) + 1u)));
            }
            return Mathf.Max(0, (int)count.Evaluate(nt, random.NextValue()));
        }

        // ------------------------------------------------------------------ emitting particles

        /// <summary>Emits <paramref name="count"/> particles born at playback time
        /// <paramref name="birthTime"/> (their start values are taken there) that have already lived
        /// <paramref name="preAge"/> seconds.</summary>
        internal void EmitParticles(int count, float preAge, float birthTime, EmitParams overrides, bool hasOverrides, int spreadIndex = -1, int spreadCount = 0, bool simulate = true)
        {
            if (m_GameObject == null) return;
            if (m_CollectRateBatch && count > 0)
            {
                m_EmissionRequests.Add(new EmissionRequest { count = count, preAge = preAge, birthTime = birthTime,
                    speedNt = m_SpeedNt, spreadIndex = spreadIndex, spreadCount = spreadCount });
                return;
            }
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
                if (!InitParticle(ref m_Particles[index], emitter, nt, hasOverrides, ref overrides, si, sn, null, si, sn))
                { RemoveAt(index); continue; }
                var trailBirthPosition = m_Particles[index].m_Position;
                if (simulate) BeginModuleSample(si, m_Particles[index].m_RandomSeed);
                if (simulate && !Integrate(ref m_Particles[index], preAge, ref m_Context))
                    RemoveAt(index);
                else StartTrail(ref m_Particles[index], simulate ? trailBirthPosition : null);
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
        bool InitParticle(ref Particle p, Matrix4x4 emitter, float nt, bool hasOverrides, ref EmitParams o, int spreadIndex, int spreadCount, Vector3? origin,
            int initializationIndex, int initializationCount, Vector3? inheritedAxis = null, Quaternion? originRotation = null)
        {
            var samples = InitialSamplesAt(initializationIndex, initializationCount, hasOverrides, ref o);
            uint seed = samples.seed;
            var M = m_Main;

            var noiseSum = p.m_NoiseSum;
            p = default;
            p.m_NoiseSum = noiseSum;
            p.m_RandomSeed = seed;
            float lifetime = M.startLifetime.Evaluate(nt, samples.lifetime);
            if (m_LifetimeByEmitterSpeed.enabled)
            {
                var range = m_LifetimeByEmitterSpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, m_EmitterVelocity.magnitude);
                lifetime *= m_LifetimeByEmitterSpeed.curve.Evaluate(s, R.Value(seed, R.Inherit + 100));
            }
            float speed = M.startSpeed.Evaluate(m_SpeedNt >= 0f ? m_SpeedNt : nt, InitialSeedValue(seed, 2527743459u));
            Vector3 size;
            if (M.startSize3D)
                size = new Vector3(M.startSizeX.Evaluate(nt, samples.sizeX), M.startSizeY.Evaluate(nt, samples.sizeY), M.startSizeZ.Evaluate(nt, samples.sizeZ));
            else
            {
                float s = M.startSizeX.Evaluate(nt, samples.sizeX);
                size = new Vector3(s, s, s);
            }
            Vector3 rotation = M.startRotation3D
                ? new Vector3(M.startRotationX.Evaluate(nt, samples.rotationX), M.startRotationY.Evaluate(nt, samples.rotationY), M.startRotationZ.Evaluate(nt, samples.rotationZ))
                : new Vector3(0f, 0f, M.startRotationZ.Evaluate(nt, samples.rotationZ));
            if (M.flipRotation > 0f && InitialSeedValue(seed, 4281053604u) < M.flipRotation)
            {
                rotation = -rotation;
                p.m_Flags |= Particle.FlagFlipRotation;
            }
            var color = M.startColor.Evaluate(nt, samples.color);

            BeginShapeSample(initializationIndex);
            SampleShape(spreadIndex, spreadCount, out var pos, out var dir);
            // Use Mesh Colors modulates the start colour with the sampled mesh colour.
            if (m_ShapeColorSet) color *= m_ShapeColor;
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
                var frame = Matrix4x4.TRS(origin.Value, originRotation ?? t.rotation, EmitterScale());
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
                if (o.m_AxisOfRotationSet)
                {
                    MaterializeAxisOfRotation();
                    p.m_AxisOfRotation = o.m_Particle.m_AxisOfRotation;
                }
                if (o.m_StartColorSet) color = o.m_Particle.m_StartColor;
                if (o.m_MeshIndexSet)
                {
                    p.m_MeshIndex = o.m_Particle.m_MeshIndex;
                    p.m_Flags |= Particle.FlagMeshIndexSet;
                }
            }
            if (!(hasOverrides && o.m_AxisOfRotationSet)) p.m_AxisOfRotation = inheritedAxis ?? Vector3.forward;

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
            m_ShapeColorSet = false;
            if (!S.enabled)
            {
                pos = Vector3.zero;
                dir = Vector3.forward;
                return;
            }
            bool measuredModifiers = (S.randomDirectionAmount > 0f || S.sphericalDirectionAmount > 0f || S.randomPositionAmount > 0f)
                && Compat.ParticleShapeModifiersMeasured;
            bool coneDirectionSampled = false;
            float radius = S.radius;
            float thickness = Mathf.Clamp01(S.radiusThickness);
            switch (S.shapeType)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.SphereShell:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.HemisphereShell:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    float height = ShapeValue();
                    bool hemisphere = S.shapeType == ParticleSystemShapeType.Hemisphere || S.shapeType == ParticleSystemShapeType.HemisphereShell;
                    float z = hemisphere ? height : height * 2f - 1f;
                    float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                    dir = new Vector3(ring * Mathf.Cos(phi), ring * Mathf.Sin(phi), z);
                    bool shell = S.shapeType == ParticleSystemShapeType.SphereShell || S.shapeType == ParticleSystemShapeType.HemisphereShell;
                    float inner = shell ? 1f : Mathf.Pow(1f - thickness, 3f);
                    float sample = ShapeValue();
                    float r = radius * ShapeCubeRoot((1f - sample) + inner * sample);
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
                    float sample = ShapeValue();
                    float r01 = Mathf.Sqrt((1f - sample) + Mathf.Max(0.001f, shell ? 1f : 1f - thickness) * sample);
                    float angle = Mathf.Clamp(S.angle, 0f, 90f) * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
                    float directionX = cos * r01, directionY = sin * r01;
                    if (measuredModifiers && S.shapeType == ParticleSystemShapeType.Cone && S.randomDirectionAmount > 0f)
                    {
                        // Cone direction randomization blends a second disc sample
                        // before applying the cone angle, independently of thickness.
                        float randomPhi = ShapeValue() * (Mathf.PI * 2f);
                        float randomRadius = ShapeValue();
                        randomRadius = Mathf.Sqrt((1f - randomRadius) + 0.001f * randomRadius);
                        directionX = Mathf.Lerp(directionX, Mathf.Cos(randomPhi) * randomRadius, S.randomDirectionAmount);
                        directionY = Mathf.Lerp(directionY, Mathf.Sin(randomPhi) * randomRadius, S.randomDirectionAmount);
                        coneDirectionSampled = true;
                    }
                    dir = new Vector3(directionX * Mathf.Sin(angle), directionY * Mathf.Sin(angle), Mathf.Cos(angle)).normalized;
                    pos = new Vector3(cos * radius * r01, sin * radius * r01, 0f);
                    if (S.shapeType == ParticleSystemShapeType.ConeVolume || S.shapeType == ParticleSystemShapeType.ConeVolumeShell)
                    {
                        float along = ShapeValue() * S.length;
                        pos += dir * along;
                    }
                    break;
                }
                case ParticleSystemShapeType.Box:
                    pos = new Vector3(ShapeValue() - 0.5f, ShapeValue() - 0.5f, ShapeValue() - 0.5f);
                    dir = Vector3.forward;
                    break;
                case ParticleSystemShapeType.BoxShell:
                {
                    pos = new Vector3(ShapeValue() - 0.5f, ShapeValue() - 0.5f, ShapeValue() - 0.5f);
                    int axis = (int)(ShapeValue() * 3f) % 3;
                    pos[axis] = ShapeValue() < 0.5f ? -0.5f : 0.5f;
                    dir = Vector3.forward;
                    break;
                }
                case ParticleSystemShapeType.BoxEdge:
                {
                    pos = new Vector3(ShapeValue() < 0.5f ? -0.5f : 0.5f, ShapeValue() < 0.5f ? -0.5f : 0.5f, ShapeValue() < 0.5f ? -0.5f : 0.5f);
                    int axis = (int)(ShapeValue() * 3f) % 3;
                    pos[axis] = ShapeValue() - 0.5f;
                    dir = Vector3.forward;
                    break;
                }
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.CircleEdge:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    float inner = 1f - thickness;
                    float r01 = S.shapeType == ParticleSystemShapeType.CircleEdge ? 1f : Mathf.Sqrt(inner * inner + (1f - inner * inner) * ShapeValue());
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
                        default: u = ShapeValue(); break;
                    }
                    if (S.radiusSpread > 0f) u = Mathf.Floor(u / S.radiusSpread) * S.radiusSpread;
                    pos = new Vector3(Mathf.Lerp(-radius, radius, u), 0f, 0f);
                    dir = Vector3.up;
                    break;
                }
                case ParticleSystemShapeType.Donut:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    float theta = ShapeValue() * Mathf.PI * 2f;
                    float tube = S.donutRadius * Mathf.Lerp(Mathf.Max(0.001f, 1f - thickness), 1f, ShapeValue());
                    var ring = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0f);
                    dir = ring * Mathf.Cos(theta) + Vector3.forward * Mathf.Sin(theta);
                    pos = ring * radius + dir * tube;
                    break;
                }
                case ParticleSystemShapeType.Rectangle:
                    pos = new Vector3(ShapeValue() - 0.5f, ShapeValue() - 0.5f, 0f);
                    dir = Vector3.forward;
                    break;
                default:
                    SampleMeshShape(out pos, out dir);
                    break;
            }

            if (measuredModifiers)
            {
                // Unity 6000.0.71f1, tests/projects/particlerandom: both blends use
                // shape coordinates, without normalizing between them. The outward
                // direction uses the basic position, before the separate jitter draw.
                if (!coneDirectionSampled && S.randomDirectionAmount > 0f)
                    dir = Vector3.Lerp(dir, ShapeModifierDirection(), S.randomDirectionAmount);
                if (S.sphericalDirectionAmount > 0f)
                    dir = Vector3.Lerp(dir, pos.normalized, S.sphericalDirectionAmount);
                if (S.randomPositionAmount > 0f)
                    pos += ShapeModifierDirection() * S.randomPositionAmount;
            }
            else if (S.randomPositionAmount > 0f)
                pos += RandomUnitVector() * (S.randomPositionAmount * Mathf.Pow(ShapeValue(), 1f / 3f));

            var rot = Quaternion.Euler(S.rotation);
            var shapeMatrix = Matrix4x4.TRS(S.position, rot, S.scale);
            pos = shapeMatrix.MultiplyPoint3x4(pos);
            var d = shapeMatrix.MultiplyVector(dir);
            dir = d.sqrMagnitude > 1e-12f ? d.normalized : rot * dir;

            if (!measuredModifiers && S.sphericalDirectionAmount > 0f)
            {
                var outward = (pos - S.position).sqrMagnitude > 1e-12f ? (pos - S.position).normalized : dir;
                dir = Vector3.Lerp(dir, outward, S.sphericalDirectionAmount).normalized;
            }
            if (!measuredModifiers && S.randomDirectionAmount > 0f)
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
                    angle = ShapeValue() * arc;
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

        /// <summary>Mesh, renderer and sprite shapes: a point of the mesh (a vertex, a point on an
        /// edge or a point in a triangle, with its normal), or a point of the sprite's rectangle; a
        /// unit box when there is nothing to sample.</summary>
        void SampleMeshShape(out Vector3 pos, out Vector3 dir)
        {
            var S = m_Shape;
            if (S.shapeType == ParticleSystemShapeType.Mesh || S.shapeType == ParticleSystemShapeType.MeshRenderer || S.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer)
            {
                var mesh = MeshShapeSource(out var toShape, out bool identity, out var material);
                var table = mesh != null && mesh.m_Vertices.Length > 0 ? MeshShapeTableFor(mesh, toShape, identity) : null;
                if (table != null && SampleMeshTable(table, out pos, out dir, out var meshColor, out bool hasColor))
                {
                    pos += dir * S.normalOffset;
                    if (S.useMeshColors)
                    {
                        if (hasColor)
                        {
                            m_ShapeColor = meshColor;
                            m_ShapeColorSet = true;
                        }
                        else m_ShapeColorSet = MaterialShapeColor(material, out m_ShapeColor);
                    }
                    return;
                }
            }
            Sprite sprite = S.shapeType == ParticleSystemShapeType.SpriteRenderer && S.spriteRenderer != null ? S.spriteRenderer.sprite : S.sprite;
            if ((S.shapeType == ParticleSystemShapeType.Sprite || S.shapeType == ParticleSystemShapeType.SpriteRenderer) && sprite != null)
            {
                var b = sprite.bounds;
                pos = new Vector3(Mathf.Lerp(b.min.x, b.max.x, ShapeValue()), Mathf.Lerp(b.min.y, b.max.y, ShapeValue()), 0f);
                dir = Vector3.forward;
                return;
            }
            pos = new Vector3(ShapeValue() - 0.5f, ShapeValue() - 0.5f, ShapeValue() - 0.5f);
            dir = Vector3.forward;
        }

        // Mesh shapes (Unity 6000.0 ShapeModule: meshShapeType, meshSpawnMode/Spread/Speed,
        // useMeshMaterialIndex/meshMaterialIndex, useMeshColors, normalOffset). Documented:
        // Vertex emits from the vertices, Edge from the edges, Triangle from the surface. Not
        // documented (an unmeasured hypothesis): triangles are picked by area and edges by length,
        // uniformly over the surface or the wire. In the Loop and PingPong modes the emission
        // position moves across the edges (Edge) or the vertices (Vertex) with the spawn speed and
        // snaps to the spread; triangles are always random (Loop and PingPong warn once). Random
        // draws use the shape's random stream; Unity's own sequence is not reproduced.

        /// <summary>The colour the start colour is multiplied by (Use Mesh Colors), when
        /// <see cref="m_ShapeColorSet"/>; set by the last shape sample.</summary>
        Color m_ShapeColor;
        bool m_ShapeColorSet;
        MeshShapeTable m_MeshShapeTable;
        System.Collections.Generic.HashSet<(int, string)> m_MeshShapeWarnings;
        static readonly int s_ColorId = Shader.PropertyToID("_Color");
        static readonly int s_TintColorId = Shader.PropertyToID("_TintColor");

        /// <summary>A mesh prepared for sampling, and what it was prepared from: a table is used
        /// again only while all of that is unchanged.</summary>
        sealed class MeshShapeTable
        {
            internal Mesh Mesh;
            internal Vector3[] SourceVertices, SourceNormals;
            internal Color32[] SourceColors;
            internal (int[] Indices, int BaseVertex, MeshTopology Topology)[] SourceSubMeshes;
            internal int SyncVersion;
            internal Matrix4x4 ToShape;
            internal int SubMesh;
            /// <summary>Built while the mesh had unsent changes: valid for the frame it was built
            /// in only (in-place vertex buffer writes keep the arrays), so a mesh that is never
            /// drawn is prepared at most once a frame, not once a particle.</summary>
            internal bool BuiltDirty;
            internal int BuiltFrame;

            internal Vector3[] Vertices, Normals;
            internal Color[] Colors;
            /// <summary>The vertices the submesh uses, ascending; null: every vertex.</summary>
            internal int[] VertexList;
            /// <summary>Triangle corners; edge e runs from corner e to the next corner of its triangle.</summary>
            internal int[] Triangles;
            /// <summary>Running sums of the triangle areas and of the edge lengths.</summary>
            internal float[] TriangleEnds, EdgeEnds;
            internal bool SkippedTopology;

            internal bool Matches(Mesh mesh, Matrix4x4 toShape, int subMesh, int frame)
            {
                if (!ReferenceEquals(Mesh, mesh) || !ReferenceEquals(SourceVertices, mesh.m_Vertices) || !ReferenceEquals(SourceNormals, mesh.m_Normals)
                    || !ReferenceEquals(SourceColors, mesh.m_Colors) || SyncVersion != mesh.m_SyncVersion || SubMesh != subMesh || !ToShape.Equals(toShape)
                    || SourceSubMeshes.Length != mesh.m_SubMeshes.Count)
                    return false;
                if (mesh.m_Dirty && !(BuiltDirty && BuiltFrame == frame)) return false;
                for (int s = 0; s < SourceSubMeshes.Length; s++)
                {
                    var sm = mesh.m_SubMeshes[s];
                    var built = SourceSubMeshes[s];
                    if (!ReferenceEquals(built.Indices, sm.Indices) || built.BaseVertex != sm.BaseVertex || built.Topology != sm.Topology) return false;
                }
                return true;
            }
        }

        /// <summary>The mesh the shape emits from; the matrix from its vertices to the shape's
        /// space (the renderer's own space: identity unless the renderer's current pose is a
        /// deformed copy); the material Use Mesh Colors falls back to.</summary>
        Mesh MeshShapeSource(out Matrix4x4 toShape, out bool identity, out Material material)
        {
            var S = m_Shape;
            toShape = Matrix4x4.identity;
            identity = true;
            material = null;
            Renderer renderer = null;
            Mesh mesh = null;
            switch (S.shapeType)
            {
                case ParticleSystemShapeType.Mesh:
                    mesh = S.mesh;
                    break;
                case ParticleSystemShapeType.MeshRenderer:
                    if (S.meshRenderer != null)
                    {
                        renderer = S.meshRenderer;
                        var filter = S.meshRenderer.GetComponent<MeshFilter>();
                        mesh = filter != null ? filter.m_Mesh : null;
                    }
                    break;
                case ParticleSystemShapeType.SkinnedMeshRenderer:
                    if (S.skinnedMeshRenderer is SkinnedMeshRenderer skinned && skinned != null)
                    {
                        renderer = skinned;
                        // The current pose: the (skinned, blend-shaped or cloth) mesh the renderer
                        // draws this frame, brought back into the renderer's space.
                        mesh = skinned.DrawGeometry(out var model);
                        var localToWorld = skinned.transform.localToWorldMatrix;
                        if (mesh != null && !ReferenceEquals(mesh, skinned.sharedMesh) && !model.Equals(localToWorld))
                        {
                            toShape = skinned.transform.worldToLocalMatrix * model;
                            identity = false;
                        }
                    }
                    break;
            }
            if (renderer != null)
            {
                var materials = renderer.m_Materials;
                int slot = S.useMeshMaterialIndex ? S.meshMaterialIndex : 0;
                material = slot >= 0 && slot < materials.Length ? materials[slot] : null;
            }
            return mesh;
        }

        /// <summary>Use Mesh Colors without vertex colours: the material's "_Color", else its
        /// "_TintColor" (the documented fallback); nothing without either.</summary>
        static bool MaterialShapeColor(Material material, out Color color)
        {
            color = Color.white;
            if (material == null) return false;
            if (material.HasProperty(s_ColorId)) { color = material.GetColor(s_ColorId); return true; }
            if (material.HasProperty(s_TintColorId)) { color = material.GetColor(s_TintColorId); return true; }
            return false;
        }

        MeshShapeTable MeshShapeTableFor(Mesh mesh, Matrix4x4 toShape, bool identity)
        {
            var S = m_Shape;
            int subMesh = -1;
            if (S.useMeshMaterialIndex)
            {
                if (S.meshMaterialIndex >= 0 && S.meshMaterialIndex < mesh.m_SubMeshes.Count) subMesh = S.meshMaterialIndex;
                else WarnMeshShape(mesh, $"has no submesh {S.meshMaterialIndex} for Single Material ({mesh.m_SubMeshes.Count} submeshes); emitting from the whole mesh");
            }
            int frame = Time.frameCount;
            var table = m_MeshShapeTable;
            if (table != null && table.Matches(mesh, toShape, subMesh, frame)) return table;
            table = BuildMeshShapeTable(mesh, toShape, identity, subMesh);
            table.BuiltDirty = mesh.m_Dirty;
            table.BuiltFrame = frame;
            m_MeshShapeTable = table;
            return table;
        }

        static MeshShapeTable BuildMeshShapeTable(Mesh mesh, Matrix4x4 toShape, bool identity, int subMesh)
        {
            var source = mesh.m_Vertices;
            int n = source.Length;
            var t = new MeshShapeTable
            {
                Mesh = mesh, SourceVertices = source, SourceNormals = mesh.m_Normals, SourceColors = mesh.m_Colors,
                SyncVersion = mesh.m_SyncVersion, ToShape = toShape, SubMesh = subMesh,
                SourceSubMeshes = new (int[], int, MeshTopology)[mesh.m_SubMeshes.Count],
            };
            if (identity) t.Vertices = source;
            else
            {
                t.Vertices = new Vector3[n];
                for (int i = 0; i < n; i++) t.Vertices[i] = toShape.MultiplyPoint3x4(source[i]);
            }
            var normals = mesh.m_Normals;
            if (normals != null && normals.Length == n)
            {
                if (identity) t.Normals = normals;
                else
                {
                    t.Normals = new Vector3[n];
                    for (int i = 0; i < n; i++) t.Normals[i] = toShape.MultiplyVector(normals[i]);
                }
            }
            var colors = mesh.m_Colors;
            if (colors != null && colors.Length == n)
            {
                t.Colors = new Color[n];
                for (int i = 0; i < n; i++) t.Colors[i] = colors[i];
            }

            var triangles = new System.Collections.Generic.List<int>();
            var used = subMesh >= 0 ? new bool[n] : null;
            for (int s = 0; s < mesh.m_SubMeshes.Count; s++)
            {
                var sm = mesh.m_SubMeshes[s];
                t.SourceSubMeshes[s] = (sm.Indices, sm.BaseVertex, sm.Topology);
                if (subMesh >= 0 && s != subMesh) continue;
                var indices = sm.Indices;
                if (used != null)
                    foreach (int index in indices)
                    {
                        int v = index + sm.BaseVertex;
                        if ((uint)v < (uint)n) used[v] = true;
                    }
                if (sm.Topology != MeshTopology.Triangles)
                {
                    if (indices.Length > 0) t.SkippedTopology = true;
                    continue;
                }
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i] + sm.BaseVertex, b = indices[i + 1] + sm.BaseVertex, c = indices[i + 2] + sm.BaseVertex;
                    if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                }
            }
            if (used != null)
            {
                var list = new System.Collections.Generic.List<int>();
                for (int v = 0; v < n; v++) if (used[v]) list.Add(v);
                t.VertexList = list.ToArray();
            }
            t.Triangles = triangles.ToArray();
            var V = t.Vertices;
            int count = t.Triangles.Length / 3;
            t.TriangleEnds = new float[count];
            t.EdgeEnds = new float[count * 3];
            float area = 0f, length = 0f;
            for (int k = 0; k < count; k++)
            {
                var a = V[t.Triangles[k * 3]];
                var b = V[t.Triangles[k * 3 + 1]];
                var c = V[t.Triangles[k * 3 + 2]];
                area += 0.5f * Vector3.Cross(b - a, c - a).magnitude;
                t.TriangleEnds[k] = area;
                length += (b - a).magnitude;
                t.EdgeEnds[k * 3] = length;
                length += (c - b).magnitude;
                t.EdgeEnds[k * 3 + 1] = length;
                length += (a - c).magnitude;
                t.EdgeEnds[k * 3 + 2] = length;
            }
            return t;
        }

        /// <summary>A point of the mesh by the shape's mesh type and spawn mode, in the mesh's
        /// (shape) space, with its normal and (when the mesh has vertex colours) colour; false when
        /// the mesh has nothing of that kind to emit from.</summary>
        bool SampleMeshTable(MeshShapeTable t, out Vector3 pos, out Vector3 dir, out Color color, out bool hasColor)
        {
            var S = m_Shape;
            var V = t.Vertices;
            pos = Vector3.zero;
            dir = Vector3.forward;
            color = Color.white;
            hasColor = false;
            switch (S.meshShapeType)
            {
                case ParticleSystemMeshShapeType.Triangle:
                {
                    int count = t.TriangleEnds.Length;
                    float total = count > 0 ? t.TriangleEnds[count - 1] : 0f;
                    if (!(total > 0f))
                    {
                        WarnMeshShape(t.Mesh, "has no triangles with an area to emit from; emitting from a unit box");
                        return false;
                    }
                    // No ordered triangle sampling: say so instead of silently emitting at random.
                    if (S.meshSpawnMode == ParticleSystemShapeMultiModeValue.Loop || S.meshSpawnMode == ParticleSystemShapeMultiModeValue.PingPong)
                        WarnMeshShape(t.Mesh, "uses the Triangle mesh type with the Loop or Ping-Pong mode, which is not supported; emitting from random points of the triangles");
                    int k = MeshShapeFind(t.TriangleEnds, ShapeValue() * total);
                    // Uniform in the triangle (unmeasured hypothesis, as the area weighting).
                    float s = Mathf.Sqrt(ShapeValue()), r = ShapeValue();
                    float wa = 1f - s, wb = s * (1f - r), wc = s * r;
                    int a = t.Triangles[k * 3], b = t.Triangles[k * 3 + 1], c = t.Triangles[k * 3 + 2];
                    pos = V[a] * wa + V[b] * wb + V[c] * wc;
                    dir = MeshShapeNormal(t.Normals, a, b, c, wa, wb, wc, Vector3.Cross(V[b] - V[a], V[c] - V[a]));
                    if (t.Colors != null)
                    {
                        color = t.Colors[a] * wa + t.Colors[b] * wb + t.Colors[c] * wc;
                        hasColor = true;
                    }
                    break;
                }
                case ParticleSystemMeshShapeType.Edge:
                {
                    int count = t.EdgeEnds.Length;
                    float total = count > 0 ? t.EdgeEnds[count - 1] : 0f;
                    if (!(total > 0f))
                    {
                        WarnMeshShape(t.Mesh, "has no triangle edges with a length to emit from; emitting from a unit box");
                        return false;
                    }
                    // A position along all the edges: moving with the spawn speed in the loop modes.
                    float travel = S.meshSpawnSpeed.Evaluate(0f, 1f) * m_ShapeTime;
                    float u;
                    switch (S.meshSpawnMode)
                    {
                        case ParticleSystemShapeMultiModeValue.Loop: u = Frac(travel); break;
                        case ParticleSystemShapeMultiModeValue.PingPong: u = Mathf.PingPong(travel, 1f); break;
                        default: u = ShapeValue(); break;
                    }
                    if (S.meshSpawnSpread > 0f) u = Mathf.Floor(u / S.meshSpawnSpread) * S.meshSpawnSpread;
                    float d = Mathf.Clamp01(u) * total;
                    int e = MeshShapeFind(t.EdgeEnds, d);
                    float start = e > 0 ? t.EdgeEnds[e - 1] : 0f;
                    float edge = t.EdgeEnds[e] - start;
                    float f = edge > 0f ? Mathf.Clamp01((d - start) / edge) : 0f;
                    int a = t.Triangles[e], b = t.Triangles[e - e % 3 + (e % 3 + 1) % 3];
                    pos = Vector3.LerpUnclamped(V[a], V[b], f);
                    dir = MeshShapeNormal(t.Normals, a, b, b, 1f - f, f, 0f, Vector3.zero);
                    if (t.Colors != null)
                    {
                        color = Color.LerpUnclamped(t.Colors[a], t.Colors[b], f);
                        hasColor = true;
                    }
                    break;
                }
                default:
                {
                    int count = t.VertexList?.Length ?? V.Length;
                    if (count == 0)
                    {
                        WarnMeshShape(t.Mesh, $"has no vertices in submesh {t.SubMesh} to emit from; emitting from a unit box");
                        return false;
                    }
                    int k;
                    if (S.meshSpawnMode == ParticleSystemShapeMultiModeValue.Loop || S.meshSpawnMode == ParticleSystemShapeMultiModeValue.PingPong)
                    {
                        // The emission position moves across the vertices with the spawn speed, as
                        // along the edges: fixed at speed 0, snapped to the spread. The travel rate
                        // and the vertex order it maps to are unmeasured.
                        float travel = S.meshSpawnSpeed.Evaluate(0f, 1f) * m_ShapeTime;
                        float u = S.meshSpawnMode == ParticleSystemShapeMultiModeValue.Loop ? Frac(travel) : Mathf.PingPong(travel, 1f);
                        if (S.meshSpawnSpread > 0f) u = Mathf.Floor(u / S.meshSpawnSpread) * S.meshSpawnSpread;
                        k = Math.Min((int)(Mathf.Clamp01(u) * count), count - 1);
                    }
                    else k = Math.Min((int)(ShapeValue() * count), count - 1);
                    int v = t.VertexList != null ? t.VertexList[k] : k;
                    pos = V[v];
                    dir = t.Normals != null && t.Normals[v].sqrMagnitude > 0f ? t.Normals[v].normalized : Vector3.forward;
                    if (t.Colors != null)
                    {
                        color = t.Colors[v];
                        hasColor = true;
                    }
                    break;
                }
            }
            if (t.SkippedTopology && S.meshShapeType != ParticleSystemMeshShapeType.Vertex)
                WarnMeshShape(t.Mesh, "has submeshes that are not triangles; emitting from its triangle submeshes only");
            return true;
        }

        /// <summary>The first running sum above <paramref name="x"/>; past the end, the last
        /// element with a size.</summary>
        static int MeshShapeFind(float[] ends, float x)
        {
            int lo = 0, hi = ends.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (ends[mid] > x) hi = mid;
                else lo = mid + 1;
            }
            while (lo > 0 && ends[lo] == ends[lo - 1]) lo--;
            return lo;
        }

        /// <summary>The interpolated vertex normal; without normals, the face normal; else forward.</summary>
        static Vector3 MeshShapeNormal(Vector3[] normals, int a, int b, int c, float wa, float wb, float wc, Vector3 face)
        {
            if (normals != null)
            {
                var n = normals[a] * wa + normals[b] * wb + normals[c] * wc;
                if (n.sqrMagnitude > 1e-12f) return n.normalized;
            }
            return face.sqrMagnitude > 1e-12f ? face.normalized : Vector3.forward;
        }

        void WarnMeshShape(Mesh mesh, string problem)
        {
            m_MeshShapeWarnings ??= new System.Collections.Generic.HashSet<(int, string)>();
            if (m_MeshShapeWarnings.Add((mesh.GetInstanceID(), problem)))
                Debug.LogWarning($"Unbound: the shape of particle system '{name}': mesh '{mesh.name}' {problem}.");
        }

        Vector3 RandomUnitVector()
        {
            float z = ShapeValue() * 2f - 1f;
            float a = ShapeValue() * Mathf.PI * 2f;
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
            public float NoiseTime;
            public bool InheritCurrent;
            public bool BirthSubs;
            public bool TemporaryLane;
        }

        /// <param name="dt">The step's length: system-time curves are read where it ends (measured
        /// for the gravity modifier, as for the noise scroll speed).</param>
        StepContext MakeContext(float dt = 0f)
        {
            var c = new StepContext();
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            float endTime = m_Time + dt;
            if (m_Main.loop) endTime %= duration;
            c.Nt = Mathf.Clamp01(endTime / duration);
            c.WorldToSim = WorldToSimulationRotation();
            c.HasGravity = m_Main.gravityModifier.mode != ParticleSystemCurveMode.Constant || m_Main.gravityModifier.constant != 0f;
            bool simWorld = m_Main.simulationSpace == ParticleSystemSimulationSpace.World;
            if (c.HasGravity)
            {
                // Into simulation space with its scale: local space divides by the scaling mode's
                // emitter scale, custom space by the custom transform's (measured).
                var gravityWorld = SourceGravity(m_Main.gravitySource);
                c.Gravity = simWorld ? gravityWorld : SimulationToWorld().inverse.MultiplyVector(gravityWorld);
            }
            c.Center = simWorld ? m_GameObject.m_Transform.position : Vector3.zero;
            if (m_Main.simulationSpace == ParticleSystemSimulationSpace.Custom && m_Main.customSimulationSpace != null)
                c.Center = m_Main.customSimulationSpace.InverseTransformPoint(m_GameObject.m_Transform.position);
            c.InheritCurrent = m_InheritVelocity.enabled && m_InheritVelocity.mode == ParticleSystemInheritVelocityMode.Current && simWorld;
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
                // A particle a trigger killed in the previous update dies now, without ageing again,
                // like one whose life ran out exactly at the end of that update.
                bool killed = (m_Particles[i].m_Flags & Particle.FlagTriggerKilled) != 0;
                float ageBefore = 0f;
                if (deathSubs)
                {
                    // Integrate's first action; its lifetime curves use the age before the step.
                    if (!killed && (m_Particles[i].m_Flags & Particle.FlagAgeInitialized) == 0) m_Particles[i].InitializeAge();
                    ageBefore = m_Particles[i].Age01;
                }
                bool agedOut = false;
                BeginModuleSample(i, m_Particles[i].m_RandomSeed);
                if (!killed && Integrate(ref m_Particles[i], dt, ref m_Context, true, out agedOut))
                {
                    i++;
                    continue;
                }
                if (deathSubs)
                {
                    // Its Death sub-emitters fire where the whole step's movement takes it, not where
                    // its life ran out (6000.0.71f1 trigger lifetime natural/trigger_death_child and
                    // broad sub_emitters_child: .46 at 2/s with .013 s left of a .025 s step emits at
                    // .51; killed at .51, it emits at .56 in the next step; the broad parent dying in
                    // the first of two steps emits at 1.05). Only the event's position moves: the
                    // particle itself, its trail and what children inherit from it stay as they were.
                    var dying = m_Particles[i];
                    if (killed || agedOut)
                    {
                        var end = dying;
                        Move(ref end, dt, ref m_Context, ageBefore, out _);
                        dying.m_Position = end.m_Position;
                    }
                    TriggerSubEmitters(ParticleSystemSubEmitterType.Death, ref dying);
                }
                if (KeepOrphanTrail(ref m_Particles[i])) { i++; continue; }
                EndTrail(ref m_Particles[i]);
                RemoveAt(i);
            }
        }

        /// <summary>Advances one particle by <paramref name="dt"/>; false when it dies. The lifetime
        /// curves are evaluated at the particle's age before the step (measured: velocity over
        /// lifetime moves a newborn not at all in its first step). <paramref name="existing"/>: the
        /// particle was alive before this update (false for a newborn being emitted).</summary>
        bool Integrate(ref Particle p, float dt, ref StepContext c, bool existing = false) => Integrate(ref p, dt, ref c, existing, out _);

        /// <summary>As above; <paramref name="agedOut"/>: it died because its life ran out in this
        /// step, before it moved (not in a collision).</summary>
        bool Integrate(ref Particle p, float dt, ref StepContext c, bool existing, out bool agedOut)
        {
            agedOut = false;
            if ((p.m_Flags & Particle.FlagAgeInitialized) == 0) p.InitializeAge();
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
                if (ring != ParticleSystemRingBufferMode.PauseUntilReplaced)
                {
                    agedOut = true;
                    return false;
                }
                p.m_AgePercent = 100f;
                p.m_Lifetime = 0f;
                return true;
            }
            var before = p.m_Position;
            float speedModifier = Move(ref p, dt, ref c, age, out var moving);
            if (!c.TemporaryLane && m_Collision.enabled && !Collide(ref p, before, age, dt, speedModifier)) return false;
            if (!c.TemporaryLane && m_Trigger.enabled && !Trigger(ref p))
            {
                // A newborn killed while it is emitted is never added (measured: none is
                // reported, even in a call's last step). An existing particle's life ends
                // here: it keeps its moved state with zero remaining lifetime, and the next
                // update removes it (measured: killed in a call's last internal step it is
                // reported at lifetime 0, killed in an earlier one it is gone). The rest of
                // this update still skips it, as before.
                if (!existing) return false;
                p.m_AgePercent = 100f;
                p.m_Lifetime = 0f;
                p.m_Flags |= Particle.FlagTriggerKilled;
                return true;
            }

            uint seed = p.m_RandomSeed;
            var RO = m_Rotation;
            var RS = m_RotationBySpeed;
            if (RO.enabled || RS.enabled)
            {
                // The rotation modules set the angular velocity (GetParticles reports it).
                var w = Vector3.zero;
                if (RO.enabled)
                {
                    float r = ModuleValue(seed, 1793934638u);
                    w += RO.separateAxes ? new Vector3(RO.x.Evaluate(age, r), RO.y.Evaluate(age, r), RO.z.Evaluate(age, r)) : new Vector3(0f, 0f, RO.z.Evaluate(age, r));
                }
                if (RS.enabled)
                {
                    float s = Mathf.InverseLerp(RS.range.x, RS.range.y, moving.magnitude);
                    float r = ModuleValue(seed, 3737431713u);
                    w += RS.separateAxes ? new Vector3(RS.x.Evaluate(s, r), RS.y.Evaluate(s, r), RS.z.Evaluate(s, r)) : new Vector3(0f, 0f, RS.z.Evaluate(s, r));
                }
                if ((p.m_Flags & Particle.FlagFlipRotation) != 0) w = -w;
                p.m_AngularVelocity = w;
            }
            p.m_Rotation += p.m_AngularVelocity * dt;

            if (c.BirthSubs) BirthSubEmitters(ref p, ageBefore, p.m_StartLifetime - p.m_Lifetime, dt, before);
            return true;
        }

        /// <summary>The velocity modules and one step's movement: sets the particle's velocity,
        /// animated velocity and position, with the lifetime curves at <paramref name="age"/>. Returns
        /// the speed modifier; <paramref name="moving"/> is the whole velocity it moved with.</summary>
        float Move(ref Particle p, float dt, ref StepContext c, float age, out Vector3 moving)
        {
            uint seed = p.m_RandomSeed;
            var v = p.m_Velocity;
            var V = m_Velocity;
            var F = m_Force;

            if (c.HasGravity)
                v += c.Gravity * (m_Main.gravityModifier.Evaluate(c.Nt, ModuleValue(seed, 3803694019u)) * dt);

            if (m_ExternalForces.enabled)
                ApplyForceFields(ref p, ref v, age, c.WorldToSim, dt);

            if (F.enabled)
            {
                // The axes are evaluated in the first axis' mode: a constant reads a curve's
                // multiplier (Unity shares one scalar between them).
                var random = F.randomized ? m_ForceSamples[m_ModuleLane] : ModuleVector(seed, 306581307u);
                var mode = F.x.mode;
                var f = new Vector3(InMode(F.x, mode).Evaluate(age, random.x), InMode(F.y, mode).Evaluate(age, random.y), InMode(F.z, mode).Evaluate(age, random.z));
                v += ModuleToSimulation(f, F.space, c.WorldToSim) * dt;
            }

            var animated = Vector3.zero;
            float speedModifier = 1f;
            if (V.enabled)
            {
                if (V.x.mode == V.y.mode && V.y.mode == V.z.mode)
                {
                    var random = ModuleVector(seed, 3774601268u);
                    var lin = new Vector3(V.x.Evaluate(age, random.x), V.y.Evaluate(age, random.y), V.z.Evaluate(age, random.z));
                    if (lin != Vector3.zero) animated += ModuleToSimulation(lin, V.space, c.WorldToSim);
                }
                else ReportOncePerPass("Particle Velocity curves must all be in the same mode");
                speedModifier = V.speedModifier.Evaluate(age, ModuleValue(m_ModuleGroupSeed, 3129089844u));
                var orbitRandom = ModuleVector(seed, 3509140396u);
                var orbital = Vector3.zero;
                if (V.orbitalX.mode == V.orbitalY.mode && V.orbitalY.mode == V.orbitalZ.mode)
                    orbital = new Vector3(V.orbitalX.Evaluate(age, orbitRandom.x), V.orbitalY.Evaluate(age, orbitRandom.y), V.orbitalZ.Evaluate(age, orbitRandom.z));
                else ReportOncePerPass("Particle Orbital Velocity curves must all be in the same mode");
                float radial = V.radial.Evaluate(age, ModuleValue(seed, 3400765981u));
                if ((orbital != Vector3.zero || radial != 0f) && dt > 1e-5f)
                {
                    // The offset from the center turns by orbital × dt and moves out by radial × dt
                    // (both times the speed modifier); the velocity reported is that movement over
                    // the step without the modifier (measured exactly).
                    var offset = new Vector3(V.orbitalOffsetX.Evaluate(age, orbitRandom.x), V.orbitalOffsetY.Evaluate(age, orbitRandom.y), V.orbitalOffsetZ.Evaluate(age, orbitRandom.z));
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
            moving = v + animated;
            p.m_Position += moving * (speedModifier * dt);
            return speedModifier;
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
            float r = ModuleValue(seed, 322376503u);
            float dampen = Mathf.Clamp01(L.dampen);
            float keep = dampen >= 1f ? 0f : Mathf.Pow(1f - dampen, dt * 30f);
            var total = v + animated;
            if (L.separateAxes)
            {
                var random = ModuleVector(seed, 322376503u);
                var limits = new Vector3(L.limitX.Evaluate(age, random.x), L.limitY.Evaluate(age, random.y), L.limitZ.Evaluate(age, random.z));
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
            float drag = L.drag.Evaluate(age, ModuleValue(seed, 2792948374u));
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
            var emitters = m_SubEmitters.emitters;
            for (int i = 0; i < emitters.Count; i++)
            {
                var e = emitters[i];
                if (e.type != type || FiredEarlier(emitters, i)) continue;
                TriggerSubEmitterAt(e, ref parent);
            }
        }

        /// <summary>Whether an earlier slot of the same type already emits from the same child: a
        /// child in two Death slots emits once, from the first slot (6000.0.71f1 subemitters
        /// death/two_slots; different children both emit, death/two_children).</summary>
        static bool FiredEarlier(System.Collections.Generic.List<SubEmitter> emitters, int index)
        {
            var e = emitters[index];
            if (e.system == null) return false;
            for (int j = 0; j < index; j++)
                if (emitters[j].type == e.type && emitters[j].system == e.system) return true;
            return false;
        }

        /// <summary>A Death sub-emitter, or one triggered from script (Manual): the child emits its
        /// first burst at the particle, along the particle's direction of travel.</summary>
        void TriggerSubEmitterAt(SubEmitter sub, ref Particle parent)
        {
            var child = sub.system;
            if (child == null || child == this || child.m_GameObject == null) return;
            if (sub.emitProbability <= 0f || sub.emitProbability < 1f && ModuleValue(parent.m_RandomSeed, 1520730008u) > sub.emitProbability) return;
            int count = child.EventBurstCount(parent.m_RandomSeed, m_Main.randomSeed);
            if (count <= 0) return;
            if (ParticleSystemManager.Updating)
                ParticleSystemManager.HoldEventEmission(new ParticleSystemManager.EventEmission
                {
                    child = child, parentSystem = this, parent = parent, properties = sub.properties, count = count,
                });
            else child.EmitFromParent(this, ref parent, sub.properties, count, 0f, eventEmission: true);
        }

        /// <summary>A Death emission the frame's update held (see ParticleSystemManager): emitted now,
        /// and the child plays the particles it received.</summary>
        internal void EmitEventNow(ParticleSystem parentSystem, ref Particle parent, ParticleSystemSubEmitterProperties properties, int count)
        {
            EmitFromParent(parentSystem, ref parent, properties, count, 0f, eventEmission: true);
            if (m_Count > 0 && !m_IsPlaying)
            {
                m_IsPlaying = true;
                m_StopEmitting = true;
            }
        }

        /// <summary>How many particles a Death, Manual, Collision or Trigger sub-emitter's child
        /// emits for one parent particle. SubEmittersModule.death0 and collision0 (6000.0): the
        /// sub-emitter "uses the first set of emission burst properties ... If the system does not
        /// contain burst properties, then the system emits nothing; if the system contains more than
        /// one set of burst properties, then it ignores the additional properties." Measured in
        /// 6000.0.71f1 (subemitters probe) for Death and for TriggerSubEmitter on Manual and Death
        /// slots: the first burst in the list (not the earliest), at once, whatever its time; its count
        /// once (cycles, also infinite ones, and the interval are ignored); its probability applies;
        /// the child's Emission module may be disabled. Collision and Trigger follow the same rule
        /// (unmeasured).</summary>
        int EventBurstCount(uint parentSeed, uint systemSeed)
        {
            var bursts = m_Emission.bursts;
            if (bursts.Count == 0) return 0;
            var burst = bursts[0];
            var random = ModuleRandom(parentSeed, systemSeed);
            if (burst.probability <= 0f || burst.probability < 1f && random.NextValue() >= burst.probability) return 0;
            return BurstCount(burst.m_Count, 0f, ref random);
        }

        /// <summary>Birth sub-emitters emit with their own rate and bursts over the parent particle's life.</summary>
        void BirthSubEmitters(ref Particle parent, float ageBefore, float ageAfter, float dt, Vector3 positionBefore)
        {
            bool firstBirth = ageBefore <= 0f && (!m_SimulatingBirths || (parent.m_Flags & Particle.FlagBirthBurstsDone) == 0);
            int slot = 0;
            foreach (var e in m_SubEmitters.emitters)
            {
                if (e.type != ParticleSystemSubEmitterType.Birth) continue;
                var child = e.system;
                if (child == null || child == this || child.m_GameObject == null) { slot++; continue; }
                if (e.emitProbability <= 0f || e.emitProbability < 1f && ModuleValue(parent.m_RandomSeed, 1520730008u) > e.emitProbability) { slot++; continue; }
                float age01 = parent.Age01;
                int n = 0;
                float birthAge = 0f, spacing = 0f;
                if (child.m_Emission.enabled)
                {
                    var emissionRandom = ModuleRandom(parent.m_RandomSeed, m_Main.randomSeed);
                    float rate = child.m_Emission.rateOverTime.Evaluate(age01, emissionRandom.NextValue());
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
                    if (n > 0 && rate > 0f)
                    {
                        float remainder = slot == 0 ? parent.m_EmitAccumulator0 : parent.m_EmitAccumulator1;
                        birthAge = remainder / rate;
                        spacing = -1f / rate;
                    }
                    foreach (var burst in child.m_Emission.bursts)
                    {
                        float at = burst.m_Time;
                        bool first = firstBirth && at <= 0f;
                        if ((first || (at > ageBefore && at <= ageAfter)) &&
                            (burst.probability >= 1f || burst.probability > 0f && emissionRandom.NextValue() < burst.probability))
                        {
                            int burstCount = BurstCount(burst.m_Count, age01, ref emissionRandom);
                            if (burstCount > 0 && m_SimulatingBirths)
                            {
                                var birth = parent;
                                float preAge = first ? dt : Mathf.Max(0f, ageAfter - at);
                                birth.m_Position = Vector3.LerpUnclamped(positionBefore, parent.m_Position,
                                    dt > 0f ? 1f - preAge / dt : 0f);
                                child.EmitFromParent(this, ref birth, e.properties, burstCount, preAge,
                                    birthCatchup: m_BirthCallRemaining);
                            }
                            else n += burstCount;
                        }
                    }
                }
                if (n > 0)
                {
                    if (m_SimulatingBirths)
                    {
                        var birth = parent;
                        birth.m_Position = Vector3.LerpUnclamped(positionBefore, parent.m_Position,
                            dt > 0f ? 1f - birthAge / dt : 0f);
                        child.EmitFromParent(this, ref birth, e.properties, n, birthAge,
                            birthCatchup: m_BirthCallRemaining, birthSpacing: spacing,
                            birthVelocity: dt > 0f ? (parent.m_Position - positionBefore) / dt : Vector3.zero);
                    }
                    else child.EmitFromParent(this, ref parent, e.properties, n, 0f);
                }
                slot++;
            }
            if (m_SimulatingBirths) parent.m_Flags |= Particle.FlagBirthBurstsDone;
        }

        /// <summary>Emits `count` particles at a parent system's particle, inheriting what the
        /// sub-emitter's properties ask for.
        /// <para><paramref name="eventEmission"/> (Death and Manual) follows what 6000.0.71f1 shows for
        /// those (subemitters probe): the child's emission frame turns its +Z to the parent
        /// particle's world direction of travel and its +Y towards +Z as far as that direction
        /// allows (Quaternion.LookRotation(direction, Vector3.forward)), measured for an unrotated
        /// parent and child with axis-aligned and oblique directions and a shifted shape; a still
        /// particle leaves the child's own frame; under a rotated parent the child's rotation that it
        /// inherits from the parent does not turn the direction (direction/rotated_parent). Unbound's
        /// choices (unmeasured): the up axis is the parent's simulation-space +Z, travel along it keeps
        /// those axes and travel against it turns 180° about Y. A child rotated against its parent
        /// combines that rotation with the frame in a way not reproduced here (Unbound applies it
        /// inside the frame); its emission direction stays a known difference. InheritLifetime
        /// multiplies the start lifetime by the parent's remaining lifetime in seconds, not its
        /// fraction (2.013 × 3.863 s left gives 7.776; a dying parent leaves none, so the children
        /// are not emitted).</para></summary>
        void EmitFromParent(ParticleSystem parentSystem, ref Particle parent, ParticleSystemSubEmitterProperties properties, int count, float preAge, bool eventEmission = false, float birthCatchup = -1f, float birthSpacing = 0f, Vector3 birthVelocity = default)
        {
            // The measured constant-lifetime Birth controls skip an emission whose
            // remaining call time reaches its lifetime bound, without consuming start samples.
            var lifetime = m_Main.startLifetime;
            if (birthCatchup >= 0f && !m_LifetimeByEmitterSpeed.enabled && (properties & ParticleSystemSubEmitterProperties.InheritLifetime) == 0
                && (lifetime.mode == ParticleSystemCurveMode.Constant || lifetime.mode == ParticleSystemCurveMode.TwoConstants)
                && birthCatchup >= lifetime.MaxValue) return;
            var origin = parentSystem.SimulationToWorld().MultiplyPoint3x4(parent.m_Position);
            Quaternion? frame = null;
            if (eventEmission || birthCatchup >= 0f)
            {
                var travel = parentSystem.SimulationToWorld().MultiplyVector(parent.m_Velocity + parent.m_AnimatedVelocity);
                if (travel.sqrMagnitude > 1e-12f)
                {
                    var simRotation = Quaternion.Inverse(parentSystem.WorldToSimulationRotation());
                    var forward = travel.normalized;
                    var up = simRotation * Vector3.forward;
                    if (birthCatchup >= 0f)
                    {
                        // Measured Birth frame: blend the child's forward and up axes,
                        // align with travel, then apply its rotation relative to the parent.
                        var childRotation = m_GameObject.m_Transform.rotation;
                        var childForward = childRotation * Vector3.forward;
                        float blend = Mathf.Abs(Vector3.Dot(forward, childForward));
                        up = Vector3.LerpUnclamped(childForward, childRotation * Vector3.up, blend);
                    }
                    float along = Vector3.Dot(forward, up);
                    var align = Mathf.Abs(along) < 1f - 1e-6f ? Quaternion.LookRotation(forward, up)
                        : along > 0f ? simRotation : simRotation * Quaternion.AngleAxis(180f, Vector3.up);
                    var parentTransform = parentSystem.m_GameObject.m_Transform.rotation;
                    frame = align * (Quaternion.Inverse(parentTransform) * m_GameObject.m_Transform.rotation);
                }
            }
            var emitter = EmitterMatrix();
            Color parentColor = parentSystem.CurrentColor(ref parent);
            var parentSize = parentSystem.CurrentSize3D(ref parent);
            var parentRotation = parent.m_Rotation;
            float parentLife = eventEmission ? Mathf.Max(parent.m_Lifetime, 0f)
                : parent.m_StartLifetime > 0f ? parent.m_Lifetime / parent.m_StartLifetime : 0f;
            EmitParams none = default;
            var context = MakeContext();
            for (int k = 0; k < count; k++)
            {
                int index = Allocate();
                if (index < 0) return;
                ref var p = ref m_Particles[index];
                var birthOrigin = origin + parentSystem.SimulationToWorld().MultiplyVector(birthVelocity) * (birthSpacing * k);
                if (!InitParticle(ref p, emitter, Mathf.Clamp01(m_Time / Mathf.Max(m_Main.duration, 0.0001f)), false, ref none, k, count, birthOrigin,
                    k, count, (properties & ParticleSystemSubEmitterProperties.InheritRotation) != 0 ? parent.m_AxisOfRotation : null, frame))
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
                BeginModuleSample(k, p.m_RandomSeed);
                if (!Integrate(ref p, Mathf.Max(0f, preAge - birthSpacing * k), ref context)) RemoveAt(index);
                else
                {
                    bool alive = true;
                    if (birthCatchup >= 0f)
                    {
                        float remaining = birthCatchup;
                        float step = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                        for (int guard = 0; remaining >= step && guard < 100000; guard++)
                        {
                            remaining -= step;
                            if (!Integrate(ref p, step, ref context)) { alive = false; break; }
                        }
                    }
                    if (alive) StartTrail(ref p);
                    else RemoveAt(index);
                }
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
                float r = ModuleValue(seed, 2368504881u);
                if (m_Size.separateAxes)
                    size = Vector3.Scale(size, new Vector3(m_Size.x.Evaluate(age, r), m_Size.y.Evaluate(age, r), m_Size.z.Evaluate(age, r)));
                else
                    size *= m_Size.x.Evaluate(age, r);
            }
            if (m_SizeBySpeed.enabled)
            {
                var range = m_SizeBySpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, (p.m_Velocity + p.m_AnimatedVelocity).magnitude);
                float r = ModuleValue(seed, 4085612399u);
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
                c *= m_Color.color.Evaluate(p.Age01, ModuleValue(seed, 1494990940u));
            if (m_ColorBySpeed.enabled)
            {
                var range = m_ColorBySpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, (p.m_Velocity + p.m_AnimatedVelocity).magnitude);
                c *= m_ColorBySpeed.color.Evaluate(s, ModuleValue(seed, 1089181156u));
            }
            return c;
        }

        internal int MeshIndexOf(ref Particle p)
        {
            if ((p.m_Flags & Particle.FlagMeshIndexSet) != 0) return p.m_MeshIndex;
            var renderer = m_GameObject != null ? m_GameObject.GetComponent<ParticleSystemRenderer>() : null;
            int count = renderer != null ? renderer.meshCount : 1;
            if (count <= 1) return 0;
            return (int)(ModuleWord(p.m_RandomSeed, 3159510623u) % (uint)count);
        }
    }
}
