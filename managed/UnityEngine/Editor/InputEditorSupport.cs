using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.PackageManager;
using UnityEngine;

// Editor API the Input System package calls in Play mode: the Unity Remote message channel, the
// Editor's focus, the build profile's player settings it reads the active input handling from,
// and the path mapping its importers read files through.

namespace UnityEditor.Remote
{
    /// <summary>The Unity Remote message channel: handlers are kept, as in Unity; no Remote device
    /// connects to Unbound, so they are never called and the gyroscope settings go nowhere.</summary>
    internal static partial class GenericRemote
    {
        static readonly List<Func<IntPtr, bool>> s_Handlers = new List<Func<IntPtr, bool>>();

        public static void AddMessageHandler(Func<IntPtr, bool> handler) => s_Handlers.Add(handler);

        public static void RemoveMessageHandler(Func<IntPtr, bool> handler) => s_Handlers.Remove(handler);

        internal static bool CallMessageHandlers(IntPtr messageData)
        {
            foreach (var handler in s_Handlers)
                if (handler(messageData)) return true;
            return false;
        }

        [Unbound.EditorFaithful("no Unity Remote device is connected")]
        public static void SetGyroEnabled(bool enabled) { }

        [Unbound.EditorFaithful("no Unity Remote device is connected")]
        public static void SetGyroUpdateInterval(float interval) { }
    }
}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        /// <summary>Whether the Editor application has the focus: the game's window does.</summary>
        public static bool isApplicationActive => Application.isFocused;
    }
}

namespace UnityEditor.Build.Profile
{
    /// <summary>The Editor's build profiles. A project plays with its classic platform (no
    /// active profile), whose player settings are the project's: the Input System reads its
    /// active input handling from <c>s_GlobalPlayerSettings</c> through reflection.</summary>
    public sealed partial class BuildProfile : ScriptableObject
    {
        static PlayerSettings s_GlobalPlayerSettings = PlayerSettings.Project;
        PlayerSettings m_PlayerSettings;

        [Unbound.EditorFaithful("the project plays with its classic platform: no build profile is active")]
        public static BuildProfile GetActiveBuildProfile() => null;
    }
}

namespace UnityEditor
{
    public sealed partial class PlayerSettings
    {
        /// <summary>The project's player settings object (ProjectSettings.asset), as editor code
        /// serializes it.</summary>
        internal static readonly PlayerSettings Project = new PlayerSettings { name = "ProjectSettings" };

        /// <summary><c>activeInputHandler</c>: 0 Input Manager, 1 Input System package, 2 both. A
        /// change applies in memory only (Unity applies it after a restart).</summary>
        internal static int s_ActiveInputHandler;
    }

    public partial class FileUtil
    {
        /// <summary>The file a project path names: <c>Packages/name/...</c> in the package's
        /// folder, other paths as they are.</summary>
        public static string GetPhysicalPath(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("Packages/", StringComparison.Ordinal)) return path;
            var rest = normalized.Substring("Packages/".Length);
            int slash = rest.IndexOf('/');
            var name = slash < 0 ? rest : rest.Substring(0, slash);
            foreach (var package in PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name != name || string.IsNullOrEmpty(package.resolvedPath)) continue;
                return slash < 0 ? package.resolvedPath : Path.Combine(package.resolvedPath, rest.Substring(slash + 1)).Replace('\\', '/');
            }
            return path;
        }

        /// <summary>The project path of a file: inside a package's folder <c>Packages/name/...</c>,
        /// other paths as they are.</summary>
        public static string GetLogicalPath(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var normalized = path.Replace('\\', '/');
            foreach (var package in PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (string.IsNullOrEmpty(package.resolvedPath)) continue;
                var root = package.resolvedPath.Replace('\\', '/').TrimEnd('/');
                if (normalized == root) return "Packages/" + package.name;
                if (normalized.StartsWith(root + "/", StringComparison.Ordinal))
                    return "Packages/" + package.name + normalized.Substring(root.Length);
            }
            return path;
        }
    }
}
