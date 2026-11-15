using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;

namespace UnityEngine.VFX
{
    // The running effect: its systems, its event queue and its steps. An enabled effect with an
    // asset has systems; each frame the manager gives it the frame's time and it runs the steps
    // its asset's update mode asks for. The rules match the Unity 6000.0.71f1 Editor
    // (tests/projects/vfx).
    public partial class VisualEffect
    {
        /// <summary>A step no longer than this does not advance the effect (a millionth of a
        /// second, as a float; a rate of zero or a pause gives such steps: tests/projects/vfx
        /// `sim/control`).</summary>
        const float MinDeltaTime = 9.99999997e-07f;

        /// <summary>What a step does, as <see cref="StepFlags"/> says.</summary>
        [Flags]
        enum StepFlags
        {
            /// <summary>Evaluate the expressions and take the queued events.</summary>
            Events = 1,
            /// <summary>Evaluate the expressions if a step ran since.</summary>
            Evaluate = 2,
            Bounds = 4,
            Simulate = 8,
            All = Events | Evaluate | Bounds | Simulate,
        }

        enum QueuedKind { Simulate, Event, Initial }

        struct Queued
        {
            internal QueuedKind Kind;
            internal int NameId;
            /// <summary>The event's attributes as they were when it was sent; null for the
            /// asset's own.</summary>
            internal uint[] Payload;
            internal uint Steps;
            internal float DeltaTime;
        }

        /// <summary>Where the effect is in its start: nothing to start, the initial event and
        /// prewarm still to run (which a culled effect runs too), or started.</summary>
        enum StartState { None, Pending, Started }

        VFXSystem[] m_Systems = Array.Empty<VFXSystem>();
        VFXParticleSystem[] m_ParticleSystems = Array.Empty<VFXParticleSystem>();
        GraphicsBuffer[] m_GpuBuffers = Array.Empty<GraphicsBuffer>();
        readonly VisualEffectState m_State = new VisualEffectState();
        // Events are taken at the start of a step from the queue filled before it; what a step
        // sends waits for the next.
        List<Queued> m_Queue = new List<Queued>(), m_Taken = new List<Queued>();
        StartState m_Start;
        float m_TotalTime;
        uint m_Seed;
        bool m_AdvanceOneFrame, m_Registered;
        /// <summary>Whether the effect ever ran a step (starting over does not undo it).</summary>
        bool m_EverSimulated;
        Bounds m_LocalBounds;
        bool m_HasBounds;
        /// <summary>What the graph uses that Unbound does not run yet; such an effect does not simulate.</summary>
        string m_Unsupported;

        /// <summary>The seeds of effects that reset their seed on play: a sequence of its own,
        /// started from the clock, or from UNBOUND_RANDOM_SEED so that scripted runs repeat.</summary>
        static Random.State s_Seeds = Random.State.FromSeed(
            int.TryParse(Environment.GetEnvironmentVariable("UNBOUND_RANDOM_SEED"), out int pinned) ? pinned : Environment.TickCount);

        static readonly HashSet<int> s_WarnedUnsupported = new HashSet<int>();

        internal VisualEffectCompiledData Data => m_Data;
        internal VisualEffectValues Values => m_Values;

        /// <summary>The effect's time: the sum of the steps it ran.</summary>
        internal float time => m_TotalTime;

        // --------------------------------------------------------------------- the systems

        /// <summary>An effect enabled again after it started starts over; one that has not taken
        /// its initial event yet goes on waiting for it.</summary>
        internal override void OnEngineEnable()
        {
            VFXManager.Add(this);
            m_Registered = true;
            EnableRenderer(true);
            if (m_Systems.Length == 0) CreateSystems(true);
            else if (m_Start == StartState.Started) Initialize(true);
        }

        /// <summary>A disabled effect, or one on an inactive object, stops updating and keeps its
        /// systems (tests/projects/vfx, `sim/disabled`, `sim/inactive`, `sim/toggle*`).</summary>
        internal override void OnEngineDisable()
        {
            VFXManager.Remove(this);
            m_Registered = false;
            EnableRenderer(false);
        }

