using UnityEditor;
using UnityEditor.SceneManagement;

public static class ApvLightingRunner
{
    /// <summary>The editor updates let pass before playing: a new project's first ones bring
    /// the render pipeline's materials up to date.</summary>
    const int SettleUpdates = 5;

    static int s_Updates;

    /// <summary>Opens the baked scene, then plays it: the probe records.</summary>
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Lighting.unity");
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
