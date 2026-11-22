using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;

namespace UnityEditor
{
    /// <summary>
    /// FileUtil's copy, move and replace calls and its path helpers, as the Unity 6000.0.71f1 Editor
    /// on macOS has them (tests/projects/fileutilcopy; the Linux Editor is not measured). A copy
    /// makes a file with the source's text and read-only flag, or a directory with everything in the
    /// source's (hidden files, .meta files and empty directories too); links are followed, so both
    /// copy calls make files and directories of what the links point to. A move renames the entry
    /// itself, a link as a link. A .meta file beside a copied or moved path is left alone. A
    /// destination that exists, a missing source and a destination whose folder is missing fail with
    /// an IOException and change nothing. Relative paths are the working directory's (the project's);
    /// backslashes separate folders. Unbound's choices (unmeasured): a link back into a copied tree
    /// fails the copy; what a failed copy of a tree has already made stays; a move to another volume
    /// copies and deletes; destinations and moved sources that land on what
    /// <see cref="DeleteFileOrDirectory"/> refuses are refused and reported the same way.
    /// </summary>
    public partial class FileUtil
    {
        internal const string UnsupportedCopyMember = "FileUtil copy, move or replace onto the project folder, its Library folder, Library/Unbound and what is in it, or a package not embedded in the project's Packages folder and what is in it";

        static void CheckSourceAndDestination(string source, string dest)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (dest == null) throw new ArgumentNullException("dest");
            // The Editor's own argument order: the name is the message.
            if (source == string.Empty) throw new ArgumentException("source", "The source path cannot be empty.");
            if (dest == string.Empty) throw new ArgumentException("dest", "The destination path cannot be empty.");
        }