        /// <summary>The effect's renderer is enabled with the effect and disabled with it, also
        /// when it is the object that goes inactive (`sim/toggle*/renderer`): no camera takes
        /// the renderer of an effect that is off.</summary>
        void EnableRenderer(bool enabled)
        {
            if (m_Renderer || TryGetComponent(out m_Renderer)) m_Renderer.enabled = enabled;
        }

        internal override void OnEngineDestroy()
        {
            VFXManager.Remove(this);
            m_Registered = false;
            DestroySystems();
        }

        /// <summary>What a graph uses that the runtime does not do yet, null when it runs whole.</summary>
        internal static string Unsupported(VisualEffectCompiledData data)
        {
            foreach (var op in data.Operations)
                if (!VFXExpressionEvaluator.Supported(op)) return $"the expression operation {op}";
            foreach (var system in data.Systems)
            {
                if (system.type == VFXSystemType.Mesh) return $"the mesh output system '{system.name}'";
                if (system.type == VFXSystemType.OutputEvent) return $"the output event '{system.name}'";
                if (system.type != VFXSystemType.Particle) continue;
                const VFXSystemFlag NotYet = VFXSystemFlag.SystemHasStrips | VFXSystemFlag.SystemReceivedEventGPU | VFXSystemFlag.SystemHasDirectLink
                    | VFXSystemFlag.SystemNeedsComputeBounds | VFXSystemFlag.SystemAutomaticBounds | VFXSystemFlag.SystemIsRayTraced;
                if ((system.flags & NotYet) != 0) return $"{system.flags & NotYet} of the system '{system.name}'";
            }
            foreach (var e in data.Events)
                if (e.initSystems != null && e.initSystems.Length > 0) return $"the event '{e.name}' wired to an Initialize context";
            return null;
        }

        void CreateSystems(bool sendInitialEvent)
        {
            DestroySystems();
            // Systems are made once the effect's object is active (enabled or not), and kept.
            if (m_Asset == null || m_Data.Systems.Length == 0 || !(m_Registered || gameObject.activeInHierarchy)) return;
            m_Unsupported = Unsupported(m_Data);
            if (m_Unsupported != null)
            {
                // Said once per asset: every effect of it is in the same position.
                if (s_WarnedUnsupported.Add(m_Asset.GetInstanceID()))
                    Debug.LogWarning($"Unbound: VisualEffect '{name}' ({m_Asset.name}) is not simulated: it uses {m_Unsupported}, which Unbound does not run yet. `unbound doctor` lists the project's effects.", this);
                return;
            }
            if (m_Asset.m_Settings.Renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) WarnShadowsNotCast();
            m_GpuBuffers = new GraphicsBuffer[m_Data.GpuBuffers.Length];
            var systems = new List<VFXSystem>(m_Data.Systems.Length);
            var particles = new List<VFXParticleSystem>();
            // Spawners first, as the compiler lists them: a particle system finds the ones that feed it.
            for (int i = 0; i < m_Data.Systems.Length; i++)
            {
                if (m_Data.Systems[i].type == VFXSystemType.Spawner) systems.Add(new VFXSpawnerSystem(this, i));
                else if (m_Data.Systems[i].type == VFXSystemType.Particle)
                {
                    var system = new VFXParticleSystem(this, i, systems);
                    systems.Add(system);
                    particles.Add(system);
                }
            }
            foreach (var system in systems) (system as VFXSpawnerSystem)?.Link(systems);
            m_Systems = systems.ToArray();
            m_ParticleSystems = particles.ToArray();
            Initialize(sendInitialEvent);
        }

