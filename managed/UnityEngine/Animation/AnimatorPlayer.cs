using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>A state an Animator layer is playing.</summary>
    internal sealed class StateRun
    {
        internal StateDef Def;
        /// <summary>Normalized time, unwrapped: the integer part counts loops.</summary>
        internal double Normalized;
        internal double Previous;
        /// <summary>Where the root motion of the update the state was entered in starts: a whole
        /// update's step before the time it was entered at (see <see cref="AnimatorPlayer.Enter"/>).
        /// Null in later updates, whose motion starts at <see cref="Previous"/>.</summary>
        internal double? MotionPrevious;
        /// <summary>Entered since the last update: events at its start time fire.</summary>
        internal bool Fresh = true;
        /// <summary>Events were fired for the state's first evaluation (an Animator.Update(0) that
        /// enters no state yet still fires events at the start time).</summary>
        internal bool EventsStarted;
        /// <summary>OnStateEnter was queued this update (OnStateUpdate waits for the next one).</summary>
        internal bool EnteredThisUpdate;

        internal StateRun(StateDef def, double normalized)
        {
            Def = def;
            Normalized = normalized;
            Previous = normalized;
        }
    }

    internal sealed class TransitionRun
    {
        internal TransitionDef Def;
        internal float Duration;
        internal float NominalDuration;
        internal bool FixedDuration;
        internal double Elapsed;
        internal float Progress;
        internal bool AnyState;
        internal int FullPathHash, NameHash, UserNameHash;
    }

    internal sealed class LayerRun
    {
        internal LayerDef Def;
        float m_Weight;
        /// <summary>The weight GetLayerWeight reports. Evaluation ignores negative weights and keeps
        /// the last weight that was not (<see cref="Evaluated"/>).</summary>
        internal float Weight
        {
            get => m_Weight;
            set
            {
                m_Weight = value;
                if (value >= 0f) Evaluated = value;
            }
        }
        internal float Evaluated;
        internal StateRun Current;
        internal StateRun Next;
        internal TransitionRun Transition;
        internal PendingPlay Pending;
        /// <summary>The layer's last values (an additive layer's: differences): what its states
        /// without a motion and without write defaults keep. Held marks the channels it has.</summary>
        internal float[] Last = Array.Empty<float>();
        internal bool[] Held = Array.Empty<bool>();
        /// <summary>The layer's own root motion of its last evaluation, which its holding states
        /// keep (<see cref="Kept"/>, see <see cref="AnimatorPlayer.HoldWeight"/>), and of this
        /// evaluation (<see cref="Own"/>, kept when the next evaluation starts).</summary>
        internal HeldMotion Kept, Own;
        /// <summary>The source of a transition that completed in this update (see
        /// <see cref="AnimatorPlayer.BuildLayerPoses"/>); null otherwise.</summary>
        internal StateRun Completed;
        /// <summary>The current and next states before this update's step: the states an
        /// update's OnStateMove and OnStateIK still reach after they exit, and the order of its
        /// state callbacks (see <see cref="AnimatorPlayer.OrderCallbacks"/>).</summary>
        internal StateRun PreviousCurrent, PreviousNext;
    }

    /// <summary>A layer's own root motion of an evaluation: its generic step in the root's frame
    /// and its blended humanoid sample.</summary>
    internal struct HeldMotion
    {
        internal Vector3 Delta;
        internal Quaternion Turn;
        internal bool HasStep;
        internal Unbound.Runtime.Humanoid.HumanRootMotionSample Human;
        internal bool HasHuman;
    }

    /// <summary>Play / CrossFade requests: they take effect at the layer's next update, as in Unity.</summary>
    internal sealed class PendingPlay
    {
        internal StateDef State;
        internal float Time;
        internal bool FixedTime;
        internal bool CrossFade;
        internal float Duration;
        internal bool FixedDuration;
        internal float TransitionTime;
    }

    /// <summary>
    /// The playback of one Animator's controller: parameters, each layer's state machine (states,
    /// transitions, exit times, cross-fades), blend trees, layer blending, write defaults, animation
    /// events and StateMachineBehaviour callbacks.
    /// </summary>
    internal sealed partial class AnimatorPlayer
    {
        readonly Animator m_Animator;
        internal readonly RuntimeAnimatorController Controller;
        internal readonly ControllerGraph Graph;
        internal readonly AnimationBindings Bindings;
        internal readonly float[] Floats;
        internal readonly int[] Ints;
        internal readonly bool[] Bools;
        readonly float[] m_Velocity;
        internal readonly LayerRun[] Layers;
        /// <summary>By source layer: the layers synced to it with "Timing" on, in layer order (null
        /// where none). Their motions share the source state's duration (see <see cref="TimedLength"/>).</summary>
        readonly int[][] m_TimingLayers;
        int m_OverrideVersion;

        readonly Dictionary<StateMachineBehaviour, StateMachineBehaviour> m_Instances = new Dictionary<StateMachineBehaviour, StateMachineBehaviour>();
        internal readonly List<StateMachineBehaviour> AllBehaviours = new List<StateMachineBehaviour>();
        List<StateCallback> m_Callbacks = new List<StateCallback>();
        readonly Dictionary<string, int> m_ParameterIndex = new Dictionary<string, int>();
        readonly Dictionary<(Motion, bool), (bool[] Floats, bool[] Objects, int FloatCount, int ObjectCount)> m_MotionMasks = new Dictionary<(Motion, bool), (bool[], bool[], int, int)>();
        readonly HashSet<AnimationClip> m_WarnedLegacy = new HashSet<AnimationClip>();
        AnimationPose[] m_Poses = Array.Empty<AnimationPose>();
        LayerPose[] m_LayerPoses = Array.Empty<LayerPose>();
        /// <summary>The values this Animator last wrote (where Held): what states without write
        /// defaults keep, whatever game code set since (measured).</summary>
        float[] m_Written = Array.Empty<float>();
        bool[] m_WrittenHeld = Array.Empty<bool>();
        (bool[] Floats, bool[] Objects, int FloatCount)[] m_LayerMasks;
        /// <summary>The clips an evaluation sampled, each with its weight and, for a humanoid clip
        /// whose root motion the sampling already evaluated, its place in
        /// <see cref="m_HumanClipSamples"/> (−1: none).</summary>
        readonly List<(LayerRun Layer, int LayerIndex, StateRun Run, AnimationClip Clip, float Weight, int HumanSample)> m_Contributions =
            new List<(LayerRun, int, StateRun, AnimationClip, float, int)>();

        /// <summary>States left within this update through a transition that completed at once
        /// (no duration): the stretch they played before leaving, up to the exit point, still fires
        /// its events (measured: an event at a clip's end fires as an exit time of 1 leaves it).</summary>
        List<(LayerRun Layer, StateRun Run, double End)> m_Left = new List<(LayerRun, StateRun, double)>();
        // Spare lists for FireEvents (taken while it runs, so a reentrant update allocates its own).
        List<(LayerRun Layer, StateRun Run, double End)> m_LeftSpare = new List<(LayerRun, StateRun, double)>();
        List<(LayerRun Layer, int LayerIndex, StateRun Run, AnimationClip Clip, float Weight, int HumanSample)> m_ContributionsSpare =
            new List<(LayerRun, int, StateRun, AnimationClip, float, int)>();

        /// <summary>Whether this is the Animator's own player (its speed applies, Animator API
        /// calls reach it) rather than an AnimatorControllerPlayable's.</summary>
        internal readonly bool Attached = true;

        /// <summary>The controller playable this player runs for (state machine behaviours get it).</summary>
        internal AnimatorControllerPlayable Playable;

        /// <summary>A controller playable's player: it animates <paramref name="animator"/>'s
        /// hierarchy through <paramref name="bindings"/> without becoming the Animator's player.</summary>
        internal AnimatorPlayer(Animator animator, RuntimeAnimatorController controller, ControllerGraph graph, AnimationBindings bindings)
            : this(animator, controller, graph, bindings, attached: false) { }

        internal AnimatorPlayer(Animator animator, RuntimeAnimatorController controller, ControllerGraph graph)
            : this(animator, controller, graph, null, attached: true) { }

        AnimatorPlayer(Animator animator, RuntimeAnimatorController controller, ControllerGraph graph, AnimationBindings bindings, bool attached)
        {
            Attached = attached;
            m_Animator = animator;
            Controller = controller;
            Graph = graph;
            int n = graph.Parameters.Length;
            Floats = new float[n];
            Ints = new int[n];
            Bools = new bool[n];
            m_Velocity = new float[n];
            for (int i = 0; i < n; i++)
            {
                var p = graph.Parameters[i];
                Floats[i] = p.DefaultFloat;
                Ints[i] = p.DefaultInt;
                Bools[i] = p.DefaultBool;
            }
            Bindings = bindings ?? new AnimationBindings(animator.m_GameObject);
            // Clips may animate this Animator's own parameters: binding them asks the Animator for
            // its player, which is this one from here on.
            if (attached) animator.m_Player = this;
            BindClips();
            m_OverrideVersion = controller.OverrideVersion;

            Layers = new LayerRun[graph.Layers.Length];
            m_LayerMasks = new (bool[], bool[], int)[graph.Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
                Layers[i] = new LayerRun { Def = graph.Layers[i], Weight = graph.Layers[i].DefaultWeight };
            m_TimingLayers = TimingLayers(controller, graph);
            m_MayHold = AnyHoldingState();
            foreach (var layer in Layers)
            {
                if (layer.Def.Root == null) continue;
                var state = StartState(layer);
                if (state == null) continue;
                var run = new StateRun(state, 0.0);
                SetCurrent(layer, run);
                QueueEnter(layer, run);
            }
        }

        void BindClips()
        {
            foreach (var original in Graph.Clips)
            {
                var clip = Controller.MapClip(original);
                if (clip != null && !clip.m_Legacy) Bindings.Bind(clip);
            }
        }

        // ------------------------------------------------------------------ parameters

        internal int ParameterIndex(string name)
        {
            if (name == null) return -1;
            if (!m_ParameterIndex.TryGetValue(name, out var index))
                m_ParameterIndex[name] = index = Graph.ParameterIndex(name);
            return index;
        }

        internal int ParameterIndex(int hash) => Graph.ParameterByHash.TryGetValue(hash, out var i) ? i : -1;

        internal float GetAsFloat(int i)
        {
            switch (Graph.Parameters[i].Type)
            {
                case AnimatorControllerParameterType.Float: return Floats[i];
                case AnimatorControllerParameterType.Int: return Ints[i];
                default: return Bools[i] ? 1f : 0f;
            }
        }

        internal void SetFromCurve(int i, float value)
        {
            switch (Graph.Parameters[i].Type)
            {
                case AnimatorControllerParameterType.Float: Floats[i] = value; break;
                case AnimatorControllerParameterType.Int: Ints[i] = (int)Math.Round(value); break;
                default: Bools[i] = value >= 0.5f; break;
            }
        }

        internal void SetFloatDamped(int i, float value, float dampTime, float deltaTime)
        {
            if (dampTime <= 0f || deltaTime <= 0f)
            {
                Floats[i] = value;
                m_Velocity[i] = 0f;
                return;
            }
            Floats[i] = Mathf.SmoothDamp(Floats[i], value, ref m_Velocity[i], dampTime, float.PositiveInfinity, deltaTime);
        }

        bool Condition(in ConditionDef c)
        {
            int p = c.Parameter;
            if (p < 0) return false;
            var type = Graph.Parameters[p].Type;
            switch (c.Mode)
            {
                case 1: // If
                    return type == AnimatorControllerParameterType.Float ? Floats[p] != 0f : type == AnimatorControllerParameterType.Int ? Ints[p] != 0 : Bools[p];
                case 2: // IfNot
                    return type == AnimatorControllerParameterType.Float ? Floats[p] == 0f : type == AnimatorControllerParameterType.Int ? Ints[p] == 0 : !Bools[p];
                case 3: // Greater
                    return type == AnimatorControllerParameterType.Int ? Ints[p] > c.Threshold : Floats[p] > c.Threshold;
                case 4: // Less
                    return type == AnimatorControllerParameterType.Int ? Ints[p] < c.Threshold : Floats[p] < c.Threshold;
                case 6: // Equals
                    return type == AnimatorControllerParameterType.Int ? Ints[p] == (int)c.Threshold : Floats[p] == c.Threshold;
                case 7: // NotEqual
                    return type == AnimatorControllerParameterType.Int ? Ints[p] != (int)c.Threshold : Floats[p] != c.Threshold;
                default:
                    return false;
            }
        }

        bool Conditions(TransitionDef t)
        {
            foreach (var c in t.Conditions)
                if (!Condition(c)) return false;
            return true;
        }

        /// <summary>A transition taken resets the triggers its conditions used.</summary>
        void ConsumeTriggers(TransitionDef t)
        {
            foreach (var c in t.Conditions)
                if (c.Parameter >= 0 && Graph.Parameters[c.Parameter].Type == AnimatorControllerParameterType.Trigger)
                    Bools[c.Parameter] = false;
        }

        // ------------------------------------------------------------------ the update

        internal void Tick(float deltaTime, bool automatic = false)
        {
            if (m_Ticking) return;
            m_Ticking = true;
            m_AutomaticUpdate = automatic;
            try
            {
                TickCore(deltaTime);
            }
            finally
            {
                m_Ticking = false;
                m_AutomaticUpdate = false;
            }
        }

        bool m_Ticking;
        bool m_AutomaticUpdate;

        /// <summary>Whether the player has had an update (Animator culling: a CullCompletely
        /// Animator's first update runs even while culled).</summary>
        internal bool Ticked;

        /// <summary>Set by the Animator for a culled update (<see cref="Animator.Tick"/>): the state
        /// machines, events, callbacks and root motion run, the Transforms and the humanoid pose are
        /// not written and the IK pass does not run; other properties (parameter curves, materials,
        /// script fields) are written.</summary>
        internal bool SkipWrites;
        /// <summary>The pending playable outputs combine the controller's pose and motion before
        /// writing and delivering movement once for the Animator.</summary>
        internal bool DeferOutput;
        Unbound.Runtime.Playables.AnimStream m_DeferredPose;

        /// <summary>The attached controller's already-composed pose. Its defaults, retained values
        /// and root-motion exclusions are the same as for a direct WritePose; output-only bindings
        /// added afterwards leave the stream's current values alone.</summary>
        internal void CopyDeferredInto(float[] values, bool[] animated, UnityEngine.Object[] objects, bool[] objectAnimated)
        {
            var pose = m_DeferredPose;
            if (pose == null) return;
            for (int c = 0; c < Math.Min(values.Length, pose.FloatCount); c++)
                if (pose.Animated[c]) { values[c] = pose.Values[c]; animated[c] = true; }
            for (int c = 0; c < Math.Min(objects.Length, pose.ObjectCount); c++)
                if (pose.ObjectAnimated[c]) { objects[c] = pose.Objects[c]; objectAnimated[c] = true; }
        }

        int m_PosedFloats = -1, m_PosedObjects = -1;

        /// <summary>A culled Animator one of whose renderers became visible
        /// (<see cref="Animator.RendererBecameVisible"/>): the pose of its last evaluation is written
        /// and the IK pass runs, without advancing; with <paramref name="move"/>, the movement
        /// callbacks (or the builtin root motion) run first with that evaluation's deltas, applied
        /// again (measured, tests/projects/animculling).</summary>
        internal void EvaluateOnVisible(bool move)
        {
            if (m_Ticking || !Ticked || m_LayerPoses.Length < Layers.Length) return;
            m_Ticking = true;
            try
            {
                if (move)
                {
                    m_Animator.m_RootMotionApplied = false;
                    ProcessRootMotion();
                }
                // Bindings added since (a playable output's clip, an object rebound) need poses of
                // their size: sampled again at the same times.
                bool sized = Bindings.FloatCount == m_PosedFloats && Bindings.ObjectCount == m_PosedObjects;
                WritePose(sized ? Layers.Length : BuildLayerPoses());
            }
            finally
            {
                m_Ticking = false;
            }
        }

        void TickCore(float deltaTime)
        {
            if (Controller.OverrideVersion != m_OverrideVersion)
            {
                m_OverrideVersion = Controller.OverrideVersion;
                BindClips();
            }
            m_RootDeltaTime = deltaTime;
            m_CallbacksStarted |= deltaTime != 0f;
            StepLayers(deltaTime * m_Animator.m_Speed);
            Ticked = true;
            Evaluate();
        }

        /// <summary>Advances the state machines (a controller playable's prepare).</summary>
        internal void StepLayers(float dt)
        {
            if (Controller.OverrideVersion != m_OverrideVersion)
            {
                m_OverrideVersion = Controller.OverrideVersion;
                BindClips();
            }
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                if (layer.Def.SyncedLayerIndex >= 0) continue;
                layer.PreviousCurrent = layer.Current;
                layer.PreviousNext = layer.Next;
                Step(layer, i, dt);
            }
        }

        /// <summary>Events and state callbacks. Controller playables call this after writing their
        /// output; the attached Animator runs it before movement callbacks and transform writes.</summary>
        internal void AfterEvaluate()
        {
            // Animator.Update(0) before the first advancing evaluation does not enter states; it
            // fires the events at the states' start times, once (measured, tests/projects/modelclips).
            if (Attached && !m_CallbacksStarted)
            {
                FireEvents();
                foreach (var layer in Layers)
                {
                    if (layer.Current != null) layer.Current.EventsStarted = true;
                    if (layer.Next != null) layer.Next.EventsStarted = true;
                }
                return;
            }
            FireEvents();
            OrderCallbacks();
            foreach (var layer in Layers)
            {
                if (layer.Current != null) { layer.Current.Fresh = false; layer.Current.EnteredThisUpdate = false; }
                if (layer.Next != null) { layer.Next.Fresh = false; layer.Next.EnteredThisUpdate = false; }
            }
            // With pending playable outputs, their clip events fire first (6000.0.71f1,
            // tests/projects/animcallbacks): AnimationOutputs.RunPending runs these afterwards.
            if (DeferOutput && m_AutomaticUpdate && AnimationPhases.Split) return;
            RunCallbacks();
        }

        /// <summary>The state callbacks an update with pending playable outputs held back.</summary>
        internal void RunDeferredCallbacks()
        {
            bool outer = EnterDeferred();
            try { RunCallbacks(); }
            finally { ExitDeferred(outer); }
        }

        /// <summary>The deferred parts of an automatic update (<see cref="AnimationPhases"/>) run
        /// as part of it: an Animator.Update from their callbacks is ignored, as during the
        /// update itself. Returns the previous state for <see cref="ExitDeferred"/>.</summary>
        internal bool EnterDeferred()
        {
            bool outer = m_Ticking;
            m_Ticking = true;
            return outer;
        }

        internal void ExitDeferred(bool outer) => m_Ticking = outer;

        /// <summary>The controller's pose as an animation stream (a controller playable): layers
        /// over the stream's values (the default values), as <see cref="PoseWriter.WriteLayers"/>
        /// does over the current ones.</summary>
        internal void EvaluateInto(float[] values, bool[] animated, UnityEngine.Object[] objects, bool[] objectAnimated)
        {
            int count = BuildLayerPoses();
            PoseWriter.CombineLayers(Bindings, m_LayerPoses, count, values, animated, overDefaults: false);
            int m = Math.Min(Bindings.ObjectCount, objects.Length);
            for (int c = 0; c < m; c++)
            {
                for (int l = count - 1; l >= 0; l--)
                {
                    if (m_LayerPoses[l].AnimationPose.ObjectWeight[c] > 0f && m_LayerPoses[l].Weight > 0f)
                    {
                        objects[c] = m_LayerPoses[l].AnimationPose.Objects[c];
                        objectAnimated[c] = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// This player's own sample of one float channel (a parameter curve's) from the layer poses
        /// its last <see cref="EvaluateInto"/> built, for its own parameter: the per-channel rule of
        /// <see cref="PoseWriter.CombineLayers"/> over <paramref name="defaultValue"/> (the
        /// parameter's default, below the layers and filling them, as the Animator's own write
        /// combines over the default values) with <paramref name="current"/> held where a state
        /// keeps its value. Nothing is recorded (the pose's own combination did that). False when no
        /// layer animates the channel. Unbound's choice (unmeasured): the held value is the
        /// parameter's current value.
        /// </summary>
        internal bool LayeredSample(int channel, float defaultValue, float current, out float value)
        {
            float r = defaultValue;
            bool any = false, written = false;
            int count = Math.Min(Layers.Length, m_LayerPoses.Length);
            for (int l = 0; l < count; l++)
            {
                var layer = m_LayerPoses[l];
                var pose = layer.AnimationPose;
                if (pose == null || channel < 0 || channel >= Bindings.FloatCount || channel >= pose.Weight.Length) continue;
                float w = pose.Weight[channel], keep = pose.Keep[channel];
                if (w <= 0f && keep <= 0f && pose.Fill[channel] <= 0f && pose.Rest[channel] <= 0f) continue;
                any = true;
                float lw = layer.Weight;
                if (lw <= 0f) continue;
                float v = pose.Value[channel];
                if (layer.Additive)
                {
                    r += lw * v;
                    written = true;
                    continue;
                }
                float fill = w > 0f || keep > 0f ? pose.Fill[channel] : 0f;
                v += keep * (written ? r : current) + fill * defaultValue;
                w += keep + fill;
                float lv = w >= 1f ? v / w : v + (1f - w) * r;
                r = lw >= 1f ? lv : r + (lv - r) * lw;
                written = true;
            }
            value = r;
            return any;
        }

        void Step(LayerRun layer, int index, float dt)
        {
            layer.Completed = null;
            StateRun requested = null;
            if (layer.Pending != null)
            {
                var pending = layer.Pending;
                layer.Pending = null;
                requested = ApplyPending(layer, index, pending);
            }
            if (layer.Current == null)
            {
                if (layer.Def.Root == null) return;
                var state = StartState(layer);
                if (state == null) return;
                var run = new StateRun(state, 0.0);
                SetCurrent(layer, run);
                QueueEnter(layer, run);
            }
            // A state a Play or CrossFade request enters plays from the requested time: the update
            // it enters in does not advance it (6000.0.71f1, tests/projects/animstartup and
            // humanoid playState/crossFade*); its root motion covers that update up to there.
            // A state driven by a motion time parameter takes the parameter at once (AnimatorState.timeParameter
            // replaces the normalized time; its entry update is not measured).
            bool hold = requested != null && requested.Def.TimeParameter < 0 && Compat.AnimatorStartupMeasured;
            if (hold) Enter(layer, requested, dt);
            if (!hold || layer.Current != requested) Advance(layer, layer.Current, dt);
            if (layer.Next != null && (!hold || layer.Next != requested)) Advance(layer, layer.Next, dt);
            if (layer.Transition != null)
            {
                var t = layer.Transition;
                t.Elapsed += dt;
                t.Progress = t.Duration > 0f ? (float)Math.Min(1.0, t.Elapsed / t.Duration) : 1f;
                if (t.Progress >= 1f) CompleteTransition(layer);
            }
            EvaluateTransitions(layer, dt);
        }

        /// <summary>A state entered in this update at its current time: its root motion covers the
        /// whole update, ending at that time, as if it had played all of it (measured,
        /// tests/projects/humanoid: transitions' destinations at any exit time, with or without a
        /// duration, generic and humanoid clips, and states entered by Play and CrossFade).</summary>
        void Enter(LayerRun layer, StateRun run, float dt)
        {
            run.Previous = run.Normalized;
            float length = StateLength(layer, run);
            run.MotionPrevious = run.Def.TimeParameter < 0 && length > 0f ? run.Normalized - dt * SpeedOf(run.Def) / length : run.Normalized;
        }

        void Advance(LayerRun layer, StateRun run, float dt)
        {
            run.MotionPrevious = null;
            run.Previous = run.Normalized;
            var def = run.Def;
            if (def.TimeParameter >= 0)
            {
                run.Normalized = Floats[def.TimeParameter];
                return;
            }
            float length = StateLength(layer, run);
            run.Normalized = (float)(run.Normalized + dt * SpeedOf(def) / length);
        }

        float SpeedOf(StateDef def) => def.Speed * (def.SpeedParameter >= 0 ? Floats[def.SpeedParameter] : 1f);

        /// <summary>Applies a Play or CrossFade request; returns the state it enters (null when the
        /// requested state already plays and continues).</summary>
        StateRun ApplyPending(LayerRun layer, int index, PendingPlay p)
        {
            var state = p.State;
            if (!p.CrossFade)
            {
                if (float.IsNegativeInfinity(p.Time) && layer.Current != null && layer.Current.Def == state && layer.Transition == null)
                    return null; // already playing: it continues
                double n = float.IsNegativeInfinity(p.Time) ? 0.0 : p.FixedTime ? p.Time / MotionLength(layer, MotionOf(layer.Def, state)) : p.Time;
                ExitRun(layer, layer.Next);
                ExitRun(layer, layer.Current);
                layer.Next = null;
                layer.Transition = null;
                var run = new StateRun(state, n);
                SetCurrent(layer, run);
                QueueEnter(layer, run);
                return run;
            }
            CollapseTransition(layer);
            var from = layer.Current;
            double offset = float.IsNegativeInfinity(p.Time) ? (from != null && from.Def == state ? from.Normalized : 0.0)
                : p.FixedTime ? p.Time / MotionLength(layer, MotionOf(layer.Def, state)) : p.Time;
            float duration = p.FixedDuration ? p.Duration : p.Duration * (from != null ? StateDuration(layer, from) : 0f);
            var next = new StateRun(state, offset);
            if (from == null || duration <= 0f)
            {
                ExitRun(layer, from);
                SetCurrent(layer, next);
                QueueEnter(layer, next);
                return next;
            }
            layer.Next = next;
            layer.Transition = new TransitionRun
            {
                Duration = duration,
                NominalDuration = p.Duration,
                FixedDuration = p.FixedDuration,
                Elapsed = p.TransitionTime * duration,
                Progress = p.TransitionTime,
                FullPathHash = 0,
                NameHash = 0,
                UserNameHash = 0,
            };
            QueueEnter(layer, next);
            return next;
        }

        /// <summary>An ongoing transition collapses to the state with more weight.</summary>
        void CollapseTransition(LayerRun layer)
        {
            if (layer.Transition == null) return;
            if (layer.Transition.Progress >= 0.5f)
            {
                ExitRun(layer, layer.Current);
                layer.Current = layer.Next;
            }
            else
            {
                ExitRun(layer, layer.Next);
            }
            layer.Next = null;
            layer.Transition = null;
        }

        void SetCurrent(LayerRun layer, StateRun run)
        {
            layer.Current = run;
        }

        void CompleteTransition(LayerRun layer)
        {
            layer.Completed = layer.Current;
            ExitRun(layer, layer.Current);
            layer.Current = layer.Next;
            layer.Next = null;
            layer.Transition = null;
        }

        void EvaluateTransitions(LayerRun layer, float dt)
        {
            var current = layer.Current;
            if (current == null) return;
            if (layer.Transition == null)
            {
                var chosen = FindTransition(layer, current, layer.Def.AnyState, null);
                if (chosen == null)
                    chosen = FindTransition(layer, current, current.Def.Transitions, null);
                if (chosen != null) StartTransition(layer, chosen, current, dt, ExitOvershoot(layer, current, chosen));
                return;
            }

            // Interruptions: Any State transitions, then the transitions the interruption source allows.
            var active = layer.Transition.Def;
            var next = layer.Next;
            TransitionDef interrupt = FindTransition(layer, current, layer.Def.AnyState, active, next);
            StateRun from = current;
            if (interrupt == null && active != null)
            {
                switch (active.InterruptionSource)
                {
                    case 1: // Source
                        interrupt = SourceInterrupt(layer, current, active);
                        break;
                    case 2: // Destination
                        interrupt = FindTransition(layer, next, next.Def.Transitions, active);
                        from = next;
                        break;
                    case 3: // Source then destination
                        interrupt = SourceInterrupt(layer, current, active);
                        if (interrupt == null) { interrupt = FindTransition(layer, next, next.Def.Transitions, active); from = next; }
                        break;
                    case 4: // Destination then source
                        interrupt = FindTransition(layer, next, next.Def.Transitions, active);
                        from = next;
                        if (interrupt == null) { interrupt = SourceInterrupt(layer, current, active); from = current; }
                        break;
                }
            }
            if (interrupt == null) return;
            // The interrupted transition collapses to the state the new transition leaves.
            if (from == next)
            {
                ExitRun(layer, current);
                layer.Current = next;
            }
            else
            {
                ExitRun(layer, next);
            }
            layer.Next = null;
            layer.Transition = null;
            StartTransition(layer, interrupt, layer.Current, dt);
        }

        TransitionDef SourceInterrupt(LayerRun layer, StateRun current, TransitionDef active)
        {
            var list = current.Def.Transitions;
            foreach (var t in list)
            {
                if (t == active) break;
                if (!active.OrderedInterruption || t.Index < active.Index)
                    if (Check(t, current)) return t;
            }
            if (!active.OrderedInterruption)
                foreach (var t in list)
                    if (t != active && t.Index > active.Index && Check(t, current)) return t;
            return null;
        }

        TransitionDef FindTransition(LayerRun layer, StateRun run, IReadOnlyList<TransitionDef> list, TransitionDef exclude, StateRun alsoCurrent = null)
        {
            if (run == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == exclude) continue;
                if (t.IsAnyState && !t.CanTransitionToSelf && t.DstState != null &&
                    (t.DstState == run.Def || (alsoCurrent != null && t.DstState == alsoCurrent.Def)))
                    continue;
                if (Check(t, run)) return t;
            }
            return null;
        }

        bool Check(TransitionDef t, StateRun run)
        {
            if (t.Conditions.Length == 0 && !t.HasExitTime) return false; // Unity ignores such a transition
            if (t.HasExitTime && !ExitTimeReached(run, t.ExitTime)) return false;
            return Conditions(t);
        }

        /// <summary>
        /// The exit time is crossed this update: after the time the update started at, up to the time
        /// it ends at. Below 1 it recurs every loop (k + exitTime); from 1 up it is crossed once. A
        /// state does not cross the time it starts at (measured, tests/projects/humanoid exit000: an
        /// exit time of 0 is first reached at the end of the first loop).
        /// </summary>
        static bool ExitTimeReached(StateRun run, float exitTime) => !double.IsNaN(ExitCrossing(run, exitTime));

        /// <summary>The time (seconds) an update ran past the exit time a transition left at: the
        /// transition starts there, its destination and progress already that far (Unity's
        /// sub-frame start; transitions on conditions alone start at the update's end).</summary>
        double ExitOvershoot(LayerRun layer, StateRun run, TransitionDef t)
        {
            if (!t.HasExitTime || run.Def.TimeParameter >= 0) return 0.0;
            double crossing = ExitCrossing(run, t.ExitTime);
            if (double.IsNaN(crossing)) return 0.0;
            float speed = Math.Abs(SpeedOf(run.Def));
            if (speed <= 1e-6f) return 0.0;
            return Math.Max(0.0, (run.Normalized - crossing) * StateLength(layer, run) / speed);
        }

        /// <summary>The normalized time of the exit time crossed this update, NaN when none is (see
        /// <see cref="ExitTimeReached"/>).</summary>
        static double ExitCrossing(StateRun run, float exitTime)
        {
            double from = run.Previous, to = run.Normalized;
            if (to < from) return double.NaN;
            double crossing = exitTime >= 1f ? exitTime : Math.Max(0.0, Math.Floor(from - exitTime) + 1.0) + exitTime;
            return from < crossing && crossing <= to ? crossing : double.NaN;
        }

        void StartTransition(LayerRun layer, TransitionDef t, StateRun from, float dt, double overshoot = 0.0)
        {
            ConsumeTriggers(t);
            var dest = Destination(layer, t, from.Def, 0);
            if (dest == null) return;
            float duration = t.HasFixedDuration ? t.Duration : t.Duration * StateDuration(layer, from);
            var next = new StateRun(dest, t.Offset);
            if (overshoot > 0.0)
            {
                // The destination plays the rest of the update already.
                float length = StateLength(layer, next);
                if (next.Def.TimeParameter < 0 && length > 0f) next.Normalized += overshoot * SpeedOf(next.Def) / length;
            }
            // Its events still start at the offset; its root motion covers the whole update.
            double eventsFrom = next.Previous;
            Enter(layer, next, dt);
            next.Previous = eventsFrom;
            if (duration <= 0f || overshoot >= duration)
            {
                float fromLength = StateLength(layer, from);
                double end = from.Normalized - (fromLength > 0f && from.Def.TimeParameter < 0 ? overshoot * SpeedOf(from.Def) / fromLength : 0.0);
                m_Left.Add((layer, from, end));
                ExitRun(layer, from);
                SetCurrent(layer, next);
                QueueEnter(layer, next);
                return;
            }
            layer.Next = next;
            var src = t.IsAnyState ? "AnyState" : layer.Def.Name + "." + from.Def.Name;
            layer.Transition = new TransitionRun
            {
                Def = t,
                Duration = duration,
                NominalDuration = t.Duration,
                FixedDuration = t.HasFixedDuration,
                AnyState = t.IsAnyState,
                FullPathHash = Animator.StringToHash(src + " -> " + layer.Def.Name + "." + dest.Name),
                NameHash = Animator.StringToHash((t.IsAnyState ? "AnyState" : from.Def.Name) + " -> " + dest.Name),
                UserNameHash = t.UserNameHash,
                Elapsed = overshoot,
                Progress = (float)Math.Min(1.0, overshoot / duration),
            };
            QueueEnter(layer, next);
        }

        StateDef Destination(LayerRun layer, TransitionDef t, StateDef source, int depth)
        {
            if (depth > 16) return null;
            if (t.DstState != null) return t.DstState;
            if (t.DstMachine != null) return EnterMachine(layer, t.DstMachine, depth + 1, callEnter: true);
            if (t.IsExit) return ExitMachine(layer, source?.Machine ?? layer.Def.Root, depth + 1);
            return null;
        }

        /// <summary>A layer's first state: its top machine's default state. Measured on the 6000.0
        /// Editor: starting does not go through the Entry node (its transitions, even conditional
        /// ones that hold, are not taken); a sub-machine entered later does take them.</summary>
        StateDef StartState(LayerRun layer) =>
            layer.Def.Root.Default ?? EnterMachine(layer, layer.Def.Root, 0, callEnter: false);

        /// <summary>Entering a state machine through its Entry node: the first entry transition whose
        /// conditions hold, else the default state.</summary>
        StateDef EnterMachine(LayerRun layer, MachineDef machine, int depth, bool callEnter)
        {
            if (depth > 16) return null;
            if (callEnter) QueueMachine(layer, machine, enter: true);
            foreach (var e in machine.Entry)
            {
                if (!Conditions(e)) continue;
                ConsumeTriggers(e);
                var d = Destination(layer, e, null, depth + 1);
                if (d != null) return d;
            }
            if (machine.Default != null) return machine.Default;
            if (machine.States.Count > 0) return machine.States[0];
            foreach (var child in machine.Children)
            {
                var d = EnterMachine(layer, child, depth + 1, callEnter);
                if (d != null) return d;
            }
            return null;
        }

        /// <summary>Leaving a sub-state machine through its Exit node: the parent's transitions from
        /// that machine, else the parent's Entry. The top machine's Exit leads to its Entry.</summary>
        StateDef ExitMachine(LayerRun layer, MachineDef machine, int depth)
        {
            if (depth > 16 || machine == null) return null;
            QueueMachine(layer, machine, enter: false);
            var parent = machine.Parent;
            if (parent == null) return EnterMachine(layer, machine, depth + 1, callEnter: false);
            if (parent.MachineTransitions.TryGetValue(machine, out var list))
            {
                foreach (var t in list)
                {
                    if (!Conditions(t)) continue;
                    ConsumeTriggers(t);
                    var d = Destination(layer, t, null, depth + 1);
                    if (d != null) return d;
                }
            }
            return EnterMachine(layer, parent, depth + 1, callEnter: false);
        }

        // ------------------------------------------------------------------ motions

        internal Motion MotionOf(LayerDef layer, StateDef state)
        {
            if (layer.SyncedLayerIndex >= 0)
                return layer.SyncedMotions.TryGetValue(state, out var m) ? m : null;
            return state.Motion;
        }

        /// <summary>A state's length in seconds (the motion's; 1 for a state without one), with the
        /// layers synced to it with "Timing" on taken into account (<see cref="TimedLength"/>).</summary>
        internal float StateLength(LayerRun layer, StateRun run) => TimedLength(layer, run.Def, MotionLength(layer, MotionOf(layer.Def, run.Def)));

        /// <summary>
        /// A source layer state's duration with its synced layers' "Timing". The 6000.0 Manual
        /// (Animation Layers): without Timing, the synced layer's animation is stretched to the
        /// length of the original layer's (the length stays <paramref name="length"/>); with
        /// Timing, the length is "a balance for both animations, based on weight". The balance
        /// (Unity 6000.0.71f1, tests/projects/animatoradditive timing*): starting from the source
        /// state's motion length, each Timing layer in layer order moves the duration toward its
        /// own motion's length by its weight (clamped to 0..1; weights outside it are unmeasured).
        /// A layer without a motion for the state takes no part, nor does a source state without a
        /// motion (the first Timing motion then counts by its weight from zero); a duration of zero
        /// is the 1 s of a state without a motion.
        /// </summary>
        float TimedLength(LayerRun source, StateDef state, float length)
        {
            int index = source.Def.Index;
            var synced = index >= 0 && index < m_TimingLayers.Length ? m_TimingLayers[index] : null;
            if (synced == null) return length;
            float duration = MotionOf(source.Def, state) != null ? length : 0f;
            foreach (int i in synced)
            {
                var layer = Layers[i];
                var motion = MotionOf(layer.Def, state);
                if (motion == null) continue;
                // The base layer's weight is always 1.
                float weight = i == 0 ? 1f : Math.Clamp(layer.Evaluated, 0f, 1f);
                duration += weight * (MotionLength(layer, motion) - duration);
            }
            return duration > 0f ? duration : 1f;
        }

        /// <summary>The layers synced with "Timing" on, by source layer, in layer order.</summary>
        static int[][] TimingLayers(RuntimeAnimatorController controller, ControllerGraph graph)
        {
            var result = new int[graph.Layers.Length][];
            for (int i = 0; i < graph.Layers.Length; i++)
            {
                int source = graph.Layers[i].SyncedLayerIndex;
                if (source < 0 || source >= graph.Layers.Length || source == i) continue;
                if (graph.Layers[source].SyncedLayerIndex >= 0 || !controller.SyncedLayerAffectsTiming(i)) continue;
                var list = result[source] ?? Array.Empty<int>();
                Array.Resize(ref list, list.Length + 1);
                list[^1] = i;
                result[source] = list;
            }
            return result;
        }

        /// <summary>The length a state info reports for a state of <paramref name="layer"/>: the
        /// state's duration (AnimatorStateInfo.length). A synced layer, with Timing on or off,
        /// reports its source state's duration (tests/projects/animatoradditive timing*).</summary>
        float InfoLength(LayerRun layer, StateDef state, Motion motion)
        {
            int source = layer.Def.SyncedLayerIndex;
            if (source < 0 || source >= Layers.Length) return TimedLength(layer, state, MotionLength(layer, motion));
            var owner = Layers[source];
            return TimedLength(owner, state, MotionLength(owner, MotionOf(owner.Def, state)));
        }

        /// <summary>Adds a tree's clips' lengths by their weights in the whole tree.</summary>
        void AddLeafLengths(LayerRun layer, BlendTreeMotion tree, float weight, int depth, ref float sum, ref float total)
        {
            if (depth > 16) return;
            var weights = BlendWeights(tree);
            for (int i = 0; i < tree.Children.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                var child = tree.Children[i];
                float w = weight * weights[i];
                if (child.Motion is BlendTreeMotion subtree)
                {
                    // A child tree's time scale applies to its clips.
                    float before = sum;
                    AddLeafLengths(layer, subtree, w, depth + 1, ref sum, ref total);
                    sum = before + (sum - before) / Math.Max(Math.Abs(child.TimeScale), 1e-4f);
                    continue;
                }
                sum += w * MotionLength(layer, child.Motion) / Math.Max(Math.Abs(child.TimeScale), 1e-4f);
                total += w;
            }
        }

        /// <summary>A state's duration at its speed: what normalized transition durations scale.</summary>
        float StateDuration(LayerRun layer, StateRun run)
        {
            float speed = Math.Abs(SpeedOf(run.Def));
            return StateLength(layer, run) / Math.Max(speed, 1e-4f);
        }

        /// <summary>A motion's length in seconds: a clip's; a blend tree's is its clips' lengths
        /// (each over its child's time scale) summed by their weights in the whole tree, and
        /// divided by the weights' total only while that is below one. Trees whose weights sum to
        /// one give the same either way; a Direct blend tree whose weights sum above one lasts
        /// the plain sum (matches the Unity 6000.0.71f1 Editor, tests/projects/humanoid
        /// body.directHigh: clips of 1 s at 0.8 and 0.6 make a state of 1.4 s; at 0.3 and 0.4,
        /// body.directLow, 1 s). A child tree without clips counts for nothing (in an Editor
        /// capture of the probe's HumanBodyDirectEmptyChild controller, not in the reference); a
        /// child without a motion counts as one second by its weight,
        /// as before (unmeasured, as are clips of other lengths below a total of one).</summary>
        internal float MotionLength(LayerRun layer, Motion motion)
        {
            switch (motion)
            {
                case AnimationClip original:
                {
                    var clip = Controller.MapClip(original);
                    float length = clip != null ? clip.length : 0f;
                    return length > 0f ? length : 1f;
                }
                case BlendTreeMotion tree:
                {
                    float sum = 0f, total = 0f;
                    AddLeafLengths(layer, tree, 1f, 0, ref sum, ref total);
                    if (total > 0f) return total >= 1f ? sum : sum / total;
                    return tree.Children.Count > 0 ? MotionLength(layer, tree.Children[0].Motion) : 1f;
                }
                default:
                    return 1f;
            }
        }

        bool MotionLoops(Motion motion)
        {
            if (motion is AnimationClip original)
            {
                var clip = Controller.MapClip(original);
                return clip != null && clip.isLooping;
            }
            return motion != null && motion.isLooping;
        }

        /// <summary>The clips of a motion and their weights (for clip infos).</summary>
        internal void ClipWeights(Motion motion, float weight, List<AnimatorClipInfo> into)
        {
            switch (motion)
            {
                case AnimationClip original:
                {
                    var clip = Controller.MapClip(original);
                    if (clip != null) into.Add(new AnimatorClipInfo { m_Clip = clip, m_Weight = weight });
                    break;
                }
                case BlendTreeMotion tree:
                {
                    var weights = BlendWeights(tree);
                    for (int i = 0; i < weights.Length; i++)
                        if (weights[i] > 0f) ClipWeights(tree.Children[i].Motion, weight * weights[i], into);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ evaluation

        void Evaluate()
        {
            // A later evaluation replaces a deferred write (an explicit update between the leaves).
            m_PendingWrite = false;
            m_PendingComposed = false;
            int count = BuildLayerPoses();
            ComputeRootMotion(count);
            AfterEvaluate();
            if (DeferOutput)
            {
                m_DeferredPose ??= new Unbound.Runtime.Playables.AnimStream();
                m_DeferredPose.Reset(Bindings);
                WritePose(count, stream: m_DeferredPose);
                // The playable outputs that take this pose on blend and write goals relative to the body.
                if (PlacesGoals) m_Animator.Human.PlacedGoalsToBody(m_DeferredPose);
                return;
            }
            if (m_AutomaticUpdate && Attached && AnimationPhases.Split)
            {
                // Movement after every Animator's state machines; the write in the End leaf.
                m_PendingWrite = true;
                m_PendingCount = count;
                m_PendingSkipWrites = SkipWrites;
                AnimationPhases.Defer(m_Animator, this);
                return;
            }
            ProcessRootMotion();
            WritePose(count, SkipWrites);
        }

        bool m_PendingWrite, m_PendingSkipWrites, m_PendingComposed;
        int m_PendingCount;
        Unbound.Runtime.Playables.AnimStream m_PendingPose;

        /// <summary>The deferred update's movement (<see cref="AnimationPhases"/>), then the write of
        /// the Animator's own Transform: the pose is composed once here, its root channels are
        /// written with the movement and the rest in the End leaf (6000.0.71f1,
        /// tests/projects/animcallbacks root* cases).</summary>
        internal void DeliverDeferredMovement()
        {
            if (!m_PendingWrite) return;
            bool outer = EnterDeferred();
            try
            {
                ProcessRootMotion();
                // A movement callback may have evaluated again or disabled the Animator.
                if (!m_PendingWrite || !m_Animator.isActiveAndEnabled) return;
                m_PendingPose ??= new Unbound.Runtime.Playables.AnimStream();
                m_PendingPose.Reset(Bindings);
                WritePose(m_PendingCount, stream: m_PendingPose);
                m_PendingComposed = true;
                // A culled Animator keeps its Transforms, except the root an output animates.
                if (!m_PendingSkipWrites || RootDuringCull() != null) PoseWriter.WriteStream(m_PendingPose, onlyRoot: m_Animator.transform);
            }
            finally { ExitDeferred(outer); }
        }

        /// <summary>The deferred update's write and IK (<see cref="AnimationPhases"/>): what its
        /// movement step composed, but the root, or the whole pose when that step did not run.</summary>
        internal void WriteDeferred()
        {
            if (!m_PendingWrite) return;
            m_PendingWrite = false;
            bool composed = m_PendingComposed;
            m_PendingComposed = false;
            bool outer = EnterDeferred();
            try
            {
                if (!composed)
                {
                    WritePose(m_PendingCount, m_PendingSkipWrites);
                    return;
                }
                PoseWriter.WriteStream(m_PendingPose, skipRoot: m_Animator.transform, skipTransforms: m_PendingSkipWrites);
                if (!m_PendingSkipWrites) FinishWrite(m_PendingCount);
            }
            finally { ExitDeferred(outer); }
        }

        /// <summary>The root a culled Animator still writes: an attached controller's, when
        /// playable outputs animate it too (measured, tests/projects/animculling).</summary>
        Transform RootDuringCull() =>
            Attached && Unbound.Runtime.Playables.AnimationOutputs.Of(m_Animator)?.Count > 0 ? m_Animator.transform : null;

        void WritePose(int count, bool skipTransforms = false, Unbound.Runtime.Playables.AnimStream stream = null)
        {
            // Events and movement callbacks may bind additional stream channels after sampling.
            // Grow in place so the current pose and root-motion exclusions are not recomputed.
            for (int i = 0; i < count; i++)
                m_LayerPoses[i].AnimationPose.Grow(Bindings.FloatCount, Bindings.ObjectCount);
            m_PosedFloats = Bindings.FloatCount;
            m_PosedObjects = Bindings.ObjectCount;
            if (m_Written.Length < Bindings.FloatCount)
            {
                Array.Resize(ref m_Written, Bindings.FloatCount);
                Array.Resize(ref m_WrittenHeld, Bindings.FloatCount);
            }
            PoseWriter.WriteLayers(Bindings, m_LayerPoses, count, m_Written, m_WrittenHeld, humanBodyInRootMotion: Attached,
                skipTransforms: skipTransforms, stream: stream, rootDuringCull: RootDuringCull(), placedGoals: m_PlacedHuman != null);
            if (skipTransforms || stream != null) return;
            FinishWrite(count);
        }

        /// <summary>After the layers are written: the humanoid's body and muscles, and the root pose
        /// the next root motion starts from.</summary>
        void FinishWrite(int count)
        {
            if (!Attached) return;
            WriteMassCenterPose(count);
            if (m_Animator.Human is { } human)
            {
                // The goal curves just written hold placed goals (HumanStateGoals).
                human.GoalsInMotion = true;
                if (FootIK()) human.FootIK = true;
            }
            m_Animator.Human?.DefaultPoseUnlessWritten();
            m_Animator.ApplyHumanPose();
            m_Animator.CaptureRootMotionPose();
        }

        /// <summary>
        /// Whether the humanoid pose solves its feet onto their goals for a state's "Foot IK"
        /// (AnimatorState.iKOnFeet, see <see cref="Unbound.Runtime.Humanoid.HumanIK.SetIKOnFeet"/>;
        /// Manual 6000.0 Animation States: "Enable to respect Foot IK for this state", humanoid
        /// only): any state with it that the evaluation samples at a weight above 0, alone
        /// (tests/projects/humanoid footIK) or with other states in a transition, a blend tree or
        /// on a layer, an additive one included; the feet then go onto the goals all the sampled
        /// clips blend to (6000.0.71f1, footIKTransition, goals.*). An AnimatorControllerPlayable's
        /// player still blends goals relative to its clips' bodies (see <see cref="PlacesGoals"/>):
        /// there Foot IK applies only while one humanoid clip is sampled and is otherwise left
        /// out with a diagnostic.
        /// </summary>
        internal bool FootIK()
        {
            bool asked = false;
            int humanClips = 0;
            foreach (var (layer, layerIndex, run, clip, weight, _) in m_Contributions)
            {
                if (weight <= 0f || (layerIndex > 0 && layer.Evaluated <= 0f)) continue;
                if (run.Def.IKOnFeet) asked = true;
                if (clip.HasHumanCurves) humanClips++;
            }
            if (!asked) return false;
            if (Attached || humanClips <= 1) return true;
            Stubs.Ignored("AnimatorControllerPlayable Foot IK of blended humanoid states",
                "a state's Foot IK is not applied while an AnimatorControllerPlayable blends its clip with other humanoid clips "
                + "(transitions, blend trees, layers): it blends their feet goals in another frame than Unity's (not supported yet)");
            return false;
        }

        LayerPose[] m_MassCenterLayers = Array.Empty<LayerPose>();
        float[] m_MassCenterValues = Array.Empty<float>();

        /// <summary>A humanoid Animator's mass center pose, which places the body (hips) before the
        /// muscles of every layer apply: the layers whose avatar mask leaves out Root do not take part
        /// (measured, tests/projects/humanoid: the hips stay where the layers below put them). Only
        /// set when such a layer has weight and Animator.layersAffectMassCenter is off; otherwise the
        /// whole pose places the body. Curves no placing layer animates take their defaults, the
        /// identity body and zero muscles (measured over an empty base state, tests/projects/humanoid
        /// noRootOverEmpty).</summary>
        void WriteMassCenterPose(int count)
        {
            var human = m_Animator.Human;
            if (human == null || m_Animator.layersAffectMassCenter) return;
            bool any = false;
            for (int i = 1; i < count && !any; i++)
                any = m_LayerPoses[i].Weight > 0f && !PlacesBody(i);
            if (!any) return;
            if (m_MassCenterLayers.Length < count) m_MassCenterLayers = new LayerPose[count];
            int k = 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0 && !PlacesBody(i)) continue;
                var layer = m_LayerPoses[i];
                // Combined again without recording the layers' own values.
                layer.Last = null;
                layer.Held = null;
                m_MassCenterLayers[k++] = layer;
            }
            int n = Bindings.FloatCount;
            if (m_MassCenterValues.Length < n) m_MassCenterValues = new float[n];
            for (int c = 0; c < n; c++)
            {
                var owner = Bindings.FloatOwner[c];
                m_MassCenterValues[c] = owner != null && owner.HumanCurve >= 0 ? Bindings.Defaults[c] : 0f;
            }
            PoseWriter.CombineLayers(Bindings, m_MassCenterLayers, k, m_MassCenterValues, null, overDefaults: true, humanBodyInRootMotion: true,
                placedGoals: m_PlacedHuman != null);
            for (int c = 0; c < n; c++)
            {
                var owner = Bindings.FloatOwner[c];
                if (owner != null && owner.HumanCurve >= 0 && owner.First == c) human.SetMassCenter(owner.HumanCurve, m_MassCenterValues[c]);
            }
        }

        /// <summary>Whether a layer's avatar mask lets it place the humanoid body (its Root part).</summary>
        bool PlacesBody(int layer)
        {
            var mask = Layers[layer].Def.Mask;
            return mask == null || mask.m_HumanoidParts[(int)AvatarMaskBodyPart.Root];
        }

        /// <summary>The root motion of the humanoid clips this evaluation sampled
        /// (<see cref="m_Contributions"/> names each clip's), reused by the root motion pass.</summary>
        readonly List<Unbound.Runtime.Humanoid.HumanRootMotionSample> m_HumanClipSamples = new List<Unbound.Runtime.Humanoid.HumanRootMotionSample>();
        /// <summary>Per sample of <see cref="m_HumanClipSamples"/>, its clip's weight within its
        /// state (the product of its blend trees' weights; 1 for a state's single clip).</summary>
        readonly List<float> m_HumanClipShares = new List<float>();
        /// <summary>The weight of the state being sampled in its layer.</summary>
        float m_SamplingStateWeight;
        Unbound.Runtime.Humanoid.HumanStateGoals m_StateGoals;
        /// <summary>The goals of the state being sampled (null: its clips' goals are not placed).</summary>
        Unbound.Runtime.Humanoid.HumanStateGoals m_SamplingGoals;
        Unbound.Runtime.Humanoid.AnimatorHuman m_PlacedHuman;
        /// <summary>Whether the cached motion, layer channel and layer masks were made for a player
        /// that places goals (<see cref="Collect"/>, <see cref="LayerMask"/>).</summary>
        bool m_MasksPlaceGoals;

        /// <summary>Whether this player's humanoid clips place their IK goals by their bodies while
        /// they are sampled (<see cref="Unbound.Runtime.Humanoid.HumanStateGoals"/>): the
        /// Animator's own controller, whose root motion pass gives each clip's body. An
        /// AnimatorControllerPlayable's player has no humanoid root motion (unmeasured, see
        /// ControllerKind.RootMotion) and keeps its goals relative to the clips' bodies.</summary>
        bool PlacesGoals => Attached && m_Animator.Human != null;

        /// <summary>A state's times for a humanoid clip's root motion.</summary>
        Unbound.Runtime.Humanoid.HumanClipMotionInput HumanInput(StateRun run, AnimationClip clip) => new Unbound.Runtime.Humanoid.HumanClipMotionInput
        {
            Time = (float)ClipSampling.StateClipNormalized(run.Normalized, clip.length),
            // A state entered in this update steps the whole update (see Enter).
            PreviousTime = (float)ClipSampling.StateClipNormalized(run.MotionPrevious ?? run.Previous, clip.length),
            Speed = SpeedOf(run.Def),
            CycleOffset = run.Def.CycleOffset + (float)MirrorShift(run.Def, clip),
        };

        int BuildLayerPoses()
        {
            int count = Layers.Length;
            if (m_Poses.Length < count)
            {
                Array.Resize(ref m_Poses, count);
                m_LayerPoses = new LayerPose[count];
            }
            m_Contributions.Clear();
            m_HumanClipSamples.Clear();
            m_HumanClipShares.Clear();
            m_PlacedHuman = PlacesGoals ? m_Animator.Human : null;
            if (m_MasksPlaceGoals != (m_PlacedHuman != null))
            {
                // The channel masks depend on it (an avatar assigned or removed since they were made).
                m_MasksPlaceGoals = m_PlacedHuman != null;
                m_MotionMasks.Clear();
                m_NullMotionMask = default;
                Array.Clear(m_LayerChannels);
                Array.Clear(m_LayerMasks);
            }
            // Holding states keep the root motion of the layer's last evaluation.
            for (int i = 0; i < count; i++) Layers[i].Kept = Layers[i].Own;
            for (int i = 0; i < count; i++)
            {
                var layer = Layers[i];
                var pose = m_Poses[i] ??= new AnimationPose();
                pose.Reset(Bindings.FloatCount, Bindings.ObjectCount);
                var source = layer.Def.SyncedLayerIndex >= 0 && layer.Def.SyncedLayerIndex < count ? Layers[layer.Def.SyncedLayerIndex] : layer;
                var (mask, objectMask) = LayerMask(i);
                if (source.Current != null)
                {
                    bool transitioning = source.Next != null && source.Transition != null;
                    float p = transitioning ? source.Transition.Progress : 0f;
                    var completed = !transitioning && i == 0 ? source.Completed : null;
                    if (transitioning && AloneBesideEmpty(layer, i, source.Next, source.Current))
                        SampleAtFullWeight(layer, i, source.Next, 1f, pose, mask, objectMask);
                    else if (transitioning && AloneBesideEmpty(layer, i, source.Current, source.Next))
                        SampleAtFullWeight(layer, i, source.Current, 1f, pose, mask, objectMask);
                    else if (completed != null && EmptyWithDefaults(layer, source.Current) && MotionOf(layer.Def, completed.Def) != null)
                        SampleAtFullWeight(layer, i, completed, 1f, pose, mask, objectMask);
                    else
                    {
                        SampleState(layer, i, source.Current, 1f - p, pose, mask, objectMask);
                        if (transitioning) SampleState(layer, i, source.Next, p, pose, mask, objectMask);
                    }
                    if (transitioning && !layer.Def.Additive)
                    {
                        FillWhereKept(layer, i, source.Current, 1f - p, pose, mask);
                        FillWhereKept(layer, i, source.Next, p, pose, mask);
                    }
                }
                m_LayerPoses[i] = new LayerPose { AnimationPose = pose, Weight = i == 0 ? 1f : layer.Evaluated, Additive = layer.Def.Additive };
                // A state without a motion and without write defaults that a transition leaves keeps
                // the values it held when the transition began (measured: the layer's blended values
                // during the transition do not become its own; as a transition's destination it takes them).
                bool frozen = source.Next != null && source.Transition != null && Holds(layer, source.Current);
                if (!layer.Def.Additive)
                {
                    // Combining the layers records the layer's values anew.
                    EnsureLast(layer);
                    if (!frozen)
                    {
                        Array.Clear(layer.Held);
                        m_LayerPoses[i].Last = layer.Last;
                        m_LayerPoses[i].Held = layer.Held;
                    }
                }
                else
                {
                    if (!frozen) KeepLast(layer, pose);
                    // The layer writes what its motions animate every frame, if only with no difference.
                    AddNoDifference(pose, 1f, LayerChannels(i).Floats, null, mask, onlyUnweighted: true);
                }
            }
            m_PosedFloats = Bindings.FloatCount;
            m_PosedObjects = Bindings.ObjectCount;
            return count;
        }

        bool EmptyWithDefaults(LayerRun layer, StateRun run) => run != null && run.Def.WriteDefaults && MotionOf(layer.Def, run.Def) == null;

        /// <summary>A base layer's state with a motion in a transition with a state without a motion
        /// and with write defaults: its clips are all the layer plays, at full weight whatever the
        /// transition's progress, for the pose, the clip infos, events and root motion (measured,
        /// tests/projects/humanoid emptyExit, emptyCond, generic.emptyExit: the motion state's clip
        /// infos report weight 1 and its root motion applies in full from the transition's start).
        /// A motion state's transition into an empty state follows the same rule (measured for the
        /// pose, clip infos and root motion, tests/projects/humanoid emptyBack, generic.emptyBack;
        /// events unmeasured).</summary>
        internal bool AloneBesideEmpty(LayerRun layer, int layerIndex, StateRun run, StateRun other) =>
            layerIndex == 0 && EmptyWithDefaults(layer, other) && run != null && MotionOf(layer.Def, run.Def) != null;

        bool Holds(LayerRun layer, StateRun run) => run != null && !run.Def.WriteDefaults && MotionOf(layer.Def, run.Def) == null;

        /// <summary>Whether a layer has a state that holds (<see cref="Holds"/>): only then does a
        /// layer's own root step need keeping in every evaluation.</summary>
        bool m_MayHold;

        bool AnyHoldingState()
        {
            foreach (var layer in Layers)
            {
                int synced = layer.Def.SyncedLayerIndex;
                var states = synced >= 0 && synced < Layers.Length ? Layers[synced].Def.States : layer.Def.States;
                foreach (var state in states)
                    if (!state.WriteDefaults && MotionOf(layer.Def, state) == null) return true;
            }
            return false;
        }

        /// <summary>The base layer's transitions between a state without a motion and with write
        /// defaults and a motion state: the former adds nothing, the motion state's values apply in
        /// full whatever the transition's progress, also in the update the transition completes
        /// (measured; alone, the state without a motion writes the defaults). Contributions (events,
        /// root motion) take <paramref name="weight"/>: 1 during the transition (see
        /// <see cref="AloneBesideEmpty"/>) and in the update a transition into the state without a
        /// motion completes, whose root motion covers that whole update (6000.0.71f1,
        /// tests/projects/humanoid emptyBack, generic.emptyBack).</summary>
        void SampleAtFullWeight(LayerRun layer, int layerIndex, StateRun run, float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            int first = m_Contributions.Count;
            SampleState(layer, layerIndex, run, 1f, pose, mask, objectMask);
            for (int k = m_Contributions.Count - 1; k >= first; k--)
            {
                var entry = m_Contributions[k];
                if (weight <= 0f) m_Contributions.RemoveAt(k);
                else m_Contributions[k] = (entry.Layer, entry.LayerIndex, entry.Run, entry.Clip, entry.Weight * weight, entry.HumanSample);
            }
        }

        /// <summary>In a transition, a motion state with write defaults has the default value for
        /// what the other state keeps without write defaults, beyond its own layer's channels
        /// (measured in override layers: the channels only the base layer animates blend towards
        /// their defaults, which neither state restores alone).</summary>
        void FillWhereKept(LayerRun layer, int layerIndex, StateRun run, float weight, AnimationPose pose, bool[] mask)
        {
            if (weight <= 0f || !run.Def.WriteDefaults) return;
            var motion = MotionOf(layer.Def, run.Def);
            if (motion == null) return;
            var (animated, _) = MotionMask(motion, Mirrored(run.Def));
            var (inLayer, _) = LayerChannels(layerIndex);
            for (int c = 0; c < Bindings.FloatCount; c++)
            {
                if ((c < animated.Length && animated[c]) || (c < inLayer.Length && inLayer[c]) || (mask != null && !mask[c])) continue;
                if (pose.Keep[c] > 0f) pose.Fill[c] += weight;
            }
        }

        /// <summary>Remembers an additive layer's pose, its rotations as <see cref="PoseWriter.AdditiveRotation"/> applies them.</summary>
        void KeepLast(LayerRun layer, AnimationPose pose)
        {
            int n = Bindings.FloatCount;
            EnsureLast(layer);
            for (int c = 0; c < n; c++)
            {
                layer.Held[c] = pose.Weight[c] > 0f;
                layer.Last[c] = pose.Value[c];
                var owner = Bindings.FloatOwner[c];
                if (!layer.Held[c] || !owner.IsQuaternion || owner.First != c || owner.Width != 4 || c + 3 >= n) continue;
                if (!PoseWriter.AdditiveRotation(pose, c, 1f, out var d)) d = new Quaternion(0f, 0f, 0f, 0f);
                for (int i = 0; i < 4; i++) layer.Held[c + i] = true;
                layer.Last[c] = d.x; layer.Last[c + 1] = d.y; layer.Last[c + 2] = d.z; layer.Last[c + 3] = d.w;
                c += 3;
            }
        }

        void EnsureLast(LayerRun layer)
        {
            int n = Bindings.FloatCount;
            if (layer.Last.Length >= n) return;
            Array.Resize(ref layer.Last, n);
            Array.Resize(ref layer.Held, n);
        }

        /// <summary>Adds "no difference" (0, a rotation's identity) with a weight to the channels in
        /// <paramref name="channels"/> that <paramref name="except"/> and the mask leave (only those
        /// without weight yet when <paramref name="onlyUnweighted"/>).</summary>
        void AddNoDifference(AnimationPose pose, float weight, bool[] channels, bool[] except, bool[] mask, bool onlyUnweighted = false)
        {
            int n = Math.Min(Bindings.FloatCount, channels.Length);
            for (int c = 0; c < n; c++)
            {
                if (!channels[c] || (except != null && c < except.Length && except[c]) || (mask != null && !mask[c])) continue;
                if (onlyUnweighted && pose.Weight[c] > 0f) continue;
                var owner = Bindings.FloatOwner[c];
                if (owner.IsQuaternion && c == owner.First + 3)
                {
                    float dot = pose.Value[c];
                    pose.Value[c] += dot < 0f ? -weight : weight;
                }
                pose.Weight[c] += weight;
            }
        }

        /// <summary>A state without a motion and without write defaults keeps the layer's last values.</summary>
        void SampleLast(LayerRun layer, float weight, AnimationPose pose)
        {
            pose.Total += weight;
            int n = Math.Min(Bindings.FloatCount, layer.Last.Length);
            for (int c = 0; c < n; c++)
            {
                if (!layer.Held[c]) continue;
                var owner = Bindings.FloatOwner[c];
                if (owner.IsQuaternion && owner.First == c && owner.Width == 4 && c + 3 < n)
                {
                    float x = layer.Last[c], y = layer.Last[c + 1], z = layer.Last[c + 2], w = layer.Last[c + 3];
                    if (pose.Value[c] * x + pose.Value[c + 1] * y + pose.Value[c + 2] * z + pose.Value[c + 3] * w < 0f) { x = -x; y = -y; z = -z; w = -w; }
                    pose.Value[c] += weight * x; pose.Value[c + 1] += weight * y; pose.Value[c + 2] += weight * z; pose.Value[c + 3] += weight * w;
                    for (int i = 0; i < 4; i++) pose.Weight[c + i] += weight;
                    c += 3;
                    continue;
                }
                pose.Value[c] += weight * layer.Last[c];
                pose.Weight[c] += weight;
            }
        }

        bool[] m_OutputBound = Array.Empty<bool>(), m_OutputObjectBound = Array.Empty<bool>();

        void SampleState(LayerRun layer, int layerIndex, StateRun run, float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            if (weight <= 0f) return;
            var motion = MotionOf(layer.Def, run.Def);
            double normalized = run.Normalized + run.Def.CycleOffset + (run.Def.CycleOffsetParameter >= 0 ? Floats[run.Def.CycleOffsetParameter] : 0f);
            bool mirror = Mirrored(run.Def);
            // The state's humanoid clips place their IK goals and blend them among themselves
            // before the state joins the layer's pose (additive layers leave the goals alone).
            m_SamplingGoals = m_PlacedHuman != null && !layer.Def.Additive ? m_StateGoals ??= new Unbound.Runtime.Humanoid.HumanStateGoals(Bindings) : null;
            m_SamplingGoals?.BeginState();
            m_SamplingStateWeight = weight;
            SampleMotion(layer, layerIndex, run, motion, mirror, normalized, weight, pose, mask, objectMask);
            m_SamplingGoals?.EndState(pose, mask, weight, humanoid: AnimatesGoals(motion, mirror));
            m_SamplingGoals = null;
            if (layer.Def.Additive)
            {
                // Additive poses are differences. Without write defaults, a state without a motion
                // keeps the layer's last pose, and a motion's state adds no difference for what the
                // layer animates and it does not (with its weight); with write defaults they add nothing.
                if (run.Def.WriteDefaults) return;
                if (motion == null) { SampleLast(layer, weight, pose); return; }
                var (own, _) = MotionMask(motion, mirror);
                var (layerFloats, _) = LayerChannels(layerIndex);
                AddNoDifference(pose, weight, layerFloats, own, mask);
                return;
            }
            // What this state does not animate (see PoseWriter.CombineLayers): with write defaults,
            // what the layer's motions animate; without, every channel of the controller, which the
            // layer then has the value below for. A state without a motion and without write
            // defaults keeps the layer's last values, which so include channels only the layers
            // below animate; where the layer has none yet, the default value while another state
            // has the channel, else the value below (measured: entered from a motion state it
            // holds the base layer's channels as they were; as the first state it leaves them).
            bool holds = motion == null && !run.Def.WriteDefaults;
            if (holds) SampleLast(layer, weight, pose);
            var (animated, animatedObjects) = MotionMask(motion, mirror);
            var (inLayer, objectsInLayer) = LayerChannels(layerIndex);
            var leftAs = holds ? pose.Fill : !run.Def.WriteDefaults ? pose.Keep : motion != null ? pose.Fill : pose.Rest;
            // The base layer's write defaults also cover channels bound by the Animator's outputs.
            bool outputBound = Attached && layerIndex == 0 && run.Def.WriteDefaults
                && Unbound.Runtime.Playables.AnimationOutputs.BoundChannels(m_Animator, ref m_OutputBound, ref m_OutputObjectBound,
                    Bindings.FloatCount, Bindings.ObjectCount);
            for (int c = 0; c < Bindings.FloatCount; c++)
            {
                if ((c < animated.Length && animated[c]) || (mask != null && !mask[c])) continue;
                if (run.Def.WriteDefaults && (c >= inLayer.Length || !inLayer[c]) && !(outputBound && m_OutputBound[c])) continue;
                if (holds && c < layer.Held.Length && layer.Held[c]) continue;
                leftAs[c] += weight;
            }
            if (!run.Def.WriteDefaults || layerIndex > 0) return;
            for (int c = 0; c < Bindings.ObjectCount && c < objectsInLayer.Length; c++)
            {
                if (animatedObjects[c] || (!objectsInLayer[c] && !(outputBound && m_OutputObjectBound[c]))
                    || (objectMask != null && !objectMask[c])) continue;
                if (weight > pose.ObjectWeight[c])
                {
                    pose.Objects[c] = Bindings.ObjectDefaults[c];
                    pose.ObjectWeight[c] = weight;
                }
            }
        }

        /// <summary>A state is mirrored by its mirror flag, or by its bool parameter when
        /// mirrorParameterActive is set (AnimatorState.mirror, mirrorParameter).</summary>
        internal bool Mirrored(StateDef def) => def.MirrorParameter >= 0 ? Bools[def.MirrorParameter] : def.Mirror;

        /// <summary>A mirrored state plays its looping humanoid clips half a cycle later (measured,
        /// tests/projects/humanoid: its pose and root motion at a normalized time are the mirrored
        /// clip's at that time + 0.5, for clips of 1 s and 2/3 s; a blend tree child's mirror does not
        /// shift it).</summary>
        internal const float MirrorCycleOffset = 0.5f;

        /// <summary>How much later a state plays a clip because the state is mirrored
        /// (<see cref="MirrorCycleOffset"/> for humanoid clips, nothing otherwise): its pose, root
        /// motion and events alike (events measured, tests/projects/humanoid eventsMirror*).</summary>
        double MirrorShift(StateDef def, AnimationClip clip) => Mirrored(def) && clip.HasHumanCurves ? MirrorCycleOffset : 0.0;

        /// <summary>The clip a state plays: mirrored (a blend tree child's mirror toggles the
        /// state's), a humanoid clip as <see cref="AnimationClip.StateMirrored"/>.</summary>
        AnimationClip StateClip(AnimationClip original, bool mirror)
        {
            var clip = Controller.MapClip(original);
            if (clip == null || !mirror || clip.m_Legacy || !clip.HasHumanCurves) return clip;
            return clip.StateMirrored;
        }

        void SampleMotion(LayerRun layer, int layerIndex, StateRun run, Motion motion, bool mirror, double normalized, float weight, AnimationPose pose,
            bool[] mask, bool[] objectMask)
        {
            if (weight <= 0f) return;
            switch (motion)
            {
                case AnimationClip original:
                {
                    var clip = StateClip(original, mirror);
                    if (clip == null) return;
                    if (clip.m_Legacy)
                    {
                        if (m_WarnedLegacy.Add(clip))
                            Debug.LogWarning($"The AnimationClip '{clip.name}' used by the Animator '{m_Animator.name}' must not be legacy.", m_Animator);
                        return;
                    }
                    var bound = Bindings.Bind(clip);
                    if (pose.Value.Length < Bindings.FloatCount || pose.Objects.Length < Bindings.ObjectCount) return; // bound after the poses were sized
                    // Cycle offsets (the clip's, the state's and blend trees') shift looping clips
                    // only; a clip without loop time plays from the state's time (measured,
                    // tests/projects/modelclips).
                    // The clip reads the state's time (StateClipNormalized), then its cycle offsets.
                    double read = ClipSampling.StateClipNormalized(run.Normalized, clip.length);
                    double time = clip.isLooping ? read + (normalized - run.Normalized) + MirrorShift(run.Def, clip) + clip.m_CycleOffset : read;
                    // A humanoid clip of the Animator's own controller: its root motion gives the body
                    // its goals are placed by (additive layers take neither, see LayerMask).
                    int humanSample = -1;
                    var goals = clip.HasHumanCurves ? m_SamplingGoals : null;
                    if (goals != null)
                    {
                        m_PlacedHuman.SampleMotion(clip, clip.HumanClipMotion, HumanInput(run, clip), out var sample);
                        humanSample = m_HumanClipSamples.Count;
                        m_HumanClipSamples.Add(sample);
                        m_HumanClipShares.Add(weight / m_SamplingStateWeight);
                        goals.BeginClip(sample.Body);
                    }
                    ClipSampling.Sample(bound, ClipSampling.ClipTime(clip, time, clip.isLooping), weight, pose, mask, objectMask,
                        layer.Def.Additive ? Bindings.AdditiveReference(bound) : null, loopPose: true, goals);
                    if (goals != null)
                    {
                        // The clip is a whole human pose: what it has no curve for is at rest.
                        ClipSampling.CompleteHuman(bound, Bindings, weight, pose, mask);
                        goals.AddClip(weight);
                    }
                    m_Contributions.Add((layer, layerIndex, run, clip, weight, humanSample));
                    break;
                }
                case BlendTreeMotion tree:
                {
                    if (tree.BlendType == 4) { SampleDirect(layer, layerIndex, run, tree, mirror, normalized, weight, pose, mask, objectMask); break; }
                    var weights = BlendWeights(tree);
                    bool any = false;
                    for (int i = 0; i < weights.Length; i++)
                        if (weights[i] > 0f)
                        {
                            any = true;
                            SampleMotion(layer, layerIndex, run, tree.Children[i].Motion, mirror ^ tree.Children[i].Mirror,
                                normalized + tree.Children[i].CycleOffset, weight * weights[i], pose, mask, objectMask);
                        }
                    if (!any && !layer.Def.Additive) SampleEmptyTree(tree, mirror, weight, pose, mask, objectMask);
                    break;
                }
            }
        }

        /// <summary>The channels a motion animates (played mirrored or not).</summary>
        (bool[] Floats, bool[] Objects) MotionMask(Motion motion, bool mirror)
        {
            int fc = Bindings.FloatCount, oc = Bindings.ObjectCount;
            var cached = motion == null ? m_NullMotionMask : m_MotionMasks.TryGetValue((motion, mirror), out var found) ? found : default;
            if (cached.Floats != null && cached.FloatCount == fc && cached.ObjectCount == oc)
                return (cached.Floats, cached.Objects);
            var floats = new bool[fc];
            var objects = new bool[oc];
            Collect(motion, mirror, floats, objects, 0);
            if (motion == null) m_NullMotionMask = (floats, objects, fc, oc);
            else m_MotionMasks[(motion, mirror)] = (floats, objects, fc, oc);
            return (floats, objects);
        }

        (bool[] Floats, bool[] Objects, int FloatCount, int ObjectCount) m_NullMotionMask;
        (bool[] Floats, bool[] Objects, int FloatCount, int ObjectCount)[] m_LayerChannels = Array.Empty<(bool[], bool[], int, int)>();

        /// <summary>The channels a layer's motions animate (a synced layer's: its own motions), the
        /// ones its write-defaults states restore.</summary>
        (bool[] Floats, bool[] Objects) LayerChannels(int index)
        {
            int fc = Bindings.FloatCount, oc = Bindings.ObjectCount;
            if (m_LayerChannels.Length < Layers.Length) Array.Resize(ref m_LayerChannels, Layers.Length);
            var cached = m_LayerChannels[index];
            if (cached.Floats != null && cached.FloatCount == fc && cached.ObjectCount == oc) return (cached.Floats, cached.Objects);
            var def = Layers[index].Def;
            var states = def.SyncedLayerIndex >= 0 && def.SyncedLayerIndex < Layers.Length ? Layers[def.SyncedLayerIndex].Def.States : def.States;
            // A state mirrored through its parameter plays its motion either way.
            foreach (var state in states)
                for (int m = 0; m < 2; m++)
                    if (CanMirror(state, m == 1)) MotionMask(MotionOf(def, state), m == 1); // binds what is not bound yet
            fc = Bindings.FloatCount; oc = Bindings.ObjectCount;
            var floats = new bool[fc];
            var objects = new bool[oc];
            foreach (var state in states)
                for (int m = 0; m < 2; m++)
                {
                    if (!CanMirror(state, m == 1)) continue;
                    var (f, o) = MotionMask(MotionOf(def, state), m == 1);
                    for (int c = 0; c < fc; c++) floats[c] |= f[c];
                    for (int c = 0; c < oc; c++) objects[c] |= o[c];
                }
            m_LayerChannels[index] = (floats, objects, fc, oc);
            return (floats, objects);
        }

        static bool CanMirror(StateDef state, bool mirror) => state.MirrorParameter >= 0 || state.Mirror == mirror;

        void Collect(Motion motion, bool mirror, bool[] floats, bool[] objects, int depth)
        {
            if (depth > 16) return;
            switch (motion)
            {
                case AnimationClip original:
                {
                    var clip = StateClip(original, mirror);
                    if (clip == null || clip.m_Legacy) return;
                    var bound = Bindings.Bind(clip);
                    foreach (var ch in bound.Float) if (ch >= 0 && ch < floats.Length) floats[ch] = true;
                    if (bound.EulerRotation != null)
                        foreach (var ch in bound.EulerRotation) if (ch >= 0 && ch + 3 < floats.Length) floats[ch + 3] = true;
                    foreach (var ch in bound.Object) if (ch >= 0 && ch < objects.Length) objects[ch] = true;
                    // A humanoid clip gives the whole human pose, with or without a curve for each
                    // part: the body and every IK goal (HumanStateGoals), muscle and translation
                    // (ClipSampling.CompleteHuman; a clip with body curves alone leaves those).
                    if (clip.HasHumanCurves && m_MasksPlaceGoals)
                        for (int curve = Unbound.Runtime.Humanoid.HumanClipImport.RootFirst,
                             end = clip.humanMotion ? Unbound.Runtime.Humanoid.HumanClipImport.CurveCount : Unbound.Runtime.Humanoid.HumanClipImport.MuscleFirst;
                             curve < end; curve++)
                        {
                            int ch = Bindings.HumanChannel(curve);
                            if (ch >= 0 && ch < floats.Length) floats[ch] = true;
                        }
                    break;
                }
                case BlendTreeMotion tree:
                    foreach (var c in tree.Children) Collect(c.Motion, mirror ^ c.Mirror, floats, objects, depth + 1);
                    break;
            }
        }

        /// <summary>The channels a layer may animate (null: all): what its avatar mask lets through
        /// and, on an additive layer of a player that places goals (<see cref="PlacesGoals"/>), not
        /// the humanoid IK goal curves. An additive layer leaves the goals of the layers below as
        /// they are, whatever its clips' goal curves hold and with or without a mask (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/humanoid goals.additive, goals.additiveFootIK;
        /// AnimationLayerMixerPlayables likewise, mixer.additiveNoRoot). An
        /// AnimatorControllerPlayable's additive layers still add their goal curves' differences
        /// (unmeasured).</summary>
        (bool[], bool[]) LayerMask(int index)
        {
            var mask = Layers[index].Def.Mask;
            bool filters = mask != null && mask.Filters;
            bool noGoals = m_MasksPlaceGoals && Layers[index].Def.Additive && AnyGoalChannel();
            if (!filters && !noGoals) return (null, null);
            var cached = m_LayerMasks[index];
            if (cached.Floats != null && cached.FloatCount == Bindings.FloatCount && cached.Objects.Length == Bindings.ObjectCount)
                return (cached.Floats, filters ? cached.Objects : null);
            var floats = new bool[Bindings.FloatCount];
            var objects = new bool[Bindings.ObjectCount];
            var root = Bindings.Root != null ? Bindings.Root.transform : null;
            for (int c = 0; c < floats.Length; c++)
            {
                var owner = Bindings.FloatOwner[c];
                floats[c] = (!filters || mask.AllowsGroup(root, owner))
                    && !(noGoals && owner != null && Unbound.Runtime.Humanoid.HumanStateGoals.Slot(owner.HumanCurve) >= 0);
            }
            for (int c = 0; c < objects.Length; c++) objects[c] = !filters || mask.AllowsGroup(root, Bindings.ObjectOwner[c]);
            m_LayerMasks[index] = (floats, objects, Bindings.FloatCount);
            return (floats, filters ? objects : null);
        }

        /// <summary>Whether a motion gives humanoid IK goals: one of its clips is humanoid
        /// (<see cref="Collect"/> marks every goal curve for it).</summary>
        bool AnimatesGoals(Motion motion, bool mirror)
        {
            if (motion == null) return false;
            var (animated, _) = MotionMask(motion, mirror);
            int c = Bindings.HumanChannel(Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst);
            return c >= 0 && c < animated.Length && animated[c];
        }

        /// <summary>Whether any humanoid IK goal curve is bound.</summary>
        bool AnyGoalChannel()
        {
            for (int curve = Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst; curve < Unbound.Runtime.Humanoid.HumanClipImport.MuscleFirst; curve++)
                if (Bindings.HumanChannel(curve) >= 0) return true;
            return false;
        }

        // ------------------------------------------------------------------ events and callbacks

        void FireEvents()
        {
            // Event handlers can update this Animator again: iterate taken lists, not the live ones.
            var left = m_Left;
            m_Left = m_LeftSpare ?? new List<(LayerRun, StateRun, double)>();
            m_LeftSpare = null;
            var contributions = m_ContributionsSpare ?? new List<(LayerRun, int, StateRun, AnimationClip, float, int)>();
            m_ContributionsSpare = null;
            try
            {
                if (!m_Animator.m_FireEvents) return;
                foreach (var (layer, run, end) in left)
                {
                    if (!(MotionOf(layer.Def, run.Def) is AnimationClip leftClip) || leftClip.m_Events.Count == 0) continue;
                    double leftOffset = leftClip.isLooping ? run.Def.CycleOffset + leftClip.m_CycleOffset + MirrorShift(run.Def, leftClip) : 0.0;
                    FireClipEvents(leftClip, StateInfo(layer, run), 1f, Single(run.Previous + leftOffset), Single(end + leftOffset),
                        run.Fresh && !run.EventsStarted);
                }
                contributions.AddRange(m_Contributions);
                foreach (var (layer, layerIndex, run, played, weight, _) in contributions)
                {
                    var clip = played.Asset;
                    if (clip.m_Events.Count == 0 || weight <= 0f) continue;
                    double offset = clip.isLooping ? run.Def.CycleOffset + clip.m_CycleOffset + MirrorShift(run.Def, played) : 0.0;
                    FireClipEvents(clip, StateInfo(layer, run), weight, Single(run.Previous + offset), Single(run.Normalized + offset),
                        run.Fresh && !run.EventsStarted);
                }
            }
            finally
            {
                left.Clear();
                m_LeftSpare = left;
                contributions.Clear();
                m_ContributionsSpare = contributions;
            }
        }

        /// <summary>A clip's events crossed by a state step, sent with the state's information
        /// (only called for clips with events: its closure is allocated per call).</summary>
        void FireClipEvents(AnimationClip clip, AnimatorStateInfo info, float weight, double from, double to, bool includeStart)
        {
            var go = m_Animator.m_GameObject;
            var clipInfo = new AnimatorClipInfo { m_Clip = clip, m_Weight = weight };
            AnimationEvents.FireCrossed(clip, from, to, clip.isLooping, includeStart, e =>
            {
                var fired = e.Copy();
                fired.m_Source = AnimationEventSource.Animator;
                fired.m_AnimatorStateInfo = info;
                fired.m_AnimatorClipInfo = clipInfo;
                AnimationEvents.Dispatch(go, fired, clip);
            });
        }

        /// <summary>A state time as the Editor compares event times with it: in single precision
        /// (a state offset that lands exactly on an event fires it).</summary>
        static double Single(double normalized) => (float)normalized;

        internal AnimatorStateInfo StateInfo(LayerRun layer, StateRun run)
        {
            if (run == null) return default;
            var def = run.Def;
            var motion = MotionOf(layer.Def, def);
            return new AnimatorStateInfo
            {
                m_Name = def.NameHash,
                m_Path = def.LayerPathHash,
                m_FullPath = def.FullPathHash,
                m_NormalizedTime = (float)run.Normalized,
                m_Length = InfoLength(layer, def, motion),
                m_Speed = def.Speed,
                m_SpeedMultiplier = def.SpeedParameter >= 0 ? Floats[def.SpeedParameter] : 1f,
                m_Tag = def.TagHash,
                m_Loop = MotionLoops(motion) ? 1 : 0,
            };
        }

        internal AnimatorTransitionInfo TransitionInfo(LayerRun layer)
        {
            var t = layer.Transition;
            if (t == null) return default;
            return new AnimatorTransitionInfo
            {
                m_FullPath = t.FullPathHash,
                m_UserName = t.UserNameHash,
                m_Name = t.NameHash,
                m_HasFixedDuration = t.FixedDuration,
                m_Duration = t.NominalDuration,
                m_NormalizedTime = t.Progress,
                m_AnyState = t.AnyState,
            };
        }

        StateMachineBehaviour Instance(StateMachineBehaviour asset)
        {
            if (asset == null) return null;
            if (m_Instances.TryGetValue(asset, out var instance)) return instance;
            ScriptMessages.WarnNotSent(asset.GetType());
            if (asset.GetType().IsDefined(typeof(SharedBetweenAnimatorsAttribute), true)) instance = asset;
            else
            {
                try { instance = Object.Instantiate(asset); }
                catch (Exception e) { Debug.LogException(e); instance = asset; }
            }
            m_Instances[asset] = instance;
            AllBehaviours.Add(instance);
            return instance;
        }

        /// <summary>Instantiates every behaviour of the controller (GetBehaviour finds them before
        /// their states run).</summary>
        internal void InstantiateBehaviours()
        {
            foreach (var layer in Graph.Layers)
            {
                foreach (var s in layer.States) foreach (var b in s.Behaviours) Instance(b);
                foreach (var list in layer.SyncedBehaviours.Values) foreach (var b in list) Instance(b);
                if (layer.Root != null) InstantiateMachine(layer.Root);
            }
        }

        void InstantiateMachine(MachineDef m)
        {
            foreach (var b in m.Behaviours) Instance(b);
            foreach (var c in m.Children) InstantiateMachine(c);
        }

        /// <summary>A state's behaviours, then those of the machines containing it (a synced
        /// layer: its own behaviours of the state only).</summary>
        StateBehaviours BehavioursOf(LayerRun layer, StateDef state)
        {
            if (layer.Def.SyncedLayerIndex < 0) return new StateBehaviours(this, state.Behaviours, state.Machine);
            return new StateBehaviours(this,
                layer.Def.SyncedBehaviours.TryGetValue(state, out var synced) ? synced : Array.Empty<StateMachineBehaviour>(), null);
        }

        /// <summary>The behaviour instances of <see cref="BehavioursOf"/>, enumerated without
        /// allocating (it runs every frame for state update and move callbacks).</summary>
        readonly struct StateBehaviours
        {
            readonly AnimatorPlayer m_Player;
            readonly StateMachineBehaviour[] m_Own;
            readonly MachineDef m_Machine;

            internal StateBehaviours(AnimatorPlayer player, StateMachineBehaviour[] own, MachineDef machine)
            {
                m_Player = player;
                m_Own = own;
                m_Machine = machine;
            }

            public Enumerator GetEnumerator() => new Enumerator(m_Player, m_Own, m_Machine);

            internal struct Enumerator
            {
                readonly AnimatorPlayer m_Player;
                StateMachineBehaviour[] m_Assets;
                MachineDef m_Next;
                int m_Index;

                internal Enumerator(AnimatorPlayer player, StateMachineBehaviour[] own, MachineDef machine)
                {
                    m_Player = player;
                    m_Assets = own;
                    m_Next = machine;
                    m_Index = -1;
                    Current = null;
                }

                public StateMachineBehaviour Current { get; private set; }

                public bool MoveNext()
                {
                    while (true)
                    {
                        if (m_Assets != null && ++m_Index < m_Assets.Length)
                        {
                            Current = m_Player.Instance(m_Assets[m_Index]);
                            return true;
                        }
                        if (m_Next == null) return false;
                        m_Assets = m_Next.Behaviours;
                        m_Next = m_Next.Parent;
                        m_Index = -1;
                    }
                }
            }
        }

        internal StateMachineBehaviour[] Behaviours(int fullPathHash, int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return Array.Empty<StateMachineBehaviour>();
            var layer = Layers[layerIndex];
            var def = layer.Def.SyncedLayerIndex >= 0 && layer.Def.SyncedLayerIndex < Layers.Length ? Layers[layer.Def.SyncedLayerIndex].Def : layer.Def;
            if (!def.StateByHash.TryGetValue(fullPathHash, out var state)) return Array.Empty<StateMachineBehaviour>();
            var result = new List<StateMachineBehaviour>();
            foreach (var b in BehavioursOf(layer, state)) result.Add(b);
            return result.ToArray();
        }

        void QueueEnter(LayerRun layer, StateRun run)
        {
            run.EnteredThisUpdate = true;
            foreach (var target in LayersPlaying(layer))
            {
                foreach (var b in BehavioursOf(target.Layer, run.Def))
                {
                    if (b == null) continue;
                    m_Callbacks.Add(new StateCallback(StateCallbackKind.Enter, b, StateInfo(target.Layer, run), target.Index, run, target.Index));
                }
            }
        }

        void ExitRun(LayerRun layer, StateRun run)
        {
            if (run == null) return;
            foreach (var target in LayersPlaying(layer))
            {
                foreach (var b in BehavioursOf(target.Layer, run.Def))
                {
                    if (b == null) continue;
                    m_Callbacks.Add(new StateCallback(StateCallbackKind.Exit, b, StateInfo(target.Layer, run), target.Index, run, target.Index));
                }
            }
        }

        void QueueMachine(LayerRun layer, MachineDef machine, bool enter)
        {
            foreach (var asset in machine.Behaviours)
            {
                var b = Instance(asset);
                if (b == null) continue;
                m_Callbacks.Add(new StateCallback(enter ? StateCallbackKind.MachineEnter : StateCallbackKind.MachineExit,
                    b, default, machine.FullPathHash, null, Array.IndexOf(Layers, layer)));
            }
        }

        /// <summary>A layer and the layers synced to it (they play its states with their behaviours).</summary>
        IEnumerable<(LayerRun Layer, int Index)> LayersPlaying(LayerRun layer)
        {
            int index = Array.IndexOf(Layers, layer);
            yield return (layer, index);
            for (int i = 0; i < Layers.Length; i++)
                if (Layers[i].Def.SyncedLayerIndex == index && i != index) yield return (Layers[i], i);
        }

        /// <summary>Orders an update's state callbacks layer by layer. Within a layer, the states
        /// that played before the step come first, in slot order (current, then next), each with
        /// OnStateExit if it left, else OnStateUpdate; then the layer's other callbacks (entered
        /// states, state machines) in the order the step queued them. A transition's start is
        /// "Update(source), Enter(destination)" and its end "Exit(source), Update(destination)"
        /// (6000.0.71f1, tests/projects/animcallbacks).</summary>
        void OrderCallbacks()
        {
            var queued = m_Callbacks;
            var ordered = s_CallbackLists.Count > 0 ? s_CallbackLists.Pop() : new List<StateCallback>();
            if (m_Taken.Length < queued.Count) m_Taken = new bool[Math.Max(queued.Count, m_Taken.Length * 2)];
            Array.Clear(m_Taken, 0, queued.Count);
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                var source = PlayedBy(layer);
                AppendPrevious(ordered, layer, source, source.PreviousCurrent, i);
                if (source.PreviousNext != source.PreviousCurrent) AppendPrevious(ordered, layer, source, source.PreviousNext, i);
                for (int k = 0; k < queued.Count; k++)
                    if (!m_Taken[k] && queued[k].Layer == i) { m_Taken[k] = true; ordered.Add(queued[k]); }
                // States neither previous nor entered in this step (none in a normal update).
                if (source.Current != source.PreviousCurrent && source.Current != source.PreviousNext) QueueUpdate(ordered, layer, source.Current, i);
                if (source.Next != source.PreviousCurrent && source.Next != source.PreviousNext && source.Next != source.Current)
                    QueueUpdate(ordered, layer, source.Next, i);
            }
            for (int k = 0; k < queued.Count; k++)
                if (!m_Taken[k]) ordered.Add(queued[k]);
            queued.Clear();
            s_CallbackLists.Push(queued);
            m_Callbacks = ordered;
        }

        bool[] m_Taken = Array.Empty<bool>();

        /// <summary>A state that played before the step: its queued OnStateExit when it left,
        /// else OnStateUpdate while it still plays.</summary>
        void AppendPrevious(List<StateCallback> ordered, LayerRun layer, LayerRun source, StateRun run, int layerIndex)
        {
            if (run == null) return;
            var queued = m_Callbacks;
            bool exited = false;
            for (int k = 0; k < queued.Count; k++)
            {
                var call = queued[k];
                if (m_Taken[k] || call.Layer != layerIndex || call.Kind != StateCallbackKind.Exit || call.Run != run) continue;
                m_Taken[k] = true;
                ordered.Add(call);
                exited = true;
            }
            if (!exited && (source.Current == run || source.Next == run)) QueueUpdate(ordered, layer, run, layerIndex);
        }

        void QueueUpdate(List<StateCallback> into, LayerRun layer, StateRun run, int layerIndex)
        {
            if (run == null || run.EnteredThisUpdate) return;
            foreach (var b in BehavioursOf(layer, run.Def))
                if (b != null) into.Add(new StateCallback(StateCallbackKind.Update, b, StateInfo(layer, run), layerIndex, run, layerIndex));
        }

        /// <summary>The <paramref name="index"/>th state an update of the layer evaluated (null past
        /// the last), in slot order: those that played
        /// before its step (also when they exited in it), then those entered. OnStateMove and
        /// OnStateIK reach each of them (6000.0.71f1, tests/projects/animcallbacks: a transition's
        /// source still gets OnStateMove in the update that completes it).</summary>
        static StateRun UpdateRun(LayerRun source, int index)
        {
            int seen = 0;
            for (int slot = 0; slot < 4; slot++)
            {
                var run = slot == 0 ? source.PreviousCurrent : slot == 1 ? source.PreviousNext : slot == 2 ? source.Current : source.Next;
                if (run == null || slot > 0 && source.PreviousCurrent == run || slot > 1 && source.PreviousNext == run
                    || slot > 2 && source.Current == run) continue;
                if (seen++ == index) return run;
            }
            return null;
        }

        enum StateCallbackKind : byte { Enter, Update, Exit, MachineEnter, MachineExit }

        /// <summary>A queued state machine behaviour callback (a struct, so queueing allocates
        /// nothing).</summary>
        readonly struct StateCallback
        {
            internal readonly StateCallbackKind Kind;
            internal readonly StateMachineBehaviour Behaviour;
            internal readonly AnimatorStateInfo Info;
            /// <summary>The layer index; for machine callbacks, the machine's full path hash.</summary>
            internal readonly int Argument;
            /// <summary>The state (null for machine callbacks) and the layer index it was queued for.</summary>
            internal readonly StateRun Run;
            internal readonly int Layer;

            internal StateCallback(StateCallbackKind kind, StateMachineBehaviour behaviour, AnimatorStateInfo info, int argument,
                StateRun run, int layer)
            {
                Kind = kind;
                Behaviour = behaviour;
                Info = info;
                Argument = argument;
                Run = run;
                Layer = layer;
            }
        }

        // A callback can update this Animator again reentrantly: each run takes its own list.
        static readonly Stack<List<StateCallback>> s_CallbackLists = new Stack<List<StateCallback>>();

        void RunCallbacks()
        {
            if (m_Callbacks.Count == 0) return;
            var calls = m_Callbacks;
            m_Callbacks = s_CallbackLists.Count > 0 ? s_CallbackLists.Pop() : new List<StateCallback>();
            try
            {
                for (int i = 0; i < calls.Count; i++)
                {
                    try { Invoke(calls[i]); }
                    catch (Exception e) { Debug.LogException(e, m_Animator); }
                }
            }
            finally
            {
                calls.Clear();
                s_CallbackLists.Push(calls);
            }
        }

        void Invoke(in StateCallback call)
        {
            var b = call.Behaviour;
            switch (call.Kind)
            {
                case StateCallbackKind.Enter:
                    b.OnStateEnter(m_Animator, call.Info, call.Argument);
                    b.OnStateEnter(m_Animator, call.Info, call.Argument, Playable);
                    break;
                case StateCallbackKind.Update:
                    b.OnStateUpdate(m_Animator, call.Info, call.Argument);
                    b.OnStateUpdate(m_Animator, call.Info, call.Argument, Playable);
                    break;
                case StateCallbackKind.Exit:
                    b.OnStateExit(m_Animator, call.Info, call.Argument);
                    b.OnStateExit(m_Animator, call.Info, call.Argument, Playable);
                    break;
                case StateCallbackKind.MachineEnter:
                    b.OnStateMachineEnter(m_Animator, call.Argument);
                    b.OnStateMachineEnter(m_Animator, call.Argument, Playable);
                    break;
                case StateCallbackKind.MachineExit:
                    b.OnStateMachineExit(m_Animator, call.Argument);
                    b.OnStateMachineExit(m_Animator, call.Argument, Playable);
                    break;
            }
        }

        // ------------------------------------------------------------------ requests

        internal void RequestPlay(int layerIndex, StateDef state, float time, bool fixedTime)
        {
            Layers[layerIndex].Pending = new PendingPlay { State = state, Time = time, FixedTime = fixedTime };
        }

        internal void RequestCrossFade(int layerIndex, StateDef state, float duration, bool fixedDuration, float offset, bool fixedTime, float transitionTime)
        {
            Layers[layerIndex].Pending = new PendingPlay
            {
                State = state,
                Time = offset,
                FixedTime = fixedTime,
                CrossFade = true,
                Duration = duration,
                FixedDuration = fixedDuration,
                TransitionTime = Mathf.Clamp01(transitionTime),
            };
        }

        /// <summary>The layer whose states include the hash (-1 for none).</summary>
        internal int FindLayer(int stateHash, int layer, out StateDef state)
        {
            state = null;
            if (layer >= 0)
            {
                if (layer >= Layers.Length) return -1;
                var def = Layers[layer].Def;
                // A synced layer plays its source layer's states: the request goes there.
                if (def.SyncedLayerIndex >= 0 && def.SyncedLayerIndex < Layers.Length) layer = def.SyncedLayerIndex;
                return Layers[layer].Def.StateByHash.TryGetValue(stateHash, out state) ? layer : -1;
            }
            for (int i = 0; i < Layers.Length; i++)
            {
                if (Layers[i].Def.SyncedLayerIndex >= 0) continue;
                if (Layers[i].Def.StateByHash.TryGetValue(stateHash, out state)) return i;
            }
            return -1;
        }
    }
}
