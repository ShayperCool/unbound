using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class ProbeRunner
{
    /// <summary>Opens the probe's scene (the profile file names another: `first`, the scene whose
    /// wind blows from the first frame) and enters Play mode.</summary>
    public static void Record()
    {
        bool first = File.Exists("SpeedTree8WindProfile.txt") && File.ReadAllText("SpeedTree8WindProfile.txt").Trim() == "first";
        EditorSceneManager.OpenScene(first ? "Assets/Scenes/FirstWind.unity" : "Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
