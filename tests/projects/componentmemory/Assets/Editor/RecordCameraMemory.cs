using UnityEditor;
using UnityEditor.SceneManagement;
static class RecordCameraMemory
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.isPlaying = true;
    }
}
