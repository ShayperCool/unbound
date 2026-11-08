using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>
/// Root motion from the Animator's own body curves (RootT.x/y/z, RootQ.x/y/z/w on the root, the
/// curves an Editor-authored generic clip carries) on Animators without an Avatar, and the controls
/// around them: every case runs at once from the first frame, its Animator updated by the player
/// loop (Time.captureFramerate 50, fixed step 0.02 s). Each frame records the root's pose and the
/// Animator's motion (deltaPosition, deltaRotation, velocity, angularVelocity), in LateUpdate and,
/// for the cases with a movement script, in OnAnimatorMove.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class RootCurves : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder s_Output = new StringBuilder();
    const int Frames = 80;
    int frame, step;
    readonly List<(string Name, Animator Animator)> cases = new List<(string, Animator)>();
    readonly List<PlayableGraph> graphs = new List<PlayableGraph>();

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    public static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    public static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string S(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static void Record(string key, string value) =>
        s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");

    public static string Motion(Animator a) =>
        "\"dp\":" + V(a.deltaPosition) + ",\"dr\":" + Q(a.deltaRotation) + ",\"v\":" + V(a.velocity) + ",\"av\":" + V(a.angularVelocity);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("ROOT_CURVES_OUT") == null) return;
        Time.captureFramerate = 50;
        Time.fixedDeltaTime = .02f;
        Application.runInBackground = true;
        DontDestroyOnLoad(new GameObject("RootCurves").AddComponent<RootCurves>().gameObject);
        Application.logMessageReceived += (message, stack, kind) =>
        {
            if ((kind == LogType.Error || kind == LogType.Exception) && !stack.Contains("UnityEditor."))
                Record("error", S(message));
        };
    }

    static RuntimeAnimatorController Controller(string name) => Resources.Load<RuntimeAnimatorController>(name);

    Animator Make(string name, string controller, bool rootMotion = true, bool script = false, bool physics = false,
        bool avatar = false, float scale = 1f)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.position = new Vector3(cases.Count * 10f, 0f, 0f);
        go.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
        go.transform.localScale = Vector3.one * scale;
        new GameObject("Child").transform.SetParent(go.transform, false);
        if (physics)
        {
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
        }
        var animator = go.AddComponent<Animator>();
        if (avatar) animator.avatar = AvatarBuilder.BuildGenericAvatar(go, "");
        if (controller != null) animator.runtimeAnimatorController = Controller(controller);
        animator.applyRootMotion = rootMotion;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (physics) animator.updateMode = AnimatorUpdateMode.Fixed;
        if (script)
        {
            var mover = physics ? go.AddComponent<BodyMover>() : (Mover)go.AddComponent<Mover>();
            mover.label = name;
        }
        go.SetActive(true);
        cases.Add((name, animator));
        return animator;
    }

    static AnimationClip Clip(string name) => Resources.Load<AnimationClip>(name);

    /// <summary>An Animator without a controller played by a PlayableGraph (GameTime) whose
    /// AnimationPlayableOutput takes the playable <paramref name="source"/> builds.</summary>
    void MakeGraph(string name, Func<PlayableGraph, Playable> source, bool rootMotion = true, bool script = false)
    {
        var animator = Make(name, null, rootMotion, script);
        var graph = PlayableGraph.Create(name);
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationPlayableOutput.Create(graph, "Animation", animator).SetSourcePlayable(source(graph));
        graph.Play();
        graphs.Add(graph);
    }

    static Playable ClipPlayable(PlayableGraph graph, string clip) => AnimationClipPlayable.Create(graph, Clip(clip));

    static Playable Mixer(PlayableGraph graph, string first, float firstWeight, string second, float secondWeight)
    {
        var mixer = AnimationMixerPlayable.Create(graph, 2);
        graph.Connect(ClipPlayable(graph, first), 0, mixer, 0);
        graph.Connect(ClipPlayable(graph, second), 0, mixer, 1);
        mixer.SetInputWeight(0, firstWeight);
        mixer.SetInputWeight(1, secondWeight);
        return mixer;
    }

    void Start()
    {
        // The settings variants with builtin root motion (no script): the clip's root settings.
        foreach (var clip in new[] { "Game", "Plain", "KeepOrientation", "KeepY", "KeepXZ", "BakeRotation",
                     "BakeY", "BakeXZ", "NoLoopBlend", "Once" })
            Make("builtin." + clip, "C_" + clip);
        Make("off", "C_Game", rootMotion: false);
        Make("script", "C_Game", script: true);
        Make("scriptOff", "C_Game", rootMotion: false, script: true);
        Make("physics", "C_Game", script: true, physics: true);
        Make("blend", "C_Blend");
        Make("transition", "C_Transition");
        var overridden = Make("override", "C_Walk");
        var map = new AnimatorOverrideController(overridden.runtimeAnimatorController);
        map["Walk"] = Resources.Load<AnimationClip>("Game");
        overridden.runtimeAnimatorController = map;
        Make("motionCurves", "C_Motion");
        Make("partial", "C_Partial");
        Make("withRootTransform", "C_WithRoot");
        Make("avatar", "C_Game", avatar: true);
        Make("scaled", "C_Game", scale: 2f);
        // Playable graph outputs on Animators without a controller: the same clips' body curves.
        MakeGraph("graph.clip", g => ClipPlayable(g, "Game"));
        MakeGraph("graph.keepOrientation", g => ClipPlayable(g, "KeepOrientation"));
        MakeGraph("graph.bakeXZ", g => ClipPlayable(g, "BakeXZ"));
        MakeGraph("graph.once", g => ClipPlayable(g, "Once"));
        MakeGraph("graph.off", g => ClipPlayable(g, "Game"), rootMotion: false);
        MakeGraph("graph.script", g => ClipPlayable(g, "Game"), script: true);
        MakeGraph("graph.scriptOff", g => ClipPlayable(g, "Game"), rootMotion: false, script: true);
        MakeGraph("graph.mixer", g => Mixer(g, "Walk", 0.5f, "Game", 0.5f));
        MakeGraph("graph.mixerPartial", g => Mixer(g, "Walk", 0.25f, "Game", 0.25f));
        MakeGraph("graph.motionCurves", g => ClipPlayable(g, "Motion"));
        MakeGraph("graph.controller", g => AnimatorControllerPlayable.Create(g, Controller("C_Game")));
        foreach (var (name, a) in cases)
            Record("info." + name, "{\"hasRootMotion\":" + (a.hasRootMotion ? 1 : 0) + ",\"isHuman\":" + (a.isHuman ? 1 : 0)
                + ",\"avatar\":" + (a.avatar != null ? 1 : 0) + ",\"updateMode\":" + S(a.updateMode.ToString()) + "}");
        foreach (var clip in Resources.LoadAll<AnimationClip>(""))
            Record("clip." + clip.name, "{\"length\":" + F(clip.length) + ",\"isLooping\":" + (clip.isLooping ? 1 : 0)
                + ",\"hasRootCurves\":" + (clip.hasRootCurves ? 1 : 0) + ",\"hasMotionCurves\":" + (clip.hasMotionCurves ? 1 : 0)
                + ",\"hasGenericRootTransform\":" + (clip.hasGenericRootTransform ? 1 : 0)
                + ",\"averageSpeed\":" + V(clip.averageSpeed) + ",\"averageAngularSpeed\":" + F(clip.averageAngularSpeed) + "}");
        // Motion statistics beyond averageSpeed, as separate records.
        foreach (var clip in Resources.LoadAll<AnimationClip>(""))
            Record("stats." + clip.name, "{\"apparentSpeed\":" + F(clip.apparentSpeed) + ",\"averageDuration\":" + F(clip.averageDuration)
                + ",\"isHumanMotion\":" + (clip.isHumanMotion ? 1 : 0) + ",\"humanMotion\":" + (clip.humanMotion ? 1 : 0)
                + ",\"hasMotionFloatCurves\":" + (clip.hasMotionFloatCurves ? 1 : 0) + ",\"isLooping\":" + (clip.isLooping ? 1 : 0) + "}");
    }

    void FixedUpdate()
    {
        if (Finished || cases.Count == 0) return;
        foreach (var (name, a) in cases)
            if (a.updateMode == AnimatorUpdateMode.Fixed)
                Record("fixed." + step + "." + name, "{\"p\":" + V(a.transform.position) + ",\"v\":" + V(a.GetComponent<Rigidbody>().linearVelocity) + ",\"frame\":" + frame + "}");
        step++;
    }

    void LateUpdate()
    {
        if (Finished) return;
        foreach (var (name, a) in cases)
            Record(frame + "." + name, "{\"p\":" + V(a.transform.position) + ",\"r\":" + Q(a.transform.rotation)
                + ",\"child\":" + V(a.transform.GetChild(0).localPosition) + "," + Motion(a) + "}");
        if (++frame < Frames) return;
        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Environment.GetEnvironmentVariable("ROOT_CURVES_OUT"), s_Output.ToString());
        Finished = true;
        foreach (var graph in graphs) graph.Destroy();
        foreach (var (_, a) in cases) Object.Destroy(a.gameObject);
    }
}

/// <summary>Moves the root by the Animator's motion from OnAnimatorMove and records it.</summary>
public class Mover : MonoBehaviour
{
    public string label;
    int calls;

    protected virtual void Move(Animator animator)
    {
        transform.position += animator.deltaPosition;
        transform.rotation = animator.deltaRotation * transform.rotation;
    }

    void OnAnimatorMove()
    {
        if (RootCurves.Finished) return;
        var animator = GetComponent<Animator>();
        RootCurves.Record("move." + calls++ + "." + label, "{" + RootCurves.Motion(animator) + "}");
        Move(animator);
    }
}

/// <summary>Drives a Rigidbody with the Animator's motion, as a character controller would:
/// linearVelocity = deltaPosition / deltaTime.</summary>
public class BodyMover : Mover
{
    protected override void Move(Animator animator)
    {
        var body = GetComponent<Rigidbody>();
        body.linearVelocity = Time.deltaTime > 0f ? animator.deltaPosition / Time.deltaTime : Vector3.zero;
        body.MoveRotation(animator.deltaRotation * body.rotation);
    }
}
