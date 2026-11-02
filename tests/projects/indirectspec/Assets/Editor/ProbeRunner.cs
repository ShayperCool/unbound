using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath &lt;copy&gt; -executeMethod ProbeRunner.Record` with
/// <c>INDIRECT_SPEC_OUT</c> set: opens the baked Main scene and enters Play mode, where
/// <see cref="IndirectSpecWitness"/> records and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
