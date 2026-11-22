using System;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>deltaTime before Play mode's first frame and in it (tests/projects/capturestart
    /// records the Editor's: 0.0199999921 whatever the fixed timestep and the time scale).</summary>
    public class PlayStartDeltaTests
    {
        const float StartDelta = 0.0199999921f;

        static void Restoring(Action test)
        {
            float fixedStep = Time.fixedDeltaTime, scale = Time.timeScale, capture = Time.captureDeltaTime;
            try { test(); }
            finally
            {
                Time.fixedDeltaTime = fixedStep;
                Time.timeScale = scale;
                Time.captureDeltaTime = capture;
                Time.ResetForPlayMode();
            }
        }

        [Fact]
        public void TheDeltasBeforeTheFirstFrameAndInItIgnoreTheFixedTimestepAndTheTimeScale() => Restoring(() =>
        {
            Time.captureDeltaTime = 0f;
            Time.fixedDeltaTime = 0.05f;
            Time.timeScale = 0.5f;
            Time.ResetForPlayMode();
            Assert.Equal((StartDelta, StartDelta, 0f, 0f), (Time.deltaTime, Time.unscaledDeltaTime, Time.smoothDeltaTime, Time.time));
            Time.BeginFrame(0.3);
            Assert.Equal((StartDelta, 0f, 0f), (Time.deltaTime, Time.smoothDeltaTime, Time.time));
            // From the second frame the frame's own time, scaled.
            Time.BeginFrame(0.1);
            Assert.Equal(0.05f, Time.deltaTime, 5);
        });

        [Fact]
        public void ACaptureRateSetBeforeTheFirstFrameStepsIt() => Restoring(() =>
        {
            Time.captureDeltaTime = 0f;
            Time.ResetForPlayMode();
            Assert.Equal(StartDelta, Time.deltaTime);
            Time.captureDeltaTime = 1f / 30f;
            Time.BeginFrame(0.3);
            Assert.Equal((0.0333333351f, 0.0333333351f), (Time.deltaTime, Time.time));
        });

        [Fact]
        public void AnotherFamilyKeepsNoDeltaBeforeTheFirstFrame() => Restoring(() =>
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            Time.captureDeltaTime = 0f;
            Time.fixedDeltaTime = 0.05f;
            Time.timeScale = 0.5f;
            Time.ResetForPlayMode();
            Assert.Equal(0f, Time.deltaTime);
            Time.BeginFrame(0.3);
            Assert.Equal(0.025f, Time.deltaTime, 6);
        });
    }
}