        void DestroySystems()
        {
            foreach (var system in m_Systems) system.Release();
            foreach (var buffer in m_GpuBuffers) buffer?.Release();
            m_Systems = Array.Empty<VFXSystem>();
            m_ParticleSystems = Array.Empty<VFXParticleSystem>();
            m_GpuBuffers = Array.Empty<GraphicsBuffer>();
            m_Queue.Clear();
            m_Taken.Clear();
            m_Start = StartState.None;
            m_HasBounds = false;
        }

        /// <summary>The effect as new: no events waiting, every system reset, time zero, a seed
        /// (the start seed, or a new one for an effect that resets its seed on play) and, when
        /// asked, the initial event and the prewarm queued.</summary>
        void Initialize(bool sendInitialEvent)
        {
            m_Queue.Clear();
            m_Taken.Clear();
            foreach (var system in m_Systems) system.Reset();
            m_TotalTime = 0f;
            m_Seed = m_ResetSeedOnPlay ? NewSeed() : m_StartSeed;
            m_State.Random = Random.State.FromSeed(unchecked((int)m_Seed));
            m_Start = StartState.Pending;
            if (sendInitialEvent) m_Queue.Add(new Queued { Kind = QueuedKind.Initial, NameId = initialEventID });
        }

        static uint NewSeed()
        {
            s_Seeds.NextValue();
            return s_Seeds.S3;
        }

        /// <summary>A GPU buffer of the asset's compiled graph, made when first used and made
        /// anew (its contents lost) when more elements are asked for than it has.</summary>
        internal GraphicsBuffer GpuBuffer(int index, int minCount)
        {
            var buffer = m_GpuBuffers[index];
            if (buffer != null && buffer.count >= minCount) return buffer;
            var desc = m_Data.GpuBuffers[index];
            int count = Math.Max(Math.Max((int)desc.size, minCount), 1);
            buffer?.Release();
            var target = desc.target == GraphicsBuffer.Target.Raw ? GraphicsBuffer.Target.Raw : GraphicsBuffer.Target.Structured;
            return m_GpuBuffers[index] = new GraphicsBuffer(target, count, (int)Math.Max(desc.stride, sizeof(uint)));
        }

        // ---------------------------------------------------------------------- the events

        public void SendEvent(int eventNameID, VFXEventAttribute eventAttribute)
        {
            // Each play of an effect that resets its seed on play takes a new seed.
            if (m_ResetSeedOnPlay && eventNameID == VisualEffectAsset.PlayEventID) m_Seed = NewSeed();
            if (m_Systems.Length == 0) return;
            m_Queue.Add(new Queued { Kind = QueuedKind.Event, NameId = eventNameID, Payload = eventAttribute == null ? null : (uint[])eventAttribute.Words.Clone() });
        }

        public void SendEvent(string eventName, VFXEventAttribute eventAttribute) => SendEvent(Shader.PropertyToID(eventName), eventAttribute);
        public void SendEvent(int eventNameID) => SendEvent(eventNameID, null);
        public void SendEvent(string eventName) => SendEvent(Shader.PropertyToID(eventName), null);

        public void Play(VFXEventAttribute eventAttribute) => SendEvent(VisualEffectAsset.PlayEventID, eventAttribute);
        public void Play() => SendEvent(VisualEffectAsset.PlayEventID);
        public void Stop(VFXEventAttribute eventAttribute) => SendEvent(VisualEffectAsset.StopEventID, eventAttribute);
        public void Stop() => SendEvent(VisualEffectAsset.StopEventID);

        public void Reinit() => Reinit(true);

        /// <summary>Starts the effect over: its systems reset and its exposed values kept.</summary>
        internal void Reinit(bool sendInitialEventAndPrewarm = true)
        {
            if (m_Systems.Length == 0)
            {
                CreateSystems(sendInitialEventAndPrewarm);
                return;
            }
            Initialize(sendInitialEventAndPrewarm);
        }

        /// <summary>A paused effect runs its next frame as if it were not.</summary>
        public void AdvanceOneFrame() => m_AdvanceOneFrame = true;

