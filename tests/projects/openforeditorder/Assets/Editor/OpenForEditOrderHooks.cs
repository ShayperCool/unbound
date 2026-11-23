using System.Collections.Generic;
using UnityEditor;

/// <summary>The calls the processors below receive (read by OpenForEditProbe).</summary>
public static class OpenForEditHooks
{
    public static readonly List<string> Calls = new List<string>();

    internal static string Show(string[] paths) => paths == null ? "<null>" : "[" + string.Join(",", paths) + "]";
    internal static string Show(List<string> list) => list == null ? "<null>" : "[" + string.Join(",", list) + "]";

    /// <summary>Adds the paths naming `marker` to the list; whether there were none.</summary>
    internal static bool Refuse(string[] paths, List<string> outNotEditablePaths, string marker)
    {
        bool all = true;
        foreach (var path in paths ?? new string[0])
        {
            if (path == null || !path.Contains(marker)) continue;
            all = false;
            outNotEditablePaths?.Add(path);
        }
        return all;
    }

    internal static bool IsOpenForEdit(string name, string marker, string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions)
    {
        Calls.Add(name + ".IsOpenForEdit(" + Show(paths) + "," + Show(outNotEditablePaths) + "," + statusQueryOptions + ")");
        return Refuse(paths, outNotEditablePaths, marker);
    }

    internal static bool MakeEditable(string name, string marker, string[] paths, string prompt, List<string> outNotEditablePaths)
    {
        Calls.Add(name + ".MakeEditable(" + Show(paths) + "," + (prompt ?? "<null>") + "," + Show(outNotEditablePaths) + ")");
        return Refuse(paths, outNotEditablePaths, marker);
    }
}

/// <summary>Two processors with the same callbacks: the list IsOpenForEdit and MakeEditable,
/// refusing "Second" and "Third".</summary>
public class OpenForEditSecondProcessor : AssetModificationProcessor
{
    static bool IsOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
        OpenForEditHooks.IsOpenForEdit("second", "Second", paths, outNotEditablePaths, statusQueryOptions);

    static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths) =>
        OpenForEditHooks.MakeEditable("second", "Second", paths, prompt, outNotEditablePaths);
}

public class OpenForEditThirdProcessor : AssetModificationProcessor
{
    static bool IsOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
        OpenForEditHooks.IsOpenForEdit("third", "Third", paths, outNotEditablePaths, statusQueryOptions);

    static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths) =>
        OpenForEditHooks.MakeEditable("third", "Third", paths, prompt, outNotEditablePaths);
}
