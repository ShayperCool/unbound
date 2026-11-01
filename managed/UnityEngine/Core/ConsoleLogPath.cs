using System;
using System.Collections.Generic;
using System.IO;

namespace Unbound.Runtime
{
    /// <summary>Only generated-output locations may receive a session log or its rotated copy.</summary>
    internal static class ConsoleLogPath
    {
        internal static string Validate(string path, string project, string cacheHome) =>
            new Policy(project, cacheHome).Validate(path);

        // One read-only snapshot per Open, shared by the main and rotation destinations.
        // Only the authored hierarchy and its reachable aliases are enumerated. In particular,
        // allowing a cache root does not cause that cache to be scanned.
        internal sealed class Policy
        {
            readonly HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readonly HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readonly string projectRoot, cacheRoot, validationRoot;

            internal Policy(string project, string cacheHome)
            {
                try
                {
                    if (!string.IsNullOrEmpty(project))
                    {
                        string physicalProject = Resolve(project);
                        projectRoot = System.IO.Path.Combine(physicalProject, "Library", "Unbound");
                        var visited = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string part in new[] { "Assets", "Packages", "ProjectSettings" })
                        {
                            directories.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(project, part)));
                            string root = System.IO.Path.Combine(physicalProject, part);
                            directories.Add(root);
                            // Missing authored roots are normal for a new project. A dangling root
                            // alias, unlike an absent root, must be rejected by Snapshot.
                            if (new FileInfo(root).LinkTarget == null && !Attributes(root).HasValue) continue;
                            Snapshot(root, visited);
                        }
                    }
                    cacheRoot = Resolve(System.IO.Path.Combine(cacheHome, "unbound"));
                    validationRoot = Resolve(System.IO.Path.Combine(cacheHome, "unbound", "validation"));
                }
                catch (Exception e)
                {
                    throw new IOException("cannot establish authored log-path membership: " + e.Message, e);
                }
            }

            internal string Validate(string path)
            {
                string resolved = Resolve(path, Veto);
                if (!Inside(resolved, projectRoot) && !Inside(resolved, cacheRoot) && !Inside(resolved, validationRoot))
                    throw new IOException("log path must be inside this project's Library/Unbound or the global XDG cache/unbound directory");
                return resolved;
            }

            void Veto(string path)
            {
                // Check lexical membership before resolving any alias, including each target
                // and traversed ancestor. Directory membership covers its entire subtree.
                if (files.Contains(path)) throw new IOException("log path cannot write the project's authored files");
                for (string parent = path; parent != null; parent = System.IO.Path.GetDirectoryName(parent))
                    if (directories.Contains(parent))
                        throw new IOException("log path cannot write the project's authored files");
            }

            void Snapshot(string root, HashSet<string> visited)
            {
                var active = new HashSet<string>(StringComparer.Ordinal);
                var stack = new Stack<(string path, IEnumerator<string> entries)>();
                void Visit(string path, bool canonicalParent = false)
                {
                    string target = new FileInfo(path).LinkTarget;
                    string resolved = canonicalParent && target == null ? path : Resolve(path);
                    var attributes = Attributes(resolved);
                    if (!attributes.HasValue) throw new IOException("unresolvable authored entry: " + path);
                    bool directory = (attributes.Value & FileAttributes.Directory) != 0;
                    var membership = directory ? directories : files;
                    membership.Add(path);
                    if (target != null)
                        membership.Add(System.IO.Path.GetFullPath(target, System.IO.Path.GetDirectoryName(path)));
                    membership.Add(resolved);
                    if (!directory) return;
                    if (active.Contains(resolved)) throw new IOException("authored directory alias cycle: " + path);
                    if (!visited.Add(resolved)) return;
                    active.Add(resolved);
                    stack.Push((resolved, Directory.EnumerateFileSystemEntries(resolved).GetEnumerator()));
                }
                try
                {
                    Visit(root);
                    while (stack.Count != 0)
                    {
                        var top = stack.Peek();
                        if (top.entries.MoveNext()) Visit(top.entries.Current, true);
                        else
                        {
                            top.entries.Dispose();
                            stack.Pop();
                            active.Remove(top.path);
                        }
                    }
                }
                finally
                {
                    foreach (var entry in stack) entry.entries.Dispose();
                }
            }
        }

        static FileAttributes? Attributes(string path)
        {
            try { return File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        static bool Inside(string path, string root) => root != null &&
            path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);

        // Preserve membership before resolution discards a spelling. LinkTarget sees dangling
        // links, and targets are walked again so aliases in their ancestors are checked too.
        static string Resolve(string path, Action<string> check = null, int links = 0)
        {
            if (links > 40) throw new IOException("too many log path symlinks");
            path = System.IO.Path.GetFullPath(path);
            check?.Invoke(path);
            string current = System.IO.Path.GetPathRoot(path);
            foreach (string part in path.Substring(current.Length).Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, part);
                check?.Invoke(current);
                Attributes(current); // Access errors must not silently discard a branch.
                string target = new FileInfo(current).LinkTarget;
                if (target != null)
                    current = Resolve(System.IO.Path.GetFullPath(target, System.IO.Path.GetDirectoryName(current)), check, links + 1);
                check?.Invoke(current);
            }
            return current;
        }
    }
}
