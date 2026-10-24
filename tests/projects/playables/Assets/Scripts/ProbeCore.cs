using System;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// The playable graph itself, evaluated synchronously: callback order, time, speed, weights, play
/// states, traversal modes, wrap modes, connections, outputs and destruction.
/// </summary>
public static class ProbeCore
{
    public static void RunAll()
    {
        Run("core.defaults", Defaults);
        Run("core.tree", Tree);
        Run("core.time", Time);
        Run("core.outputs", Outputs);
        Run("core.passthrough", Passthrough);
        Run("core.structure", Structure);
        Run("core.notifications", Notifications);
        Run("core.wrap", Wrap);
    }

    static void Run(string name, Action scenario)
    {
        if (!ProbeEntry.Wanted(name)) return;
        ProbeLog.Begin(name);
        TraceBehaviour.Reset();
        try { scenario(); }
        catch (Exception e) { ProbeLog.Event("exception " + e.GetType().Name + ": " + e.Message); }
    }

    static void Step(string what) => ProbeLog.Event("-- " + what);

    static void Eval(PlayableGraph graph, float dt)
    {
        Step("Evaluate " + ProbeLog.F(dt));
        graph.Evaluate(dt);
    }

    static void Dump(string label, Playable p) => ProbeLog.Event($"{label}: {TraceBehaviour.State(p)} speed={ProbeLog.F(p.GetSpeed())} dur={ProbeLog.F(p.GetDuration())} in={p.GetInputCount()} out={p.GetOutputCount()}");

    static UserData NewUserData(string name)
    {
        var o = ScriptableObject.CreateInstance<UserData>();
        o.name = name;
        return o;
    }

    static void Defaults()
    {
        var graph = PlayableGraph.Create("defaults");
        ProbeLog.Value("valid", graph.IsValid());
        ProbeLog.Value("playing", graph.IsPlaying());
        ProbeLog.Value("done", graph.IsDone());
        ProbeLog.Value("updateMode", graph.GetTimeUpdateMode().ToString());
        ProbeLog.Value("name", graph.GetEditorName());
        ProbeLog.Value("resolverNull", graph.GetResolver() == null);
        var p = Playable.Create(graph, 2);
        ProbeLog.Value("playable.type", p.GetPlayableType()?.Name ?? "null");
        ProbeLog.Value("playable.state", p.GetPlayState().ToString());
        ProbeLog.Value("playable.time", p.GetTime());
        ProbeLog.Value("playable.speed", p.GetSpeed());
        ProbeLog.Value("playable.duration", p.GetDuration());
        ProbeLog.Value("playable.done", p.IsDone());
        ProbeLog.Value("playable.propagate", p.GetPropagateSetTime());
        ProbeLog.Value("playable.traversal", p.GetTraversalMode().ToString());
        ProbeLog.Value("playable.inputs", p.GetInputCount());
        ProbeLog.Value("playable.outputs", p.GetOutputCount());
        ProbeLog.Value("playable.weight0", p.GetInputWeight(0));
        ProbeLog.Value("playable.input0null", p.GetInput(0).IsNull());
        ProbeLog.Value("playable.canChange", p.CanChangeInputs());
        ProbeLog.Value("playable.canWeights", p.CanSetWeights());
        ProbeLog.Value("playable.canDestroy", p.CanDestroy());
        ProbeLog.Value("playable.leadTime", p.GetLeadTime());
        var s = TraceBehaviour.Create(graph, "s");
        ProbeLog.Value("script.type", ((Playable)s).GetPlayableType()?.Name ?? "null");
        ProbeLog.Value("script.outputs", s.GetOutputCount());
        ProbeLog.Value("graph.playables", graph.GetPlayableCount());
        ProbeLog.Value("graph.roots", graph.GetRootPlayableCount());
        var o = ScriptPlayableOutput.Create(graph, "out");
        ProbeLog.Value("output.weight", o.GetWeight());
        ProbeLog.Value("output.port", o.GetSourceOutputPort());
        ProbeLog.Value("output.sourceNull", o.GetSourcePlayable().IsNull());
        ProbeLog.Value("output.type", ((PlayableOutput)o).GetPlayableOutputType()?.Name ?? "null");
        ProbeLog.Value("output.userNull", o.GetUserData() == null);
        ProbeLog.Value("graph.outputs", graph.GetOutputCount());
        ProbeLog.Value("graph.scriptOutputs", graph.GetOutputCountByType<ScriptPlayableOutput>());
        ProbeLog.Value("graph.done.unconnected", graph.IsDone());
        o.SetSourcePlayable(s);
        ProbeLog.Value("graph.done.connected", graph.IsDone());
        ProbeLog.Value("playable.nullValid", Playable.Null.IsValid());
        ProbeLog.Value("playable.nullIsNull", Playable.Null.IsNull());
        ProbeLog.Value("handle.null", PlayableHandle.Null == default(PlayableHandle));
        graph.Destroy();
        ProbeLog.Value("graph.validAfterDestroy", graph.IsValid());
        ProbeLog.Value("playable.validAfterDestroy", p.IsValid());
        ProbeLog.Value("playable.nullAfterDestroy", p.IsNull());
    }

