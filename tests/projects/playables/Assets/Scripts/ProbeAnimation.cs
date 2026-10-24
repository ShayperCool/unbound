using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Reads the root's position from the stream, writes the child's from it, scales a
/// script's float, and reads a scene transform.</summary>
public struct ProbeJob : IAnimationJob
{
    public TransformStreamHandle root;
    public TransformStreamHandle child;
    public PropertyStreamHandle value;
    public TransformSceneHandle other;
    public float factor;

    public void ProcessRootMotion(AnimationStream stream) { }

    public void ProcessAnimation(AnimationStream stream)
    {
        var p = root.GetLocalPosition(stream);
        var o = other.GetPosition(stream);
        child.SetLocalPosition(stream, new Vector3(p.x * factor, o.y, stream.deltaTime));
        value.SetFloat(stream, value.GetFloat(stream) * 2f + p.x);
    }
}

/// <summary>Animation playables evaluated synchronously: clips, mixers, layer mixers, masks,
/// outputs sharing an Animator, the controller playable, events and animation jobs.</summary>
public static class ProbeAnimation
{
    public static void RunAll()
    {
        Run("anim.defaults", Defaults);
        Run("anim.clip", ClipScenario);
        Run("anim.mixer", Mixer);
        Run("anim.layers", Layers);
        Run("anim.outputs", Outputs);
        Run("anim.misc", Misc);
        Run("anim.controller", Controller);
        Run("anim.job", Job);
    }

    static void Run(string name, Action scenario)
    {
        if (!ProbeEntry.Wanted(name)) return;
        ProbeLog.Begin(name);
        TraceBehaviour.Reset();
        try { scenario(); }
        catch (Exception e) { ProbeLog.Event("exception " + e.GetType().Name + ": " + e.Message); }
    }

    internal static T Load<T>(string name) where T : UnityEngine.Object => Resources.Load<T>(name);

    internal static Animator MakeActor(string name)
    {
        var go = new GameObject(name);
        go.transform.localPosition = new Vector3(5, 5, 5);
        var child = new GameObject("Child");
        child.transform.SetParent(go.transform, false);
        child.transform.localPosition = new Vector3(0.5f, 0.5f, 0.5f);
        return go.AddComponent<Animator>();
    }

    internal static string V(Vector3 v) => $"({ProbeLog.F(v.x)}, {ProbeLog.F(v.y)}, {ProbeLog.F(v.z)})";

    internal static string Pose(Animator a)
    {
        var c = a.transform.Find("Child");
        return $"pos={V(a.transform.localPosition)} rot={V(a.transform.localEulerAngles)} child={V(c.localPosition)} childScale={V(c.localScale)} childActive={c.gameObject.activeSelf}";
    }

    static void Log(string what, Animator a) => ProbeLog.Event(what + ": " + Pose(a));

