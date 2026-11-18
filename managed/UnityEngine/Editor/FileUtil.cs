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

        internal const string UnsupportedMember = "FileUtil.DeleteFileOrDirectory of the project folder, its Library folder, Library/Unbound and what is in it, or a package not embedded in the project's Packages folder and what is in it";

        static bool Delete(string full)
        {
            var entry = new FileInfo(full);
            if (entry.LinkTarget != null || File.Exists(full)) entry.Delete();
            else if (Directory.Exists(full)) Directory.Delete(full, true);
            else return false;
            return true;
        }

        /// <summary>
        /// The game's working directory is Unbound's project view (<c>Library/Unbound/ProjectView</c>):
        /// a directory of links to each top-level project entry and to each package as
        /// <c>Packages/&lt;name&gt;</c>. Unbound's reading (unmeasured): a path naming a top-level
        /// link, the view's <c>Packages</c> folder or an embedded package's link (one whose package
        /// is in the project's Packages folder) names the project's own entry; paths below those
        /// links reach the project's files through them. Refused and reported, decided on where a
        /// path lands (its links followed, and without regard to case on a volume that ignores it),
        /// as what the Editor does there is unmeasured or would take Unbound's own files: the project
        /// folder, its Library folder, Library/Unbound and what is in it, and a package kept
        /// elsewhere (the Editor's built-in packages, a package cache or store, a local package
        /// outside Packages) and what is in it.
        /// </summary>
        sealed class ProjectView
        {
            readonly string m_View, m_Project;
            readonly StringComparison m_Case;
            readonly string m_PhysicalView, m_PhysicalProject, m_PhysicalLibrary, m_PhysicalUnbound, m_PhysicalEmbedded;
            readonly List<string> m_OtherPackages = new List<string>();

            ProjectView(string view, string project)
            {
                m_View = view;
                m_Project = project;
                m_Case = IgnoresCase(view) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                m_PhysicalView = ConsoleLogPath.Resolve(view);
                m_PhysicalProject = ConsoleLogPath.Resolve(project);
                m_PhysicalLibrary = Path.Combine(m_PhysicalProject, "Library");
                m_PhysicalUnbound = Path.Combine(m_PhysicalLibrary, "Unbound");
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
                string unbound = Path.GetDirectoryName(view), library = Path.GetDirectoryName(unbound);
                if (Path.GetFileName(view) != "ProjectView" || Path.GetFileName(unbound) != "Unbound" || Path.GetFileName(library) != "Library")
                    return null;
                return new ProjectView(view, Path.GetDirectoryName(library));
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

            /// <summary>Whether the deletion of <paramref name="full"/> would land on the project
            /// folder, Library, Library/Unbound or below it, or in a package kept elsewhere.</summary>
            internal bool Refuses(string full)
            {
                // Where the entry itself lands: its folder's links followed, not its own.
                string parent = Path.GetDirectoryName(full);
                string landing = parent == null ? full : Path.Combine(ConsoleLogPath.Resolve(parent), Path.GetFileName(full));
                if (Same(landing, m_PhysicalProject) || Same(landing, m_PhysicalLibrary)) return true;
                // Library/Unbound and what is in it, except the view's own entries (links, handled
                // above, and what a script wrote in its working directory).
                if (Below(landing, m_PhysicalUnbound) != null && Below(landing, m_PhysicalView) is not { Length: > 0 }) return true;
                // A folder that holds one of those goes with it.
                if (Below(m_PhysicalProject, landing) != null) return true;
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
