using System;
using System.IO;

namespace Unbound.Runtime
{
    /// <summary>Only generated-output locations may receive a session log or its rotated copy.</summary>
    internal static class ConsoleLogPath
    {
        internal static string Validate(string path, string project, string cacheHome)
        {
            string resolved = Resolve(path);
            // Resolve project aliases, but do not bless a Library symlink into authored content.
            string projectRoot = string.IsNullOrEmpty(project) ? null : System.IO.Path.Combine(Resolve(project), "Library", "Unbound");
            // The global cache itself may be relocated by symlink, as supported by cache migration.
            string cacheRoot = Resolve(System.IO.Path.Combine(cacheHome, "unbound"));
            if (!Inside(resolved, projectRoot) && !Inside(resolved, cacheRoot))
                throw new IOException("log path must be inside this project's Library/Unbound or the global XDG cache/unbound directory");
            return resolved;
        }

        static bool Inside(string path, string root) => root != null &&
            path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);

        // Walk existing ancestors as well as the final entry. LinkTarget also sees dangling links;
        // ResolveLinkTarget fails closed on broken chains and loops. Mutations use the returned path.
        static string Resolve(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            string current = System.IO.Path.GetPathRoot(path);
            foreach (string part in path.Substring(current.Length).Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, part);
                var entry = new FileInfo(current);
                if (entry.LinkTarget != null)
                    current = entry.ResolveLinkTarget(true)?.FullName ?? throw new IOException("cannot resolve log path symlink");
            }
            return System.IO.Path.GetFullPath(current);
        }
    }
}
