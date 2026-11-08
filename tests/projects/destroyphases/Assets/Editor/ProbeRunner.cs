using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) return;
        if (Environment.GetEnvironmentVariable("DESTROY_PHASES_OUT") == null) return;
        if (SessionState.GetBool("DestroyPhases.finished", false))
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Begin;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("DestroyPhases.finished", false)) EditorApplication.Exit(0);
        };
        EditorApplication.update += Tick;
    }
    static void Begin()
    {
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () => {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                view.Focus();
                PlayModeWindow.SetPlayModeFocused(true);
                EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
    static void Tick()
    {
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (!EditorApplication.isPlaying || !DestroyPhases.Finished) return;
        SessionState.SetBool("DestroyPhases.finished", true);
        EditorApplication.ExitPlaymode();
    }
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        var clip = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop };
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, .1f));
        AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = .04f, functionName = "OnAnimationEvent" } });
        AssetDatabase.CreateAsset(clip, "Assets/Resources/Event.anim");
        AssetDatabase.SaveAssets();
    }
}
