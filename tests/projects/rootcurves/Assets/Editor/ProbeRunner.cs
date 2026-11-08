using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> (batchmode) writes the probe's clips and controllers. With
/// <c>ROOT_CURVES_OUT</c> set, a windowed Editor opens the scene, enters Play mode and quits when
/// <see cref="RootCurves"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("ROOT_CURVES_OUT") == null || Application.isBatchMode) return;
        if (SessionState.GetBool("RootCurves.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("RootCurves.finished", false)) EditorApplication.Exit(0);
        };
        EditorApplication.update += Tick;
    }

    static void Begin()
    {
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
            foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
            var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            game.Focus();
            PlayModeWindow.SetPlayModeFocused(true);
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }

    static void Tick()
    {
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !RootCurves.Finished) return;
        SessionState.SetBool("RootCurves.finished", true);
        EditorApplication.ExitPlaymode();
    }

    const float Step = 1f / 30f;

    /// <summary>A curve through f at every 1/30 s of [0, 1], with central-difference tangents.</summary>
    static AnimationCurve Curve(Func<float, float> f)
    {
        var keys = new Keyframe[31];
        for (int i = 0; i <= 30; i++)
        {
            float t = i * Step;
            float slope = (f(Math.Min(t + Step, 1f)) - f(Math.Max(t - Step, 0f))) / ((i == 0 || i == 30) ? Step : 2f * Step);
            keys[i] = new Keyframe(t, f(t), slope, slope);
        }
        return new AnimationCurve(keys);
    }

    static float Wave(float t) => Mathf.Sin(2f * Mathf.PI * t);

    /// <summary>The body's motion over the clip: sideways sway, a bob around 1 m, 2 m forward, and
    /// a yaw from 40 to 130 degrees with a small pitch sway.</summary>
    static Quaternion Turn(float t) => Quaternion.Euler(5f * Wave(t), 40f + 90f * t, 0f);

    static void Body(AnimationClip clip, string prefix, float speed = 2f, bool turn = true, bool partial = false)
    {
        void Set(string attribute, Func<float, float> f) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prefix + attribute), Curve(f));
        Set("T.x", t => 0.2f * Wave(t));
        if (!partial) Set("T.y", t => 1f + 0.1f * Mathf.Sin(4f * Mathf.PI * t));
        Set("T.z", t => speed * t);
        if (partial) return;
        Set("Q.x", t => turn ? Turn(t).x : 0f);
        Set("Q.y", t => turn ? Turn(t).y : 0f);
        Set("Q.z", t => turn ? Turn(t).z : 0f);
        Set("Q.w", t => turn ? Turn(t).w : 1f);
    }

    static AnimationClip Clip(string name, Action<AnimationClip> curves, Action<AnimationClipSettings> settings)
    {
        var clip = new AnimationClip { name = name, frameRate = 30f };
        curves(clip);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x"),
            AnimationCurve.Linear(0f, 0f, 1f, 1f));
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = true;
        settings(s);
        AnimationUtility.SetAnimationClipSettings(clip, s);
        AssetDatabase.CreateAsset(clip, "Assets/Resources/" + name + ".anim");
        return clip;
    }

    static AnimatorController Controller(string name, out AnimatorStateMachine machine)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/C_" + name + ".controller");
        machine = controller.layers[0].stateMachine;
        return controller;
    }

    static void Single(string name, AnimationClip clip)
    {
        Controller(name, out var machine);
        machine.AddState(clip.name).motion = clip;
    }

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        // The settings of the generated clips that motivated the probe: loop pose, Y based on original.
        var game = Clip("Game", c => Body(c, "Root"), s => { s.loopBlend = true; s.keepOriginalPositionY = true; });
        Single("Game", game);
        Single("Plain", Clip("Plain", c => Body(c, "Root"), s => s.loopBlend = true));
        Single("KeepOrientation", Clip("KeepOrientation", c => Body(c, "Root"), s => s.keepOriginalOrientation = true));
        Single("KeepY", Clip("KeepY", c => Body(c, "Root"), s => s.keepOriginalPositionY = true));
        Single("KeepXZ", Clip("KeepXZ", c => Body(c, "Root"), s => s.keepOriginalPositionXZ = true));
        Single("BakeRotation", Clip("BakeRotation", c => Body(c, "Root"), s => s.loopBlendOrientation = true));
        Single("BakeY", Clip("BakeY", c => Body(c, "Root"), s => s.loopBlendPositionY = true));
        Single("BakeXZ", Clip("BakeXZ", c => Body(c, "Root"), s => s.loopBlendPositionXZ = true));
        Single("NoLoopBlend", Clip("NoLoopBlend", c => Body(c, "Root"), s => { }));
        Single("Once", Clip("Once", c => Body(c, "Root"), s => s.loopTime = false));
        Single("Motion", Clip("Motion", c => Body(c, "Motion"), s => s.loopBlend = true));
        Single("Partial", Clip("Partial", c => Body(c, "Root", partial: true), s => { }));
        Single("WithRoot", Clip("WithRoot", c =>
        {
            Body(c, "Root");
            AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y"),
                AnimationCurve.Linear(0f, 0f, 1f, 0.5f));
        }, s => { }));
        var walk = Clip("Walk", c => Body(c, "Root", speed: 1f, turn: false), s => { });
        Single("Walk", walk);

        var blend = Controller("Blend", out var blendMachine);
        blend.AddParameter(new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f });
        var tree = new BlendTree { name = "Tree", blendParameter = "Speed", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, blend);
        tree.AddChild(walk, 0f);
        tree.AddChild(game, 1f);
        blendMachine.AddState("Tree").motion = tree;

        Controller("Transition", out var machine);
        var from = machine.AddState("Walk");
        from.motion = walk;
        var to = machine.AddState("Game");
        to.motion = game;
        var transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 0.5f;
        transition.duration = 0.25f;
        transition.hasFixedDuration = true;
        AssetDatabase.SaveAssets();
    }
}
