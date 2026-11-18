using System;
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
        /// paths through the project view, see <see cref="ThroughProjectView"/>.
        /// </summary>
        public static bool DeleteFileOrDirectory(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path == string.Empty) throw new ArgumentException("path", "The path cannot be empty.");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('\\', '/')));
            try
            {
                switch (ThroughProjectView(full, out string projectEntry, out string viewEntry))
                {
                    case ViewEntry.None:
                        return Delete(full);
                    case ViewEntry.Unsupported:
                        Stubs.NotImplemented(UnsupportedMember);
                        return false;
                    default:
                        if (!Delete(projectEntry)) return false;
                        // The view's own entry goes too (only links live there), as if the project's
                        // entry had never been there.
                        if (new FileInfo(viewEntry).LinkTarget != null) File.Delete(viewEntry);
                        else if (Directory.Exists(viewEntry)) Directory.Delete(viewEntry, true);
                        return true;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal const string UnsupportedMember = "FileUtil.DeleteFileOrDirectory of the project folder or of a package that is not embedded in the project's Packages folder";

        static bool Delete(string full)
        {
            var entry = new FileInfo(full);
            if (entry.LinkTarget != null || File.Exists(full)) entry.Delete();
            else if (Directory.Exists(full)) Directory.Delete(full, true);
            else return false;
            return true;
        }

        enum ViewEntry { None, ProjectEntry, Unsupported }

        /// <summary>
        /// The game's working directory is Unbound's project view (<c>Library/Unbound/ProjectView</c>):
        /// a directory of links to each top-level project entry and to each package as
        /// <c>Packages/&lt;name&gt;</c>. A path naming a top-level link, the view's <c>Packages</c>
        /// folder or an embedded package's link (one in the project's Packages folder) names the
        /// project's own entry (Unbound's reading, unmeasured). The view itself (the project folder)
        /// and a package kept elsewhere (the Editor's built-in packages, a package cache or store, a
        /// local package outside Packages) are not deleted and are reported: what the Editor does
        /// with them is unmeasured. Paths below those links reach the project's files through them.
        /// </summary>
        static ViewEntry ThroughProjectView(string full, out string projectEntry, out string viewEntry)
        {
            projectEntry = viewEntry = null;
            string view = UnityEngine.ProjectInfo.ProjectPath;
            if (string.IsNullOrEmpty(view)) return ViewEntry.None;
            view = Path.TrimEndingDirectorySeparator(Path.GetFullPath(view));
            string unbound = Path.GetDirectoryName(view), library = Path.GetDirectoryName(unbound);
            if (Path.GetFileName(view) != "ProjectView" || Path.GetFileName(unbound) != "Unbound" || Path.GetFileName(library) != "Library")
                return ViewEntry.None;
            string project = Path.GetDirectoryName(library);
            // Compared as physical paths: the working directory is spelled without the links above
            // it (a temporary or home directory may be one), the configured view with them.
            string physicalView = ConsoleLogPath.Resolve(view);
            string physicalPackages = Path.Combine(physicalView, "Packages");
            string physicalFull = ConsoleLogPath.Resolve(full);
            if (physicalFull == physicalView) return ViewEntry.Unsupported;
            if (physicalFull == physicalPackages)
            {
                projectEntry = Path.Combine(project, "Packages");
                viewEntry = full;
                return ViewEntry.ProjectEntry;
            }
            string parent = Path.GetDirectoryName(full);
            string physicalParent = ConsoleLogPath.Resolve(parent);
            if (physicalParent != physicalView && physicalParent != physicalPackages) return ViewEntry.None;
            string target = new FileInfo(full).LinkTarget;
            if (target == null) return ViewEntry.None;
            target = Path.GetFullPath(target, parent);
            if (physicalParent == physicalPackages && !Path.Exists(target)) return ViewEntry.None;
            if (physicalParent == physicalPackages)
            {
                string embedded = ConsoleLogPath.Resolve(Path.Combine(project, "Packages")) + Path.DirectorySeparatorChar;
                string physicalTarget = ConsoleLogPath.Resolve(Path.GetDirectoryName(target)) + Path.DirectorySeparatorChar + Path.GetFileName(target);
                if (!physicalTarget.StartsWith(embedded, StringComparison.Ordinal)) return ViewEntry.Unsupported;
            }
            projectEntry = target;
            viewEntry = full;
            return ViewEntry.ProjectEntry;
        }
    }
}
