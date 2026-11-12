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
        /// <summary>Stopped in the frame's update while its sub-emitters may still have particles: its
        /// stop callback and action wait for them (see ParticleSystemManager.UpdateAll).</summary>
        internal bool m_StopPending;
        bool m_PlaybackRestored;
        internal float m_Time;
        internal float m_TotalTime;
        internal float m_DelayRemaining;
        internal int m_Loop;
        internal float m_RateAccumulator;
        /// <summary>The time of earlier calls not yet stepped: what a fixed-step Simulate left
        /// below one step, or the residual (at most a microsecond) that ends a variable-step
        /// advance. The next advance adds it to its own time; a new playback drops it
        /// (6000.0.71f1, tests/projects/particleemission stoppedSimulate/*/1/0 and
        /// stoppedResume/*).</summary>
        internal float m_StepRemainder;
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

        /// <summary>This system, after the frame's particle update completed: what a script
        /// reads or changes first waits for that (see <see cref="ParticleSystemManager.Sync"/>).</summary>
        ParticleSystem Completed
        {
            get
            {
                ParticleSystemManager.Sync();
                return this;
            }
        }

        public bool isPlaying => Completed.m_IsPlaying && !m_IsPaused;
        // A zero rate, an empty system and the start delay still count as emitting. The
        // module switch gates this observation without changing the playback state.
        public bool isEmitting => Completed.m_IsPlaying && !m_IsPaused && !m_StopEmitting && m_Emission.enabled;
        public bool isStopped => !Completed.m_IsPlaying && !m_IsPaused;
        public bool isPaused => Completed.m_IsPaused;
        public int particleCount => Completed.m_Count;

        public float time
        {
            get => Completed.m_Time;
            set => Completed.m_Time = Mathf.Clamp(value, 0f, m_Main.duration);
        }

        public float totalTime => Completed.m_TotalTime;

        public uint randomSeed
        {
            // With automatic seeding, the seed drawn for the current playback; before one
            // has begun, the serialized seed (unmeasured).
            get => m_Main.autoRandomSeed && m_InitialRandomReady ? m_PlaybackSeed : m_Main.randomSeed;
            set
            {
                if (isPlaying || m_Count > 0)
                {
                    Debug.LogError("Cannot set the random seed while system is playing.\nUse ParticleSystem.Stop() before changing the random seed or use ParticleSystem.useAutoRandomSeed.", this);
                    return;
                }
                m_Main.randomSeed = value;
                m_Main.autoRandomSeed = false;
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

        /// <summary>Scripting API 6000.0: "Only a subset of modules and properties supports
        /// procedural mode"; the Limit Velocity over Lifetime module and "modifying any properties
        /// from script while the system is playing" disable it. Measured in 6000.0.71f1
        /// (tests/projects/particlerender proc/*): true until the first playback starts; then
        /// false with World or Custom simulation space, a rate over distance, a gravity modifier
        /// that is not a constant, orbital or radial velocity, Limit Velocity, a randomized Force
        /// over Lifetime, Rotation by Speed, External Forces, Noise, Collision, Trigger, Sub
        /// Emitters or Trails enabled, or a stop action; and false after a property is set while
        /// playing, until a new playback starts. The other modules and settings of the probe keep
        /// it (velocity, force and start curves, color, size and speed modules, texture sheet,
        /// lights, custom data, a mesh shape, start delay, prewarm, simulation speed, unscaled
        /// time, ring buffer, emitter velocity mode). Module methods that change settings (bursts,
        /// planes, colliders, sub-emitters, sprites) are unmeasured and keep it.</summary>
        public bool proceduralSimulationSupported => !m_HasPlayed || !m_ProceduralLost && ProceduralSettings();

        /// <summary>A playback has started (procedural mode is decided from then on).</summary>
        bool m_HasPlayed;
        /// <summary>A property was set from script while the system played.</summary>
        bool m_ProceduralLost;

        /// <summary>Called by the module setters: a property set while playing ends procedural
        /// mode until the next playback (see <see cref="proceduralSimulationSupported"/>).</summary>
        internal ParticleSystem ModifiedByScript()
        {
            if (m_IsPlaying) m_ProceduralLost = true;
            return this;
        }

        bool ProceduralSettings()
        {
            if (m_Main.simulationSpace != ParticleSystemSimulationSpace.Local) return false;
            if (m_Main.gravityModifier.mode != ParticleSystemCurveMode.Constant) return false;
            if (m_Main.stopAction != ParticleSystemStopAction.None) return false;
            if (m_Emission.enabled && MayBeNonZero(m_Emission.rateOverDistance)) return false;
            if (m_Velocity.enabled && (MayBeNonZero(m_Velocity.orbitalX) || MayBeNonZero(m_Velocity.orbitalY)
                || MayBeNonZero(m_Velocity.orbitalZ) || MayBeNonZero(m_Velocity.radial))) return false;
            if (m_Force.enabled && m_Force.randomized) return false;
            return !m_LimitVelocity.enabled && !m_RotationBySpeed.enabled && !m_ExternalForces.enabled && !m_Noise.enabled
                && !m_Collision.enabled && !m_Trigger.enabled && !m_SubEmitters.enabled && !m_Trails.enabled;
        }

        static bool MayBeNonZero(in ModuleCurve curve) => curve.mode != ParticleSystemCurveMode.Constant || curve.scalar != 0f;

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
        public float startDelay { get => m_Main.startDelay.Multiplier; set => ModifiedByScript().m_Main.startDelay.Multiplier = value; }

        [Obsolete("loop property is deprecated. Use main.loop instead.", false)]
        public bool loop { get => m_Main.loop; set => ModifiedByScript().m_Main.loop = value; }

        [Obsolete("playOnAwake property is deprecated. Use main.playOnAwake instead.", false)]
        public bool playOnAwake { get => m_Main.playOnAwake; set => ModifiedByScript().m_Main.playOnAwake = value; }

        [Obsolete("duration property is deprecated. Use main.duration instead.", false)]
        public float duration => m_Main.duration;

        [Obsolete("playbackSpeed property is deprecated. Use main.simulationSpeed instead.", false)]
        public float playbackSpeed { get => m_Main.simulationSpeed; set => ModifiedByScript().m_Main.simulationSpeed = value; }

        [Obsolete("enableEmission property is deprecated. Use emission.enabled instead.", false)]
        public bool enableEmission { get => m_Emission.enabled; set => ModifiedByScript().m_Emission.enabled = value; }

        [Obsolete("emissionRate property is deprecated. Use emission.rateOverTime, emission.rateOverDistance, emission.rateOverTimeMultiplier or emission.rateOverDistanceMultiplier instead.", false)]
        public float emissionRate { get => m_Emission.rateOverTime.Multiplier; set => ModifiedByScript().m_Emission.rateOverTime.Set(value); }

        [Obsolete("startSpeed property is deprecated. Use main.startSpeed or main.startSpeedMultiplier instead.", false)]
        public float startSpeed { get => m_Main.startSpeed.Multiplier; set => ModifiedByScript().m_Main.startSpeed.Multiplier = value; }

        [Obsolete("startSize property is deprecated. Use main.startSize or main.startSizeMultiplier instead.", false)]
        public float startSize { get => m_Main.startSizeX.Multiplier; set => ModifiedByScript().m_Main.startSizeX.Multiplier = value; }

        [Obsolete("startColor property is deprecated. Use main.startColor instead.", false)]
        public Color startColor { get => m_Main.startColor.color; set => ModifiedByScript().m_Main.startColor = value; }

        [Obsolete("startRotation property is deprecated. Use main.startRotation or main.startRotationMultiplier instead.", false)]
        public float startRotation { get => m_Main.startRotationZ.Multiplier; set => ModifiedByScript().m_Main.startRotationZ.Multiplier = value; }

        [Obsolete("startRotation3D property is deprecated. Use main.startRotationX, main.startRotationY and main.startRotationZ instead. (Or main.startRotationXMultiplier, main.startRotationYMultiplier and main.startRotationZMultiplier).", false)]
        public Vector3 startRotation3D
        {
            get => new Vector3(m_Main.startRotationX.Multiplier, m_Main.startRotationY.Multiplier, m_Main.startRotationZ.Multiplier);
            set
            {
                var main = ModifiedByScript().m_Main;
                main.startRotationX.Multiplier = value.x;
                main.startRotationY.Multiplier = value.y;
                main.startRotationZ.Multiplier = value.z;
            }
        }

        [Obsolete("startLifetime property is deprecated. Use main.startLifetime or main.startLifetimeMultiplier instead.", false)]
        public float startLifetime { get => m_Main.startLifetime.Multiplier; set => ModifiedByScript().m_Main.startLifetime.Multiplier = value; }

        [Obsolete("gravityModifier property is deprecated. Use main.gravityModifier or main.gravityModifierMultiplier instead.", false)]
        public float gravityModifier { get => m_Main.gravityModifier.Multiplier; set => ModifiedByScript().m_Main.gravityModifier.Multiplier = value; }

        [Obsolete("maxParticles property is deprecated. Use main.maxParticles instead.", false)]
        public int maxParticles { get => m_Main.maxParticles; set => ModifiedByScript().m_Main.maxParticles = Math.Max(0, value); }

        [Obsolete("simulationSpace property is deprecated. Use main.simulationSpace instead.", false)]
        public ParticleSystemSimulationSpace simulationSpace { get => m_Main.simulationSpace; set => ModifiedByScript().m_Main.simulationSpace = value; }

        [Obsolete("scalingMode property is deprecated. Use main.scalingMode instead.", false)]
        public ParticleSystemScalingMode scalingMode { get => m_Main.scalingMode; set => ModifiedByScript().m_Main.scalingMode = value; }

        [Obsolete("automaticCullingEnabled property is deprecated. Use proceduralSimulationSupported instead (UnityUpgradable) -> proceduralSimulationSupported", false)]
        public bool automaticCullingEnabled => proceduralSimulationSupported;

        internal void SetSimulationSpace(ParticleSystemSimulationSpace space) => m_Main.simulationSpace = space;

        // ------------------------------------------------------------------ playback

        public void Play() => Play(true);

        public void Play([Internal.DefaultValue("true")] bool withChildren)
        {
            ThrowIfDestroyed();
            ParticleSystemManager.Sync();
            foreach (var ps in Systems(withChildren, includeSubEmitters: false))
            {
                ps.PlaySelf();
            }
        }

        public void Pause() => Pause(true);

        public void Pause([Internal.DefaultValue("true")] bool withChildren)
        {
            ParticleSystemManager.Sync();
            foreach (var ps in Systems(withChildren, includeSubEmitters: true)) ps.m_IsPaused = true;
        }

        public void Stop() => Stop(true, ParticleSystemStopBehavior.StopEmitting);

        public void Stop([Internal.DefaultValue("true")] bool withChildren) => Stop(withChildren, ParticleSystemStopBehavior.StopEmitting);

        public void Stop([Internal.DefaultValue("true")] bool withChildren, [Internal.DefaultValue("ParticleSystemStopBehavior.StopEmitting")] ParticleSystemStopBehavior stopBehavior)
        {
            ParticleSystemManager.Sync();
            var systems = Systems(withChildren, includeSubEmitters: true);
            // Finish clearing linked buffers before StopSelf dispatches stop actions.
            // A stop callback may emit again; those new particles must survive.
            if (stopBehavior == ParticleSystemStopBehavior.StopEmittingAndClear)
                foreach (var ps in ClearedSystems(withChildren))
                {
                    ps.ClearParticles();
                    // Linked-only systems receive Clear's playback transition, but
                    // selected systems retain StopSelf's callback eligibility.
                    if (ps.m_StopEmitting && !systems.Contains(ps)) ps.m_IsPlaying = false;
                }
            foreach (var ps in systems) ps.StopSelf(stopBehavior);
        }

        public void Clear() => Clear(true);

        /// <summary>Removes the particles of this system, of the systems it links as sub-emitters
        /// (wherever they are in the hierarchy, with or without children), and with children of
        /// the child systems too (6000.0 Scripting API, ParticleSystem.Clear remarks).</summary>
        public void Clear([Internal.DefaultValue("true")] bool withChildren)
        {
            ParticleSystemManager.Sync();
            foreach (var ps in ClearedSystems(withChildren))
            {
                ps.ClearParticles();
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
            ParticleSystemManager.Sync();
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
            ParticleSystemManager.Sync();
            var systems = SimulatedSystems(withChildren, out int hierarchyCount);
            // Every simulated system has its bounds again.
            foreach (var ps in systems) ps.GiveBounds();
            // Reset every selected system before a parent can emit into its children.
            // Otherwise the child's later reset erases that emission and its random cursors.
            if (restart)
                foreach (var ps in systems)
                {
                    ps.ClearParticles();
                    ps.m_IsPlaying = false;
                    ps.m_StopEmitting = false;
                    ps.m_StepRemainder = 0f;
                }
            var startedSystems = new bool[systems.Count];
            var stoppedSystems = new bool[systems.Count];
            for (int index = 0; index < systems.Count; index++)
            {
                var ps = systems[index];
                // Paused systems continue where they were; an empty stopped system
                // advances its clock without restarting its delay or emission.
                bool resume = !restart && ps.m_IsPaused;
                stoppedSystems[index] = !restart && !ps.m_IsPlaying && ps.m_StopEmitting;
                startedSystems[index] = !ps.m_IsPlaying && !resume && !stoppedSystems[index];
            }
            // Every starting system restarts (drawing its playback random, in list order) before any
            // prewarm runs: a prewarm emits into its sub-emitters with their own fresh random state,
            // and no later restart erases it, so repeated restarting calls give the same result.
            for (int index = 0; index < systems.Count; index++)
                if (startedSystems[index]) systems[index].Restart(prewarm: false);
            if (restart)
                for (int index = 0; index < systems.Count; index++)
                    if (startedSystems[index]) systems[index].PrewarmIfLooping();
            foreach (var ps in systems)
            {
                ps.m_SimulatingBirths = Compat.ParticleBirthStepsMeasured;
                // Measured Birth Simulate calls age child particles while a delayed
                // child's shared playback clock stays at zero. Emission's per-parent
                // delay is evaluated separately in BirthSubEmitters.
                ps.m_BirthDelayClock = false;
                if (ps != this && ps.m_SimulatingBirths && ps.m_DelayRemaining > 0f && ps.m_SubEmitterParent != null)
                    foreach (var sub in ps.m_SubEmitterParent.m_SubEmitters.emitters)
                        if (sub.system == ps && sub.type == ParticleSystemSubEmitterType.Birth)
                            ps.m_BirthDelayClock = true;
            }
            try
            {
                foreach (int index in BirthSimulationOrder(systems))
                {
                    var ps = systems[index];
                    bool referenced = index >= hierarchyCount;
                    bool started = startedSystems[index];
                    bool stopped = stoppedSystems[index];
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
                    ps.m_BirthCallRemaining = total;
                    // A Simulate call queries as much as the system's last frame allowed.
                    ps.m_Rays = ps.m_AssignedRays;
                    if (fixedTimeStep)
                    {
                        float step = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                        ps.m_StepRemainder += total;
                        int count = (int)Math.Min(ps.m_StepRemainder / step, 100000f);
                        float distance = ps.m_FrameDistance / Math.Max(count, 1);
                        for (int guard = 0; ps.m_StepRemainder >= step && guard < 100000; guard++)
                        {
                            if (stopped) ps.AdvanceStoppedClock(step, distance, carry: false);
                            else ps.Advance(step, distance, false, (float)guard / Math.Max(count, 1), (float)(guard + 1) / Math.Max(count, 1), carry: false);
                            ps.m_StepRemainder -= step;
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
            finally
            {
                foreach (var ps in systems)
                {
                    ps.m_SimulatingBirths = false;
                    ps.m_BirthDelayClock = false;
                }
            }
        }

        // Measured Simulate histories: Birth children advance their existing particles
        // before their parents emit newborns with their own catch-up time. Other links
        // retain the selected systems' order. This does not order particle buffers.
        static List<int> BirthSimulationOrder(List<ParticleSystem> systems)
        {
            var order = new List<int>(systems.Count);
            var visited = new bool[systems.Count];
            void Visit(int index)
            {
                if (visited[index]) return;
                visited[index] = true;
                var ps = systems[index];
                // Children a step emits into (Birth, Death, Collision, Trigger) advance their own
                // particles first; the newborns then get their own catch-up.
                if (ps.m_SimulatingBirths && ps.m_SubEmitters.enabled)
                    foreach (var sub in ps.m_SubEmitters.emitters)
                        if (sub.type != ParticleSystemSubEmitterType.Manual)
                        {
                            int child = systems.IndexOf(sub.system);
                            if (child >= 0) Visit(child);
                        }
                order.Add(index);
            }
            for (int i = 0; i < systems.Count; i++) Visit(i);
            return order;
        }

        /// <summary>A stopped system still simulates particles supplied by SetParticles, and its
        /// steps advance the clock (past what is left of its start delay, see
        /// AdvanceEmissionStep) without restarting automatic emission. One that never played
        /// takes its start delay at its first stopped step (6000.0.71f1,
        /// tests/projects/particleemission stoppedSimulate/{1,2,3}/1/0.25).</summary>
        void AdvanceStoppedClock(float dt, float distance, bool carry = true)
        {
            if (!m_HasPlayed && !m_StoppedDelayTaken)
            {
                m_StoppedDelayTaken = true;
                m_DelayRemaining = Mathf.Max(0f, m_Main.startDelay.Evaluate(0f, ModuleValue(m_PlaybackSeed, 0u)));
            }
            Advance(dt, distance, false, carry: carry);
        }

        /// <summary>A system that never played has taken its start delay for its stopped clock.</summary>
        bool m_StoppedDelayTaken;

        /// <summary>Emits now; a system not playing starts playing (measured).</summary>
        public void Emit(int count) => EmitNow(count, default, false);

        public void Emit(EmitParams emitParams, int count) => EmitNow(count, emitParams, true);

        /// <summary>Emitting into a system that is not playing makes it play its particles without
        /// emitting more (measured: isPlaying true, isEmitting false).</summary>
        void EmitNow(int count, EmitParams emitParams, bool hasOverrides)
        {
            ParticleSystemManager.Sync();
            if (count == 0) return;
            if (!m_IsPlaying || m_IsPaused)
            {
                m_IsPlaying = true;
                m_StopEmitting = true;
            }
            m_IsPaused = false;
            if (!hasOverrides && count > 0 && m_GameObject != null && StartsInBatches)
            {
                // Emit(count) starts its particles as one batch, like a step's emission
                // (6000.0.71f1, tests/projects/particlemeshshape texture/clip/emit*).
                m_EmissionRequests.Clear();
                m_EmissionRequests.Add(new EmissionRequest { count = count, birthTime = m_Time, speedNt = -1f, spreadIndex = -1 });
                try { FlushRateBatch(simulate: false); }
                finally { m_EmissionRequests.Clear(); }
                return;
            }
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
            ParticleSystemManager.Sync();
            int index = Allocate();
            if (index >= 0)
            {
                MaterializeAxisOfRotation();
                m_Particles[index] = particle;
            }
        }

        public void TriggerSubEmitter(int subEmitterIndex)
        {
            ParticleSystemManager.Sync();
            if (!ScriptSubEmitter(subEmitterIndex, out var sub)) return;
            for (int i = 0; i < m_Count; i++) TriggerSubEmitterAt(sub, ref m_Particles[i], fromScript: true);
        }

        public void TriggerSubEmitter(int subEmitterIndex, ref Particle particle)
        {
            ParticleSystemManager.Sync();
            if (ScriptSubEmitter(subEmitterIndex, out var sub)) TriggerSubEmitterAt(sub, ref particle, fromScript: true);
        }

        public void TriggerSubEmitter(int subEmitterIndex, List<Particle> particles)
        {
            ParticleSystemManager.Sync();
            if (particles == null || !ScriptSubEmitter(subEmitterIndex, out var sub)) return;
            for (int i = 0; i < particles.Count; i++)
            {
                var p = particles[i];
                TriggerSubEmitterAt(sub, ref p, fromScript: true);
            }
        }

        /// <summary>The slot TriggerSubEmitter names. Measured in 6000.0.71f1 (subemitters probe): a
        /// disabled Sub Emitters module emits nothing, and an index past the last slot does nothing,
        /// without an exception or a logged error (a negative index is treated the same, unmeasured).
        /// Any slot type can be triggered (a Death slot emits as a Manual one).</summary>
        bool ScriptSubEmitter(int index, out SubEmitter sub)
        {
            sub = default;
            if (!m_SubEmitters.enabled || index < 0 || index >= m_SubEmitters.emitters.Count) return false;
            sub = m_SubEmitters.emitters[index];
            return true;
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
            ParticleSystemManager.Sync();
            if (particles == null) throw new ArgumentNullException(nameof(particles));
            if (size < 0 || size > particles.Length) size = particles.Length;
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            int count = offset + size;
            int before = m_Count;
            Reserve(count);
            if (count > 0) MaterializeAxisOfRotation();
            for (int i = 0; i < size && offset + i < count; i++) WriteParticle(offset + i, particles[i], offset + i < before);
            for (int s = 0; s < 2; s++)
                if (m_Custom[s] != null && m_Custom[s].Length < count) Array.Resize(ref m_Custom[s], m_Particles.Length);
            m_Count = count;
            NoteMostParticles();
            // Native consumes the input lifetime before encoding normalized age. Nonpositive
            // inputs die immediately; tiny positive inputs can export as zero and still survive.
            // The retained prefix is untouched, even when its exported lifetime is zero.
            var marks = SlotMarks(m_Count);
            for (int i = offset; i < m_Count; i++)
            {
                if (DiesWhenWritten(i)) marks[i] = SlotMark.Dead;
                else StartWrittenTrail(ref m_Particles[i], i >= before);
            }
            RemoveDeadSlots(offset & ~3, DeadSlotRemoval.Drop);
            for (int i = offset; i < m_Count; i++) SettleWrittenParticle(i);
            // A particle written at a nonpositive lifetime, or cut off by the size, leaves with its
            // trail, also a fading one: it does not fade (6000.0.71f1, tests/projects/particletrails
            // pp/setparticles and pp/killlife, whatever dieWithParticles).
            FreeUnheldTrails();
        }

        /// <summary>What leaving the buffer means for a dead slot: SetParticles just drops it; a death in a step ends its trail (or leaves it to fade), and can fire
        /// Death sub-emitters.</summary>
        enum DeadSlotRemoval : byte { Drop, Death, DeathWithSubEmitters }

        [Flags]
        enum SlotMark : byte { Dead = 1 }

        // Per-slot removal state and a point each slot carries through the compaction (a dying
        // particle's event position, a newborn's origin), reused by every step (grown, never shrunk).
        SlotMark[] m_SlotMarks = Array.Empty<SlotMark>();
        Vector3[] m_SlotPoints = Array.Empty<Vector3>();
        /// <summary>The slots the current marks describe; a particle appended after them (by
        /// a sub-emitter cycle while the dead leave) is unmarked.</summary>
        int m_MarkedSlots;

        /// <summary>The cleared marks for slots [0, <paramref name="count"/>).</summary>
        /// <summary>Empties the buffer: no particles, the ring restarted, no trails.</summary>
        internal void ClearParticles()
        {
            m_Count = 0;
            m_RingIndex = 0;
            ClearTrails();
        }

        SlotMark[] SlotMarks(int count)
        {
            if (m_SlotMarks.Length < count)
            {
                m_SlotMarks = new SlotMark[Math.Max(count, m_SlotMarks.Length * 2)];
                m_SlotPoints = new Vector3[m_SlotMarks.Length];
            }
            else Array.Clear(m_SlotMarks, 0, count);
            m_MarkedFrom = 0;
            m_MarkedSlots = count;
            return m_SlotMarks;
        }

        /// <summary>Marks for the slots from <paramref name="from"/> to <paramref name="count"/>
        /// only (a batch appended to the buffer): the slots before read unmarked, without clearing
        /// them each time.</summary>
        SlotMark[] SlotMarksFrom(int from, int count)
        {
            // An outer pass over this buffer may still hold its marks and points below: kept.
            if (m_SlotMarks.Length < count)
            {
                Array.Resize(ref m_SlotMarks, Math.Max(count, m_SlotMarks.Length * 2));
                Array.Resize(ref m_SlotPoints, m_SlotMarks.Length);
            }
            Array.Clear(m_SlotMarks, from, count - from);
            m_MarkedFrom = from;
            m_MarkedSlots = count;
            return m_SlotMarks;
        }

        /// <summary>The first slot the current marks cover.</summary>
        int m_MarkedFrom;

        SlotMark MarkOf(int index) => index >= m_MarkedFrom && index < m_MarkedSlots ? m_SlotMarks[index] : 0;

        /// <summary>Removes the slots marked dead from <paramref name="from"/> (a multiple of four)
        /// on. Unity 6000.0.71f1 compacts in four-slot groups aligned to the buffer: it collects
        /// a group's dead slots, removes them high to low, each taking the last particle in, then
        /// revisits the group, whose swapped-in particles can be dead too (particlerandom
        /// setparticles and model/death/: the same order for SetParticles and for deaths in a
        /// step; a rate batch's newborns follow another order, see FlushRateBatch). A dying
        /// particle fires its Death sub-emitters as it leaves; one whose trail stays to fade
        /// keeps its slot as the trail's carrier, and leaves the same way once the trail is spent
        /// (tests/projects/particletrails).</summary>
        void RemoveDeadSlots(int from, DeadSlotRemoval removal)
        {
            var marks = m_SlotMarks;
            for (int block = from; block < m_Count;)
            {
                int mask = 0;
                for (int i = block; i < Math.Min(block + 4, m_Count); i++)
                    if ((MarkOf(i) & SlotMark.Dead) != 0) mask |= 1 << (i - block);
                if (mask == 0) { block += 4; continue; }
                for (int lane = 3; lane >= 0; lane--)
                {
                    if ((mask & (1 << lane)) == 0) continue;
                    int index = block + lane;
                    marks[index] = 0;
                    if (m_Particles[index].IsTrailCarrier)
                    {
                        // A carrier of a fading trail already died: no second death; its trail
                        // goes with it (spent, or dropped by SetParticles: measured, it does not fade).
                        EndTrail(ref m_Particles[index]);
                    }
                    else if (removal != DeadSlotRemoval.Drop)
                    {
                        if (removal == DeadSlotRemoval.DeathWithSubEmitters)
                        {
                            var dying = m_Particles[index];
                            dying.m_Position = m_SlotPoints[index];
                            TriggerSubEmitters(ParticleSystemSubEmitterType.Death, ref dying);
                        }
                        if (KeepOrphanTrail(ref m_Particles[index])) continue;
                        EndTrail(ref m_Particles[index]);
                    }
                    RemoveSlot(index);
                }
            }
        }

        /// <summary>Removes a slot as <see cref="RemoveAt"/> does; the last slot's mark and
        /// point move with its particle.</summary>
        void RemoveSlot(int index)
        {
            int last = m_Count - 1;
            RemoveAt(index);
            if (index < m_MarkedSlots) m_SlotMarks[index] = 0;
            if (index >= last) return;
            if (index < m_MarkedSlots)
            {
                m_SlotMarks[index] = MarkOf(last);
                if (last < m_MarkedSlots) m_SlotPoints[index] = m_SlotPoints[last];
            }
            if (last < m_MarkedSlots) m_SlotMarks[last] = 0;
        }

        /// <summary>SetParticles' write of one slot: the public particle replaces it.</summary>
        /// <summary>Writes a particle into a slot. Like the noise arrays, the trail belongs to the
        /// slot, not to the written struct: a live slot keeps its trail, whatever particle is
        /// written there (6000.0.71f1, tests/projects/particletrails api/rt trailsfirst: trails
        /// stay with the indices across SetParticles); a slot past the live ones has none yet.</summary>
        void WriteParticle(int index, in Particle particle, bool live = true)
        {
            // Native optional noise arrays are separate from the public particle struct.
            var noiseSum = m_Particles[index].m_NoiseSum;
            var noiseImpulse = m_Particles[index].m_NoiseImpulse;
            int trail = live ? m_Particles[index].m_TrailId : 0;
            uint slotSeed = m_Particles[index].m_RandomSeed;
            m_Particles[index] = particle;
            m_Particles[index].m_NoiseSum = noiseSum;
            m_Particles[index].m_NoiseImpulse = noiseImpulse;
            m_Particles[index].m_TrailId = trail;
            // What was written is a particle, not a carrier: one written dead leaves at once (with
            // its slot's trail), one written alive makes the slot's fading trail live again.
            m_Particles[index].m_Flags &= ~Particle.FlagOrphanTrail;
            var held = trail > 0 && trail <= m_TrailList.Count ? m_TrailList[trail - 1] : null;
            if (held != null)
            {
                held.Orphan = held.Spent = false;
                // The draws a trail takes from its particle follow another particle (seed) written
                // there; the same particle written back keeps them, also with a changed start
                // lifetime or size (unmeasured).
                if (particle.m_RandomSeed != slotSeed) SetTrailDraws(ref m_Particles[index], held);
            }
        }

        /// <summary>Whether a written slot dies at once (SetParticles' nonpositive remaining lifetime).</summary>
        bool DiesWhenWritten(int index) => m_Particles[index].m_Lifetime <= 0f;

        /// <summary>SetParticles' encoding of a written survivor's age.</summary>
        void SettleWrittenParticle(int index)
        {
            m_Particles[index].InitializeAge();
            // SetParticles clamps inputs that exceed their start lifetime to birth.
            if (m_Particles[index].m_AgePercent < 0f)
            {
                m_Particles[index].m_AgePercent = 0f;
                m_Particles[index].ExportLifetime();
            }
        }

        /// <summary>SetParticles' write path for live slots in any order (the trigger
        /// write-back): each particle replaces its slot as SetParticles writes one (a slot
        /// listed twice keeps the later particle). A written particle whose encoded age a
        /// remaining or start lifetime setter cleared dies at once or encodes its age as
        /// SetParticles does; one that still has its encoded age keeps it and its life
        /// (Unbound's choice, unmeasured: the trigger write-back changes only the fields a
        /// script changed). SetParticles' removal order is measured for a contiguous range
        /// only; here the dead slots go highest first, so no pending slot moves (Unbound's
        /// choice, unmeasured).</summary>
        internal void WriteParticlesAt(int[] slots, Particle[] particles, int count)
        {
            if (count <= 0) return;
            MaterializeAxisOfRotation();
            for (int k = 0; k < count; k++) WriteParticle(slots[k], particles[k]);
            var written = new HashSet<int>();
            var dead = new List<int>();
            for (int k = 0; k < count; k++)
            {
                int slot = slots[k];
                if (!written.Add(slot)) continue;
                if ((m_Particles[slot].m_Flags & Particle.FlagAgeInitialized) != 0) continue;
                if (DiesWhenWritten(slot)) dead.Add(slot);
                else SettleWrittenParticle(slot);
            }
            dead.Sort();
            for (int k = dead.Count - 1; k >= 0; k--) RemoveAt(dead[k]);
            // Their trails go with them, as with SetParticles.
            if (dead.Count > 0) FreeUnheldTrails();
        }

        public void SetParticles(Particle[] particles, int size) => SetParticles(particles, size, 0);

        public void SetParticles(Particle[] particles) => SetParticles(particles, -1, 0);

        public void SetParticles(NativeArray<Particle> particles, int size, int offset) => SetParticles(particles.ToArray(), size, offset);

        public void SetParticles(NativeArray<Particle> particles, int size) => SetParticles(particles.ToArray(), size, 0);

        public void SetParticles(NativeArray<Particle> particles) => SetParticles(particles.ToArray(), -1, 0);

        public int GetParticles(Particle[] particles, int size, int offset)
        {
            ParticleSystemManager.Sync();
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
            ParticleSystemManager.Sync();
            if (customData == null) throw new ArgumentNullException(nameof(customData));
            var data = EnsureCustom((int)streamIndex);
            int n = Math.Min(customData.Count, m_Count);
            for (int i = 0; i < n; i++) data[i] = customData[i];
        }

        public int GetCustomParticleData(List<Vector4> customData, ParticleSystemCustomData streamIndex)
        {
            ParticleSystemManager.Sync();
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
                Color c = m_CustomData.colors[stream].Evaluate(age, ModuleValue(seed, 1940387770u - (uint)stream));
                return new Vector4(c.r, c.g, c.b, c.a);
            }
            // The vector is written by the last update, at the age before it (measured).
            if (p.m_StartLifetime > 0f) age = Mathf.Max(0f, age - m_LastStepDt / p.m_StartLifetime);
            int count = Mathf.Clamp(m_CustomData.componentCounts[stream], 0, 4);
            for (int k = 0; k < count; k++)
                stored[k] = m_CustomData.vectors[stream, k].Evaluate(age, ModuleValue(seed, 1940387771u + (uint)(stream * 4 + k)));
            return stored;
        }

        public PlaybackState GetPlaybackState() => new PlaybackState
        {
            m_PlaybackTime = Completed.m_Time,
            m_StartDelay = m_DelayRemaining,
            m_AccumulatedDt = m_RateAccumulator,
            m_TotalTime = m_TotalTime,
            m_Loop = m_Loop,
            m_BurstCycles = (int[])m_BurstCycles.Clone(),
            m_RandomState = m_RandomState,
            m_EmissionRandom = m_EmissionRandom,
            m_ForceRandom0 = m_ForceRandom[0], m_ForceRandom1 = m_ForceRandom[1],
            m_ForceRandom2 = m_ForceRandom[2], m_ForceRandom3 = m_ForceRandom[3],
            m_CollisionRandom0 = m_CollisionRandom[0], m_CollisionRandom1 = m_CollisionRandom[1],
            m_CollisionRandom2 = m_CollisionRandom[2], m_CollisionRandom3 = m_CollisionRandom[3],
            m_InitialRandom0 = m_InitialRandom[0],
            m_InitialRandom1 = m_InitialRandom[1],
            m_InitialRandom2 = m_InitialRandom[2],
            m_InitialRandom3 = m_InitialRandom[3],
            m_InitialRandomReady = m_InitialRandomReady,
            m_PlaybackSeed = m_PlaybackSeed,
            m_ShapeRandom0 = m_ShapeRandom[0],
            m_ShapeRandom1 = m_ShapeRandom[1],
            m_ShapeRandom2 = m_ShapeRandom[2],
            m_ShapeRandom3 = m_ShapeRandom[3],
            m_ShapeRandomReady = m_ShapeRandomReady,
            m_MeshVertexCursor = m_MeshVertexCursor,
        };

        public void SetPlaybackState(PlaybackState playbackState)
        {
            ParticleSystemManager.Sync();
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
            m_EmissionRandom = playbackState.m_EmissionRandom;
            m_ForceRandom[0] = playbackState.m_ForceRandom0; m_ForceRandom[1] = playbackState.m_ForceRandom1;
            m_ForceRandom[2] = playbackState.m_ForceRandom2; m_ForceRandom[3] = playbackState.m_ForceRandom3;
            m_CollisionRandom[0] = playbackState.m_CollisionRandom0; m_CollisionRandom[1] = playbackState.m_CollisionRandom1;
            m_CollisionRandom[2] = playbackState.m_CollisionRandom2; m_CollisionRandom[3] = playbackState.m_CollisionRandom3;
            m_InitialRandom[0] = playbackState.m_InitialRandom0;
            m_InitialRandom[1] = playbackState.m_InitialRandom1;
            m_InitialRandom[2] = playbackState.m_InitialRandom2;
            m_InitialRandom[3] = playbackState.m_InitialRandom3;
            m_InitialRandomReady = playbackState.m_InitialRandomReady;
            m_PlaybackSeed = playbackState.m_PlaybackSeed;
            m_ShapeRandom[0] = playbackState.m_ShapeRandom0;
            m_ShapeRandom[1] = playbackState.m_ShapeRandom1;
            m_ShapeRandom[2] = playbackState.m_ShapeRandom2;
            m_ShapeRandom[3] = playbackState.m_ShapeRandom3;
            m_ShapeRandomReady = playbackState.m_ShapeRandomReady;
            m_MeshVertexCursor = playbackState.m_MeshVertexCursor;
        }

        public Trails GetTrails()
        {
            var t = new Trails();
            GetTrails(ref t);
            return t;
        }

        /// <summary>Scripting API 6000.0: fills <paramref name="trailData"/> for a later SetTrails.
        /// Documented as returning the number of trails; Unity 6000.0.71f1 returns the size of the
        /// system's trail storage, which also becomes the Trails' capacity when that is smaller
        /// (tests/projects/particletrails api/*, see <see cref="TrailStorageSize"/>).</summary>
        public int GetTrails(ref Trails trailData)
        {
            ParticleSystemManager.Sync();
            trailData.positions ??= new List<Vector4>();
            trailData.frontPositions ??= new List<int>();
            trailData.backPositions ??= new List<int>();
            trailData.positionCounts ??= new List<int>();
            trailData.textureOffsets ??= new List<float>();
            // The trail state is captured in Unbound's own form beside these fields, which stay
            // empty. A Trails passed again is refilled in place (Scripting API: GetTrails(ref)
            // avoids garbage with presized data).
            CaptureTrails(trailData);
            int size = TrailStorageSize();
            if (trailData.positions.Capacity < size) trailData.positions.Capacity = size;
            return size;
        }

        /// <summary>Scripting API 6000.0: restores trails stored by an earlier GetTrails, used with
        /// SetParticles and SetPlaybackState. Trails go to particles by index, also in another
        /// system (6000.0.71f1, see <see cref="RestoreTrails"/>). A Trails that GetTrails never
        /// filled is refused with an error and changes nothing (unmeasured).</summary>
        public void SetTrails(Trails trailData)
        {
            ParticleSystemManager.Sync();
            if (trailData.positions == null || CaptureOf(trailData) == null)
            {
                Debug.LogError("Unbound: ParticleSystem.SetTrails was given a Trails value that ParticleSystem.GetTrails never filled; the trails were left unchanged", this);
                return;
            }
            RestoreTrails(trailData);
        }

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
            m_StopPending = false;
            // Any Play drops the time a PauseAndCatchup system missed while culled before it
            // (6000.0.71f1, tests/projects/particlerender cull/catchup_loop and catchup_replay).
            m_CullingMissed = 0f;
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
            // A system that is emitting plays on; a playing one that is not (its emission module
            // off) starts a new playback, keeping its particles (6000.0.71f1,
            // tests/projects/particlerender firstvis/*/shown_play against cull/catchup_replay).
            bool playing = m_IsPlaying && !m_StopEmitting && !m_PlaybackRestored;
            if (playing && m_Emission.enabled) return;
            // A replay keeps its particles: no prewarm over them (unmeasured).
            Restart(prewarm: !playing);
        }

        /// <summary>Starts a new playback from time 0, keeping the particles that are alive.</summary>
        internal void Restart(bool prewarm)
        {
            m_StartedByParent = false;
            BeginPlaybackRandom();
            uint seed = m_PlaybackSeed;
            m_RandomState = ((ulong)seed << 32) ^ 0x9e3779b97f4a7c15UL ^ seed;
            if (m_RandomState == 0) m_RandomState = 0x9e3779b97f4a7c15UL;
            m_NoiseSumEnabled = false;
            m_NoiseScroll = 0f;
            m_Time = 0f;
            m_TotalTime = 0f;
            m_CullingMissed = 0f;
            m_HasPlayed = true;
            m_ProceduralLost = false;
            m_CullingGraceFrame = Time.frameCount;
            m_Loop = 0;
            m_RateAccumulator = 0f;
            m_EmitPhase = 0f;
            m_LoopStart = false;
            m_DistanceAccumulator = 0f;
            Array.Clear(m_BurstCycles, 0, m_BurstCycles.Length);
            m_StepRemainder = 0f;
            m_IsPlaying = true;
            m_StopEmitting = false;
            m_StopPending = false;
            m_PlaybackRestored = false;
            m_DelayRemaining = Mathf.Max(0f, m_Main.startDelay.Evaluate(0f, ModuleValue(seed, 0u)));
            if (prewarm) PrewarmIfLooping();
        }

        /// <summary>A freshly restarted looping system with prewarm on prewarms, with no start delay.</summary>
        internal void PrewarmIfLooping()
        {
            if (!m_Main.prewarm || !m_Main.loop) return;
            m_DelayRemaining = 0f;
            Prewarm();
        }

        static uint NextAutoSeed()
        {
            s_AutoSeed = ParticleRandom.Hash(s_AutoSeed + 0x6d2b79f5u + (uint)Environment.TickCount);
            return s_AutoSeed;
        }

        /// <summary>Prewarm: the playback starts as though the system had already been running.
        /// <para>Measured in 6000.0.71f1 (tests/projects/particlerender prewarm/*,
        /// <see cref="Compat.ParticlePrewarmMeasured"/>): one simulation call as long as the longest
        /// start lifetime plus, for systems with sub-emitters, the longest lifetime down each chain of
        /// sub-emitters (<see cref="SubEmitterLifetimeSpan"/>), split into equal steps of at most
        /// Time.maximumParticleDeltaTime, starting the playback clock at duration less that span's
        /// remainder so it ends on a loop's end; the rate counts from the call's start and bursts
        /// fire as the clock passes them. The clock then stays where the call left it (the duration,
        /// or just past the loop's start). Sub-emitter children emitted in the call are kept only when
        /// the call time left at their step's start is below the child's longest start lifetime, and
        /// are not aged by the rest of the call (see <see cref="EmitFromParent"/>); the
        /// sub-emitters play, without emitting, the direct ones with their time advanced by the span
        /// (<see cref="StartSubEmittersAfterPrewarm"/>).</para>
        /// <para>Other releases keep the previous model: one longest start lifetime from time 0,
        /// then the playback restarts at time 0 keeping the emission's phase.</para></summary>
        void Prewarm()
        {
            // At Medium and Low quality a prewarm makes no collision queries, as a system advanced
            // by Simulate makes none (tests/projects/particlerender collide/cache/simulate_medium);
            // its particles meet only planes already cached (Unbound's choice for prewarm, unmeasured).
            m_Rays = 0;
            float lifetime = Mathf.Max(LongestLifetime(m_Main.startLifetime), 0.0001f);
            if (!Compat.ParticlePrewarmMeasured)
            {
                Advance(lifetime, 0f, false);
                KeepEmissionPhase();
                m_Time = 0f;
                m_Loop = 0;
                m_TotalTime = 0f;
                Array.Clear(m_BurstCycles, 0, m_BurstCycles.Length);
                return;
            }
            float span = lifetime + SubEmitterLifetimeSpan(0);
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            float rest = span % duration;
            if (rest > 0f) StartClockAt(duration - rest);
            if (PrewarmsProcedurally)
            {
                ProceduralPrewarm(span, duration);
                m_Loop = 0;
                m_TotalTime = 0f;
                return;
            }
            bool simulatingBirths = m_SimulatingBirths;
            m_Prewarming = true;
            m_SimulatingBirths = Compat.ParticleBirthStepsMeasured;
            m_BirthCallRemaining = span;
            try { Advance(span, 0f, false); }
            finally
            {
                m_Prewarming = false;
                m_SimulatingBirths = simulatingBirths;
            }
            m_Loop = 0;
            m_TotalTime = 0f;
            StartSubEmittersAfterPrewarm(span, 0);
        }

        /// <summary>The longest start lifetime a curve gives (curves sampled at 33 points), for the
        /// prewarm's span and the lifetimes that bound its children.</summary>
        internal static float LongestLifetime(in ModuleCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.TwoConstants: return Mathf.Max(curve.minScalar, curve.scalar);
                case ParticleSystemCurveMode.Curve: return curve.scalar * MaxKey(curve.maxCurve);
                case ParticleSystemCurveMode.TwoCurves: return curve.scalar * Mathf.Max(MaxKey(curve.minCurve), MaxKey(curve.maxCurve));
                default: return curve.scalar;
            }
        }

        static float MaxKey(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) return 0f;
            float max = float.NegativeInfinity;
            for (int i = 0; i <= 32; i++) max = Mathf.Max(max, curve.Evaluate(i / 32f));
            return max;
        }

        /// <summary>Whether an earlier slot holds the same child (it is started once).</summary>
        static bool InEarlierSlot(List<SubEmitter> emitters, int index)
        {
            for (int j = 0; j < index; j++)
                if (emitters[j].system == emitters[index].system) return true;
            return false;
        }

        /// <summary>Moves a fresh playback's clock to <paramref name="time"/> as if it had started
        /// there: the rate counts from now (its phase follows the clock) and the bursts due before
        /// that time count as fired.</summary>
        void StartClockAt(float time)
        {
            var rate = m_Emission.rateOverTime;
            if (rate.mode == ParticleSystemCurveMode.Constant && rate.scalar > 0f)
            {
                float phase = m_EmitPhase - rate.scalar * time;
                m_EmitPhase = phase - (float)Math.Floor(phase);
            }
            m_Time = time;
            BurstsChanged();
        }

        /// <summary>How much longer than its own particles a prewarm keeps simulating a system for
        /// its sub-emitters: the longest of each sub-emitter's longest start lifetime plus its own
        /// span (6000.0.71f1, particlerender prewarm/*: a Birth or Death child with a 3 s lifetime
        /// adds 3 s, a child of 1 to 4 s adds 4 s, a grandchild adds its own). That every type of
        /// sub-emitter counts, and that a disabled Sub Emitters module adds nothing, is Unbound's
        /// choice (unmeasured).</summary>
        float SubEmitterLifetimeSpan(int depth)
        {
            if (!m_SubEmitters.enabled || depth >= MaxSubEmitterChain) return 0f;
            float span = 0f;
            foreach (var e in m_SubEmitters.emitters)
            {
                var child = e.system;
                if (child == null || child == this || child.m_Destroyed) continue;
                // Kept for the prewarm's child bounds (see PrewarmKeeps): the curve is sampled once.
                child.m_PrewarmLifetime = Mathf.Max(LongestLifetime(child.m_Main.startLifetime), 0f);
                span = Mathf.Max(span, child.m_PrewarmLifetime + child.SubEmitterLifetimeSpan(depth + 1));
            }
            return span;
        }

        /// <summary>After a prewarm every sub-emitter down the chains plays, without emitting, and
        /// stops again at the end of the frame if it is still empty, as after an update; the direct
        /// sub-emitters' time is that of a playback run for the prewarm's span (looping, or held at
        /// the duration), the further ones' is left (6000.0.71f1, particlerender prewarm/chain, prewarm/childloop).</summary>
        void StartSubEmittersAfterPrewarm(float span, int depth)
        {
            if (!m_SubEmitters.enabled || depth >= MaxSubEmitterChain || !Compat.ParticleSubEmitterStartMeasured) return;
            var emitters = m_SubEmitters.emitters;
            for (int i = 0; i < emitters.Count; i++)
            {
                var child = emitters[i].system;
                if (child == null || child == this || child.m_Destroyed || child.m_GameObject == null || InEarlierSlot(emitters, i)) continue;
                // A child this prewarm starts (or one its own restart just reset) is a fresh playback.
                bool fresh = !child.m_IsPlaying && !child.m_IsPaused || child.m_TotalTime == 0f && child.m_Time == 0f;
                if (!child.m_IsPaused && !child.m_IsPlaying)
                {
                    child.m_IsPlaying = true;
                    child.m_StopEmitting = true;
                    child.m_StartedByParent = true;
                    ParticleSystemManager.NoteStartedByParent(child);
                }
                if (depth == 0 && fresh)
                {
                    // As a fresh playback run for the span (not added to an earlier playback's time).
                    float duration = Mathf.Max(child.m_Main.duration, 0.0001f);
                    child.m_Time = child.m_Main.loop ? span % duration : Mathf.Min(span, duration);
                }
                child.StartSubEmittersAfterPrewarm(span, depth + 1);
            }
        }

        /// <summary>Either behaviour latches the stop of emission, whatever the system did
        /// before (also one that never played): only a playback start clears it, so a later
        /// non-restarting Simulate advances the stopped clock without emitting (6000.0.71f1,
        /// tests/projects/particleemission longCall/false/* and stoppedSimulate/*).</summary>
        void StopSelf(ParticleSystemStopBehavior behavior)
        {
            m_StartedByParent = false;
            m_StopEmitting = true;
            if (behavior == ParticleSystemStopBehavior.StopEmittingAndClear)
            {
                bool wasPlaying = m_IsPlaying;
                ClearParticles();
                m_IsPlaying = false;
                m_IsPaused = false;
                m_CullingMissed = 0f;
                if (wasPlaying) OnStopped();
                return;
            }
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

        /// <summary>Marks the systems this one drives as its sub-emitters, also while the module is
        /// disabled: such a child still does not emit on its own (6000.0.71f1 subemitters
        /// manual/module_disabled: a child with a burst at time 0 under a parent whose module was
        /// disabled stays empty through Simulate with children).</summary>
        internal void SubEmittersChanged()
        {
            foreach (var e in m_SubEmitters.emitters)
                if (e.system != null && e.system != this)
                    e.system.m_SubEmitterParent = this;
        }

        /// <summary>AddComponent&lt;ParticleSystem&gt;() adds the renderer at once, also on an
        /// inactive object (6000.0.71f1, tests/projects/particlemeshshape component/*).</summary>
        internal override Type AddedCompanion => typeof(ParticleSystemRenderer);

        internal override void OnEngineEnable()
        {
            if (m_GameObject == null || m_GameObject.m_IsPrefabAsset || m_Registered) return;
            m_Registered = true;
            ParticleSystemManager.Add(this);
            // A serialized system saved without its renderer gets one here (Unbound's choice,
            // unmeasured; AddComponent adds it with the system).
            if (m_GameObject.GetComponent<ParticleSystemRenderer>() == null)
                m_GameObject.AddComponent<ParticleSystemRenderer>();
            // A system that starts with a Mesh-mode renderer (loaded or copied) has its axes of
            // rotation before Play On Awake or a prewarm makes particles: births turn about forward
            // and later reads keep them (only a switch to Mesh mode was measured).
            if (CullingRenderer is ParticleSystemRenderer meshRenderer && meshRenderer.renderMode == ParticleSystemRenderMode.Mesh)
                MaterializeAxisOfRotation();
            m_BoundsGeneration++;
            SubEmittersChanged();
            m_HasLastPosition = false;
            if (m_Main.playOnAwake && m_SubEmitterParent == null)
            {
                PlaySelf();
                GiveBounds();
            }
        }

        internal override void OnEngineDisable()
        {
            DestroyModuleLights();
            if (!m_Registered) return;
            m_Registered = false;
            ParticleSystemManager.Remove(this);
            // Deactivating a system kills its particles and stops it (without its stop action).
            ClearParticles();
            m_IsPlaying = false;
            m_IsPaused = false;
            m_StopEmitting = false;
            m_StopPending = false;
            m_CullingMissed = 0f;
        }

        internal override void OnEngineDestroy()
        {
            OnEngineDisable();
            m_CollisionEvents.Clear();
            ClearTriggerEvents();
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

        /// <summary>The frame a playback started: a system simulates its first two updates that move
        /// time whatever its visibility (6000.0.71f1, tests/projects/particlerender hidden/*, the
        /// first frames before any camera rendered, firstvis/paused/play_auto).</summary>
        int m_CullingGraceFrame = int.MinValue;

        /// <summary>
        /// The frame's update under MainModule.cullingMode. Documented (6000.0 Scripting API
        /// ParticleSystemCullingMode, Manual "Main module" Culling Mode): AlwaysSimulate simulates
        /// every frame "regardless of whether it is on screen or not"; Pause "stops simulating while
        /// offscreen"; PauseAndCatchup also stops and, "when re-entering the view, the simulation
        /// performs a large step to reach the point where it would have been had it not paused";
        /// Automatic: "Looping systems use Pause, and all other system use Always Simulate", and
        /// only a procedural system can be culled (ParticleSystem.proceduralSimulationSupported).
        /// <para>Measured in 6000.0.71f1 (tests/projects/particlerender cull/*, hidden/*): a
        /// system is offscreen when its ParticleSystemRenderer is not visible
        /// (<see cref="Renderer.isVisible"/>, read at the update: a camera's culling makes it
        /// visible at once, and it becomes invisible early in the frame after the first frame no
        /// camera took it); its first two updates after a playback starts simulate whatever its
        /// visibility. Automatic culls a looping procedural system as Pause and simulates a
        /// one-shot or non-procedural one; Pause and PauseAndCatchup cull procedural and
        /// non-procedural systems alike, whatever the render mode. A culled update changes
        /// nothing: time, particles, isPlaying, isPaused stay. PauseAndCatchup simulates the
        /// missed time in its first visible update, before the frame's own time; a Play call
        /// (also a resume from Pause, or on a playing system) drops the time missed before it,
        /// and time missed while a script paused the system does not count.</para>
        /// <para>The missed time and the frame's own time are simulated as one advance, split by
        /// Time.maximumParticleDeltaTime like a Simulate call (matches the measured times and
        /// counts, except births due exactly at a step's end). Unbound's choices (unmeasured): the
        /// emitter's movement while culled is not replayed; a culled update sends no collision or
        /// trigger message; a sub-emitter is culled only when both its own culling and its
        /// parent's cull it (measured: an AlwaysSimulate parent keeps an unseen child updating);
        /// a system without an enabled ParticleSystemRenderer keeps simulating, reported once. Visibility comes from the
        /// renderer's culling bounds (<see cref="ParticleSystemRenderer.CullingBounds"/>), which
        /// Unbound computes conservatively from the particles and the emitter; where it cannot
        /// bound what is drawn (trails, a minimum particle size, camera velocity stretching) the
        /// renderer counts as visible whenever a camera draws its layer, so the system keeps
        /// simulating, reported once.</para>
        /// </summary>
        // The renderer's culling bounds, as the Editor keeps them (6000.0.71f1,
        // tests/projects/particlerender firstvis/*, destroyphases delayFraction, resources
        // mask_particles): a frame update with a positive frame time (at any simulation speed),
        // a Simulate, Play On Awake or a render before any update gives the system bounds; an
        // update with zero frame time leaves them as they were (m_BoundsFrozen: particles emitted
        // meanwhile do not extend them, a system seen keeps being seen), and a system that never
        // had bounds then has nothing to draw (m_DrawPending: not drawn, not visible) until it
        // gets them.
        internal bool m_HasBounds, m_BoundsFrozen, m_DrawPending;
        /// <summary>Changes when the system gets bounds anew (Simulate, Play On Awake, enabling):
        /// the renderer then recomputes them once even in a frame that keeps bounds.</summary>
        internal int m_BoundsGeneration;

        internal void GiveBounds()
        {
            m_HasBounds = true;
            m_BoundsFrozen = m_DrawPending = false;
            m_BoundsGeneration++;
        }

        internal void CullingFrameUpdate()
        {
            var mode = m_Main.cullingMode;
            var parent = m_SubEmitterParent;
            // A sub-emitter updates while the system emitting into it updates or its own renderer
            // is seen, whatever its own culling mode (6000.0.71f1, tests/projects/particlerender
            // subcull/*: an unseen AlwaysSimulate child stops with its culled parent, a seen child
            // of a culled parent runs on, a Pause child runs on with an AlwaysSimulate parent;
            // particletriggerapi carrier/*: the never played, never seen child of an
            // AlwaysSimulate parent updates).
            bool linked = parent != null && !parent.m_Destroyed && parent.m_GameObject != null && parent != this;
            bool parentSimulates = linked && parent.SimulatesThisFrame(1);
            var culling = linked
                ? (parentSimulates || CullingRenderer is ParticleSystemRenderer own && own.enabled && own.isVisible
                    ? CullingState.Simulated : CullingState.Culled)
                : Culling(mode);
            if (parentSimulates) StartWithParent(parent);
            // A frame that moves time gives the renderer its bounds, culled or not (a culled
            // system's bounds are unmeasured; this keeps it from staying hidden for good).
            if ((m_Main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) > 0f) GiveBounds();
            else
            {
                if (m_HasBounds) m_BoundsFrozen = true;
                else if (!m_DrawPending)
                {
                    m_DrawPending = true;
                    // Nothing to draw: invisible at once, as a renderer turned off.
                    CullingRenderer?.SetVisible(false);
                }
                // A playback's two grace updates are updates that move time (firstvis/paused/play_auto).
                if (Time.frameCount <= m_CullingGraceFrame + 1) m_CullingGraceFrame++;
            }
            if (culling != CullingState.Culled)
            {
                float catchUp = mode == ParticleSystemCullingMode.PauseAndCatchup && m_TotalTime == m_CullingMissedFrom ? m_CullingMissed : 0f;
                // Missed time a frame cannot simulate yet (a zero time scale) waits for the next.
                if (FrameUpdate(catchUp) || catchUp == 0f) m_CullingMissed = 0f;
                // The second update of a playback simulates offscreen too, and its time is caught
                // up again once the system is seen (6000.0.71f1, particlerender cull/catchup_*: the
                // first visible update steps the culled frames and that one).
                if (culling == CullingState.SimulatedOffscreen && mode == ParticleSystemCullingMode.PauseAndCatchup) MissCullingTime();
                return;
            }
            float raw = m_Main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            TrackEmitter(raw);
            m_CollisionEvents.Clear();
            ClearTriggerEvents();
            if (mode != ParticleSystemCullingMode.PauseAndCatchup)
            {
                m_CullingMissed = 0f;
                return;
            }
            MissCullingTime();
        }

        /// <summary>Adds the frame's simulation time to the time PauseAndCatchup catches up (a
        /// script pause or a zero step adds nothing; a new playback or a script call that moved
        /// the playback drops what was missed before).</summary>
        void MissCullingTime()
        {
            float raw = m_Main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float dt = raw * m_Main.simulationSpeed;
            if (!CullingSimulates || !(dt > 0f)) return;
            if (m_CullingMissed == 0f || m_TotalTime != m_CullingMissedFrom)
            {
                m_CullingMissed = 0f;
                m_CullingMissedFrom = m_TotalTime;
            }
            m_CullingMissed += dt;
        }

        enum CullingState : byte { Simulated, Culled, SimulatedOffscreen }

        /// <summary>Whether <paramref name="mode"/> pauses the system this frame (see
        /// <see cref="CullingFrameUpdate"/>).</summary>
        CullingState Culling(ParticleSystemCullingMode mode)
        {
            if (mode == ParticleSystemCullingMode.AlwaysSimulate) return CullingState.Simulated;
            if (mode == ParticleSystemCullingMode.Automatic && !(m_Main.loop && proceduralSimulationSupported)) return CullingState.Simulated;
            int frame = Time.frameCount;
            // A playback's first update simulates; so does its second, offscreen or not.
            if (frame <= m_CullingGraceFrame) return CullingState.Simulated;
            var renderer = CullingRenderer;
            if (renderer == null || !renderer.enabled)
            {
                if (CullingSimulates)
                    Unbound.Runtime.Stubs.Ignored("ParticleSystem.main.cullingMode without a renderer",
                        "keeps simulating a system without an enabled ParticleSystemRenderer (its culling is not decided)");
                return CullingState.Simulated;
            }
            if (renderer.isVisible)
            {
                if (!renderer.CullingBoundsCoverDraw && CullingSimulates)
                    Unbound.Runtime.Stubs.Ignored("Particle culling without covering bounds",
                        "may keep simulating offscreen: Unbound cannot bound this system's drawing (trails, a minimum particle size, stretching by camera velocity), so it counts as visible whenever a camera draws its layer");
                return CullingState.Simulated;
            }
            return frame == m_CullingGraceFrame + 1 ? CullingState.SimulatedOffscreen : CullingState.Culled;
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

        // ------------------------------------------------------------------ storage

        /// <summary>The most particles the system has held (never lowered by deaths or Clear):
        /// BakeTexture keeps room for at least this many records (6000.0.71f1,
        /// tests/projects/particlerender bakecap/300/then1).</summary>
        internal int m_MostParticles;

        void NoteMostParticles()
        {
            if (m_Count > m_MostParticles) m_MostParticles = m_Count;
        }

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
                if (StartsInBatches) ReportUnsupported("ring buffer replacement by sub-emitter births (the oldest particle is replaced; Unity's ring order is not matched)");
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
            int slot = m_Count++;
            NoteMostParticles();
            return slot;
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

    /// <summary>Steps every active particle system once per frame, where Unity does: the
    /// simulation at PreLateUpdate.ParticleSystemBeginUpdateAll, its completion on the main thread
    /// (collision and trigger messages, then stop callbacks and actions) at
    /// PostLateUpdate.ParticleSystemEndUpdateAll, or earlier when a script uses a particle system
    /// in between (6000.0.71f1, tests/projects/destroyphases trigger controls: a system whose
    /// Destroy is delivered at PostLateUpdate's scripting point sends no message unless a script
    /// read it after the update began; tests/projects/particletriggerapi */gone).</summary>
    internal static class ParticleSystemManager
    {
        static readonly List<ParticleSystem> s_Systems = new List<ParticleSystem>();
        static ParticleSystem[] s_Snapshot = new ParticleSystem[16];
        /// <summary>The systems the last update stepped, until their update completes.</summary>
        static readonly List<ParticleSystem> s_Updated = new List<ParticleSystem>();
        static bool s_CompletionPending;
        static ParticleSystem[] s_StoppedRun = new ParticleSystem[16];

        static ParticleSystemManager()
        {
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PreLateUpdate.ParticleSystemBeginUpdateAll), UpdateAll);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.ParticleSystemEndUpdateAll), EndUpdateAll);
        }

        internal static void EnsureRegistered() { }

        internal static IReadOnlyList<ParticleSystem> Systems => s_Systems;

        internal static void Add(ParticleSystem ps) => s_Systems.Add(ps);

        internal static void Remove(ParticleSystem ps) => s_Systems.Remove(ps);

        /// <summary>A script uses a particle system: the frame's update completes first. Measured
        /// for a read of the system itself after the update began (destroyphases' after.particles
        /// sample); that a read of one system completes every system's update is Unbound's
        /// choice (unmeasured).</summary>
        internal static void Sync()
        {
            if (s_CompletionPending) CompleteUpdate();
        }

        internal static void UpdateAll()
        {
            // A loop without the end-of-update system completes the previous frame here.
            CompleteUpdate();
            StopIdleSubEmitters();
            int n = s_Systems.Count;
            if (s_Snapshot.Length < n) s_Snapshot = new ParticleSystem[Math.Max(n, s_Snapshot.Length * 2)];
            s_Systems.CopyTo(s_Snapshot);
            ParticleSystem.AssignRayBudgets(s_Snapshot, n);
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
            FlushEventEmissions();
            for (int i = 0; i < n; i++)
            {
                var ps = s_Snapshot[i];
                if (ps == null) continue;
                try { ps.UpdateModuleLights(); }
                catch (Exception e) { Debug.LogException(e, ps); }
                s_Updated.Add(ps);
            }
            Array.Clear(s_Snapshot, 0, n);
            s_CompletionPending = true;
        }

        /// <summary>The end of the frame's particle update (PostLateUpdate): the update completes
        /// and the sub-emitters their parents started without giving them particles stop.</summary>
        internal static void EndUpdateAll()
        {
            CompleteUpdate();
            StopIdleSubEmitters();
        }

        static readonly List<ParticleSystem> s_StartedByParent = new List<ParticleSystem>();

        internal static void NoteStartedByParent(ParticleSystem ps) => s_StartedByParent.Add(ps);

        /// <summary>Sub-emitters started by their parent's update this frame stop again if they
        /// are still empty (see ParticleSystem.StartWithParent); a script reading them before
        /// this point sees them playing.</summary>
        static void StopIdleSubEmitters()
        {
            int n = s_StartedByParent.Count;
            for (int i = 0; i < n; i++)
            {
                var ps = s_StartedByParent[i];
                if (ps == null || !ps.m_StartedByParent) continue;
                ps.m_StartedByParent = false;
                // A script that played, paused or stopped it since keeps what it set.
                if (ps.m_Count > 0 || ps.HasAliveTrails || !ps.m_IsPlaying || ps.m_IsPaused || !ps.m_StopEmitting) continue;
                ps.m_IsPlaying = false;
                ps.m_StopEmitting = false;
            }
            s_StartedByParent.Clear();
        }

        /// <summary>Completes the last update: every system's collision and trigger messages go
        /// out, then the stop actions run (measured: OnParticleTrigger before
        /// OnParticleSystemStopped of a system updated earlier). A system destroyed meanwhile
        /// sends nothing.</summary>
        internal static void CompleteUpdate()
        {
            if (!s_CompletionPending) return;
            s_CompletionPending = false;
            int n = s_Updated.Count;
            for (int i = 0; i < n; i++)
            {
                var ps = s_Updated[i];
                if (ps == null || !ps.m_StopPending || ps.SubEmittersHaveParticles()) continue;
                ps.m_StopPending = false;
                s_Stopped.Add(ps);
            }
            for (int i = 0; i < n; i++)
            {
                var ps = s_Updated[i];
                if (ps == null || !ps.m_Registered) continue;
                try
                {
                    ps.SendCollisionMessages();
                    ps.SendTriggerMessage();
                }
                catch (Exception e) { Debug.LogException(e, ps); }
            }
            s_Updated.Clear();
            int stopped = s_Stopped.Count;
            if (s_StoppedRun.Length < stopped) s_StoppedRun = new ParticleSystem[Math.Max(stopped, s_StoppedRun.Length * 2)];
            s_Stopped.CopyTo(s_StoppedRun);
            s_Stopped.Clear();
            for (int i = 0; i < stopped; i++)
            {
                var ps = s_StoppedRun[i];
                s_StoppedRun[i] = null;
                try { ps.RunStopAction(); }
                catch (Exception e) { Debug.LogException(e, ps); }
            }
        }

        static bool s_Deferring;
        static readonly List<ParticleSystem> s_Stopped = new List<ParticleSystem>();

        /// <summary>Whether the frame's particle update is running (Simulate and script calls are
        /// outside it).</summary>
        internal static bool Updating => s_Deferring;

        /// <summary>A sub-emitter emission that the frame's update holds until every system has
        /// updated: a Death one, or a Birth one (<see cref="birth"/>) with the ages its children
        /// reach at the end of the frame and its batch in <see cref="s_BirthRequests"/>.</summary>
        internal struct EventEmission
        {
            public ParticleSystem child, parentSystem;
            public ParticleSystem.Particle parent;
            public ParticleSystemSubEmitterProperties properties;
            public int count;
            public bool birth;
            public ParticleSystem.BirthEmission first;
            public int requestStart, requestCount;
        }

        static List<EventEmission> s_EventEmissions = new List<EventEmission>();
        static List<EventEmission> s_Flushing = new List<EventEmission>();
        static readonly List<ParticleSystem.BirthEmission> s_BirthRequests = new List<ParticleSystem.BirthEmission>();
        static readonly List<ParticleSystem.BirthEmission> s_BirthBatch = new List<ParticleSystem.BirthEmission>();

        internal static void HoldEventEmission(EventEmission emission) => s_EventEmissions.Add(emission);

        /// <summary>Holds a Birth emission with its batch.</summary>
        internal static void HoldBirthEmission(ParticleSystem child, ParticleSystem parentSystem, ref ParticleSystem.Particle parent,
            ParticleSystemSubEmitterProperties properties, int count, ParticleSystem.BirthEmission first,
            List<ParticleSystem.BirthEmission> requests)
        {
            int start = s_BirthRequests.Count;
            if (requests != null) s_BirthRequests.AddRange(requests);
            s_EventEmissions.Add(new EventEmission
            {
                child = child, parentSystem = parentSystem, parent = parent, properties = properties, count = count,
                birth = true, first = first, requestStart = start, requestCount = s_BirthRequests.Count - start,
            });
        }

        /// <summary>Emits what the frame's Death and Birth sub-emitters held, after every system has
        /// updated: the children are not aged again in the frame they are born in, at any update
        /// order, and a child that receives particles starts playing (6000.0.71f1 subemitters
        /// played/death and played/finish: remaining lifetime equal to the start lifetime at the
        /// end of the frame, isPlaying true; particlerender birthstart/*).</summary>
        static void FlushEventEmissions()
        {
            if (s_EventEmissions.Count == 0) return;
            (s_Flushing, s_EventEmissions) = (s_EventEmissions, s_Flushing);
            try
            {
                for (int i = 0; i < s_Flushing.Count; i++)
                {
                    var e = s_Flushing[i];
                    if (e.child == null || e.child.m_Destroyed || e.child.m_GameObject == null) continue;
                    try
                    {
                        var parent = e.parent;
                        if (!e.birth)
                        {
                            e.child.EmitEventNow(e.parentSystem, ref parent, e.properties, e.count);
                            continue;
                        }
                        List<ParticleSystem.BirthEmission> requests = null;
                        if (e.requestCount > 0)
                        {
                            s_BirthBatch.Clear();
                            for (int r = 0; r < e.requestCount; r++) s_BirthBatch.Add(s_BirthRequests[e.requestStart + r]);
                            requests = s_BirthBatch;
                        }
                        e.child.EmitBirthNow(e.parentSystem, ref parent, e.properties, e.count, e.first, requests);
                    }
                    catch (Exception ex) { Debug.LogException(ex, e.child); }
                }
            }
            finally
            {
                s_Flushing.Clear();
                s_BirthRequests.Clear();
            }
        }

        /// <summary>Whether a stop action waits for the end of the frame's particle update.</summary>
        internal static bool Defer(ParticleSystem ps)
        {
            if (!s_Deferring) return false;
            s_Stopped.Add(ps);
            return true;
        }
    }
}
