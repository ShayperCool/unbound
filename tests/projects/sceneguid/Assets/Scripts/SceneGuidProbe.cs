using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Writes the GUID each kind of scene reports (Scene.guid, internal: what URP's probe volume system
/// finds a scene's baking set by) into values.jsonl in $SCENE_GUID_OUT (tests/value_probe.py): the
/// first scene, a scene loaded additively by build index, a scene made at runtime, the
/// DontDestroyOnLoad scene and an invalid scene.
/// </summary>
public class SceneGuidProbe : MonoBehaviour
{
    static string s_Directory;
    static StreamWriter output;
    static readonly PropertyInfo s_Guid = typeof(Scene).GetProperty("guid", BindingFlags.Instance | BindingFlags.NonPublic);

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    static string Guid(Scene scene)
    {
        try { return (string)s_Guid.GetValue(scene) ?? "null"; }
        catch (TargetInvocationException e) { return "throws " + e.InnerException?.GetType().Name; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        s_Directory = Environment.GetEnvironmentVariable("SCENE_GUID_OUT");
        if (string.IsNullOrEmpty(s_Directory)) return;
        var go = new GameObject("probe");
        go.AddComponent<SceneGuidProbe>();
        SceneManager.LoadScene(1, LoadSceneMode.Additive);
    }

    void Update()
    {
        if (Time.frameCount < 3) return;
        enabled = false;
        try
        {
            Directory.CreateDirectory(s_Directory);
            using (output = new StreamWriter(Path.Combine(s_Directory, "values.jsonl")))
            {
                Row("first", Guid(SceneManager.GetSceneByBuildIndex(0)));
                Row("additive", Guid(SceneManager.GetSceneByBuildIndex(1)));
                Row("created", Guid(SceneManager.CreateScene("Created")));
                var kept = new GameObject("kept");
                DontDestroyOnLoad(kept);
                Row("dontDestroyOnLoad", Guid(kept.scene));
                Row("invalid", Guid(default(Scene)));
                Row("first.path", SceneManager.GetSceneByBuildIndex(0).path);
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
