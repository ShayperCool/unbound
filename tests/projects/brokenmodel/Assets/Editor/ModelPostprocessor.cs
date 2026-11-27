using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Notes the model import callbacks of tests/projects/brokenmodel's models.</summary>
public class ModelPostprocessor : AssetPostprocessor
{
    static bool Ours(string path) => path.StartsWith("Assets/Models/");

    void OnPreprocessAsset()
    {
        if (Ours(assetPath)) ModelEvents.Events.Add("preprocessAsset " + assetPath);
    }

    void OnPreprocessModel()
    {
        if (Ours(assetPath)) ModelEvents.Events.Add("preprocessModel " + assetPath);
    }

    void OnPostprocessModel(GameObject root)
    {
        if (Ours(assetPath))
            ModelEvents.Events.Add("postprocessModel " + assetPath + " " + (root == null ? "null" : root.name + " children " + root.transform.childCount));
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var ours = new List<string>();
        foreach (var path in imported) if (Ours(path)) ours.Add(path);
        ours.Sort(System.StringComparer.Ordinal);
        if (ours.Count > 0) ModelEvents.Events.Add("all " + string.Join(",", ours));
    }
}
