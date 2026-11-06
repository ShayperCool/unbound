using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> (batchmode) writes the probe's clips, controllers and empty scene. With
/// <c>ANIMCULLING_PROBE_OUT</c> set, a windowed Editor (an X server of its own, the GPU through
/// VirtualGL) closes every Scene view (a Scene view camera also makes renderers visible), opens
/// the probe scene at a 192x108 Game view, enters Play mode and quits when
/// <see cref="AnimCullingProbe"/> has written its record (nonzero on failure). A watchdog quits
/// with code 4 when Play mode does not finish in time.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory("Assets/Scenes");
        var loop = Clip("Loop", false, ("Marker", "m_LocalPosition.x", 0f, 1f));
        var second = Clip("Second", false, ("Marker", "m_LocalPosition.y", 0f, 1f));
        var rootMove = Clip("RootMove", false, ("", "m_LocalPosition.y", 0f, 0.5f), ("Marker", "m_LocalPosition.x", 0f, 1f));
        Clip("LegacyLoop", true, ("Marker", "m_LocalPosition.x", 0f, 1f));

        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/Culling.controller");
        var machine = controller.layers[0].stateMachine;
        var a = machine.AddState("A");
        a.motion = loop;
        var b = machine.AddState("B");
        b.motion = second;
        machine.defaultState = a;
        var toB = a.AddTransition(b);
        toB.hasExitTime = true;
        toB.exitTime = 1.2f;
        toB.duration = 0f;
        toB.hasFixedDuration = true;

        var motion = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/RootMotion.controller");
        var move = motion.layers[0].stateMachine.AddState("Move");
        move.motion = rootMove;
        motion.layers[0].stateMachine.defaultState = move;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        CreatePlayables();
        AssetDatabase.SaveAssets();
    }

    public static void CreatePlayables()
    {
        var a = Clip("PlayablePose", false, ("", "m_LocalPosition.y", 0f, 0.5f), ("Marker", "m_LocalPosition.x", 0f, 1f));
        var b = Clip("PlayableSecond", false, ("", "m_LocalPosition.y", 0f, 1f), ("Marker", "m_LocalPosition.y", 0f, 2f));
        AnimationUtility.SetEditorCurve(a, EditorCurveBinding.FloatCurve("", typeof(CullReceiver), "value"), AnimationCurve.Linear(0f, 2f, 0.5f, 3f));
        AnimationUtility.SetEditorCurve(b, EditorCurveBinding.FloatCurve("", typeof(CullReceiver), "value"), AnimationCurve.Linear(0f, 4f, 0.5f, 6f));
        AssetDatabase.SaveAssets();
    }

    /// <summary>A 0.5 s looping clip of linear curves with an event "OnClipEvent" at 0.1 s.</summary>
    static AnimationClip Clip(string name, bool legacy, params (string path, string property, float from, float to)[] curves)
    {
        var clip = new AnimationClip { name = name, frameRate = 60f, legacy = legacy };
        foreach (var c in curves)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(c.path, typeof(Transform), c.property),
                AnimationCurve.Linear(0f, c.from, 0.5f, c.to));
        AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = 0.1f, functionName = "OnClipEvent" } });
        if (legacy) clip.wrapMode = WrapMode.Loop;
        else
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        AssetDatabase.CreateAsset(clip, "Assets/Resources/" + name + ".anim");
        return clip;
    }

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("ANIMCULLING_PROBE_OUT");
        // Unbound runs the probe by itself and quits through EditorApplication.Exit.
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("AnimCulling.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt("AnimCulling.code", 5));
            return;
        }
        if (EditorApplication.isPlaying)
        {
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup - start < WatchdogSeconds) return;
                File.WriteAllText(Path.Combine(output, "watchdog.txt"), "Play mode did not finish\n");
                EditorApplication.Exit(4);
            };
            AnimCullingProbe.finished = code =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("AnimCulling.finished", true);
                SessionState.SetInt("AnimCulling.code", code);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(code);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("AnimCulling.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("AnimCulling.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            try
            {
                EditorSettings.asyncShaderCompilation = false;
                EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
                PlayModeWindow.SetCustomRenderingResolution(192, 108, "AnimCulling");
                PlayModeWindow.SetPlayModeFocused(true);
                Debug.Log("[animculling] entering Play mode, scene views open: " + Resources.FindObjectsOfTypeAll<SceneView>().Length);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(6);
            }
        };
        EditorApplication.update += wait;
    }
}
