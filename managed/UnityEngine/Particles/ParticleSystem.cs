using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;

namespace UnityEngine
{
    /// <summary>
    /// Unity's particle system (Shuriken), simulated on the managed side.
    ///
    /// Play state follows Unity: <c>Play</c> starts emitting (after the start delay, or prewarmed);
    /// a non-looping system stops emitting after its duration and stops playing once its last
    /// particle dies, then runs its stop action; <c>Stop</c> stops emitting (or also clears);
    /// particles emitted with <c>Emit</c> live and move whether or not the system plays.
    /// Systems step where Unity steps them (PreLateUpdate.ParticleSystemBeginUpdateAll), with the
    /// frame's time split in steps of at most <c>Time.maximumParticleDeltaTime</c>.
    /// </summary>
    [ClassId(198)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class ParticleSystem : Component
    {
        internal Particle[] m_Particles = Array.Empty<Particle>();
        internal int m_Count;
        // m_Custom[stream][particle], allocated when custom data is set or computed.
        internal Vector4[][] m_Custom = new Vector4[2][];

        // Play state. "Playing" lasts until the last particle died after emission stopped.
        internal bool m_IsPlaying;
        internal bool m_IsPaused;
        internal bool m_StopEmitting;
        internal float m_Time;
        internal float m_TotalTime;
        internal float m_DelayRemaining;
        internal int m_Loop;
        internal float m_RateAccumulator;
        /// <summary>Simulate with a fixed time step: the time not yet stepped.</summary>
        internal float m_FixedRemainder;
        internal float m_DistanceAccumulator;
        internal int[] m_BurstCycles = Array.Empty<int>();
        internal ulong m_RandomState;
        static uint s_AutoSeed = 0x2545f491u;

        internal bool m_Registered;
        /// <summary>The system that drives this one as a sub-emitter; it does not emit by itself then.</summary>
        internal ParticleSystem m_SubEmitterParent;

        public ParticleSystem() { }

        static ParticleSystem() => ParticleSystemManager.EnsureRegistered();

        // ------------------------------------------------------------------ state

        public bool isPlaying => m_IsPlaying && !m_IsPaused;
        public bool isEmitting => m_IsPlaying && !m_IsPaused && !m_StopEmitting;
        public bool isStopped => !m_IsPlaying && !m_IsPaused;
        public bool isPaused => m_IsPaused;
        public int particleCount => m_Count;

        public float time
        {
            get => m_Time;
            set => m_Time = Mathf.Clamp(value, 0f, m_Main.duration);
        }

        public float totalTime => m_TotalTime;

        public uint randomSeed
        {
            get => m_Main.randomSeed;
            set
            {
                if (isPlaying || m_Count > 0)
                {
                    Debug.LogError("Cannot set the random seed while system is playing.\nUse ParticleSystem.Stop() before changing the random seed or use ParticleSystem.useAutoRandomSeed.", this);
                    return;
                }
                m_Main.randomSeed = value;
            }
        }

        public bool useAutoRandomSeed
        {
            get => m_Main.autoRandomSeed;
            set
            {
                if (isPlaying || m_Count > 0)
                {
                    Debug.LogError("Cannot change useAutoRandomSeed while system is playing.\nUse ParticleSystem.Stop() before changing useAutoRandomSeed.", this);
                    return;
                }
                m_Main.autoRandomSeed = value;
            }
        }

        public bool proceduralSimulationSupported => false;

        public bool has3DParticleRotations =>
            m_Main.startRotation3D || (m_Rotation.enabled && m_Rotation.separateAxes) || (m_RotationBySpeed.enabled && m_RotationBySpeed.separateAxes);

        public bool hasNonUniformParticleSizes =>
            m_Main.startSize3D || (m_Size.enabled && m_Size.separateAxes) || (m_SizeBySpeed.enabled && m_SizeBySpeed.separateAxes);

        // ------------------------------------------------------------------ modules

        public MainModule main => new MainModule(this);
        public EmissionModule emission => new EmissionModule(this);
        public ShapeModule shape => new ShapeModule(this);
        public VelocityOverLifetimeModule velocityOverLifetime => new VelocityOverLifetimeModule(this);
        public LimitVelocityOverLifetimeModule limitVelocityOverLifetime => new LimitVelocityOverLifetimeModule(this);
        public InheritVelocityModule inheritVelocity => new InheritVelocityModule(this);
        public LifetimeByEmitterSpeedModule lifetimeByEmitterSpeed => new LifetimeByEmitterSpeedModule(this);
        public ForceOverLifetimeModule forceOverLifetime => new ForceOverLifetimeModule(this);
        public ColorOverLifetimeModule colorOverLifetime => new ColorOverLifetimeModule(this);
        public ColorBySpeedModule colorBySpeed => new ColorBySpeedModule(this);
        public SizeOverLifetimeModule sizeOverLifetime => new SizeOverLifetimeModule(this);
        public SizeBySpeedModule sizeBySpeed => new SizeBySpeedModule(this);
        public RotationOverLifetimeModule rotationOverLifetime => new RotationOverLifetimeModule(this);
        public RotationBySpeedModule rotationBySpeed => new RotationBySpeedModule(this);
        public ExternalForcesModule externalForces => new ExternalForcesModule(this);
        public NoiseModule noise => new NoiseModule(this);
        public CollisionModule collision => new CollisionModule(this);
        public TriggerModule trigger => new TriggerModule(this);
        public SubEmittersModule subEmitters => new SubEmittersModule(this);
        public TextureSheetAnimationModule textureSheetAnimation => new TextureSheetAnimationModule(this);
        public LightsModule lights => new LightsModule(this);
        public TrailModule trails => new TrailModule(this);
        public CustomDataModule customData => new CustomDataModule(this);

        // ------------------------------------------------------------------ obsolete accessors

        [Obsolete("safeCollisionEventSize has been deprecated. Use GetSafeCollisionEventSize() instead (UnityUpgradable) -> ParticlePhysicsExtensions.GetSafeCollisionEventSize(UnityEngine.ParticleSystem)", false)]
        public int safeCollisionEventSize => 0;

        [Obsolete("startDelay property is deprecated. Use main.startDelay or main.startDelayMultiplier instead.", false)]
        public float startDelay { get => m_Main.startDelay.constant; set => m_Main.startDelay = value; }

        [Obsolete("loop property is deprecated. Use main.loop instead.", false)]
        public bool loop { get => m_Main.loop; set => m_Main.loop = value; }

        [Obsolete("playOnAwake property is deprecated. Use main.playOnAwake instead.", false)]
        public bool playOnAwake { get => m_Main.playOnAwake; set => m_Main.playOnAwake = value; }

        [Obsolete("duration property is deprecated. Use main.duration instead.", false)]
        public float duration => m_Main.duration;

        [Obsolete("playbackSpeed property is deprecated. Use main.simulationSpeed instead.", false)]
        public float playbackSpeed { get => m_Main.simulationSpeed; set => m_Main.simulationSpeed = value; }

        [Obsolete("enableEmission property is deprecated. Use emission.enabled instead.", false)]
        public bool enableEmission { get => m_Emission.enabled; set => m_Emission.enabled = value; }

        [Obsolete("emissionRate property is deprecated. Use emission.rateOverTime, emission.rateOverDistance, emission.rateOverTimeMultiplier or emission.rateOverDistanceMultiplier instead.", false)]
        public float emissionRate { get => m_Emission.rateOverTime.constantMax; set => m_Emission.rateOverTime = value; }

        [Obsolete("startSpeed property is deprecated. Use main.startSpeed or main.startSpeedMultiplier instead.", false)]
        public float startSpeed { get => m_Main.startSpeed.constant; set => m_Main.startSpeed = value; }

        [Obsolete("startSize property is deprecated. Use main.startSize or main.startSizeMultiplier instead.", false)]
        public float startSize { get => m_Main.startSizeX.constant; set => m_Main.startSizeX = value; }

        [Obsolete("startColor property is deprecated. Use main.startColor instead.", false)]
        public Color startColor { get => m_Main.startColor.color; set => m_Main.startColor = value; }

        [Obsolete("startRotation property is deprecated. Use main.startRotation or main.startRotationMultiplier instead.", false)]
        public float startRotation { get => m_Main.startRotationZ.constant; set => m_Main.startRotationZ = value; }

        [Obsolete("startRotation3D property is deprecated. Use main.startRotationX, main.startRotationY and main.startRotationZ instead. (Or main.startRotationXMultiplier, main.startRotationYMultiplier and main.startRotationZMultiplier).", false)]
        public Vector3 startRotation3D
        {
            get => new Vector3(m_Main.startRotationX.constant, m_Main.startRotationY.constant, m_Main.startRotationZ.constant);
            set
            {
                m_Main.startRotationX = value.x;
                m_Main.startRotationY = value.y;
                m_Main.startRotationZ = value.z;
            }
        }

        [Obsolete("startLifetime property is deprecated. Use main.startLifetime or main.startLifetimeMultiplier instead.", false)]
        public float startLifetime { get => m_Main.startLifetime.constant; set => m_Main.startLifetime = value; }

        [Obsolete("gravityModifier property is deprecated. Use main.gravityModifier or main.gravityModifierMultiplier instead.", false)]
        public float gravityModifier { get => m_Main.gravityModifier.constant; set => m_Main.gravityModifier = value; }

        [Obsolete("maxParticles property is deprecated. Use main.maxParticles instead.", false)]
        public int maxParticles { get => m_Main.maxParticles; set => m_Main.maxParticles = Math.Max(0, value); }

        [Obsolete("simulationSpace property is deprecated. Use main.simulationSpace instead.", false)]
        public ParticleSystemSimulationSpace simulationSpace { get => m_Main.simulationSpace; set => m_Main.simulationSpace = value; }

        [Obsolete("scalingMode property is deprecated. Use main.scalingMode instead.", false)]
        public ParticleSystemScalingMode scalingMode { get => m_Main.scalingMode; set => m_Main.scalingMode = value; }

        [Obsolete("automaticCullingEnabled property is deprecated. Use proceduralSimulationSupported instead (UnityUpgradable) -> proceduralSimulationSupported", false)]
        public bool automaticCullingEnabled => proceduralSimulationSupported;

        internal void SetSimulationSpace(ParticleSystemSimulationSpace space) => m_Main.simulationSpace = space;

        // ------------------------------------------------------------------ playback

        public void Play() => Play(true);

        public void Play([Internal.DefaultValue("true")] bool withChildren)
        {
            foreach (var ps in Systems(withChildren, includeSubEmitters: false)) ps.PlaySelf();
        }

        public void Pause() => Pause(true);

        public void Pause([Internal.DefaultValue("true")] bool withChildren)
        {
            foreach (var ps in Systems(withChildren, includeSubEmitters: true)) ps.m_IsPaused = true;
        }

        public void Stop() => Stop(true, ParticleSystemStopBehavior.StopEmitting);

        public void Stop([Internal.DefaultValue("true")] bool withChildren) => Stop(withChildren, ParticleSystemStopBehavior.StopEmitting);

        public void Stop([Internal.DefaultValue("true")] bool withChildren, [Internal.DefaultValue("ParticleSystemStopBehavior.StopEmitting")] ParticleSystemStopBehavior stopBehavior)
        {
            foreach (var ps in Systems(withChildren, includeSubEmitters: true)) ps.StopSelf(stopBehavior);
        }

        public void Clear() => Clear(true);

        public void Clear([Internal.DefaultValue("true")] bool withChildren)
        {
            foreach (var ps in Systems(withChildren, includeSubEmitters: true)) ps.m_Count = 0;
        }

        public bool IsAlive() => IsAlive(true);

        public bool IsAlive([Internal.DefaultValue("true")] bool withChildren)
        {
            // A paused system counts as alive even with nothing left (measured after Simulate).
            foreach (var ps in Systems(withChildren, includeSubEmitters: true))
                if (ps.m_Count > 0 || (ps.m_IsPlaying && !ps.m_StopEmitting) || ps.m_IsPaused) return true;
            return false;
        }

        public void Simulate(float t) => Simulate(t, true, true, true);

        public void Simulate(float t, [Internal.DefaultValue("true")] bool withChildren) => Simulate(t, withChildren, true, true);

        public void Simulate(float t, [Internal.DefaultValue("true")] bool withChildren, [Internal.DefaultValue("true")] bool restart) => Simulate(t, withChildren, restart, true);

        /// <summary>Fast-forwards by <paramref name="t"/> seconds and leaves the system paused. With a
        /// fixed time step the time advances in whole Time.fixedDeltaTime steps and the rest carries
        /// over to the next call (measured); otherwise in the frame's steps (see Advance).</summary>
        public void Simulate(float t, [Internal.DefaultValue("true")] bool withChildren, [Internal.DefaultValue("true")] bool restart, [Internal.DefaultValue("true")] bool fixedTimeStep)
        {
            foreach (var ps in Systems(withChildren, includeSubEmitters: true))
            {
                if (restart)
                {
                    ps.m_Count = 0;
                    ps.m_IsPlaying = false;
                    ps.m_StopEmitting = false;
                    ps.m_FixedRemainder = 0f;
                }
                // A system Simulate left paused continues where it was, even when it has finished.
                bool resume = !restart && ps.m_IsPaused;
                bool started = false;
                if (!ps.m_IsPlaying && !resume)
                {
                    ps.Restart(prewarm: restart);
                    started = true;
                }
                ps.m_IsPaused = false;
                ps.m_CollisionEvents.Clear();
                ps.TrackEmitter(t);
                // A sub-emitter starts with its parent but its time only runs from the next call
                // (measured).
                if (started && ps.m_SubEmitterParent != null)
                {
                    ps.m_IsPaused = true;
                    continue;
                }
                float total = Mathf.Max(0f, t) * ps.m_Main.simulationSpeed;
                if (fixedTimeStep)
                {
                    float step = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                    ps.m_FixedRemainder += total;
                    int count = (int)Math.Min(ps.m_FixedRemainder / step, 100000f);
                    float distance = ps.m_FrameDistance / Math.Max(count, 1);
                    for (int guard = 0; ps.m_FixedRemainder >= step && guard < 100000; guard++)
                    {
                        ps.Advance(step, distance, false, (float)guard / Math.Max(count, 1), (float)(guard + 1) / Math.Max(count, 1));
                        ps.m_FixedRemainder -= step;
                    }
                }
                else if (total > 0f)
                    ps.Advance(total, ps.m_FrameDistance, false);
                ps.m_IsPaused = true;
            }
        }

        /// <summary>Emits now; a system not playing starts playing (measured).</summary>
        public void Emit(int count) => EmitNow(count, default, false);

        public void Emit(EmitParams emitParams, int count) => EmitNow(count, emitParams, true);

        /// <summary>Emitting into a system that is not playing makes it play its particles without
        /// emitting more (measured: isPlaying true, isEmitting false).</summary>
        void EmitNow(int count, EmitParams emitParams, bool hasOverrides)
        {
            if (!m_IsPlaying || m_IsPaused)
            {
                m_IsPlaying = true;
                m_StopEmitting = true;
            }
            m_IsPaused = false;
            EmitParticles(count, 0f, m_Time, emitParams, hasOverrides);
        }

        [Obsolete("Emit with specific parameters is deprecated. Pass a ParticleSystem.EmitParams parameter instead, which allows you to override some/all of the emission properties", false)]
        public void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color32 color)
        {
            var p = new EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = color,
            };
            EmitNow(1, p, true);
        }

