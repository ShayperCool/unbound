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
    }
}
