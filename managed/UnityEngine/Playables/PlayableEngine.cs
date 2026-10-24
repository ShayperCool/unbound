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

        /// <summary>Whether reaching its duration (wrap mode None) makes the playable done; engine
        /// playables that play media keep going (clips) or decide themselves (audio clips).</summary>
        internal virtual bool FinishesAtDuration => true;

        internal virtual bool CanChangeInputs => true;
        internal virtual bool CanSetWeights => true;
        internal virtual bool CanDestroy => true;
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
                    Invoke(() => b.OnGraphStop(p));
                }
                Invoke(() => b.OnPlayableDestroy(p));
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
            var seen = new HashSet<PlayableNode>();
            if (PlayingAtDestroy) FrameId++;
            foreach (var o in Outputs.ToArray())
            {
                if (o.Source == null || Stopped) continue;
                Walk(o.Source, o.SourcePort, 1f, 1f, 1.0, true, (n, w, ew, ps, s, playing) =>
                {
                    if (!playing || !seen.Add(n)) return;
                    var b = n.Behaviour;
                    if (b == null) return;
                    var info = MakeInfo(n, 0, w, ew, ps, s, n.FrameFlags & ControlFlags, true, o, false);
                    n.LastPlaying = false;
                    var p = n.ToPlayable();
                    Invoke(() => b.OnBehaviourPause(p, info));
                });
            }
            foreach (var n in Nodes.ToArray()) FinishNode(n);
            foreach (var o in Outputs.ToArray()) DestroyOutput(o);
        }

        internal static void FlushDestroyed()
        {
            if (s_PendingDestroy.Count == 0) return;
            var list = s_PendingDestroy.ToArray();
            s_PendingDestroy.Clear();
            foreach (var g in list) g.FinishDestroy();
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

        internal delegate void Visitor(PlayableNode node, float weight, float effectiveWeight, double parentSpeed, double speed, bool playing);

        /// <summary>Depth first, parent first, along the inputs an output reaches through a port.</summary>
        internal static void Walk(PlayableNode n, int viaPort, float w, float ew, double parentSpeed, bool parentPlaying, Visitor visit, int depth = 0)
        {
            if (n == null || depth > 256) return;
            bool playing = parentPlaying && n.State == PlayState.Playing;
            double speed = parentSpeed * n.Speed;
            visit(n, w, ew, parentSpeed, speed, playing);
            if (n.Traversal == PlayableTraversalMode.Passthrough)
            {
                if (viaPort >= 0 && viaPort < n.Inputs.Count)
                {
                    var i = n.Inputs[viaPort];
                    Walk(i.Node, i.SourcePort, i.Weight, ew * i.Weight, speed, playing, visit, depth + 1);
                }
                return;
            }
            for (int k = 0; k < n.Inputs.Count; k++)
            {
                var i = n.Inputs[k];
                if (i.Node == null) continue;
                Walk(i.Node, i.SourcePort, i.Weight, ew * i.Weight, speed, playing, visit, depth + 1);
            }
        }

        const FrameData.Flags ControlFlags = FrameData.Flags.SeekOccured | FrameData.Flags.Loop | FrameData.Flags.Hold;

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

        /// <summary>Playables that reached their duration this evaluation: they report done once
        /// the evaluation is over.</summary>
        readonly List<PlayableNode> m_Finished = new List<PlayableNode>();

        void FlushFinished()
        {
            foreach (var n in m_Finished) n.Done = true;
            m_Finished.Clear();
        }

        /// <summary>Moves a playing playable's time by its share of the frame, with its duration rules.</summary>
        void Advance(PlayableNode n, double delta, ref FrameData.Flags flags)
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
                        if (n.Kind == null || n.Kind.FinishesAtDuration) m_Finished.Add(n);
                        break;
                }
            }
            n.Time = t;
        }

        /// <summary>The prepare pass over the given outputs (all when null).</summary>
        internal void PreparePass(double dt, bool evaluate, Func<PlayableOutputData, bool> which = null)
        {
            ulong frame = FrameId;
            foreach (var o in Outputs.ToArray())
            {
                if (o.Destroyed || o.Source == null || (which != null && !which(o))) continue;
                Walk(o.Source, o.SourcePort, 1f, 1f, 1.0, true, (n, w, ew, ps, s, playing) =>
                {
                    if (n.Destroyed || n.PreparedFrame == frame) return;
                    n.PreparedFrame = frame;
                    var flags = (FrameData.Flags)0;
                    var b = n.Behaviour;

                    if (b == null) n.Started = true;
                    if (playing)
                    {
                        if (n.TimeSet)
                        {
                            flags |= FrameData.Flags.SeekOccured;
                            n.TimeSet = false;
                        }
                        else Advance(n, dt * s, ref flags);
                    }
                    n.FrameFlags = flags;
                    var info = MakeInfo(n, dt, w, ew, ps, s, flags, playing, o, evaluate);
                    var p = n.ToPlayable();
                    if (b != null)
                    {
                        if (!n.Started)
                        {
                            n.Started = true;
                            Invoke(() => b.OnGraphStart(p));
                        }
                        if (playing != n.LastPlaying)
                        {
                            n.LastPlaying = playing;
                            if (playing) Invoke(() => b.OnBehaviourPlay(p, info));
                            else Invoke(() => b.OnBehaviourPause(p, info));
                        }
                        if (playing) Invoke(() => b.PrepareFrame(p, info));
                    }
                    else if (playing != n.LastPlaying)
                    {
                        n.LastPlaying = playing;
                        if (n.Kind != null) Invoke(() => n.Kind.PlayStateChanged(n, playing, info));
                    }
                    if (playing && n.Kind != null) Invoke(() => n.Kind.Prepare(n, info));
                });
            }
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
            if (n == null || n.Destroyed || depth > 256) return;
            bool playing = parentPlaying && n.State == PlayState.Playing;
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
            var p = n.ToPlayable();
            object data = o.UserData;
            Invoke(() => b.ProcessFrame(p, info, data));
        }

        internal void DeliverNotifications(PlayableOutputData o)
        {
            if (o.Pending.Count == 0) return;
            var pending = o.Pending.ToArray();
            o.Pending.Clear();
            foreach (var r in o.Receivers.ToArray())
            {
                if (r == null || (r is Object uo && uo == null)) continue;
                foreach (var (origin, notification, context) in pending)
                {
                    var originPlayable = new Playable(origin);
                    Invoke(() => r.OnNotify(originPlayable, notification, context));
                }
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
            foreach (var o in Outputs.ToArray())
            {
                if (o.Destroyed) continue;
                if (o.OutputType == typeof(ScriptPlayableOutput)) ProcessOutput(o, dt);
                else OutputSystems.Process(o, dt, seek);
            }
        }

        /// <summary>A Play in the player loop: started playables learn they play.</summary>
        void StartPass()
        {
            ulong frame = FrameId;
            foreach (var o in Outputs.ToArray())
            {
                if (o.Destroyed || o.Source == null) continue;
                Walk(o.Source, o.SourcePort, 1f, 1f, 1.0, true, (n, w, ew, ps, s, playing) =>
                {
                    if (n.Destroyed || !playing) return;
                    var b = n.Behaviour;
                    var flags = n.FrameFlags & ControlFlags;
                    var info = MakeInfo(n, 0, w, ew, ps, s, flags, true, o, false);
                    var p = n.ToPlayable();
                    if (b == null)
                    {
                        n.Started = true;
                        return;
                    }
                    if (!n.Started)
                    {
                        n.Started = true;
                        Invoke(() => b.OnGraphStart(p));
                    }
                    if (!n.LastPlaying)
                    {
                        n.LastPlaying = true;
                        Invoke(() => b.OnBehaviourPlay(p, info));
                    }
                });
            }
            FrameId++;
        }

        /// <summary>A Stop in the player loop: playing playables pause and every one stops.</summary>
        void StopPass()
        {
            FrameId++;
            var seen = new HashSet<PlayableNode>();
            foreach (var o in Outputs.ToArray())
            {
                if (o.Destroyed || o.Source == null) continue;
                Walk(o.Source, o.SourcePort, 1f, 1f, 1.0, true, (n, w, ew, ps, s, playing) =>
                {
                    if (n.Destroyed || !seen.Add(n)) return;
                    var b = n.Behaviour;
                    var p = n.ToPlayable();
                    if (playing && n.LastPlaying)
                    {
                        n.LastPlaying = false;
                        var info = MakeInfo(n, 0, w, ew, ps, s, n.FrameFlags & ControlFlags, true, o, false);
                        if (b != null) Invoke(() => b.OnBehaviourPause(p, info));
                        else n.Kind?.PlayStateChanged(n, false, info);
                    }
                    if (n.Started)
                    {
                        n.Started = false;
                        if (b != null) Invoke(() => b.OnGraphStop(p));
                    }
                });
            }
            FrameId++;
            Stopped = true;
            OutputSystems.Stopped(this);
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
            foreach (var g in s_Graphs.ToArray())
            {
                if (g.PendingStop && !g.Destroyed)
                {
                    g.PendingStop = false;
                    g.StopPass();
                }
            }
            var playing = new List<PlayableGraphData>();
            foreach (var g in s_Playing.ToArray())
            {
                if (g.Destroyed || !g.Playing || g.UpdateMode == DirectorUpdateMode.Manual) continue;
                playing.Add(g);
            }
            foreach (var g in playing)
            {
                if (!g.PendingStart) continue;
                g.PendingStart = false;
                g.EverStarted = true;
                g.StartPass();
            }
            var deltas = new double[playing.Count];
            for (int i = 0; i < playing.Count; i++)
            {
                var g = playing[i];
                deltas[i] = g.DeltaTime();
                if (g.Destroyed) continue;
                g.Director?.BeforeUpdate(deltas[i]);
                g.PreparePass(deltas[i], evaluate: false);
            }
            for (int i = 0; i < playing.Count; i++)
            {
                var g = playing[i];
                if (g.Destroyed) continue;
                foreach (var o in g.Outputs.ToArray())
                    if (o.OutputType == typeof(ScriptPlayableOutput)) g.ProcessOutput(o, deltas[i], deliver: false);
                g.FrameId++;
            }
            s_Updated.Clear();
            for (int i = 0; i < playing.Count; i++)
            {
                var g = playing[i];
                if (g.Destroyed) continue;
                g.FlushFinished();
                s_Updated.Add((g, deltas[i]));
                g.Director?.AfterUpdate();
            }
        }

        /// <summary>The graphs this frame's DirectorUpdate advanced, with their delta times: their
        /// other outputs (animation, audio) process in the animation phase.</summary>
        static readonly List<(PlayableGraphData Graph, double Delta)> s_Updated = new List<(PlayableGraphData, double)>();

        /// <summary>PreLateUpdate.DirectorUpdateAnimationBegin: the animation (and audio) outputs of
        /// the graphs DirectorUpdate advanced.</summary>
        internal static void ProcessOtherOutputs()
        {
            foreach (var (g, dt) in s_Updated.ToArray())
            {
                if (g.Destroyed) continue;
                foreach (var o in g.Outputs.ToArray())
                    if (o.OutputType != typeof(ScriptPlayableOutput)) OutputSystems.Process(o, dt, false);
            }
            s_Updated.Clear();
        }

        /// <summary>PreLateUpdate.DirectorDeferredEvaluate: notifications the frame's updates pushed.</summary>
        internal static void DeliverAllNotifications()
        {
            foreach (var g in s_Graphs.ToArray())
            {
                if (g.Destroyed) continue;
                foreach (var o in g.Outputs.ToArray())
                    if (!o.Destroyed) g.DeliverNotifications(o);
            }
        }

        internal static void Invoke(Action a)
        {
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
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