        [Obsolete("Emit with a single particle structure is deprecated. Pass a ParticleSystem.EmitParams parameter instead, which allows you to override some/all of the emission properties", false)]
        public void Emit(Particle particle)
        {
            int index = Allocate();
            if (index >= 0) m_Particles[index] = particle;
        }

        public void TriggerSubEmitter(int subEmitterIndex)
        {
            var sub = CheckSubEmitter(subEmitterIndex);
            for (int i = 0; i < m_Count; i++) TriggerSubEmitterAt(sub, ref m_Particles[i]);
        }

        public void TriggerSubEmitter(int subEmitterIndex, ref Particle particle) => TriggerSubEmitterAt(CheckSubEmitter(subEmitterIndex), ref particle);

        public void TriggerSubEmitter(int subEmitterIndex, List<Particle> particles)
        {
            var sub = CheckSubEmitter(subEmitterIndex);
            if (particles == null) return;
            for (int i = 0; i < particles.Count; i++)
            {
                var p = particles[i];
                TriggerSubEmitterAt(sub, ref p);
            }
        }

        SubEmitter CheckSubEmitter(int index)
        {
            if (index < 0 || index >= m_SubEmitters.emitters.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Sub-emitter index out of range.");
            return m_SubEmitters.emitters[index];
        }

        public static void ResetPreMappedBufferMemory() { }

        public static void SetMaximumPreMappedBufferCounts(int vertexBuffersCount, int indexBuffersCount) { }

        public void AllocateAxisOfRotationAttribute() { }

        public void AllocateMeshIndexAttribute() { }

        public void AllocateCustomDataAttribute(ParticleSystemCustomData stream) => EnsureCustom((int)stream);

        // ------------------------------------------------------------------ particle access

        public void SetParticles(Particle[] particles, int size, int offset)
        {
            if (particles == null) throw new ArgumentNullException(nameof(particles));
            if (size < 0 || size > particles.Length) size = particles.Length;
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            int count = offset == 0 ? size : Math.Max(m_Count, offset + size);
            count = Math.Min(count, Math.Max(m_Main.maxParticles, offset + size));
            Reserve(count);
            for (int i = 0; i < size && offset + i < count; i++) m_Particles[offset + i] = particles[i];
            for (int s = 0; s < 2; s++)
                if (m_Custom[s] != null && m_Custom[s].Length < count) Array.Resize(ref m_Custom[s], m_Particles.Length);
            m_Count = count;
            // Particles set with a negative remaining lifetime die at once (the last takes their place).
            for (int i = 0; i < m_Count;)
            {
                if (m_Particles[i].m_Lifetime < 0f) RemoveAt(i);
                else i++;
            }
        }

        public void SetParticles(Particle[] particles, int size) => SetParticles(particles, size, 0);

        public void SetParticles(Particle[] particles) => SetParticles(particles, -1, 0);

        public void SetParticles(NativeArray<Particle> particles, int size, int offset) => SetParticles(particles.ToArray(), size, offset);

        public void SetParticles(NativeArray<Particle> particles, int size) => SetParticles(particles.ToArray(), size, 0);

        public void SetParticles(NativeArray<Particle> particles) => SetParticles(particles.ToArray(), -1, 0);

        public int GetParticles(Particle[] particles, int size, int offset)
        {
            if (particles == null) throw new ArgumentNullException(nameof(particles));
            if (size < 0) size = particles.Length;
            offset = Math.Max(0, offset);
            int n = Math.Min(Math.Min(size, particles.Length), m_Count - offset);
            if (n <= 0) return 0;
            Array.Copy(m_Particles, offset, particles, 0, n);
            return n;
        }

        public int GetParticles(Particle[] particles, int size) => GetParticles(particles, size, 0);

        public int GetParticles(Particle[] particles) => GetParticles(particles, -1, 0);

        public int GetParticles(NativeArray<Particle> particles, int size, int offset)
        {
            var copy = new Particle[particles.Length];
            int n = GetParticles(copy, size, offset);
            NativeArray<Particle>.Copy(copy, 0, particles, 0, n);
            return n;
        }

        public int GetParticles(NativeArray<Particle> particles, int size) => GetParticles(particles, size, 0);

        public int GetParticles(NativeArray<Particle> particles) => GetParticles(particles, -1, 0);

        public void SetCustomParticleData(List<Vector4> customData, ParticleSystemCustomData streamIndex)
        {
            if (customData == null) throw new ArgumentNullException(nameof(customData));
            var data = EnsureCustom((int)streamIndex);
            int n = Math.Min(customData.Count, m_Count);
            for (int i = 0; i < n; i++) data[i] = customData[i];
        }

        public int GetCustomParticleData(List<Vector4> customData, ParticleSystemCustomData streamIndex)
        {
            if (customData == null) throw new ArgumentNullException(nameof(customData));
            customData.Clear();
            int stream = (int)streamIndex;
            var data = m_Custom[stream];
            var mode = m_CustomData.enabled ? m_CustomData.modes[stream] : ParticleSystemCustomDataMode.Disabled;
            for (int i = 0; i < m_Count; i++)
            {
                var value = data != null && i < data.Length ? data[i] : Vector4.zero;
                if (mode != ParticleSystemCustomDataMode.Disabled) value = CustomDataOf(ref m_Particles[i], stream, mode, value);
                customData.Add(value);
            }
            return m_Count;
        }

        /// <summary>A particle's custom data as the custom data module gives it at its current age:
        /// the vector's components (those past the component count stay as they were), or the color.</summary>
        Vector4 CustomDataOf(ref Particle p, int stream, ParticleSystemCustomDataMode mode, Vector4 stored)
        {
            float age = p.Age01;
            uint seed = p.m_RandomSeed;
            if (mode == ParticleSystemCustomDataMode.Color)
            {
                Color c = m_CustomData.colors[stream].Evaluate(age, Unbound.Runtime.ParticleRandom.Value(seed, Unbound.Runtime.ParticleRandom.Custom * 100u + (uint)stream * 8u));
                return new Vector4(c.r, c.g, c.b, c.a);
            }
            // The vector is written by the last update, at the age before it (measured).
            if (p.m_StartLifetime > 0f) age = Mathf.Max(0f, age - m_LastStepDt / p.m_StartLifetime);
            int count = Mathf.Clamp(m_CustomData.componentCounts[stream], 0, 4);
            for (int k = 0; k < count; k++)
                stored[k] = m_CustomData.vectors[stream, k].Evaluate(age, Unbound.Runtime.ParticleRandom.Value(seed, Unbound.Runtime.ParticleRandom.Custom * 100u + (uint)(stream * 8 + 1 + k)));
            return stored;
        }

        public PlaybackState GetPlaybackState() => new PlaybackState
        {
            m_PlaybackTime = m_Time,
            m_StartDelay = m_DelayRemaining,
            m_AccumulatedDt = m_RateAccumulator,
            m_TotalTime = m_TotalTime,
            m_Loop = m_Loop,
            m_Playing = m_IsPlaying,
            m_Paused = m_IsPaused,
            m_StopEmitting = m_StopEmitting,
            m_BurstCycles = (int[])m_BurstCycles.Clone(),
            m_RandomState = m_RandomState,
        };

        public void SetPlaybackState(PlaybackState playbackState)
        {
            m_Time = playbackState.m_PlaybackTime;
            m_DelayRemaining = playbackState.m_StartDelay;
            m_RateAccumulator = playbackState.m_AccumulatedDt;
            m_TotalTime = playbackState.m_TotalTime;
            m_Loop = playbackState.m_Loop;
            m_IsPlaying = playbackState.m_Playing;
            m_IsPaused = playbackState.m_Paused;
            m_StopEmitting = playbackState.m_StopEmitting;
            m_BurstCycles = (int[])playbackState.m_BurstCycles?.Clone() ?? Array.Empty<int>();
            m_RandomState = playbackState.m_RandomState;
        }

        public Trails GetTrails()
        {
            var t = new Trails();
            GetTrails(ref t);
            return t;
        }

        public int GetTrails(ref Trails trailData)
        {
            trailData.positions ??= new List<Vector4>();
            trailData.frontPositions ??= new List<int>();
            trailData.backPositions ??= new List<int>();
            trailData.positionCounts ??= new List<int>();
            trailData.textureOffsets ??= new List<float>();
            trailData.positions.Clear();
            trailData.frontPositions.Clear();
            trailData.backPositions.Clear();
            trailData.positionCounts.Clear();
            trailData.textureOffsets.Clear();
            trailData.maxTrailCount = 0;
            trailData.maxPositionsPerTrailCount = 0;
            return 0;
        }

        public void SetTrails(Trails trailData) { }

        // ------------------------------------------------------------------ internals

        /// <summary>This system, then (with children) the systems below it, depth first.</summary>
        List<ParticleSystem> Systems(bool withChildren, bool includeSubEmitters)
        {
            var list = new List<ParticleSystem> { this };
            if (withChildren && m_GameObject != null) Collect(m_GameObject.m_Transform, list, includeSubEmitters);
            return list;
        }

        static void Collect(Transform t, List<ParticleSystem> into, bool includeSubEmitters)
        {
            foreach (var child in t.m_Children)
            {
                var go = child.m_GameObject;
                foreach (var c in go.m_Components)
                {
                    if (c is ParticleSystem ps && !ps.m_Destroyed && (includeSubEmitters || ps.m_SubEmitterParent == null))
                        into.Add(ps);
                }
                Collect(child, into, includeSubEmitters);
            }
        }

        void PlaySelf()
        {
            if (m_IsPaused)
            {
                m_IsPaused = false;
                if (m_IsPlaying) return;
            }
            if (m_IsPlaying && !m_StopEmitting) return;
            Restart(prewarm: true);
        }

        /// <summary>Starts a new playback from time 0, keeping the particles that are alive.</summary>
        internal void Restart(bool prewarm)
        {
            uint seed = m_Main.autoRandomSeed ? NextAutoSeed() : m_Main.randomSeed;
            m_RandomState = ((ulong)seed << 32) ^ 0x9e3779b97f4a7c15UL ^ seed;
            if (m_RandomState == 0) m_RandomState = 0x9e3779b97f4a7c15UL;
            m_Time = 0f;
            m_TotalTime = 0f;
            m_Loop = 0;
            m_RateAccumulator = 0f;
            m_EmitPhase = 0f;
            m_LoopStart = false;
            m_DistanceAccumulator = 0f;
            Array.Clear(m_BurstCycles, 0, m_BurstCycles.Length);
            m_IsPlaying = true;
            m_StopEmitting = false;
            bool doPrewarm = prewarm && m_Main.prewarm && m_Main.loop;
            m_DelayRemaining = doPrewarm ? 0f : Mathf.Max(0f, m_Main.startDelay.Evaluate(0f, NextFloat()));
            if (doPrewarm) Prewarm();
        }

        static uint NextAutoSeed()
        {
            s_AutoSeed = ParticleRandom.Hash(s_AutoSeed + 0x6d2b79f5u + (uint)Environment.TickCount);
            return s_AutoSeed;
        }

        /// <summary>Simulates one full loop so the system starts in its steady state.</summary>
        /// <summary>Prewarm simulates one start lifetime (the longest), so every particle a loop
        /// keeps alive is there, then playback starts from time 0 with the emission keeping its
        /// phase (measured: a lifetime of 0.737 or 2.537 s in a 1 or 2 s loop).</summary>
        void Prewarm()
        {
            Advance(Mathf.Max(MaxValue(m_Main.startLifetime), 0.0001f), 0f, false);
            KeepEmissionPhase();
            m_Time = 0f;
            m_Loop = 0;
            m_TotalTime = 0f;
            Array.Clear(m_BurstCycles, 0, m_BurstCycles.Length);
        }

        static float MaxValue(MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.TwoConstants: return Mathf.Max(curve.constantMin, curve.constantMax);
                case ParticleSystemCurveMode.Curve: return curve.curveMultiplier * MaxKey(curve.curveMax);
                case ParticleSystemCurveMode.TwoCurves: return curve.curveMultiplier * Mathf.Max(MaxKey(curve.curveMin), MaxKey(curve.curveMax));
                default: return curve.constant;
            }
        }