    /// <summary>root(0.25: c0(g), 0.75: c1) on a script output, evaluated through play state,
    /// speed, time, propagation and graph play/stop changes.</summary>
    static void Tree()
    {
        var graph = PlayableGraph.Create("tree");
        var outA = ScriptPlayableOutput.Create(graph, "A");
        TraceBehaviour.Outputs.Add((outA, "A"));
        Step("create");
        var root = TraceBehaviour.Create(graph, "root", 2);
        var c0 = TraceBehaviour.Create(graph, "c0", 1);
        var c1 = TraceBehaviour.Create(graph, "c1");
        var g = TraceBehaviour.Create(graph, "g");
        graph.Connect(c0, 0, root, 0);
        graph.Connect(c1, 0, root, 1);
        graph.Connect(g, 0, c0, 0);
        root.SetInputWeight(0, 0.25f);
        root.SetInputWeight(1, 0.75f);
        outA.SetSourcePlayable(root);
        outA.SetUserData(NewUserData("userA"));
        ProbeLog.Event($"roots={graph.GetRootPlayableCount()} playables={graph.GetPlayableCount()} c0.weight={ProbeLog.F(c0.GetInputWeight(0))}");
        Eval(graph, 0.1f);
        Eval(graph, 0.1f);
        Step("c1.Pause");
        c1.Pause();
        Eval(graph, 0.1f);
        Step("c1.Play");
        c1.Play();
        Eval(graph, 0.1f);
        Step("c0.SetSpeed 2, root.SetTime 1");
        c0.SetSpeed(2);
        root.SetTime(1);
        Eval(graph, 0.1f);
        Eval(graph, 0.1f);
        Step("root.SetPropagateSetTime true, root.SetTime 0.5");
        root.SetPropagateSetTime(true);
        root.SetTime(0.5);
        Dump("root", root); Dump("c0", c0); Dump("c1", c1); Dump("g", g);
        Eval(graph, 0.1f);
        Eval(graph, 0f);
        Step("Evaluate()");
        graph.Evaluate();
        Step("root.SetSpeed 0.5");
        root.SetSpeed(0.5);
        Eval(graph, 0.2f);
        Step("root.Pause");
        root.Pause();
        Eval(graph, 0.1f);
        Step("root.Play");
        root.Play();
        Eval(graph, 0.1f);
        Step("root.SetSpeed -1");
        root.SetSpeed(-1);
        Eval(graph, 0.1f);
        root.SetSpeed(1);
        Step("output weight 0.5");
        outA.SetWeight(0.5f);
        Eval(graph, 0.1f);
        Step("root weight1 0");
        root.SetInputWeight(1, 0f);
        Eval(graph, 0.1f);
        Step("graph.Play");
        graph.Play();
        ProbeLog.Event("playing=" + graph.IsPlaying());
        Eval(graph, 0.1f);
        Step("graph.Stop");
        graph.Stop();
        ProbeLog.Event("playing=" + graph.IsPlaying());
        Eval(graph, 0.1f);
        Step("graph.Play again");
        graph.Play();
        Step("c1.SetDone true");
        c1.SetDone(true);
        Dump("c1", c1);
        Eval(graph, 0.1f);
        Step("graph.Destroy");
        graph.Destroy();
        Step("end");
    }

