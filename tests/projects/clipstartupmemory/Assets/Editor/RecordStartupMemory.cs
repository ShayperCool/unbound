using UnityEditor;

public static class RecordStartupMemory
{
    public static void Record() => EditorApplication.EnterPlaymode();
}
