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
        /// <summary>The frame's step (the fixed timestep in the first frame, see BeginFrame).</summary>
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
            // Real time counts from the first frame, like unscaledTime (measured in Play mode:
            // realtimeSinceStartup equal to unscaledTime to the millisecond); loading before it
            // does not count.
            s_FrameRealtime = s_FrameCount == 0 ? 0.0 : Math.Max(s_FrameRealtime + realDelta, Realtime());
            s_FrameClock.Restart();
            s_RealDeltaTime = realDelta;
            // Under a capture rate the game clock steps by the capture delta while the unscaled
            // clock keeps real time (measured: unscaledDeltaTime 0.69 s, deltaTime 0.02).
            double step = s_CaptureFramerate > 0 ? 1.0 / s_CaptureFramerate : realDelta;
            float unscaled = (float)Math.Min(step, s_MaximumDeltaTime);
            if (s_CaptureFramerate > 0) unscaled = (float)step;
            if (s_FrameCount == 0)
            {
                // The first frame steps by the fixed timestep while the clocks stay at 0 (measured:
                // deltaTime and unscaledDeltaTime 0.0199999921 at time 0, whatever capture rate
                // Start sets).
                s_UnscaledDeltaTime = s_FixedDeltaTime;
                s_DeltaTime = s_FixedDeltaTime * s_TimeScale;
                // A capture rate set before the first frame (in Awake) steps the game clock from
                // that frame on (measured, tests/projects/inputframes: captureFramerate 60 in Awake,
                // the first frame's time and deltaTime 1/60, its fixed step at 0).
                if (s_CaptureFramerate > 0)
                {
                    s_DeltaTime = (float)step * s_TimeScale;
                    s_Time += s_DeltaTime;
                }
            }
            else
            {
                // maximumDeltaTime clamps the game clock only: the unscaled clock keeps real time
                // through hitches (measured: unscaledTime equal to realtimeSinceStartup after a
                // load, time 10 s behind).
                s_UnscaledDeltaTime = (float)realDelta;
                s_UnscaledTime += realDelta;
                s_DeltaTime = unscaled * s_TimeScale;
                s_Time += s_DeltaTime;
            }
            // smoothDeltaTime starts at 0 in the first frame and at deltaTime in the second (measured).
            s_SmoothDeltaTime = s_FrameCount == 0 ? 0f : s_FrameCount == 1 ? s_DeltaTime : s_SmoothDeltaTime + (s_DeltaTime - s_SmoothDeltaTime) * 0.2f;
            s_FrameCount++;
        }

        internal static void LevelLoaded() => s_TimeSinceLevelLoadStart = s_Time;

        /// <summary>Play mode starts its clocks from zero (the first frame has no delta).</summary>
        /// <summary>Play mode has run its first fixed step (at fixed time 0).</summary>
        internal static bool s_FixedStarted;

        internal static void ResetForPlayMode()
        {
            s_FixedStarted = false;
            s_Time = s_UnscaledTime = s_FixedTime = s_FixedUnscaledTime = 0;
            s_DeltaTime = s_UnscaledDeltaTime = s_SmoothDeltaTime = 0f;
            s_TimeSinceLevelLoadStart = 0;
            s_FrameCount = 0;
        }
    }
}
