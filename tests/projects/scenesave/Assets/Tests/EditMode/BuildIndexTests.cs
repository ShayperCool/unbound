using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Edit mode: the build indices of a build settings list with disabled, missing, stale and repeated
/// entries (run.py seeds it: Second disabled, Base, Missing, Second under a stale path, Base
/// disabled, Deep/Third). What SceneManager.sceneCountInBuildSettings, SceneUtility.GetScenePathByBuildIndex
/// and GetBuildIndexByScenePath (paths, other spellings, names), SceneManager.GetSceneByBuildIndex
/// and Application.CanStreamedLevelBeLoaded answer; the build index of scenes the Editor opens, of
/// a new scene and of a scene outside the list; then the same after the list is set to other lists
/// (enabled scenes reordered, all disabled, empty) while the scenes stay open, and of a scene
/// opened again after the change.
/// </summary>
public class BuildIndexTests
{
    const string Base = "Assets/Scenes/Base.unity";
    const string Second = "Assets/Scenes/Second.unity";

    static readonly string[] Lookups =
    {
        Base, Second, "Assets/Scenes/Missing.unity", "Assets/Old/Moved.unity", "assets/scenes/base.unity", "Base",
        "Scenes/Base", "Assets/Scenes/Base", "Assets\\Scenes\\Base.unity", "", null, "Assets/Scenes/Deep/Third.unity", "Third",
        "Deep/Third", "Scenes/Deep/Third", "Deep/Third.unity", "Scenes/Deep/Third.unity",
    };

    static readonly string[] Names = { "Base", "Second", "Missing", Second, "Assets/Scenes/Missing.unity", "Moved", "" };

    static void OnLog(string message, string stack, LogType type) => ProbeLog.Row("log", type + ": " + message);

    static void Indices(string key)
    {
        int count = SceneManager.sceneCountInBuildSettings;
        ProbeLog.Row(key + ".count", count);
        for (int i = -1; i <= count; i++)
        {
            int index = i;
            ProbeLog.Try(key + ".path[" + i + "]", () => SceneUtility.GetScenePathByBuildIndex(index));
        }
        foreach (var path in Lookups)
        {
            var lookup = path;
            ProbeLog.Try(key + ".index[" + (path ?? "null") + "]", () => SceneUtility.GetBuildIndexByScenePath(lookup));
        }
        for (int i = -1; i <= count; i++)
        {
            int index = i;
            ProbeLog.Try(key + ".canLoad[" + i + "]", () => Application.CanStreamedLevelBeLoaded(index));
        }
        foreach (var name in Names)
        {
            var lookup = name;
            ProbeLog.Try(key + ".canLoadName[" + name + "]", () => Application.CanStreamedLevelBeLoaded(lookup));
        }
        for (int i = -1; i <= count; i++)
        {
            int index = i;
            ProbeLog.Try(key + ".sceneByIndex[" + i + "]", () => Describe(SceneManager.GetSceneByBuildIndex(index)));
        }
    }

    static string Describe(Scene scene) => scene.IsValid() ? scene.path + "|" + scene.buildIndex + "|" + scene.isLoaded : "invalid";

    static void Open(string key)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            ProbeLog.Row(key + ".open[" + i + "]", (scene.path.Length > 0 ? scene.path : "untitled:" + scene.name) + "|" + scene.buildIndex);
        }
    }

    [Test]
    public void EditMode()
    {
        Application.logMessageReceived += OnLog;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            ProbeLog.Row("start.scenes", string.Join("; ", EditorBuildSettings.scenes.Select(s => (s.enabled ? "1" : "0") + "|" + s.path)));
            Indices("start");

            EditorSceneManager.OpenScene(Base, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(Second, OpenSceneMode.Additive);
            var untitled = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            ProbeLog.Row("untitled.buildIndex", untitled.buildIndex);
            Open("opened");
            Indices("opened");

            // The enabled scenes reordered, a disabled one first.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Base, false),
                new EditorBuildSettingsScene(Second, true),
                new EditorBuildSettingsScene(Base, true),
            };
            Open("reordered");
            Indices("reordered");
            EditorSceneManager.OpenScene(Base, OpenSceneMode.Single);
            Open("reopened");

            EditorSceneManager.OpenScene(Second, OpenSceneMode.Additive);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Second, false), new EditorBuildSettingsScene(Base, false) };
            Open("disabled");
            Indices("disabled");
            EditorSceneManager.OpenScene(Second, OpenSceneMode.Single);
            Open("disabledReopened");

            EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
            Open("empty");
            Indices("empty");
            EditorSceneManager.OpenScene(Base, OpenSceneMode.Single);
            Open("emptyReopened");
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            Application.logMessageReceived -= OnLog;
        }
    }
}
