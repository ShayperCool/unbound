using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -projectPath &lt;staged project&gt; -executeMethod ResidentDrawerRunner.Record` (a windowed
/// Editor: the drawer dispatches its changes from the player loop, which a batch-mode Editor does
/// not run between frames) with <c>RESIDENT_DRAWER_OUT</c> set: opens the probe scene and enters
/// Play mode, where <see cref="ResidentDrawerProbe"/> records the capture and exits the Editor.
/// Shaders compile asynchronously, as the Editor's default: a BatchRendererGroup draws from jobs,
/// which cannot compile a variant at once (with synchronous compilation the group draws the error
/// shader); the probe waits until no shader compiles before its scripted frames.
/// </summary>
public static class ResidentDrawerRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = true;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