        /// <summary>Queues steps of a given length, run when the effect next takes its events.</summary>
        public void Simulate(float stepDeltaTime, uint stepCount = 1U)
        {
            if (stepCount == 0 || m_Systems.Length == 0) return;
            m_Queue.Add(new Queued { Kind = QueuedKind.Simulate, Steps = stepCount, DeltaTime = stepDeltaTime });
        }

        /// <summary>The event's start, stop and direct systems are told, with the event's
        /// attributes (the asset's initial ones for an event sent without any).</summary>
        void ProcessEvent(int nameId, uint[] payload)
        {
            int index = Array.IndexOf(m_Data.EventIds, nameId);
            if (index < 0) return;
            var e = m_Data.Events[index];
            payload ??= m_Data.CpuInitialWords.Length > 0 ? m_Data.CpuInitialWords[0] : Array.Empty<uint>();
            foreach (uint system in e.startSystems ?? Array.Empty<uint>()) Spawner(system)?.OnPlay(payload, m_State);
            foreach (uint system in e.stopSystems ?? Array.Empty<uint>()) Spawner(system)?.OnStop(payload, m_State);
        }

        VFXSpawnerSystem Spawner(uint index)
        {
            foreach (var system in m_Systems)
                if (system.Index == index) return system as VFXSpawnerSystem;
            return null;
        }

        /// <summary>Runs steps of a given length now, whatever the frame's time.</summary>
        void ProcessSimulate(uint steps, float deltaTime)
        {
            if (steps == 0) return;
            var state = m_State;
            float delta = state.DeltaTime, unscaled = state.UnscaledDeltaTime;
            state.DeltaTime = state.UnscaledDeltaTime = deltaTime;
            state.NeedsEvaluation = true;
            for (uint i = 0; i < steps; i++) Step(StepFlags.Evaluate | StepFlags.Simulate);
            state.DeltaTime = delta;
            state.UnscaledDeltaTime = unscaled;
        }

        void TakeEvents()
        {
            if (m_Queue.Count == 0) return;
            (m_Queue, m_Taken) = (m_Taken, m_Queue);
            m_Queue.Clear();
            for (int i = 0; i < m_Taken.Count; i++)
            {
                var item = m_Taken[i];
                switch (item.Kind)
                {
                    case QueuedKind.Simulate:
                        ProcessSimulate(item.Steps, item.DeltaTime);
                        break;
                    case QueuedKind.Initial:
                        ProcessEvent(item.NameId, null);
                        ProcessSimulate(m_Asset.m_Settings.PreWarmStepCount, m_Asset.m_Settings.PreWarmDeltaTime);
                        m_Start = StartState.Started;
                        break;
                    case QueuedKind.Event:
                        ProcessEvent(item.NameId, item.Payload);
                        break;
                }
            }
            m_Taken.Clear();
        }

        // ----------------------------------------------------------------------- the steps

        void EvaluateExpressions()
        {
            VFXExpressionEvaluator.Evaluate(m_Data, m_Values, 0, m_Data.Expressions.Length, m_State, null);
            m_State.NeedsEvaluation = false;
        }

        void Step(StepFlags flags)
        {
            var state = m_State;
            if ((flags & StepFlags.Events) != 0)
            {
                if (state.NeedsEvaluation) EvaluateExpressions();
                TakeEvents();
            }
            if ((flags & StepFlags.Evaluate) != 0 && state.NeedsEvaluation) EvaluateExpressions();
            if ((flags & StepFlags.Bounds) != 0) UpdateBounds();
            if ((flags & StepFlags.Simulate) == 0) return;
            foreach (var system in m_Systems) system.Update(state);
            state.TotalTime += state.DeltaTime;
            state.NeedsEvaluation = true;
            m_EverSimulated = true;
        }