        static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('\\', '/')));

        static bool IsLink(string full) => new FileInfo(full).LinkTarget != null;

        // A file, a directory or a link, also one that points nowhere.
        static bool EntryExists(string full) => File.Exists(full) || Directory.Exists(full) || IsLink(full);

        static IOException DestinationExists(string source, string dest) =>
            new IOException(string.Format("Failed to Copy File / Directory from '{0}' to '{1}': destination path already exists.", source, dest));

        // Whether Unbound lets a script write or take the entry: see DeleteFileOrDirectory.
        static bool Allowed(string full)
        {
            var view = ProjectView.Current();
            if (view == null) return true;
            view.ProjectEntryOf(full, out string projectEntry);
            if (!view.Refuses(projectEntry ?? full)) return true;
            Stubs.NotImplemented(UnsupportedCopyMember);
            return false;
        }

        public static void CopyFileOrDirectory(string source, string dest)
        {
            CheckSourceAndDestination(source, dest);
            if (EntryExists(FullPath(dest))) throw DestinationExists(source, dest);
            if (!Copy(FullPath(source), FullPath(dest)))
                throw new IOException(string.Format("Failed to Copy File / Directory from '{0}' to '{1}'.", source, dest));
        }

        /// <summary>The same as <see cref="CopyFileOrDirectory"/>: that one follows links too.</summary>
        public static void CopyFileOrDirectoryFollowSymlinks(string source, string dest) => CopyFileOrDirectory(source, dest);

        public static void MoveFileOrDirectory(string source, string dest)
        {
            CheckSourceAndDestination(source, dest);
            // The Editor's message says Copy here too.
            if (EntryExists(FullPath(dest))) throw DestinationExists(source, dest);
            if (!Move(FullPath(source), FullPath(dest)))
                throw new IOException(string.Format("Failed to Move File / Directory from '{0}' to '{1}'.", source, dest));
        }

        /// <summary>Copies a file over another: what was there is deleted first, also when the copy
        /// then fails.</summary>
        public static void ReplaceFile(string src, string dst)
        {
            if (File.Exists(dst)) DeleteFileOrDirectory(dst);
            CopyFileOrDirectory(src, dst);
        }

        /// <summary>Copies a directory over another: what was there is deleted first, also when the
        /// copy then fails.</summary>
        public static void ReplaceDirectory(string src, string dst)
        {
            if (Directory.Exists(dst) && !DeleteFileOrDirectory(dst))
                throw new IOException(string.Format("Failed to delete directory '{0}'.", dst));
            CopyFileOrDirectory(src, dst);
        }

        static bool Copy(string source, string dest)
        {
            if (!Directory.Exists(Path.GetDirectoryName(dest)) || !Allowed(dest)) return false;
            try
            {
                if (File.Exists(source))
                {
                    File.Copy(source, dest);
                    return true;
                }
                return Directory.Exists(source) && CopyDirectory(source, dest, new HashSet<string>(StringComparer.Ordinal));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // Everything in a directory, links followed; `open` holds the directories being copied, so
        // that a link back into one of them ends the copy instead of never ending.
        static bool CopyDirectory(string source, string dest, HashSet<string> open)
        {
            string real = ConsoleLogPath.Resolve(source);
            if (!open.Add(real)) return false;
            Directory.CreateDirectory(dest);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                string target = Path.Combine(dest, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    if (!CopyDirectory(entry, target, open)) return false;
                }
                else if (File.Exists(entry)) File.Copy(entry, target);
                // A link that points nowhere has nothing to copy.
            }
            open.Remove(real);
            return true;
        }

        static bool Move(string source, string dest)
        {
            if (!EntryExists(source) || !Directory.Exists(Path.GetDirectoryName(dest)) || !Allowed(dest) || !Allowed(source)) return false;
            try
            {
                // A rename: a link stays a link, whatever it points to.
                if (Directory.Exists(source)) Directory.Move(source, dest);
                else File.Move(source, dest);
                return true;
            }
            catch (IOException) when (!IsLink(source) && Directory.Exists(source) && !EntryExists(dest))
            {
                // Another volume: a directory cannot be renamed onto it.
                try
                {
                    if (!CopyDirectory(source, dest, new HashSet<string>(StringComparer.Ordinal))) return false;
                    Directory.Delete(source, true);
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return false;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>A path in the project's Temp folder that nothing has: nothing is made.</summary>
        public static string GetUniqueTempPathInProject() => "Temp/UnityTempFile-" + Guid.NewGuid().ToString("N");

        /// <summary>The path under the project folder of an absolute path inside it, spelled as it
        /// was given; "" for the project folder, for a path outside it and for a relative one. The
        /// case of the project folder counts as it does on its volume.</summary>
        public static string GetProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string project = UnityEngine.ProjectInfo.ProjectPath;
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project)) + "/";
            var comparison = IgnoresCaseAt(project) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return path.StartsWith(project, comparison) ? path.Substring(project.Length) : "";
        }

        // Whether the volume finds the folder under another case of its name.
        static bool IgnoresCaseAt(string folder)
        {
            folder = Path.TrimEndingDirectorySeparator(folder);
            string upper = folder.ToUpperInvariant(), lower = folder.ToLowerInvariant();
            string other = upper != folder ? upper : lower;
            return other != folder && Directory.Exists(other);
        }

        // The start of a path's last component: slashes separate, backslashes are name characters.
        static int LastComponent(string path) => path.LastIndexOf('/') + 1;

        /// <summary>What follows a path's last slash ("" after a trailing one).</summary>
        internal static string GetLastPathNameComponent(string path) => path == null ? "" : path.Substring(LastComponent(path));

        /// <summary>A path without its last component and the slashes before it ("/" for an entry of
        /// the root); a path that ends with slashes loses them first.</summary>
        internal static string DeleteLastPathNameComponent(string path)
        {
            if (path == null) return "";
            path = path.TrimEnd('/');
            int slash = path.LastIndexOf('/');
            return slash < 0 ? "" : slash == 0 ? "/" : path.Substring(0, slash).TrimEnd('/');
        }

        /// <summary>What follows the last dot of a path's last component, a leading dot too.</summary>
        internal static string GetPathExtension(string path)
        {
            if (path == null) return "";
            int dot = path.LastIndexOf('.');
            return dot < LastComponent(path) ? "" : path.Substring(dot + 1);
        }

        /// <summary>A path up to the last dot of its last component.</summary>
        internal static string GetPathWithoutExtension(string path)
        {
            if (path == null) return "";
            int dot = path.LastIndexOf('.');
            return dot < LastComponent(path) ? path : path.Substring(0, dot);
        }

        internal static bool IsSymlink(string path) => !string.IsNullOrEmpty(path) && IsLink(FullPath(path));

        /// <summary>Where a link leads, as an absolute path; any other path as it was given.</summary>
        internal static string ResolveSymlinks(string path)
        {
            if (string.IsNullOrEmpty(path)) return path ?? "";
            string full = FullPath(path);
            if (!IsLink(full)) return path;
            var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
            return target == null ? path : Path.TrimEndingDirectorySeparator(target.FullName);
        }

        /// <summary>A path with each existing component spelled as its entry is on the volume (it
        /// differs only on a volume that ignores case); a path that names nothing as it was given.</summary>
        internal static string GetActualPathName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path ?? "";
            if (!EntryExists(FullPath(path))) return path;
            string[] parts = path.Split('/');
            string walked = path.StartsWith("/") ? "/" : "";
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length != 0 && part != "." && part != "..")
                {
                    string actual = null;
                    foreach (string entry in Directory.EnumerateFileSystemEntries(walked.Length == 0 ? "." : walked))
                    {
                        string name = Path.GetFileName(entry);
                        if (name == part)
                        {
                            actual = name;
                            break;
                        }
                        if (actual == null && string.Equals(name, part, StringComparison.OrdinalIgnoreCase)) actual = name;
                    }
                    part = actual ?? part;
                }
                parts[i] = part;
                walked = walked.Length == 0 || walked == "/" ? walked + part : walked + "/" + part;
            }
            return string.Join("/", parts);
        }
    }
}
