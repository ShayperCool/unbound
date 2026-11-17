using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Enters Play mode in the probe scene, where ForceCrashProbe crashes the Editor as its environment asks and
/// exits the Editor.</summary>
public static class ForceCrashRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
