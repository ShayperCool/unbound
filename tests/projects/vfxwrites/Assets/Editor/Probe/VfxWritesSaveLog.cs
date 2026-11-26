using System.Collections.Generic;
using UnityEditor;

/// <summary>Logs the paths each AssetDatabase.SaveAssets or other save hands the project's
/// AssetModificationProcessor.OnWillSaveAssets (VfxWritesProbe records them per step), and saves
/// them all.</summary>
public sealed class VfxWritesSaveLog : AssetModificationProcessor
{
    internal static readonly List<string> Calls = new List<string>();

    /// <summary>Once the probe has recorded its rows: the file each later call is appended to (a
    /// line per call), for the Editor's exit.</summary>
    internal static string ExitFile;

    static string[] OnWillSaveAssets(string[] paths)
    {
        Calls.Add(string.Join(", ", paths));
        if (ExitFile != null) System.IO.File.AppendAllText(ExitFile, string.Join(", ", paths) + "\n");
        return paths;
    }
}
