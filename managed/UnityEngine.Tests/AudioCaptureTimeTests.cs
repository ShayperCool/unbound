using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace UnityEngine.Tests
{
    public class AudioCaptureTimeTests
    {
        [Fact]
        public void FractionalCaptureDurationKeepsItsRequestedBackingAndStepsByTheDuration()
        {
            var saved = new Dictionary<FieldInfo, object>();
            foreach (var field in typeof(Time).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                if (!field.IsInitOnly && field.FieldType.IsPrimitive) saved[field] = field.GetValue(null);
            try
            {
                float requested = 1024f / 48000;
                Time.captureDeltaTime = requested;
                Assert.Equal(requested, Time.s_CaptureDeltaTime);
                Assert.Equal(47, Time.captureFramerate);
                // Independently repeated Native property bits for the fractional duration.
                Assert.Equal(BitConverter.SingleToInt32Bits(0.0213333257f), BitConverter.SingleToInt32Bits(Time.captureDeltaTime));
                Time.s_FrameCount = 1;
                Time.timeScale = 1;
                Time.BeginFrame(0.01);
                Assert.Equal(Time.captureDeltaTime, Time.deltaTime);
                Assert.Equal(0.01f, Time.unscaledDeltaTime);
                Assert.Equal(0.01, Time.s_RealDeltaTime);
                foreach (int fps in new[] { 0, 24, 30, 47, 60, 120, 1000 })
                {
                    Time.captureFramerate = fps;
                    Assert.Equal(fps, Time.captureFramerate);
                }
            }
            finally { foreach (var pair in saved) pair.Key.SetValue(null, pair.Value); }
        }
    }
}