    /// <summary>Durations, done flags and wrap modes of single playables.</summary>
    static void Time()
    {
        foreach (var mode in new[] { "default", "Hold", "Loop", "None" })
        {
            var graph = PlayableGraph.Create("time");
            var o = ScriptPlayableOutput.Create(graph, "T");
            TraceBehaviour.Outputs.Add((o, "T"));
            TraceBehaviour.Quiet = true;
            var p = TraceBehaviour.Create(graph, "p");
            TraceBehaviour.Quiet = false;
            p.SetDuration(1);
            o.SetSourcePlayable(p);
            Step("mode " + mode);
            for (int i = 0; i < 6; i++)
            {
                Eval(graph, 0.3f);
                ProbeLog.Event($"after: {TraceBehaviour.State(p)} graphDone={graph.IsDone()}");
            }
            Step("SetTime 5");
            p.SetTime(5);
            Eval(graph, 0.1f);
            Step("SetTime -1");
            p.SetTime(-1);
            Eval(graph, 0.1f);
            Step("SetDone false, SetTime 0.5, speed -1");
            p.SetDone(false);
            p.SetTime(0.5);
            p.SetSpeed(-1);
            for (int i = 0; i < 3; i++)
            {
                Eval(graph, 0.3f);
                ProbeLog.Event($"after: {TraceBehaviour.State(p)} graphDone={graph.IsDone()}");
            }
            TraceBehaviour.Quiet = true;
            graph.Destroy();
            TraceBehaviour.Quiet = false;
            TraceBehaviour.Outputs.Clear();
        }
    }

    /// <summary>Two script outputs sharing a subtree; output order, weights and user data.</summary>
    static void Outputs()
    {
        var graph = PlayableGraph.Create("outputs");
        var a = ScriptPlayableOutput.Create(graph, "A");
        var b = ScriptPlayableOutput.Create(graph, "B");
        TraceBehaviour.Outputs.Add((a, "A"));
        TraceBehaviour.Outputs.Add((b, "B"));
        var shared = TraceBehaviour.Create(graph, "shared");
        var mixA = TraceBehaviour.Create(graph, "mixA", 1);
        var mixB = TraceBehaviour.Create(graph, "mixB", 1);
        ProbeLog.Event("shared.outputs=" + shared.GetOutputCount());
        mixA.ConnectInput(0, shared, 0, 0.5f);
        ProbeLog.Event("shared.outputs after A=" + shared.GetOutputCount());
        shared.SetOutputCount(2);
        mixB.ConnectInput(0, shared, 1, 0.25f);
        a.SetSourcePlayable(mixA);
        b.SetSourcePlayable(mixB);
        a.SetUserData(NewUserData("userA"));
        b.SetUserData(NewUserData("userB"));
        b.SetWeight(0.5f);
        ProbeLog.Event($"roots={graph.GetRootPlayableCount()} outputs={graph.GetOutputCount()}");
        Eval(graph, 0.1f);
        Eval(graph, 0.1f);
        Step("b source = shared port 1");
        b.SetSourcePlayable(shared, 1);
        Eval(graph, 0.1f);
        Step("destroy output A");
        graph.DestroyOutput(a);
        ProbeLog.Event($"outputs={graph.GetOutputCount()} a.valid={a.IsOutputValid()}");
        Eval(graph, 0.1f);
        graph.Destroy();
    }

