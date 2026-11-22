using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Unbound.GameDrive
{
    /// <summary>The Editor's entry of a driven run (<c>-executeMethod Unbound.GameDrive.GameDriveRun.Run</c>):
    /// opens GAMEDRIVE_SCENE when given, sets the Game view to GAMEDRIVE_SIZE (WIDTHxHEIGHT) when
    /// given, and enters Play mode. The driver itself starts from GAMEDRIVE_OUT.</summary>
    public static class GameDriveRun
    {
        public static void Run()
        {
            var scene = Environment.GetEnvironmentVariable("GAMEDRIVE_SCENE");
            if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene);
            var size = (Environment.GetEnvironmentVariable("GAMEDRIVE_SIZE") ?? "").Split('x');
            if (size.Length == 2 && uint.TryParse(size[0], out uint width) && uint.TryParse(size[1], out uint height))
            {
                PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
                PlayModeWindow.SetCustomRenderingResolution(width, height, "gamedrive");
            }
            EditorApplication.EnterPlaymode();
        }
    }
}
