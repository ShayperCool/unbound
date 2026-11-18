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
        /// read-only files go too; a symbolic link is removed, not what it points to (also inside a
        /// tree); a .meta file beside the path stays; nothing is logged. A path the process may not
        /// delete gives false (unmeasured).
        /// </summary>
        public static bool DeleteFileOrDirectory(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (path == string.Empty) throw new ArgumentException("path", "The path cannot be empty.");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Replace('\\', '/')));
            var entry = new FileInfo(full);
            try
            {
                if (entry.LinkTarget != null || File.Exists(full)) entry.Delete();
                else if (Directory.Exists(full)) Directory.Delete(full, true);
                else return false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
            return true;
        }
    }
}
