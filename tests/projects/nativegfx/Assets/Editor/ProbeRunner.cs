using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Records the probe: a batchmode Editor on the GPU opens the probe scene and enters
/// Play mode; <c>NativeGfxProbe</c> writes its record and quits.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
