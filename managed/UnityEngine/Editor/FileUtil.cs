using System;
using System.IO;

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
        /// deleted tree are removed, not followed; a path the process may not delete gives false.
        /// </summary>
        public static bool DeleteFileOrDirectory(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path == string.Empty) throw new ArgumentException("path", "The path cannot be empty.");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('\\', '/')));
            try
            {
                string viewLink = ProjectViewLink(full, out string projectEntry);
                if (projectEntry != null)
                {
                    if (!Delete(projectEntry)) return false;
                    // The view's link to it goes too, as if the entry had never been there.
                    if (viewLink != null && new FileInfo(viewLink).LinkTarget != null) File.Delete(viewLink);
                    return true;
                }
                return Delete(full);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

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
        /// a directory of links to each top-level project entry and to each <c>Packages/&lt;name&gt;</c>.
        /// A path naming one of those links, the view's <c>Packages</c> folder or the view itself
        /// names the project's own entry, which is returned with the view link (null for the folder
        /// and the view); paths below them already reach the project's files through the link.
        /// </summary>
        static string ProjectViewLink(string full, out string projectEntry)
        {
            projectEntry = null;
            string view = UnityEngine.ProjectInfo.ProjectPath;
            if (string.IsNullOrEmpty(view)) return null;
            view = Path.TrimEndingDirectorySeparator(Path.GetFullPath(view));
            string unbound = Path.GetDirectoryName(view), library = Path.GetDirectoryName(unbound);
            if (Path.GetFileName(view) != "ProjectView" || Path.GetFileName(unbound) != "Unbound" || Path.GetFileName(library) != "Library")
                return null;
            string project = Path.GetDirectoryName(library);
            // Compared as physical paths: the working directory is spelled without the links above
            // it (a temporary or home directory may be one), the configured view with them.
            string physicalView = Unbound.Runtime.ConsoleLogPath.Resolve(view);
            string physicalPackages = Path.Combine(physicalView, "Packages");
            string physicalFull = Unbound.Runtime.ConsoleLogPath.Resolve(full);
            if (physicalFull == physicalView)
            {
                projectEntry = project;
                return null;
            }
            if (physicalFull == physicalPackages)
            {
                projectEntry = Path.Combine(project, "Packages");
                return null;
            }
            string parent = Path.GetDirectoryName(full);
            string physicalParent = Unbound.Runtime.ConsoleLogPath.Resolve(parent);
            if (physicalParent != physicalView && physicalParent != physicalPackages) return null;
            string target = new FileInfo(full).LinkTarget;
            if (target == null) return null;
            projectEntry = Path.GetFullPath(target, parent);
            return full;
        }
    }
}
