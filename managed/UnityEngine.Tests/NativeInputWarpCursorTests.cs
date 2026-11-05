using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngineInternal.Input;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The mouse's warp command (Input System 1.19 <c>Mouse.WarpCursorPosition</c>, the
    /// 'WPMS' command with the position in player window space as its payload), next to the device
    /// command answers the Linux Editor gave in tests/projects/input/Reference.</summary>
    public unsafe class NativeInputWarpCursorTests
    {
        const int Warp = 0x57504D53; // 'WPMS'

        static string Reference([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.Combine(Path.GetDirectoryName(source), "..", "..", "tests", "projects", "input", "Reference", "unity-6000.0.71f1.jsonl");

        /// <summary>The measured command answers of one device: the command's FourCC (the label's
        /// first four characters) and the result.</summary>
        static List<(int Code, long Result, string Label)> Answers(string device)
        {
            var answers = new List<(int, long, string)>();
            foreach (var line in File.ReadAllLines(Reference()))
            {
                if (line.Trim().Length == 0) continue;
                var r = JsonDocument.Parse(line).RootElement;
                if (!r.TryGetProperty("k", out var k) || k.GetString() != "ioctl" || r.GetProperty("device").GetString() != device) continue;
                string label = r.GetProperty("type").GetString();
                int code = (label[0] << 24) | (label[1] << 16) | (label[2] << 8) | label[3];
                answers.Add((code, r.GetProperty("result").GetInt64(), label));
            }
            return answers;
        }

        /// <summary>Runs <paramref name="body"/> with the Input System's backends on and the warps
        /// the window would get collected (answering <paramref name="accept"/>).</summary>
        static List<Vector2> WithWarps(Action body, bool accept = true)
        {
            var warps = new List<Vector2>();
            var send = NativeInput.RequestWarp;
            NativeInput.Enabled = true;
            NativeInput.RequestWarp = position => { warps.Add(position); return accept; };
            try { body(); }
            finally
            {
                NativeInput.RequestWarp = send;
                NativeInput.Enabled = false;
            }
            return warps;
        }

        static long Command(int device, int code, float x, float y, int size = 8)
        {
            var payload = stackalloc float[64];
            payload[0] = x;
            payload[1] = y;
            return NativeInputSystem.IOCTL(device, code, (IntPtr)payload, size);
        }

        [Fact]
        public void TheMouseWarpSucceedsAndHandsTheWindowItsPosition()
        {
            long result = 0;
            var warps = WithWarps(() => result = Command(NativeInput.MouseId, Warp, 123.5f, 234f));
            Assert.Equal(1, result);
            // Player window space as given: Unity's screen coordinates, from the bottom left.
            Assert.Equal(new[] { new Vector2(123.5f, 234f) }, warps);
        }

        [Fact]
        public void AWarpTheWindowRefusesFails()
        {
            long result = 0;
            var warps = WithWarps(() => result = Command(NativeInput.MouseId, Warp, 10f, 20f), accept: false);
            Assert.Equal(-1, result);
            Assert.Single(warps);
        }

        [Fact]
        public void AWarpWithoutAPositionFailsWithoutMovingTheCursor()
        {
            var results = new List<long>();
            var warps = WithWarps(() =>
            {
                results.Add(Command(NativeInput.MouseId, Warp, 10f, 20f, size: 4));
                results.Add(Command(NativeInput.MouseId, Warp, float.NaN, 20f));
                results.Add(Command(NativeInput.MouseId, Warp, 10f, float.PositiveInfinity));
                results.Add(NativeInputSystem.IOCTL(NativeInput.MouseId, Warp, IntPtr.Zero, 8));
            });
            Assert.Equal(new long[] { -1, -1, -1, -1 }, results);
            Assert.Empty(warps);
        }

        [Fact]
        public void AWarpFailsWhileTheInputSystemsBackendsAreOff()
        {
            long result = 0;
            var warps = WithWarps(() =>
            {
                NativeInput.Enabled = false;
                result = Command(NativeInput.MouseId, Warp, 10f, 20f);
            });
            Assert.Equal(-1, result);
            Assert.Empty(warps);
        }

        [Fact]
        public void TheKeyboardAnswersAsBeforeAndDoesNotWarp()
        {
            var answers = Answers("Keyboard");
            Assert.Equal(13, answers.Count);
            var buffer = new byte[300];
            var results = new List<long>();
            long warp = 0;
            var warps = WithWarps(() =>
            {
                fixed (byte* p = buffer)
                {
                    foreach (var (code, _, _) in answers)
                    {
                        Array.Clear(buffer);
                        results.Add(NativeInputSystem.IOCTL(NativeInput.KeyboardId, code, (IntPtr)p, 256));
                    }
                    // The layout name, as before.
                    Array.Clear(buffer);
                    NativeInputSystem.IOCTL(NativeInput.KeyboardId, 0x4B424C54, (IntPtr)p, 256);
                    Assert.Equal("English (US)", new string((char*)(p + 2), 0, *(ushort*)p));
                    // KYCF for Key.A (15): scan code 15, name "A".
                    Array.Clear(buffer);
                    *(int*)p = 15;
                    Assert.Equal(1, NativeInputSystem.IOCTL(NativeInput.KeyboardId, 0x4B594346, (IntPtr)p, 260));
                    Assert.Equal(15, *(int*)p);
                    Assert.Equal("A", new string((char*)(p + 6), 0, *(ushort*)(p + 4)));
                }
                warp = Command(NativeInput.KeyboardId, Warp, 10f, 20f);
            });
            for (int i = 0; i < answers.Count; i++)
                Assert.True(answers[i].Result == results[i], $"Keyboard {answers[i].Label}: the Editor answered {answers[i].Result}, Unbound {results[i]}");
            Assert.Equal(-1, warp);
            Assert.Empty(warps);
        }

        [Fact]
        public void TheMouseStillFailsEveryCommandTheEditorFailed()
        {
            var answers = Answers("Mouse");
            Assert.Equal(13, answers.Count);
            var results = new List<long>();
            // An 8-byte payload, as large as the warp's: only the command decides.
            var warps = WithWarps(() =>
            {
                foreach (var (code, _, _) in answers)
                    results.Add(Command(NativeInput.MouseId, code, 10f, 20f));
                // A device the window does not have.
                results.Add(Command(NativeInput.KeyboardId + 1, Warp, 10f, 20f));
            });
            for (int i = 0; i < answers.Count; i++)
                Assert.True(answers[i].Result == results[i], $"Mouse {answers[i].Label}: the Editor answered {answers[i].Result}, Unbound {results[i]}");
            Assert.Equal(-1, results[answers.Count]);
            Assert.Empty(warps);
        }
    }
}
