using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Unbound
{
    /// <summary>
    /// Marks a generated compatibility stub (Generated/&lt;release&gt;/*.g.cs): a type or member that
    /// exists so game code binds, but has no implementation yet. The API gap report counts stubs as
    /// missing. A few stubs are declared in hand-written sources, written as the generator writes
    /// them, only because hand-written code names or overrides them and must build without the
    /// generated files; gen-stubs checks those signatures against the release's reference assemblies.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    internal sealed class StubAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a hand-written member that does nothing, or answers a constant, because the Editor does
    /// the same (an Android API in the Linux Editor, a player-only setting). <c>unbound api-check</c>
    /// accepts empty bodies and constant answers only with this or a <see cref="Runtime.Stubs"/> call.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor
                    | AttributeTargets.Event | AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    internal sealed class EditorFaithfulAttribute : Attribute
    {
        public EditorFaithfulAttribute(string why)
        {
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>What the bodies of generated stubs call.</summary>
    internal static class Stubs
    {
        static readonly ConcurrentDictionary<string, int> Hits = new(StringComparer.Ordinal);

        /// <summary>Records a call of an unimplemented member; the first call of each logs a
        /// warning.</summary>
        public static void NotImplemented(string member)
        {
            // Counted before logging, so a stub reached while logging does not log again.
            if (Hits.AddOrUpdate(member, 1, (_, n) => n + 1) == 1)
            {
                UnityEngine.Debug.LogWarning(Trace.StubsOn
                    ? "Unbound: " + member + " is not implemented yet, called\n" + new System.Diagnostics.StackTrace(1, true)
                    : "Unbound: " + member + " is not implemented yet");
            }
        }

        /// <summary>Records a use of a hand-written member that Unbound does not yet honour as Unity
        /// does (a setting stored but not acted on, a mode drawn as another); the first use of each
        /// logs a warning, as a stub's does.</summary>
        public static void Ignored(string member, string what = null)
        {
            if (Hits.AddOrUpdate(member, 1, (_, n) => n + 1) == 1)
            {
                var message = "Unbound: " + member + (what != null ? " " + what : " is not honoured yet");
                UnityEngine.Debug.LogWarning(Trace.StubsOn
                    ? message + ", used\n" + new System.Diagnostics.StackTrace(1, true)
                    : message);
            }
        }

        /// <summary>The stubs called so far and how often, most called first.</summary>
        public static IReadOnlyList<KeyValuePair<string, int>> HitReport() =>
            Hits.OrderByDescending(h => h.Value).ThenBy(h => h.Key, StringComparer.Ordinal).ToList();

        /// <summary>The file in the project's Unbound directory that <see cref="ReportAtExit"/> writes.</summary>
        internal const string ReportFile = "stub-hits.tsv";

        /// <summary>With <c>UNBOUND_TRACE=stubs</c>, as a session ends: the members of
        /// <see cref="HitReport"/> and how often each was used in the process, added to those of the
        /// sessions it continues (a session relaunched for a script reload is a new process, which
        /// starts counting again), most used first; to the log and to <see cref="ReportFile"/>.</summary>
        internal static void ReportAtExit(bool continuesEarlierSession)
        {
            if (Trace.StubsOn)
                Native.Log(3, WriteReport(UnityEngine.ProjectInfo.UnboundPath(ReportFile), HitReport(), continuesEarlierSession));
        }

        /// <summary>Writes the report of <paramref name="hits"/> (with the one at
        /// <paramref name="path"/> added when the session continues it) and returns it as a log
        /// line.</summary>
        internal static string WriteReport(string path, IEnumerable<KeyValuePair<string, int>> hits, bool continuesEarlierSession)
        {
            string earlier = null;
            if (continuesEarlierSession)
            {
                try { earlier = System.IO.File.ReadAllText(path); }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException) { }
            }
            var report = Merge(hits, earlier);
            var text = Format(report);
            long calls = 0;
            foreach (var hit in report) calls += hit.Value;
            string written;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, text);
                written = "written to " + path;
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                written = "not written to " + path + ": " + e.Message;
            }
            return $"[trace] stubs: {report.Count} member(s) used {calls} time(s)"
                + (continuesEarlierSession ? " with the sessions this one continues" : "")
                + $", {written}\n" + text;
        }

        /// <summary>The hits of this process added to those of an earlier report's text (lines of a
        /// count, a tab and the member; other lines are skipped), most used first, then by name.</summary>
        internal static List<KeyValuePair<string, int>> Merge(IEnumerable<KeyValuePair<string, int>> hits, string earlier)
        {
            var merged = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var hit in hits) Add(merged, OneLine(hit.Key), hit.Value);
            if (!string.IsNullOrEmpty(earlier))
            {
                foreach (var line in earlier.Split('\n'))
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0 || !int.TryParse(line.AsSpan(0, tab), System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var count)) continue;
                    Add(merged, line.Substring(tab + 1).TrimEnd('\r'), count);
                }
            }
            return merged
                .Select(m => new KeyValuePair<string, int>(m.Key, (int)Math.Min(m.Value, int.MaxValue)))
                .OrderByDescending(m => m.Value).ThenBy(m => m.Key, StringComparer.Ordinal).ToList();
        }

        static void Add(Dictionary<string, long> merged, string member, long count) =>
            merged[member] = merged.TryGetValue(member, out var n) ? n + count : count;

        /// <summary>A member's key on one line of the report.</summary>
        static string OneLine(string member) => member.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

        /// <summary>One line per member: its count, a tab, the member.</summary>
        internal static string Format(IReadOnlyList<KeyValuePair<string, int>> report)
        {
            var text = new System.Text.StringBuilder();
            foreach (var hit in report)
                text.Append(hit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\t').Append(hit.Key).Append('\n');
            return text.ToString();
        }

        /// <summary>A writable location for stubs that return by reference.</summary>
        public static ref T Ref<T>() => ref Box<T>.Value;

        static class Box<T>
        {
            public static T Value;
        }
    }
}
