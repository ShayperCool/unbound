using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;

namespace UnityEditor
{
    public partial class FileUtil
    {
        /// <summary>
        /// Deletes the file or the directory tree the path names and nothing else (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/fileutildelete): true when something was deleted, false
        /// for a path that does not exist (a wildcard is a name); null and empty paths throw. A
        /// relative path is the working directory's (the project's); backslashes separate folders;
        /// read-only files go too; a symbolic link is removed, not what it points to; a .meta file
        /// beside the path stays; nothing is logged. Unbound's choices (unmeasured): links inside a
        /// deleted tree are removed, not followed; a path the process may not delete gives false;
        /// paths through the project view, see <see cref="ProjectView"/>.
        /// </summary>
        public static bool DeleteFileOrDirectory(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path == string.Empty) throw new ArgumentException("path", "The path cannot be empty.");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('\\', '/')));
            try
            {
                var view = ProjectView.Current();
                if (view == null) return Delete(full);
                string viewEntry = view.ProjectEntryOf(full, out string projectEntry);
                if (view.Refuses(projectEntry ?? full))
                {
                    Stubs.NotImplemented(UnsupportedMember);
                    return false;
                }
                if (projectEntry == null) return Delete(full);
                if (!Delete(projectEntry)) return false;
                // The view's own entry goes too (only links live there), as if the project's entry
                // had never been there.
                if (new FileInfo(viewEntry).LinkTarget != null) File.Delete(viewEntry);
                else if (Directory.Exists(viewEntry)) Directory.Delete(viewEntry, true);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal const string UnsupportedMember = "FileUtil.DeleteFileOrDirectory of the project folder, its Library folder, Unbound's directory (LibraryUnbound) and what is in it, or a package not embedded in the project's Packages folder and what is in it";

        static bool Delete(string full)
        {
            var entry = new FileInfo(full);
            if (entry.LinkTarget != null || File.Exists(full)) entry.Delete();
            else if (Directory.Exists(full)) Directory.Delete(full, true);
            else return false;
            return true;
        }

        /// <summary>
        /// The game's working directory is Unbound's project view (<c>LibraryUnbound/ProjectView</c>):
        /// a directory of links to each top-level project entry and to each package as
        /// <c>Packages/&lt;name&gt;</c>. Unbound's reading (unmeasured): a path naming a top-level
        /// link, the view's <c>Packages</c> folder or an embedded package's link (one whose package
        /// is in the project's Packages folder) names the project's own entry; paths below those
        /// links reach the project's files through them. Refused and reported, decided on where a
        /// path lands (its links followed, and without regard to case on a volume that ignores it),
        /// as what the Editor does there is unmeasured or would take Unbound's own files: the project
        /// folder, its Library folder, Unbound's directory (LibraryUnbound) and what is in it, and a package kept
        /// elsewhere (the Editor's built-in packages, a package cache or store, a local package
        /// outside Packages) and what is in it.
        /// </summary>
        sealed class ProjectView
        {
            readonly string m_View, m_Project, m_Unbound;
            readonly StringComparison m_Case;
            readonly string m_PhysicalView, m_PhysicalProject, m_PhysicalLibrary, m_PhysicalUnbound, m_PhysicalEmbedded;
            readonly List<string> m_OtherPackages = new List<string>();

            ProjectView(string view, string project, string unbound)
            {
                m_View = view;
                m_Project = project;
                m_Case = IgnoresCase(view) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                m_PhysicalView = ConsoleLogPath.Resolve(view);
                m_PhysicalProject = ConsoleLogPath.Resolve(project);
                m_PhysicalLibrary = Path.Combine(m_PhysicalProject, "Library");
                m_Unbound = unbound;
                m_PhysicalUnbound = ConsoleLogPath.Resolve(unbound);
                m_PhysicalEmbedded = Path.Combine(m_PhysicalProject, "Packages");
                string packages = Path.Combine(view, "Packages");
                if (!Directory.Exists(packages)) return;
                foreach (string entry in Directory.EnumerateFileSystemEntries(packages))
                {
                    string target = new FileInfo(entry).LinkTarget;
                    if (target == null || !Directory.Exists(entry)) continue;
                    string root = ConsoleLogPath.Resolve(Path.GetFullPath(target, packages));
                    if (Below(root, m_PhysicalEmbedded) == null) m_OtherPackages.Add(root);
                }
            }

            /// <summary>The view the game runs in, or null when the project path is not one.</summary>
            internal static ProjectView Current()
            {
                string view = UnityEngine.ProjectInfo.ProjectPath;
                if (string.IsNullOrEmpty(view)) return null;
                view = Path.TrimEndingDirectorySeparator(Path.GetFullPath(view));
                // The view is in Unbound's directory, which the host places (by default in the
                // project's folder, which is then the folder that holds it).
                string unbound = Path.GetDirectoryName(view);
                if (Path.GetFileName(view) != UnityEngine.ProjectInfo.ViewFolder
                    || !string.Equals(unbound, Path.TrimEndingDirectorySeparator(Path.GetFullPath(UnityEngine.ProjectInfo.UnboundDirectory(UnityEngine.ProjectInfo.ProjectPath))), StringComparison.Ordinal))
                    return null;
                string project = UnityEngine.ProjectInfo.ProjectRootPath;
                if (string.IsNullOrEmpty(project))
                {
                    if (Path.GetFileName(unbound) != UnityEngine.ProjectInfo.UnboundFolder) return null;
                    project = Path.GetDirectoryName(unbound);
                }
                return new ProjectView(view, Path.TrimEndingDirectorySeparator(Path.GetFullPath(project)), unbound);
            }

            /// <summary>Whether the volume holding <paramref name="directory"/> finds it under another case.</summary>
            static bool IgnoresCase(string directory)
            {
                string name = Path.GetFileName(directory);
                var swapped = new char[name.Length];
                for (int i = 0; i < name.Length; i++)
                    swapped[i] = char.IsUpper(name[i]) ? char.ToLowerInvariant(name[i]) : char.ToUpperInvariant(name[i]);
                string other = new string(swapped);
                return other != name && Directory.Exists(Path.Combine(Path.GetDirectoryName(directory), other));
            }

            /// <summary>The view entry a path names when it names the project's own entry (a top-level
            /// link, the Packages folder, an embedded package's link), with that entry; else null.</summary>
            internal string ProjectEntryOf(string full, out string projectEntry)
            {
                projectEntry = null;
                string inView = Below(full, m_View) ?? Below(full, m_PhysicalView);
                if (string.IsNullOrEmpty(inView)) return null;
                string[] parts = inView.Split('/');
                bool packages = string.Equals(parts[0], "Packages", m_Case);
                if (packages && parts.Length == 1)
                {
                    projectEntry = Path.Combine(m_Project, "Packages");
                    return Path.Combine(m_View, "Packages");
                }
                if (parts.Length != (packages ? 2 : 1)) return null;
                string link = packages ? Path.Combine(m_View, "Packages", parts[1]) : Path.Combine(m_View, parts[0]);
                string target = new FileInfo(link).LinkTarget;
                if (target == null) return null;
                target = Path.GetFullPath(target, Path.GetDirectoryName(link));
                if (packages && Below(ConsoleLogPath.Resolve(target), m_PhysicalEmbedded) is not { Length: > 0 }) return null;
                projectEntry = target;
                return link;
            }

            /// <summary>The project's path for a path that names an entry directly in the view or in
            /// its Packages folder, one of the view's links or a name nothing has yet, with the
            /// view's entry; null for any other path, also for an entry a script made in the view
            /// itself.</summary>
            internal string ProjectPathOf(string full, out string viewEntry)
            {
                viewEntry = null;
                string inView = Below(full, m_View) ?? Below(full, m_PhysicalView);
                if (string.IsNullOrEmpty(inView)) return null;
                string[] parts = inView.Split('/');
                bool packages = string.Equals(parts[0], "Packages", m_Case);
                if (parts.Length != (packages && parts.Length > 1 ? 2 : 1)) return null;
                string entry = Path.Combine(m_View, inView);
                if (packages && parts.Length == 1)
                {
                    viewEntry = entry;
                    return Path.Combine(m_Project, "Packages");
                }
                string target = new FileInfo(entry).LinkTarget;
                if (target == null && (File.Exists(entry) || Directory.Exists(entry))) return null;
                viewEntry = entry;
                return target != null ? Path.GetFullPath(target, Path.GetDirectoryName(entry)) : Path.Combine(m_Project, inView);
            }

            /// <summary>Whether the deletion of <paramref name="full"/> would land on the project
            /// folder, Library, Unbound's directory or below it, or in a package kept elsewhere.</summary>
            internal bool Refuses(string full)
            {
                // Spelled into Unbound's directory: by its own path, or, where it is in the
                // project, beside the view's links (where a new name is the project's).
                string inView = Below(full, m_View) ?? Below(full, m_PhysicalView);
                if (Below(full, m_Unbound) != null && inView == null) return true;
                if (Below(m_Unbound, m_Project) is { Length: > 0 } place && inView != null && (Same(inView, place) || inView.StartsWith(place + "/", m_Case)))
                    return true;
                // Where the entry itself lands: its folder's links followed, not its own.
                string parent = Path.GetDirectoryName(full);
                string landing = parent == null ? full : Path.Combine(ConsoleLogPath.Resolve(parent), Path.GetFileName(full));
                if (Same(landing, m_PhysicalProject) || Same(landing, m_PhysicalLibrary)) return true;
                // Unbound's directory (LibraryUnbound) and what is in it, except the view's own entries (links, handled
                // above, and what a script wrote in its working directory) named in the view
                // itself: one reached through a link into it (a project's own link) is refused.
                if (Below(landing, m_PhysicalUnbound) != null && !(inView is { Length: > 0 } && Below(landing, m_PhysicalView) is { Length: > 0 } inViewLanding
                        && Same(inViewLanding, inView)))
                    return true;
                // A folder that holds one of those goes with it.
                if (Below(m_PhysicalProject, landing) != null || Below(m_PhysicalUnbound, landing) != null) return true;
                // A package kept elsewhere: what is in it, a folder holding it, and the view's link to it.
                string link = new FileInfo(full).LinkTarget;
                string linked = link == null || parent == null ? null : ConsoleLogPath.Resolve(Path.GetFullPath(link, parent));
                foreach (string root in m_OtherPackages)
                    if (Below(landing, root) != null || Below(root, landing) != null || (linked != null && Same(linked, root))) return true;
                return false;
            }

            bool Same(string a, string b) => string.Equals(a, b, m_Case);

            /// <summary><paramref name="path"/> relative to <paramref name="root"/> ("" for the root
            /// itself), or null outside it.</summary>
            string Below(string path, string root)
            {
                if (string.Equals(path, root, m_Case)) return "";
                return path.StartsWith(root + "/", m_Case) ? path.Substring(root.Length + 1) : null;
            }
        }
    }
}
