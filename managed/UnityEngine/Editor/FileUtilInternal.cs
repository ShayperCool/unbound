using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// FileUtil's internal directory, file and text helpers, which packages reach as friends of the
    /// Editor: plain file calls, as in the Unity 6000.0.71f1 Editor on macOS
    /// (tests/projects/fileutilcopy internal.*). They work on the paths as given, like a script's
    /// own file calls.
    /// </summary>
    public partial class FileUtil
    {
        /// <summary>Replaces text in each line of a file: pairs of what to find and what to put.</summary>
        internal static void ReplaceText(string path, params string[] input)
        {
            path = NiceWinPath(path);
            string[] data = File.ReadAllLines(path);
            for (int i = 0; i < input.Length; i += 2)
                for (int q = 0; q < data.Length; ++q)
                    data[q] = data[q].Replace(input[i], input[i + 1]);
            File.WriteAllLines(path, data);
        }

        /// <summary>The same with patterns; whether any line changed.</summary>
        internal static bool ReplaceTextRegex(string path, params string[] input)
        {
            bool changed = false;
            path = NiceWinPath(path);
            string[] data = File.ReadAllLines(path);
            for (int i = 0; i < input.Length; i += 2)
            {
                for (int q = 0; q < data.Length; ++q)
                {
                    string before = data[q];
                    data[q] = Regex.Replace(before, input[i], input[i + 1]);
                    if (before != data[q]) changed = true;
                }
            }
            File.WriteAllLines(path, data);
            return changed;
        }

        /// <summary>Puts a line after the first line that holds a text; whether one did.</summary>
        internal static bool AppendTextAfter(string path, string find, string append)
        {
            bool found = false;
            path = NiceWinPath(path);
            var data = new List<string>(File.ReadAllLines(path));
            for (int q = 0; q < data.Count; ++q)
            {
                if (!data[q].Contains(find)) continue;
                data.Insert(q + 1, append);
                found = true;
                break;
            }
            File.WriteAllLines(path, data.ToArray());
            return found;
        }

        internal static void CopyDirectoryRecursive(string source, string target) => CopyDirectoryRecursive(source, target, overwrite: false, ignoreMeta: false);

        internal static void CopyDirectoryRecursive(string source, string target, bool overwrite) => CopyDirectoryRecursive(source, target, overwrite, ignoreMeta: false);

        internal static void CopyDirectory(string source, string target, bool overwrite) => CopyDirectoryFiltered(source, target, overwrite, f => true, false);

        internal static void CopyDirectoryRecursive(string source, string target, bool overwrite, bool ignoreMeta) =>
            CopyDirectoryRecursiveFiltered(source, target, overwrite, ignoreMeta ? MetaFiles : null);

        // .meta files; and with them every path below a name that starts with a dot.
        const string MetaFiles = @"\.meta$";
        const string HiddenAndMetaFiles = @".*/\.+|\.meta$";

        internal static void CopyDirectoryRecursiveForPostprocess(string source, string target, bool overwrite) =>
            CopyDirectoryRecursiveFiltered(source, target, overwrite, HiddenAndMetaFiles);

        internal static void CopyDirectoryRecursiveFiltered(string source, string target, bool overwrite, string regExExcludeFilter) =>
            CopyDirectoryFiltered(source, target, overwrite, regExExcludeFilter, true);

        /// <summary>Copies the files (and with <paramref name="recursive"/> the directories) whose
        /// paths the pattern does not match; a pattern that is none is logged and nothing is copied.</summary>
        internal static void CopyDirectoryFiltered(string source, string target, bool overwrite, string regExExcludeFilter, bool recursive)
        {
            Regex exclude = null;
            try
            {
                if (regExExcludeFilter != null) exclude = new Regex(regExExcludeFilter);
            }
            catch (ArgumentException)
            {
                Debug.Log("CopyDirectoryRecursive: Pattern '" + regExExcludeFilter + "' is not a correct Regular Expression. Not excluding any files.");
                return;
            }
            CopyDirectoryFiltered(source, target, overwrite, file => exclude == null || !exclude.IsMatch(file), recursive);
        }

        /// <summary>Copies the files the callback takes; the target directory is made with the first
        /// of them (a directory without one is not made), and only into a target that was already
        /// there are existing files overwritten.</summary>
        internal static void CopyDirectoryFiltered(string source, string target, bool overwrite, Func<string, bool> includeCallback, bool recursive)
        {
            bool createDirectory = !Directory.Exists(target);
            foreach (string file in Sorted(Directory.GetFiles(source)))
            {
                if (!includeCallback(file)) continue;
                if (createDirectory)
                {
                    Directory.CreateDirectory(target);
                    overwrite = false;
                    createDirectory = false;
                }
                UnityFileCopy(file, Path.Combine(target, Path.GetFileName(file)), overwrite);
            }
            if (!recursive) return;
            foreach (string directory in Sorted(Directory.GetDirectories(source)))
            {
                if (!includeCallback(directory)) continue;
                string name = Path.GetFileName(directory);
                CopyDirectoryFiltered(Path.Combine(source, name), Path.Combine(target, name), overwrite, includeCallback, recursive);
            }
        }

        // A directory's files or subdirectories in the order of their names: what a copy that fails
        // midway has made by then matches the Editor's (internal.copyRecursive.noOverwriteInSubfolder).
        static string[] Sorted(string[] entries)
        {
            Array.Sort(entries, StringComparer.Ordinal);
            return entries;
        }

        internal static void UnityDirectoryDelete(string path) => UnityDirectoryDelete(path, false);

        internal static void UnityDirectoryDelete(string path, bool recursive) => Directory.Delete(NiceWinPath(path), recursive);

        /// <summary>Makes every file in a directory tree writable.</summary>
        internal static void UnityDirectoryRemoveReadonlyAttribute(string target_dir)
        {
            foreach (string file in Directory.GetFiles(target_dir)) File.SetAttributes(file, FileAttributes.Normal);
            foreach (string directory in Directory.GetDirectories(target_dir)) UnityDirectoryRemoveReadonlyAttribute(directory);
        }

        /// <summary>Moves a file that exists over whatever the destination is.</summary>
        internal static void MoveFileIfExists(string src, string dst)
        {
            if (!File.Exists(src)) return;
            DeleteFileOrDirectory(dst);
            MoveFileOrDirectory(src, dst);
            File.SetLastWriteTime(dst, DateTime.Now);
        }

        internal static void CopyFileIfExists(string src, string dst, bool overwrite)
        {
            if (File.Exists(src)) UnityFileCopy(src, dst, overwrite);
        }

        internal static void UnityFileCopy(string from, string to, bool overwrite) => File.Copy(NiceWinPath(from), NiceWinPath(to), overwrite);

        internal static void UnityFileCopy(string from, string to) => UnityFileCopy(from, to, false);

        /// <summary>A file's bytes; without them the reason.</summary>
        internal static bool ReadFileContentBinary(string assetPath, out byte[] fileContent, out string errorMessage)
        {
            fileContent = null;
            errorMessage = null;
            try
            {
                fileContent = File.ReadAllBytes(PathToAbsolutePath(assetPath));
            }
            catch (Exception e)
            {
                fileContent = null;
                errorMessage = e.Message;
            }
            return fileContent != null;
        }

        /// <summary>A path with the separators of the Editor's own system: on Windows backslashes.</summary>
        internal static string NiceWinPath(string unityPath) =>
            Application.platform == RuntimePlatform.WindowsEditor ? unityPath.Replace("/", @"\") : unityPath;

        // A doubled slash in a path stays a doubled slash through the path calls.
        internal static string UnityGetFileNameWithoutExtension(string path) =>
            Path.GetFileNameWithoutExtension(path.Replace("//", @"\\")).Replace(@"\\", "//");

        internal static string UnityGetFileName(string path) => Path.GetFileName(path.Replace("//", @"\\")).Replace(@"\\", "//");

        internal static string UnityGetDirectoryName(string path)
        {
            // The Editor's runtime refuses an empty path here.
            if (path.Length == 0) throw new ArgumentException("Invalid path");
            return Path.GetDirectoryName(path.Replace("//", @"\\")).Replace(@"\\", "//");
        }

        internal static void CreateOrCleanDirectory(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
        }

        /// <summary>A path without the leading folders it shares with another.</summary>
        internal static string RemovePathPrefix(string fullPath, string prefix)
        {
            var partsOfFull = fullPath.Split(Path.DirectorySeparatorChar);
            var partsOfPrefix = prefix.Split(Path.DirectorySeparatorChar);
            int index = partsOfFull[0] == string.Empty ? 1 : 0;
            while (index < partsOfFull.Length && index < partsOfPrefix.Length && partsOfFull[index] == partsOfPrefix[index]) ++index;
            return index == partsOfFull.Length ? "" : string.Join(Path.DirectorySeparatorChar.ToString(), partsOfFull, index, partsOfFull.Length - index);
        }

        internal static string CombinePaths(params string[] paths) =>
            paths == null ? string.Empty : string.Join(Path.DirectorySeparatorChar.ToString(), paths);

        internal static List<string> GetAllFilesRecursive(string path)
        {
            var files = new List<string>();
            WalkFilesystemRecursively(path, files.Add, p => true);
            return files;
        }

        /// <summary>Every file of a directory, then the same for each subdirectory the callback takes.</summary>
        internal static void WalkFilesystemRecursively(string path, Action<string> fileCallback, Func<string, bool> directoryCallback)
        {
            foreach (string file in Sorted(Directory.GetFiles(path))) fileCallback(file);
            foreach (string subdir in Sorted(Directory.GetDirectories(path)))
                if (directoryCallback(subdir)) WalkFilesystemRecursively(subdir, fileCallback, directoryCallback);
        }

        /// <summary>The bytes of all files in a directory tree.</summary>
        internal static long GetDirectorySize(string path)
        {
            long size = 0;
            // Every file: the Editor's "*.*" search takes names without a dot too.
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) size += new FileInfo(file).Length;
            return size;
        }
    }
}
