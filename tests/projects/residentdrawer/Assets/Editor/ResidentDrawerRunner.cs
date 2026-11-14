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
/// Play mode starts once the Editor has finished its start-up work, as when a user presses Play
/// in an open Editor: entered at once, the first Play mode frames still see that work (in this
/// project the switch to its gamma colour space and URP's re-import of every material), which
/// reloads every material and so has URP's ShaderGUI validate the materials the probe made at
/// run time (a transparent Lit material then blends premultiplied: source factor One with
/// _ALPHAPREMULTIPLY_ON). The wait is kept in the session, so a domain reload during it resumes it.
/// </summary>
public static class ResidentDrawerRunner
{
    // Measured in this project: 300 Editor updates after opening the scene are enough for the
    // start-up work above; the wait also needs no compilation or asset import in progress.
    const int k_StartUpUpdates = 300;
    const float k_StartUpSeconds = 3f;
    const string k_Waiting = "ResidentDrawerRunner.Waiting", k_Updates = "ResidentDrawerRunner.Updates", k_Start = "ResidentDrawerRunner.Start";

    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = true;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        SessionState.SetBool(k_Waiting, true);
        SessionState.SetInt(k_Updates, 0);
        SessionState.SetFloat(k_Start, (float)EditorApplication.timeSinceStartup);
        Resume();
    }

    [InitializeOnLoadMethod]
    static void Resume()
    {
        if (!SessionState.GetBool(k_Waiting, false)) return;
        EditorApplication.update -= WaitForStartUp;
        EditorApplication.update += WaitForStartUp;
    }

    static void WaitForStartUp()
    {
        int updates = SessionState.GetInt(k_Updates, 0) + 1;
        SessionState.SetInt(k_Updates, updates);
        if (updates < k_StartUpUpdates || EditorApplication.timeSinceStartup - SessionState.GetFloat(k_Start, 0f) < k_StartUpSeconds
            || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        EditorApplication.update -= WaitForStartUp;
        SessionState.SetBool(k_Waiting, false);
        EditorApplication.EnterPlaymode();
    }
}
