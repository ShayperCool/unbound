// overrideReferences chooses the precompiled plugins; the engine's modules stay as for any runtime
// assembly.
public static class OverrideUses
{
    public static void Use() => UnityEngine.ScreenCapture.CaptureScreenshot("override.png");
}