        static float MaxKey(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return 0f;
            float max = float.NegativeInfinity;
            for (int i = 0; i <= 32; i++) max = Mathf.Max(max, curve.Evaluate(i / 32f));
            return max;
        }

        void StopSelf(ParticleSystemStopBehavior behavior)
        {
            if (behavior == ParticleSystemStopBehavior.StopEmittingAndClear)
            {
                bool wasPlaying = m_IsPlaying;
                m_Count = 0;
                m_IsPlaying = false;
                m_IsPaused = false;
                m_StopEmitting = false;
                m_DelayRemaining = 0f;
                if (wasPlaying) OnStopped();
                return;
            }
            if (m_IsPlaying) m_StopEmitting = true;
        }

        /// <summary>The system stopped playing (emission over and no particle left): the stop action.</summary>
        void OnStopped()
        {
            if (ParticleSystemManager.Defer(this)) return;
            RunStopAction();
        }

        internal void RunStopAction()
        {
            if (m_GameObject == null || m_Destroyed) return;
            switch (m_Main.stopAction)
            {
                case ParticleSystemStopAction.Disable:
                    m_GameObject.SetActive(false);
                    break;
                case ParticleSystemStopAction.Destroy:
                    Destroy(m_GameObject);
                    break;
                case ParticleSystemStopAction.Callback:
                    Messaging.SendFast(m_GameObject, "OnParticleSystemStopped");
                    break;
            }
        }

