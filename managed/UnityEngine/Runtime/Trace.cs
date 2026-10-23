using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Developer tracing of what the engine does, for finding why a game stalls: set
    /// <c>UNBOUND_TRACE</c> to a comma-separated list of channels (<c>lifecycle</c>, <c>scenes</c>,
    /// <c>render</c>, <c>render-detail</c>, <c>exceptions</c>, <c>perf</c>, <c>input</c>, <c>hierarchy</c>, <c>audio</c>, or <c>all</c>). Off by
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
        internal static readonly bool InputOn = On("input");
        internal static readonly bool HierarchyOn = On("hierarchy");

        /// <summary>With <c>hierarchy</c>: the scenes' objects at quit, to Library/Unbound/hierarchy.txt.</summary>
        internal static void DumpHierarchy()
        {
            if (!HierarchyOn) return;
            var text = new System.Text.StringBuilder();
            foreach (var go in ObjectRegistry.All(typeof(GameObject)))
            {
                var g = (GameObject)go;
                if (g.m_Destroyed || g.IsPersistentAsset || g.m_Transform == null || g.m_Transform.m_Parent != null) continue;
                Dump(g.m_Transform, 0, text);
            }
            var path = System.IO.Path.Combine(ProjectInfo.ProjectPath, "Library", "Unbound", "hierarchy.txt");
            System.IO.File.WriteAllText(path, text.ToString());
            Native.Log(3, $"[trace] hierarchy written to {path}");
        }

        static void Dump(Transform t, int depth, System.Text.StringBuilder text)
        {
            var go = t.m_GameObject;
            text.Append(' ', depth * 2).Append(go.m_IsActive ? "+ " : "- ").Append(go.m_Name);
            var scale = t.localScale;
            if (scale != Vector3.one) text.Append($" scale={scale}");
            foreach (var c in go.m_Components)
            {
                if (c == null || c is Transform) continue;
                text.Append(' ').Append(c.GetType().Name);
                if (c is Behaviour b && !b.m_Enabled) text.Append("(off)");
                if (c is CanvasGroup g) text.Append($"(alpha={g.alpha},blocks={g.blocksRaycasts})");
            }
            text.Append('\n');
            foreach (var child in t.m_Children) Dump(child, depth + 1, text);
        }

        /// <summary>With <c>input</c>: a press, and what the EventSystem's raycasters hit there.</summary>
        internal static void Pointer(Vector3 position)
        {
            var report = new System.Text.StringBuilder($"[trace] press at {position} (frame {Time.frameCount})");
            try
            {
                Type eventSystem = null, pointerData = null;
                foreach (var assembly in GameDomain.Assemblies())
                {
                    eventSystem ??= assembly.GetType("UnityEngine.EventSystems.EventSystem");
                    pointerData ??= assembly.GetType("UnityEngine.EventSystems.PointerEventData");
                }
                var current = eventSystem?.GetProperty("current")?.GetValue(null);
                if (current == null)
                {
                    report.Append(": no EventSystem");
                }
                else
                {
                    var data = Activator.CreateInstance(pointerData, current);
                    pointerData.GetProperty("position").SetValue(data, (Vector2)position);
                    var resultType = eventSystem.Assembly.GetType("UnityEngine.EventSystems.RaycastResult");
                    var results = (System.Collections.IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(resultType));
                    eventSystem.GetMethod("RaycastAll").Invoke(current, new[] { data, results });
                    report.Append($": {results.Count} hits");
                    foreach (var r in results)
                    {
                        var go = resultType.GetProperty("gameObject").GetValue(r) as GameObject;
                        var module = resultType.GetField("module").GetValue(r);
                        report.Append($"\n  {go?.name} ({module?.GetType().Name})");
                    }
                }
            }
            catch (Exception e)
            {
                report.Append(": " + e.Message);
            }
            Native.Log(3, report.ToString());
        }

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
