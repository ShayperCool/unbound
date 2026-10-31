using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace UnityEngine.Tests
{
    public class AudioCaptureTimeTests
    {
        [Theory]
        [InlineData(0.01666666753590107f, 60, 1015580809, 1023969417, 1028443341)]
        [InlineData(0.02133333310484886f, 47, 1018086202, 1026474810, 1032000108)]
        [InlineData(3f, 0, 1077936128, 1086324736, 1091567616)]
        public void AwakeCaptureDurationAdvancesFromTheFirstFrameEvenWhenFpsRoundsToZero(
            float duration, int fps, int first, int second, int third)
        {
            var saved = new Dictionary<FieldInfo, object>();
            foreach (var field in typeof(Time).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                if (!field.IsInitOnly && field.FieldType.IsPrimitive) saved[field] = field.GetValue(null);
            try
            {
                Time.ResetForPlayMode();
                Time.timeScale = 1;
                Time.captureDeltaTime = duration;
                Assert.Equal(fps, Time.captureFramerate);
                // Exact first-three-frame public values from tests/projects/captureclock.
                foreach (int expectedTimeBits in new[] { first, second, third })
                {
                    Time.BeginFrame(0.01);
                    Assert.Equal(first, BitConverter.SingleToInt32Bits(Time.deltaTime));
                    Assert.Equal(expectedTimeBits, BitConverter.SingleToInt32Bits(Time.time));
                }
            }
            finally { foreach (var pair in saved) pair.Key.SetValue(null, pair.Value); }
        }

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
