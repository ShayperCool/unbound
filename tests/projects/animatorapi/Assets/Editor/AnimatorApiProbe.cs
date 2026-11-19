using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode (see README.md): the Editor's animator controller object model built
/// through its API (and the file it writes), the same model read from an authored controller, a
/// controller built in memory playing on an Animator and edited while it plays, AnimationUtility
/// and AnimationMode answers, and the bounds of line and trail renderers.
/// </summary>
[InitializeOnLoad]
public static class AnimatorApiProbe
{
    const string Variable = "ANIMATOR_API_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<bool> s_Steps;

    static AnimatorApiProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Log) s_Logs.Add(type + ": " + message.Split('\n')[0]);
            };
            s_Steps = Steps();
            EditorApplication.update += Tick;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    /// <summary>Authors the clips and the controller the probe reads (run once in the Editor; the
    /// results are committed).</summary>
    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Clips");
        Directory.CreateDirectory("Assets/Controllers");
        AssetDatabase.Refresh();
        var clips = MakeClips();
        foreach (var clip in clips) AssetDatabase.CreateAsset(clip, "Assets/Clips/" + clip.name + ".anim");
        AssetDatabase.SaveAssets();
        Build("Assets/Controllers/Authored.controller", Clip("Walk"), Clip("Run"), Clip("Pose"), null);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    static void Tick()
    {
        bool more;
        try { more = s_Steps.MoveNext(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message); more = false; }
        if (more) return;
        EditorApplication.update -= Tick;
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        if (value is bool b) { s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(b ? "true" : "false").Append("}\n"); return; }
        var text = value is float f ? F(f) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(text)).Append("\"}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");

    /// <summary>The logs since the last call (warnings, errors, exceptions), under a key.</summary>
    static void Logs(string key)
    {
        Put(key + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    /// <summary>Runs an action, recording what it threw.</summary>
    static void Try(string key, Action action)
    {
        try { action(); }
        catch (Exception e) { Put(key + ".threw", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
    }

    static string F(float v) => float.IsNaN(v) ? "NaN" : float.IsPositiveInfinity(v) ? "Infinity" : float.IsNegativeInfinity(v) ? "-Infinity"
        : Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";
    static string V(Vector2 v) => "(" + F(v.x) + "," + F(v.y) + ")";
    static string N(Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Clips/" + name + ".anim");

    // ------------------------------------------------------------------ clips (Setup)

    static AnimationCurve Line(float t0, float v0, float t1, float v1) => AnimationCurve.Linear(t0, v0, t1, v1);

    static List<AnimationClip> MakeClips()
    {
        var clips = new List<AnimationClip>();
        var walk = new AnimationClip { name = "Walk", frameRate = 30 };
        walk.SetCurve("Child", typeof(Transform), "m_LocalPosition.x", Line(0, 0, 1, 1));
        clips.Add(walk);

        var run = new AnimationClip { name = "Run", frameRate = 30 };
        run.SetCurve("Child", typeof(Transform), "m_LocalPosition.x", Line(0, 0, 2, 4));
        run.SetCurve("Child", typeof(Transform), "m_LocalPosition.y", Line(0, 0.5f, 2, 0.5f));
        run.SetCurve("Child", typeof(Transform), "m_LocalPosition.z", Line(0, 0, 2, -1));
        var q1 = Quaternion.Euler(0, 90, 0);
        run.SetCurve("Child", typeof(Transform), "m_LocalRotation.x", Line(0, 0, 2, q1.x));
        run.SetCurve("Child", typeof(Transform), "m_LocalRotation.y", Line(0, 0, 2, q1.y));
        run.SetCurve("Child", typeof(Transform), "m_LocalRotation.z", Line(0, 0, 2, q1.z));
        run.SetCurve("Child", typeof(Transform), "m_LocalRotation.w", Line(0, 1, 2, q1.w));
        clips.Add(run);

        var pose = new AnimationClip { name = "Pose", frameRate = 30 };
        pose.SetCurve("Child", typeof(Transform), "m_LocalScale.x", new AnimationCurve(new Keyframe(0, 2)));
        pose.SetCurve("Child", typeof(Transform), "m_LocalScale.y", new AnimationCurve(new Keyframe(0, 2)));
        pose.SetCurve("Child", typeof(Transform), "m_LocalScale.z", new AnimationCurve(new Keyframe(0, 2)));
        clips.Add(pose);

        var mixed = new AnimationClip { name = "Mixed", frameRate = 60 };
        AnimationUtility.SetEditorCurve(mixed, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "localEulerAnglesRaw.y"), Line(0, 0, 1.5f, 180));
        AnimationUtility.SetEditorCurve(mixed, EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "amount"), Line(0, 1, 1.5f, 3));
        AnimationUtility.SetEditorCurve(mixed, EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "offset.x"), Line(0, 5, 1.5f, 5));
        AnimationUtility.SetEditorCurve(mixed, EditorCurveBinding.FloatCurve("Child2", typeof(GameObject), "m_IsActive"),
            new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.5f, 0), new Keyframe(1, 1)));
        AnimationUtility.SetObjectReferenceCurve(mixed, EditorCurveBinding.PPtrCurve("", typeof(ProbeTarget), "reference"), new[]
        {
            new ObjectReferenceKeyframe { time = 0, value = walk },
            new ObjectReferenceKeyframe { time = 1, value = run },
        });
        clips.Add(mixed);

        var root = new AnimationClip { name = "Root", frameRate = 30 };
        root.SetCurve("", typeof(Animator), "RootT.x", Line(0, 0, 1, 0));
        root.SetCurve("", typeof(Animator), "RootT.y", Line(0, 1, 1, 1));
        root.SetCurve("", typeof(Animator), "RootT.z", Line(0, 0, 1, 2));
        root.SetCurve("", typeof(Animator), "RootQ.x", Line(0, 0, 1, 0));
        root.SetCurve("", typeof(Animator), "RootQ.y", Line(0, 0, 1, 0));
        root.SetCurve("", typeof(Animator), "RootQ.z", Line(0, 0, 1, 0));
        root.SetCurve("", typeof(Animator), "RootQ.w", Line(0, 1, 1, 1));
        clips.Add(root);

        clips.Add(new AnimationClip { name = "Empty" });
        return clips;
    }

    // ------------------------------------------------------------------ the controller built through the API

    /// <summary>Builds the probe's controller at <paramref name="path"/> (in memory when null),
    /// recording what each step answers under <paramref name="prefix"/> (nothing when null).</summary>
    static AnimatorController Build(string path, AnimationClip walk, AnimationClip run, AnimationClip pose, string prefix)
    {
        void P(string key, object value) { if (prefix != null) Put(prefix + "." + key, value); }
        void L(string key) { if (prefix != null) Logs(prefix + "." + key); else s_Logs.Clear(); }

        AnimatorController c;
        if (path != null) c = AnimatorController.CreateAnimatorControllerAtPath(path);
        else
        {
            c = new AnimatorController { name = "Memory" };
            c.AddLayer("Base Layer");
        }
        P("created.name", c.name);
        P("created.hideFlags", c.hideFlags);
        P("created.path", AssetDatabase.GetAssetPath(c));
        P("created.layers", Layers(c));
        P("created.sm", SmHeader(c.layers[0].stateMachine, c));
        L("created");

        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Go", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Count", AnimatorControllerParameterType.Int);
        c.AddParameter("On", AnimatorControllerParameterType.Bool);
        c.AddParameter(new AnimatorControllerParameter { name = "Raw", type = AnimatorControllerParameterType.Float, defaultFloat = 2.5f });
        c.AddParameter(new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Int, defaultInt = 4 });
        P("params.added", Params(c));
        P("params.uniqueSpeed", c.MakeUniqueParameterName("Speed"));
        P("params.uniqueFresh", c.MakeUniqueParameterName("Fresh"));
        P("params.uniqueEmpty", c.MakeUniqueParameterName(""));
        P("layers.uniqueBase", c.MakeUniqueLayerName("Base Layer"));
        P("layers.uniqueOther", c.MakeUniqueLayerName("Other"));
        c.RemoveParameter(c.parameters.Length - 1);
        P("params.removedLast", Params(c));
        c.RemoveParameter(new AnimatorControllerParameter { name = "Raw", type = AnimatorControllerParameterType.Float, defaultFloat = 2.5f });
        P("params.removedRaw", Params(c));
        c.RemoveParameter(new AnimatorControllerParameter { name = "Count", type = AnimatorControllerParameterType.Float });
        P("params.removedCountWrongType", Params(c));
        L("params");

        var sm = c.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        P("states.idle", StateHeader(idle, sm) + " default=" + N(sm.defaultState));
        var walkState = c.AddMotion(walk);
        P("states.walk", StateHeader(walkState, sm) + " default=" + N(sm.defaultState));
        var dup = sm.AddState("Idle");
        P("states.dup", StateHeader(dup, sm));
        var runState = sm.AddState("Run", new Vector3(500, 100, 0));
        runState.motion = run;
        sm.AddState(runState, Vector3.zero);
        L("states.addExisting");
        P("states.uniqueIdle", sm.MakeUniqueStateName("Idle"));
        P("states.uniqueFresh", sm.MakeUniqueStateName("Fresh"));
        var sub = sm.AddStateMachine("Sub");
        var sub2 = sm.AddStateMachine("Sub");
        P("machines.sub", SmHeader(sub, c) + "@" + V(Position(sm, sub)));
        P("machines.sub2", SmHeader(sub2, c) + "@" + V(Position(sm, sub2)));
        sm.AddStateMachine(sub, Vector3.one);
        L("machines.addExisting");
        P("machines.uniqueSub", sm.MakeUniqueStateMachineName("Sub"));
        P("machines.uniqueIdle", sm.MakeUniqueStateMachineName("Idle"));
        var inner = sub.AddState("Inner");
        var innerIdle = sub.AddState("Idle");
        P("states.inner", StateHeader(inner, sub));
        P("states.innerIdle", StateHeader(innerIdle, sub));
        P("states.uniqueInSub", sub.MakeUniqueStateName("Inner") + "|" + sm.MakeUniqueStateName("Inner"));
        L("states");

        var t1 = idle.AddTransition(walkState);
        P("transitions.plain", Transition(t1, idle));
        t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        t1.AddCondition(AnimatorConditionMode.If, 0, "Go");
        t1.AddCondition(AnimatorConditionMode.Equals, 3, "Count");
        t1.RemoveCondition(t1.conditions[1]);
        P("transitions.conditions", Conditions(t1));
        t1.RemoveCondition(new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = "Count", threshold = 3 });
        P("transitions.conditionsByValue", Conditions(t1));
        t1.RemoveCondition(new AnimatorCondition { mode = AnimatorConditionMode.Less, parameter = "Speed", threshold = 0.1f });
        P("transitions.conditionsAbsent", Conditions(t1));
        P("transitions.exitWalk", Transition(walkState.AddTransition(runState, true), walkState));
        P("transitions.exitRun", Transition(runState.AddTransition(idle, true), runState));
        P("transitions.toMachine", Transition(idle.AddTransition(sub), idle));
        P("transitions.toMachineExit", Transition(idle.AddTransition(sub2, true), idle));
        P("transitions.noMotion", Transition(dup.AddTransition(idle, true), dup));
        P("transitions.exit", Transition(runState.AddExitTransition(), runState));
        P("transitions.exitTimed", Transition(walkState.AddExitTransition(true), walkState));
        P("transitions.innerExit", Transition(inner.AddExitTransition(true), inner));
        var any1 = sm.AddAnyStateTransition(runState);
        any1.AddCondition(AnimatorConditionMode.Less, -1f, "Speed");
        P("transitions.any", Transition(any1, sm));
        var any2 = sm.AddAnyStateTransition(sub);
        P("transitions.anyMachine", Transition(any2, sm));
        var entry1 = sm.AddEntryTransition(walkState);
        entry1.AddCondition(AnimatorConditionMode.If, 0, "On");
        P("transitions.entry", Transition(entry1, sm));
        var entry2 = sm.AddEntryTransition(sub);
        P("transitions.entryMachine", Transition(entry2, sm));
        P("transitions.subEntry", Transition(sub.AddEntryTransition(inner), sub));
        var m1 = sm.AddStateMachineTransition(sub);
        P("transitions.machine", Transition(m1, sub));
        P("transitions.machineToMachine", Transition(sm.AddStateMachineTransition(sub, sub2), sub));
        P("transitions.machineToState", Transition(sm.AddStateMachineTransition(sub, idle), sub));
        P("transitions.machineExit", Transition(sm.AddStateMachineExitTransition(sub), sub));
        P("transitions.ofSub", string.Join(";", sm.GetStateMachineTransitions(sub).Select(t => Transition(t, sub))));
        P("transitions.ofSub2", sm.GetStateMachineTransitions(sub2).Length);
        P("transitions.ofSubInSub", sub.GetStateMachineTransitions(sub).Length);
        var removed = idle.AddTransition(runState);
        idle.RemoveTransition(removed);
        P("transitions.removed", (removed == null) + " " + idle.transitions.Length);
        P("transitions.removeAny", sm.RemoveAnyStateTransition(any2) + " " + sm.RemoveAnyStateTransition(any2) + " " + sm.anyStateTransitions.Length);
        P("transitions.removeEntry", sm.RemoveEntryTransition(entry2) + " " + sm.RemoveEntryTransition(entry2) + " " + sm.entryTransitions.Length);
        P("transitions.removeMachine", sm.RemoveStateMachineTransition(sub, m1) + " " + sm.GetStateMachineTransitions(sub).Length + " removed=" + (m1 == null));
        Try(prefix + ".transitions.removeMachineAgain", () => P("transitions.removeMachineAgain", sm.RemoveStateMachineTransition(sub, m1) + " " + sm.GetStateMachineTransitions(sub).Length));
        var kept = sm.AddStateMachineTransition(sub, sub2);
        sm.RemoveStateMachineTransition(sub, kept);
        P("transitions.removeMachineValid", sm.GetStateMachineTransitions(sub).Length + " removed=" + (kept == null));
        P("transitions.names", t1.GetDisplayName(idle) + "|" + any1.GetDisplayName(sm) + "|" + entry1.GetDisplayName(sm) + "|" + sm.GetStateMachineTransitions(sub)[0].GetDisplayName(sub) + "|" + runState.transitions[1].GetDisplayName(runState));
        L("transitions");

        var moveState = c.CreateBlendTreeInController("Move", out var tree);
        P("trees.state", StateHeader(moveState, sm) + " motion=" + N(moveState.motion));
        P("trees.created", Tree(tree));
        tree.AddChild(walk);
        P("trees.oneChild", Tree(tree));
        tree.AddChild(run);
        P("trees.twoChildren", Tree(tree));
        tree.AddChild(pose, 0.8f);
        P("trees.threeChildren", Tree(tree));
        var nested = tree.CreateBlendTreeChild(0.5f);
        P("trees.nested", Tree(nested) + " in " + Tree(tree));
        nested.blendType = BlendTreeType.SimpleDirectional2D;
        nested.AddChild(walk, new Vector2(1, 0));
        nested.AddChild(run, new Vector2(0, 1));
        P("trees.nested2D", Tree(nested));
        tree.useAutomaticThresholds = false;
        tree.AddChild(walk, 3f);
        P("trees.manual", Tree(tree));
        tree.RemoveChild(2);
        P("trees.removed", Tree(tree));
        tree.useAutomaticThresholds = true;
        P("trees.automaticAgain", Tree(tree));
        tree.minThreshold = -1;
        tree.maxThreshold = 4;
        P("trees.range", Tree(tree));
        var directState = c.CreateBlendTreeInController("Direct", out var direct);
        direct.blendType = BlendTreeType.Direct;
        direct.AddChild(walk);
        direct.AddChild(run);
        var children = direct.children;
        children[1].directBlendParameter = "Speed";
        children[1].timeScale = 2;
        children[1].cycleOffset = 0.25f;
        children[1].mirror = true;
        direct.children = children;
        P("trees.direct", StateHeader(directState, sm) + " " + Tree(direct));
        L("trees");

        c.AddLayer("Upper");
        c.AddLayer("Upper");
        c.AddLayer(new AnimatorControllerLayer { name = "Synced", syncedLayerIndex = 0, defaultWeight = 0.5f, blendingMode = AnimatorLayerBlendingMode.Additive });
        P("layers.added", Layers(c));
        c.SetStateEffectiveMotion(walkState, run, 3);
        P("layers.effective", N(c.GetStateEffectiveMotion(walkState, 3)) + " " + N(c.GetStateEffectiveMotion(walkState, 0)) + " " + N(c.GetStateEffectiveMotion(walkState))
            + " " + N(c.layers[3].GetOverrideMotion(walkState)) + " " + N(c.layers[3].GetOverrideMotion(idle)) + " " + N(walkState.motion));
        c.SetStateEffectiveMotion(idle, pose, 0);
        P("layers.effectiveBase", N(idle.motion));
        L("layers");

        var b1 = idle.AddStateMachineBehaviour<ProbeBehaviour>();
        var b2 = sm.AddStateMachineBehaviour<ProbeBehaviour>();
        var b3 = inner.AddStateMachineBehaviour(typeof(OtherBehaviour));
        var b4 = c.AddEffectiveStateMachineBehaviour<ProbeBehaviour>(walkState, 3);
        var b5 = c.AddEffectiveStateMachineBehaviour<OtherBehaviour>(runState, 0);
        P("behaviours.made", string.Join(";", new StateMachineBehaviour[] { b1, b2, b3, b4, b5 }.Select(Behaviour)));
        if (prefix != null)
        {
            s_Known.Clear();
            s_Known[b1] = "b1:idle";
            s_Known[b2] = "b2:machine";
            s_Known[b3] = "b3:inner";
            s_Known[b4] = "b4:synced";
            s_Known[b5] = "b5:run";
        }
        L("behaviours.add");
        P("behaviours.idle", Behaviours(idle.behaviours));
        P("behaviours.machine", Behaviours(sm.behaviours));
        P("behaviours.run", Behaviours(runState.behaviours));
        P("behaviours.walk", Behaviours(walkState.behaviours));
        L("behaviours.lists");
        P("behaviours.effective", Behaviours(c.GetStateEffectiveBehaviours(walkState, 3)) + " / " + Behaviours(c.GetStateEffectiveBehaviours(runState, 0))
            + " / " + Behaviours(c.GetStateEffectiveBehaviours(idle, 3)));
        L("behaviours.effectiveCall");
        Try(prefix + ".behaviours.override", () => P("behaviours.override", Behaviours(c.layers[3].GetOverrideBehaviours(walkState)) + " / " + Behaviours(c.layers[3].GetOverrideBehaviours(idle))));
        L("behaviours.overrideCall");
        P("behaviours.ofType", Behaviours(c.GetBehaviours<ProbeBehaviour>()) + " / " + Behaviours(c.GetBehaviours<OtherBehaviour>()) + " / " + Behaviours(c.GetBehaviours<StateMachineBehaviour>()));
        L("behaviours.ofTypeCall");
        foreach (var (name, b) in new[] { ("state", b1), ("machine", b2), ("inner", b3), ("synced", b4) })
            P("behaviours.context." + name, string.Join(";", AnimatorController.FindStateMachineBehaviourContext(b)
                .Select(x => N(x.animatorController) + "/" + N(x.animatorObject) + "/" + x.layerIndex)));
        L("behaviours.contextCall");
        P("behaviours.clips", string.Join(",", c.animationClips.Select(x => x.name)));
        L("behaviours");

        var temp = sm.AddState("Temp");
        idle.AddTransition(temp);
        sm.AddAnyStateTransition(temp);
        sm.AddEntryTransition(temp);
        inner.AddTransition(temp);
        sm.AddStateMachineTransition(sub, temp);
        P("remove.before", Counts(sm, idle, inner, sub));
        sm.RemoveState(temp);
        P("remove.state", Counts(sm, idle, inner, sub) + " temp=" + (temp == null));
        sm.RemoveStateMachine(sub2);
        P("remove.machine", Counts(sm, idle, inner, sub) + " sub2=" + (sub2 == null) + " idleTo=" + string.Join(",", idle.transitions.Select(t => N(t.destinationState) + "/" + N(t.destinationStateMachine))));
        c.RemoveLayer(1);
        P("remove.layer", Layers(c));
        P("remove.behaviours", Behaviours(c.GetBehaviours<StateMachineBehaviour>()) + " synced=" + string.Join(";", AnimatorController.FindStateMachineBehaviourContext(b4)
            .Select(x => N(x.animatorController) + "/" + N(x.animatorObject) + "/" + x.layerIndex)) + " effective=" + Behaviours(c.GetStateEffectiveBehaviours(walkState, 2)));
        L("remove");
        return c;
    }

    static Vector3 Position(AnimatorStateMachine parent, AnimatorStateMachine child)
    {
        foreach (var m in parent.stateMachines)
            if (m.stateMachine == child) return m.position;
        return new Vector3(-1, -1, -1);
    }

    static string Counts(AnimatorStateMachine sm, AnimatorState idle, AnimatorState inner, AnimatorStateMachine sub) =>
        $"states={sm.states.Length} any={sm.anyStateTransitions.Length} entry={sm.entryTransitions.Length} idle={idle.transitions.Length} inner={inner.transitions.Length} sub={sm.GetStateMachineTransitions(sub).Length} machines={sm.stateMachines.Length}";

    static string Params(AnimatorController c) => string.Join(";", c.parameters.Select(p =>
        $"{p.name}:{p.type}:{F(p.defaultFloat)}/{p.defaultInt}/{p.defaultBool}:{p.nameHash}"));

    static string Layers(AnimatorController c) => string.Join(";", c.layers.Select(l =>
        $"{l.name}:sm={N(l.stateMachine)}:mask={N(l.avatarMask)}:{l.blendingMode}:{l.syncedLayerIndex}:{F(l.defaultWeight)}:{l.iKPass}:{l.syncedLayerAffectsTiming}"));

    static string SmHeader(AnimatorStateMachine sm, AnimatorController c) => sm == null ? "null" :
        $"{sm.name} flags={sm.hideFlags} sameFile={AssetDatabase.GetAssetPath(sm) == AssetDatabase.GetAssetPath(c)} sub={AssetDatabase.IsSubAsset(sm)}";

    static string StateHeader(AnimatorState s, AnimatorStateMachine sm)
    {
        var pos = new Vector3(-1, -1, -1);
        foreach (var child in sm.states)
            if (child.state == s) pos = child.position;
        return $"{s.name}@{V(pos)} flags={s.hideFlags} hash={s.nameHash} path={AssetDatabase.GetAssetPath(s)}";
    }

    static string Conditions(AnimatorTransitionBase t) => string.Join(",", t.conditions.Select(x => $"{x.mode}:{x.parameter}:{F(x.threshold)}"));

    static string Transition(AnimatorTransitionBase t, Object source)
    {
        var sb = new StringBuilder();
        sb.Append(t.GetType().Name).Append(" '").Append(t.name).Append("' flags=").Append(t.hideFlags)
            .Append(" path=").Append(AssetDatabase.GetAssetPath(t))
            .Append(" solo=").Append(t.solo).Append(" mute=").Append(t.mute).Append(" exit=").Append(t.isExit)
            .Append(" to=").Append(N(t.destinationState)).Append('/').Append(N(t.destinationStateMachine))
            .Append(" cond=[").Append(Conditions(t)).Append(']');
        if (t is AnimatorStateTransition s)
            sb.Append(" dur=").Append(F(s.duration)).Append(" off=").Append(F(s.offset)).Append(" int=").Append(s.interruptionSource)
                .Append(" ordered=").Append(s.orderedInterruption).Append(" exitTime=").Append(F(s.exitTime)).Append(" hasExit=").Append(s.hasExitTime)
                .Append(" fixed=").Append(s.hasFixedDuration).Append(" self=").Append(s.canTransitionToSelf);
        sb.Append(" display='").Append(t.GetDisplayName(source)).Append('\'');
        return sb.ToString();
    }

    static string Tree(BlendTree t) => t == null ? "null" :
        $"{t.name} flags={t.hideFlags} path={AssetDatabase.GetAssetPath(t)} type={t.blendType} p={t.blendParameter} py={t.blendParameterY} auto={t.useAutomaticThresholds} min={F(t.minThreshold)} max={F(t.maxThreshold)} dur={F(t.averageDuration)} ["
        + string.Join(",", t.children.Select(ch => $"{N(ch.motion)}:{F(ch.threshold)}:{V(ch.position)}:{F(ch.timeScale)}:{F(ch.cycleOffset)}:{ch.directBlendParameter}:{ch.mirror}")) + "]";

    static string Behaviour(StateMachineBehaviour b) => b == null ? "null" :
        $"{b.GetType().Name} '{b.name}' flags={b.hideFlags} path={AssetDatabase.GetAssetPath(b)} sub={AssetDatabase.IsSubAsset(b)}";

    /// <summary>Names the probe gives objects it made, to tell instances apart in answers.</summary>
    static readonly Dictionary<Object, string> s_Known = new Dictionary<Object, string>();

    static string Behaviours(StateMachineBehaviour[] list) => list == null ? "null" : "[" + string.Join(",", list.Select(b => b == null ? "null" : s_Known.TryGetValue(b, out var k) ? k : b.GetType().Name)) + "]";

    // ------------------------------------------------------------------ a model dump

    /// <summary>Every object of a controller, labelled by where it is in the model.</summary>
    sealed class Model
    {
        internal readonly Dictionary<Object, string> Labels = new Dictionary<Object, string>();
        internal readonly List<(string Key, string Value)> Rows = new List<(string, string)>();

        internal string Label(Object o, string label)
        {
            if (o == null) return "null";
            if (Labels.TryGetValue(o, out var known)) return known;
            Labels[o] = label;
            return label;
        }

        internal string Ref(Object o) => o == null ? "null" : Labels.TryGetValue(o, out var l) ? l : N(o);
    }

    static Model Walk(AnimatorController c)
    {
        var m = new Model();
        m.Label(c, "C");
        var layers = c.layers;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].stateMachine != null) LabelMachine(m, layers[i].stateMachine, "SM:" + layers[i].name);
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].stateMachine != null) LabelBehaviours(m, c, layers[i].stateMachine);
        m.Rows.Add(("controller", $"{c.name} flags={c.hideFlags} clips=[{string.Join(",", c.animationClips.Select(x => x.name))}]"));
        m.Rows.Add(("parameters", Params(c)));
        for (int i = 0; i < layers.Length; i++)
        {
            var l = layers[i];
            m.Rows.Add(($"layer{i}", $"{l.name}:sm={m.Ref(l.stateMachine)}:mask={N(l.avatarMask)}:{l.blendingMode}:{l.syncedLayerIndex}:{F(l.defaultWeight)}:{l.iKPass}:{l.syncedLayerAffectsTiming}"));
            if (l.syncedLayerIndex >= 0 && layers[l.syncedLayerIndex].stateMachine != null)
                foreach (var s in States(layers[l.syncedLayerIndex].stateMachine))
                {
                    var motion = l.GetOverrideMotion(s);
                    var behaviours = c.GetStateEffectiveBehaviours(s, i);
                    if (motion != null || behaviours != null && behaviours.Length > 0)
                        m.Rows.Add(($"layer{i}.{m.Ref(s)}", $"motion={m.Ref(motion)} behaviours=[{string.Join(",", (behaviours ?? new StateMachineBehaviour[0]).Select(m.Ref))}]"));
                }
        }
        for (int i = 0; i < layers.Length; i++)
            if (layers[i].stateMachine != null) DumpMachine(m, layers[i].stateMachine);
        return m;
    }

    static IEnumerable<AnimatorState> States(AnimatorStateMachine sm)
    {
        foreach (var s in sm.states) yield return s.state;
        foreach (var child in sm.stateMachines)
            foreach (var s in States(child.stateMachine)) yield return s;
    }

    static void LabelMachine(Model m, AnimatorStateMachine sm, string label)
    {
        label = m.Label(sm, label);
        foreach (var s in sm.states)
        {
            var sl = m.Label(s.state, "S:" + label.Substring(3) + "/" + s.state.name);
            if (s.state.motion is BlendTree tree) LabelTree(m, tree, "BT:" + sl.Substring(2));
            var transitions = s.state.transitions;
            for (int i = 0; i < transitions.Length; i++) m.Label(transitions[i], "T:" + sl.Substring(2) + "#" + i);
        }
        var any = sm.anyStateTransitions;
        for (int i = 0; i < any.Length; i++) m.Label(any[i], "AT:" + label.Substring(3) + "#" + i);
        var entry = sm.entryTransitions;
        for (int i = 0; i < entry.Length; i++) m.Label(entry[i], "ET:" + label.Substring(3) + "#" + i);
        foreach (var child in sm.stateMachines)
        {
            LabelMachine(m, child.stateMachine, label + "/" + child.stateMachine.name);
            var own = sm.GetStateMachineTransitions(child.stateMachine);
            for (int i = 0; i < own.Length; i++) m.Label(own[i], "MT:" + label.Substring(3) + ":" + child.stateMachine.name + "#" + i);
        }
    }

    static void LabelTree(Model m, BlendTree tree, string label)
    {
        label = m.Label(tree, label);
        var children = tree.children;
        for (int i = 0; i < children.Length; i++)
            if (children[i].motion is BlendTree child) LabelTree(m, child, label + "#" + i);
    }

    static void LabelBehaviours(Model m, AnimatorController c, AnimatorStateMachine sm)
    {
        var own = sm.behaviours;
        for (int i = 0; i < own.Length; i++) m.Label(own[i], "B:" + m.Ref(sm) + "#" + i);
        foreach (var s in sm.states)
        {
            var list = s.state.behaviours;
            for (int i = 0; i < list.Length; i++) m.Label(list[i], "B:" + m.Ref(s.state) + "#" + i);
            var layers = c.layers;
            for (int l = 0; l < layers.Length; l++)
            {
                if (layers[l].syncedLayerIndex < 0) continue;
                var effective = c.GetStateEffectiveBehaviours(s.state, l) ?? new StateMachineBehaviour[0];
                for (int i = 0; i < effective.Length; i++) m.Label(effective[i], "B:" + m.Ref(s.state) + "@" + l + "#" + i);
            }
        }
        foreach (var child in sm.stateMachines) LabelBehaviours(m, c, child.stateMachine);
    }

    static void DumpMachine(Model m, AnimatorStateMachine sm)
    {
        var l = m.Ref(sm);
        m.Rows.Add((l, $"{sm.name} flags={sm.hideFlags} default={m.Ref(sm.defaultState)} any={V(sm.anyStatePosition)} entry={V(sm.entryPosition)} exit={V(sm.exitPosition)} parent={V(sm.parentStateMachinePosition)}"
            + $" states=[{string.Join(",", sm.states.Select(s => m.Ref(s.state) + "@" + V(s.position)))}]"
            + $" machines=[{string.Join(",", sm.stateMachines.Select(s => m.Ref(s.stateMachine) + "@" + V(s.position)))}]"
            + $" behaviours=[{string.Join(",", sm.behaviours.Select(m.Ref))}]"));
        foreach (var t in sm.anyStateTransitions) DumpTransition(m, t, sm);
        foreach (var t in sm.entryTransitions) DumpTransition(m, t, sm);
        foreach (var child in sm.stateMachines)
            foreach (var t in sm.GetStateMachineTransitions(child.stateMachine)) DumpTransition(m, t, child.stateMachine);
        foreach (var b in sm.behaviours) DumpBehaviour(m, b);
        foreach (var s in sm.states)
        {
            var st = s.state;
            m.Rows.Add((m.Ref(st), $"{st.name} flags={st.hideFlags} hash={st.nameHash} motion={m.Ref(st.motion)} speed={F(st.speed)} cycle={F(st.cycleOffset)} mirror={st.mirror} ik={st.iKOnFeet} wd={st.writeDefaultValues} tag='{st.tag}'"
                + $" sp='{st.speedParameter}':{st.speedParameterActive} cp='{st.cycleOffsetParameter}':{st.cycleOffsetParameterActive} mp='{st.mirrorParameter}':{st.mirrorParameterActive} tp='{st.timeParameter}':{st.timeParameterActive}"
                + $" transitions=[{string.Join(",", st.transitions.Select(m.Ref))}] behaviours=[{string.Join(",", st.behaviours.Select(m.Ref))}]"));
            foreach (var t in st.transitions) DumpTransition(m, t, st);
            foreach (var b in st.behaviours) DumpBehaviour(m, b);
            if (st.motion is BlendTree tree) DumpTree(m, tree);
        }
        foreach (var child in sm.stateMachines) DumpMachine(m, child.stateMachine);
    }

    static void DumpTransition(Model m, AnimatorTransitionBase t, Object source)
    {
        var text = Transition(t, source);
        text = Regex.Replace(text, @" to=\S+", " to=" + m.Ref(t.destinationState) + "/" + m.Ref(t.destinationStateMachine));
        m.Rows.Add((m.Ref(t), text));
    }

    static void DumpBehaviour(Model m, StateMachineBehaviour b)
    {
        var fields = b is ProbeBehaviour p ? $"weight={F(p.weight)} label={p.label}" : b is OtherBehaviour o ? $"count={o.count}" : "";
        m.Rows.Add((m.Ref(b), Behaviour(b) + " " + fields));
    }

    static void DumpTree(Model m, BlendTree t)
    {
        m.Rows.Add((m.Ref(t), Tree(t)));
        foreach (var child in t.children)
            if (child.motion is BlendTree tree) DumpTree(m, tree);
    }

    static void PutModel(string prefix, Model m)
    {
        foreach (var (key, value) in m.Rows) Put(prefix + "." + key, value);
    }

    /// <summary>A controller file's documents with their file ids replaced by the model's labels,
    /// one row per document, by label.</summary>
    static void PutFile(string prefix, string path, Model m)
    {
        if (!File.Exists(path))
        {
            Put(prefix + ".missing", path);
            return;
        }
        var ids = new Dictionary<string, string>();
        foreach (var pair in m.Labels)
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pair.Key, out _, out long id)) ids[id.ToString(CultureInfo.InvariantCulture)] = pair.Value;
        var text = File.ReadAllText(path);
        var docs = Regex.Split(text, @"(?m)^(?=--- )");
        Put(prefix + ".preamble", docs[0]);
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
        int unknown = 0;
        foreach (var doc in docs.Skip(1))
        {
            var header = Regex.Match(doc, @"^--- !u!(\d+) &(-?\d+)");
            var label = ids.TryGetValue(header.Groups[2].Value, out var known) ? known : "?" + unknown++;
            var body = Regex.Replace(doc.Substring(header.Length), @"fileID: (-?\d+)", x => ids.TryGetValue(x.Groups[1].Value, out var l) ? "fileID: <" + l + ">" : x.Value);
            rows[label] = "!u!" + header.Groups[1].Value + body;
        }
        foreach (var pair in rows) Put(prefix + "." + pair.Key, pair.Value);
        Put(prefix + ".count", rows.Count);
        Put(prefix + ".main", AssetDatabase.TryGetGUIDAndLocalFileIdentifier(AssetDatabase.LoadMainAssetAtPath(path), out _, out long main) ? main : -1);
        var meta = path + ".meta";
        Put(prefix + ".meta", File.Exists(meta) ? Regex.Replace(File.ReadAllText(meta), @"guid: [0-9a-f]{32}", "guid: <guid>") : "missing");
    }

    // ------------------------------------------------------------------ steps

    static IEnumerator<bool> Steps()
    {
        Logs("start");
        Try("model", ModelSteps);
        yield return true;
        Try("read", Read);
        yield return true;
        Try("clip", ClipSteps);
        yield return true;
        foreach (var _ in Play()) yield return true;
        Try("util", Utility);
        yield return true;
        foreach (var _ in Streams()) yield return true;
        Try("mode", Mode);
        yield return true;
        foreach (var _ in Bounds()) yield return true;
        Try("extra", Extra);
        Logs("end");
    }

    static void ModelSteps()
    {
        var path = "Assets/Built.controller";
        var c = Build(path, Clip("Walk"), Clip("Run"), Clip("Pose"), "model");
        AssetDatabase.SaveAssets();
        Logs("model.save");
        var m = Walk(c);
        PutModel("model.dump", m);
        PutFile("model.file", path, m);

        var memory = Build(null, Clip("Walk"), Clip("Run"), Clip("Pose"), "memory");
        PutModel("memory.dump", Walk(memory));

        var plain = AnimatorController.CreateAnimatorControllerAtPath("Assets/Plain.controller");
        plain.CreateBlendTreeInController("Tree", out var plainTree);
        Put("plain.params", Params(plain));
        Put("plain.tree", Tree(plainTree));
        plain.AddLayer("Second");
        plain.CreateBlendTreeInController("Tree", out var second, 1);
        Put("plain.second", Tree(second) + " " + StateHeader(plain.layers[1].stateMachine.states[0].state, plain.layers[1].stateMachine));
        var withClip = AnimatorController.CreateAnimatorControllerAtPathWithClip("Assets/WithClip.controller", Clip("Run"));
        Put("withClip.dump", string.Join("\n", Walk(withClip).Rows.Select(r => r.Key + "=" + r.Value)));
        Try("plain.save", AssetDatabase.SaveAssets);
        PutFile("plain.file", "Assets/Plain.controller", Walk(plain));
        PutFile("withClip.file", "Assets/WithClip.controller", Walk(withClip));
        Logs("plain");

        var made = AnimatorController.AllocateAnimatorClip("Made");
        var settings = AnimationUtility.GetAnimationClipSettings(made);
        Put("allocate", $"{made.name} flags={made.hideFlags} fps={F(made.frameRate)} legacy={made.legacy} wrap={made.wrapMode} length={F(made.length)} empty={made.empty} loop={settings.loopTime} path='{AssetDatabase.GetAssetPath(made)}'");
        var rig = new GameObject("SetRig");
        var animator = rig.AddComponent<Animator>();
        AnimatorController.SetAnimatorController(animator, c);
        Put("setController", N(animator.runtimeAnimatorController) + " " + (animator.runtimeAnimatorController == c));
        AnimatorController.SetAnimatorController(animator, null);
        Put("setControllerNull", N(animator.runtimeAnimatorController));
        Object.Destroy(rig);
        Logs("allocate");
    }

    static void Read()
    {
        var path = "Assets/Controllers/Authored.controller";
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        Put("read.type", N(c));
        var m = Walk(c);
        PutModel("read.dump", m);
        PutFile("read.file", path, m);
        Logs("read");
    }

    static void ClipSteps()
    {
        foreach (var name in new[] { "Walk", "Run", "Pose", "Mixed", "Root", "Empty" })
        {
            var clip = Clip(name);
            Put("clip." + name + ".motion", $"length={F(clip.length)} dur={F(clip.averageDuration)} speed={V(clip.averageSpeed)} angular={F(clip.averageAngularSpeed)} apparent={F(clip.apparentSpeed)} loop={clip.isLooping} human={clip.isHumanMotion} root={clip.hasRootCurves}/{clip.hasMotionCurves}/{clip.hasGenericRootTransform}/{clip.hasMotionFloatCurves}");
            Try("clip." + name + ".stats", () =>
            {
                var method = typeof(AnimationUtility).GetMethod("GetAnimationClipStats", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var stats = method.Invoke(null, new object[] { clip });
                Put("clip." + name + ".stats", string.Join(" ", stats.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name + "=" + f.GetValue(stats))));
            });
            Put("clip." + name + ".bindings", string.Join(";", AnimationUtility.GetCurveBindings(clip).Select(Binding)));
            Put("clip." + name + ".objectBindings", string.Join(";", AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(b =>
                Binding(b) + "=" + string.Join(",", AnimationUtility.GetObjectReferenceCurve(clip, b).Select(k => F(k.time) + ":" + N(k.value))))));
        }
        Logs("clip");
    }

    static string Binding(EditorCurveBinding b) => $"{b.path}|{b.type?.Name}|{b.propertyName}|{b.isPPtrCurve}|{b.isDiscreteCurve}";

    static GameObject Rig(string name, AnimatorController controller)
    {
        var rig = new GameObject(name);
        var child = new GameObject("Child");
        child.transform.SetParent(rig.transform, false);
        var child2 = new GameObject("Child2");
        child2.transform.SetParent(rig.transform, false);
        rig.AddComponent<ProbeTarget>();
        var animator = rig.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        return rig;
    }

    static string Name(Dictionary<int, string> names, int hash) => names.TryGetValue(hash, out var n) ? n : hash.ToString();

    static IEnumerable<bool> Play()
    {
        // A controller made in memory: A (Walk) to B (Run) on the Go trigger.
        var c = new AnimatorController { name = "PlayController" };
        c.AddLayer("Base Layer");
        c.AddParameter("Go", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var sm = c.layers[0].stateMachine;
        var a = sm.AddState("A");
        a.motion = Clip("Walk");
        var b = sm.AddState("B");
        b.motion = Clip("Run");
        var ab = a.AddTransition(b);
        ab.duration = 0;
        ab.AddCondition(AnimatorConditionMode.If, 0, "Go");
        var names = new Dictionary<int, string> { [a.nameHash] = "A", [b.nameHash] = "B" };
        Time.captureDeltaTime = 0.02f;
        var rig = Rig("PlayRig", c);
        var animator = rig.GetComponent<Animator>();
        var hook = rig.AddComponent<FrameHook>();
        int last = 0;
        AnimatorState late = null;
        FrameHook.Late = frame =>
        {
            last = frame;
            var info = animator.GetCurrentAnimatorStateInfo(0);
            string state = names.TryGetValue(info.shortNameHash, out var n) ? n : info.shortNameHash.ToString();
            Put($"play.{frame:D2}", $"{state} t={F(info.normalizedTime)} trans={animator.IsInTransition(0)} params={animator.parameterCount} speed={F(animator.GetFloat("Speed"))} x={F(rig.transform.Find("Child").localPosition.x)}");
            if (frame == 3) animator.SetTrigger("Go");
            if (frame == 4) animator.SetFloat("Speed", 2);
            if (frame == 6)
            {
                c.AddParameter("Extra", AnimatorControllerParameterType.Float);
                Put("play.06.edited", $"params={animator.parameterCount} speed={F(animator.GetFloat("Speed"))} state={Name(names, animator.GetCurrentAnimatorStateInfo(0).shortNameHash)}");
            }
            if (frame == 10)
            {
                late = sm.AddState("C");
                names[late.nameHash] = "C";
                var bc = b.AddTransition(late);
                bc.duration = 0;
                bc.AddCondition(AnimatorConditionMode.Greater, 1, "Speed");
                Put("play.10.edited", $"params={animator.parameterCount} state={Name(names, animator.GetCurrentAnimatorStateInfo(0).shortNameHash)}");
            }
            if (frame == 14) animator.SetFloat("Speed", 2);
            if (frame == 16)
            {
                b.speed = 3;
                Put("play.16.edited", $"state={Name(names, animator.GetCurrentAnimatorStateInfo(0).shortNameHash)} t={F(animator.GetCurrentAnimatorStateInfo(0).normalizedTime)}");
            }
        };
        while (last < 22) yield return true;
        FrameHook.Late = null;
        Put("play.parameters", string.Join(",", animator.parameters.Select(p => p.name)));
        Logs("play");

        // The authored controller as an Animator plays it, and what the Animator hands back.
        var authored = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Controllers/Authored.controller");
        var rig2 = Rig("AuthoredRig", authored);
        var animator2 = rig2.GetComponent<Animator>();
        yield return true;
        Put("play.authored", $"{N(animator2.runtimeAnimatorController)} same={(animator2.runtimeAnimatorController == authored)} layers={animator2.layerCount} params={animator2.parameterCount}");
        Object.Destroy(rig);
        Object.Destroy(rig2);
        Time.captureDeltaTime = 0;
        Logs("play.authored");
    }

    static void Utility()
    {
        var log = new List<string>();
        AnimationUtility.OnCurveWasModified callback = (clip, binding, type) => log.Add($"{clip?.name}:{type}:{Binding(binding)}");
        AnimationUtility.onCurveWasModified += callback;
        void Flush(string key)
        {
            Put("util.modified." + key, string.Join(";", log));
            log.Clear();
        }
        var c = new AnimationClip { name = "Edited" };
        Flush("new");
        var bx = EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x");
        var by = EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.y");
        AnimationUtility.SetEditorCurve(c, bx, Line(0, 0, 1, 1));
        Flush("add");
        AnimationUtility.SetEditorCurve(c, bx, Line(0, 0, 2, 1));
        Flush("modify");
        AnimationUtility.SetEditorCurves(c, new[] { bx, by }, new[] { Line(0, 1, 1, 2), Line(0, 3, 1, 4) });
        Flush("many");
        AnimationUtility.SetEditorCurve(c, by, null);
        Flush("delete");
        var pb = EditorCurveBinding.PPtrCurve("", typeof(ProbeTarget), "reference");
        AnimationUtility.SetObjectReferenceCurve(c, pb, new[] { new ObjectReferenceKeyframe { time = 0, value = Clip("Walk") } });
        Flush("objectAdd");
        AnimationUtility.SetObjectReferenceCurves(c, new[] { pb }, new[] { new[] { new ObjectReferenceKeyframe { time = 0.5f, value = Clip("Run") } } });
        Flush("objectMany");
        AnimationUtility.SetObjectReferenceCurve(c, pb, null);
        Flush("objectDelete");
        c.SetCurve("Child", typeof(Transform), "m_LocalPosition.z", Line(0, 0, 1, 1));
        Flush("setCurve");
        var settings = AnimationUtility.GetAnimationClipSettings(c);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(c, settings);
        Flush("settings");
        AnimationUtility.SetAnimationEvents(c, new[] { new AnimationEvent { time = 0.5f, functionName = "Ping" } });
        Flush("events");
        c.frameRate = 24;
        c.ClearCurves();
        Flush("clear");
        AnimationUtility.onCurveWasModified -= callback;
        Put("util.curves", string.Join(";", AnimationUtility.GetCurveBindings(c).Select(Binding)) + " bound=" + Bound(c, bx));

        // Tangent modes.
        var curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 2), new Keyframe(3, 1));
        void Keys(string key) => Put("util.tangents." + key, string.Join(";", curve.keys.Select((k, i) =>
            $"{F(k.time)}:{F(k.value)}:{F(k.inTangent)}/{F(k.outTangent)}:{k.weightedMode}:{AnimationUtility.GetKeyLeftTangentMode(curve, i)}/{AnimationUtility.GetKeyRightTangentMode(curve, i)}/{AnimationUtility.GetKeyBroken(curve, i)}")));
        Keys("initial");
        AnimationUtility.SetKeyLeftTangentMode(curve, 1, AnimationUtility.TangentMode.Linear);
        Keys("leftLinear");
        AnimationUtility.SetKeyRightTangentMode(curve, 1, AnimationUtility.TangentMode.Constant);
        Keys("rightConstant");
        AnimationUtility.SetKeyBroken(curve, 1, true);
        Keys("broken");
        AnimationUtility.SetKeyRightTangentMode(curve, 0, AnimationUtility.TangentMode.Auto);
        AnimationUtility.SetKeyLeftTangentMode(curve, 2, AnimationUtility.TangentMode.ClampedAuto);
        Keys("auto");
        AnimationUtility.SetKeyBroken(curve, 1, false);
        Keys("unbroken");
        AnimationUtility.SetKeyLeftTangentMode(curve, 1, AnimationUtility.TangentMode.ClampedAuto);
        AnimationUtility.SetKeyRightTangentMode(curve, 1, AnimationUtility.TangentMode.ClampedAuto);
        Keys("clamped");
        AnimationUtility.SetKeyLeftTangentMode(curve, 1, AnimationUtility.TangentMode.Free);
        Keys("free");
        var uneven = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 2), new Keyframe(4, 3), new Keyframe(5, 3.5f));
        void Uneven(string key) => Put("util.tangents.uneven." + key, string.Join(";", uneven.keys.Select(k => $"{F(k.inTangent)}/{F(k.outTangent)}")));
        AnimationUtility.SetKeyLeftTangentMode(uneven, 1, AnimationUtility.TangentMode.Auto);
        Uneven("auto");
        AnimationUtility.SetKeyRightTangentMode(uneven, 1, AnimationUtility.TangentMode.Auto);
        Uneven("autoBoth");
        AnimationUtility.SetKeyLeftTangentMode(uneven, 2, AnimationUtility.TangentMode.ClampedAuto);
        AnimationUtility.SetKeyRightTangentMode(uneven, 2, AnimationUtility.TangentMode.ClampedAuto);
        Uneven("clamped");
        AnimationUtility.SetKeyRightTangentMode(uneven, 0, AnimationUtility.TangentMode.Linear);
        AnimationUtility.SetKeyLeftTangentMode(uneven, 3, AnimationUtility.TangentMode.Linear);
        Uneven("linearEnds");
        AnimationUtility.SetKeyRightTangentMode(uneven, 3, AnimationUtility.TangentMode.Auto);
        AnimationUtility.SetKeyLeftTangentMode(uneven, 0, AnimationUtility.TangentMode.ClampedAuto);
        Uneven("outerSides");
        var flat = AnimationCurve.EaseInOut(0, 0, 1, 1);
        Put("util.tangents.easeInOut", string.Join(";", flat.keys.Select((k, i) => $"{AnimationUtility.GetKeyLeftTangentMode(flat, i)}/{AnimationUtility.GetKeyRightTangentMode(flat, i)}")));
        Try("util.tangents.outOfRange", () => Put("util.tangents.outOfRange", AnimationUtility.GetKeyLeftTangentMode(curve, 7)));
        Logs("util.tangents");

        // A rig's clips, values and stream bindings.
        var rig = Rig("UtilRig", AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Controllers/Authored.controller"));
        rig.transform.Find("Child").localPosition = new Vector3(0.25f, 0.5f, 0.75f);
        rig.GetComponent<ProbeTarget>().reference = Clip("Pose");
        Put("util.clips.animator", string.Join(",", AnimationUtility.GetAnimationClips(rig).Select(x => x.name)));
        var legacy = new GameObject("Legacy");
        var animation = legacy.AddComponent<Animation>();
        AnimationUtility.SetAnimationClips(animation, new[] { Clip("Walk"), Clip("Run") });
        Put("util.clips.animation", string.Join(",", AnimationUtility.GetAnimationClips(legacy).Select(x => x.name)) + " count=" + animation.GetClipCount() + " clip=" + N(animation.clip));
        Put("util.clips.none", AnimationUtility.GetAnimationClips(new GameObject("None")).Length);
        Put("util.float", AnimationUtility.GetFloatValue(rig, bx, out float fx) + " " + F(fx));
        Put("util.discrete", AnimationUtility.GetDiscreteIntValue(rig, EditorCurveBinding.DiscreteCurve("", typeof(ProbeTarget), "steps"), out int steps) + " " + steps
            + " / " + AnimationUtility.GetDiscreteIntValue(rig, EditorCurveBinding.DiscreteCurve("Child2", typeof(GameObject), "m_IsActive"), out int active) + " " + active
            + " / " + AnimationUtility.GetDiscreteIntValue(rig, EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "amount"), out int amount) + " " + amount);
        var renderer = rig.transform.Find("Child").gameObject.AddComponent<MeshRenderer>();
        var material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "ProbeMaterial" };
        renderer.sharedMaterial = material;
        rig.transform.Find("Child2").gameObject.SetActive(false);
        Put("util.discreteMore", AnimationUtility.GetDiscreteIntValue(rig, EditorCurveBinding.DiscreteCurve("", typeof(ProbeTarget), "mode"), out int mode) + " " + mode
            + " / " + AnimationUtility.GetDiscreteIntValue(rig, EditorCurveBinding.DiscreteCurve("Child2", typeof(GameObject), "m_IsActive"), out int inactive) + " " + inactive
            + " / " + AnimationUtility.GetDiscreteIntValue(rig, EditorCurveBinding.DiscreteCurve("Child", typeof(MeshRenderer), "m_Enabled"), out int enabled) + " " + enabled
            + " / " + AnimationUtility.GetDiscreteIntValue(rig, EditorCurveBinding.DiscreteCurve("", typeof(ProbeTarget), "counted"), out int counted) + " " + counted);
        Put("util.objectMaterial", AnimationUtility.GetObjectReferenceValue(rig, EditorCurveBinding.PPtrCurve("Child", typeof(MeshRenderer), "m_Materials.Array.data[0]"), out var mat) + " " + N(mat)
            + " / " + AnimationUtility.GetObjectReferenceValue(rig, EditorCurveBinding.PPtrCurve("Child", typeof(MeshRenderer), "m_Materials.Array.data[1]"), out var mat1) + " " + N(mat1));
        Put("util.object", AnimationUtility.GetObjectReferenceValue(rig, pb, out var reference) + " " + N(reference)
            + " / " + AnimationUtility.GetObjectReferenceValue(rig, EditorCurveBinding.PPtrCurve("", typeof(ProbeTarget), "missing"), out var missing) + " " + N(missing));
        foreach (var (name, target, property) in new[] { ("position", (Object)rig.transform.Find("Child"), "m_LocalPosition.x"), ("field", rig.GetComponent<ProbeTarget>(), "amount"),
                     ("vector", rig.GetComponent<ProbeTarget>(), "offset.y"), ("active", rig.transform.Find("Child2").gameObject, "m_IsActive"), ("object", rig.GetComponent<ProbeTarget>(), "reference"),
                     ("rotation", rig.transform.Find("Child"), "m_LocalRotation.w"), ("unknown", rig.GetComponent<ProbeTarget>(), "nothing") })
        {
            var mod = new PropertyModification { target = target, propertyPath = property, value = "1" };
            Try("util.fromModification." + name, () =>
            {
                var type = AnimationUtility.PropertyModificationToEditorCurveBinding(mod, rig, out var binding);
                Put("util.fromModification." + name, "type=" + (type == null ? "null" : type.Name) + " " + Binding(binding));
            });
        }
        Stream("util.stream.static", rig);
        Try("util.generic", () => Put("util.generic", string.Join(";", AnimationUtility.EditorCurveBindingsToGenericBindings(new[] { bx, pb }).Select(g =>
            $"{g.isObjectReference}/{g.isDiscrete}/{g.typeID}/{g.transformPathHash}/{g.propertyNameHash}"))));
        Object.Destroy(rig);
        Object.Destroy(legacy);
        Logs("util");
    }

    static void Stream(string key, GameObject root) => Try(key, () =>
    {
        var method = typeof(AnimationUtility).GetMethod("GetAnimationStreamBindings", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var bindings = (EditorCurveBinding[])method.Invoke(null, new object[] { root });
        Put(key, string.Join(";", bindings.Select(Binding)));
    });

    /// <summary>The stream bindings of rigs whose controllers play clips with generic and object
    /// curves, before and after their first update.</summary>
    static IEnumerable<bool> Streams()
    {
        var c = new AnimatorController { name = "StreamController" };
        c.AddLayer("Base Layer");
        c.AddMotion(Clip("Mixed"));
        c.CreateBlendTreeInController("Blend", out var blend);
        blend.AddChild(Clip("Walk"));
        blend.AddChild(Clip("Root"));
        var rig = Rig("StreamRig", c);
        var plain = Rig("PlainRig", null);
        Stream("stream.new", rig);
        Stream("stream.none", plain);
        yield return true;
        yield return true;
        Stream("stream.updated", rig);
        var animator = rig.GetComponent<Animator>();
        Try("stream.bind", () =>
        {
            UnityEngine.Animations.AnimatorJobExtensions.BindStreamProperty(animator, rig.transform, typeof(ProbeTarget), "amount");
            UnityEngine.Animations.AnimatorJobExtensions.BindStreamTransform(animator, rig.transform.Find("Child2"));
            UnityEngine.Animations.AnimatorJobExtensions.BindCustomStreamProperty(animator, "custom", UnityEngine.Animations.CustomStreamPropertyType.Float);
        });
        Stream("stream.bound", rig);
        yield return true;
        Stream("stream.boundUpdated", rig);
        var callbacks = new List<string>();
        AnimationUtility.OnCurveWasModified callback = (clip, binding, type) => callbacks.Add($"{clip?.name}:{type}:{Binding(binding)}");
        AnimationUtility.onCurveWasModified += callback;
        var copy = Object.Instantiate(Clip("Walk"));
        Put("stream.instantiateClip", string.Join(";", callbacks) + " name=" + copy.name);
        AnimationUtility.onCurveWasModified -= callback;
        Object.Destroy(rig);
        Object.Destroy(plain);
        Logs("stream");
    }

    static string Bound(AnimationClip c, EditorCurveBinding b) => AnimationUtility.GetEditorCurve(c, b) is AnimationCurve curve ? curve.length.ToString() : "none";

    static void Mode()
    {
        var rig = Rig("ModeRig", null);
        var child = rig.transform.Find("Child");
        child.localPosition = new Vector3(5, 6, 7);
        var target = rig.GetComponent<ProbeTarget>();
        Put("mode.colors", $"{AnimationMode.animatedPropertyColor} {AnimationMode.recordedPropertyColor} {AnimationMode.candidatePropertyColor}");
        var driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
        var other = ScriptableObject.CreateInstance<AnimationModeDriver>();
        Put("mode.before", AnimationMode.InAnimationMode() + " " + AnimationMode.InAnimationMode(driver) + " " + AnimationUtility.InAnimationMode());
        Try("mode.start", () => AnimationMode.StartAnimationMode(driver));
        Put("mode.started", AnimationMode.InAnimationMode() + " " + AnimationMode.InAnimationMode(driver) + " " + AnimationMode.InAnimationMode(other));
        Try("mode.sample", () =>
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(rig, Clip("Run"), 0.5f);
            AnimationMode.SampleAnimationClip(rig, Clip("Mixed"), 0.75f);
            AnimationMode.EndSampling();
        });
        Put("mode.sampled", $"{V(child.localPosition)} {V(child.localEulerAngles)} amount={F(target.amount)} ref={N(target.reference)} active={rig.transform.Find("Child2").gameObject.activeSelf}");
        Put("mode.animated", string.Join(" ", new[] { "m_LocalPosition.x", "m_LocalPosition", "m_LocalRotation.y", "m_LocalScale.x" }.Select(p => p + "=" + AnimationMode.IsPropertyAnimated(child, p)))
            + " amount=" + AnimationMode.IsPropertyAnimated(target, "amount") + " steps=" + AnimationMode.IsPropertyAnimated(target, "steps"));
        Try("mode.add", () =>
        {
            AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(ProbeTarget), "steps"), new PropertyModification { target = target, propertyPath = "steps", value = "2" }, true);
            AnimationMode.AddEditorCurveBinding(rig, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.x"));
        });
        target.steps = 9;
        rig.transform.localScale = new Vector3(3, 3, 3);
        Put("mode.added", "steps=" + AnimationMode.IsPropertyAnimated(target, "steps") + " scale=" + AnimationMode.IsPropertyAnimated(rig.transform, "m_LocalScale.x"));
        child.localPosition = new Vector3(8, 8, 8);
        Try("mode.stopOther", () => AnimationMode.StopAnimationMode(other));
        Put("mode.stoppedOther", AnimationMode.InAnimationMode() + " " + V(child.localPosition));
        Try("mode.stop", () => AnimationMode.StopAnimationMode(driver));
        Put("mode.stopped", $"{AnimationMode.InAnimationMode()} {V(child.localPosition)} {V(child.localEulerAngles)} amount={F(target.amount)} ref={N(target.reference)} steps={target.steps} scale={V(rig.transform.localScale)} active={rig.transform.Find("Child2").gameObject.activeSelf}");
        Try("mode.plain", () =>
        {
            AnimationMode.StartAnimationMode();
            Put("mode.plainStarted", AnimationMode.InAnimationMode() + " " + AnimationMode.InAnimationMode(driver));
            AnimationMode.SampleAnimationClip(rig, Clip("Walk"), 0.5f);
            Put("mode.plainSampled", V(child.localPosition));
            AnimationMode.StopAnimationMode();
            Put("mode.plainStopped", AnimationMode.InAnimationMode() + " " + V(child.localPosition));
        });
        Try("mode.outside", () =>
        {
            AnimationMode.SampleAnimationClip(rig, Clip("Walk"), 0.25f);
            Put("mode.outside", V(child.localPosition) + " " + AnimationMode.IsPropertyAnimated(child, "m_LocalPosition.x"));
        });
        Object.Destroy(rig);
        Logs("mode");
    }

    /// <summary>Validation messages, naming, removal scope, new-object defaults, tangent
    /// clamping and a clip's load callback.</summary>
    static void Extra()
    {
        var t0 = new AnimatorStateTransition();
        Put("extra.newStateTransition", $"dur={F(t0.duration)} off={F(t0.offset)} exitTime={F(t0.exitTime)} hasExit={t0.hasExitTime} fixed={t0.hasFixedDuration} int={t0.interruptionSource} ordered={t0.orderedInterruption} self={t0.canTransitionToSelf} name='{t0.name}' flags={t0.hideFlags}");
        var t1 = new AnimatorTransition();
        Put("extra.newTransition", $"exit={t1.isExit} solo={t1.solo} mute={t1.mute} name='{t1.name}' flags={t1.hideFlags} display='{t1.GetDisplayName(null)}'");
        var bt = new BlendTree();
        Put("extra.newTree", Tree(bt));
        var st = new AnimatorState();
        Put("extra.newState", $"'{st.name}' motion={N(st.motion)} transitions={st.transitions.Length}");

        var c = new AnimatorController { name = "Checks" };
        c.AddLayer("Base Layer");
        c.AddParameter("P", AnimatorControllerParameterType.Float);
        var sm = c.layers[0].stateMachine;
        var a = sm.AddState("A");
        var b = sm.AddState("B");
        sm.AddAnyStateTransition(b);
        sm.AddEntryTransition(b);
        var sub = sm.AddStateMachine("Clash");
        sub.AddState("Deep");
        sm.AddStateMachineTransition(sub, a);
        var tree = new BlendTree { name = "Two", blendType = BlendTreeType.FreeformCartesian2D, blendParameter = "MissA", blendParameterY = "MissB" };
        tree.AddChild(Clip("Walk"), new Vector2(1, 0));
        var direct = tree.CreateBlendTreeChild(new Vector2(0, 1));
        direct.blendType = BlendTreeType.Direct;
        direct.AddChild(Clip("Run"));
        var treeState = sm.AddState("TreeState");
        treeState.motion = tree;
        var muted = a.AddTransition(b);
        muted.mute = true;
        var exitOnly = a.AddExitTransition();
        exitOnly.solo = true;
        s_Logs.Clear();
        Put("extra.compile", string.Join(",", c.animationClips.Select(x => x.name)));
        Logs("extra.compile");
        Put("extra.uniqueClash", sm.MakeUniqueStateName("Clash") + "|" + sm.MakeUniqueStateMachineName("A") + "|" + sm.MakeUniqueStateName("a"));
        var deep = sub.states[0].state;
        b.AddTransition(deep);
        sub.RemoveState(deep);
        Put("extra.removeFromSub", $"b={b.transitions.Length} deep={(deep == null)} sub={sub.states.Length}");
        var both = new AnimatorStateTransition();
        both.destinationState = b;
        both.destinationStateMachine = sub;
        Put("extra.bothDestinations", N(both.destinationState) + " " + N(both.destinationStateMachine) + " '" + both.GetDisplayName(a) + "'");
        both.isExit = true;
        Put("extra.exitWithDestination", "'" + both.GetDisplayName(a) + "' '" + both.GetDisplayName(sm) + "'");
        Logs("extra.model");

        var steep = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 0.1f), new Keyframe(1.1f, 5), new Keyframe(2, 5.5f));
        AnimationUtility.SetKeyLeftTangentMode(steep, 1, AnimationUtility.TangentMode.ClampedAuto);
        AnimationUtility.SetKeyRightTangentMode(steep, 1, AnimationUtility.TangentMode.ClampedAuto);
        AnimationUtility.SetKeyLeftTangentMode(steep, 2, AnimationUtility.TangentMode.Auto);
        AnimationUtility.SetKeyRightTangentMode(steep, 2, AnimationUtility.TangentMode.Auto);
        Put("extra.tangents.steep", string.Join(";", steep.keys.Select(k => $"{F(k.inTangent)}/{F(k.outTangent)}")));
        var shapes = new[]
        {
            new[] { new Vector2(0, 0), new Vector2(1, 0.1f), new Vector2(2, 5) },
            new[] { new Vector2(0, 0), new Vector2(2, 0.1f), new Vector2(2.5f, 5) },
            new[] { new Vector2(0, 0), new Vector2(1, 1), new Vector2(1.5f, 1.2f) },
            new[] { new Vector2(0, 5), new Vector2(1, 4.9f), new Vector2(1.1f, 0) },
            new[] { new Vector2(0, 0), new Vector2(0.1f, 4.9f), new Vector2(1.1f, 5) },
            new[] { new Vector2(0, 0), new Vector2(1, 3), new Vector2(2, 4) },
        };
        for (int i = 0; i < shapes.Length; i++)
        {
            var shape = new AnimationCurve(shapes[i].Select(v => new Keyframe(v.x, v.y)).ToArray());
            AnimationUtility.SetKeyLeftTangentMode(shape, 1, AnimationUtility.TangentMode.ClampedAuto);
            var auto = new AnimationCurve(shapes[i].Select(v => new Keyframe(v.x, v.y)).ToArray());
            AnimationUtility.SetKeyLeftTangentMode(auto, 1, AnimationUtility.TangentMode.Auto);
            Put("extra.tangents.shape" + i, F(shape.keys[1].inTangent) + "/" + F(shape.keys[1].outTangent) + " auto " + F(auto.keys[1].inTangent) + "/" + F(auto.keys[1].outTangent));
        }
        var mixed = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 2), new Keyframe(2, 3));
        AnimationUtility.SetKeyRightTangentMode(mixed, 1, AnimationUtility.TangentMode.Constant);
        AnimationUtility.SetKeyLeftTangentMode(mixed, 1, AnimationUtility.TangentMode.ClampedAuto);
        Put("extra.tangents.clampedConstant", string.Join(";", mixed.keys.Select((k, i) => $"{F(k.inTangent)}/{F(k.outTangent)}:{AnimationUtility.GetKeyLeftTangentMode(mixed, i)}/{AnimationUtility.GetKeyRightTangentMode(mixed, i)}/{AnimationUtility.GetKeyBroken(mixed, i)}")));
        var linear = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 2), new Keyframe(2, 3));
        AnimationUtility.SetKeyLeftTangentMode(linear, 1, AnimationUtility.TangentMode.ClampedAuto);
        AnimationUtility.SetKeyRightTangentMode(linear, 1, AnimationUtility.TangentMode.Linear);
        Put("extra.tangents.clampedLinear", string.Join(";", linear.keys.Select((k, i) => $"{F(k.inTangent)}/{F(k.outTangent)}:{AnimationUtility.GetKeyLeftTangentMode(linear, i)}/{AnimationUtility.GetKeyRightTangentMode(linear, i)}/{AnimationUtility.GetKeyBroken(linear, i)}")));
        var raw = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
        AnimationUtility.SetKeyLeftTangentMode(raw, 0, AnimationUtility.TangentMode.Linear);
        AnimationUtility.SetKeyRightTangentMode(raw, 1, AnimationUtility.TangentMode.Constant);
