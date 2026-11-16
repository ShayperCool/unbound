using System;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// The script-bake capture (`script_bake.py`, <c>REALTIME_GI_SCRIPT_BAKE_OUT</c> set):
/// <see cref="Record"/> opens <c>RoomScriptBake</c> (no lighting data, no bake on load) and
/// enters Play mode; as the editor code loads for Play mode (isPlaying still false) this class
/// bakes it with <see cref="Lightmapping.Bake"/>, noting the answer and the events for
/// <see cref="ScriptBakeProbe"/>, which records Play mode.
/// </summary>
[InitializeOnLoad]
public static class ScriptBake
{
    static ScriptBake()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REALTIME_GI_SCRIPT_BAKE_OUT"))) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) return;
        Action started = () => ScriptBakeProbe.EnteringEvents.Add("started");
        Action completed = () => ScriptBakeProbe.EnteringEvents.Add("completed");
        Lightmapping.bakeStarted += started;
        Lightmapping.bakeCompleted += completed;
        ScriptBakeProbe.EnteringIsPlaying = EditorApplication.isPlaying.ToString();
        ScriptBakeProbe.EnteringResult = Lightmapping.Bake().ToString();
        Lightmapping.bakeStarted -= started;
        Lightmapping.bakeCompleted -= completed;
    }

    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/RoomScriptBake.unity");
        EditorApplication.EnterPlaymode();
    }
}
