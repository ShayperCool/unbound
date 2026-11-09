using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        string path = Environment.GetEnvironmentVariable("GLYPH_RASTER_OUT");
        Directory.CreateDirectory(Path.Combine(path, "../font-inputs"));
        File.WriteAllText(Path.Combine(path, "../font-inputs/legacy.json"), EditorJsonUtility.ToJson(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
        EditorApplication.EnterPlaymode();
    }
}
