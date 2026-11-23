using UnityEditor;
using UnityEditor.SceneManagement;

static class RecordTransformMemory
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.isPlaying = true;
    }
}
