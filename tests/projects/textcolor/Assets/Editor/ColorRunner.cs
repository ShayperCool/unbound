using System;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Opens the probe scene and enters Play mode, where TextColorProbe records the fonts and
/// exits the Editor.</summary>
public static class ColorRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
