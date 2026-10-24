using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityEditor.Utils
{
    /// <summary>The Editor's path helpers (internal; packages use them through
    /// InternalsVisibleTo-style access).</summary>
    internal static partial class Paths
    {
        public static string Combine(params string[] components)
        {
            if (components.Length < 1) throw new ArgumentException("At least one component must be provided!");
            var path = components[0];
            for (int i = 1; i < components.Length; i++) path = Path.Combine(path, components[i]);
            return path;
        }

        public static string[] Split(string path) =>
            path.Split(Path.DirectorySeparatorChar).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();

        public static string NormalizePath(this string path) =>
            Path.DirectorySeparatorChar == '\\' ? path.Replace('/', '\\') : path.Replace('\\', Path.DirectorySeparatorChar);

        public static string ConvertSeparatorsToWindows(this string path) => path.Replace('/', '\\');

        public static string ConvertSeparatorsToUnity(this string path) => path.Replace('\\', '/');

        public static string TrimTrailingSlashes(this string path) => path.TrimEnd('/', '\\');

        public static string GetPathRelativeToProjectDirectory(string filePath) =>
            string.Join("/", filePath.ConvertSeparatorsToUnity().TrimTrailingSlashes().Split('/').SkipWhile(s => s != "assets" && s != "Assets"));

        public static string SkipPathPrefix(string path, string prefix) =>
            path.StartsWith(prefix, StringComparison.Ordinal) ? path.Substring(prefix.Length + 1) : path;

        public static string UnifyDirectorySeparator(string path) =>
            path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        public static bool AreEqual(string pathA, string pathB, bool ignoreCase)
        {
            if (pathA == "" && pathB == "") return true;
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)) return false;
            return string.Compare(Path.GetFullPath(pathA), Path.GetFullPath(pathB),
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == 0;
        }
    }
}