        /// <summary>Marks the systems this one drives as its sub-emitters.</summary>
        internal void SubEmittersChanged()
        {
            foreach (var e in m_SubEmitters.emitters)
                if (e.system != null && e.system != this)
                    e.system.m_SubEmitterParent = m_SubEmitters.enabled ? this : (e.system.m_SubEmitterParent == this ? null : e.system.m_SubEmitterParent);
        }

        internal override void OnEngineEnable()
        {
            if (m_GameObject == null || m_GameObject.m_IsPrefabAsset || m_Registered) return;
            m_Registered = true;
            ParticleSystemManager.Add(this);
            if (m_GameObject.GetComponent<ParticleSystemRenderer>() == null)
                m_GameObject.AddComponent<ParticleSystemRenderer>();
            SubEmittersChanged();
            m_HasLastPosition = false;
            if (m_Main.playOnAwake && m_SubEmitterParent == null) PlaySelf();
        }

        internal override void OnEngineDisable()
        {
            if (!m_Registered) return;
            m_Registered = false;
            ParticleSystemManager.Remove(this);
            // Deactivating a system kills its particles and stops it (without its stop action).
            m_Count = 0;
            m_IsPlaying = false;
            m_IsPaused = false;
            m_StopEmitting = false;
        }

        internal override void OnEngineDestroy()
        {
            OnEngineDisable();
            foreach (var e in m_SubEmitters.emitters)
                if (e.system != null && e.system.m_SubEmitterParent == this) e.system.m_SubEmitterParent = null;
        }

