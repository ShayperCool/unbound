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
        internal float Weight;
        internal StateRun Current;
        internal StateRun Next;
        internal TransitionRun Transition;
        internal PendingPlay Pending;
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
    internal sealed class AnimatorPlayer
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
        (bool[] Floats, bool[] Objects, int FloatCount)[] m_LayerMasks;
        readonly List<(LayerRun Layer, int LayerIndex, StateRun Run, AnimationClip Clip, float Weight)> m_Contributions =
            new List<(LayerRun, int, StateRun, AnimationClip, float)>();

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
                var state = EnterMachine(layer, layer.Def.Root, 0, callEnter: false);
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
            StepLayers(deltaTime * m_Animator.m_Speed);
            Evaluate();
            AfterEvaluate();
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

        /// <summary>After the pose is written: events, state machine callbacks.</summary>
        internal void AfterEvaluate()
        {
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
            int n = Math.Min(Bindings.FloatCount, values.Length);
            for (int c = 0; c < n; c++)
            {
                float r = values[c];
                bool any = false;
                for (int l = 0; l < count; l++)
                {
                    var layer = m_LayerPoses[l];
                    float w = layer.AnimationPose.Weight[c];
                    if (w <= 0f) continue;
                    any = true;
                    if (layer.Weight <= 0f) continue;
                    float v = layer.AnimationPose.Value[c];
                    if (layer.Additive)
                    {
                        r += layer.Weight * (v - w * Bindings.Defaults[c]);
                        continue;
                    }
                    float lv = w >= 1f ? v / w : v + (1f - w) * r;
                    r = layer.Weight >= 1f ? lv : r + (lv - r) * layer.Weight;
                }
                if (!any) continue;
                values[c] = r;
                animated[c] = true;
            }
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
            if (layer.Pending != null)
            {
                var pending = layer.Pending;
                layer.Pending = null;
                ApplyPending(layer, index, pending);
            }
            if (layer.Current == null)
            {
                if (layer.Def.Root == null) return;
                var state = EnterMachine(layer, layer.Def.Root, 0, callEnter: false);
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

        /// <summary>Child weights of a blend tree for the current parameters.</summary>
        internal float[] BlendWeights(BlendTreeMotion tree)
        {
            int n = tree.Children.Count;
            var w = new float[n];
            if (n == 0) return w;
            switch (tree.BlendType)
            {
                case 4: // Direct
                    for (int i = 0; i < n; i++)
                    {
                        int p = ParameterIndex(tree.Children[i].DirectParameter);
                        w[i] = p >= 0 ? Floats[p] : 0f;
                    }
                    return w;
                case 0: // Simple 1D
                {
                    int p = ParameterIndex(tree.Parameter);
                    float x = p >= 0 ? GetAsFloat(p) : 0f;
                    var order = new List<int>();
                    for (int i = 0; i < n; i++) order.Add(i);
                    order.Sort((a, b) => tree.Children[a].Threshold.CompareTo(tree.Children[b].Threshold));
                    if (x <= tree.Children[order[0]].Threshold) { w[order[0]] = 1f; return w; }
                    if (x >= tree.Children[order[n - 1]].Threshold) { w[order[n - 1]] = 1f; return w; }
                    for (int k = 0; k < n - 1; k++)
                    {
                        float a = tree.Children[order[k]].Threshold, b = tree.Children[order[k + 1]].Threshold;
                        if (x < a || x > b) continue;
                        float t = b > a ? (x - a) / (b - a) : 0f;
                        w[order[k]] = 1f - t;
                        w[order[k + 1]] = t;
                        return w;
                    }
                    w[order[0]] = 1f;
                    return w;
                }
                default: // 2D: gradient band interpolation
                {
                    int px = ParameterIndex(tree.Parameter), py = ParameterIndex(tree.ParameterY);
                    var p = new Vector2(px >= 0 ? GetAsFloat(px) : 0f, py >= 0 ? GetAsFloat(py) : 0f);
                    if (n == 1) { w[0] = 1f; return w; }
                    float total = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var pi = tree.Children[i].Position;
                        float h = 1f;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == i) continue;
                            var pij = tree.Children[j].Position - pi;
                            float d = Vector2.Dot(pij, pij);
                            if (d <= 0f) continue;
                            h = Math.Min(h, 1f - Vector2.Dot(p - pi, pij) / d);
                        }
                        w[i] = Math.Max(0f, h);
                        total += w[i];
                    }
                    if (total > 0f)
                    {
                        for (int i = 0; i < n; i++) w[i] /= total;
                    }
                    else
                    {
                        int best = 0;
                        float bestDistance = float.MaxValue;
                        for (int i = 0; i < n; i++)
                        {
                            float d = (tree.Children[i].Position - p).sqrMagnitude;
                            if (d < bestDistance) { bestDistance = d; best = i; }
                        }
                        w[best] = 1f;
                    }
                    return w;
                }
            }
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
            PoseWriter.WriteLayers(Bindings, m_LayerPoses, count);
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
                    float p = source.Transition != null ? source.Transition.Progress : 0f;
                    SampleState(layer, i, source.Current, 1f - p, pose, mask, objectMask);
                    if (source.Next != null && source.Transition != null) SampleState(layer, i, source.Next, p, pose, mask, objectMask);
                }
                m_LayerPoses[i] = new LayerPose { AnimationPose = pose, Weight = i == 0 ? 1f : layer.Weight, Additive = layer.Def.Additive && i > 0 };
            }
            return count;
        }

        void SampleState(LayerRun layer, int layerIndex, StateRun run, float weight, AnimationPose pose, bool[] mask, bool[] objectMask)
        {
            if (weight <= 0f) return;
            var motion = MotionOf(layer.Def, run.Def);
            double normalized = run.Normalized + run.Def.CycleOffset + (run.Def.CycleOffsetParameter >= 0 ? Floats[run.Def.CycleOffsetParameter] : 0f);
            SampleMotion(layer, layerIndex, run, motion, normalized, weight, pose, mask, objectMask);
            if (!run.Def.WriteDefaults) return;
            // Write defaults: what the controller animates and this state does not goes back to its default.
            var (animated, animatedObjects) = MotionMask(motion);
            var defaults = Bindings.Defaults;
            for (int c = 0; c < Bindings.FloatCount; c++)
            {
                if (animated[c] || (mask != null && !mask[c])) continue;
                pose.Value[c] += weight * defaults[c];
                pose.Weight[c] += weight;
            }
            for (int c = 0; c < Bindings.ObjectCount; c++)
            {
                if (animatedObjects[c] || (objectMask != null && !objectMask[c])) continue;
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
                    ClipSampling.Sample(bound, ClipSampling.ClipTime(clip, normalized, clip.isLooping), weight, pose, mask, objectMask);
                    m_Contributions.Add((layer, layerIndex, run, clip, weight));
                    break;
                }
                case BlendTreeMotion tree:
                {
                    var weights = BlendWeights(tree);
                    for (int i = 0; i < weights.Length; i++)
                        if (weights[i] > 0f)
                            SampleMotion(layer, layerIndex, run, tree.Children[i].Motion, normalized + tree.Children[i].CycleOffset, weight * weights[i], pose, mask, objectMask);
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

        static bool Allowed(AvatarMask mask, Transform root, PropertyGroup group)
        {
            var t = group?.Transform;
            if (t == null || root == null) return true;
            var parts = new List<string>();
            for (var p = t; p != null && p != root; p = p.m_Parent) parts.Insert(0, p.m_GameObject.m_Name);
            return mask.Allows(string.Join("/", parts));
        }

        // ------------------------------------------------------------------ events and callbacks

        void FireEvents()
        {
            if (!m_Animator.m_FireEvents) return;
            var go = m_Animator.m_GameObject;
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