    /// <summary>What engine playables start with: duration, propagation, wrap mode, structure.</summary>
    static void Defaults()
    {
        var g = PlayableGraph.Create("defaults");
        var wrap = typeof(PlayableHandle).GetMethod("GetTimeWrapMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        void Describe(string name, Playable p)
        {
            object handle = p.GetHandle();
            ProbeLog.Event($"{name}: type={p.GetPlayableType()?.Name} dur={ProbeLog.F(p.GetDuration())} propagate={p.GetPropagateSetTime()} wrap={wrap.Invoke(handle, null)} " +
                $"in={p.GetInputCount()} out={p.GetOutputCount()} change={p.CanChangeInputs()} weights={p.CanSetWeights()} destroy={p.CanDestroy()} traversal={p.GetTraversalMode()} ps={p.GetPlayState()}");
        }
        Describe("clip", AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipA")));
        Describe("clipB", AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipB")));
        Describe("mixer", AnimationMixerPlayable.Create(g, 2));
        Describe("layers", AnimationLayerMixerPlayable.Create(g, 2));
        Describe("controller", AnimatorControllerPlayable.Create(g, Load<RuntimeAnimatorController>("Probe")));
        Describe("job", AnimationScriptPlayable.Create(g, new ProbeJob(), 1));
        Describe("audioClip", UnityEngine.Audio.AudioClipPlayable.Create(g, Load<AudioClip>("Tone"), false));
        Describe("audioMixer", UnityEngine.Audio.AudioMixerPlayable.Create(g, 2));
        var assembly = typeof(AnimationClipPlayable).Assembly;
        foreach (var (name, args) in new (string, object[])[]
        {
            ("UnityEngine.Animations.AnimationOffsetPlayable", new object[] { g, Vector3.zero, Quaternion.identity, 1 }),
            ("UnityEngine.Animations.AnimationMotionXToDeltaPlayable", new object[] { g }),
            ("UnityEngine.Animations.AnimationRemoveScalePlayable", new object[] { g, 1 }),
            ("UnityEngine.Animations.AnimationPosePlayable", new object[] { g }),
        })
        {
            var type = assembly.GetType(name);
            var create = type?.GetMethod("Create", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (create == null) { ProbeLog.Event(name + ": missing"); continue; }
            var playable = create.Invoke(null, args);
            var op = type.GetMethod("op_Implicit", new[] { type });
            Describe(type.Name, (Playable)op.Invoke(null, new[] { playable }));
        }
        ProbeLog.Event($"clip generic root: A={Load<AnimationClip>("ClipA").hasGenericRootTransform} Child={Load<AnimationClip>("ClipChild").hasGenericRootTransform} Rot={Load<AnimationClip>("ClipRot").hasGenericRootTransform}");
        g.Destroy();
    }

    static void ClipScenario()
    {
        var a = MakeActor("ClipActor");
        var g = PlayableGraph.Create("clip");
        var output = AnimationPlayableOutput.Create(g, "out", a);
        var clip = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipA"));
        output.SetSourcePlayable(clip);
        ProbeLog.Event($"duration={ProbeLog.F(clip.GetDuration())} clip={clip.GetAnimationClip().name} footIK={clip.GetApplyFootIK()} playableIK={clip.GetApplyPlayableIK()} target={output.GetTarget().name} weight={ProbeLog.F(output.GetWeight())} updateMode={g.GetTimeUpdateMode()}");
        Log("before", a);
        g.Evaluate(0f);
        Log("t=0", a);
        foreach (var dt in new[] { 0.25f, 0.25f, 0.5f, 0.5f, 0.3f })
        {
            g.Evaluate(dt);
            Log("+" + ProbeLog.F(dt) + " t=" + ProbeLog.F(clip.GetTime()), a);
        }
        clip.SetTime(0.3);
        g.Evaluate(0f);
        Log("SetTime 0.3", a);
        clip.SetSpeed(2);
        g.Evaluate(0.1f);
        Log("speed 2 +0.1", a);
        clip.Pause();
        g.Evaluate(0.1f);
        Log("paused +0.1", a);
        g.Destroy();
        Log("destroyed", a);

        var b = MakeActor("ClipActorB");
        g = PlayableGraph.Create("clipB");
        output = AnimationPlayableOutput.Create(g, "out", b);
        clip = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipB"));
        output.SetSourcePlayable(clip);
        foreach (var dt in new[] { 0f, 1f, 1f, 1f })
        {
            g.Evaluate(dt);
            Log("B +" + ProbeLog.F(dt) + " t=" + ProbeLog.F(clip.GetTime()) + " done=" + clip.IsDone(), b);
        }
        clip.SetDuration(2.0);
        clip.SetTime(1.5);
        g.Evaluate(0f);
        g.Evaluate(1f);
        Log("B duration 2, +1 t=" + ProbeLog.F(clip.GetTime()) + " done=" + clip.IsDone(), b);
        var rot = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipRot"));
        output.SetSourcePlayable(rot);
        rot.SetTime(0.5);
        g.Evaluate(0f);
        Log("rot 0.5", b);
        g.Destroy();
        UnityEngine.Object.DestroyImmediate(a.gameObject);
        UnityEngine.Object.DestroyImmediate(b.gameObject);
    }

    static void Mixer()
    {
        var a = MakeActor("MixActor");
        var g = PlayableGraph.Create("mixer");
        var output = AnimationPlayableOutput.Create(g, "out", a);
        var mixer = AnimationMixerPlayable.Create(g, 2);
        var ca = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipA"));
        var cb = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipB"));
        g.Connect(ca, 0, mixer, 0);
        g.Connect(cb, 0, mixer, 1);
        output.SetSourcePlayable(mixer);
        ca.SetTime(0.5);
        cb.SetTime(0.5);
        ca.Pause();
        cb.Pause();
        foreach (var (w0, w1) in new[] { (1f, 0f), (0f, 1f), (0.5f, 0f), (0.25f, 0.25f), (0.5f, 0.5f), (1f, 1f), (0.7f, 0.6f), (0f, 0f) })
        {
            mixer.SetInputWeight(0, w0);
            mixer.SetInputWeight(1, w1);
            g.Evaluate(0f);
            Log($"w=({ProbeLog.F(w0)}, {ProbeLog.F(w1)})", a);
        }
        var child = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipChild"));
        child.SetTime(0.5);
        child.Pause();
        g.Disconnect(mixer, 1);
        g.Connect(child, 0, mixer, 1);
        foreach (var (w0, w1) in new[] { (1f, 0f), (0f, 1f), (0.5f, 0.5f), (1f, 1f) })
        {
            mixer.SetInputWeight(0, w0);
            mixer.SetInputWeight(1, w1);
            g.Evaluate(0f);
            Log($"child w=({ProbeLog.F(w0)}, {ProbeLog.F(w1)})", a);
        }
        g.Destroy();
        UnityEngine.Object.DestroyImmediate(a.gameObject);
    }

    static void Layers()
    {
        var a = MakeActor("LayerActor");
        var g = PlayableGraph.Create("layers");
        var output = AnimationPlayableOutput.Create(g, "out", a);
        var layers = AnimationLayerMixerPlayable.Create(g, 2);
        var ca = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipA"));
        var cc = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipChild"));
        var cb = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipB"));
        g.Connect(ca, 0, layers, 0);
        g.Connect(cc, 0, layers, 1);
        output.SetSourcePlayable(layers);
        foreach (var c in new[] { ca, cc, cb })
        {
            c.SetTime(0.5);
            c.Pause();
        }
        ProbeLog.Event($"additive0={layers.IsLayerAdditive(0)} additive1={layers.IsLayerAdditive(1)}");
        foreach (var (w0, w1) in new[] { (1f, 1f), (1f, 0.5f), (0.5f, 1f), (0f, 1f), (0.5f, 0.5f) })
        {
            layers.SetInputWeight(0, w0);
            layers.SetInputWeight(1, w1);
            g.Evaluate(0f);
            Log($"w=({ProbeLog.F(w0)}, {ProbeLog.F(w1)})", a);
        }
        layers.SetLayerMaskFromAvatarMask(1, Load<AvatarMask>("ChildOnly"));
        layers.SetInputWeight(0, 1f);
        layers.SetInputWeight(1, 0.5f);
        g.Evaluate(0f);
        Log("masked child layer 0.5", a);
        g.Disconnect(layers, 1);
        g.Connect(cb, 0, layers, 1);
        layers.SetLayerMaskFromAvatarMask(1, new AvatarMask());
        foreach (var w1 in new[] { 1f, 0.5f })
        {
            layers.SetInputWeight(1, w1);
            g.Evaluate(0f);
            Log($"B override layer {ProbeLog.F(w1)}", a);
        }
        layers.SetLayerAdditive(1, true);
        foreach (var w1 in new[] { 1f, 0.5f })
        {
            layers.SetInputWeight(1, w1);
            g.Evaluate(0f);
            Log($"B additive layer {ProbeLog.F(w1)}", a);
        }
        g.Destroy();
        UnityEngine.Object.DestroyImmediate(a.gameObject);
    }

    static void Outputs()
    {
        var a = MakeActor("OutputsActor");
        var g = PlayableGraph.Create("outputs");
        var o1 = AnimationPlayableOutput.Create(g, "o1", a);
        var o2 = AnimationPlayableOutput.Create(g, "o2", a);
        var ca = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipA"));
        var cb = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipB"));
        ca.SetTime(0.5);
        cb.SetTime(0.5);
        ca.Pause();
        cb.Pause();
        o1.SetSourcePlayable(ca);
        o2.SetSourcePlayable(cb);
        g.Evaluate(0f);
        Log("o1=A o2=B", a);
        o2.SetWeight(0.5f);
        g.Evaluate(0f);
        Log("o2 weight 0.5", a);
        o2.SetWeight(0f);
        g.Evaluate(0f);
        Log("o2 weight 0", a);
        o1.SetWeight(0.5f);
        g.Evaluate(0f);
        Log("o1 weight 0.5, o2 0", a);
        o2.SetWeight(1f);
        UnityEngine.Experimental.Animations.AnimationPlayableOutputExtensions.SetSortingOrder(o1, 200);
        g.Evaluate(0f);
        Log("o1 sorting 200", a);
        var cc = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipChild"));
        cc.SetTime(0.5);
        cc.Pause();
        o1.SetSourcePlayable(cc);
        o1.SetWeight(1f);
        g.Evaluate(0f);
        Log("o1=Child o2=B", a);
        g.DestroyOutput(o2);
        g.Evaluate(0f);
        Log("o2 destroyed", a);
        o1.SetTarget(null);
        a.transform.localPosition = new Vector3(7, 7, 7);
        g.Evaluate(0f);
        Log("o1 target null", a);
        g.Destroy();
        UnityEngine.Object.DestroyImmediate(a.gameObject);
    }

    static void Misc()
    {
        var a = MakeActor("MiscActor");
        a.gameObject.AddComponent<SignalSink>();
        SignalSink.Logging = true;
        var g = PlayableGraph.Create("misc");
        var output = AnimationPlayableOutput.Create(g, "out", a);
        var active = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipActive"));
        output.SetSourcePlayable(active);
        foreach (var t in new[] { 0.0, 0.25, 0.75, 0.25 })
        {
            active.SetTime(t);
            g.Evaluate(0f);
            Log("active t=" + ProbeLog.F(t), a);
        }
        var ev = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipEvent"));
        output.SetSourcePlayable(ev);
        ev.SetTime(0);
        g.Evaluate(0f);
        for (int i = 0; i < 6; i++)
        {
            ProbeLog.Event("-- Evaluate 0.2");
            g.Evaluate(0.2f);
            Log("event t=" + ProbeLog.F(ev.GetTime()), a);
        }
        // A script playable between the output and a clip: callbacks and what passes through.
        var s = TraceBehaviour.Create(g, "S", 1);
        var ca = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipA"));
        g.Connect(ca, 0, s, 0);
        s.SetInputWeight(0, 0.5f);
        output.SetSourcePlayable(s);
        output.SetUserData(ScriptableObject.CreateInstance<UserData>());
        ca.SetTime(0.5);
        ca.Pause();
        TraceBehaviour.Outputs.Add((output, "anim"));
        g.Evaluate(0.1f);
        Log("script over clip (input weight 0.5)", a);
        SignalSink.Logging = false;
        TraceBehaviour.Quiet = true;
        g.Destroy();
        UnityEngine.Object.DestroyImmediate(a.gameObject);
    }

    static void Controller()
    {
        var a = MakeActor("ControllerActor");
        var g = PlayableGraph.Create("controller");
        var output = AnimationPlayableOutput.Create(g, "out", a);
        var controller = AnimatorControllerPlayable.Create(g, Load<RuntimeAnimatorController>("Probe"));
        output.SetSourcePlayable(controller);
        string Info()
        {
            var s = controller.GetCurrentAnimatorStateInfo(0);
            return $"A={s.IsName("A")} B={s.IsName("B")} n={ProbeLog.F(s.normalizedTime)} transition={controller.IsInTransition(0)} params={controller.GetParameterCount()} layers={controller.GetLayerCount()}";
        }
        foreach (var dt in new[] { 0f, 0.1f, 0.25f })
        {
            g.Evaluate(dt);
            Log("+" + ProbeLog.F(dt) + " " + Info(), a);
        }
        controller.SetTrigger("go");
        foreach (var dt in new[] { 0.1f, 0.1f, 0.1f, 0.5f })
        {
            g.Evaluate(dt);
            Log("go +" + ProbeLog.F(dt) + " " + Info(), a);
        }
        g.Destroy();

        // An Animator with a controller runs it through its own graph.
        var b = MakeActor("AnimatorActor");
        b.runtimeAnimatorController = Load<RuntimeAnimatorController>("Probe");
        var graph = b.playableGraph;
        ProbeLog.Event($"animator graph valid={graph.IsValid()}");
        if (graph.IsValid())
            ProbeLog.Event($"outputs={graph.GetOutputCount()} roots={graph.GetRootPlayableCount()} root={graph.GetRootPlayable(0).GetPlayableType()?.Name} playing={graph.IsPlaying()} mode={graph.GetTimeUpdateMode()}");
        b.Update(0.25f);
        Log("animator Update 0.25", b);
        UnityEngine.Object.DestroyImmediate(a.gameObject);
        UnityEngine.Object.DestroyImmediate(b.gameObject);
    }

    static void Job()
    {
        var a = MakeActor("JobActor");
        var value = a.gameObject.AddComponent<ProbeValue>();
        var other = new GameObject("Other");
        other.transform.position = new Vector3(0, 9, 0);
        var g = PlayableGraph.Create("job");
        var output = AnimationPlayableOutput.Create(g, "out", a);
        var job = new ProbeJob
        {
            root = a.BindStreamTransform(a.transform),
            child = a.BindStreamTransform(a.transform.Find("Child")),
            value = a.BindStreamProperty(a.transform, typeof(ProbeValue), "value"),
            other = a.BindSceneTransform(other.transform),
            factor = 3f,
        };
        var script = AnimationScriptPlayable.Create(g, job, 1);
        var ca = AnimationClipPlayable.Create(g, Load<AnimationClip>("ClipA"));
        g.Connect(ca, 0, script, 0);
        script.SetInputWeight(0, 1f);
        output.SetSourcePlayable(script);
        ProbeLog.Event($"processInputs={script.GetProcessInputs()} jobType={((Playable)script).GetPlayableType()?.Name}");
        foreach (var dt in new[] { 0f, 0.25f, 0.25f })
        {
            g.Evaluate(dt);
            ProbeLog.Event($"+{ProbeLog.F(dt)}: {Pose(a)} value={ProbeLog.F(value.value)}");
        }
        var data = script.GetJobData<ProbeJob>();
        data.factor = -1f;
        script.SetJobData(data);
        other.transform.position = new Vector3(0, -2, 0);
        g.Evaluate(0.25f);
        ProbeLog.Event($"factor -1: {Pose(a)} value={ProbeLog.F(value.value)}");
        g.Destroy();
        UnityEngine.Object.DestroyImmediate(a.gameObject);
        UnityEngine.Object.DestroyImmediate(other);
    }
}