    /// <summary>A root with two output ports in passthrough mode: output i reaches input i only.</summary>
    static void Passthrough()
    {
        foreach (var mode in new[] { PlayableTraversalMode.Passthrough, PlayableTraversalMode.Mix })
        {
            var graph = PlayableGraph.Create("passthrough");
            var a = ScriptPlayableOutput.Create(graph, "A");
            var b = ScriptPlayableOutput.Create(graph, "B");
            TraceBehaviour.Outputs.Add((a, "A"));
            TraceBehaviour.Outputs.Add((b, "B"));
            TraceBehaviour.Quiet = true;
            var root = TraceBehaviour.Create(graph, "root", 2);
            var i0 = TraceBehaviour.Create(graph, "i0");
            var i1 = TraceBehaviour.Create(graph, "i1");
            TraceBehaviour.Quiet = false;
            root.SetOutputCount(2);
            root.SetTraversalMode(mode);
            root.ConnectInput(0, i0, 0, 1f);
            root.ConnectInput(1, i1, 0, 0.5f);
            a.SetSourcePlayable(root, 0);
            b.SetSourcePlayable(root, 1);
            Step("mode " + mode);
            Eval(graph, 0.1f);
            Eval(graph, 0.1f);
            TraceBehaviour.Quiet = true;
            graph.Destroy();
            TraceBehaviour.Quiet = false;
            TraceBehaviour.Outputs.Clear();
        }
    }

    /// <summary>Connections and destruction: disconnect, AddInput, SetInputCount, DestroyPlayable,
    /// DestroySubgraph, playables created while the graph runs.</summary>
    static void Structure()
    {
        var graph = PlayableGraph.Create("structure");
        var a = ScriptPlayableOutput.Create(graph, "A");
        TraceBehaviour.Outputs.Add((a, "A"));
        var root = TraceBehaviour.Create(graph, "root", 1);
        var x = TraceBehaviour.Create(graph, "x");
        var y = TraceBehaviour.Create(graph, "y", 1);
        var z = TraceBehaviour.Create(graph, "z");
        root.ConnectInput(0, x, 0, 1f);
        a.SetSourcePlayable(root);
        Eval(graph, 0.1f);
        Step("AddInput y");
        int port = root.AddInput(y, 0, 0.5f);
        y.ConnectInput(0, z, 0, 1f);
        ProbeLog.Event($"port={port} inputs={root.GetInputCount()} roots={graph.GetRootPlayableCount()}");
        Eval(graph, 0.1f);
        Step("Disconnect root 0");
        graph.Disconnect(root, 0);
        ProbeLog.Event($"inputs={root.GetInputCount()} input0null={root.GetInput(0).IsNull()} x.outputs={x.GetOutputCount()} x.output0null={x.GetOutput(0).IsNull()} roots={graph.GetRootPlayableCount()}");
        Eval(graph, 0.1f);
        Step("SetInputCount 3");
        root.SetInputCount(3);
        ProbeLog.Event($"inputs={root.GetInputCount()} weight2={ProbeLog.F(root.GetInputWeight(2))}");
        Step("connect x to occupied port 1");
        bool ok = graph.Connect(x, 0, root, 1);
        ProbeLog.Event("connected=" + ok);
        Step("graph.Play then create w");
        graph.Play();
        var w = TraceBehaviour.Create(graph, "w");
        root.ConnectInput(2, w, 0, 1f);
        Eval(graph, 0.1f);
        Step("DestroyPlayable z");
        graph.DestroyPlayable(z);
        ProbeLog.Event($"y.input0null={y.GetInput(0).IsNull()} y.inputs={y.GetInputCount()} playables={graph.GetPlayableCount()}");
        Eval(graph, 0.1f);
        Step("DestroySubgraph y");
        z = TraceBehaviour.Create(graph, "z2");
        y.ConnectInput(0, z, 0, 1f);
        graph.DestroySubgraph(y);
        ProbeLog.Event($"playables={graph.GetPlayableCount()} root.inputs={root.GetInputCount()}");
        Eval(graph, 0.1f);
        Step("SetInputCount 1 (drops w)");
        root.SetInputCount(1);
        ProbeLog.Event($"roots={graph.GetRootPlayableCount()} w.outputs={w.GetOutputCount()}");
        Eval(graph, 0.1f);
        Step("graph.Destroy");
        graph.Destroy();
    }