        /// <summary>The union of the systems' bounds, in the effect's space: what the renderer
        /// is culled by.</summary>
        void UpdateBounds()
        {
            bool any = false;
            var union = default(Bounds);
            foreach (var system in m_ParticleSystems)
            {
                if (!system.LocalBounds(m_State, out var bounds)) continue;
                if (any) union.Encapsulate(bounds);
                else union = bounds;
                any = true;
            }
            m_HasBounds = any;
            m_LocalBounds = union;
        }

        /// <summary>The effect's bounds for its renderer; false while no system has any.</summary>
        internal bool RendererBounds(out Bounds bounds)
        {
            bounds = m_LocalBounds;
            return m_HasBounds;
        }

        /// <summary>The steps of a frame and their length, by the asset's update mode: the
        /// frame's delta time in one step, or the frame's fixed steps (as one step of their
        /// total, or each on its own when the mode asks for exact steps; a frame that holds
        /// none still runs one step of no time).</summary>
        internal static void UpdateTime(VFXUpdateMode mode, in VFXManager.FrameTime time, out uint steps, out float deltaTime)
        {
            bool unscaled = (mode & VFXUpdateMode.IgnoreTimeScale) != 0;
            steps = 1;
            if ((mode & VFXUpdateMode.DeltaTime) != 0)
            {
                deltaTime = unscaled ? time.UnscaledDeltaTime : time.DeltaTime;
                return;
            }
            uint fixedSteps = unscaled ? time.UnscaledFixedSteps : time.FixedSteps;
            deltaTime = unscaled ? time.UnscaledFixedTime : time.FixedTime;
            if ((mode & VFXUpdateMode.ExactFixedTimeStep) == 0) return;
            if (fixedSteps == 0) deltaTime = 0f;
            else
            {
                steps = fixedSteps;
                deltaTime = VFXManager.fixedTimeStep;
            }
        }

        /// <summary>The draws of the effect's outputs for a camera that sees its renderer.</summary>
        internal void CollectDraws(Renderer renderer, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!isActiveAndEnabled) return;
            foreach (var system in m_ParticleSystems) system.CollectDraws(renderer, camera, into);
        }

        /// <summary>Whether no camera sees the effect: none took its renderer in the last frame
        /// (so an effect that was off in that frame is not seen in the update after it is
        /// enabled, and one disabled and enabled within a frame is: `sim/toggle`, `sim/toggle
        /// at once`). A renderer no camera ever took counts as seen until the effect first
        /// simulated, so a new effect's first frame runs whole (measured for new effects,
        /// tests/projects/vfx `sim/behind*`; an effect started over out of view is not measured).</summary>
        bool ComputeCulled()
        {
            if (!m_Renderer && !TryGetComponent(out m_Renderer)) return true;
            if (m_Renderer.m_RenderedFrame < 0) return m_EverSimulated;
            return m_Renderer.m_RenderedFrame < Time.frameCount - 1;
        }

        VFXRenderer m_Renderer;

        /// <summary>Whether no camera saw the effect when it last updated; an effect that has
        /// not updated yet counts as culled.</summary>
        public bool culled => m_Culled;

        bool m_Culled = true;

