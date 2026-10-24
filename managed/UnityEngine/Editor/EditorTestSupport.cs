using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    public sealed partial class PlayerSettings
    {
        /// <summary>Play Mode tests run in Play mode.</summary>
        internal static bool runPlayModeTestAsEditModeTest { get; set; }
    }

    internal partial class ConsoleWindow
    {
        static bool s_ErrorPause;

        /// <summary>The console's "Error Pause" toggle (off; nothing pauses on errors).</summary>
        public static bool GetConsoleErrorPause() => s_ErrorPause;

        public static void SetConsoleErrorPause(bool enabled) => s_ErrorPause = enabled;
    }

    public partial class AssetDatabase
    {
        /// <summary>Unbound never deletes a project's files: a path that names an asset is refused
        /// (and said so); one naming nothing (a scene saved only in memory) is not there to delete.</summary>
        public static bool DeleteAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!string.IsNullOrEmpty(AssetPathToGUID(path)))
                Debug.LogError($"Unbound does not delete project files: {path} is left as it is.");
            return false;
        }

        public static bool DeleteAssets(string[] paths, List<string> outFailedPaths)
        {
            bool all = true;
            foreach (var path in paths)
            {
                if (DeleteAsset(path)) continue;
                all = false;
                outFailedPaths?.Add(path);
            }
            return all;
        }
    }
}
