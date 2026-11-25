using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Play mode: the build indices of the build settings list run.py seeds (Second disabled, Base,
/// Missing, Second under a stale path, Base disabled, Deep/Third, Base again), with the test runner's scene
/// open as Play mode starts. What sceneCountInBuildSettings, GetScenePathByBuildIndex, GetBuildIndexByScenePath,
/// GetSceneByBuildIndex and CanStreamedLevelBeLoaded answer; which scene LoadScene(int) loads at
/// each index and the build index of scenes loaded by index, by name (and the spellings of a name
/// LoadScene accepts) and by
/// EditorSceneManager.LoadSceneInPlayMode; then the same after EditorBuildSettings.scenes is set in
/// Play mode. The runner's scene path (a generated name) is written as "<runner>".
/// </summary>
public class BuildIndexPlayTests
{
#if UNITY_EDITOR
    const string Base = "Assets/Scenes/Base.unity";
    const string Second = "Assets/Scenes/Second.unity";

    static readonly string[] Lookups =
    {
        Base, Second, "Assets/Scenes/Missing.unity", "Assets/Old/Moved.unity", "assets/scenes/base.unity", "Base",
        "Scenes/Base", "Assets/Scenes/Base", "Assets\\Scenes\\Base.unity", "", null, "Assets/Scenes/Deep/Third.unity", "Third",
        "Deep/Third", "Scenes/Deep/Third", "Deep/Third.unity", "Scenes/Deep/Third.unity",
    };

    static readonly string[] Names = { "Base", "Second", "Missing", Second, "Scenes/Second", "Assets/Scenes/Second", "second", "",
        "Third", "Deep/Third", "Scenes/Deep/Third", "Deep/Third.unity" };

    static string s_Runner;

    static string Clean(string path) => !string.IsNullOrEmpty(s_Runner) && path == s_Runner ? "<runner>" : path;

    static void OnLog(string message, string stack, LogType type) => ProbeLog.Row("log", type + ": " + message.Replace(s_Runner ?? "\0", "<runner>"));

    static string Describe(Scene scene) => scene.IsValid() ? Clean(scene.path) + "|" + scene.buildIndex + "|" + scene.isLoaded : "invalid";

    static void Indices(string key)
    {
        int count = SceneManager.sceneCountInBuildSettings;
        ProbeLog.Row(key + ".count", count);
        for (int i = -1; i <= count; i++)
        {
            int index = i;
            ProbeLog.Try(key + ".path[" + i + "]", () => Clean(SceneUtility.GetScenePathByBuildIndex(index)));
        }
        foreach (var path in Lookups)
        {
            var lookup = path;
            ProbeLog.Try(key + ".index[" + (path ?? "null") + "]", () => SceneUtility.GetBuildIndexByScenePath(lookup));
        }
        ProbeLog.Try(key + ".index[<runner>]", () => SceneUtility.GetBuildIndexByScenePath(s_Runner));
        for (int i = -1; i <= count; i++)
        {
            int index = i;
            ProbeLog.Try(key + ".canLoad[" + i + "]", () => Application.CanStreamedLevelBeLoaded(index));
            ProbeLog.Try(key + ".sceneByIndex[" + i + "]", () => Describe(SceneManager.GetSceneByBuildIndex(index)));
        }
        foreach (var name in Names)
        {
            var lookup = name;
            ProbeLog.Try(key + ".canLoadName[" + name + "]", () => Application.CanStreamedLevelBeLoaded(lookup));
        }
    }

    static void Open(string key)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            ProbeLog.Row(key + ".open[" + i + "]", Describe(SceneManager.GetSceneAt(i)));
    }

    /// <summary>Loads by index, additively; the scenes it added, then unloads them.</summary>
    static IEnumerator LoadIndex(string key, int index)
    {
        int before = SceneManager.sceneCount;
        ProbeLog.Try(key + ".load", () => { SceneManager.LoadScene(index, LoadSceneMode.Additive); return "done"; });
        yield return null;
        for (int i = before; i < SceneManager.sceneCount; i++)
            ProbeLog.Row(key + ".loaded", Describe(SceneManager.GetSceneAt(i)));
        yield return UnloadAdded(before);
    }

    static IEnumerator UnloadAdded(int keep)
    {
        while (SceneManager.sceneCount > keep)
        {
            var op = SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(SceneManager.sceneCount - 1));
            if (op == null) break;
            while (!op.isDone) yield return null;
        }
    }

    [UnityTest]
    public IEnumerator PlayMode()
    {
        Application.logMessageReceived += OnLog;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            yield return null;
            s_Runner = SceneManager.GetActiveScene().path;
            ProbeLog.Row("runner.named", s_Runner.Length > 0);
            ProbeLog.Row("start.scenes", string.Join("; ", EditorBuildSettings.scenes.Select(s => (s.enabled ? "1" : "0") + "|" + s.path)));
            Open("start");
            Indices("start");
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++) yield return LoadIndex("byIndex[" + i + "]", i);

            int keep = SceneManager.sceneCount;
            SceneManager.LoadScene("Second", LoadSceneMode.Additive);
            yield return null;
            SceneManager.LoadScene("Base", LoadSceneMode.Additive);
            yield return null;
            EditorSceneManager.LoadSceneInPlayMode(Second, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            Open("byName");
            yield return UnloadAdded(keep);
            foreach (var name in new[] { "Scenes/Deep/Third", "deep/third", "Deep/Third", "Assets/Scenes/Deep/Third", "Assets/Scenes/Deep/Third.unity", "scenes/deep/third.unity" })
            {
                int before = SceneManager.sceneCount;
                ProbeLog.Try("loadName[" + name + "]", () => { SceneManager.LoadScene(name, LoadSceneMode.Additive); return "done"; });
                yield return null;
                for (int i = before; i < SceneManager.sceneCount; i++)
                    ProbeLog.Row("loadName[" + name + "].loaded", Describe(SceneManager.GetSceneAt(i)));
                yield return UnloadAdded(before);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Second, true), new EditorBuildSettingsScene(Base, false) };
            ProbeLog.Row("set.file", ProbeLog.Hash("ProjectSettings/EditorBuildSettings.asset"));
            Indices("set");
            yield return LoadIndex("set.byIndex[0]", 0);
            yield return LoadIndex("set.byIndex[1]", 1);
            SceneManager.LoadScene("Base", LoadSceneMode.Additive);
            yield return null;
            Open("set.byName");
            yield return UnloadAdded(keep);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            Application.logMessageReceived -= OnLog;
        }
    }
#endif
}
