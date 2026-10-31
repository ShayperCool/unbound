using UnityEditor;
public static class TransitionInlineRunner
{
    public static void Record() { TransitionInlineProbe.Run(); EditorApplication.Exit(0); }
}