        // ------------------------------------------------------------------ storage

        void Reserve(int count)
        {
            if (m_Particles.Length >= count) return;
            int size = Math.Max(count, Math.Max(16, m_Particles.Length * 2));
            Array.Resize(ref m_Particles, size);
            for (int s = 0; s < 2; s++)
                if (m_Custom[s] != null) Array.Resize(ref m_Custom[s], size);
        }

        Vector4[] EnsureCustom(int stream)
        {
            if (m_Custom[stream] == null || m_Custom[stream].Length < m_Particles.Length)
                Array.Resize(ref m_Custom[stream], Math.Max(m_Particles.Length, 16));
            return m_Custom[stream];
        }

        /// <summary>A slot for a new particle: appended, or (ring buffer) the oldest one replaced; -1 when full.</summary>
        int Allocate()
        {
            if (m_Count >= m_Main.maxParticles)
            {
                if (m_Main.ringBufferMode == ParticleSystemRingBufferMode.Disabled || m_Count == 0) return -1;
                int oldest = 0;
                float oldestAge = float.NegativeInfinity;
                for (int i = 0; i < m_Count; i++)
                {
                    float age = m_Particles[i].m_StartLifetime - m_Particles[i].m_Lifetime;
                    if (age > oldestAge) { oldestAge = age; oldest = i; }
                }
                ClearCustom(oldest);
                return oldest;
            }
            Reserve(m_Count + 1);
            ClearCustom(m_Count);
            return m_Count++;
        }

