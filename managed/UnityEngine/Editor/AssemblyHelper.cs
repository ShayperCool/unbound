using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Scripting.ScriptCompilation;

namespace UnityEditor
{
    internal partial class AssemblyHelper
    {
        /// <summary>
        /// The folders package editor code (Burst's loader) searches for assemblies: the folders of
        /// the Editor's precompiled assemblies, sorted by their full paths, then the Unity engine
        /// assemblies' folder and Library/ScriptAssemblies (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/editorhooks). The Editor's precompiled assemblies are UnityEditor.Graphs
        /// (in Managed) and each installed platform's Editor extensions (its playback engine folder
        /// holding a UnityEditor.*.Extensions.dll), read from the installation; the project's and
        /// packages' precompiled plug-ins join them (unmeasured: the probe has none).
        /// </summary>
        public static string[] GetDefaultAssemblySearchPaths()
        {
            var contents = UnityInstallInfo.DataPath.Replace('\\', '/').TrimEnd('/');
            var folders = new SortedSet<string>(StringComparer.Ordinal);
            if (File.Exists(Path.Combine(contents, "Managed", "UnityEditor.Graphs.dll"))) folders.Add(contents + "/Managed");
            foreach (var engine in BuildPipeline.InstalledPlaybackEngineFolders())
                if (Directory.GetFiles(engine, "UnityEditor.*.Extensions.dll").Length > 0) folders.Add(engine.Replace('\\', '/'));
            foreach (var plugin in EditorCompilationInterface.Instance.GetAllPrecompiledAssemblies())
            {
                var folder = Path.GetDirectoryName(Path.GetFullPath(plugin.Path, UnityEngine.ProjectInfo.ProjectPath));
                if (!string.IsNullOrEmpty(folder)) folders.Add(folder.Replace('\\', '/'));
            }
            var paths = new List<string>(folders);
            foreach (var tail in new[] { contents + "/Managed/UnityEngine", "Library/ScriptAssemblies" })
                if (!paths.Contains(tail)) paths.Add(tail);
            return paths.ToArray();
        }
    }
}
