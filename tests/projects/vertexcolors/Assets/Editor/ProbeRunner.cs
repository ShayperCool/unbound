using UnityEditor;
using UnityEditor.SceneManagement;

public static class ProbeRunner
{
    /// <summary>Editor updates let pass before playing: the render pipeline package brings a new
    /// project up to date in the first ones.</summary>
    const int SettleUpdates = 5;

    static int s_Updates;

    /// <summary>Plays the probe's scene: the probe records and exits.</summary>
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        s_Updates = 0;
        EditorApplication.update += PlayOnceSettled;
    }

    static void PlayOnceSettled()
    {
        if (++s_Updates < SettleUpdates) return;
        EditorApplication.update -= PlayOnceSettled;
        EditorApplication.EnterPlaymode();
    }
}
