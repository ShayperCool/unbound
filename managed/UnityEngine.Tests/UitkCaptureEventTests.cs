using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Pointer capture events answer their pointer through the internal interface too
    /// (Unity 6000.0.71f1, tests/projects/captureevents). Control outside the probe: a pen pointer.</summary>
    public sealed class UitkCaptureEventTests
    {
        [Fact]
        public void CaptureEventsAnswerTheirPointerThroughTheInternalInterface()
        {
            var a = new VisualElement();
            var b = new VisualElement();
            int pen = PointerId.penPointerIdBase + 1;
            using (var capture = PointerCaptureEvent.GetPooled(a, b, pen))
            {
                IPointerCaptureEventInternal e = capture;
                Assert.Equal(pen, e.pointerId);
                Assert.Equal(pen, capture.pointerId);
            }
            using (var release = PointerCaptureOutEvent.GetPooled(b, a, PointerId.mousePointerId))
                Assert.Equal(PointerId.mousePointerId, ((IPointerCaptureEventInternal)release).pointerId);
            var reused = PointerCaptureEvent.GetPooled();
            Assert.Equal(PointerId.invalidPointerId, ((IPointerCaptureEventInternal)reused).pointerId);
            reused.Dispose();
        }
    }
}
