using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `-executeMethod ProbeRunner.Record` with <c>DEFAULT_REFLECTION_PROBE_OUT</c> set plays the
/// first scene, where <see cref="DefaultReflectionProbe"/> records both scenes and quits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Data.unity");
        EditorApplication.EnterPlaymode();
    }
}
