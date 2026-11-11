using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ProbeRunner
{
    static string output;
    static int ticks, packageUpdates;
    static MonoBehaviour packageHooks;
    static readonly List<CallbackCounter> counters = new List<CallbackCounter>();
    static string[] names = { "edit", "always", "generic", "plain", "direct", "hidden", "disabled", "inactive" };
    static ProbeRunner()
    {
        output = Environment.GetEnvironmentVariable("EDIT_INHERITANCE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        if (Environment.GetEnvironmentVariable("EDIT_INHERITANCE_STARTUP") == "1")
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) CreateForPlay();
            else if (typeof(MonoBehaviour).Assembly.GetType("Unbound.Runtime.EditorSession") == null)
                EditorApplication.delayCall += () =>
                {
                    EditorSceneManager.OpenScene("Assets/Scenes/Empty.unity");
                    EditorApplication.EnterPlaymode();
                };
            return;
        }
        int warmup = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () => { if (++warmup < 30) return; EditorApplication.update -= wait; Begin(); };
        EditorApplication.update += wait;
    }
    static void Put(string key, string value)
    {
        File.AppendAllText(output, "{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");
    }
    static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.WriteAllText(output, "");
        if (Environment.GetEnvironmentVariable("EDIT_INHERITANCE_TRANSITION") == "1")
        {
            CreateTransition();
            return;
        }
        if (Environment.GetEnvironmentVariable("EDIT_INHERITANCE_PACKAGES") == "1") InstallPackageHook();
        var types = new[] { typeof(EditDerived), typeof(AlwaysDerived), typeof(GenericDerived), typeof(PlainCounter), typeof(DirectCounter) };
        foreach (var type in types)
            foreach (var attr in new[] { typeof(ExecuteInEditMode), typeof(ExecuteAlways) })
                foreach (bool inherit in new[] { false, true })
                {
                    string key = "reflection." + type.Name + "." + attr.Name + "." + inherit;
                    Put(key + ".defined", type.IsDefined(attr, inherit).ToString());
                    Put(key + ".attributes", type.GetCustomAttributes(attr, inherit).Length.ToString());
                }
        string[] attributeNames = { "UnityEngine.SerializeField", "UnityEngine.SerializeReference", "UnityEngine.HideInInspector", "UnityEngine.RequireComponent", "UnityEngine.DisallowMultipleComponent", "UnityEngine.ExecuteInEditMode", "UnityEngine.ExecuteAlways", "UnityEngine.DefaultExecutionOrder", "UnityEngine.AddComponentMenu", "UnityEngine.CreateAssetMenuAttribute", "UnityEngine.ContextMenu", "UnityEngine.ContextMenuItemAttribute", "UnityEngine.HelpURLAttribute", "UnityEngine.SelectionBaseAttribute", "UnityEngine.ExcludeFromPresetAttribute" };
        foreach (string name in attributeNames)
        {
            var type = typeof(MonoBehaviour).Assembly.GetType(name, true);
            var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(type, typeof(AttributeUsageAttribute), true);
            Put("usage." + name, ((int)usage.ValidOn) + "," + usage.Inherited + "," + usage.AllowMultiple);
        }
        for (int i = 0; i < names.Length; i++)
        {
            var go = new GameObject(names[i]);
            if (i == 5) go.hideFlags = HideFlags.HideAndDontSave;
            if (i >= 6) go.SetActive(false);
            var counter = (CallbackCounter)go.AddComponent(i < types.Length ? types[i] : typeof(PlainCounter));
            if (i == 6) { counter.enabled = false; go.SetActive(true); }
            counters.Add(counter);
        }
        EditorApplication.update += Tick;
    }
    static void Snapshot(string key)
    {
        for (int i = 0; i < counters.Count; i++) Put(key + "." + names[i], string.Join(",", counters[i].calls));
    }
    static void Clear() { foreach (var counter in counters) counter.calls.Clear(); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (++ticks < 12) return;
        ticks = 0;
        Snapshot("edit");
        if (packageHooks != null)
        {
            Put("addressables.updated", (packageUpdates > 0).ToString());
            Put("addressables.editMode", (!Application.isPlaying).ToString());
            Put("addressables.flags", ((int)packageHooks.gameObject.hideFlags).ToString());
        }
        Clear();
        for (int i = 0; i < 5; i++) counters[i].enabled = false;
        Snapshot("disable");
        Clear();
        for (int i = 0; i < 5; i++) counters[i].enabled = true;
        Snapshot("reenable");
        Put("complete", "1");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    static void CreateTransition()
    {
        names = new[] { "plain", "disabled", "inactive" };
        for (int i = 0; i < names.Length; i++)
        {
            var go = new GameObject(names[i]);
            if (i != 0) go.SetActive(false);
            var counter = go.AddComponent<PlainCounter>();
            if (i == 1) { counter.enabled = false; go.SetActive(true); }
            counters.Add(counter);
        }
        Snapshot("edit");
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += TransitionTick;
        EditorApplication.EnterPlaymode();
    }
    static void TransitionTick()
    {
        if (!Application.isPlaying || ++ticks < 12) return;
        Snapshot("play");
        Clear();
        counters[1].enabled = true;
        counters[2].gameObject.SetActive(true);
        EditorApplication.update -= TransitionTick;
        EditorApplication.update += Finish;
        ticks = 0;
    }

    static void InstallPackageHook()
    {
        Type type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            type = type ?? assembly.GetType("MonoBehaviourCallbackHooks");
        if (type == null) throw new InvalidOperationException("Addressables callback hook type not loaded");
        packageHooks = (MonoBehaviour)type.BaseType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        Action<float> callback = _ => packageUpdates++;
        var field = type.GetField("m_OnUpdateDelegate", BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(packageHooks, Delegate.Combine((Delegate)field.GetValue(packageHooks), callback));
    }

    static void CreateForPlay()
    {
        File.WriteAllText(output, "");
        if (Environment.GetEnvironmentVariable("EDIT_INHERITANCE_PACKAGES") == "1")
        {
            InstallPackageHook();
            Put("addressables.created.editmode", (!Application.isPlaying).ToString());
            Put("addressables.flags", ((int)packageHooks.gameObject.hideFlags).ToString());
        }
        Put("created.isPlaying", Application.isPlaying.ToString());
        Put("created.willChange", EditorApplication.isPlayingOrWillChangePlaymode.ToString());
        Put("created.scene.name", SceneManager.GetActiveScene().name);
        Put("created.scene.loaded", SceneManager.GetActiveScene().isLoaded.ToString());
        for (int i = 0; i < names.Length; i++)
        {
            var go = new GameObject(names[i]);
            if (i == 5) go.hideFlags = HideFlags.HideAndDontSave;
            if (i >= 6) go.SetActive(false);
            var counter = go.AddComponent<PlainCounter>();
            if (i == 6) { counter.enabled = false; go.SetActive(true); }
            counters.Add(counter);
        }
        Snapshot("created");
        EditorApplication.update += FinishPlay;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeSceneLoad()
    {
        if (counters.Count == 0 || Environment.GetEnvironmentVariable("EDIT_INHERITANCE_STARTUP") != "1") return;
        Snapshot("beforeSceneLoad");
        // Keep these objects through the initial scene load, as a package singleton does.
        foreach (var counter in counters) Object.DontDestroyOnLoad(counter.gameObject);
    }
    static void FinishPlay()
    {
        if (!Application.isPlaying || ++ticks < 12) return;
        Snapshot("play");
        Put("play.scene.rootCount", SceneManager.GetActiveScene().rootCount.ToString());
        var after = new GameObject("AfterStartupSingletons");
        Put("play.after.sibling", after.transform.GetSiblingIndex().ToString());
        Object.DestroyImmediate(after);
        if (packageHooks != null) Put("addressables.updated", (packageUpdates > 0).ToString());
        Clear();
        counters[6].enabled = true;
        counters[7].gameObject.SetActive(true);
        EditorApplication.update -= FinishPlay;
        EditorApplication.update += Finish;
        ticks = 0;
    }
    static void Finish()
    {
        if (++ticks < 12) return;
        Snapshot("enabled");
        Put("complete", "1");
        EditorApplication.update -= Finish;
        EditorApplication.Exit(0);
    }
}
