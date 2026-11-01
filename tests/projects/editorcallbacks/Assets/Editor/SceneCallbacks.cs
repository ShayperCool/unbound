using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>The Editor callbacks around entering Play mode and loading scenes: the domain's
/// initialization, [DidReloadScripts], the play mode state, and the scene processing callbacks
/// ([PostProcessScene] and IProcessSceneWithReport) with interleaved callback orders, one of
/// them throwing.</summary>
[InitializeOnLoad]
static class SceneCallbacks
{
    static SceneCallbacks()
    {
        Log("InitializeOnLoad");
        EditorApplication.playModeStateChanged += state =>
            ProbeLog.Event("playModeStateChanged").Add("state", state.ToString()).Frame().Write();
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (message.Contains("probe callback"))
                ProbeLog.Event("log").Add("type", type.ToString()).Add("message", message.Split('\n')[0]).Frame().Write();
        };
    }

    [InitializeOnLoadMethod]
    static void InitializeOnLoadMethod() => Log("InitializeOnLoadMethod");

    [InitializeOnEnterPlayMode]
    static void InitializeOnEnterPlayMode(EnterPlayModeOptions options) => Log("InitializeOnEnterPlayMode");

    [DidReloadScripts]
    static void DidReloadScripts() => Reloaded("default");

    [DidReloadScripts(5)]
    static void DidReloadScripts5() => Reloaded("5");

    [DidReloadScripts(-3)]
    static void DidReloadScriptsMinus3() => Reloaded("-3");

    [DidReloadScripts(0)]
    static void DidReloadScripts0() => Reloaded("0");

    static void Reloaded(string order) =>
        ProbeLog.Event("DidReloadScripts").Add("order", order)
            .Add("willPlay", EditorApplication.isPlayingOrWillChangePlaymode).Frame().Write();

    static void Log(string name) =>
        ProbeLog.Event(name).Add("willPlay", EditorApplication.isPlayingOrWillChangePlaymode).Frame().Write();

    /// <summary>Marks the scene's root behaviours: their Awake shows whether processing came first.</summary>
    [PostProcessScene(-5)]
    static void PostProcessSceneMinus5()
    {
        var scene = Processed("PostProcessScene", "-5");
        foreach (var root in scene.GetRootGameObjects())
            foreach (var marker in root.GetComponentsInChildren<ProbeMarker>(true))
                marker.label += "p";
    }

    [PostProcessScene]
    static void PostProcessSceneDefault() => Processed("PostProcessScene", "default");

    [PostProcessScene(0)]
    static void PostProcessScene0() => Processed("PostProcessScene", "0");

    [PostProcessScene(7)]
    static void PostProcessSceneThrows()
    {
        Processed("PostProcessScene", "7");
        throw new InvalidOperationException("probe callback failure");
    }

    [PostProcessScene(10)]
    static void PostProcessScene10() => Processed("PostProcessScene", "10");

    /// <summary>The scene being processed is the last loaded scene.</summary>
    internal static Scene Processed(string kind, string order)
    {
        var scene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
        var r = ProbeLog.Event(kind).Add("order", order).Add("scene", scene.name).Add("loaded", scene.isLoaded)
            .Add("roots", scene.rootCount).Add("count", SceneManager.sceneCount).Add("active", SceneManager.GetActiveScene().name)
            .Add("building", BuildPipeline.isBuildingPlayer);
        r.Frame().Write();
        return scene;
    }
}

public class SceneProcessorMinus10 : IProcessSceneWithReport
{
    public int callbackOrder => -10;
    public void OnProcessScene(Scene scene, BuildReport report) => SceneProcessors.Log(this, scene, report);
}

public class SceneProcessor3 : IProcessSceneWithReport
{
    public int callbackOrder => 3;
    public void OnProcessScene(Scene scene, BuildReport report) => SceneProcessors.Log(this, scene, report);
}

public class SceneProcessor10 : IProcessSceneWithReport
{
    public int callbackOrder => 10;
    public void OnProcessScene(Scene scene, BuildReport report) => SceneProcessors.Log(this, scene, report);
}

static class SceneProcessors
{
    internal static void Log(IProcessSceneWithReport processor, Scene scene, BuildReport report) =>
        ProbeLog.Event("OnProcessScene").Add("order", processor.callbackOrder).Add("scene", scene.name)
            .Add("loaded", scene.isLoaded).Add("roots", scene.rootCount).Add("count", SceneManager.sceneCount).Add("active", SceneManager.GetActiveScene().name)
            .Add("last", SceneManager.GetSceneAt(SceneManager.sceneCount - 1).name)
            .Add("report", report == null ? "null" : report.GetType().Name).Frame().Write();
}

#pragma warning disable 618
public class LegacySceneProcessor : IProcessScene
{
    public int callbackOrder => 3;
    public void OnProcessScene(Scene scene) =>
        ProbeLog.Event("IProcessScene").Add("order", callbackOrder).Add("scene", scene.name)
            .Add("loaded", scene.isLoaded).Add("roots", scene.rootCount).Frame().Write();
}
#pragma warning restore 618
