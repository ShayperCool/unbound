using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Edit mode: the build settings' scene list. EditorBuildSettingsScene's constructors and helpers;
/// EditorBuildSettings.scenes and globalScenes set to entries made from a path (with forward and
/// back slashes), a GUID, a missing path, a stale path with a known GUID and a GUID alone; what the
/// getters answer after each set (and whether they hand out copies), the build indices SceneManager
/// and SceneUtility answer, sceneListChanged, the file's hash right after each set and after
/// SaveAssets; a null entry, a null list, an empty list; UseParallelAssetBundleBuilding; a list
/// left for the Editor's exit. Then an untitled scene saved without a path. run.py compares the
/// files on disk after the run.
/// </summary>
public class BuildSceneTests
{
    const string File = "ProjectSettings/EditorBuildSettings.asset";
    const string BaseGuid = "e0fd96c04ed5f4230bfb1fc1aa060f13";
    const string SecondGuid = "6f748856d2f6b48bba33779b860ce87a";

    static string Describe(EditorBuildSettingsScene s) =>
        s == null ? "null" : (s.enabled ? "1" : "0") + "|" + (s.path ?? "null") + "|" + s.guid;

    static string List(EditorBuildSettingsScene[] list) => list == null ? "null" : "[" + string.Join("; ", list.Select(Describe)) + "]";

    static string BuildIndices()
    {
        int count = SceneManager.sceneCountInBuildSettings;
        var paths = Enumerable.Range(0, count).Select(SceneUtility.GetScenePathByBuildIndex);
        return count + ":" + string.Join(",", paths) + "|second=" + SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Second.unity")
            + "|base=" + SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Base.unity");
    }

    static int s_Changed;
    static void OnChanged() => s_Changed++;

    static void OnLog(string message, string stack, LogType type) => ProbeLog.Row("log", type + ": " + message);

    static void State(string key)
    {
        ProbeLog.Row(key + ".changed", s_Changed);
        ProbeLog.Row(key + ".file", ProbeLog.Hash(File));
        ProbeLog.Row(key + ".scenes", List(EditorBuildSettings.scenes));
        ProbeLog.Row(key + ".global", List(EditorBuildSettings.globalScenes));
        ProbeLog.Row(key + ".build", BuildIndices());
    }

