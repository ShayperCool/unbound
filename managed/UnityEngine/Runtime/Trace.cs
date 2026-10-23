using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Developer tracing of what the engine does, for finding why a game stalls: set
    /// <c>UNBOUND_TRACE</c> to a comma-separated list of channels (<c>lifecycle</c>, <c>scenes</c>,
    /// <c>render</c>, <c>render-detail</c>, <c>exceptions</c>, <c>perf</c>, <c>audio</c>, or <c>all</c>). Off by
    /// default; traces go to the host log, not to the game's log handlers.
    /// </summary>
    internal static class Trace
    {
        static readonly string s_Channels = Environment.GetEnvironmentVariable("UNBOUND_TRACE") ?? "";

        static bool On(string channel) =>
            s_Channels.Length > 0 && (s_Channels == "all" || Array.IndexOf(s_Channels.Split(','), channel) >= 0);

        internal static readonly bool LifecycleOn = On("lifecycle");
        internal static readonly bool ScenesOn = On("scenes");
        internal static readonly bool RenderOn = On("render") || On("render-detail");
        internal static readonly bool RenderDetailOn = On("render-detail");
        internal static readonly bool ExceptionsOn = On("exceptions");
        internal static readonly bool PerfOn = On("perf");

        static readonly System.Collections.Generic.Dictionary<string, (double Ms, long Calls)> s_Perf =
            new System.Collections.Generic.Dictionary<string, (double, long)>();

        /// <summary>With <c>perf</c>: time spent per script callback, reported at shutdown.</summary>
        internal static void Perf(string name, long startTicks)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            lock (s_Perf)
            {
                s_Perf.TryGetValue(name, out var entry);
                s_Perf[name] = (entry.Ms + ms, entry.Calls + 1);
            }
        }

        internal static void ReportPerf()
        {
            if (!PerfOn) return;
            var list = new System.Collections.Generic.List<(string Name, double Ms, long Calls)>();
            lock (s_Perf)
                foreach (var pair in s_Perf) list.Add((pair.Key, pair.Value.Ms, pair.Value.Calls));
            list.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            var report = new System.Text.StringBuilder("[trace] perf (total ms, calls, callback):");
            foreach (var (name, ms, calls) in list.GetRange(0, Math.Min(30, list.Count)))
                report.Append($"\n  {ms,10:F1} {calls,8} {name}");
            Native.Log(3, report.ToString());
        }

        /// <summary>With <c>exceptions</c>: every exception as it is thrown, also those code catches
        /// itself (plugins that swallow errors).</summary>
        internal static void Install()
        {
            if (!ExceptionsOn) return;
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                if (s_InHandler) return;
                s_InHandler = true;
                try { Native.Log(3, $"[trace] thrown {e.Exception.GetType().Name}: {e.Exception.Message}\n{Environment.StackTrace}"); }
                finally { s_InHandler = false; }
            };
        }

        [ThreadStatic] static bool s_InHandler;

        internal static void Lifecycle(string message, MonoBehaviour mb)
        {
            if (!LifecycleOn) return;
            Native.Log(3, $"[trace] {message} {mb.GetType().FullName} on '{mb.m_GameObject?.m_Name}' (frame {Time.frameCount})");
        }

        internal static void Render(string message)
        {
            if (!RenderOn) return;
            Native.Log(3, $"[trace] {message} (frame {Time.frameCount})");
        }

        internal static void Scenes(string message)
        {
            if (!ScenesOn) return;
            Native.Log(3, $"[trace] {message} (frame {Time.frameCount})");
        }

        internal static readonly bool AudioOn = On("audio");

        internal static void Audio(string message)
        {
            if (!AudioOn) return;
            Native.Log(3, $"[trace] audio: {message} (frame {Time.frameCount}, dsp {AudioSettings.dspTime:F3})");
        }
    }
}
