using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -batchmode -projectPath &lt;staged project&gt; -executeMethod BrgRunner.Record` with
/// <c>BRG_PROBE_OUT</c> set: opens the probe scene and enters Play mode, where <see cref="BrgProbe"/>
/// records the capture and exits the Editor. Shaders compile synchronously, so the first render
/// draws every variant rather than the placeholder of one still compiling.
/// </summary>
public static class BrgRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
