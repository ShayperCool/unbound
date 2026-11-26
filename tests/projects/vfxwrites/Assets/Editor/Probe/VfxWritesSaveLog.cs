using System.Collections.Generic;
using UnityEditor;

/// <summary>Logs the paths each AssetDatabase.SaveAssets or other save hands the project's
/// AssetModificationProcessor.OnWillSaveAssets (VfxWritesProbe records them per step), and saves
/// them all.</summary>
public sealed class VfxWritesSaveLog : AssetModificationProcessor
{
    internal static readonly List<string> Calls = new List<string>();

    static string[] OnWillSaveAssets(string[] paths)
    {
        Calls.Add(string.Join(", ", paths));
        return paths;
    }
}
