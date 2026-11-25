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
    /// load makes and the work it put off (its forced imports are in the load's own call; its other
    /// imports and its replayed postprocessing are not reported), scripts and assembly definitions
    /// (not measured: Unbound's session started again for them lists nothing), and what is not
    /// measured: the save as the Editor exits, a sprite atlas pack's saves, a bake's files, a
    /// package's import, SetLabels.
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

        /// <summary>A script or assembly definition: its import, move or deletion compiles the scripts
        /// again (the Editor reloads its domain; Unbound starts its session again, whose start lists
        /// nothing): not reported, not measured.</summary>
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
            if (!Reported || string.IsNullOrEmpty(path) || IsScript(path)) return;
            if (s_DeletedSet.Add(path)) s_Deleted.Add(path);
            Ended();
        }

        internal static void Moved(string from, string to)
        {
            if (!Reported || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || IsScript(to)) return;
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
            if (AssetDatabase.Editing || AssetPostprocessing.InRound || s_Imported.Count < 2 || !s_ImportedSet.Contains(path)) return;
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

        static bool s_Flushing;

        /// <summary>The rounds a flush makes at most: a postprocessor that writes at every round
        /// would never let it end.</summary>
        const int MaxRounds = 100;

        /// <summary>An OnPostprocessAllAssets round (any: a session write's, a domain load's, a VFX
        /// import's) has ended: what its callbacks wrote is reported now.</summary>
        internal static void RoundEnded()
        {
            if (!s_Flushing && s_Depth == 0 && !AssetDatabase.Editing) Flush();
        }

        /// <summary>Reports what was gathered (one call) and starts afresh; nothing when nothing was.
        /// What the callbacks themselves write is reported in a call of its own once they have all
        /// run (Scripting API 6000.0, AssetPostprocessor.OnPostprocessAllAssets: imports a callback
        /// causes call it again once they are complete; not measured).</summary>
        internal static void Flush()
        {
            if (s_Flushing || AssetPostprocessing.InRound) return;
            s_Flushing = true;
            try { FlushRounds(); }
            finally { s_Flushing = false; }
        }

        static void FlushRounds()
        {
            for (int round = 0; s_Imported.Count > 0 || s_Deleted.Count > 0 || s_Moved.Count > 0; round++)
            {
                if (round == MaxRounds)
                {
                    UnityEngine.Debug.LogError($"Unbound: OnPostprocessAllAssets callbacks wrote assets at each of {MaxRounds} rounds; what they wrote last is not reported to them ({string.Join(", ", s_Imported)}).");
                    Clear();
                    return;
                }
                var imported = s_Imported.ToArray();
                var deleted = s_Deleted.ToArray();
                var moved = s_Moved.ToArray();
                var movedFrom = s_MovedFrom.ToArray();
                Clear();
                Report(imported, deleted, moved, movedFrom);
            }
        }

        static void Clear()
        {
            s_Imported.Clear();
            s_ImportedSet.Clear();
            s_Deleted.Clear();
            s_DeletedSet.Clear();
            s_Moved.Clear();
            s_MovedFrom.Clear();
        }
    }
}
