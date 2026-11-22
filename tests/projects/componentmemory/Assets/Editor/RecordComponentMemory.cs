using UnityEditor;

static class RecordComponentMemory
{
    public static void Record() => EditorApplication.isPlaying = true;
}
