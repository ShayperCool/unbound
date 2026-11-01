// noEngineReferences: no engine module at all.
public static class NoEngineUses
{
    public static void Use() => UnityEngine.ScreenCapture.CaptureScreenshot("noengine.png");
}
