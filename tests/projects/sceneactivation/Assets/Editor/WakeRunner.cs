using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A windowed Editor started with SCENE_ACTIVATION_OUT set opens Wake.unity in Edit mode,
/// records its hierarchy and every marker's identity (instance ids, GlobalObjectIds of its
/// GameObject, Transform and component), then enters Play mode, where <see cref="WakeDriver"/>
/// records both phases. When the driver finishes, the Editor leaves Play mode and quits with 0
/// from Edit mode. A watchdog quits with 4 when Play mode does not finish in time; a failure to
/// open the scene or enter Play mode quits with 6. Nothing here saves a scene or an asset.</summary>
[InitializeOnLoad]
public static class WakeRunner
{
    const double WatchdogSeconds = 600;
    const string ScenePath = "Assets/Scenes/Wake.unity";

    static WakeRunner()
    {
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (!WakeLog.Enabled || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("SceneActivation.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (EditorApplication.isPlaying)
        {
            double start = EditorApplication.timeSinceStartup;
            string output = Environment.GetEnvironmentVariable("SCENE_ACTIVATION_OUT");
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup - start < WatchdogSeconds) return;
                File.WriteAllText(output + ".watchdog", "Play mode did not finish\n");
                EditorApplication.Exit(4);
            };
            WakeDriver.finished = () =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("SceneActivation.finished", true);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!SessionState.GetBool("SceneActivation.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("SceneActivation.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Bind(scene);
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

    /// <summary>Edit mode: the hierarchy, then one record per marker in pre-order.</summary>
    static void Bind(Scene scene)
    {
        WakeLog.Hierarchy("edit", scene, false);
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var marker in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var go = marker.gameObject;
                new WakeLog.Record("bind").Add("scene", scene.name).Add("obj", WakeLog.PathOf(go))
                    .Add("type", marker.GetType().Name)
                    .Add("cid", marker.GetInstanceID()).Add("gid", go.GetInstanceID())
                    .Add("tid", go.transform.GetInstanceID())
                    .Add("go", GlobalObjectId.GetGlobalObjectIdSlow(go).ToString())
                    .Add("tr", GlobalObjectId.GetGlobalObjectIdSlow(go.transform).ToString())
                    .Add("mb", GlobalObjectId.GetGlobalObjectIdSlow(marker).ToString())
                    .Write();
            }
        }
    }
}
