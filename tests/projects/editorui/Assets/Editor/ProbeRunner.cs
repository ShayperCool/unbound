using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `-executeMethod ProbeRunner.Create` saves the empty probe scene; `-executeMethod ProbeRunner.Record`
/// with <c>EDITORUI_PROBE_OUT</c> set plays it, where <see cref="EditorUIProbe"/> runs.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
