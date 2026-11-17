using UnityEditor;
using UnityEditor.SceneManagement;

public static class ProbeRunner
{
    /// <summary>The editor updates let pass before playing. In the first one the packages bring
    /// a new project up to date: the graphs, and the render pipeline's materials with what
    /// depends on them, are imported again where the project settings name older versions.
    /// Entered at once, play would have that import between its first and second frame, where
    /// it makes every effect anew from what was saved.</summary>
    const int SettleUpdates = 5;

    static int s_Updates;

    /// <summary>Authors the graphs and the scene, then plays the scene: the probe records.</summary>
    public static void Record()
    {
        try
        {
            BoundsAuthoring.Author();
            EditorSceneManager.OpenScene(BoundsAuthoring.Scene);
        }
        catch (System.Exception e)
        {
            // Nothing to play: the capture ends here, without values.
            UnityEngine.Debug.LogError("vfxbounds probe: authoring failed: " + e);
            EditorApplication.Exit(1);
            return;
        }
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
