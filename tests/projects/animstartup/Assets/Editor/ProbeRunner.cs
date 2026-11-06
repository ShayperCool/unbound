using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> (batchmode) writes the probe's clips, controllers and empty scene. With
/// <c>ANIMSTARTUP_PROBE_OUT</c> set, a windowed Editor (an X server of its own, the GPU through
/// VirtualGL) closes every Scene view (a Scene view camera also makes renderers visible), opens
/// the probe scene at a 192x108 Game view, enters Play mode and quits when
/// <see cref="AnimStartupProbe"/> has written its record (nonzero on failure). A watchdog quits
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
        var clip = new AnimationClip { name = "Startup", frameRate = 50f };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Marker", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0f, 2f, 1f, 4f));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y"), AnimationCurve.Linear(0f, 0f, 1f, 1f));
        AnimationUtility.SetAnimationEvents(clip, new[] {
            new AnimationEvent { time = 0f, functionName = "AtZero" },
            new AnimationEvent { time = 0.01f, functionName = "AtEarly" },
            new AnimationEvent { time = 0.04f, functionName = "AtLater" } });
        AssetDatabase.CreateAsset(clip, "Assets/Resources/Startup.anim");
        var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/Resources/Startup.controller");
        var state = controller.layers[0].stateMachine.AddState("Run");
        state.motion = clip;
        controller.layers[0].stateMachine.defaultState = state;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var driver = new GameObject("Driver").AddComponent<AnimStartupProbe>();
        driver.sceneEnabled = Rig("scene/enabled", controller, true);
        driver.sceneDisabled = Rig("scene/disabled", controller, false);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Driver").AddComponent<AnimStartupProbe>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Delayed.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true), new EditorBuildSettingsScene("Assets/Scenes/Delayed.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static Animator Rig(string name, RuntimeAnimatorController controller, bool enabled)
    {
        var go = new GameObject(name);
        new GameObject("Marker").transform.SetParent(go.transform, false);
        go.AddComponent<StartupReceiver>();
        var animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.enabled = enabled;
        animator.applyRootMotion = true;
        return animator;
    }

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("ANIMSTARTUP_PROBE_OUT");
        // Unbound runs the probe by itself and quits through EditorApplication.Exit.
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("AnimStartup.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt("AnimStartup.code", 5));
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
            AnimStartupProbe.finished = code =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("AnimStartup.finished", true);
                SessionState.SetInt("AnimStartup.code", code);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(code);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("AnimStartup.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("AnimStartup.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            try
            {
                EditorSettings.asyncShaderCompilation = false;
                EditorSceneManager.OpenScene(Environment.GetEnvironmentVariable("ANIMSTARTUP_DELAYED") == "1" ? "Assets/Scenes/Delayed.unity" : "Assets/Scenes/Probe.unity");
                foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
                PlayModeWindow.SetCustomRenderingResolution(192, 108, "AnimStartup");
                PlayModeWindow.SetPlayModeFocused(true);
                Debug.Log("[animstartup] entering Play mode, scene views open: " + Resources.FindObjectsOfTypeAll<SceneView>().Length);
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
