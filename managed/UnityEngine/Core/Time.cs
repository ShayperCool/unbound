using System;
using System.Diagnostics;

namespace UnityEngine
{
    /// <summary>Frame and fixed-step time, advanced by the player loop.</summary>
    public partial class Time
    {

        internal static double s_Time;
        internal static double s_UnscaledTime;
        internal static double s_FixedTime;
        internal static double s_FixedUnscaledTime;
        internal static float s_DeltaTime;
        internal static float s_UnscaledDeltaTime;
        internal static float s_SmoothDeltaTime;
        internal static float s_FixedDeltaTime = 0.02f;
        internal static float s_MaximumDeltaTime = 0.3333333f;
        internal static float s_MaximumParticleDeltaTime = 0.03f;
        internal static float s_TimeScale = 1f;
        internal static int s_FrameCount;
        internal static int s_RenderedFrameCount;
        internal static bool s_InFixedTimeStep;
        internal static double s_TimeSinceLevelLoadStart;
        internal static int s_CaptureFramerate;
        /// <summary>The frame's real time, unclamped (audio runs in real time).</summary>
        internal static double s_RealDeltaTime;

        public static float time => (float)(s_InFixedTimeStep ? s_FixedTime : s_Time);
        public static double timeAsDouble => s_InFixedTimeStep ? s_FixedTime : s_Time;
        public static float timeSinceLevelLoad => (float)(s_Time - s_TimeSinceLevelLoadStart);
        public static double timeSinceLevelLoadAsDouble => s_Time - s_TimeSinceLevelLoadStart;
        public static float deltaTime => s_InFixedTimeStep ? s_FixedDeltaTime : s_DeltaTime;
        public static float fixedTime => (float)s_FixedTime;
        public static double fixedTimeAsDouble => s_FixedTime;
        public static float unscaledTime => (float)s_UnscaledTime;
        public static double unscaledTimeAsDouble => s_UnscaledTime;
        public static float fixedUnscaledTime => (float)s_FixedUnscaledTime;
        public static double fixedUnscaledTimeAsDouble => s_FixedUnscaledTime;
        public static float unscaledDeltaTime => s_InFixedTimeStep ? s_FixedDeltaTime : s_UnscaledDeltaTime;
        public static float fixedUnscaledDeltaTime => s_FixedDeltaTime;
        public static float smoothDeltaTime => s_SmoothDeltaTime;
        public static bool inFixedTimeStep => s_InFixedTimeStep;
        public static int frameCount => s_FrameCount;
        public static int renderedFrameCount => s_RenderedFrameCount;
        public static float realtimeSinceStartup => (float)Realtime();
        public static double realtimeSinceStartupAsDouble => Realtime();

        /// <summary>The real time at the start of this frame: the sum of the host's real frame
        /// deltas (wall time in a window; the simulated frame times headless, so real-time waits
        /// advance with the frames).</summary>
        static double s_FrameRealtime;
        static readonly Stopwatch s_FrameClock = Stopwatch.StartNew();

        /// <summary>Real time: the frame's start plus the wall time spent in the frame so far.</summary>
        static double Realtime() => s_FrameRealtime + s_FrameClock.Elapsed.TotalSeconds;

        public static float fixedDeltaTime
        {
            get => s_FixedDeltaTime;
            set => s_FixedDeltaTime = Math.Max(value, 0.0001f);
        }

        public static float maximumDeltaTime
        {
            get => s_MaximumDeltaTime;
            set => s_MaximumDeltaTime = Math.Max(value, s_FixedDeltaTime);
        }

        public static float maximumParticleDeltaTime
        {
            get => s_MaximumParticleDeltaTime;
            set => s_MaximumParticleDeltaTime = value;
        }

        public static float timeScale
        {
            get => s_TimeScale;
            set
            {
                if (value < 0f || value > 100f || float.IsNaN(value))
                {
                    Debug.LogError("Time.timeScale is out of range. Needs to be between 0 and 100.");
                    return;
                }
                s_TimeScale = value;
            }
        }

        public static int captureFramerate
        {
            get => s_CaptureFramerate;
            set => s_CaptureFramerate = value;
        }

        public static float captureDeltaTime
        {
            get => s_CaptureFramerate > 0 ? 1f / s_CaptureFramerate : 0f;
            set => s_CaptureFramerate = value > 0 ? (int)Math.Round(1f / value) : 0;
        }

        /// <summary>Starts a frame: advances the frame clocks by the real time elapsed.</summary>
        internal static void BeginFrame(double realDelta)
        {
            // The host's delta covers at least the wall time spent since the last frame began.
            s_FrameRealtime = Math.Max(s_FrameRealtime + realDelta, Realtime());
            s_FrameClock.Restart();
            s_RealDeltaTime = realDelta;
            if (s_CaptureFramerate > 0) realDelta = 1.0 / s_CaptureFramerate;
            float unscaled = (float)Math.Min(realDelta, s_MaximumDeltaTime);
            if (s_FrameCount == 0) unscaled = 0f;
            s_UnscaledDeltaTime = unscaled;
            s_UnscaledTime += unscaled;
            s_DeltaTime = unscaled * s_TimeScale;
            s_Time += s_DeltaTime;
            s_SmoothDeltaTime = s_FrameCount <= 1 ? s_DeltaTime : s_SmoothDeltaTime + (s_DeltaTime - s_SmoothDeltaTime) * 0.2f;
            s_FrameCount++;
        }

        internal static void LevelLoaded() => s_TimeSinceLevelLoadStart = s_Time;
    }
}
