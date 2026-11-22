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
    /// backslashes separate folders. Unbound's choices (unmeasured): a destination inside the copied
    /// or moved directory and a link back into a copied tree fail before anything is made; what a
    /// failed copy of a tree has already made stays; a directory is not moved to another volume; a
    /// link that points nowhere counts as a destination that exists. In Unbound's project view (see
    /// <see cref="DeleteFileOrDirectory"/>) a path naming one of the view's own links, or a new
    /// name beside them, names the project's own entry: it is copied, moved or made there and the
    /// view gets or loses its link (a project entry the view has no link for counts as an existing
    /// destination; the Packages folder is not moved); destinations and moved sources that land on what
    /// DeleteFileOrDirectory refuses are refused and reported the same way.
    /// </summary>
    public partial class FileUtil
    {
        internal const string UnsupportedCopyMember = "FileUtil copy, move or replace onto the project folder, its Library folder, Unbound's directory (LibraryUnbound) and what is in it, or a package not embedded in the project's Packages folder and what is in it; a move of the Packages folder";

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

        // Where an entry is made or taken: the project's own entry for a path naming one of the
        // view's links or a new name beside them (`viewEntry` is then the view's), else the path.
        // Not allowed where DeleteFileOrDirectory refuses: that is reported.
        static string Landing(string full, out string viewEntry, out bool allowed)
        {
            viewEntry = null;
            allowed = true;
            var view = ProjectView.Current();
            if (view == null) return full;
            string landing = view.ProjectPathOf(full, out viewEntry) ?? full;
            if (view.Refuses(landing))
            {
                Stubs.NotImplemented(UnsupportedCopyMember);
                allowed = false;
            }
            return landing;
        }

        // Whether a destination is taken: by an entry at the path, or by the project's own entry
        // that the path names in the view (the view has no link for one made after it was built,
        // nor for a folder of Packages that is not a package).
        static bool DestinationTaken(string full)
        {
            if (EntryExists(full)) return true;
            var view = ProjectView.Current();
            string project = view?.ProjectPathOf(full, out _);
            return project != null && EntryExists(project);
        }

        // The view's link to a project entry that was just made.
        static void LinkInView(string viewEntry, string landing)
        {
            if (viewEntry == null || viewEntry == landing || EntryExists(viewEntry)) return;
            if (Directory.Exists(landing)) Directory.CreateSymbolicLink(viewEntry, landing);
            else File.CreateSymbolicLink(viewEntry, landing);
        }

        // Whether a path is a directory or inside it, links resolved.
        static bool Inside(string path, string directory)
        {
            string parent = Path.GetDirectoryName(path);
            string real = parent == null ? path : Path.Combine(ConsoleLogPath.Resolve(parent), Path.GetFileName(path));
            string root = ConsoleLogPath.Resolve(directory);
            return real == root || real.StartsWith(root + "/", StringComparison.Ordinal);
        }

        public static void CopyFileOrDirectory(string source, string dest)
        {
            CheckSourceAndDestination(source, dest);
            if (DestinationTaken(FullPath(dest))) throw DestinationExists(source, dest);
            if (!Copy(FullPath(source), FullPath(dest)))
                throw new IOException(string.Format("Failed to Copy File / Directory from '{0}' to '{1}'.", source, dest));
        }

        /// <summary>The same as <see cref="CopyFileOrDirectory"/>: that one follows links too.</summary>
        public static void CopyFileOrDirectoryFollowSymlinks(string source, string dest) => CopyFileOrDirectory(source, dest);

        public static void MoveFileOrDirectory(string source, string dest)
        {
            CheckSourceAndDestination(source, dest);
            // The Editor's message says Copy here too.
            if (DestinationTaken(FullPath(dest))) throw DestinationExists(source, dest);
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
            dest = Landing(dest, out string viewEntry, out bool allowed);
            if (!allowed || !Directory.Exists(Path.GetDirectoryName(dest))) return false;
            try
            {
                if (File.Exists(source)) File.Copy(source, dest);
                else if (!Directory.Exists(source) || Inside(dest, source) || !CopyDirectory(source, dest, new HashSet<string>(StringComparer.Ordinal))) return false;
                LinkInView(viewEntry, dest);
                return true;
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
            if (!EntryExists(source)) return false;
            source = Landing(source, out string sourceEntry, out bool sourceAllowed);
            dest = Landing(dest, out string destEntry, out bool destAllowed);
            if (!sourceAllowed || !destAllowed || !Directory.Exists(Path.GetDirectoryName(dest))) return false;
            // The view's Packages folder holds the view's links to the packages: moving the
            // project's folder from under them is not done.
            if (sourceEntry != null && sourceEntry != source && !IsLink(sourceEntry))
            {
                Stubs.NotImplemented(UnsupportedCopyMember);
                return false;
            }
            try
            {
                // A rename: a link stays a link, whatever it points to.
                if (Directory.Exists(source))
                {
                    if (!IsLink(source) && Inside(dest, source)) return false;
                    Directory.Move(source, dest);
                }
                else File.Move(source, dest);
                // The view's link to the moved project entry goes, and one to the new entry comes.
                if (sourceEntry != null && sourceEntry != source && IsLink(sourceEntry)) File.Delete(sourceEntry);
                LinkInView(destEntry, dest);
                return true;
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
        /// project folder's case counts (not measured on a volume that ignores it).</summary>
        public static string GetProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string project = UnityEngine.ProjectInfo.ProjectPath;
            if (string.IsNullOrEmpty(project)) project = Directory.GetCurrentDirectory();
            project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project)) + "/";
            return path.StartsWith(project, StringComparison.Ordinal) ? path.Substring(project.Length) : "";
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

        /// <summary>Where a link leads, as an absolute path; any other path as it was given, also one
        /// that only passes through a link (unmeasured).</summary>
        internal static string ResolveSymlinks(string path)
        {
            if (string.IsNullOrEmpty(path)) return path ?? "";
            string full = FullPath(path);
            if (!IsLink(full)) return path;
            try
            {
                var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
                return target == null ? path : Path.TrimEndingDirectorySeparator(target.FullName);
            }
            catch (IOException)
            {
                // Links that lead back to themselves lead nowhere.
                return path;
            }
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
