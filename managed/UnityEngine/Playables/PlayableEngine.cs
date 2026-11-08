using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Unbound.Runtime.Playables
{
    /// <summary>
    /// The objects behind playable handles: graphs, playables and outputs share one table of slots,
    /// a handle being a slot (plus one, zero is null) and the slot's version when it was made.
    /// </summary>
    internal static class HandleTable
    {
        static readonly List<object> s_Objects = new List<object> { null };
        static readonly List<uint> s_Versions = new List<uint> { 0 };
        static readonly Stack<int> s_Free = new Stack<int>();

        internal static (IntPtr Handle, uint Version) Add(object o)
        {
            int slot;
            if (s_Free.Count > 0)
            {
                slot = s_Free.Pop();
                s_Objects[slot] = o;
            }
            else
            {
                slot = s_Objects.Count;
                s_Objects.Add(o);
                s_Versions.Add(1);
            }
            return ((IntPtr)slot, s_Versions[slot]);
        }

        internal static void Remove(IntPtr handle, uint version)
        {
            int slot = (int)handle;
            if (slot <= 0 || slot >= s_Objects.Count || s_Versions[slot] != version) return;
            s_Objects[slot] = null;
            s_Versions[slot] = version + 1;
            s_Free.Push(slot);
        }

        internal static T Get<T>(IntPtr handle, uint version) where T : class
        {
            long slot = (long)handle;
            if (slot <= 0 || slot >= s_Objects.Count || s_Versions[(int)slot] != version) return null;
            return s_Objects[(int)slot] as T;
        }
    }

    /// <summary>What an engine playable (clip, mixer, controller, audio clip) does beyond the
    /// generic node: per-frame work in the prepare pass, and cleanup.</summary>
    internal abstract class PlayableKind
    {
        /// <summary>Called in the prepare pass for an effectively playing node, after its time moved.</summary>
        internal virtual void Prepare(PlayableNode node, in FrameData info) { }

        /// <summary>The node changed effective play state in a traversal (Play/Pause of an engine playable).</summary>
        internal virtual void PlayStateChanged(PlayableNode node, bool playing, in FrameData info) { }

        internal virtual void Destroyed(PlayableNode node) { }

        /// <summary>Whether reaching its duration (wrap mode None) makes the playable done. An
        /// AnimationClipPlayable does (6000.0.71f1, animculling clock/duration); audio clips decide
        /// themselves and controller playables keep going.</summary>
        internal virtual bool FinishesAtDuration => true;

        internal virtual bool CanChangeInputs => true;
        internal virtual bool CanSetWeights => true;

        /// <summary>The input weight a set (SetInputWeight, ConnectInput) stores; GetInputWeight
        /// reads it back.</summary>
        internal virtual float StoredWeight(float weight) => weight;
        internal virtual bool CanDestroy => true;

        /// <summary>An animation playable: below an animation output, it and its inputs run on the
        /// target Animator's clock (Play-entry interval, culling) instead of the graph's.</summary>
        internal virtual bool AnimatorClock => false;
    }

    /// <summary>An input port: the playable connected there, from which of its output ports, and its weight.</summary>
    internal struct InputPort
    {
        internal PlayableNode Node;
        internal int SourcePort;
        internal float Weight;
    }

    /// <summary>An output port: the playable it feeds and which of that playable's inputs.</summary>
    internal struct OutputPort
    {
        internal PlayableNode Node;
        internal int InputPort;
    }

    internal sealed class PlayableNode
    {
        internal PlayableGraphData Graph;
        internal IntPtr Handle;
        internal uint Version;
        /// <summary>The playable's type: the behaviour's type for script playables, the struct
        /// (AnimationClipPlayable, ...) for engine ones, Playable for plain ones.</summary>
        internal Type PlayableType;
        internal Type JobType;
        internal object Script;
        internal PlayableKind Kind;
        internal readonly List<InputPort> Inputs = new List<InputPort>();
        internal readonly List<OutputPort> Outputs = new List<OutputPort> { default };
        internal PlayState State = PlayState.Playing;
        internal double Time;
        internal double PreviousTime;
        internal double Speed = 1.0;
        internal double Duration = double.MaxValue;
        internal bool Done;
        /// <summary>Reached its duration; reports done when its graph flushes (see
        /// <see cref="PlayableGraphData.LateUpdate"/>).</summary>
        internal bool DonePending;
        internal bool PropagateSetTime;
        internal PlayableTraversalMode Traversal = PlayableTraversalMode.Mix;
        internal DirectorWrapMode WrapMode = DirectorWrapMode.None;
        internal double Delay;
        internal float LeadTime;
        /// <summary>Has had OnGraphStart (and gets OnGraphStop).</summary>
        internal bool Started;
        /// <summary>Its effective play state the last time a traversal reached it.</summary>
        internal bool LastPlaying;
        /// <summary>Its time was set (SetTime) since a traversal last advanced it: the next one
        /// reports a seek and does not advance it.</summary>
        internal bool TimeSet;
        /// <summary>Held by a CullCompletely-culled Animator in its last prepare (see
        /// <see cref="PlayableGraphData.PreparePass"/>).</summary>
        internal bool Held;

        internal ulong PreparedFrame = ulong.MaxValue;
        /// <summary>The time flags of its last prepare (seek, loop, hold), which the Play, Stop and
        /// teardown passes report again; a new playable has the seek flag.</summary>
        internal FrameData.Flags FrameFlags = FrameData.Flags.SeekOccured;
        /// <summary>In the graph's list of playables without connected outputs.</summary>
        internal bool IsRoot;
        internal bool Destroyed;

        internal PlayableHandle ToHandle() => new PlayableHandle { m_Handle = Handle, m_Version = Version };

        internal Playable ToPlayable() => new Playable(ToHandle());

        internal IPlayableBehaviour Behaviour => Script as IPlayableBehaviour;

        internal bool HasConnectedOutput()
        {
            foreach (var o in Outputs)
                if (o.Node != null) return true;
            return false;
        }
    }

    internal sealed class PlayableOutputData
    {
        internal PlayableGraphData Graph;
        internal IntPtr Handle;
        internal uint Version;
        internal Type OutputType;
        internal string Name;
        internal PlayableNode Source;
        internal int SourcePort;
        internal float Weight = 1f;
        internal Object ReferenceObject;
        internal Object UserData;
        internal readonly List<INotificationReceiver> Receivers = new List<INotificationReceiver>();
        internal readonly List<(PlayableHandle Origin, INotification Notification, object Context)> Pending =
            new List<(PlayableHandle, INotification, object)>();
        /// <summary>Type-specific state (the animation output's target and evaluation).</summary>
        internal object Engine;
        internal bool Destroyed;

        internal PlayableOutputHandle ToHandle() => new PlayableOutputHandle { m_Handle = Handle, m_Version = Version };
    }

    /// <summary>
    /// A playable graph as Unity's native side keeps it, with Unity's evaluation rules (measured
    /// with the Editor, tests/projects/playables):
    /// <list type="bullet">
    /// <item>The prepare pass walks each output's source tree depth first, parent before inputs,
    /// once per playable and frame (later outputs only reach playables not prepared yet). A playing
    /// playable's time advances by the frame's delta times the product of the speeds above it,
    /// unless its time was set (then it reports a seek and stays); a new playable gets OnGraphStart,
    /// a change of effective play state OnBehaviourPlay/OnBehaviourPause, then PrepareFrame.
    /// Paused subtrees are still walked (their playables learn they are paused) but neither advance
    /// nor prepare.</item>
    /// <item>The process pass (script outputs) walks each output again, inputs before parent, once
    /// per output path, calling ProcessFrame with the output's user data. Paused subtrees are skipped.</item>
    /// <item>Passthrough playables lead an output that comes in through output port i to input i only.</item>
    /// <item>Notifications pushed on an output are delivered receiver by receiver: after its process
    /// pass in an Evaluate, in PreLateUpdate.DirectorDeferredEvaluate when the player loop plays it.</item>
    /// <item>Stop and Play take effect in the next DirectorUpdate; Destroy at the next Director
    /// system (DirectorUpdate, DirectorLateUpdate) or director evaluation.</item>
    /// <item>Wrap modes at the duration: None clamps and reports done after the evaluation, Hold
    /// clamps and reports the time held, Loop wraps and reports the loop.</item>
    /// </list>
    /// </summary>
    internal sealed class PlayableGraphData
    {
        internal IntPtr Handle;
        internal uint Version;
        internal string Name;
        internal readonly List<PlayableNode> Nodes = new List<PlayableNode>();
        internal readonly List<PlayableOutputData> Outputs = new List<PlayableOutputData>();
        internal readonly List<PlayableNode> Roots = new List<PlayableNode>();
        internal DirectorUpdateMode UpdateMode = DirectorUpdateMode.DSPClock;
        internal IExposedPropertyTable Resolver;
        internal bool Playing;
        internal bool PendingStart;
        internal bool PendingStop;
        internal bool EverStarted;
        internal bool Destroyed;
        /// <summary>Playing when Destroy was called (its teardown counts a frame, as a stop does).</summary>
        internal bool PlayingAtDestroy;
        /// <summary>The last control pass the player loop ran was a stop: playables already know
        /// they are paused, so the teardown does not pause them again.</summary>
        internal bool Stopped;
        /// <summary>Unity's frame identifiers start at 0xDEADBEF0 in every graph.</summary>
        internal ulong FrameId = 0xDEADBEF0;
        internal FrameRate? MatchFrameRate;
        /// <summary>The playable director that owns the graph (its outputs are bound by it).</summary>
        internal PlayableDirector Director;

        internal PlayableGraph ToGraph() => new PlayableGraph { m_Handle = Handle, m_Version = Version };

        // ------------------------------------------------------------------ registry

        static readonly List<PlayableGraphData> s_Graphs = new List<PlayableGraphData>();
        static readonly List<PlayableGraphData> s_PendingDestroy = new List<PlayableGraphData>();
        /// <summary>Graphs the player loop updates, in the order they started playing.</summary>
        static readonly List<PlayableGraphData> s_Playing = new List<PlayableGraphData>();

        internal static PlayableGraphData Create(string name)
        {
            var g = new PlayableGraphData { Name = name ?? "" };
            (g.Handle, g.Version) = HandleTable.Add(g);
            s_Graphs.Add(g);
            PlayableSystem.EnsureRegistered();
            return g;
        }

        /// <summary>The Animator a controller graph belongs to (Animator.playableGraph): the
        /// Animator updates it, not the player loop.</summary>
        internal AnimatorPlayer ControllerPlayer;

        internal static PlayableGraphData CreateForAnimator(Animator animator, AnimatorPlayer player)
        {
            var g = Create(animator.name);
            g.UpdateMode = animator.updateMode == AnimatorUpdateMode.UnscaledTime ? DirectorUpdateMode.UnscaledGameTime : DirectorUpdateMode.GameTime;
            g.ControllerPlayer = player;
            g.Playing = true;
            g.EverStarted = true;
            var n = g.CreateNode(typeof(UnityEngine.Animations.AnimatorControllerPlayable));
            n.Kind = new ControllerKind { Controller = player.Controller, Player = player, Node = n };
            player.Playable = new UnityEngine.Animations.AnimatorControllerPlayable(n.ToHandle());
            var o = g.CreateOutput(typeof(UnityEngine.Animations.AnimationPlayableOutput), "Animation");
            o.Engine = new AnimationOutputState { Target = animator, SortingOrder = 0 };
            o.Source = n;
            return g;
        }

        internal static PlayableGraphData Get(PlayableGraph graph) => HandleTable.Get<PlayableGraphData>(graph.m_Handle, graph.m_Version);

        internal static PlayableNode Get(PlayableHandle handle) => HandleTable.Get<PlayableNode>(handle.m_Handle, handle.m_Version);

        internal static PlayableOutputData Get(PlayableOutputHandle handle) => HandleTable.Get<PlayableOutputData>(handle.m_Handle, handle.m_Version);

        // ------------------------------------------------------------------ structure

        internal PlayableNode CreateNode(Type type)
        {
            var n = new PlayableNode { Graph = this, PlayableType = type ?? typeof(Playable) };
            (n.Handle, n.Version) = HandleTable.Add(n);
            Nodes.Add(n);
            n.IsRoot = true;
            Roots.Add(n);
            return n;
        }

        internal PlayableOutputData CreateOutput(Type type, string name)
        {
            var o = new PlayableOutputData { Graph = this, OutputType = type, Name = name ?? "" };
            (o.Handle, o.Version) = HandleTable.Add(o);
            Outputs.Add(o);
            return o;
        }

        internal void SetRoot(PlayableNode n, bool root)
        {
            if (n.IsRoot == root) return;
            n.IsRoot = root;
            if (root) Roots.Add(n);
            else Roots.Remove(n);
        }

        internal bool Connect(PlayableNode source, int sourcePort, PlayableNode dest, int destPort)
        {
            if (source == null || dest == null) return false;
            if (source.Graph != this || dest.Graph != this)
            {
                Debug.LogError("Cannot connect playables that belong to different graphs.");
                return false;
            }
            if (sourcePort < 0 || destPort < 0) return false;
            if (dest.Kind != null && !dest.Kind.CanChangeInputs)
            {
                Debug.LogError("Cannot change the inputs of this playable.");
                return false;
            }
            while (source.Outputs.Count <= sourcePort) source.Outputs.Add(default);
            while (dest.Inputs.Count <= destPort) dest.Inputs.Add(new InputPort());
            // A taken output port is disconnected from where it led; a taken input port drops its
            // playable but keeps its weight.
            var oldOut = source.Outputs[sourcePort];
            if (oldOut.Node != null) DisconnectInput(oldOut.Node, oldOut.InputPort, updateRoots: false);
            var old = dest.Inputs[destPort];
            if (old.Node != null) DisconnectInput(dest, destPort, updateRoots: true);
            dest.Inputs[destPort] = new InputPort { Node = source, SourcePort = sourcePort, Weight = dest.Inputs[destPort].Weight };
            source.Outputs[sourcePort] = new OutputPort { Node = dest, InputPort = destPort };
            SetRoot(source, false);
            return true;
        }

        internal void DisconnectInput(PlayableNode dest, int port, bool updateRoots)
        {
            if (dest == null || port < 0 || port >= dest.Inputs.Count) return;
            var input = dest.Inputs[port];
            if (input.Node == null) return;
            var src = input.Node;
            if (input.SourcePort < src.Outputs.Count && src.Outputs[input.SourcePort].Node == dest)
                src.Outputs[input.SourcePort] = default;
            dest.Inputs[port] = new InputPort { Weight = input.Weight };
            if (updateRoots && !src.HasConnectedOutput()) SetRoot(src, true);
        }

        internal void SetInputCount(PlayableNode n, int count)
        {
            if (count < 0) count = 0;
            // Dropped ports are disconnected; Unity does not count the playables they held as roots again.
            for (int i = n.Inputs.Count - 1; i >= count; i--)
            {
                DisconnectInput(n, i, updateRoots: false);
                n.Inputs.RemoveAt(i);
            }
            while (n.Inputs.Count < count) n.Inputs.Add(new InputPort());
        }

        internal void SetOutputCount(PlayableNode n, int count)
        {
            if (count < 0) count = 0;
            for (int i = n.Outputs.Count - 1; i >= count; i--)
            {
                var o = n.Outputs[i];
                if (o.Node != null) DisconnectInput(o.Node, o.InputPort, updateRoots: false);
                n.Outputs.RemoveAt(i);
            }
            while (n.Outputs.Count < count) n.Outputs.Add(default);
        }

        /// <summary>DestroyPlayable: disconnects and destroys one playable now.</summary>
        internal void DestroyNode(PlayableNode n)
        {
            if (n == null || n.Destroyed) return;
            if (n.Kind != null && !n.Kind.CanDestroy)
            {
                Debug.LogError("This playable cannot be destroyed.");
                return;
            }
            for (int i = 0; i < n.Inputs.Count; i++) DisconnectInput(n, i, updateRoots: true);
            for (int i = 0; i < n.Outputs.Count; i++)
            {
                var o = n.Outputs[i];
                if (o.Node != null) DisconnectInput(o.Node, o.InputPort, updateRoots: false);
            }
            foreach (var output in Outputs)
                if (output.Source == n) output.Source = null;
            FinishNode(n);
        }

        /// <summary>DestroySubgraph: the playable's inputs (recursively, inputs first), then the playable.</summary>
        internal void DestroySubgraph(PlayableNode n)
        {
            if (n == null || n.Destroyed) return;
            var order = new List<PlayableNode>();
            var seen = new HashSet<PlayableNode>();
            void Collect(PlayableNode p)
            {
                if (p == null || !seen.Add(p)) return;
                foreach (var i in p.Inputs.ToArray()) Collect(i.Node);
                order.Add(p);
            }
            Collect(n);
            foreach (var p in order) DestroyNode(p);
        }

        void FinishNode(PlayableNode n)
        {
            if (n.Destroyed) return;
            n.Destroyed = true;
            var p = n.ToPlayable();
            var b = n.Behaviour;
            if (b != null)
            {
                if (n.Started)
                {
                    n.Started = false;
                    Call(b, Callback.GraphStop, p, default);
                }
                Call(b, Callback.Destroy, p, default);
            }
            try { n.Kind?.Destroyed(n); }
            catch (Exception e) { Debug.LogException(e); }
            Nodes.Remove(n);
            SetRoot(n, false);
            HandleTable.Remove(n.Handle, n.Version);
        }

        internal void DestroyOutput(PlayableOutputData o)
        {
            if (o == null || o.Destroyed) return;
            o.Destroyed = true;
            (o.Engine as IDisposable)?.Dispose();
            Outputs.Remove(o);
            HandleTable.Remove(o.Handle, o.Version);
        }

        /// <summary>PlayableGraph.Destroy: the graph handle dies now, its playables at the end of the frame.</summary>
        internal void Destroy()
        {
            if (Destroyed) return;
            Destroyed = true;
            PlayingAtDestroy = Playing && EverStarted;
            s_Graphs.Remove(this);
            s_Playing.Remove(this);
            HandleTable.Remove(Handle, Version);
            s_PendingDestroy.Add(this);
        }

        void FinishDestroy()
        {
            // Playables that a traversal would find playing are paused first (unless a stop already
            // did it), then every playable in creation order stops and is destroyed.
            if (PlayingAtDestroy) FrameId++;
            var outputs = Scratch<PlayableOutputData>.Rent(Outputs);
            var seen = Scratch<PlayableNode>.RentSet();
            try
            {
                foreach (var o in outputs)
                {
                    if (o.Source == null || Stopped) continue;
                    var pause = new TeardownPause { Graph = this, Output = o, Seen = seen };
                    Walk(o, ref pause);
                }
                var nodes = Scratch<PlayableNode>.Rent(Nodes);
                foreach (var n in nodes) FinishNode(n);
                Scratch<PlayableNode>.Return(nodes);
                foreach (var o in outputs) DestroyOutput(o);
            }
            finally
            {
                Scratch<PlayableOutputData>.Return(outputs);
                Scratch<PlayableNode>.Return(seen);
            }
        }

        struct TeardownPause : INodeVisitor
        {
            internal PlayableGraphData Graph;
            internal PlayableOutputData Output;
            internal HashSet<PlayableNode> Seen;

            public void Visit(PlayableNode n, in NodeVisit v)
            {
                if (!v.Playing || !Seen.Add(n)) return;
                var b = n.Behaviour;
                if (b == null) return;
                var info = Graph.MakeInfo(n, 0, v, n.FrameFlags & ControlFlags, true, Output, false);
                n.LastPlaying = false;
                Call(b, Callback.Pause, n.ToPlayable(), info);
            }
        }

        /// <summary>A domain reload: the graphs' behaviours belong to the old game code, so every
        /// graph is dropped without callbacks (directors build theirs again when played).</summary>
        internal static void ForgetAll()
        {
            var all = new List<PlayableGraphData>(s_Graphs);
            all.AddRange(s_PendingDestroy);
            s_Graphs.Clear();
            s_Playing.Clear();
            s_PendingDestroy.Clear();
            s_Updated.Clear();
            s_FinishAtFrameEnd.Clear();
            foreach (var g in all)
            {
                OutputSystems.Stopped(g);
                g.Destroyed = true;
                foreach (var n in g.Nodes) n.Destroyed = true;
                foreach (var o in g.Outputs)
                {
                    o.Destroyed = true;
                    (o.Engine as IDisposable)?.Dispose();
                }
                HandleTable.Remove(g.Handle, g.Version);
            }
        }

        internal static void FlushDestroyed()
        {
            if (s_PendingDestroy.Count == 0) return;
            var list = Scratch<PlayableGraphData>.Rent(s_PendingDestroy);
            s_PendingDestroy.Clear();
            try
            {
                foreach (var g in list) g.FinishDestroy();
            }
            finally { Scratch<PlayableGraphData>.Return(list); }
        }

        // ------------------------------------------------------------------ play state

        internal void Play()
        {
            if (Playing) return;
            Playing = true;
            PendingStop = false;
            PendingStart = true;
            Stopped = false;
            s_Playing.Remove(this);
            s_Playing.Add(this);
        }

        internal void Stop()
        {
            if (!Playing) return;
            Playing = false;
            PendingStart = false;
            PendingStop = true;
        }

        /// <summary>Done when it has outputs and every output's source (if any) is done.</summary>
        internal bool IsDone()
        {
            if (Outputs.Count == 0) return false;
            foreach (var o in Outputs)
            {
                if (o.Source == null) continue;
                if (!o.Source.Done) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ traversal

        /// <summary>What a traversal knows about a playable it reaches: its input weight, effective
        /// weight, the effective speeds above it and of it, its effective play state, and whether
        /// it is at or below an animation playable (<see cref="PlayableKind.AnimatorClock"/>).</summary>
        internal readonly struct NodeVisit
        {
            internal readonly float Weight, EffectiveWeight;
            internal readonly double ParentSpeed, Speed;
            internal readonly bool Playing, AnimatorClock;

            internal NodeVisit(float weight, float effectiveWeight, double parentSpeed, double speed, bool playing, bool animatorClock)
            {
                Weight = weight;
                EffectiveWeight = effectiveWeight;
                ParentSpeed = parentSpeed;
                Speed = speed;
                Playing = playing;
                AnimatorClock = animatorClock;
            }
        }

        /// <summary>A traversal's per-playable step. Visitors are structs passed by reference, so a
        /// traversal allocates nothing.</summary>
        internal interface INodeVisitor
        {
            void Visit(PlayableNode node, in NodeVisit visit);
        }

        const int MaxDepth = 256;

        /// <summary>Depth first, parent first, along the inputs an output reaches through its source
        /// port. <paramref name="animatorClock"/> puts the whole tree on the Animator's clock.</summary>
        internal static void Walk<T>(PlayableOutputData output, ref T visitor, bool animatorClock = false) where T : struct, INodeVisitor =>
            Walk(output.Source, output.SourcePort, 1f, 1f, 1.0, true, animatorClock, ref visitor, 0);

        static void Walk<T>(PlayableNode n, int viaPort, float w, float ew, double parentSpeed, bool parentPlaying, bool parentClock, ref T visitor, int depth)
            where T : struct, INodeVisitor
        {
            if (n == null || depth > MaxDepth) return;
            var visit = new NodeVisit(w, ew, parentSpeed, parentSpeed * n.Speed, parentPlaying && n.State == PlayState.Playing,
                parentClock || (n.Kind?.AnimatorClock ?? false));
            visitor.Visit(n, visit);
            if (n.Traversal == PlayableTraversalMode.Passthrough)
            {
                if (viaPort >= 0 && viaPort < n.Inputs.Count)
                {
                    var i = n.Inputs[viaPort];
                    Walk(i.Node, i.SourcePort, i.Weight, ew * i.Weight, visit.Speed, visit.Playing, visit.AnimatorClock, ref visitor, depth + 1);
                }
                return;
            }
            for (int k = 0; k < n.Inputs.Count; k++)
            {
                var i = n.Inputs[k];
                if (i.Node == null) continue;
                Walk(i.Node, i.SourcePort, i.Weight, ew * i.Weight, visit.Speed, visit.Playing, visit.AnimatorClock, ref visitor, depth + 1);
            }
        }

        const FrameData.Flags ControlFlags = FrameData.Flags.SeekOccured | FrameData.Flags.Loop | FrameData.Flags.Hold;

        internal FrameData MakeInfo(PlayableNode n, double dt, in NodeVisit v, FrameData.Flags flags, bool playing, PlayableOutputData output, bool evaluate) =>
            MakeInfo(n, dt, v.Weight, v.EffectiveWeight, v.ParentSpeed, v.Speed, flags, playing, output, evaluate);

        internal FrameData MakeInfo(PlayableNode n, double dt, float w, float ew, double parentSpeed, double speed, FrameData.Flags flags, bool playing, PlayableOutputData output, bool evaluate)
        {
            if (evaluate) flags |= FrameData.Flags.Evaluate;
            if (playing) flags |= FrameData.Flags.EffectivePlayStatePlaying;
            return new FrameData
            {
                m_FrameID = FrameId,
                m_DeltaTime = dt,
                m_Weight = w,
                m_EffectiveWeight = ew,
                m_EffectiveParentDelay = 0,
                m_EffectiveParentSpeed = (float)parentSpeed,
                m_EffectiveSpeed = (float)speed,
                m_Flags = flags,
                m_Output = output != null ? new PlayableOutput(output.ToHandle()) : PlayableOutput.Null,
            };
        }

        /// <summary>Playables that reached their duration (wrap mode None). They report done once
        /// the explicit evaluation that moved them is over (tests/projects/playables), or at the end
        /// of the frame when the player loop moved them, or when they are animation playables
        /// themselves below an animation output: that frame's LateUpdate still sees them not done
        /// (6000.0.71f1, animculling clock/duration, both update modes; measured for clips, other
        /// animation playables with a duration follow them unmeasured).</summary>
        readonly List<PlayableNode> m_Finished = new List<PlayableNode>();
        readonly List<PlayableNode> m_FinishedAtFrameEnd = new List<PlayableNode>();
        static readonly List<PlayableGraphData> s_FinishAtFrameEnd = new List<PlayableGraphData>();

        void Finish(PlayableNode n, bool atFrameEnd)
        {
            n.DonePending = true;
            if (!atFrameEnd) m_Finished.Add(n);
            else
            {
                if (m_FinishedAtFrameEnd.Count == 0) s_FinishAtFrameEnd.Add(this);
                m_FinishedAtFrameEnd.Add(n);
            }
        }

        static void SetDone(List<PlayableNode> finished)
        {
            foreach (var n in finished)
            {
                if (!n.DonePending) continue; // SetDone since
                n.DonePending = false;
                n.Done = true;
            }
            finished.Clear();
        }

        void FlushFinished() => SetDone(m_Finished);

        /// <summary>PostLateUpdate.DirectorLateUpdate: the playables that finished in this frame's
        /// automatic passes report done, then destroyed graphs are torn down (their teardown
        /// callbacks already see them done: tests/projects/playables director.frames).</summary>
        internal static void LateUpdate()
        {
            if (s_FinishAtFrameEnd.Count > 0)
            {
                var graphs = Scratch<PlayableGraphData>.Rent(s_FinishAtFrameEnd);
                s_FinishAtFrameEnd.Clear();
                foreach (var g in graphs) SetDone(g.m_FinishedAtFrameEnd);
                Scratch<PlayableGraphData>.Return(graphs);
            }
            FlushDestroyed();
        }

        /// <summary>Moves a playing playable's time by its share of the frame, with its duration rules.</summary>
        void Advance(PlayableNode n, double delta, ref FrameData.Flags flags, bool doneAtFrameEnd)
        {
            n.PreviousTime = n.Time;
            double t = n.Time + delta;
            double duration = n.Duration;
            if (delta > 0 && duration < double.MaxValue && t >= duration)
            {
                switch (n.WrapMode)
                {
                    case DirectorWrapMode.Loop:
                        if (duration > 0)
                        {
                            t %= duration;
                            flags |= FrameData.Flags.Loop;
                        }
                        else t = 0;
                        break;
                    case DirectorWrapMode.Hold:
                        flags |= FrameData.Flags.Hold;
                        t = duration;
                        break;
                    default:
                        t = duration;
                        if (n.Kind == null || n.Kind.FinishesAtDuration) Finish(n, doneAtFrameEnd);
                        break;
                }
            }
            n.Time = t;
        }

        /// <summary>
        /// The prepare pass over every output. While an animation output's Animator is
        /// CullCompletely and invisible, the animation playables of its tree
        /// (<see cref="PlayableKind.AnimatorClock"/>) and everything under them are held, like
        /// paused playables: their times stay, they get no PrepareFrame, and entering the hold
        /// pauses them once (OnBehaviourPause). Plain and script playables above them keep
        /// advancing. A hold wins over other outputs reaching the same playable; otherwise a
        /// playable that any output advances moves by the frame's delta. Measured in 6000.0.71f1
        /// (animculling clock/, both update modes, shared clips, several outputs and Animators);
        /// other releases hold the whole source tree
        /// (<see cref="Compat.PlayableCullingHoldsAnimationPlayablesOnly"/>). An animation output's
        /// automatic Play-entry interval (<see cref="AnimationSystem.AutomaticFrameDelta"/>) applies
        /// to its whole tree.
        /// </summary>
        internal void PreparePass(double dt, bool evaluate)
        {
            ulong frame = FrameId;
            var outputs = Scratch<PlayableOutputData>.Rent();
            var held = Scratch<PlayableNode>.RentSet();
            var advancing = Scratch<PlayableNode>.RentSet();
            try
            {
                foreach (var o in Outputs)
                {
                    if (o.Destroyed || o.Source == null) continue;
                    var mark = new Mark { Held = held, Advancing = advancing, Step = dt };
                    if (o.Engine is AnimationOutputState animation)
                    {
                        animation.Automatic = !evaluate;
                        if (!evaluate && UpdateMode == DirectorUpdateMode.GameTime) mark.Step = AnimationSystem.AutomaticFrameDelta(dt);
                        bool culled = CulledCompletely(animation);
                        // The first culled evaluation still samples the held pose.
                        animation.SkipEvaluation = culled && animation.Ticked;
                        animation.Ticked = true;
                        mark.Animation = true;
                        mark.Hold = culled;
                    }
                    outputs.Add(o);
                    Walk(o, ref mark, WholeTreeOnAnimatorClock(o));
                }
                foreach (var o in outputs)
                {
                    var prepare = new Prepare { Graph = this, Output = o, Dt = dt, Frame = frame, Evaluate = evaluate, Held = held, Advancing = advancing };
                    Walk(o, ref prepare, WholeTreeOnAnimatorClock(o));
                }
            }
            finally
            {
                Scratch<PlayableOutputData>.Return(outputs);
                Scratch<PlayableNode>.Return(held);
                Scratch<PlayableNode>.Return(advancing);
            }
        }

        static bool CulledCompletely(AnimationOutputState animation) =>
            animation.Target != null && animation.Target.isActiveAndEnabled
            && animation.Target.cullingMode == AnimatorCullingMode.CullCompletely
            && !Animator.AnyRendererVisible(animation.Target.gameObject);

        /// <summary>The playables culled Animators hold now (what the next prepare pass holds).</summary>
        void CollectHeld(HashSet<PlayableNode> held)
        {
            foreach (var o in Outputs)
            {
                if (o.Destroyed || o.Source == null || o.Engine is not AnimationOutputState animation || !CulledCompletely(animation)) continue;
                var mark = new Mark { Held = held, Advancing = null, Animation = true, Hold = true };
                Walk(o, ref mark, WholeTreeOnAnimatorClock(o));
            }
        }

        static bool WholeTreeOnAnimatorClock(PlayableOutputData o) =>
            o.Engine is AnimationOutputState && !Compat.PlayableCullingHoldsAnimationPlayablesOnly;

        /// <summary>Sorts one output's playables into held and advancing ones.</summary>
        struct Mark : INodeVisitor
        {
            internal HashSet<PlayableNode> Held, Advancing;
            internal double Step;
            internal bool Animation, Hold;

            public void Visit(PlayableNode n, in NodeVisit v)
            {
                if (Animation && Hold && v.AnimatorClock) Held.Add(n);
                else if (Advancing != null && Step != 0) Advancing.Add(n);
            }
        }

        struct Prepare : INodeVisitor
        {
            internal PlayableGraphData Graph;
            internal PlayableOutputData Output;
            internal double Dt;
            internal ulong Frame;
            internal bool Evaluate;
            internal HashSet<PlayableNode> Held, Advancing;

            public void Visit(PlayableNode n, in NodeVisit v)
            {
                if (n.Destroyed || n.PreparedFrame == Frame) return;
                n.PreparedFrame = Frame;
                if (Held.Contains(n)) Graph.HoldNode(n, v, Output, Evaluate);
                else Graph.PrepareNode(n, v, Advancing.Contains(n) ? Dt : 0, Output, Evaluate);
            }
        }

        /// <summary>A held playable's share of the prepare pass: it keeps its time, as a paused one
        /// does, and learns once that it is paused (also when the hold starts before it ever played).</summary>
        void HoldNode(PlayableNode n, in NodeVisit v, PlayableOutputData o, bool evaluate)
        {
            bool entering = !n.Held;
            n.Held = true;
            n.FrameFlags = 0;
            var b = n.Behaviour;
            if (b == null) n.Started = true;
            bool notify = entering && (v.Playing || n.LastPlaying);
            n.LastPlaying = false;
            if (b == null && !notify) return;
            var info = MakeInfo(n, 0, v, 0, false, o, evaluate);
            var p = n.ToPlayable();
            if (b != null)
            {
                if (!n.Started)
                {
                    n.Started = true;
                    Call(b, Callback.GraphStart, p, info);
                }
                if (notify) Call(b, Callback.Pause, p, info);
            }
            else if (n.Kind != null) CallKind(n.Kind, n, KindCall.Pause, info);
        }

        /// <summary>One playable's share of the prepare pass: its time, start and play-state
        /// notifications, then PrepareFrame (script) or the engine playable's own preparation.</summary>
        void PrepareNode(PlayableNode n, in NodeVisit v, double step, PlayableOutputData o, bool evaluate)
        {
            n.Held = false;
            var flags = (FrameData.Flags)0;
            var b = n.Behaviour;
            bool playing = v.Playing;
            if (b == null) n.Started = true;
            if (playing)
            {
                if (n.TimeSet)
                {
                    flags |= FrameData.Flags.SeekOccured;
                    n.TimeSet = false;
                }
                else Advance(n, step * v.Speed, ref flags, !evaluate || (o.Engine is AnimationOutputState && (n.Kind?.AnimatorClock ?? false)));
            }
            n.FrameFlags = flags;
            var info = MakeInfo(n, step, v, flags, playing, o, evaluate);
            var p = n.ToPlayable();
            if (b != null)
            {
                if (!n.Started)
                {
                    n.Started = true;
                    Call(b, Callback.GraphStart, p, info);
                }
                if (playing != n.LastPlaying)
                {
                    n.LastPlaying = playing;
                    Call(b, playing ? Callback.Play : Callback.Pause, p, info);
                }
                if (playing) Call(b, Callback.Prepare, p, info);
            }
            else if (playing != n.LastPlaying)
            {
                n.LastPlaying = playing;
                if (n.Kind != null) CallKind(n.Kind, n, playing ? KindCall.Play : KindCall.Pause, info);
            }
            if (playing && n.Kind != null) CallKind(n.Kind, n, KindCall.Prepare, info);
        }

        /// <summary>The process pass of one script output, then its notifications.</summary>
        internal void ProcessOutput(PlayableOutputData o, double dt, bool deliver = true)
        {
            if (o.Destroyed) return;
            if (o.Source != null) Process(o, o.Source, o.SourcePort, 1f, 1f, 1.0, true, dt, 0);
            if (deliver) DeliverNotifications(o);
        }

        void Process(PlayableOutputData o, PlayableNode n, int viaPort, float w, float ew, double parentSpeed, bool parentPlaying, double dt, int depth)
        {
            if (n == null || n.Destroyed || depth > MaxDepth) return;
            // Held playables are skipped like paused ones (Unbound's choice for a held playable
            // that a script output also reaches; unmeasured).
            bool playing = parentPlaying && n.State == PlayState.Playing && !n.Held;
            if (!playing) return;
            double speed = parentSpeed * n.Speed;
            if (n.Traversal == PlayableTraversalMode.Passthrough)
            {
                if (viaPort >= 0 && viaPort < n.Inputs.Count)
                {
                    var i = n.Inputs[viaPort];
                    Process(o, i.Node, i.SourcePort, i.Weight, ew * i.Weight, speed, playing, dt, depth + 1);
                }
            }
            else
            {
                for (int k = 0; k < n.Inputs.Count; k++)
                {
                    var i = n.Inputs[k];
                    if (i.Node == null) continue;
                    Process(o, i.Node, i.SourcePort, i.Weight, ew * i.Weight, speed, playing, dt, depth + 1);
                }
            }
            var b = n.Behaviour;
            if (b == null || n.Destroyed) return;
            var info = MakeInfo(n, dt, w, ew, parentSpeed, speed, n.FrameFlags, true, o, false);
            Call(b, Callback.Process, n.ToPlayable(), info, o.UserData);
        }

        internal void DeliverNotifications(PlayableOutputData o)
        {
            if (o.Pending.Count == 0) return;
            var pending = Scratch<(PlayableHandle Origin, INotification Notification, object Context)>.Rent(o.Pending);
            var receivers = Scratch<INotificationReceiver>.Rent(o.Receivers);
            o.Pending.Clear();
            try
            {
                foreach (var r in receivers)
                {
                    if (r == null || (r is Object uo && uo == null)) continue;
                    foreach (var (origin, notification, context) in pending)
                    {
                        try { r.OnNotify(new Playable(origin), notification, context); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                }
            }
            finally
            {
                Scratch<(PlayableHandle, INotification, object)>.Return(pending);
                Scratch<INotificationReceiver>.Return(receivers);
            }
        }

        /// <summary>PlayableGraph.Evaluate: every output, now, as an evaluation.</summary>
        internal void Evaluate(double dt)
        {
            PreparePass(dt, evaluate: true);
            EvaluateOutputs(dt, seek: true);
            OutputSystems.Flush();
            FlushFinished();
            FrameId += 2;
        }

        /// <summary>Processes every output after a prepare pass: script outputs, then the other
        /// kinds through their systems (animation, audio).</summary>
        internal void EvaluateOutputs(double dt, bool seek)
        {
            var outputs = Scratch<PlayableOutputData>.Rent(Outputs);
            try
            {
                foreach (var o in outputs)
                {
                    if (o.Destroyed) continue;
                    if (o.OutputType == typeof(ScriptPlayableOutput)) ProcessOutput(o, dt);
                    else OutputSystems.Process(o, dt, seek);
                }
            }
            finally { Scratch<PlayableOutputData>.Return(outputs); }
        }

        /// <summary>A Play in the player loop: started playables learn they play, except those a
        /// culled Animator holds (they learn they are paused in the prepare pass; 6000.0.71f1,
        /// animculling clock/mixerScript).</summary>
        void StartPass()
        {
            var outputs = Scratch<PlayableOutputData>.Rent(Outputs);
            var held = Scratch<PlayableNode>.RentSet();
            try
            {
                CollectHeld(held);
                foreach (var o in outputs)
                {
                    if (o.Destroyed || o.Source == null) continue;
                    var start = new Start { Graph = this, Output = o, Held = held };
                    Walk(o, ref start);
                }
            }
            finally
            {
                Scratch<PlayableOutputData>.Return(outputs);
                Scratch<PlayableNode>.Return(held);
            }
            FrameId++;
        }

        struct Start : INodeVisitor
        {
            internal PlayableGraphData Graph;
            internal PlayableOutputData Output;
            internal HashSet<PlayableNode> Held;

            public void Visit(PlayableNode n, in NodeVisit v)
            {
                if (n.Destroyed || !v.Playing || Held.Contains(n)) return;
                var b = n.Behaviour;
                if (b == null)
                {
                    n.Started = true;
                    return;
                }
                var info = Graph.MakeInfo(n, 0, v, n.FrameFlags & ControlFlags, true, Output, false);
                var p = n.ToPlayable();
                if (!n.Started)
                {
                    n.Started = true;
                    Call(b, Callback.GraphStart, p, info);
                }
                if (!n.LastPlaying)
                {
                    n.LastPlaying = true;
                    Call(b, Callback.Play, p, info);
                }
            }
        }

        /// <summary>A Stop in the player loop: playing playables pause and every one stops.</summary>
        void StopPass()
        {
            FrameId++;
            var outputs = Scratch<PlayableOutputData>.Rent(Outputs);
            var seen = Scratch<PlayableNode>.RentSet();
            try
            {
                foreach (var o in outputs)
                {
                    if (o.Destroyed || o.Source == null) continue;
                    var stop = new StopVisit { Graph = this, Output = o, Seen = seen };
                    Walk(o, ref stop);
                }
            }
            finally
            {
                Scratch<PlayableOutputData>.Return(outputs);
                Scratch<PlayableNode>.Return(seen);
            }
            FrameId++;
            Stopped = true;
            OutputSystems.Stopped(this);
        }

        struct StopVisit : INodeVisitor
        {
            internal PlayableGraphData Graph;
            internal PlayableOutputData Output;
            internal HashSet<PlayableNode> Seen;

            public void Visit(PlayableNode n, in NodeVisit v)
            {
                if (n.Destroyed || !Seen.Add(n)) return;
                var b = n.Behaviour;
                var p = n.ToPlayable();
                if (v.Playing && n.LastPlaying)
                {
                    n.LastPlaying = false;
                    var info = Graph.MakeInfo(n, 0, v, n.FrameFlags & ControlFlags, true, Output, false);
                    if (b != null) Call(b, Callback.Pause, p, info);
                    else if (n.Kind != null) CallKind(n.Kind, n, KindCall.Pause, info);
                }
                if (n.Started)
                {
                    n.Started = false;
                    if (b != null) Call(b, Callback.GraphStop, p, default);
                }
            }
        }

        double DeltaTime()
        {
            switch (UpdateMode)
            {
                case DirectorUpdateMode.GameTime: return UnityEngine.Time.deltaTime;
                case DirectorUpdateMode.UnscaledGameTime: return UnityEngine.Time.unscaledDeltaTime;
                case DirectorUpdateMode.DSPClock: return PlayableSystem.DspDelta;
                default: return 0;
            }
        }

        /// <summary>DirectorUpdate: pending stops and starts, then the prepare pass of every playing
        /// graph, then their script outputs.</summary>
        internal static void UpdateAll()
        {
            FlushDestroyed();
            var graphs = Scratch<PlayableGraphData>.Rent(s_Graphs);
            var frame = Scratch<(PlayableGraphData Graph, double Delta)>.Rent();
            try
            {
                foreach (var g in graphs)
                {
                    if (g.PendingStop && !g.Destroyed)
                    {
                        g.PendingStop = false;
                        g.StopPass();
                    }
                }
                graphs.Clear();
                graphs.AddRange(s_Playing);
                foreach (var g in graphs)
                {
                    if (g.Destroyed || !g.Playing || g.UpdateMode == DirectorUpdateMode.Manual) continue;
                    frame.Add((g, 0));
                }
                foreach (var (g, _) in frame)
                {
                    if (!g.PendingStart) continue;
                    g.PendingStart = false;
                    g.EverStarted = true;
                    g.StartPass();
                }
                for (int i = 0; i < frame.Count; i++)
                {
                    var g = frame[i].Graph;
                    double delta = g.DeltaTime();
                    frame[i] = (g, delta);
                    if (g.Destroyed) continue;
                    g.Director?.BeforeUpdate(delta);
                    g.PreparePass(delta, evaluate: false);
                }
                foreach (var (g, delta) in frame)
                {
                    if (g.Destroyed) continue;
                    var outputs = Scratch<PlayableOutputData>.Rent(g.Outputs);
                    foreach (var o in outputs)
                        if (o.OutputType == typeof(ScriptPlayableOutput)) g.ProcessOutput(o, delta, deliver: false);
                    Scratch<PlayableOutputData>.Return(outputs);
                    g.FrameId++;
                }
                s_Updated.Clear();
                foreach (var (g, delta) in frame)
                {
                    if (g.Destroyed) continue;
                    s_Updated.Add((g, delta));
                    g.Director?.AfterUpdate();
                }
            }
            finally
            {
                Scratch<PlayableGraphData>.Return(graphs);
                Scratch<(PlayableGraphData, double)>.Return(frame);
            }
        }

        /// <summary>The graphs this frame's DirectorUpdate advanced, with their delta times: their
        /// other outputs (animation, audio) process in the animation phase.</summary>
        static readonly List<(PlayableGraphData Graph, double Delta)> s_Updated = new List<(PlayableGraphData, double)>();

        /// <summary>PreLateUpdate.DirectorUpdateAnimationBegin: the animation (and audio) outputs of
        /// the graphs DirectorUpdate advanced.</summary>
        internal static void ProcessOtherOutputs()
        {
            if (s_Updated.Count == 0) return;
            var updated = Scratch<(PlayableGraphData Graph, double Delta)>.Rent(s_Updated);
            s_Updated.Clear();
            try
            {
                foreach (var (g, dt) in updated)
                {
                    if (g.Destroyed) continue;
                    var outputs = Scratch<PlayableOutputData>.Rent(g.Outputs);
                    foreach (var o in outputs)
                        if (o.OutputType != typeof(ScriptPlayableOutput))
                            OutputSystems.Process(o, o.Engine is AnimationOutputState && g.UpdateMode == DirectorUpdateMode.GameTime ? AnimationSystem.AutomaticFrameDelta(dt) : dt, false);
                    Scratch<PlayableOutputData>.Return(outputs);
                }
            }
            finally { Scratch<(PlayableGraphData, double)>.Return(updated); }
        }

        /// <summary>PreLateUpdate.DirectorDeferredEvaluate: notifications the frame's updates pushed.</summary>
        internal static void DeliverAllNotifications()
        {
            var graphs = Scratch<PlayableGraphData>.Rent(s_Graphs);
            try
            {
                foreach (var g in graphs)
                {
                    if (g.Destroyed) continue;
                    var outputs = Scratch<PlayableOutputData>.Rent(g.Outputs);
                    foreach (var o in outputs)
                        if (!o.Destroyed) g.DeliverNotifications(o);
                    Scratch<PlayableOutputData>.Return(outputs);
                }
            }
            finally { Scratch<PlayableGraphData>.Return(graphs); }
        }

        // ------------------------------------------------------------------ callbacks

        internal enum Callback { Create, GraphStart, GraphStop, Play, Pause, Prepare, Process, Destroy }

        /// <summary>One behaviour callback; an exception is logged and the pass goes on, as in Unity.</summary>
        internal static void Call(IPlayableBehaviour b, Callback callback, Playable p, in FrameData info, object playerData = null)
        {
            try
            {
                switch (callback)
                {
                    case Callback.Create: b.OnPlayableCreate(p); break;
                    case Callback.GraphStart: b.OnGraphStart(p); break;
                    case Callback.GraphStop: b.OnGraphStop(p); break;
                    case Callback.Play: b.OnBehaviourPlay(p, info); break;
                    case Callback.Pause: b.OnBehaviourPause(p, info); break;
                    case Callback.Prepare: b.PrepareFrame(p, info); break;
                    case Callback.Process: b.ProcessFrame(p, info, playerData); break;
                    case Callback.Destroy: b.OnPlayableDestroy(p); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        enum KindCall { Play, Pause, Prepare }

        static void CallKind(PlayableKind kind, PlayableNode n, KindCall call, in FrameData info)
        {
            try
            {
                if (call == KindCall.Prepare) kind.Prepare(n, info);
                else kind.PlayStateChanged(n, call == KindCall.Play, info);
            }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Reusable lists and sets for the graph passes. A pass rents its own (passes nest
    /// when a callback evaluates a graph) and returns it cleared, so warm passes allocate nothing.</summary>
    internal static class Scratch<T>
    {
        static readonly Stack<List<T>> s_Lists = new Stack<List<T>>();
        static readonly Stack<HashSet<T>> s_Sets = new Stack<HashSet<T>>();

        internal static List<T> Rent() => s_Lists.Count > 0 ? s_Lists.Pop() : new List<T>();

        /// <summary>A snapshot of <paramref name="items"/>, safe against callbacks that change it.</summary>
        internal static List<T> Rent(List<T> items)
        {
            var list = Rent();
            list.AddRange(items);
            return list;
        }

        internal static void Return(List<T> list)
        {
            list.Clear();
            s_Lists.Push(list);
        }

        internal static HashSet<T> RentSet() => s_Sets.Count > 0 ? s_Sets.Pop() : new HashSet<T>();

        internal static void Return(HashSet<T> set)
        {
            set.Clear();
            s_Sets.Push(set);
        }
    }

    /// <summary>
    /// What happens to outputs other than script outputs (animation and audio): the modules
    /// register a processor for their output type.
    /// </summary>
    internal static class OutputSystems
    {
        internal interface IProcessor
        {
            void Process(PlayableOutputData output, double dt, bool seek);
            void Stopped(PlayableGraphData graph);
            /// <summary>Applies what Process gathered (the animation outputs of an Evaluate).</summary>
            void Flush();
        }

        static readonly Dictionary<Type, IProcessor> s_Processors = new Dictionary<Type, IProcessor>();

        internal static void Register(Type outputType, IProcessor processor) => s_Processors[outputType] = processor;

        internal static void Process(PlayableOutputData o, double dt, bool seek)
        {
            if (o.Destroyed || o.OutputType == null) return;
            if (s_Processors.TryGetValue(o.OutputType, out var p))
            {
                try { p.Process(o, dt, seek); }
                catch (Exception e) { Debug.LogException(e); }
            }
            else o.Graph.DeliverNotifications(o);
        }

        internal static void Flush()
        {
            foreach (var p in s_Processors.Values)
            {
                try { p.Flush(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal static void Stopped(PlayableGraphData g)
        {
            foreach (var p in s_Processors.Values)
            {
                try { p.Stopped(g); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
