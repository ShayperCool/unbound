using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>The Editor's entry (-executeMethod): plays the probe's scene, where SpeedTree8Probe records.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
