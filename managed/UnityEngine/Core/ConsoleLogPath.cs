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
            string projectPath = string.IsNullOrEmpty(project) ? null : Resolve(project);
            string projectRoot = projectPath == null ? null : System.IO.Path.Combine(projectPath, "Library", "Unbound");
            if (projectPath != null)
                foreach (string authored in new[] { "Assets", "Packages", "ProjectSettings" })
                {
                    string authoredRoot = System.IO.Path.Combine(projectPath, authored);
                    if (Inside(resolved, authoredRoot, StringComparison.OrdinalIgnoreCase) ||
                        Inside(resolved, Resolve(authoredRoot), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("log path cannot write the project's authored files");
                }
            // The global cache itself may be relocated by symlink, as supported by cache migration.
            string cacheRoot = Resolve(System.IO.Path.Combine(cacheHome, "unbound"));
            // Match tests/probe_paths.py's existing cache_artifact_path policy: validation
            // has its own supported disk-migration symlink on this machine.
            string validationRoot = Resolve(System.IO.Path.Combine(cacheHome, "unbound", "validation"));
            if (!Inside(resolved, projectRoot) && !Inside(resolved, cacheRoot) && !Inside(resolved, validationRoot))
                throw new IOException("log path must be inside this project's Library/Unbound or the global XDG cache/unbound directory");
            return resolved;
        }

        static bool Inside(string path, string root, StringComparison comparison = StringComparison.Ordinal) => root != null &&
            path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, comparison);

        // Walk existing ancestors as well as the final entry. LinkTarget also sees dangling links;
        // link targets are walked again so symlinks in their ancestors cannot escape validation.
        // A bounded link count refuses loops. Mutations use the returned path.
        static string Resolve(string path, int links = 0)
        {
            if (links > 40) throw new IOException("too many log path symlinks");
            path = System.IO.Path.GetFullPath(path);
            string current = System.IO.Path.GetPathRoot(path);
            foreach (string part in path.Substring(current.Length).Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, part);
                var entry = new FileInfo(current);
                string target = entry.LinkTarget;
                if (target != null)
                    current = Resolve(System.IO.Path.GetFullPath(target, System.IO.Path.GetDirectoryName(current)), links + 1);
            }
            return System.IO.Path.GetFullPath(current);
        }
    }
}
