using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static void Prepare()
    {
        try {
            Directory.CreateDirectory("Assets/Resources");
            foreach (string key in GeneratorBoundaryProbe.AllCases) {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController", true);
            var mixer = (UnityEngine.Object)type.GetMethod("CreateMixerControllerAtPath", F)
                .Invoke(null, new object[] { "Assets/Resources/" + key + ".mixer" });
            var master = type.GetProperty("masterGroup", F).GetValue(mixer);
            var child = type.GetMethod("CreateNewGroup", F).Invoke(mixer, new object[] { "Child", true });
            type.GetMethod("AddChildToParent", F).Invoke(mixer, new[] { child, master });
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Probe");
            go.AddComponent<AudioListener>();
            go.AddComponent<GeneratorBoundaryProbe>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Probe.unity");
            EditorApplication.Exit(0);
        } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Record()
    {
        try { EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity"); EditorApplication.EnterPlaymode(); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