#pragma warning disable 618
        Put("extra.tangents.rawModes", string.Join(";", raw.keys.Select(k => k.tangentMode)));
#pragma warning restore 618

        var callbacks = new List<string>();
        AnimationUtility.OnCurveWasModified callback = (clip, binding, type) => callbacks.Add($"{clip?.name}:{type}:{Binding(binding)}");
        AnimationUtility.onCurveWasModified += callback;
        var spare = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Clips/Spare.anim");
        Put("extra.loadClip", string.Join(";", callbacks) + " " + N(spare));
        callbacks.Clear();
        var made = AnimatorController.AllocateAnimatorClip("Allocated");
        Put("extra.allocateClip", string.Join(";", callbacks));
        AnimationUtility.onCurveWasModified -= callback;
        Logs("extra");
    }

    static IEnumerable<bool> Bounds()
    {
        var holder = new GameObject("Lines");
        holder.transform.position = new Vector3(1, 2, 3);
        holder.transform.rotation = Quaternion.Euler(0, 30, 0);
        holder.transform.localScale = new Vector3(2, 1, 1);
        LineRenderer Make(string name, bool world, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = world;
            line.widthMultiplier = width;
            line.loop = loop;
            line.positionCount = 3;
            line.SetPositions(new[] { new Vector3(0, 0, 0), new Vector3(1, 2, 0), new Vector3(3, -1, 1) });
            return line;
        }
        var lines = new[]
        {
            Make("local", false, 0.5f, false), Make("world", true, 0.5f, false), Make("loop", false, 1f, true), Make("thin", false, 0f, false),
            Make("curve", false, 1f, false), Make("view", false, 0.5f, false), Make("single", false, 0.5f, false), Make("empty", false, 0.5f, false),
        };
        lines[4].widthCurve = new AnimationCurve(new Keyframe(0, 0.2f), new Keyframe(1, 2f));
        lines[5].alignment = LineAlignment.TransformZ;
        lines[6].positionCount = 1;
        lines[7].positionCount = 0;
        var trailGo = new GameObject("Trail");
        trailGo.transform.position = new Vector3(0, 0, 0);
        var trail = trailGo.AddComponent<TrailRenderer>();
        trail.time = 10;
        trail.widthMultiplier = 0.5f;
        trail.minVertexDistance = 0.01f;
        yield return true;
        for (int i = 1; i <= 4; i++)
        {
            trailGo.transform.position = new Vector3(i, i * 0.5f, -i);
            yield return true;
        }
        yield return true;
        foreach (var line in lines)
            Put("bounds.line." + line.name, $"{V(line.bounds.center)} {V(line.bounds.size)} local={V(line.localBounds.center)} {V(line.localBounds.size)} enabled={line.enabled}");
        Put("bounds.trail", $"{V(trail.bounds.center)} {V(trail.bounds.size)} local={V(trail.localBounds.center)} {V(trail.localBounds.size)} points={trail.positionCount}");
        Object.Destroy(holder);
        Object.Destroy(trailGo);
        Logs("bounds");
    }
}
