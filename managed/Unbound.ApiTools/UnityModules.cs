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
///
/// Platform modules (<c>PlaybackEngines/*/UnityEditor.*.Extensions.dll</c>,
/// <c>Unity.Android.*.dll</c>, <c>Managed/UnityEditor.Graphs.dll</c>) live elsewhere in the install;
/// the caller passes their exact list (the Rust side's <c>UnityInstall::platform_modules</c>, the same
/// list game code is compiled against) and they are treated as modules too.
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

    /// <summary>The module DLLs of a release: those of the managed engine directory, then the
    /// platform modules; each part sorted by file name (ordinal) for determinism. A platform module
    /// whose file name is already listed is ignored.</summary>
    public static List<string> Files(string unityManagedEngineDir, IEnumerable<string>? platformModules = null)
    {
        if (!Directory.Exists(unityManagedEngineDir))
            throw new DirectoryNotFoundException($"Unity managed engine directory {unityManagedEngineDir} does not exist");
        var files = Directory.GetFiles(unityManagedEngineDir, "*.dll")
            .Where(f => HasModuleName(Path.GetFileNameWithoutExtension(f)))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();
        var names = files.Select(f => Path.GetFileName(f)).ToHashSet(StringComparer.Ordinal);
        foreach (var module in (platformModules ?? Enumerable.Empty<string>()).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            if (!File.Exists(module))
                throw new FileNotFoundException($"platform module {module} does not exist");
            if (names.Add(Path.GetFileName(module)))
                files.Add(Path.GetFullPath(module));
        }
        return files;
    }
}
