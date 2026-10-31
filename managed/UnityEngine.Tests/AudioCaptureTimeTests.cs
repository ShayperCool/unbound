using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace UnityEngine.Tests
{
    public class AudioCaptureTimeTests
    {
        static string ClockReference(string profile, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.Combine(Path.GetDirectoryName(source), "..", "..", "tests", "projects", "captureclock",
                "Reference", "unity-6000.0.71f1", profile + ".jsonl");

        [Theory]
        [InlineData("fps60")]
        [InlineData("dsp48000")]
        [InlineData("slow")]
        [InlineData("fps60-half")]
        [InlineData("dsp48000-scaled")]
        public void AwakeCaptureDurationAdvancesFromTheFirstFrameEvenWhenFpsRoundsToZero(
            string profile)
        {
            var saved = new Dictionary<FieldInfo, object>();
            foreach (var field in typeof(Time).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                if (!field.IsInitOnly && field.FieldType.IsPrimitive) saved[field] = field.GetValue(null);
            try
            {
                var reference = ClockReference(profile);
                Time.ResetForPlayMode();
                foreach (string line in File.ReadAllLines(reference))
                {
                    using var record = JsonDocument.Parse(line);
                    var row = record.RootElement;
                    Time.timeScale = BitConverter.Int32BitsToSingle(row.GetProperty("scaleBits").GetInt32());
                    Time.captureDeltaTime = BitConverter.Int32BitsToSingle(row.GetProperty("requestedBits").GetInt32());
                    Assert.Equal(row.GetProperty("fps").GetInt32(), Time.captureFramerate);
                    Time.BeginFrame(0.01);
                    Assert.Equal(row.GetProperty("durationBits").GetInt32(), BitConverter.SingleToInt32Bits(Time.captureDeltaTime));
                    Assert.Equal(row.GetProperty("deltaBits").GetInt32(), BitConverter.SingleToInt32Bits(Time.deltaTime));
                    Assert.Equal(row.GetProperty("timeBits").GetInt32(), BitConverter.SingleToInt32Bits(Time.time));
                    Assert.Equal(row.GetProperty("timeDoubleBits").GetInt64(), BitConverter.DoubleToInt64Bits(Time.timeAsDouble));
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
