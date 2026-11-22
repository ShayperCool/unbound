using UnityEditor;

/// <summary>The Editor's entry of the probe: Play mode, where DriveProbe builds its UI and runs
/// the driver (see README.md).</summary>
public static class GameDriveProbe
{
    public static void Record() => EditorApplication.EnterPlaymode();
}
