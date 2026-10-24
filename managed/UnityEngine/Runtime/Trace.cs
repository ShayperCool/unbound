using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Developer tracing of what the engine does, for finding why a game stalls: set
    /// <c>UNBOUND_TRACE</c> to a comma-separated list of channels (<c>lifecycle</c>, <c>scenes</c>,
    /// <c>render</c>, <c>render-detail</c>, <c>exceptions</c>, <c>perf</c>, <c>input</c>, <c>hierarchy</c>, <c>audio</c>, <c>memory</c>, <c>jobs</c>, or <c>all</c>). Off by
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
        /// <summary>The managed heap and the engine objects in it, every 600 frames.</summary>
        internal static readonly bool MemoryOn = On("memory");
        /// <summary>The first call of each unimplemented (stub) member logs where it came from.</summary>
        internal static readonly bool StubsOn = On("stubs");
        /// <summary>Jobs as they are scheduled, flushed, run, finished and synced.</summary>
        internal static readonly bool JobsOn = On("jobs");

        internal static void Jobs(string text)
        {
            if (JobsOn) Native.Log(3, "[trace] jobs: " + text);
        }

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

        /// <summary>With <c>memory</c>: the GC heap and the engine objects by type (count, and the
        /// bytes of their big arrays: pixels, vertices).</summary>
        internal static void Memory()
        {
            if (!MemoryOn || Time.frameCount % 600 != 0) return;
            var info = GC.GetGCMemoryInfo();
            var report = new System.Text.StringBuilder();
            report.Append($"[trace] memory (frame {Time.frameCount}): GC heap {info.HeapSizeBytes >> 20} MB, ")
                .Append($"committed {info.TotalCommittedBytes >> 20} MB, fragmented {info.FragmentedBytes >> 20} MB, ")
                .Append($"process {Environment.WorkingSet >> 20} MB, native {Native.Call("memory-stats", "{}")}");
            var counts = new System.Collections.Generic.Dictionary<string, (int count, long bytes)>();
            foreach (var o in ObjectRegistry.All(typeof(UnityEngine.Object)))
            {
                long bytes = o switch
                {
                    Texture2D t => (t.m_Pixels?.LongLength ?? 0) * 4 + t.RawBytes,
                    Mesh m => m.m_Vertices.LongLength * 12 + (m.m_Normals?.LongLength ?? 0) * 12 + (m.m_Tangents?.LongLength ?? 0) * 16
                              + (m.m_Colors?.LongLength ?? 0) * 4 + SumUVs(m),
                    _ => 0,
                };
                var key = o.GetType().FullName ?? "?";
                counts.TryGetValue(key, out var c);
                counts[key] = (c.count + 1, c.bytes + bytes);
            }
            var top = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, (int count, long bytes)>>(counts);
            top.Sort((a, b) => b.Value.count.CompareTo(a.Value.count));
            for (int i = 0; i < Math.Min(15, top.Count); i++)
                report.Append($"\n  {top[i].Value.count,8} {top[i].Key}{(top[i].Value.bytes > 0 ? $" ({top[i].Value.bytes >> 20} MB arrays)" : "")}");
            long gpu = 0;
            int gpuCount = 0;
            foreach (var o in ObjectRegistry.All(typeof(Texture2D)))
            {
                if (!(o is Texture2D t) || t.m_NativeHandle == 0) continue;
                long level = (long)t.width * t.height * (t.format == TextureFormat.Alpha8 ? 4 : 4);
                gpu += t.mipmapCount > 1 ? level * 4 / 3 : level;
                gpuCount++;
            }
            report.Append($"\n  textures on the GPU: {gpuCount}, ~{gpu >> 20} MB (RGBA8 with mips)");
            var pixels = new System.Collections.Generic.List<Texture2D>();
            foreach (var o in ObjectRegistry.All(typeof(Texture2D)))
                if (o is Texture2D t && t.m_Pixels != null) pixels.Add(t);
            pixels.Sort((a, b) => b.m_Pixels.Length.CompareTo(a.m_Pixels.Length));
            report.Append($"\n  textures with CPU pixels: {pixels.Count}");
            for (int i = 0; i < Math.Min(12, pixels.Count); i++)
                report.Append($"\n    {pixels[i].m_Pixels.LongLength * 4 >> 20,4} MB {pixels[i].width}x{pixels[i].height} '{pixels[i].name}' readable={pixels[i].m_IsReadable} asset={pixels[i].m_AssetGuid != null}");
            Native.Log(3, report.ToString());
        }

        static long SumUVs(Mesh m)
        {
            long bytes = 0;
            foreach (var uv in m.m_UVs) bytes += (uv?.LongLength ?? 0) * 16;
            return bytes;
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
                        object Get(object o, string name) => o?.GetType().GetProperty(name)?.GetValue(o);
                        var canvas = go != null ? go.GetComponentInParent<Canvas>() : null;
                        report.Append($"\n  {go?.name} ({module?.GetType().Name} sort {Get(module, "sortOrderPriority")} render {Get(module, "renderOrderPriority")} depth {resultType.GetField("depth").GetValue(r)} canvas {canvas?.rootCanvas?.name})");
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
