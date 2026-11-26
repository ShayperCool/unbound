using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Records, for tests/projects/scenereloadoff (SCENERELOADOFF_OUT), where Play mode's
/// runtime initializers and SceneManager.sceneLoaded fall among the objects' callbacks: the load
/// type or scene, and Edit or Play mode.</summary>
static class PlayModeEntry
{
    static void Line(params string[] fields)
    {
        var path = Environment.GetEnvironmentVariable("SCENERELOADOFF_OUT");
        if (string.IsNullOrEmpty(path)) return;
        File.AppendAllText(path, string.Join("\t", fields) + "\n");
    }

    static string Mode => Application.isPlaying ? "play" : "edit";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void SubsystemRegistration()
    {
        Line("init", "SubsystemRegistration", Mode);
        // Subscribed once per Play mode: the domain may be kept between Play modes.
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
        SceneManager.activeSceneChanged -= ActiveChanged;
        SceneManager.activeSceneChanged += ActiveChanged;
    }

    static void ActiveChanged(Scene from, Scene to) => Line("activeScene", from.name + ":" + to.name, Mode);

    static void Loaded(Scene scene, LoadSceneMode mode) => Line("sceneLoaded", scene.name + ":" + mode, Mode);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void AfterAssembliesLoaded() => Line("init", "AfterAssembliesLoaded", Mode);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void BeforeSplashScreen() => Line("init", "BeforeSplashScreen", Mode);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeSceneLoad() => Line("init", "BeforeSceneLoad", Mode);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad() => Line("init", "AfterSceneLoad", Mode);
}
