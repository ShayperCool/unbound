using UnityEditor;
using UnityEditor.SceneManagement;

static class RecordSpriteMemory
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.isPlaying = true;
    }
}
