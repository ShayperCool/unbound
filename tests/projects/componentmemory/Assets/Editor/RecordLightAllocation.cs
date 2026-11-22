using UnityEditor;
static class RecordLightAllocation
{
    public static void Record() => EditorApplication.isPlaying = true;
}
