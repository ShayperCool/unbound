using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod RealtimeGIRunner.Record` with <c>REALTIME_GI_OUT</c> set: opens the
/// first scene and enters Play mode, where <see cref="RealtimeGIProbe"/> records and exits.
/// `RealtimeGIRunner.RecordBake` (with <c>REALTIME_GI_BAKE</c> set too): opens <c>RoomBake</c>,
/// which asks for a bake when it opens, and enters Play mode once that bake is done (entering
/// it during the bake would stop the bake).</summary>
public static class RealtimeGIRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Room.unity");
        EditorApplication.EnterPlaymode();
    }

    public static void RecordBake()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/RoomBake.unity");
        double start = EditorApplication.timeSinceStartup;
        int updates = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            updates++;
            bool done = updates > 30 && !Lightmapping.isRunning && Lightmapping.lightingDataAsset != null;
            if (!done && EditorApplication.timeSinceStartup - start < 900) return;
            if (!done) UnityEngine.Debug.LogError("RoomBake: the bake on load did not finish");
            EditorApplication.update -= wait;
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
}