        void ClearCustom(int index)
        {
            for (int s = 0; s < 2; s++)
                if (m_Custom[s] != null && index < m_Custom[s].Length) m_Custom[s][index] = Vector4.zero;
        }

        void RemoveAt(int index)
        {
            int last = --m_Count;
            if (index == last) return;
            m_Particles[index] = m_Particles[last];
            for (int s = 0; s < 2; s++)
                if (m_Custom[s] != null) m_Custom[s][index] = m_Custom[s][last];
        }

        internal uint NextUInt()
        {
            if (m_RandomState == 0) m_RandomState = 0x9e3779b97f4a7c15UL ^ NextAutoSeed();
            // xorshift64*
            ulong x = m_RandomState;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            m_RandomState = x;
            return (uint)((x * 0x2545F4914F6CDD1DUL) >> 32);
        }

        internal float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>Steps every active particle system once per frame, where Unity does.</summary>
    internal static class ParticleSystemManager
    {
        static readonly List<ParticleSystem> s_Systems = new List<ParticleSystem>();
        static ParticleSystem[] s_Snapshot = new ParticleSystem[16];

        static ParticleSystemManager()
        {
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PreLateUpdate.ParticleSystemBeginUpdateAll), UpdateAll);
        }

        internal static void EnsureRegistered() { }

        internal static IReadOnlyList<ParticleSystem> Systems => s_Systems;

        internal static void Add(ParticleSystem ps) => s_Systems.Add(ps);

        internal static void Remove(ParticleSystem ps) => s_Systems.Remove(ps);

        internal static void UpdateAll()
        {
            int n = s_Systems.Count;
            if (s_Snapshot.Length < n) s_Snapshot = new ParticleSystem[Math.Max(n, s_Snapshot.Length * 2)];
            s_Systems.CopyTo(s_Snapshot);
            // Every system updates, then the collision and trigger messages go out, then the stop
            // actions run (measured: OnParticleTrigger before OnParticleSystemStopped of a system
            // updated earlier).
            s_Deferring = true;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var ps = s_Snapshot[i];
                    if (ps == null || !ps.m_Registered) continue;
                    try { ps.FrameUpdate(); }
                    catch (Exception e) { Debug.LogException(e, ps); }
                }
            }
            finally { s_Deferring = false; }
            for (int i = 0; i < n; i++)
            {
                var ps = s_Snapshot[i];
                if (ps == null || !ps.m_Registered) continue;
                try
                {
                    ps.SendCollisionMessages();
                    ps.SendTriggerMessage();
                }
                catch (Exception e) { Debug.LogException(e, ps); }
            }
            Array.Clear(s_Snapshot, 0, n);
            var stopped = s_Stopped.ToArray();
            s_Stopped.Clear();
            foreach (var ps in stopped)
            {
                try { ps.RunStopAction(); }
                catch (Exception e) { Debug.LogException(e, ps); }
            }
        }

        static bool s_Deferring;
        static readonly List<ParticleSystem> s_Stopped = new List<ParticleSystem>();

        /// <summary>Whether a stop action waits for the end of the frame's particle update.</summary>
        internal static bool Defer(ParticleSystem ps)
        {
            if (!s_Deferring) return false;
            s_Stopped.Add(ps);
            return true;
        }
    }
}
