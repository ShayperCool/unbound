using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Loads an old serialized component alongside two enabled-only controls.
[InitializeOnLoad]
public static class LegacyComponents
{
    static LegacyComponents()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || Environment.GetEnvironmentVariable("LEGACY_COMPONENTS_OUT") == null) return;
            Record("play");
            EditorApplication.Exit(0);
        };
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/legacy.unity", OpenSceneMode.Single);
        Record("edit");
        EditorApplication.EnterPlaymode();
    }

    static void Record(string phase)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects().OrderBy(g => g.name))
        {
        var components = go.GetComponents<Component>();
        var names = components.Select(c => c == null ? "null" : c.GetType().FullName + (c is Behaviour b ? ":" + b.enabled.ToString().ToLowerInvariant() : ""));
        File.AppendAllText(Environment.GetEnvironmentVariable("LEGACY_COMPONENTS_OUT"), phase + "." + go.name + "=" + string.Join(",", names) + "\n");
        }
    }
}
