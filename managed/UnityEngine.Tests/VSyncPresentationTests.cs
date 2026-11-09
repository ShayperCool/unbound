using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The window waits for the vertical sync while QualitySettings.vSyncCount is above
    /// 0 and presents unsynchronized at 0 (Unity 6000.0 QualitySettings.vSyncCount); the window
    /// is told on the first frame and whenever that changes, not every frame.</summary>
    public sealed class VSyncPresentationTests
    {
        [Fact]
        public void TheWindowIsToldWhenTheSyncChanges()
        {
            int presented = -1;
            Assert.Equal("0", PlayerLoopDriver.VSyncRequest(ref presented, 0));
            Assert.Null(PlayerLoopDriver.VSyncRequest(ref presented, 0));
            Assert.Equal("1", PlayerLoopDriver.VSyncRequest(ref presented, 2));
            // Another count above 0 keeps the sync (the frame pacing divides the refresh).
            Assert.Null(PlayerLoopDriver.VSyncRequest(ref presented, 1));
            Assert.Equal("0", PlayerLoopDriver.VSyncRequest(ref presented, 0));
            presented = -1;
            Assert.Equal("1", PlayerLoopDriver.VSyncRequest(ref presented, 1));
        }

        [Fact]
        public void WithoutVSyncTheTargetFrameRatePacesFrames()
        {
            Assert.Equal(0, PlayerLoopDriver.FramePeriod(-1, 0));
            Assert.Equal(1.0 / 30, PlayerLoopDriver.FramePeriod(30, 0), 9);
        }
    }
}
