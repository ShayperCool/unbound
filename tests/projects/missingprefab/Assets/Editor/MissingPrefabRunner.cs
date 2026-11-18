using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Records the probe scene and prefabs in Edit mode, then in Play mode, and quits.
[InitializeOnLoad]
public static class MissingPrefabRunner
{
    const string Outer = "Assets/Probe/Outer.prefab";
    const string Variant = "Assets/Probe/InnerVariant.prefab";
    static string phase = "startup";
    // Import messages arrive in the Editor's import order, which is not part of the contract.
    static readonly System.Collections.Generic.List<string> startup = new System.Collections.Generic.List<string>();

    static MissingPrefabRunner()
    {
        if (Environment.GetEnvironmentVariable("MISSING_PREFAB_OUT") == null) return;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (message.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) < 0) return;
            string first = message.Split('\n')[0];
            if (phase == "startup") { startup.Add(type + " " + first); return; }
            ProbeLog.Put("log/" + phase + "#" + ProbeLog.Next("log/" + phase), type + " " + first);
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) WaitThen(3, Play);
            if (state == PlayModeStateChange.EnteredEditMode && phase == "play")
            {
                ProbeLog.Put("complete", "1");
                EditorApplication.Exit(0);
            }
        };
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        WaitThen(30, Edit);
    }

    static void WaitThen(int frames, Action action)
    {
        int n = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++n < frames) return;
            EditorApplication.update -= wait;
            action();
        };
        EditorApplication.update += wait;
    }

    static void Scene(string context)
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        ProbeLog.Put(context + "/rootCount", roots.Length.ToString());
        foreach (var root in roots) ProbeLog.Tree(context, root);
        var holder = GameObject.Find("SceneHolder");
        ProbeLog.Refs(context + "/SceneHolder", holder == null ? null : holder.GetComponent<Holder>());
    }

    static void Edit()
    {
        startup.Sort(StringComparer.Ordinal);
        ProbeLog.Put("log/startup", string.Join(" | ", startup));
        phase = "edit-open";
        EditorSceneManager.OpenScene("Assets/Probe/Probe.unity");
        phase = "edit";
        Scene("edit-scene");
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                ProbeLog.Put("edit-prefab/" + ProbeLog.Path(t), "missing=" + (PrefabUtility.IsPrefabAssetMissing(t.gameObject) ? 1 : 0)
                    + " status=" + PrefabUtility.GetPrefabInstanceStatus(t.gameObject)
                    + " part=" + (PrefabUtility.IsPartOfPrefabInstance(t.gameObject) ? 1 : 0)
                    + " root=" + (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) ? 1 : 0));
        var outer = AssetDatabase.LoadAssetAtPath<GameObject>(Outer);
        ProbeLog.Tree("edit-asset", outer);
        ProbeLog.Refs("edit-asset/Holder", outer == null ? null : outer.GetComponent<Holder>());
        ProbeLog.Tree("edit-variant", AssetDatabase.LoadAssetAtPath<GameObject>(Variant));
        phase = "edit-instantiate";
        var instance = outer == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(outer);
        ProbeLog.Tree("edit-instantiate", instance);
        ProbeLog.Refs("edit-instantiate/Holder", instance == null ? null : instance.GetComponent<Holder>());
        if (instance != null) Object.DestroyImmediate(instance);
        phase = "enter-play";
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.EnterPlaymode();
    }

    static void Play()
    {
        phase = "play";
        Scene("play-scene");
        var holder = GameObject.Find("SceneHolder")?.GetComponent<Holder>();
        var spawn = holder == null ? null : holder.spawn;
        var clone = spawn == null ? null : Object.Instantiate(spawn);
        ProbeLog.Tree("play-instantiate", clone);
        ProbeLog.Refs("play-instantiate/Holder", clone == null ? null : clone.GetComponent<Holder>());
        var variant = AssetDatabase.LoadAssetAtPath<GameObject>(Variant);
        ProbeLog.Tree("play-variant", variant == null ? null : Object.Instantiate(variant));
        // A runtime load of the scene, additively.
        phase = "play-load";
        var load = SceneManager.LoadSceneAsync("Probe", LoadSceneMode.Additive);
        load.completed += _ =>
        {
            phase = "play";
            EditorApplication.ExitPlaymode();
        };
    }
}