    /// <summary>The internal time wrap modes (Timeline sets them), through reflection.</summary>
    static void Wrap()
    {
        var get = typeof(PlayableHandle).GetMethod("GetTimeWrapMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var set = typeof(PlayableHandle).GetMethod("SetTimeWrapMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var mode in new[] { DirectorWrapMode.Hold, DirectorWrapMode.Loop, DirectorWrapMode.None })
        {
            var graph = PlayableGraph.Create("wrap");
            var o = ScriptPlayableOutput.Create(graph, "W");
            TraceBehaviour.Outputs.Add((o, "W"));
            TraceBehaviour.Quiet = true;
            var p = TraceBehaviour.Create(graph, "p", 1);
            var c = TraceBehaviour.Create(graph, "c");
            p.ConnectInput(0, c, 0, 1f);
            TraceBehaviour.Quiet = false;
            object handle = p.GetHandle();
            Step("mode " + mode + " default=" + get.Invoke(handle, null));
            set.Invoke(handle, new object[] { mode });
            p.SetDuration(1);
            o.SetSourcePlayable(p);
            for (int i = 0; i < 5; i++)
            {
                Eval(graph, 0.3f);
                ProbeLog.Event($"after: p {TraceBehaviour.State(p)} c {TraceBehaviour.State(c)} graphDone={graph.IsDone()}");
            }
            Step("big step 2.5");
            Eval(graph, 2.5f);
            Step("SetTime 0.9, speed -1");
            p.SetTime(0.9);
            p.SetSpeed(-1);
            p.SetDone(false);
            for (int i = 0; i < 5; i++) Eval(graph, 0.3f);
            TraceBehaviour.Quiet = true;
            graph.Destroy();
            TraceBehaviour.Quiet = false;
            TraceBehaviour.Outputs.Clear();
        }
    }

    class Note : INotification
    {
        public PropertyName id => new PropertyName("note");
    }

    class Receiver : INotificationReceiver
    {
        public string label;
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            ProbeLog.Event($"{label}.OnNotify origin={(origin.IsNull() ? "null" : ProbeLog.F(origin.GetTime()))} id={notification.id.GetHashCode() == new PropertyName("note").GetHashCode()} ctx={context}");
        }
    }

    class Pusher : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            ProbeLog.Event("pusher.ProcessFrame push");
            info.output.PushNotification(playable, new Note(), "fromProcess");
            ProbeLog.Event("pusher.ProcessFrame pushed");
        }
    }

    static void Notifications()
    {
        var graph = PlayableGraph.Create("notifications");
        var a = ScriptPlayableOutput.Create(graph, "A");
        var r1 = new Receiver { label = "r1" };
        var r2 = new Receiver { label = "r2" };
        a.AddNotificationReceiver(r1);
        a.AddNotificationReceiver(r2);
        a.AddNotificationReceiver(r1);
        ProbeLog.Event("receivers=" + a.GetNotificationReceivers().Length);
        var p = ScriptPlayable<Pusher>.Create(graph);
        a.SetSourcePlayable(p);
        Step("push outside evaluation");
        a.PushNotification(p, new Note(), "direct");
        ProbeLog.Event("after push");
        Eval(graph, 0.1f);
        Step("remove r1");
        a.RemoveNotificationReceiver(r1);
        ProbeLog.Event("receivers=" + a.GetNotificationReceivers().Length);
        Eval(graph, 0.1f);
        graph.Destroy();
    }
}
