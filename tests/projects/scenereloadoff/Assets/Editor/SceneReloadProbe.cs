using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// What entering Play mode does to the open scene's objects under each Enter Play Mode Options
/// setting (see README.md). The Editor's capture runs <c>-executeMethod SceneReloadProbe.Record</c>:
/// it opens Assets/Scenes/Main.unity in Edit mode, then enters Play mode; `unbound run` plays the
/// same scene. Frame 3 of Play mode records whether each template is active, frame 5 leaves the
/// Editor. <c>-executeMethod SceneReloadProbe.Create -quit</c> authors the scene.
/// </summary>
static class SceneReloadProbe
{
    internal const string Scene = "Assets/Scenes/Main.unity";

    internal static void Line(params string[] fields)
    {
        var path = Environment.GetEnvironmentVariable("SCENERELOADOFF_OUT");
        if (string.IsNullOrEmpty(path)) return;
        File.AppendAllText(path, string.Join("\t", fields) + "\n");
    }

    public static void Record()
    {
        Line("record");
        EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        Line("opened", EditorApplication.isPlaying ? "play" : "edit");
        EditorApplication.isPlaying = true;
    }

    static GameObject Make(string name, Type type, bool active, bool enabled, bool template)
    {
        var go = new GameObject(name);
        var c = (Lifecycle)go.AddComponent(type);
        if (template)
        {
            var t = new GameObject(name + "Template");
            t.transform.SetParent(go.transform, false);
            c.template = t;
        }
        c.enabled = enabled;
        go.SetActive(active);
        return go;
    }

    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Make("Always", typeof(AlwaysLifecycle), true, true, true);
        Make("Plain", typeof(PlainLifecycle), true, true, true);
        Make("AlwaysInactive", typeof(AlwaysLifecycle), false, true, true);
        Make("AlwaysDisabled", typeof(AlwaysLifecycle), true, false, true);
        EditorSceneManager.SaveScene(scene, Scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
        AssetDatabase.SaveAssets();
    }
}

[InitializeOnLoad]
static class SceneReloadFrames
{
    static int s_Frames;
    static int s_Loads;

    static SceneReloadFrames()
    {
        // A domain load: counted, so a kept domain shows as one load across Play mode.
        s_Loads++;
        SceneReloadProbe.Line("domain", s_Loads.ToString(), EditorApplication.isPlayingOrWillChangePlaymode ? "play" : "edit");
        EditorApplication.playModeStateChanged += change => SceneReloadProbe.Line("playmode", change.ToString());
        EditorApplication.update += () =>
        {
            if (!EditorApplication.isPlaying) return;
            switch (++s_Frames)
            {
                case 3:
                    foreach (var name in new[] { "Always", "Plain", "AlwaysInactive", "AlwaysDisabled" })
                    {
                        // Find sees active objects only: an inactive template is found through its parent.
                        var parent = GameObject.Find(name);
                        var t = parent != null ? parent.transform.Find(name + "Template") : null;
                        SceneReloadProbe.Line("template", name, parent == null ? "parentInactive" : t == null ? "missing" : t.gameObject.activeSelf ? "active" : "inactive");
                    }
                    break;
                case 5:
                    SceneReloadProbe.Line("exit");
                    EditorApplication.Exit(0);
                    break;
            }
        };
    }
}
