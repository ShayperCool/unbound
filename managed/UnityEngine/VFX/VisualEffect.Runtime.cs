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

        /// <summary>The texture the effect's programs sample its curves and gradients from, null
        /// while the graph bakes none or nothing was baked yet.</summary>
        internal Texture2D BakedTexture => m_State.Baked?.Texture;

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
            // Making the systems sets the renderer up too.
            if (m_Systems.Length == 0) CreateSystems(true);
            else
            {
                UpdateRenderer();
                if (m_Start == StartState.Started) Initialize(true);
            }
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

        /// <summary>The effect's renderer takes what its asset asks of it
        /// (<see cref="VFXRendererSetup"/>).</summary>
        void UpdateRenderer()
        {
            if (m_Renderer || TryGetComponent(out m_Renderer)) VFXRendererSetup.Apply(m_Renderer, m_Asset);
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
            if (data.BuiltRefusal != null) return data.BuiltRefusal;
            for (int i = 0; i < data.Operations.Length; i++)
            {
                var op = data.Operations[i];
                if (!VFXExpressionEvaluator.Supported(op)) return $"the expression operation {op}";
                if (op == VFXExpressionOperation.ReadEventAttribute)
                {
                    var type = (VFXValueType)data.Operands[i * 4 + 3];
                    if (!VFXExpressionEvaluator.ReadableEventAttribute(type)) return $"an event attribute of the type {type} read in a spawn context";
                    int words = data.CpuBuffers.Length > 0 ? (int)data.CpuBuffers[0].stride : 0;
                    if (!VFXExpressionEvaluator.EventAttributeWithin(type, data.Operands[i * 4 + 2], words))
                        return "an event attribute read in a spawn context past the spawn event's attributes";
                }
            }
            if (data.UnplacedBake) return "a curve or gradient baked among the expressions of a spawn event";
            // What the programs read in them is not measured (the effects still ask for them).
            foreach (var op in data.Operations)
                if (op == VFXExpressionOperation.GetBufferFromMainCamera) return "the main camera's buffers read by its programs";
            foreach (var system in data.Systems)
            {
                if (system.type == VFXSystemType.Mesh) return $"the mesh output system '{system.name}'";
                if (system.type != VFXSystemType.Particle) continue;
                const VFXSystemFlag NotYet = VFXSystemFlag.SystemIsRayTraced;
                if ((system.flags & NotYet) != 0) return $"{system.flags & NotYet} of the system '{system.name}'";
                if (VFXParticleSystem.StripRefusal(system) is string strips) return strips;
                if (VFXParticleSystem.BoundsRefusal(system) is string bounds) return bounds;
                // Spawned by GPU events: the graph compiler gives such a system particles that
                // can die and a list of the dead when it has an update or an output context,
                // also when no block ever kills one. One without (a system of an initialize
                // context alone, which can only pass events on) is not measured.
                const VFXSystemFlag Spawned = VFXSystemFlag.SystemReceivedEventGPU, Dying = VFXSystemFlag.SystemHasKill;
                if ((system.flags & (Spawned | Dying)) == Spawned) return $"the system '{system.name}' spawned by GPU events without a list of dead particles";
            }
            return null;
        }

        void CreateSystems(bool sendInitialEvent)
        {
            DestroySystems();
            // A built asset takes its programs before its renderer lists their materials.
            if (m_Asset != null) m_Asset.TakeBuiltPrograms();
            UpdateRenderer();
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
            m_GpuBuffers = new GraphicsBuffer[m_Data.GpuBuffers.Length];
            m_State.Baked = VFXBakedTexture.For(m_Data);
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
                else if (m_Data.Systems[i].type == VFXSystemType.OutputEvent) systems.Add(new VFXOutputEventSystem(this, i, systems));
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
            ReleaseTemporaryBuffers();
            m_State.Baked?.Release();
            m_State.Baked = null;
            m_Systems = Array.Empty<VFXSystem>();
            m_ParticleSystems = Array.Empty<VFXParticleSystem>();
            m_GpuBuffers = Array.Empty<GraphicsBuffer>();
            m_Queue.Clear();
            m_Taken.Clear();
            m_OutputEvents.Clear();
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
        /// <summary>A ring of an asset's temporary buffer: one buffer per frame it keeps, and the
        /// frame it was last asked for.</summary>
        sealed class TemporaryRing
        {
            internal GraphicsBuffer[] Buffers;
            internal uint LastFrame;
        }

        /// <summary>The temporary buffers (an output's last frames: motion vectors), by the
        /// asset's descriptor and a key (0, or a camera's for a buffer kept per camera).</summary>
        readonly Dictionary<(int Desc, int Key), TemporaryRing> m_Temporary = new Dictionary<(int, int), TemporaryRing>();
        readonly List<(int, int)> m_TemporaryExpired = new List<(int, int)>();

        /// <summary>The temporary buffer a task reads or writes for a mapping: of the asset's
        /// descriptor <paramref name="desc"/> and <paramref name="key"/>, a ring of its frame count
        /// of buffers (made on first use, not cleared), the one of the frame
        /// <paramref name="pastFrame"/> frames back, by the frame index effects see
        /// (<see cref="VFXManager.Frame"/>). Matches the Unity 6000.0.71f1 Editor's motion vectors,
        /// tests/projects/vfxoutputs `MovingVectors`.</summary>
        internal GraphicsBuffer TemporaryBuffer(int desc, int key, uint pastFrame)
        {
            var temporary = m_Data.TemporaryBuffers;
            if (desc < 0 || desc >= temporary.Length) return null;
            uint frame = VFXManager.Frame;
            if (!m_Temporary.TryGetValue((desc, key), out var ring))
            {
                var d = temporary[desc];
                uint length = Math.Max(d.frameCount, 1u);
                ring = new TemporaryRing { Buffers = new GraphicsBuffer[length] };
                var target = d.desc.target == GraphicsBuffer.Target.Raw ? GraphicsBuffer.Target.Raw : GraphicsBuffer.Target.Structured;
                int count = (int)Math.Max(d.desc.size, 1u), stride = (int)Math.Max(d.desc.stride, sizeof(uint));
                for (int i = 0; i < ring.Buffers.Length; i++) ring.Buffers[i] = new GraphicsBuffer(target, count, stride);
                m_Temporary[(desc, key)] = ring;
            }
            ring.LastFrame = frame;
            uint n = (uint)ring.Buffers.Length;
            return ring.Buffers[(frame - pastFrame + n) % n];
        }

        /// <summary>Releases the per-camera temporary buffers not asked for in more frames than
        /// they keep (a camera that stopped seeing the effect).</summary>
        internal void ExpireTemporaryBuffers()
        {
            if (m_Temporary.Count == 0) return;
            uint frame = VFXManager.Frame;
            m_TemporaryExpired.Clear();
            foreach (var entry in m_Temporary)
                if (entry.Key.Key != 0 && frame - entry.Value.LastFrame > (uint)entry.Value.Buffers.Length) m_TemporaryExpired.Add(entry.Key);
            foreach (var key in m_TemporaryExpired)
            {
                foreach (var buffer in m_Temporary[key].Buffers) buffer?.Release();
                m_Temporary.Remove(key);
            }
        }

        void ReleaseTemporaryBuffers()
        {
            foreach (var ring in m_Temporary.Values)
                foreach (var buffer in ring.Buffers) buffer?.Release();
            m_Temporary.Clear();
        }

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

        /// <summary>The effect's system of an index of the asset's, null when it has none.</summary>
        internal VFXSystem SystemAt(int index)
        {
            foreach (var system in m_Systems)
                if (system.Index == index) return system;
            return null;
        }

        // ---------------------------------------------------------------------- the events

        public void SendEvent(int eventNameID, VFXEventAttribute eventAttribute)
        {
            // Attributes are laid out for the asset that made them: another asset's are refused
            // before anything is sent (matches the Unity 6000.0.71f1 Editor, tests/projects/vfxdirect).
            if (eventAttribute != null && eventAttribute.vfxAsset != visualEffectAsset)
                throw new InvalidOperationException("Invalid VFXEventAttribute provided to VisualEffect. It has been created with another VisualEffectAsset. Use CreateVFXEventAttribute.");
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
            // Wired straight to an Initialize context: the event is that system's spawn event,
            // with the count its attributes ask for.
            foreach (uint system in e.initSystems ?? Array.Empty<uint>()) (SystemAt((int)system) as VFXParticleSystem)?.TakeEvent(payload);
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
            // The steps simulated at once have a last one of their own; the step that asked
            // for them keeps its place in its frame.
            bool last = m_State.LastStep;
            for (uint i = 0; i < steps; i++)
            {
                m_State.LastStep = i + 1 == steps;
                Step(StepFlags.Evaluate | StepFlags.Simulate);
            }
            m_State.LastStep = last;
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
            // What the evaluation baked reaches the GPU before the step's kernels read it.
            m_State.Baked?.Apply();
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
            // Only this step's events (a step a system failed in hands over none).
            m_OutputEvents.Clear();
            foreach (var system in m_Systems) system.Update(state);
            SendOutputEvents();
            state.TotalTime += state.DeltaTime;
            state.NeedsEvaluation = true;
            m_EverSimulated = true;
        }

        // ----------------------------------------------------------------- output events

        /// <summary>The output events raised in the current step: the event's name id and the
        /// attributes of the spawn context that raised it.</summary>
        readonly List<(int NameId, VFXEventAttribute Source)> m_OutputEvents = new List<(int, VFXEventAttribute)>();
        /// <summary>The one attribute set every output event of the effect is handed (made for
        /// the first event a listener takes, then given each event's attributes).</summary>
        VFXEventAttribute m_cachedEventAttribute;

        internal void RaiseOutputEvent(int nameId, VFXEventAttribute source) => m_OutputEvents.Add((nameId, source));

        /// <summary>After a step's systems updated: each event it raised, in order, to
        /// <see cref="outputEventReceived"/> with the attributes that raised it in the effect's one
        /// output event attribute set. Without a listener the step's events are dropped.</summary>
        void SendOutputEvents()
        {
            if (m_OutputEvents.Count == 0) return;
            if (outputEventReceived != null)
            {
                var attribute = m_cachedEventAttribute ??= CreateVFXEventAttribute();
                for (int i = 0; i < m_OutputEvents.Count; i++)
                {
                    var (nameId, source) = m_OutputEvents[i];
                    attribute.CopyAllFrom(source);
                    // A listener's exception is its own: the next events are still handed over;
                    // the listener is read again for each event, and one removed in the step logs
                    // the call through nothing for each event left (tests/projects/vfx
                    // `events/listener logs`, `events once`).
                    var listener = outputEventReceived;
                    try
                    {
                        if (listener == null) throw MonoExceptions.NullReference();
                        listener(new VFXOutputEventArgs(nameId, attribute));
                    }
                    catch (Exception e) { Debug.LogException(e, this); }
                }
            }
            m_OutputEvents.Clear();
        }

        /// <summary>The union of the systems' bounds, in the effect's space: what the renderer
        /// is culled by. The union is of the boxes' corners (each a centre less and plus its
        /// extents), and the box is taken back from the corners: its centre halfway between
        /// them and its extents half the way, which rounds a single system's box too
        /// (tests/projects/vfxbounds, `renderer=` against `bounds=`).</summary>
        void UpdateBounds()
        {
            bool any = false;
            Vector3 least = default, greatest = default;
            foreach (var system in m_ParticleSystems)
            {
                if (!system.LocalBounds(m_State, out var bounds)) continue;
                Vector3 min = bounds.center - bounds.extents, max = bounds.center + bounds.extents;
                least = any ? Vector3.Min(least, min) : min;
                greatest = any ? Vector3.Max(greatest, max) : max;
                any = true;
            }
            m_HasBounds = any;
            m_LocalBounds = any ? VFXParticleSystem.Box(least, greatest) : default;
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
            VFXParticleSystem.CollectDraws(m_ParticleSystems, renderer, camera, into);
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
            ExpireTemporaryBuffers();
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
            for (uint step = 0; step < steps; step++)
            {
                state.LastStep = step + 1 == steps;
                Step(flags);
            }
            m_TotalTime = state.TotalTime;
            m_AdvanceOneFrame = false;
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

        /// <summary>The bounds computed from a system's particles, grown by its padding, in the
        /// effect's space: zero for a system that computes none, and for no system.</summary>
        internal Bounds GetComputedBounds(int nameID) => SystemNamed(nameID) is VFXParticleSystem system ? system.ComputedBounds : default;

        internal Bounds GetComputedBounds(string name) => GetComputedBounds(Shader.PropertyToID(name));

        /// <summary>A system's bounds padding as the effect has it now.</summary>
        internal Vector3 GetCurrentBoundsPadding(int nameID) => SystemNamed(nameID) is VFXParticleSystem system ? system.BoundsPadding : Vector3.zero;

        internal Vector3 GetCurrentBoundsPadding(string name) => GetCurrentBoundsPadding(Shader.PropertyToID(name));

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
