using System.Collections.Generic;
using UnityEditor;

/// <summary>Notes the import callbacks of tests/projects/refreshchanged's assets.</summary>
public class RefreshPostprocessor : AssetPostprocessor
{
    static bool Ours(string path) => path.StartsWith("Assets/Data/");

    void OnPreprocessAsset()
    {
        if (Ours(assetPath)) RefreshEvents.Events.Add("preprocess " + assetPath);
    }

    void OnPostprocessTexture(UnityEngine.Texture2D texture)
    {
        if (Ours(assetPath)) RefreshEvents.Events.Add("postprocessTexture " + assetPath + " " + texture.width);
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var ours = new List<string>();
        foreach (var path in imported) if (Ours(path)) ours.Add(path);
        ours.Sort(System.StringComparer.Ordinal);
        if (ours.Count > 0) RefreshEvents.Events.Add("all " + string.Join(",", ours));
    }
}
