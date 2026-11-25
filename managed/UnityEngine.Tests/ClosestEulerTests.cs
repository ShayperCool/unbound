using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>AnimationUtility.GetClosestEuler against the Unity 6000.0.71f1 Editor's answers in
    /// tests/projects/animinternal (<c>euler.*</c>).</summary>
    public class ClosestEulerTests
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", ".."));

        static Dictionary<string, string> Reference()
        {
            var values = new Dictionary<string, string>();
            var path = Path.Combine(Repo(), "tests", "projects", "animinternal", "Reference", "unity-6000.0.71f1", "values.jsonl");
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                using var row = JsonDocument.Parse(line);
                var value = row.RootElement.GetProperty("v");
                values[row.RootElement.GetProperty("k").GetString()] = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
            return values;
        }

        static string F(float f) => f.ToString("R", Inv);
        static string V(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);

        /// <summary>Whether the angles are the recorded ones, bit for bit (the Editor prints nine
        /// digits where CoreCLR prints the shortest form).</summary>
        static bool Same(Vector3 v, string recorded)
        {
            var parts = recorded.Split(' ');
            for (int i = 0; i < 3; i++)
                if (BitConverter.SingleToInt32Bits(float.Parse(parts[i], Inv)) != BitConverter.SingleToInt32Bits(v[i])) return false;
            return true;
        }

        static Quaternion Parse(string text)
        {
            var parts = text.Split(' ');
            return new Quaternion(float.Parse(parts[0], Inv), float.Parse(parts[1], Inv), float.Parse(parts[2], Inv), float.Parse(parts[3], Inv));
        }

        [Fact]
        public void EveryRotationHintAndOrderAnswersAsTheEditor()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var reference = Reference();
            var hints = new[] { Vector3.zero, new Vector3(360f, 0f, 0f), new Vector3(-720f, 400f, 10f), new Vector3(170f, 170f, 170f), new Vector3(1000f, -1000f, 0.5f) };
            var failures = new List<string>();
            int checkedCount = 0;
            for (int r = 0; reference.TryGetValue("euler.q." + r, out var rotation); r++)
            {
                var q = Parse(rotation);
                foreach (RotationOrder order in Enum.GetValues(typeof(RotationOrder)))
                    for (int h = 0; h < hints.Length; h++)
                    {
                        var key = $"euler.{order}.{r}.{h}";
                        var got = AnimationUtility.GetClosestEuler(q, hints[h], order);
                        checkedCount++;
                        if (!Same(got, reference[key])) failures.Add($"{key}: {V(got)} (Editor {reference[key]})");
                    }
            }
            Assert.Equal(21 * 6 * 5, checkedCount);
            Assert.Empty(failures);
        }

        [Fact]
        public void ARotationAtItsHintGivesTheHintBack()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var reference = Reference();
            var cases = new (Vector3 Hint, RotationOrder Order)[]
            {
                (new Vector3(370f, -340f, 30.0004f), RotationOrder.OrderZXY),
                (new Vector3(370f, -340f, 30.002f), RotationOrder.OrderZXY),
                (new Vector3(10f, 20f, 30f), RotationOrder.OrderXYZ),
                (new Vector3(90f, 30f, 20f), RotationOrder.OrderZXY),
                (new Vector3(90f, 30.1f, 20f), RotationOrder.OrderZXY),
                (new Vector3(170f, 170f, 170f), RotationOrder.OrderZXY),
            };
            for (int n = 0; n < cases.Length; n++)
            {
                var row = reference["euler.near." + n].Split(" -> ");
                var got = AnimationUtility.GetClosestEuler(Parse(row[0]), cases[n].Hint, cases[n].Order);
                Assert.True(Same(got, row[1]), $"euler.near.{n}: {V(got)} (Editor {row[1]})");
            }
            // 0.0004 degrees off: the hint itself, not the rounded angles; in gimbal lock the hint
            // keeps its own split of the turn.
            Assert.Equal("370 -340 30.0004", reference["euler.near.0"].Split(" -> ")[1]);
            Assert.Equal("90 30 20", reference["euler.near.3"].Split(" -> ")[1]);
        }

        [Fact]
        public void AZeroRotationReadsItsUndefinedAnglesAsTheLinuxEditor()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            // Not in the probe: the Linux Editor (6000.0.71f1, two batch captures) answers this,
            // the macOS Editor gives NaN angles.
            var zero = new Quaternion(0f, 0f, 0f, 0f);
            Assert.Equal("-83.75 0 -83.75", V(AnimationUtility.GetClosestEuler(zero, Vector3.zero, RotationOrder.OrderXYZ)));
            Assert.Equal("96.25 180 96.25", V(AnimationUtility.GetClosestEuler(zero, new Vector3(170f, 170f, 170f), RotationOrder.OrderXYZ)));
        }

        /// <summary>A release without the measurement reports the member not implemented and
        /// answers nothing, as before.</summary>
        [Fact]
        public void AnUnmeasuredReleaseReportsItNotImplemented()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                var rotation = new Quaternion(0.127679437f, 0.144878134f, 0.239298329f, 0.9515485f);
                Assert.Equal(Vector3.zero, AnimationUtility.GetClosestEuler(rotation, new Vector3(10f, 20f, 30f), RotationOrder.OrderZXY));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
            Assert.Contains(logs.Lines, l => l.Contains("UnityEditor.AnimationUtility.GetClosestEuler is not implemented"));
        }

        sealed class LogCapture : ILogHandler
        {
            public readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(args.Length == 0 ? format : string.Format(format, args));
        }

        /// <summary>Unbound's own answer (unmeasured): an order outside the enum fails loudly.</summary>
        [Fact]
        public void AnUnknownOrderThrows()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimationUtility.GetClosestEuler(Quaternion.identity, Vector3.zero, (RotationOrder)6));
        }
    }
}