    [Test]
    public void BuildScenes()
    {
        EditorBuildSettings.sceneListChanged += OnChanged;
        Application.logMessageReceived += OnLog;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            Constructors();
            Lists();
            UntitledSave();
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            Application.logMessageReceived -= OnLog;
            EditorBuildSettings.sceneListChanged -= OnChanged;
        }
    }

    static void Constructors()
    {
        ProbeLog.Row("ctor.default", Describe(new EditorBuildSettingsScene()));
        ProbeLog.Row("ctor.path", Describe(new EditorBuildSettingsScene("Assets/Scenes/Base.unity", true)));
        ProbeLog.Row("ctor.backslash", Describe(new EditorBuildSettingsScene("Assets\\Scenes\\Second.unity", false)));
        ProbeLog.Row("ctor.missing", Describe(new EditorBuildSettingsScene("Assets/Scenes/Missing.unity", true)));
        ProbeLog.Row("ctor.guid", Describe(new EditorBuildSettingsScene(new GUID(SecondGuid), true)));
        ProbeLog.Row("ctor.guidUnknown", Describe(new EditorBuildSettingsScene(new GUID("0123456789abcdef0123456789abcdef"), true)));
        ProbeLog.Try("ctor.nullPath", () => Describe(new EditorBuildSettingsScene(null, true)));
        var set = new EditorBuildSettingsScene("Assets/Scenes/Base.unity", true);
        set.path = "Assets\\Other\\Path.unity";
        ProbeLog.Row("ctor.setPath", Describe(set));
        ProbeLog.Try("ctor.setNullPath", () => { set.path = null; return Describe(set); });

        var list = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Base.unity", true),
            new EditorBuildSettingsScene("", true),
            new EditorBuildSettingsScene("Assets/Scenes/Second.unity", false),
            new EditorBuildSettingsScene { enabled = true },
            new EditorBuildSettingsScene("Assets/Scenes/Missing.unity", true),
        };
        ProbeLog.Row("active", string.Join(",", EditorBuildSettingsScene.GetActiveSceneList(list).Select(p => p ?? "null")));
        ProbeLog.Try("active.null", () => EditorBuildSettingsScene.GetActiveSceneList(null).Length);
        ProbeLog.Try("active.nullEntry", () => EditorBuildSettingsScene.GetActiveSceneList(new EditorBuildSettingsScene[] { null }).Length);
        ProbeLog.Row("compare.ab", Math.Sign(list[0].CompareTo(list[2])));
        ProbeLog.Row("compare.ba", Math.Sign(list[2].CompareTo(list[0])));
        ProbeLog.Row("compare.same", list[0].CompareTo(new EditorBuildSettingsScene("Assets/Scenes/Base.unity", false)));
        ProbeLog.Row("compare.case", Math.Sign(new EditorBuildSettingsScene("Assets/b.unity", true).CompareTo(new EditorBuildSettingsScene("Assets/B.unity", true))));
        ProbeLog.Try("compare.other", () => list[0].CompareTo("Assets/Scenes/Base.unity"));
        ProbeLog.Try("compare.null", () => list[0].CompareTo(null));
    }

    static void Lists()
    {
        // run.py seeds the file with Base.unity under a stale path: what is read, and the same list
        // set again.
        State("start");
        ProjectLogStale();
        ProbeLog.Row("parallel.start", EditorBuildSettings.UseParallelAssetBundleBuilding);

        var base_ = new EditorBuildSettingsScene("Assets/Scenes/Base.unity", true);
        var list = new[]
        {
            base_,
            new EditorBuildSettingsScene("Assets\\Scenes\\Second.unity", false),
            new EditorBuildSettingsScene("Assets/Scenes/Missing.unity", true),
            new EditorBuildSettingsScene { path = "Assets/Old/Moved.unity", guid = new GUID(BaseGuid), enabled = true },
            new EditorBuildSettingsScene { path = "", guid = new GUID(SecondGuid), enabled = true },
        };
        EditorBuildSettings.scenes = list;
        State("set");
        // The list handed in and the entries handed out, changed afterwards.
        base_.enabled = false;
        base_.path = "Assets/Changed.unity";
        list[1] = null;
        ProbeLog.Row("set.afterInputChange", List(EditorBuildSettings.scenes));
        var got = EditorBuildSettings.scenes;
        got[0].enabled = false;
        got[0].path = "Assets/ChangedToo.unity";
        ProbeLog.Row("set.afterOutputChange", List(EditorBuildSettings.scenes));
        ProbeLog.Row("set.sameInstance", ReferenceEquals(EditorBuildSettings.scenes[0], EditorBuildSettings.scenes[0]));
        ProbeLog.Row("set.beforeSave.file", ProbeLog.Hash(File));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("set.saved.file", File);

        // The same list again.
        EditorBuildSettings.scenes = EditorBuildSettings.scenes;
        State("again");

        ProbeLog.Try("nullEntry", () =>
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true), null };
            return "done";
        });
        State("nullEntry");
        ProbeLog.Try("nullList", () => { EditorBuildSettings.scenes = null; return "done"; });
        State("nullList");
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
        State("empty");
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("empty.saved.file", File);

        EditorBuildSettings.globalScenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true) };
        State("global");
        EditorBuildSettings.UseParallelAssetBundleBuilding = true;
        ProbeLog.Row("parallel.set", EditorBuildSettings.UseParallelAssetBundleBuilding);
        ProbeLog.Row("parallel.set.file", ProbeLog.Hash(File));
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("global.saved.file", File);

        // Left for the Editor's exit.
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Base.unity", false),
        };
        EditorBuildSettings.UseParallelAssetBundleBuilding = false;
        State("exit");
    }

    static void ProjectLogStale()
    {
        EditorBuildSettings.scenes = EditorBuildSettings.scenes;
        State("stale");
        AssetDatabase.SaveAssets();
        ProbeLog.Snapshot("stale.saved.file", File);
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
        State("staleCleared");
        AssetDatabase.SaveAssets();
    }

    static void UntitledSave()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Base.unity", OpenSceneMode.Single);
        var untitled = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.MoveGameObjectToScene(new GameObject("In Untitled"), untitled);
        ProbeLog.Row("untitled.dirty", untitled.isDirty);
        ProbeLog.Try("untitled.save", () => EditorSceneManager.SaveScene(untitled));
        ProbeLog.Try("untitled.saveEmpty", () => EditorSceneManager.SaveScene(untitled, ""));
        ProbeLog.Row("untitled.afterSave", untitled.path + "|" + untitled.isDirty);
        EditorSceneManager.MarkSceneDirty(untitled);
        ProbeLog.Try("untitled.saveScenes", () => EditorSceneManager.SaveScenes(new[] { untitled }));
        ProbeLog.Try("untitled.saveOpen", () => EditorSceneManager.SaveOpenScenes());
        ProbeLog.Row("untitled.afterSaveOpen", untitled.path + "|" + untitled.isDirty);
        EditorSceneManager.CloseScene(untitled, true);
    }
}
