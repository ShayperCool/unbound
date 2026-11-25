using System;
using System.Collections.Generic;

namespace UnityEditor
{
    /// <summary>
    /// What a session's writes import, delete and move, reported to OnPostprocessAllAssets as the
    /// Editor reports its imports (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave
    /// session-imports): one call per write, made before the write returns, with
    /// <c>didDomainReload</c> false; nothing for a write that changed no file (a clean
    /// SaveAssets); the writes between StartAssetEditing and the last StopAssetEditing in one call
    /// there. A write is the outermost <see cref="Write"/> scope: the public AssetDatabase call
    /// and what it does inside; in Edit mode and in Play mode alike (tests/projects/firstimport
    /// open3: a render pipeline's save in Play mode). Not reported: the writes the editor domain's
    /// load makes and the imports it put off (the load's own call reports them), scripts and
    /// assembly definitions (their domain reload's call), and what is not measured: the save as the
    /// Editor exits, a sprite atlas pack's saves, a bake's files, a package's import, SetLabels.
    /// </summary>
    internal static class SessionImports
    {
        static readonly List<string> s_Imported = new List<string>();
        static readonly HashSet<string> s_ImportedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> s_DeletedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> s_Deleted = new List<string>();
        static readonly List<string> s_Moved = new List<string>();
        static readonly List<string> s_MovedFrom = new List<string>();
        static int s_Depth;

        sealed class Scope : IDisposable
        {
            internal static readonly Scope Instance = new Scope();

            public void Dispose()
            {
                if (s_Depth > 0 && --s_Depth == 0 && !AssetDatabase.Editing) Flush();
            }
        }

        /// <summary>A write: what it imports, deletes and moves is reported as it ends (when it is
        /// the outermost one and no asset editing holds it back).</summary>
        internal static IDisposable Write()
        {
            s_Depth++;
            return Scope.Instance;
        }

        static int s_Muted;

        sealed class MuteScope : IDisposable
        {
            internal static readonly MuteScope Instance = new MuteScope();
            public void Dispose() => s_Muted--;
        }

        /// <summary>A part of a write that reports what it does itself (a move: its imports are
        /// not what the Editor reports).</summary>
        internal static IDisposable Muted()
        {
            s_Muted++;
            return MuteScope.Instance;
        }

        static bool Reported => s_Muted == 0 && !EditorInitialization.DomainLoading;

        internal static void Imported(string path)
        {
            if (!Reported || string.IsNullOrEmpty(path) || IsScript(path)) return;
            if (s_ImportedSet.Add(path)) s_Imported.Add(path);
            Ended();
        }

        /// <summary>A script or assembly definition: its import compiles the scripts and reloads the
        /// domain, whose own call reports it (not reported here: not measured).</summary>
        static bool IsScript(string path) =>
            path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase);

        /// <summary>A file of the project written (a .meta for its asset): its asset imported.
        /// Only assets (Assets/, Packages/) are reported.</summary>
        internal static void ImportedFile(string file)
        {
            var path = ProjectImports.AssetPathOf(file);
            if (path != null && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)))
                Imported(path);
        }

        internal static void Deleted(string path)
        {
            if (!Reported || string.IsNullOrEmpty(path)) return;
            if (s_DeletedSet.Add(path)) s_Deleted.Add(path);
            Ended();
        }

        internal static void Moved(string from, string to)
        {
            if (!Reported || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return;
            s_Moved.Add(to);
            s_MovedFrom.Add(from);
            Ended();
        }

        /// <summary>A write outside any scope is its own call.</summary>
        static void Ended()
        {
            if (s_Depth == 0 && !AssetDatabase.Editing) Flush();
        }

        /// <summary>Reports <paramref name="path"/> alone, gathered as imported with more, ahead of the
        /// rest (a copied folder before what it holds).</summary>
        internal static void ReportFirst(string path)
        {
            // (Only a folder's own copy, outside asset editing, which reports its writes together.)
            if (AssetDatabase.Editing || s_Dispatching || s_Imported.Count < 2 || !s_ImportedSet.Contains(path)) return;
            foreach (var other in s_Imported)
                if (other != path && !other.StartsWith(path + "/", StringComparison.Ordinal)) return;
            s_Imported.Remove(path);
            s_ImportedSet.Remove(path);
            Report(new[] { path }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }

        static Action<string[], string[], string[], string[]> s_ReportForTests;

        /// <summary>Reports go to <paramref name="report"/> instead of the postprocessors until
        /// disposed (tests).</summary>
        internal static IDisposable ReportsForTests(Action<string[], string[], string[], string[]> report)
        {
            s_ReportForTests = report;
            return new Restore();
        }

        sealed class Restore : IDisposable
        {
            public void Dispose() => s_ReportForTests = null;
        }

        static void Report(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (s_ReportForTests != null) s_ReportForTests(imported, deleted, moved, movedFrom);
            else AssetPostprocessing.SessionImported(imported, deleted, moved, movedFrom);
        }

        /// <summary>Reports what was gathered (one call) and starts afresh; nothing when nothing was.</summary>
        static bool s_Dispatching;

        /// <summary>Reports what was gathered (one call) and starts afresh; nothing when nothing was.
        /// What the callbacks themselves write is reported in a call of its own once they have all
        /// run (Scripting API 6000.0, AssetPostprocessor.OnPostprocessAllAssets: imports a callback
        /// causes call it again once they are complete).</summary>
        internal static void Flush()
        {
            if (s_Dispatching) return;
            while (s_Imported.Count > 0 || s_Deleted.Count > 0 || s_Moved.Count > 0)
            {
                var imported = s_Imported.ToArray();
                var deleted = s_Deleted.ToArray();
                var moved = s_Moved.ToArray();
                var movedFrom = s_MovedFrom.ToArray();
                s_Imported.Clear();
                s_ImportedSet.Clear();
                s_Deleted.Clear();
                s_DeletedSet.Clear();
                s_Moved.Clear();
                s_MovedFrom.Clear();
                s_Dispatching = true;
                try { Report(imported, deleted, moved, movedFrom); }
                finally { s_Dispatching = false; }
            }
        }
    }
}
