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

    public sealed partial class AssetDatabase
    {
        /// <summary>Deletes the asset file at the path with its .meta (as the Scripting API
        /// documents: true once removed, false when nothing is there); the Test Framework deletes
        /// the scene it saved for a Play Mode run so. Folders are not deleted yet: refused, and
        /// said so.</summary>
        public static bool DeleteAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var file = System.IO.Path.Combine(ProjectInfo.ProjectPath, path);
            if (System.IO.Directory.Exists(file))
            {
                Debug.LogError($"Unbound does not delete folders yet: {path} is left as it is.");
                return false;
            }
            if (!System.IO.File.Exists(file)) return false;
            var guid = AssetPathToGUID(path);
            System.IO.File.Delete(file);
            if (System.IO.File.Exists(file + ".meta")) System.IO.File.Delete(file + ".meta");
            if (!string.IsNullOrEmpty(guid)) Unbound.Runtime.AssetDatabaseRuntime.Invalidate(guid);
            return true;
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
