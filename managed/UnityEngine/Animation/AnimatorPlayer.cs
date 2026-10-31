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
        /// <summary>Entered since the last update: events at its start time fire.</summary>
        internal bool Fresh = true;
        /// <summary>Its transitions have not been checked yet: an exit time at the start counts.</summary>
        internal bool ExitInclusive = true;
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
        /// <summary>The source of a transition that completed in this update (see
        /// <see cref="AnimatorPlayer.BuildLayerPoses"/>); null otherwise.</summary>
        internal StateRun Completed;
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
        int m_OverrideVersion;

        readonly Dictionary<StateMachineBehaviour, StateMachineBehaviour> m_Instances = new Dictionary<StateMachineBehaviour, StateMachineBehaviour>();
        internal readonly List<StateMachineBehaviour> AllBehaviours = new List<StateMachineBehaviour>();
        readonly List<Action> m_Callbacks = new List<Action>();
        readonly Dictionary<string, int> m_ParameterIndex = new Dictionary<string, int>();
        readonly Dictionary<Motion, (bool[] Floats, bool[] Objects, int FloatCount, int ObjectCount)> m_MotionMasks = new Dictionary<Motion, (bool[], bool[], int, int)>();
        readonly HashSet<AnimationClip> m_WarnedLegacy = new HashSet<AnimationClip>();
        AnimationPose[] m_Poses = Array.Empty<AnimationPose>();
        LayerPose[] m_LayerPoses = Array.Empty<LayerPose>();
        /// <summary>The values this Animator last wrote (where Held): what states without write
        /// defaults keep, whatever game code set since (measured).</summary>
        float[] m_Written = Array.Empty<float>();
        bool[] m_WrittenHeld = Array.Empty<bool>();
        (bool[] Floats, bool[] Objects, int FloatCount)[] m_LayerMasks;
        readonly List<(LayerRun Layer, int LayerIndex, StateRun Run, AnimationClip Clip, float Weight)> m_Contributions =
            new List<(LayerRun, int, StateRun, AnimationClip, float)>();

        /// <summary>States left within this update through a transition that completed at once
        /// (no duration): the stretch they played before leaving, up to the exit point, still fires
        /// its events (measured: an event at a clip's end fires as an exit time of 1 leaves it).</summary>
        readonly List<(LayerRun Layer, StateRun Run, double End)> m_Left = new List<(LayerRun, StateRun, double)>();

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

        internal void Tick(float deltaTime)
        {
            if (m_Ticking) return;
            m_Ticking = true;
            try
            {
                TickCore(deltaTime);
            }
            finally
            {
                m_Ticking = false;
            }
        }

        bool m_Ticking;

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
                Step(layer, i, dt);
            }
        }

        /// <summary>Events and state callbacks. Controller playables call this after writing their
        /// output; the attached Animator runs it before movement callbacks and transform writes.</summary>
        internal void AfterEvaluate()
        {
            // Animator.Update(0) before the first advancing evaluation does not enter states.
            if (Attached && !m_CallbacksStarted) return;
            FireEvents();
            QueueUpdates();
            foreach (var layer in Layers)
            {
                if (layer.Current != null) { layer.Current.Fresh = false; layer.Current.EnteredThisUpdate = false; }
                if (layer.Next != null) { layer.Next.Fresh = false; layer.Next.EnteredThisUpdate = false; }
            }
            RunCallbacks();
        }

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

        void Step(LayerRun layer, int index, float dt)
        {
            layer.Completed = null;
            if (layer.Pending != null)
            {
                var pending = layer.Pending;
                layer.Pending = null;
                ApplyPending(layer, index, pending);
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
            Advance(layer, layer.Current, dt);
            if (layer.Next != null) Advance(layer, layer.Next, dt);
            if (layer.Transition != null)
            {
                var t = layer.Transition;
                t.Elapsed += dt;
                t.Progress = t.Duration > 0f ? (float)Math.Min(1.0, t.Elapsed / t.Duration) : 1f;
                if (t.Progress >= 1f) CompleteTransition(layer);
            }
            EvaluateTransitions(layer);
        }

        void Advance(LayerRun layer, StateRun run, float dt)
        {
            run.Previous = run.Normalized;
            var def = run.Def;
            if (def.TimeParameter >= 0)
            {
                run.Normalized = Floats[def.TimeParameter];
                return;
            }
            float length = StateLength(layer, run);
            run.Normalized += dt * SpeedOf(def) / length;
        }

        float SpeedOf(StateDef def) => def.Speed * (def.SpeedParameter >= 0 ? Floats[def.SpeedParameter] : 1f);

        void ApplyPending(LayerRun layer, int index, PendingPlay p)
        {
            var state = p.State;
            if (!p.CrossFade)
            {
                if (float.IsNegativeInfinity(p.Time) && layer.Current != null && layer.Current.Def == state && layer.Transition == null)
                    return; // already playing: it continues
                double n = float.IsNegativeInfinity(p.Time) ? 0.0 : p.FixedTime ? p.Time / MotionLength(layer, MotionOf(layer.Def, state)) : p.Time;
                ExitRun(layer, layer.Next);
                ExitRun(layer, layer.Current);
                layer.Next = null;
                layer.Transition = null;
                var run = new StateRun(state, n);
                SetCurrent(layer, run);
                QueueEnter(layer, run);
                return;
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
                return;
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

        void EvaluateTransitions(LayerRun layer)
        {
            var current = layer.Current;
            if (current == null) return;
            if (layer.Transition == null)
            {
                var chosen = FindTransition(layer, current, layer.Def.AnyState, null);
                if (chosen == null)
                    chosen = FindTransition(layer, current, current.Def.Transitions, null);
                current.ExitInclusive = false;
                if (chosen != null) StartTransition(layer, chosen, current, ExitOvershoot(layer, current, chosen));
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
            StartTransition(layer, interrupt, layer.Current);
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
        /// The exit time is crossed this update. Below 1 it recurs every loop (k + exitTime); from 1 up
        /// it is crossed once.
        /// </summary>
        static bool ExitTimeReached(StateRun run, float exitTime)
        {
            double from = run.Previous, to = run.Normalized;
            bool inclusive = run.ExitInclusive;
            if (to < from) return false;
            if (exitTime >= 1f) return inclusive ? from <= exitTime && exitTime <= to : from < exitTime && exitTime <= to;
            double k = Math.Max(0.0, Math.Ceiling(from - exitTime));
            if (!inclusive && k + exitTime <= from) k += 1.0;
            return k + exitTime <= to;
        }

        /// <summary>The time (seconds) an update ran past the exit time a transition left at: the
        /// transition starts there, its destination and progress already that far (Unity's
        /// sub-frame start; transitions on conditions alone start at the update's end).</summary>
        double ExitOvershoot(LayerRun layer, StateRun run, TransitionDef t)
        {
            if (!t.HasExitTime || run.Def.TimeParameter >= 0) return 0.0;
            double crossing = ExitCrossing(run, t.ExitTime);
            if (double.IsNaN(crossing) || crossing > run.Normalized) return 0.0;
            float speed = Math.Abs(SpeedOf(run.Def));
            if (speed <= 1e-6f) return 0.0;
            return Math.Max(0.0, (run.Normalized - crossing) * StateLength(layer, run) / speed);
        }

        /// <summary>The normalized time of the exit time crossed this update (as <see cref="ExitTimeReached"/>).</summary>
        static double ExitCrossing(StateRun run, float exitTime)
        {
            double from = run.Previous, to = run.Normalized;
            if (to < from) return double.NaN;
            if (exitTime >= 1f) return exitTime;
            double k = Math.Max(0.0, Math.Ceiling(from - exitTime));
            if (!run.ExitInclusive && k + exitTime <= from) k += 1.0;
            return k + exitTime;
        }

        void StartTransition(LayerRun layer, TransitionDef t, StateRun from, double overshoot = 0.0)
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

        /// <summary>A state's length in seconds (the motion's; 1 for a state without one).</summary>
        internal float StateLength(LayerRun layer, StateRun run) => MotionLength(layer, MotionOf(layer.Def, run.Def));

        /// <summary>A state's duration at its speed: what normalized transition durations scale.</summary>
        float StateDuration(LayerRun layer, StateRun run)
        {
            float speed = Math.Abs(SpeedOf(run.Def));
            return StateLength(layer, run) / Math.Max(speed, 1e-4f);
        }

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
                    var weights = BlendWeights(tree);
                    float sum = 0f, total = 0f;
                    for (int i = 0; i < tree.Children.Count; i++)
                    {
                        if (weights[i] <= 0f) continue;
                        var child = tree.Children[i];
                        sum += weights[i] * MotionLength(layer, child.Motion) / Math.Max(Math.Abs(child.TimeScale), 1e-4f);
                        total += weights[i];
                    }
                    if (total > 0f) return sum / total;
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
            int count = BuildLayerPoses();
            ComputeRootMotion(count);
            AfterEvaluate();
            ProcessRootMotion();
            if (m_Written.Length < Bindings.FloatCount)
            {
                Array.Resize(ref m_Written, Bindings.FloatCount);
                Array.Resize(ref m_WrittenHeld, Bindings.FloatCount);
            }
            PoseWriter.WriteLayers(Bindings, m_LayerPoses, count, m_Written, m_WrittenHeld);
            if (Attached) m_Animator.CaptureRootMotionPose();
        }

        int BuildLayerPoses()
        {
            int count = Layers.Length;
            if (m_Poses.Length < count)
            {
                Array.Resize(ref m_Poses, count);
                m_LayerPoses = new LayerPose[count];
            }
            m_Contributions.Clear();
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
                    if (i == 0 && transitioning && EmptyWithDefaults(layer, source.Current) && MotionOf(layer.Def, source.Next.Def) != null)
                        SampleAtFullWeight(layer, i, source.Next, p, pose, mask, objectMask);
                    else if (i == 0 && transitioning && EmptyWithDefaults(layer, source.Next) && MotionOf(layer.Def, source.Current.Def) != null)
                        SampleAtFullWeight(layer, i, source.Current, 1f - p, pose, mask, objectMask);
                    else if (completed != null && EmptyWithDefaults(layer, source.Current) && MotionOf(layer.Def, completed.Def) != null)
                        SampleAtFullWeight(layer, i, completed, 0f, pose, mask, objectMask);
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
            return count;
        }

        bool EmptyWithDefaults(LayerRun layer, StateRun run) => run != null && run.Def.WriteDefaults && MotionOf(layer.Def, run.Def) == null;

        bool Holds(LayerRun layer, StateRun run) => run != null && !run.Def.WriteDefaults && MotionOf(layer.Def, run.Def) == null;

        /// <summary>The base layer's transitions between a state without a motion and with write
        /// defaults and a motion state: the former adds nothing, the motion state's values apply in
        /// full whatever the transition's progress, also in the update the transition completes
        /// (measured; alone, the state without a motion writes the defaults). Contributions (events,
        /// root motion) keep the state's transition weight.</summary>
        void SampleAtFullWeight(LayerRun layer, int layerIndex, StateRun run, float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            int first = m_Contributions.Count;
            SampleState(layer, layerIndex, run, 1f, pose, mask, objectMask);
            for (int k = m_Contributions.Count - 1; k >= first; k--)
            {
                var entry = m_Contributions[k];
                if (weight <= 0f) m_Contributions.RemoveAt(k);
                else m_Contributions[k] = (entry.Layer, entry.LayerIndex, entry.Run, entry.Clip, entry.Weight * weight);
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
            var (animated, _) = MotionMask(motion);
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

        void SampleState(LayerRun layer, int layerIndex, StateRun run, float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            if (weight <= 0f) return;
            var motion = MotionOf(layer.Def, run.Def);
            double normalized = run.Normalized + run.Def.CycleOffset + (run.Def.CycleOffsetParameter >= 0 ? Floats[run.Def.CycleOffsetParameter] : 0f);
            SampleMotion(layer, layerIndex, run, motion, normalized, weight, pose, mask, objectMask);
            if (layer.Def.Additive)
            {
                // Additive poses are differences. Without write defaults, a state without a motion
                // keeps the layer's last pose, and a motion's state adds no difference for what the
                // layer animates and it does not (with its weight); with write defaults they add nothing.
                if (run.Def.WriteDefaults) return;
                if (motion == null) { SampleLast(layer, weight, pose); return; }
                var (own, _) = MotionMask(motion);
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
            var (animated, animatedObjects) = MotionMask(motion);
            var (inLayer, objectsInLayer) = LayerChannels(layerIndex);
            var leftAs = holds ? pose.Fill : !run.Def.WriteDefaults ? pose.Keep : motion != null ? pose.Fill : pose.Rest;
            for (int c = 0; c < Bindings.FloatCount; c++)
            {
                if ((c < animated.Length && animated[c]) || (mask != null && !mask[c])) continue;
                if (run.Def.WriteDefaults && (c >= inLayer.Length || !inLayer[c])) continue;
                if (holds && c < layer.Held.Length && layer.Held[c]) continue;
                leftAs[c] += weight;
            }
            if (!run.Def.WriteDefaults || layerIndex > 0) return;
            for (int c = 0; c < Bindings.ObjectCount && c < objectsInLayer.Length; c++)
            {
                if (animatedObjects[c] || !objectsInLayer[c] || (objectMask != null && !objectMask[c])) continue;
                if (weight > pose.ObjectWeight[c])
                {
                    pose.Objects[c] = Bindings.ObjectDefaults[c];
                    pose.ObjectWeight[c] = weight;
                }
            }
        }

        void SampleMotion(LayerRun layer, int layerIndex, StateRun run, Motion motion, double normalized, float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            if (weight <= 0f) return;
            switch (motion)
            {
                case AnimationClip original:
                {
                    var clip = Controller.MapClip(original);
                    if (clip == null) return;
                    if (clip.m_Legacy)
                    {
                        if (m_WarnedLegacy.Add(clip))
                            Debug.LogWarning($"The AnimationClip '{clip.name}' used by the Animator '{m_Animator.name}' must not be legacy.", m_Animator);
                        return;
                    }
                    var bound = Bindings.Bind(clip);
                    if (pose.Value.Length < Bindings.FloatCount || pose.Objects.Length < Bindings.ObjectCount) return; // bound after the poses were sized
                    ClipSampling.Sample(bound, ClipSampling.ClipTime(clip, normalized, clip.isLooping), weight, pose, mask, objectMask,
                        layer.Def.Additive ? Bindings.AdditiveReference(bound) : null);
                    m_Contributions.Add((layer, layerIndex, run, clip, weight));
                    break;
                }
                case BlendTreeMotion tree:
                {
                    if (tree.BlendType == 4) { SampleDirect(layer, layerIndex, run, tree, normalized, weight, pose, mask, objectMask); break; }
                    var weights = BlendWeights(tree);
                    bool any = false;
                    for (int i = 0; i < weights.Length; i++)
                        if (weights[i] > 0f)
                        {
                            any = true;
                            SampleMotion(layer, layerIndex, run, tree.Children[i].Motion, normalized + tree.Children[i].CycleOffset, weight * weights[i], pose, mask, objectMask);
                        }
                    if (!any && !layer.Def.Additive) SampleEmptyTree(tree, weight, pose, mask, objectMask);
                    break;
                }
            }
        }

        (bool[] Floats, bool[] Objects) MotionMask(Motion motion)
        {
            int fc = Bindings.FloatCount, oc = Bindings.ObjectCount;
            var cached = motion == null ? m_NullMotionMask : m_MotionMasks.TryGetValue(motion, out var found) ? found : default;
            if (cached.Floats != null && cached.FloatCount == fc && cached.ObjectCount == oc)
                return (cached.Floats, cached.Objects);
            var floats = new bool[fc];
            var objects = new bool[oc];
            Collect(motion, floats, objects, 0);
            if (motion == null) m_NullMotionMask = (floats, objects, fc, oc);
            else m_MotionMasks[motion] = (floats, objects, fc, oc);
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
            foreach (var state in states) MotionMask(MotionOf(def, state)); // binds what is not bound yet
            fc = Bindings.FloatCount; oc = Bindings.ObjectCount;
            var floats = new bool[fc];
            var objects = new bool[oc];
            foreach (var state in states)
            {
                var (f, o) = MotionMask(MotionOf(def, state));
                for (int c = 0; c < fc; c++) floats[c] |= f[c];
                for (int c = 0; c < oc; c++) objects[c] |= o[c];
            }
            m_LayerChannels[index] = (floats, objects, fc, oc);
            return (floats, objects);
        }

        void Collect(Motion motion, bool[] floats, bool[] objects, int depth)
        {
            if (depth > 16) return;
            switch (motion)
            {
                case AnimationClip original:
                {
                    var clip = Controller.MapClip(original);
                    if (clip == null || clip.m_Legacy) return;
                    var bound = Bindings.Bind(clip);
                    foreach (var ch in bound.Float) if (ch >= 0 && ch < floats.Length) floats[ch] = true;
                    if (bound.EulerRotation != null)
                        foreach (var ch in bound.EulerRotation) if (ch >= 0 && ch + 3 < floats.Length) floats[ch + 3] = true;
                    foreach (var ch in bound.Object) if (ch >= 0 && ch < objects.Length) objects[ch] = true;
                    break;
                }
                case BlendTreeMotion tree:
                    foreach (var c in tree.Children) Collect(c.Motion, floats, objects, depth + 1);
                    break;
            }
        }

        /// <summary>The channels a layer's avatar mask lets it animate (null: all).</summary>
        (bool[], bool[]) LayerMask(int index)
        {
            var mask = Layers[index].Def.Mask;
            if (mask == null || mask.m_Elements.Count == 0) return (null, null);
            var cached = m_LayerMasks[index];
            if (cached.Floats != null && cached.FloatCount == Bindings.FloatCount && cached.Objects.Length == Bindings.ObjectCount)
                return (cached.Floats, cached.Objects);
            var floats = new bool[Bindings.FloatCount];
            var objects = new bool[Bindings.ObjectCount];
            var root = Bindings.Root != null ? Bindings.Root.transform : null;
            for (int c = 0; c < floats.Length; c++) floats[c] = Allowed(mask, root, Bindings.FloatOwner[c]);
            for (int c = 0; c < objects.Length; c++) objects[c] = Allowed(mask, root, Bindings.ObjectOwner[c]);
            m_LayerMasks[index] = (floats, objects, Bindings.FloatCount);
            return (floats, objects);
        }

        /// <summary>A transform mask masks Transform properties only: other components' properties
        /// are animated whatever the mask says about their object.</summary>
        static bool Allowed(AvatarMask mask, Transform root, PropertyGroup group)
        {
            if (!(group?.Target is Transform)) return true;
            var t = group.Transform;
            if (t == null || root == null) return true;
            var parts = new List<string>();
            for (var p = t; p != null && p != root; p = p.m_Parent) parts.Insert(0, p.m_GameObject.m_Name);
            return mask.Allows(string.Join("/", parts));
        }

        // ------------------------------------------------------------------ events and callbacks

        void FireEvents()
        {
            var left = m_Left.ToArray();
            m_Left.Clear();
            if (!m_Animator.m_FireEvents) return;
            var go = m_Animator.m_GameObject;
            foreach (var (layer, run, end) in left)
            {
                if (!(MotionOf(layer.Def, run.Def) is AnimationClip leftClip) || leftClip.m_Events.Count == 0) continue;
                double leftOffset = run.Def.CycleOffset;
                var leftInfo = StateInfo(layer, run);
                var leftClipInfo = new AnimatorClipInfo { m_Clip = leftClip, m_Weight = 1f };
                AnimationEvents.FireCrossed(leftClip, run.Previous + leftOffset, end + leftOffset, leftClip.isLooping, run.Fresh, e =>
                {
                    var fired = e.Copy();
                    fired.m_Source = AnimationEventSource.Animator;
                    fired.m_AnimatorStateInfo = leftInfo;
                    fired.m_AnimatorClipInfo = leftClipInfo;
                    AnimationEvents.Dispatch(go, fired, leftClip);
                });
            }
            foreach (var (layer, layerIndex, run, clip, weight) in m_Contributions.ToArray())
            {
                if (clip.m_Events.Count == 0 || weight <= 0f) continue;
                double offset = run.Def.CycleOffset;
                var info = StateInfo(layer, run);
                var clipInfo = new AnimatorClipInfo { m_Clip = clip, m_Weight = weight };
                AnimationEvents.FireCrossed(clip, run.Previous + offset, run.Normalized + offset, clip.isLooping, run.Fresh, e =>
                {
                    var fired = e.Copy();
                    fired.m_Source = AnimationEventSource.Animator;
                    fired.m_AnimatorStateInfo = info;
                    fired.m_AnimatorClipInfo = clipInfo;
                    AnimationEvents.Dispatch(go, fired, clip);
                });
            }
        }

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
                m_Length = MotionLength(layer, motion),
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

        /// <summary>A state's behaviours, then those of the machines containing it.</summary>
        IEnumerable<StateMachineBehaviour> BehavioursOf(LayerRun layer, StateDef state)
        {
            var own = layer.Def.SyncedLayerIndex >= 0
                ? (layer.Def.SyncedBehaviours.TryGetValue(state, out var synced) ? synced : Array.Empty<StateMachineBehaviour>())
                : state.Behaviours;
            foreach (var b in own) yield return Instance(b);
            if (layer.Def.SyncedLayerIndex >= 0) yield break;
            for (var m = state.Machine; m != null; m = m.Parent)
                foreach (var b in m.Behaviours) yield return Instance(b);
        }

        internal StateMachineBehaviour[] Behaviours(int fullPathHash, int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return Array.Empty<StateMachineBehaviour>();
            var layer = Layers[layerIndex];
            var def = layer.Def.SyncedLayerIndex >= 0 && layer.Def.SyncedLayerIndex < Layers.Length ? Layers[layer.Def.SyncedLayerIndex].Def : layer.Def;
            if (!def.StateByHash.TryGetValue(fullPathHash, out var state)) return Array.Empty<StateMachineBehaviour>();
            return new List<StateMachineBehaviour>(BehavioursOf(layer, state)).ToArray();
        }

        void QueueEnter(LayerRun layer, StateRun run)
        {
            run.EnteredThisUpdate = true;
            foreach (var target in LayersPlaying(layer))
            {
                foreach (var b in BehavioursOf(target.Layer, run.Def))
                {
                    if (b == null) continue;
                    var info = StateInfo(target.Layer, run);
                    int li = target.Index;
                    m_Callbacks.Add(() =>
                    {
                        b.OnStateEnter(m_Animator, info, li);
                        b.OnStateEnter(m_Animator, info, li, Playable);
                    });
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
                    var info = StateInfo(target.Layer, run);
                    int li = target.Index;
                    m_Callbacks.Add(() =>
                    {
                        b.OnStateExit(m_Animator, info, li);
                        b.OnStateExit(m_Animator, info, li, Playable);
                    });
                }
            }
        }

        void QueueMachine(LayerRun layer, MachineDef machine, bool enter)
        {
            foreach (var asset in machine.Behaviours)
            {
                var b = Instance(asset);
                if (b == null) continue;
                int hash = machine.FullPathHash;
                m_Callbacks.Add(() =>
                {
                    if (enter)
                    {
                        b.OnStateMachineEnter(m_Animator, hash);
                        b.OnStateMachineEnter(m_Animator, hash, Playable);
                    }
                    else
                    {
                        b.OnStateMachineExit(m_Animator, hash);
                        b.OnStateMachineExit(m_Animator, hash, Playable);
                    }
                });
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

        void QueueUpdates()
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                var source = layer.Def.SyncedLayerIndex >= 0 && layer.Def.SyncedLayerIndex < Layers.Length ? Layers[layer.Def.SyncedLayerIndex] : layer;
                foreach (var run in new[] { source.Current, source.Next })
                {
                    if (run == null || run.EnteredThisUpdate) continue;
                    var own = layer.Def.SyncedLayerIndex >= 0
                        ? (layer.Def.SyncedBehaviours.TryGetValue(run.Def, out var synced) ? synced : Array.Empty<StateMachineBehaviour>())
                        : null;
                    var behaviours = own != null ? (IEnumerable<StateMachineBehaviour>)Array.ConvertAll(own, Instance) : BehavioursOf(layer, run.Def);
                    foreach (var b in behaviours)
                    {
                        if (b == null) continue;
                        var info = StateInfo(layer, run);
                        int li = i;
                        m_Callbacks.Add(() =>
                        {
                            b.OnStateUpdate(m_Animator, info, li);
                            b.OnStateUpdate(m_Animator, info, li, Playable);
                        });
                    }
                }
            }
        }

        void RunCallbacks()
        {
            if (m_Callbacks.Count == 0) return;
            var calls = m_Callbacks.ToArray();
            m_Callbacks.Clear();
            foreach (var call in calls)
            {
                try { call(); }
                catch (Exception e) { Debug.LogException(e, m_Animator); }
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