        /// <summary>The frame's steps of the effect.</summary>
        internal void UpdateEffect(in VFXManager.FrameTime time)
        {
            if (m_Systems.Length == 0) return;
            UpdateTime(m_Asset.m_Settings.UpdateMode, time, out uint steps, out float unscaledDelta);
            float delta = unscaledDelta * m_PlayRate;
            if (delta <= MinDeltaTime || (m_Pause && !m_AdvanceOneFrame)) delta = 0f;

            // A culled effect keeps taking its events and simulating unless its asset culls the
            // simulation, and keeps its bounds up to date unless its asset culls that; the
            // start of an effect (its initial event and prewarm) always runs.
            var culling = m_Asset.m_Settings.CullingFlags;
            bool isCulled = m_Culled = ComputeCulled(), starting = m_Start == StartState.Pending;
            bool simulate = !isCulled || (culling & VFXCullingFlags.CullSimulation) == 0;
            bool bounds = !isCulled || (culling & VFXCullingFlags.CullBoundsUpdate) == 0 || starting;
            var flags = (StepFlags)0;
            if (simulate || starting) flags |= StepFlags.Events | StepFlags.Evaluate;
            if (bounds) flags |= StepFlags.Evaluate | StepFlags.Bounds;
            if (simulate) flags |= StepFlags.Simulate;

            var state = m_State;
            var transform = this.transform;
            state.LocalToWorld = transform.localToWorldMatrix;
            state.WorldToLocal = transform.worldToLocalMatrix;
            state.DeltaTime = delta;
            state.UnscaledDeltaTime = unscaledDelta;
            state.TotalTime = m_TotalTime;
            state.PlayRate = m_PlayRate;
            state.FrameIndex = VFXManager.Frame;
            state.Seed = m_Seed;
            state.NeedsEvaluation = true;
            for (uint step = 0; step < steps; step++) Step(flags);
            m_TotalTime = state.TotalTime;
            m_AdvanceOneFrame = false;
            foreach (var system in m_ParticleSystems) system.UpdateReadback();
        }

        // ----------------------------------------------------------------------- the state

        /// <summary>The particles alive in all systems, as each system counts them (a system
        /// that kills particles reads its count back from the GPU every so often, and counts
        /// -1 until its first answer).</summary>
        public int aliveParticleCount
        {
            get
            {
                int count = 0;
                foreach (var system in m_ParticleSystems) count += system.AliveCount;
                return count;
            }
        }

        public bool HasAnySystemAwake()
        {
            foreach (var system in m_ParticleSystems)
                if (!system.Sleeping) return true;
            return false;
        }

        /// <summary>What both system getters say of a name that is not a system of their kind.</summary>
        const string NoSuchSystem = "System \"{0}\" does not exist or is not a particle system";

        VFXSystem SystemNamed(int nameID)
        {
            int index = m_Data.SystemIndex(nameID);
            if (index >= 0)
                foreach (var system in m_Systems)
                    if (system.Index == index) return system;
            return null;
        }

        /// <summary>Whether the asset has a particle system of a name. Both system getters
        /// answer nothing for a name they do not find among the running systems, and throw
        /// only when the asset has a particle system of that name (Unity 6000.0.71f1).</summary>
        bool AssetHasParticleSystem(int nameID)
        {
            int index = m_Data.SystemIndex(nameID);
            return index >= 0 && m_Data.Systems[index].type == VFXSystemType.Particle;
        }

        public VFXParticleSystemInfo GetParticleSystemInfo(int nameID)
        {
            if (SystemNamed(nameID) is VFXParticleSystem system)
            {
                system.LocalBounds(m_State, out var bounds);
                return new VFXParticleSystemInfo((uint)Math.Max(0, system.AliveCount), system.Capacity, system.Sleeping, bounds);
            }
            if (AssetHasParticleSystem(nameID)) throw new ArgumentException(string.Format(NoSuchSystem, Shader.IdToName(nameID)));
            return default;
        }

        public VFXParticleSystemInfo GetParticleSystemInfo(string name) => GetParticleSystemInfo(Shader.PropertyToID(name));

        public void GetSpawnSystemInfo(int nameID, VFXSpawnerState spawnState)
        {
            if (spawnState == null) throw new NullReferenceException("GetSpawnSystemInfo expects a non null VFXSpawnerState.");
            if (SystemNamed(nameID) is VFXSpawnerSystem system) system.CopyStateTo(spawnState);
            else if (AssetHasParticleSystem(nameID)) throw new ArgumentException(string.Format(NoSuchSystem, Shader.IdToName(nameID)));
        }

        public VFXSpawnerState GetSpawnSystemInfo(int nameID)
        {
            var state = new VFXSpawnerState();
            GetSpawnSystemInfo(nameID, state);
            return state;
        }

        public VFXSpawnerState GetSpawnSystemInfo(string name) => GetSpawnSystemInfo(Shader.PropertyToID(name));
    }
}
