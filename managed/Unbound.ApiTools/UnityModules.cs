using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unbound.ApiTools;

/// <summary>
/// Unity's engine and editor modules as they appear in a release's
/// <c>Editor/Data/Managed/UnityEngine/</c>: <c>UnityEngine.dll</c>, <c>UnityEditor.dll</c> and every
/// <c>UnityEngine.*</c> / <c>UnityEditor.*</c> module. The directory also holds unrelated libraries
/// (<c>Unity.Cecil*.dll</c>), which are not modules.
/// </summary>
public static class UnityModules
{
    /// <summary>The one assembly Unbound implements; every other module forwards to it.</summary>
    public const string ImplAssemblyName = "UnityEngine.CoreModule";

    /// <summary>Whether an assembly name is in Unity's engine/editor module namespace.</summary>
    public static bool HasModuleName(string assemblyName) =>
        assemblyName == "UnityEngine" || assemblyName == "UnityEditor"
        || assemblyName.StartsWith("UnityEngine.", StringComparison.Ordinal)
        || assemblyName.StartsWith("UnityEditor.", StringComparison.Ordinal);

    /// <summary>The module DLLs of a release, sorted by file name (ordinal) for determinism.</summary>
    public static List<string> Files(string unityManagedEngineDir)
    {
        if (!Directory.Exists(unityManagedEngineDir))
            throw new DirectoryNotFoundException($"Unity managed engine directory {unityManagedEngineDir} does not exist");
        return Directory.GetFiles(unityManagedEngineDir, "*.dll")
            .Where(f => HasModuleName(Path.GetFileNameWithoutExtension(f)))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();
    }
}
