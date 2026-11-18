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
        /// <c>Packages/&lt;name&gt;</c>. Unbound's reading (unmeasured): a path naming a top-level
        /// link, the view's <c>Packages</c> folder or an embedded package's link (one whose package
        /// is in the project's Packages folder) names the project's own entry; paths below those
        /// links reach the project's files through them. Refused and reported, as what the Editor
        /// does there is unmeasured or would take Unbound's own files: a package kept elsewhere (the
        /// Editor's built-in packages, a package cache or store, a local package outside Packages)
        /// and everything below it, the project folder, its Library folder and Library/Unbound with
        /// everything in it (also by the project's own path).
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
            // Both spellings: the working directory is spelled without the links above it (a
            // temporary or home directory may be one), the configured view with them.
            string inView = Relative(full, view);
            if (inView == null)
            {
                string inProject = Relative(full, project);
                return inProject != null && Protected(inProject) ? ViewEntry.Unsupported : ViewEntry.None;
            }
            if (Protected(inView)) return ViewEntry.Unsupported;
            string[] parts = inView.Split('/');
            string link = Path.Combine(view, parts[0]);
            if (parts[0] == "Packages")
            {
                if (parts.Length == 1)
                {
                    projectEntry = Path.Combine(project, "Packages");
                    viewEntry = link;
                    return ViewEntry.ProjectEntry;
                }
                link = Path.Combine(link, parts[1]);
                string package = new FileInfo(link).LinkTarget;
                if (package == null) return ViewEntry.None;
                package = Path.GetFullPath(package, Path.GetDirectoryName(link));
                // A package that is not embedded is not the project's, at any depth below its link.
                if (Relative(package, Path.Combine(project, "Packages")) is not string embedded || embedded.Length == 0)
                    return Path.Exists(package) ? ViewEntry.Unsupported : ViewEntry.None;
                if (parts.Length > 2) return ViewEntry.None;
                projectEntry = package;
                viewEntry = link;
                return ViewEntry.ProjectEntry;
            }
            if (parts.Length > 1) return ViewEntry.None;
            string target = new FileInfo(link).LinkTarget;
            if (target == null) return ViewEntry.None;
            projectEntry = Path.GetFullPath(target, view);
            viewEntry = link;
            return ViewEntry.ProjectEntry;
        }

        /// <summary>The folders whose loss would take the project or Unbound's own files with them.</summary>
        static bool Protected(string relative) =>
            relative.Length == 0 || relative == "Library" || relative == "Library/Unbound" || relative.StartsWith("Library/Unbound/", StringComparison.Ordinal);

        /// <summary><paramref name="path"/> below <paramref name="root"/> ("" for the root itself),
        /// as spelled or physically, or null outside it.</summary>
        static string Relative(string path, string root)
        {
            foreach (string spelling in new[] { root, ConsoleLogPath.Resolve(root) })
            {
                if (path == spelling) return "";
                if (path.StartsWith(spelling + "/", StringComparison.Ordinal)) return path.Substring(spelling.Length + 1);
            }
            return null;
        }
    }
}
