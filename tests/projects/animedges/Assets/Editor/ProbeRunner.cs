using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> (batchmode) authors the probe's clips and controllers into Assets/Resources. With
/// <c>ANIM_EDGES_OUT</c> set, a windowed Editor opens the scene, enters Play mode and quits when
/// <see cref="AnimEdgesProbe"/> has written its record.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("ANIM_EDGES_OUT") == null || Application.isBatchMode) return;
        if (SessionState.GetBool("AnimEdgesProbe.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("AnimEdgesProbe.finished", false))
                EditorApplication.Exit(0);
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
        if (!EditorApplication.isPlaying || !AnimEdgesProbe.Finished) return;
        SessionState.SetBool("AnimEdgesProbe.finished", true);
        EditorApplication.ExitPlaymode();
    }

    public static void Create()
    {
        // Each path's x has one segment whose ends carry an infinite tangent (sign and side vary);
        // E has a middle key with an infinite in-tangent; L is a plain linear control.
        var steps = new AnimationClip { name = "Steps" };
        void Curve(string path, params Keyframe[] keys) =>
            AnimationUtility.SetEditorCurve(steps, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x"), new AnimationCurve(keys));
        float inf = float.PositiveInfinity;
        Curve("A", new Keyframe(0f, 1f, 0f, inf), new Keyframe(1f, 3f, 0f, 0f));
        Curve("B", new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 3f, -inf, 0f));
        Curve("C", new Keyframe(0f, 1f, 0f, -inf), new Keyframe(1f, 3f, 0f, 0f));
        Curve("D", new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 3f, inf, 0f));
        Curve("E", new Keyframe(0f, 1f, 0f, 0f), new Keyframe(0.5f, 2f, -inf, 0f), new Keyframe(1f, 3f, 0f, 0f));
        Curve("L", new Keyframe(0f, 1f, 2f, 2f), new Keyframe(1f, 3f, 2f, 2f));
        AssetDatabase.CreateAsset(steps, "Assets/Resources/Steps.anim");
        AnimatorController.CreateAnimatorControllerAtPathWithClip("Assets/Resources/C_Steps.controller", steps);

        // Two states holding A.x at 1 and at 2 (A is the default state).
        AnimationClip Hold(string name, float value)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("A", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0f, value, 1f, value + 1f));
            AssetDatabase.CreateAsset(clip, "Assets/Resources/" + name + ".anim");
            return clip;
        }
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/C_Two.controller");
        var machine = controller.layers[0].stateMachine;
        machine.AddState("A").motion = Hold("HoldA", 1f);
        machine.AddState("B").motion = Hold("HoldB", 10f);
        AssetDatabase.SaveAssets();
    }
}
