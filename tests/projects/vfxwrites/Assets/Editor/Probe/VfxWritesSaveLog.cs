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

    /// <summary>While set, scene paths are dropped from what is returned.</summary>
    internal static bool DropScenes;

    /// <summary>The processor's calls and the scene save callbacks, in the order they came
    /// ("will save [paths]", "saving path", "saved name").</summary>
    internal static readonly List<string> Events = new List<string>();

    [InitializeOnLoadMethod]
    static void Listen()
    {
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += (scene, path) => Events.Add("saving " + path);
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += scene => Events.Add("saved " + scene.name);
    }

    static string[] OnWillSaveAssets(string[] paths)
    {
        // (The Editor's order of several paths differs from run to run: sorted here.)
        var sorted = (string[])paths.Clone();
        System.Array.Sort(sorted, System.StringComparer.Ordinal);
        Events.Add("will save [" + string.Join(", ", sorted) + "]");
        Calls.Add(string.Join(", ", paths));
        if (ExitFile != null) System.IO.File.AppendAllText(ExitFile, string.Join(", ", paths) + "\n");
        return DropScenes ? System.Array.FindAll(paths, p => !p.EndsWith(".unity", System.StringComparison.Ordinal)) : paths;
    }
}
