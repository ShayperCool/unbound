using UnityEditor;
using UnityEngine;

/// <summary>Logs a warning when a texture whose name contains "Logged" is imported, as package
/// postprocessors do (the 2D Animation package's sprite outline warning, say).</summary>
public class LoggingPostprocessor : AssetPostprocessor
{
    void OnPostprocessTexture(Texture2D texture)
    {
        if (assetPath.Contains("Logged")) Debug.LogWarning("importlogs-postprocess " + assetPath);
    }
}
