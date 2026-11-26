using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class ProbeRunner
{
    /// <summary>Opens the probe's scene (the profile file names another: `first`, the scene whose
    /// wind blows from the first frame; `zones`, the scene of the spherical zones' later cases)
    /// and enters Play mode.</summary>
    public static void Record()
    {
        string profile = File.Exists("SpeedTree8WindProfile.txt") ? File.ReadAllText("SpeedTree8WindProfile.txt").Trim() : "";
        EditorSceneManager.OpenScene(profile == "first" ? "Assets/Scenes/FirstWind.unity" :
            profile == "zones" ? "Assets/Scenes/ZonesWind.unity" : "Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
