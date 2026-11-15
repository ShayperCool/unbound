using System;
using System.Diagnostics;

namespace UnityEngine
{
    public partial struct FrameTiming
    {
        public double cpuFrameTime;
        public double cpuMainThreadFrameTime;
        public double cpuMainThreadPresentWaitTime;
        public double cpuRenderThreadFrameTime;
        public double gpuFrameTime;
        public ulong frameStartTimestamp;
        public ulong firstSubmitTimestamp;
        public ulong cpuTimePresentCalled;
        public ulong cpuTimeFrameComplete;
        public float heightScale;
        public float widthScale;
        public uint syncInterval;
    }

    /// <summary>
    /// Frame timings as the Editor keeps them: always on ("FrameTimingManager is always active in
    /// development builds and the Unity Editor", Unity 6000.0 Manual), the last frames newest
    /// first. Matches the Unity 6000.0.71f1 Linux Editor (OpenGLCore), tests/projects/asyncread:
    /// timestamps in nanoseconds, no GPU frame time (the Manual lists GPU time as unsupported on
    /// Linux OpenGL) and no present wait; macOS (Metal) is not measured.
    /// </summary>
    public static partial class FrameTimingManager
    {
        [Unbound.EditorFaithful("always active in the Editor (Unity 6000.0 Manual; asyncread timing.enabled)")]
        public static bool IsFeatureEnabled() => true;

        /// <summary>The Editor captures every frame itself; an explicit capture changes nothing
        /// (asyncread timing.countsPerFrame: the counts before and after it agree).</summary>
        [Unbound.EditorFaithful("the Editor captures every frame itself (asyncread timing.captureNoEffect)")]
        public static void CaptureFrameTimings()
        {
        }

        /// <summary>The newest <paramref name="numFrames"/> frames (at most the frames kept),
        /// written newest first into <paramref name="timings"/> as far as it reaches. The count
        /// returned does not depend on the array: the Editor returns it for a shorter array too
        /// (and writes past such an array's end, which Unbound does not).</summary>
        public static uint GetLatestTimings(uint numFrames, FrameTiming[] timings) =>
            Unbound.Runtime.FrameTimings.Engine.Latest(numFrames, timings);

        public static float GetVSyncsPerSecond() => (float)Screen.currentResolution.refreshRateRatio.value;

        public static ulong GetGpuTimerFrequency() => Unbound.Runtime.FrameTimings.TimerFrequency;

        public static ulong GetCpuTimerFrequency() => Unbound.Runtime.FrameTimings.TimerFrequency;
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>The timings of the frames the engine ran, kept in a fixed ring (no allocation per
    /// frame). A frame's start, rendering and present are marked as they happen; it enters the
    /// ring when the next one starts, which gives its whole duration, and is reported
    /// <see cref="FrameTimings.DelayFrames"/> frames after it ran.</summary>
    internal sealed class FrameTimingRing
    {
        /// <summary>The frames that ended but are not reported yet: the delay less the frame
        /// that has just ended, which enters the ring when the current one starts.</summary>
        const int Withheld = FrameTimings.DelayFrames - 1;

        readonly FrameTiming[] m_Ring = new FrameTiming[FrameTimings.Capacity + Withheld];
        readonly object m_Lock = new object();
        int m_Count;
        int m_Next;
        bool m_InFrame;
        FrameTiming m_Current;
        ulong m_RenderStart;

        static double Milliseconds(ulong from, ulong to) => to > from ? (to - from) / 1e6 : 0;

        internal void BeginFrame(ulong now)
        {
            if (m_InFrame)
            {
                m_Current.cpuFrameTime = Milliseconds(m_Current.frameStartTimestamp, now);
                lock (m_Lock)
                {
                    m_Ring[m_Next] = m_Current;
                    m_Next = (m_Next + 1) % m_Ring.Length;
                    if (m_Count < m_Ring.Length) m_Count++;
                }
            }
            m_Current = new FrameTiming { frameStartTimestamp = now, widthScale = 1, heightScale = 1 };
            m_RenderStart = 0;
            m_InFrame = true;
        }

        /// <summary>The frame's rendering starts: its first submission.</summary>
        internal void BeginRendering(ulong now)
        {
            if (!m_InFrame) return;
            m_RenderStart = now;
            if (m_Current.firstSubmitTimestamp == 0) m_Current.firstSubmitTimestamp = now;
        }

        internal void EndRendering(ulong now)
        {
            if (!m_InFrame || m_RenderStart == 0) return;
            m_Current.cpuRenderThreadFrameTime += Milliseconds(m_RenderStart, now);
            m_RenderStart = 0;
        }

        /// <summary>The frame's work is done and it is handed to the window. Without GPU
        /// timestamps a frame completes at its present (Unity 6000.0 Manual, frame timing
        /// manager).</summary>
        internal void Present(ulong now, uint syncInterval)
        {
            if (!m_InFrame) return;
            if (m_Current.firstSubmitTimestamp == 0) m_Current.firstSubmitTimestamp = now;
            m_Current.cpuTimePresentCalled = now;
            m_Current.cpuTimeFrameComplete = now;
            m_Current.cpuMainThreadFrameTime = Milliseconds(m_Current.frameStartTimestamp, now);
            m_Current.syncInterval = syncInterval;
        }

        internal uint Latest(uint numFrames, FrameTiming[] timings)
        {
            lock (m_Lock)
            {
                int reported = Math.Min(Math.Max(m_Count - Withheld, 0), FrameTimings.Capacity);
                uint count = Math.Min(numFrames, (uint)reported);
                int write = timings == null ? 0 : (int)Math.Min(count, (uint)timings.Length);
                for (int i = 0; i < write; i++) timings[i] = m_Ring[(m_Next - 1 - Withheld - i + 2 * m_Ring.Length) % m_Ring.Length];
                return count;
            }
        }
    }

    /// <summary>The engine's frame timings, marked by the main loop.</summary>
    internal static class FrameTimings
    {
        /// <summary>The Editor keeps 32 frames (asyncread timing.capacity).</summary>
        internal const int Capacity = 32;

        /// <summary>A frame is the newest timing four frames after it ran: in frame N's Update,
        /// frame N - 4 (asyncread timing.longFrame*; the Manual: "a set delay of four
        /// frames").</summary>
        internal const int DelayFrames = 4;

        /// <summary>Nanosecond timestamps (asyncread timing.cpuFrequency/gpuFrequency).</summary>
        internal const ulong TimerFrequency = 1_000_000_000;

        internal static readonly FrameTimingRing Engine = new FrameTimingRing();

        static ulong Now() => (ulong)((double)Stopwatch.GetTimestamp() * TimerFrequency / Stopwatch.Frequency);

        internal static void BeginFrame() => Engine.BeginFrame(Now());

        internal static void BeginRendering() => Engine.BeginRendering(Now());

        internal static void EndRendering() => Engine.EndRendering(Now());

        /// <summary>The sync interval reads the display's refresh rate in the measured Editor,
        /// whatever vSyncCount is (asyncread timing.vSync*).</summary>
        internal static void Present() => Engine.Present(Now(), (uint)Math.Round(Screen.currentResolution.refreshRateRatio.value));
    }
}
