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
        bool m_PlaybackRestored;
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
        // A zero rate, an empty system and the start delay still count as emitting. The
        // module switch gates this observation without changing the playback state.
        public bool isEmitting => m_IsPlaying && !m_IsPaused && !m_StopEmitting && m_Emission.enabled;
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
                m_InitialRandomReady = false;
                m_ShapeRandomReady = false;
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

        public bool proceduralSimulationSupported
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("ParticleSystem.proceduralSimulationSupported", "is always false: procedural simulation is not decided per system");
                return false;
            }
        }

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
        public int safeCollisionEventSize => this.GetSafeCollisionEventSize();

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

        /// <summary>Removes the particles of this system, of the systems it links as sub-emitters
        /// (wherever they are in the hierarchy, with or without children), and with children of
        /// the child systems too (6000.0 Scripting API, ParticleSystem.Clear remarks).</summary>
        public void Clear([Internal.DefaultValue("true")] bool withChildren)
        {
            foreach (var ps in ClearedSystems(withChildren))
            {
                ps.m_Count = 0;
                ps.ClearTrails();
                if (ps.m_StopEmitting) ps.m_IsPlaying = false;
            }
        }

        /// <summary>The systems Clear empties: <see cref="Systems"/>, then every system linked by
        /// an enabled sub-emitters module of one already in the list, each once. The docs do not
        /// say whether nested links or a disabled module's list count; this follows nested links
        /// (the set Simulate with restart empties) and treats a disabled module as unlinked, as
        /// emission does. The visited set only guarantees termination for shared and cyclic links;
        /// the order is not observable (clearing raises no callback).</summary>
        List<ParticleSystem> ClearedSystems(bool withChildren)
        {
            var list = Systems(withChildren, includeSubEmitters: true);
            var seen = new HashSet<ParticleSystem>(list);
            for (int i = 0; i < list.Count; i++)
            {
                var data = list[i].m_SubEmitters;
                if (!data.enabled) continue;
                foreach (var e in data.emitters)
                {
                    var sub = e.system;
                    if (sub != null && !sub.m_Destroyed && sub.m_GameObject != null && seen.Add(sub)) list.Add(sub);
                }
            }
            return list;
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
            var systems = SimulatedSystems(withChildren, out int hierarchyCount);
            for (int index = 0; index < systems.Count; index++)
            {
                var ps = systems[index];
                bool referenced = index >= hierarchyCount;
                if (restart)
                {
                    ps.m_Count = 0;
                    ps.ClearTrails();
                    ps.m_IsPlaying = false;
                    ps.m_StopEmitting = false;
                    ps.m_FixedRemainder = 0f;
                }
                // A system Simulate left paused continues where it was, even when it has finished.
                bool resume = !restart && ps.m_IsPaused;
                // Stop on an empty system ends public playback immediately. Simulate can
                // still advance its clock, but must not restart its delay or emission.
                bool stopped = !restart && !ps.m_IsPlaying && ps.m_StopEmitting;
                bool started = false;
                if (!ps.m_IsPlaying && !resume && !stopped)
                {
                    ps.Restart(prewarm: restart);
                    started = true;
                }
                ps.m_IsPaused = false;
                ps.m_CollisionEvents.Clear();
                ps.TrackEmitter(t);
                // A sub-emitter that a restarting call starts with its parent keeps its time
                // until the next call (measured with children). One that only its parent's
                // reference brings in runs from the call that starts it (measured without children:
                // a stopped Death sub-emitter's time advances by each call and it ends paused).
                if (started && (referenced ? restart : ps.m_SubEmitterParent != null))
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
                        if (stopped) ps.AdvanceStoppedClock(step, distance);
                        else ps.Advance(step, distance, false, (float)guard / Math.Max(count, 1), (float)(guard + 1) / Math.Max(count, 1));
                        ps.m_FixedRemainder -= step;
                    }
                }
                // Native's non-fixed Simulate ignores calls below ten microseconds;
                // this call boundary is distinct from Advance's subdivision remainder.
                else if (total >= 1e-5f)
                {
                    if (stopped) ps.AdvanceStoppedClock(total, ps.m_FrameDistance);
                    else ps.Advance(total, ps.m_FrameDistance, false);
                }
                ps.m_IsPaused = true;
            }
        }

        void AdvanceStoppedClock(float dt, float distance)
        {
            // Native keeps a stopped system inside an unfinished start delay at time zero.
            if (m_DelayRemaining <= 0f) AdvanceEmission(dt, distance);
        }

        /// <summary>Emits now; a system not playing starts playing (measured).</summary>
        public void Emit(int count) => EmitNow(count, default, false);

        public void Emit(EmitParams emitParams, int count) => EmitNow(count, emitParams, true);

        /// <summary>Emitting into a system that is not playing makes it play its particles without
        /// emitting more (measured: isPlaying true, isEmitting false).</summary>
        void EmitNow(int count, EmitParams emitParams, bool hasOverrides)
        {
            if (count == 0) return;
            if (!m_IsPlaying || m_IsPaused)
            {
                m_IsPlaying = true;
                m_StopEmitting = true;
            }
            m_IsPaused = false;
            EmitParticles(count, 0f, m_Time, emitParams, hasOverrides, simulate: false);
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
            if (index >= 0)
            {
                MaterializeAxisOfRotation();
                m_Particles[index] = particle;
            }
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

        [Unbound.EditorFaithful("no-op is equivalent: a GPU buffer memory hint; nothing a game observes")]
        public static void ResetPreMappedBufferMemory() { }

        [Unbound.EditorFaithful("no-op is equivalent: a GPU buffer memory hint; nothing a game observes")]
        public static void SetMaximumPreMappedBufferCounts(int vertexBuffersCount, int indexBuffersCount) { }

        public void AllocateAxisOfRotationAttribute() => MaterializeAxisOfRotation();

        [Unbound.EditorFaithful("no-op is equivalent: every particle always stores its mesh index")]
        public void AllocateMeshIndexAttribute() { }

        public void AllocateCustomDataAttribute(ParticleSystemCustomData stream) => EnsureCustom((int)stream);

        // ------------------------------------------------------------------ particle access

        public void SetParticles(Particle[] particles, int size, int offset)
        {
            if (particles == null) throw new ArgumentNullException(nameof(particles));
            if (size < 0 || size > particles.Length) size = particles.Length;
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            int count = offset + size;
            Reserve(count);
            if (count > 0) MaterializeAxisOfRotation();
            for (int i = 0; i < size && offset + i < count; i++)
            {
                // Native optional noise arrays are separate from the public particle struct.
                var noiseSum = m_Particles[offset + i].m_NoiseSum;
                var noiseImpulse = m_Particles[offset + i].m_NoiseImpulse;
                m_Particles[offset + i] = particles[i];
                m_Particles[offset + i].m_NoiseSum = noiseSum;
                m_Particles[offset + i].m_NoiseImpulse = noiseImpulse;
            }
            for (int s = 0; s < 2; s++)
                if (m_Custom[s] != null && m_Custom[s].Length < count) Array.Resize(ref m_Custom[s], m_Particles.Length);
            m_Count = count;
            // Native consumes the input lifetime before encoding normalized age. Nonpositive
            // inputs die immediately; tiny positive inputs can export as zero and still survive.
            // Native's observed compaction order is four-slot groups aligned to the buffer:
            // collect dead slots, remove high to low, then revisit the group after swap-back.
            // The retained prefix is untouched, even when its exported lifetime is zero.
            for (int block = offset & ~3; block < m_Count;)
            {
                int dead = 0;
                for (int i = Math.Max(block, offset); i < Math.Min(block + 4, m_Count); i++)
                    if (m_Particles[i].m_Lifetime <= 0f) dead |= 1 << (i - block);
                if (dead == 0) { block += 4; continue; }
                for (int lane = 3; lane >= 0; lane--)
                    if ((dead & (1 << lane)) != 0) RemoveAt(block + lane);
            }
            for (int i = offset; i < m_Count; i++)
            {
                m_Particles[i].InitializeAge();
                // SetParticles clamps inputs that exceed their start lifetime to birth.
                if (m_Particles[i].m_AgePercent < 0f)
                {
                    m_Particles[i].m_AgePercent = 0f;
                    m_Particles[i].ExportLifetime();
                }
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
            MaterializeAxisOfRotation();
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
        internal Vector4 VertexCustomData(ref Particle p, int index, int stream)
        {
            var data = m_Custom[stream];
            var stored = data != null && index < data.Length ? data[index] : Vector4.zero;
            var mode = m_CustomData.enabled ? m_CustomData.modes[stream] : ParticleSystemCustomDataMode.Disabled;
            return mode == ParticleSystemCustomDataMode.Disabled ? stored : CustomDataOf(ref p, stream, mode, stored);
        }

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
            m_BurstCycles = (int[])m_BurstCycles.Clone(),
            m_RandomState = m_RandomState,
            m_InitialRandom0 = m_InitialRandom[0],
            m_InitialRandom1 = m_InitialRandom[1],
            m_InitialRandom2 = m_InitialRandom[2],
            m_InitialRandom3 = m_InitialRandom[3],
            m_InitialRandomReady = m_InitialRandomReady,
            m_ShapeRandom0 = m_ShapeRandom[0],
            m_ShapeRandom1 = m_ShapeRandom[1],
            m_ShapeRandom2 = m_ShapeRandom[2],
            m_ShapeRandom3 = m_ShapeRandom[3],
            m_ShapeRandomReady = m_ShapeRandomReady,
        };

        public void SetPlaybackState(PlaybackState playbackState)
        {
            m_Time = playbackState.m_PlaybackTime;
            m_DelayRemaining = playbackState.m_StartDelay;
            m_RateAccumulator = playbackState.m_AccumulatedDt;
            m_TotalTime = playbackState.m_TotalTime;
            m_Loop = playbackState.m_Loop;
            // Native restores simulation data, while the current Play/Pause/Stop state
            // and emission module switch remain in effect.
            m_PlaybackRestored = true;
            m_BurstCycles = (int[])playbackState.m_BurstCycles?.Clone() ?? Array.Empty<int>();
            m_RandomState = playbackState.m_RandomState;
            m_InitialRandom[0] = playbackState.m_InitialRandom0;
            m_InitialRandom[1] = playbackState.m_InitialRandom1;
            m_InitialRandom[2] = playbackState.m_InitialRandom2;
            m_InitialRandom[3] = playbackState.m_InitialRandom3;
            m_InitialRandomReady = playbackState.m_InitialRandomReady;
            m_ShapeRandom[0] = playbackState.m_ShapeRandom0;
            m_ShapeRandom[1] = playbackState.m_ShapeRandom1;
            m_ShapeRandom[2] = playbackState.m_ShapeRandom2;
            m_ShapeRandom[3] = playbackState.m_ShapeRandom3;
            m_ShapeRandomReady = playbackState.m_ShapeRandomReady;
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

        public void SetTrails(Trails trailData) => Unbound.Runtime.Stubs.Ignored("ParticleSystem.SetTrails", "is not honoured yet: trail data is not written back");

        // ------------------------------------------------------------------ internals

        /// <summary>The systems a Simulate call advances: <see cref="Systems"/> (the first
        /// <paramref name="hierarchyCount"/>), then the systems they use as sub-emitters, theirs in
        /// turn, each once. A parent simulates its sub-emitters even without children (measured:
        /// a referenced Death sub-emitter's time advances with each call and it ends paused, also
        /// when it emits nothing).</summary>
        List<ParticleSystem> SimulatedSystems(bool withChildren, out int hierarchyCount)
        {
            var list = Systems(withChildren, includeSubEmitters: true);
            hierarchyCount = list.Count;
            var seen = new HashSet<ParticleSystem>(list);
            for (int i = 0; i < list.Count; i++)
            {
                var data = list[i].m_SubEmitters;
                if (!data.enabled) continue;
                foreach (var e in data.emitters)
                {
                    var sub = e.system;
                    if (sub != null && !sub.m_Destroyed && sub.m_GameObject != null && seen.Add(sub)) list.Add(sub);
                }
            }
            return list;
        }

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
                if (m_IsPlaying || m_TotalTime > 0f)
                {
                    m_IsPlaying = true;
                    m_StopEmitting = false;
                    m_PlaybackRestored = false;
                    return;
                }
            }
            if (m_IsPlaying && !m_StopEmitting && !m_PlaybackRestored) return;
            Restart(prewarm: true);
        }

        /// <summary>Starts a new playback from time 0, keeping the particles that are alive.</summary>
        internal void Restart(bool prewarm)
        {
            uint seed = m_Main.autoRandomSeed ? NextAutoSeed() : m_Main.randomSeed;
            ResetInitialRandom(seed);
            m_RandomState = ((ulong)seed << 32) ^ 0x9e3779b97f4a7c15UL ^ seed;
            if (m_RandomState == 0) m_RandomState = 0x9e3779b97f4a7c15UL;
            m_NoiseSumEnabled = false;
            m_NoiseScroll = 0f;
            m_Time = 0f;
            m_TotalTime = 0f;
            m_CullingMissed = 0f;
            m_Loop = 0;
            m_RateAccumulator = 0f;
            m_EmitPhase = 0f;
            m_LoopStart = false;
            m_DistanceAccumulator = 0f;
            Array.Clear(m_BurstCycles, 0, m_BurstCycles.Length);
            m_IsPlaying = true;
            m_StopEmitting = false;
            m_PlaybackRestored = false;
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
                ClearTrails();
                m_IsPlaying = false;
                m_IsPaused = false;
                m_StopEmitting = false;
                m_DelayRemaining = 0f;
                if (wasPlaying) OnStopped();
                return;
            }
            if (m_IsPlaying) m_StopEmitting = true;
            if (m_Count == 0)
            {
                m_IsPlaying = false;
                m_IsPaused = false;
            }
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
                    Destruction.DestroyAtEndOfFrame(m_GameObject);
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
            DestroyModuleLights();
            if (!m_Registered) return;
            m_Registered = false;
            ParticleSystemManager.Remove(this);
            // Deactivating a system kills its particles and stops it (without its stop action).
            m_Count = 0;
            ClearTrails();
            m_IsPlaying = false;
            m_IsPaused = false;
            m_StopEmitting = false;
            m_CullingMissed = 0f;
        }

        internal override void OnEngineDestroy()
        {
            OnEngineDisable();
            foreach (var e in m_SubEmitters.emitters)
                if (e.system != null && e.system.m_SubEmitterParent == this) e.system.m_SubEmitterParent = null;
        }

        // ------------------------------------------------------------------ culling

        /// <summary>PauseAndCatchup: the simulation time (simulation speed applied) missed while
        /// culled, and the system's total time when that began.</summary>
        float m_CullingMissed;
        float m_CullingMissedFrom;

        /// <summary>Whether the system's update would advance it (it is not paused by a script
        /// and plays, has particles or has trails left).</summary>
        bool CullingSimulates => !m_IsPaused && (m_IsPlaying || m_Count > 0 || HasAliveTrails);

        /// <summary>
        /// The frame's update under MainModule.cullingMode. Documented (6000.0 Scripting API
        /// ParticleSystemCullingMode, Manual "Main module" Culling Mode): AlwaysSimulate simulates
        /// every frame "regardless of whether it is on screen or not"; Pause "stops simulating while
        /// offscreen"; PauseAndCatchup also stops and, "when re-entering the view, the simulation
        /// performs a large step to reach the point where it would have been had it not paused";
        /// Automatic: "Looping systems use Pause, and all other system use Always Simulate".
        /// <para>A culled frame is like a frame paused by a script: the emitter's position is still
        /// tracked (the movement while culled is not emitted over later), nothing ages, moves,
        /// emits or ends, and the play state is unchanged. It sends no collision or trigger
        /// message (it has no simulation to report; Unbound's choice, unmeasured).</para>
        /// <para>PauseAndCatchup: the missed time is simulated as one step of that length (not
        /// split by Time.maximumParticleDeltaTime: the Manual's "large step"; unmeasured), before
        /// the frame's own time, in the first update the system is visible and simulates; its
        /// collision and trigger messages go out with that frame's. The emitter's movement while
        /// culled is not replayed (the docs advise AlwaysSimulate for a moving system). Time missed
        /// while a script paused the system does not count, and a script call that changes the
        /// playback while culled (Simulate, a restart) drops the time missed before it (Unbound's
        /// choices, unmeasured). Known defect, open: the playback is told apart by its total time,
        /// so a restart or reactivation while culled from total time 0 keeps the earlier missed
        /// time; clearing it where a playback begins lies outside this update.</para>
        /// <para>Automatic with a looping system is not applied: the 6000.0 docs also say that a
        /// non-procedural system "must continue updating itself even when offscreen"
        /// (ParticleSystem.automaticCullingEnabled), and Unbound does not decide procedural
        /// simulation (<see cref="proceduralSimulationSupported"/>). Such a system keeps simulating
        /// offscreen, as with AlwaysSimulate, and the first such frame is reported once.</para>
        /// <para>Visibility is <see cref="Renderer.isVisible"/> of the system's own
        /// ParticleSystemRenderer, read when the update runs: what the previous frame's cameras and
        /// shadow casters left after this frame's EarlyUpdate (Unity also counts shadow casting and,
        /// in the Editor, the Scene view's cameras; Unbound has no Scene view). Before the first
        /// camera has culled it a renderer is not visible, so a Pause or PauseAndCatchup system
        /// waits for the first frame drawn (unmeasured). Unbound's choices (unmeasured): a
        /// sub-emitter decides by its own renderer and mode, not its parent's; a system without an
        /// enabled ParticleSystemRenderer keeps simulating (the docs do not say), reported once.
        /// A paused system's bounds stay as they were, which lets it become visible again.</para>
        /// <para>Drawn bounds (Unbound's choice, unmeasured: the bounds Unity culls particles by are
        /// not measured). The renderer's bounds are its live particles' centres ± size/2, which
        /// the engine draws particle renderers regardless of (FrameBuilder: they are not trusted),
        /// so a system is paused only where Unbound can bound what it draws, see
        /// <see cref="CullingBoundsCoverDraw"/>: then it pauses while it is not visible and no
        /// enabled camera drawing its layer has the particles' centres ± size·√3/2 (emitter scale
        /// applied) in its frustum, tested in the update (<see cref="ConservativeBoundsInView"/>),
        /// reported once. Any other system (stretched, mesh or none render mode, trails, a pivot,
        /// a minimum particle size) keeps simulating offscreen, as with AlwaysSimulate, reported
        /// once. A camera rendered only by script while disabled (Camera.Render) is seen only
        /// through isVisible.</para>
        /// </summary>
        internal void CullingFrameUpdate()
        {
            var mode = m_Main.cullingMode;
            if (!CulledThisFrame(mode))
            {
                float catchUp = mode == ParticleSystemCullingMode.PauseAndCatchup && m_TotalTime == m_CullingMissedFrom ? m_CullingMissed : 0f;
                // Missed time a frame cannot simulate yet (a zero time scale) waits for the next.
                if (FrameUpdate(catchUp) || catchUp == 0f) m_CullingMissed = 0f;
                return;
            }
            float raw = m_Main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            TrackEmitter(raw);
            m_CollisionEvents.Clear();
            foreach (var list in m_TriggerParticles) list.Clear();
            if (mode != ParticleSystemCullingMode.PauseAndCatchup)
            {
                m_CullingMissed = 0f;
                return;
            }
            float dt = raw * m_Main.simulationSpeed;
            if (!CullingSimulates || !(dt > 0f)) return;
            if (m_CullingMissed == 0f || m_TotalTime != m_CullingMissedFrom)
            {
                m_CullingMissed = 0f;
                m_CullingMissedFrom = m_TotalTime;
            }
            m_CullingMissed += dt;
        }

        /// <summary>Whether <paramref name="mode"/> pauses the system this frame (see
        /// <see cref="CullingFrameUpdate"/>).</summary>
        bool CulledThisFrame(ParticleSystemCullingMode mode)
        {
            if (mode == ParticleSystemCullingMode.AlwaysSimulate) return false;
            if (mode == ParticleSystemCullingMode.Automatic && !m_Main.loop) return false;
            var renderer = CullingRenderer;
            if (renderer == null || !renderer.enabled)
            {
                if (CullingSimulates)
                    Unbound.Runtime.Stubs.Ignored("ParticleSystem.main.cullingMode without a renderer",
                        "keeps simulating a system without an enabled ParticleSystemRenderer (its culling is not decided)");
                return false;
            }
            if (renderer.isVisible) return false;
            if (mode == ParticleSystemCullingMode.Automatic)
            {
                if (CullingSimulates)
                    Unbound.Runtime.Stubs.Ignored("ParticleSystem.main.cullingMode Automatic",
                        "keeps simulating an offscreen looping system: the docs also keep a non-procedural system updating offscreen, and Unbound does not decide procedural simulation");
                return false;
            }
            if (!CullingBoundsCoverDraw(renderer))
            {
                if (CullingSimulates)
                    Unbound.Runtime.Stubs.Ignored("Particle culling without covering bounds",
                        "keeps simulating an offscreen Pause or PauseAndCatchup system: its culling bounds do not cover what it draws (a stretched, mesh or none render mode, trails, a pivot or a minimum particle size)");
                return false;
            }
            if (ConservativeBoundsInView(renderer)) return false;
            if (CullingSimulates)
                Unbound.Runtime.Stubs.Ignored("Particle culling with approximate bounds",
                    "pauses an offscreen billboard system by its live particles' centres ± size·√3/2 (the bounds Unity culls by are not measured)");
            return true;
        }

        /// <summary>The system's ParticleSystemRenderer, looked up again only when it is gone or
        /// belongs to another object.</summary>
        ParticleSystemRenderer m_CullingRenderer;

        ParticleSystemRenderer CullingRenderer
        {
            get
            {
                if (m_GameObject == null) return null;
                if (!m_CullingRenderer || m_CullingRenderer.m_GameObject != m_GameObject)
                    m_CullingRenderer = m_GameObject.GetComponent<ParticleSystemRenderer>();
                return m_CullingRenderer;
            }
        }

        /// <summary>Whether every quad the renderer draws lies within √2/2 of the particle's
        /// size (emitter scale applied) of its centre, so that centres ± size·√3/2 cover it: a
        /// billboard render mode (a quad of the particle's size, or 1/√2 of it, about its centre,
        /// at any rotation and alignment), no trails, a zero pivot and no minimum particle size
        /// (which can enlarge a far particle). The maximum particle size only shrinks quads.
        /// Stretched quads, meshes, trails and pivots reach further (Unbound's choice: they are not
        /// culled, see <see cref="CullingFrameUpdate"/>).</summary>
        bool CullingBoundsCoverDraw(ParticleSystemRenderer renderer)
        {
            switch (renderer.renderMode)
            {
                case ParticleSystemRenderMode.Billboard:
                case ParticleSystemRenderMode.HorizontalBillboard:
                case ParticleSystemRenderMode.VerticalBillboard:
                    break;
                default:
                    return false;
            }
            var pivot = renderer.pivot;
            return !m_Trails.enabled && !HasAliveTrails
                && pivot.x == 0f && pivot.y == 0f && pivot.z == 0f
                && !(renderer.minParticleSize > 0f);
        }

        /// <summary>Whether an enabled camera whose culling mask has the renderer's layer has the
        /// live particles' centres ± size·√3/2 (the largest size axis, by the largest emitter scale
        /// axis) at least partly in its view frustum now. Conservative on purpose: the cameras'
        /// displays, layer cull distances and LOD groups are not applied (a camera that would not
        /// draw the system only keeps it simulating).</summary>
        bool ConservativeBoundsInView(ParticleSystemRenderer renderer)
        {
            if (m_Count == 0) return false;
            int layer = 1 << renderer.m_GameObject.m_Layer;
            bool bounded = false;
            var bounds = new Bounds();
            foreach (var camera in Camera.s_Enabled)
            {
                if (!camera || !camera.isActiveAndEnabled || (camera.cullingMask & layer) == 0) continue;
                if (!bounded)
                {
                    bounds = ConservativeWorldBounds();
                    bounded = true;
                }
                if (Unbound.Runtime.FrameBuilder.InFrustum(camera.projectionMatrix * camera.worldToCameraMatrix, bounds)) return true;
            }
            return false;
        }

        Bounds ConservativeWorldBounds()
        {
            const float halfSqrt3 = 0.8660254f;
            var toWorld = SimulationToWorld();
            var scale = EmitterScale();
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int i = 0; i < m_Count; i++)
            {
                ref var p = ref m_Particles[i];
                var w = toWorld.MultiplyPoint3x4(p.m_Position);
                var s = CurrentSize3D(ref p);
                float r = halfSqrt3 * maxScale * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                min = Vector3.Min(min, w - new Vector3(r, r, r));
                max = Vector3.Max(max, w + new Vector3(r, r, r));
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
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
                    try { ps.CullingFrameUpdate(); }
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
            for (int i = 0; i < n; i++)
            {
                var ps = s_Snapshot[i];
                if (ps == null) continue;
                try { ps.UpdateModuleLights(); }
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
