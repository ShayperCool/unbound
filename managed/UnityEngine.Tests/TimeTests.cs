using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class TimeTests
    {
        [Fact]
        public void AHitchIsClampedOnTheGameClockOnly()
        {
            // Measured on the 6000.0 Editor: after loading hitches unscaledTime equals
            // realtimeSinceStartup while time lags by what maximumDeltaTime cut off.
            var (time, unscaled, frames) = (Time.s_Time, Time.s_UnscaledTime, Time.s_FrameCount);
            try
            {
                if (Time.s_FrameCount == 0) Time.s_FrameCount = 1;
                double t0 = Time.timeAsDouble, u0 = Time.unscaledTimeAsDouble;
                Time.BeginFrame(2.0);
                Assert.Equal(2.0, Time.unscaledTimeAsDouble - u0, 3);
                Assert.Equal(2f, Time.unscaledDeltaTime, 3);
                Assert.Equal(Time.maximumDeltaTime, (float)(Time.timeAsDouble - t0), 3);
                Assert.Equal(Time.maximumDeltaTime, Time.deltaTime, 3);
            }
            finally
            {
                (Time.s_Time, Time.s_UnscaledTime, Time.s_FrameCount) = (time, unscaled, frames);
            }
        }

        const double Step = 1.0 / 60;

        /// <summary>The wall time since <paramref name="wall"/> was restarted, just before the
        /// latest frame began, read after the clock read it bounds: the most real time a fixed-step
        /// frame can have counted by then. Within a frame the clock follows the wall clock, so a
        /// read is ahead of the frame's start by the time the thread took to get there, which a
        /// loaded machine stretches; that, and nothing the frames themselves add, is allowed.</summary>
        static double InFrame(System.Diagnostics.Stopwatch wall) => wall.Elapsed.TotalSeconds + 1e-9;

        /// <summary>Runs <paramref name="body"/> from a first frame (fixed-step when
        /// <paramref name="fixedStep"/> is not 0), then puts the clocks back.</summary>
        static void FromFirstFrame(double fixedStep, System.Action body)
        {
            var saved = new System.Collections.Generic.Dictionary<System.Reflection.FieldInfo, object>();
            foreach (var field in typeof(Time).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
                if (!field.IsInitOnly && !field.IsLiteral && field.FieldType.IsPrimitive) saved[field] = field.GetValue(null);
            try
            {
                Time.s_FrameCount = 0;
                Time.s_Time = Time.s_UnscaledTime = 0;
                Time.BeginFrame(0, fixedStep);
                body();
            }
            finally { foreach (var pair in saved) pair.Key.SetValue(null, pair.Value); }
        }

        /// <summary>Paused or Edit mode host frames move fixed-step real time by a step each
        /// (unscaledTime stands still), and the frames after them go on from there.</summary>
        [Fact]
        public void FixedStepRealTimeMovesThroughPausedFrames()
        {
            FromFirstFrame(Step, () =>
            {
                var wall = System.Diagnostics.Stopwatch.StartNew();
                Time.BeginFrame(Step, Step);
                double before = Time.realtimeSinceStartupAsDouble, intoFirst = InFrame(wall);
                for (int i = 0; i < 3; i++)
                {
                    wall.Restart();
                    Time.HostFrameOutsidePlay(Step);
                }
                double paused = Time.realtimeSinceStartupAsDouble;
                Assert.InRange(paused - before, 3 * Step - intoFirst, 3 * Step + InFrame(wall));
                wall.Restart();
                Time.BeginFrame(Step, Step);
                double resumed = Time.realtimeSinceStartupAsDouble;
                Assert.InRange(resumed - Time.unscaledTimeAsDouble, 3 * Step - 1e-9, 3 * Step + InFrame(wall));
                Assert.True(resumed >= paused);
            });
        }

        /// <summary>Under --fixed-fps a frame lasts its step of real time, as it does for
        /// unscaledTime: frames slower than their step on the wall clock (a scripted tap's press
        /// and release read realtimeSinceStartup against the press's unscaledTime) leave the two
        /// clocks together, and the clock never goes back.</summary>
        [Fact]
        public void FixedStepRealTimeFollowsTheFramesThroughSlowFrames()
        {
            FromFirstFrame(Step, () =>
            {
                var wall = System.Diagnostics.Stopwatch.StartNew();
                for (int frame = 1; frame <= 3; frame++)
                {
                    System.Threading.Thread.Sleep(40); // a frame over twice as slow as its step
                    double late = Time.realtimeSinceStartupAsDouble;
                    // (At most the next frame's start: the read that finds the step used up.)
                    Assert.InRange(late - Time.unscaledTimeAsDouble, 0, Step + 1e-9);
                    wall.Restart();
                    Time.BeginFrame(Step, Step);
                    double start = Time.realtimeSinceStartupAsDouble;
                    Assert.InRange(start - Time.unscaledTimeAsDouble, 0, InFrame(wall));
                    Assert.True(start >= late, $"{start} after {late}");
                }
                // The three 40 ms frames moved the clock by their steps, not by the wall time.
                double end = Time.realtimeSinceStartupAsDouble;
                Assert.InRange(end, 3 * Step, 3 * Step + InFrame(wall));
            });
        }

        /// <summary>A script that waits in one frame for more real time than the step still sees
        /// it pass; the next frame starts no earlier than what it saw, and the following frames
        /// hold the clock until unscaledTime catches up with it, even when each of them is read
        /// late.</summary>
        [Fact]
        public void FixedStepRealTimeEndsAWaitLongerThanTheStep()
        {
            FromFirstFrame(Step, () =>
            {
                Time.BeginFrame(Step, Step);
                double start = Time.realtimeSinceStartupAsDouble, now = start;
                int reads = 0;
                while (now < start + 3 * Step)
                {
                    double next = Time.realtimeSinceStartupAsDouble;
                    Assert.True(next > now || reads == 0 || next == now, "real time went back");
                    now = next;
                    Assert.True(++reads < 1_000_000, "the wait does not end");
                }
                Time.BeginFrame(Step, Step);
                double after = Time.realtimeSinceStartupAsDouble;
                Assert.True(after >= now);
                Assert.True(after - Time.unscaledTimeAsDouble > Step, "the wait moved real time ahead");
                var wall = System.Diagnostics.Stopwatch.StartNew();
                for (int frame = 0; frame < 4; frame++)
                {
                    System.Threading.Thread.Sleep(20); // read late in a slow frame
                    double late = Time.realtimeSinceStartupAsDouble;
                    Assert.True(late >= after, "real time went back");
                    after = late;
                    wall.Restart();
                    Time.BeginFrame(Step, Step);
                }
                double caughtUp = Time.realtimeSinceStartupAsDouble;
                Assert.InRange(caughtUp - Time.unscaledTimeAsDouble, 0, InFrame(wall));
            });
        }

        /// <summary>Frames that follow the wall clock keep real time as the wall clock does: a slow
        /// frame moves it by the time it took.</summary>
        [Fact]
        public void WallClockRealTimeCountsSlowFrames()
        {
            FromFirstFrame(0, () =>
            {
                Time.BeginFrame(Step);
                double start = Time.realtimeSinceStartupAsDouble;
                System.Threading.Thread.Sleep(40);
                Time.BeginFrame(Step);
                Assert.True(Time.realtimeSinceStartupAsDouble - start >= 0.039, $"{Time.realtimeSinceStartupAsDouble - start}");
                Assert.InRange(Time.unscaledTimeAsDouble, 2 * Step - 1e-9, 2 * Step + 1e-9);
            });
        }
    }
}
