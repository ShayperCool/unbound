using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Enters Play mode in the probe scene, where CoreRemainderProbe records its values and
/// exits the Editor.</summary>
public static class CoreRemainderRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
