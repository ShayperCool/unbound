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
}

/// <summary>The single-path IsOpenForEdit callback (refuses "Held", with a message) and
/// MakeEditable (refuses "NoMake").</summary>
public class OpenForEditPathProcessor : AssetModificationProcessor
{
    static bool IsOpenForEdit(string assetPath, out string message)
    {
        OpenForEditHooks.Calls.Add("path.IsOpenForEdit(" + assetPath + ")");
        bool held = assetPath != null && assetPath.Contains("Held");
        message = held ? "held by the probe" : "";
        return !held;
    }

    static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
    {
        OpenForEditHooks.Calls.Add("path.MakeEditable(" + OpenForEditHooks.Show(paths) + "," + (prompt ?? "<null>") + "," + OpenForEditHooks.Show(outNotEditablePaths) + ")");
        return OpenForEditHooks.Refuse(paths, outNotEditablePaths, "NoMake");
    }
}

/// <summary>The list IsOpenForEdit (refuses "Locked") and CanOpenForEdit (refuses "NoCan")
/// callbacks.</summary>
public class OpenForEditListProcessor : AssetModificationProcessor
{
    static bool IsOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions)
    {
        OpenForEditHooks.Calls.Add("list.IsOpenForEdit(" + OpenForEditHooks.Show(paths) + "," + OpenForEditHooks.Show(outNotEditablePaths) + "," + statusQueryOptions + ")");
        return OpenForEditHooks.Refuse(paths, outNotEditablePaths, "Locked");
    }

    static bool CanOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions)
    {
        OpenForEditHooks.Calls.Add("list.CanOpenForEdit(" + OpenForEditHooks.Show(paths) + "," + OpenForEditHooks.Show(outNotEditablePaths) + "," + statusQueryOptions + ")");
        return OpenForEditHooks.Refuse(paths, outNotEditablePaths, "NoCan");
    }
}

/// <summary>A second processor with the list IsOpenForEdit and MakeEditable callbacks, both
/// refusing "Second".</summary>
public class OpenForEditSecondProcessor : AssetModificationProcessor
{
    static bool IsOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions)
    {
        OpenForEditHooks.Calls.Add("second.IsOpenForEdit(" + OpenForEditHooks.Show(paths) + "," + OpenForEditHooks.Show(outNotEditablePaths) + "," + statusQueryOptions + ")");
        return OpenForEditHooks.Refuse(paths, outNotEditablePaths, "Second");
    }

    static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
    {
        OpenForEditHooks.Calls.Add("second.MakeEditable(" + OpenForEditHooks.Show(paths) + "," + (prompt ?? "<null>") + "," + OpenForEditHooks.Show(outNotEditablePaths) + ")");
        return OpenForEditHooks.Refuse(paths, outNotEditablePaths, "Second");
    }
}
