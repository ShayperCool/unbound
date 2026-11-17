using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Time")]
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

        /// <summary>Runs <paramref name="body"/> from a first frame (fixed-step when
        /// <paramref name="fixedStep"/> is not 0), then puts the clocks back.</summary>
        static void FromFirstFrame(double fixedStep, System.Action body)
        {
            var (time, unscaled, frames) = (Time.s_Time, Time.s_UnscaledTime, Time.s_FrameCount);
            try
            {
                Time.s_FrameCount = 0;
                Time.s_Time = Time.s_UnscaledTime = 0;
                Time.BeginFrame(0, fixedStep);
                body();
            }
            finally
            {
                Time.s_FrameCount = 0;
                Time.BeginFrame(0);
                (Time.s_Time, Time.s_UnscaledTime, Time.s_FrameCount) = (time, unscaled, frames);
            }
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
                for (int frame = 1; frame <= 3; frame++)
                {
                    System.Threading.Thread.Sleep(40); // a frame over twice as slow as its step
                    double late = Time.realtimeSinceStartupAsDouble;
                    // (At most the next frame's start: the read that finds the step used up.)
                    Assert.InRange(late - Time.unscaledTimeAsDouble, 0, Step + 1e-9);
                    Time.BeginFrame(Step, Step);
                    double start = Time.realtimeSinceStartupAsDouble;
                    Assert.InRange(start - Time.unscaledTimeAsDouble, 0, 0.01);
                    Assert.True(start >= late, $"{start} after {late}");
                }
                Assert.InRange(Time.realtimeSinceStartupAsDouble, 3 * Step, 3 * Step + 0.01);
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
                for (int frame = 0; frame < 4; frame++)
                {
                    System.Threading.Thread.Sleep(20); // read late in a slow frame
                    double late = Time.realtimeSinceStartupAsDouble;
                    Assert.True(late >= after, "real time went back");
                    after = late;
                    Time.BeginFrame(Step, Step);
                }
                Assert.InRange(Time.realtimeSinceStartupAsDouble - Time.unscaledTimeAsDouble, 0, 0.01);
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
