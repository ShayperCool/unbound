using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ParticleVisibilityRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "Particle visibility");
        PlayModeWindow.SetPlayModeFocused(true);
        EditorApplication.EnterPlaymode();
    }
}
