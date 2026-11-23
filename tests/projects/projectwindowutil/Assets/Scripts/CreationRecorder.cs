using System.Collections.Generic;
using UnityEditor;

/// <summary>Writes down the paths OnWillCreateAsset is called with, while recording.</summary>
public class CreationRecorder : AssetModificationProcessor
{
    public static bool Recording;
    public static readonly List<string> Paths = new List<string>();

    static void OnWillCreateAsset(string path)
    {
        if (Recording) Paths.Add(path);
    }
}
