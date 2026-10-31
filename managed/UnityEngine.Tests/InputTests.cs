using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Unity.IntegerTime;
using Unbound.Runtime;
using UnityEngineInternal.Input;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Input System's engine side against what the Editor recorded in
    /// tests/projects/input/Reference (the probe's README tells how).</summary>
    public unsafe class InputTests
    {
        static string Reference(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.Combine(Path.GetDirectoryName(source), "..", "..", "tests", "projects", "input", "Reference", name);

        static IEnumerable<JsonElement> Lines(string name)
        {
            foreach (var line in File.ReadAllLines(Reference(name)))
                if (line.Trim().Length > 0) yield return JsonDocument.Parse(line).RootElement;
        }

        static RationalTime Rational(JsonElement e) =>
            new RationalTime(e[0].GetInt64(), new RationalTime.TicksPerSecond(e[1].GetUInt32(), e[2].GetUInt32()));

        static string Show(RationalTime t) => $"[{t.Count},{t.Ticks.Numerator},{t.Ticks.Denominator}]";

        static string Expected(JsonElement r) => r.ValueKind == JsonValueKind.String ? r.GetString() : r.GetRawText();

        static string Run(Func<string> f)
        {
            try { return f(); }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        [Fact]
        public void RationalTimeArithmeticMatchesTheEditor()
        {
            int checkedCount = 0;
            foreach (var line in Lines("integertime-6000.0.71f1.jsonl"))
            {
                var op = line.GetProperty("op").GetString();
                string actual;
                switch (op)
                {
                    case "FromDouble":
                    {
                        var rate = line.GetProperty("rate");
                        var tps = new RationalTime.TicksPerSecond(rate[0].GetUInt32(), rate[1].GetUInt32());
                        double t = line.GetProperty("t").GetDouble();
                        actual = Run(() => Show(RationalTime.FromDouble(t, tps)));
                        break;
                    }
                    case "ToDouble":
                        actual = Run(() => Rational(line.GetProperty("a")).ToDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                        Assert.Equal(line.GetProperty("r").GetDouble(), double.Parse(actual, System.Globalization.CultureInfo.InvariantCulture));
                        checkedCount++;
                        continue;
                    case "ToDiscrete":
                        actual = Run(() => ((DiscreteTime)Rational(line.GetProperty("a"))).Value.ToString());
                        break;
                    case "Convert":
                    {
                        var rate = line.GetProperty("rate");
                        var tps = new RationalTime.TicksPerSecond(rate[0].GetUInt32(), rate[1].GetUInt32());
                        actual = Run(() => Show(Rational(line.GetProperty("a")).Convert(tps)));
                        break;
                    }
                    case "Add":
                    case "Subtract":
                    case "Multiply":
                    case "Divide":
                    {
                        var a = Rational(line.GetProperty("a"));
                        var b = Rational(line.GetProperty("b"));
                        actual = Run(() => Show(op == "Add" ? a.Add(b) : op == "Subtract" ? a.Subtract(b) : op == "Multiply" ? a.Multiply(b) : a.Divide(b)));
                        break;
                    }
                    case "Valid":
                        Assert.Equal(line.GetProperty("zero").GetInt32() == 1, new RationalTime.TicksPerSecond(0).Valid);
                        Assert.Equal(line.GetProperty("zeroDen").GetInt32() == 1, new RationalTime.TicksPerSecond(5, 0).Valid);
                        Assert.Equal(line.GetProperty("defaultValid").GetInt32() == 1, default(RationalTime).IsValid());
                        continue;
                    default:
                        continue;
                }
                Assert.True(Expected(line.GetProperty("r")) == actual, $"{line.GetRawText()}: Unbound {actual}");
                checkedCount++;
            }
            Assert.True(checkedCount > 200);
        }

        sealed class Asset : ScriptableObject { }

        [Fact]
        public void ScriptedImporterObjectsGetTheEditorsFileIds()
        {
            var obj = ScriptableObject.CreateInstance<Asset>();
            int count = 0;
            foreach (var line in Lines("fileids-6000.0.71f1.jsonl"))
            {
                Assert.Equal(line.GetProperty("fileId").GetInt64(), ScriptedImports.FileId(line.GetProperty("identifier").GetString(), obj));
                count++;
            }
            Assert.Equal(66, count);
            Object.DestroyImmediate(obj);
        }

        /// <summary>Runs an update and returns the events it handed out, keeping
        /// <paramref name="keep"/> of them (from the end) for the next update.</summary>
        static List<byte[]> TakeEvents(int keep = 0)
        {
            var events = new List<byte[]>();
            NativeInputSystem.onUpdate = (type, buffer) =>
            {
                var p = (byte*)buffer->eventBuffer;
                var all = new List<byte[]>();
                for (int i = 0; i < buffer->eventCount; i++)
                {
                    int size = ((NativeInputEvent*)p)->sizeInBytes;
                    var bytes = new byte[size];
                    Marshal.Copy((IntPtr)p, bytes, 0, size);
                    all.Add(bytes);
                    p += (size + 3) & ~3;
                }
                events.AddRange(all);
                // Leave the last `keep` events, compacted to the front as the package does.
                int skip = all.Count - keep;
                var dst = (byte*)buffer->eventBuffer;
                int written = 0;
                for (int i = skip; i < all.Count; i++)
                {
                    Marshal.Copy(all[i], 0, (IntPtr)(dst + written), all[i].Length);
                    written += (all[i].Length + 3) & ~3;
                }
                buffer->eventCount = keep;
                buffer->sizeInBytes = written;
            };
            try { NativeInput.Update(NativeInputUpdateType.Dynamic); }
            finally { NativeInputSystem.onUpdate = null; }
            return events;
        }

        static void Frame(FrameInput input)
        {
            var keys = new byte[64];
            fixed (byte* k = keys) { input.Keys = k; NativeInput.BeginFrame(&input); }
        }

        [Fact]
        public void TheWindowsInputBecomesTheEventsTheLinuxEditorQueues()
        {
            NativeInput.Enabled = true;
            try
            {
                TakeEvents();
                // A frame where W is pressed with its text and Escape without: Unity sends a KEYS
                // state per change and a TEXT per character, Escape's control character included.
                var events = new[]
                {
                    new KeyEvent { Key = (int)KeyCode.W, Pressed = 1, TextStart = 0, TextLength = 1 },
                    new KeyEvent { Key = (int)KeyCode.Escape, Pressed = 1 },
                    new KeyEvent { Key = (int)KeyCode.Escape, Pressed = 0 },
                    new KeyEvent { Key = (int)KeyCode.W, Pressed = 0 },
                };
                var text = Encoding.UTF8.GetBytes("w");
                fixed (KeyEvent* e = events)
                fixed (byte* t = text)
                    Frame(new FrameInput { KeyEvents = e, KeyEventCount = events.Length, Text = t, TextLength = text.Length, MouseX = 10, MouseY = 20, ScreenWidth = 960, ScreenHeight = 540 });
                var queued = TakeEvents();
                Assert.Equal(6, queued.Count);
                string Hex(byte[] b, int from, int count) => Convert.ToHexString(b, from, count).ToLowerInvariant();
                // The probe's reference: W down is KEYS 0000000020 (bit 37) in a 20-byte state.
                Assert.Equal("STAT", Encoding.ASCII.GetString(queued[0], 0, 4).Reverse());
                Assert.Equal(44, queued[0].Length);
                Assert.Equal("0000000020000000000000000000000000000000", Hex(queued[0], 24, 20));
                Assert.Equal('w', BitConverter.ToInt32(queued[1], 20));
                Assert.Equal("0000000020000010000000000000000000000000", Hex(queued[2], 24, 20));
                Assert.Equal(0x1b, BitConverter.ToInt32(queued[3], 20));
                Assert.Equal("0000000020000000000000000000000000000000", Hex(queued[4], 24, 20));
                Assert.Equal("0000000000000000000000000000000000000000", Hex(queued[5], 24, 20));
                // Event ids increase.
                Assert.True(BitConverter.ToInt32(queued[1], 16) == BitConverter.ToInt32(queued[0], 16) + 1);

                // The pointer: the first frame tells where it is, a move then sends position and
                // delta from the bottom left; a press sends the button bit.
                Frame(new FrameInput { MouseX = 10, MouseY = 20, ScreenWidth = 960, ScreenHeight = 540 });
                Assert.Empty(TakeEvents());
                Frame(new FrameInput { MouseX = 100, MouseY = 140, MouseButtons = 1, ScrollY = 1, ScreenWidth = 960, ScreenHeight = 540 });
                var mouse = TakeEvents(keep: 1);
                Assert.Equal(3, mouse.Count);
                Assert.Equal(56, mouse[0].Length);
                Assert.Equal(new Vector2(100, 400), new Vector2(BitConverter.ToSingle(mouse[0], 24), BitConverter.ToSingle(mouse[0], 28)));
                Assert.Equal(1, BitConverter.ToUInt16(mouse[1], 24 + 24));
                Assert.Equal(1f, BitConverter.ToSingle(mouse[2], 24 + 20));
                // What an update leaves is handed out again first.
                var left = TakeEvents();
                Assert.Single(left);
                Assert.Equal(mouse[2], left[0]);
            }
            finally
            {
                NativeInput.Enabled = false;
            }
        }

        [Fact]
        public void PressesAndReleasesWithinAFrameKeepTheirOrder()
        {
            NativeInput.Enabled = true;
            try
            {
                TakeEvents();
                Frame(new FrameInput { MouseX = 10, MouseY = 20, ScreenWidth = 960, ScreenHeight = 540 });
                TakeEvents();
                // A click with a key typed in it, all between two frames (tests/projects/inputframes,
                // frame 440): each change is its own event, in order.
                var events = new[]
                {
                    new KeyEvent { Key = (int)KeyCode.A, Pressed = 1, TextStart = 0, TextLength = 1 },
                    new KeyEvent { Key = (int)KeyCode.Mouse0, Pressed = 1, TextStart = 1 },
                    new KeyEvent { Key = (int)KeyCode.A, Pressed = 0, TextStart = 1 },
                    new KeyEvent { Key = (int)KeyCode.Mouse0, Pressed = 0, TextStart = 1 },
                };
                var text = Encoding.UTF8.GetBytes("a");
                fixed (KeyEvent* e = events)
                fixed (byte* t = text)
                {
                    var input = new FrameInput { KeyEvents = e, KeyEventCount = events.Length, Text = t, TextLength = text.Length, MouseX = 10, MouseY = 20, ScreenWidth = 960, ScreenHeight = 540 };
                    Input.BeginFrame(&input);
                    Frame(input);
                }
                var queued = TakeEvents();
                var kinds = new List<string>();
                foreach (var q in queued)
                {
                    string type = Encoding.ASCII.GetString(q, 0, 4).Reverse();
                    if (type == "TEXT") { kinds.Add("text " + (char)BitConverter.ToInt32(q, 20)); continue; }
                    string format = Encoding.ASCII.GetString(q, 20, 4).Reverse();
                    kinds.Add(format == "MOUS" ? "mouse " + BitConverter.ToUInt16(q, 24 + 24) : "keys " + Convert.ToHexString(q, 24, 3));
                }
                Assert.Equal(new[] { "keys 008000", "text a", "mouse 1", "keys 000000", "mouse 0" }, kinds);

                // The Input Manager: down, up and held for the frame.
                Assert.True(Input.GetKeyDown(KeyCode.A) && Input.GetKeyUp(KeyCode.A) && Input.GetKey(KeyCode.A));
                Assert.True(Input.GetMouseButtonDown(0) && Input.GetMouseButtonUp(0) && Input.GetMouseButton(0));
                // IMGUI: the same order; mouse events carry the button's key code.
                var gui = new List<string>();
                foreach (var g in GuiEvents.Frame)
                    gui.Add($"{g.type} {g.keyCode} {(int)g.character}");
                Assert.Equal(new[] { "KeyDown A 0", "KeyDown None 97", "mouseDown Mouse0 0", "keyUp A 0", "mouseUp Mouse0 0" }, gui);

                // The next frame: nothing pressed, nothing held.
                var empty = new FrameInput { MouseX = 10, MouseY = 20, ScreenWidth = 960, ScreenHeight = 540 };
                Input.BeginFrame(&empty);
                Assert.False(Input.GetKey(KeyCode.A) || Input.GetKeyUp(KeyCode.A) || Input.GetMouseButton(0));
                Assert.Empty(GuiEvents.Frame);
            }
            finally
            {
                NativeInput.Enabled = false;
                TakeEvents();
            }
        }

        [Fact]
        public void GuiEventsCarryTheModifiersHeldAfterThem()
        {
            var events = new[]
            {
                new KeyEvent { Key = (int)KeyCode.LeftShift, Pressed = 1 },
                new KeyEvent { Key = (int)KeyCode.A, Pressed = 1, TextStart = 0, TextLength = 1 },
                new KeyEvent { Key = (int)KeyCode.A, Pressed = 0, TextStart = 1 },
                new KeyEvent { Key = (int)KeyCode.LeftShift, Pressed = 0, TextStart = 1 },
            };
            var text = Encoding.UTF8.GetBytes("A");
            fixed (KeyEvent* e = events)
            fixed (byte* t = text)
            {
                var input = new FrameInput { KeyEvents = e, KeyEventCount = events.Length, Text = t, TextLength = text.Length, MouseX = 10, MouseY = 20, ScreenWidth = 960, ScreenHeight = 540 };
                Input.BeginFrame(&input);
            }
            var gui = new List<string>();
            foreach (var g in GuiEvents.Frame)
                gui.Add($"{g.type} {g.keyCode} {g.modifiers}");
            // Measured (tests/projects/inputframes, frames 420-424): Shift's press has Shift, its
            // release none; modifier keys are function keys.
            Assert.Equal(new[]
            {
                "KeyDown LeftShift Shift, FunctionKey", "KeyDown A Shift", "KeyDown None Shift", "keyUp A Shift", "keyUp LeftShift FunctionKey",
            }, gui);
            var empty = new FrameInput { MouseX = 10, MouseY = 20, ScreenWidth = 960, ScreenHeight = 540 };
            Input.BeginFrame(&empty);
        }

        [Fact]
        public void DeviceCommandsAnswerAsTheLinuxEditor()
        {
            NativeInput.Enabled = true;
            try
            {
                var buffer = new byte[300];
                fixed (byte* p = buffer)
                {
                    // KBLT: the layout name, length-prefixed UTF-16.
                    Assert.Equal(1, NativeInputSystem.IOCTL(NativeInput.KeyboardId, 0x4B424C54, (IntPtr)p, 256));
                    Assert.Equal("English (US)", new string((char*)(p + 2), 0, *(ushort*)p));
                    // KYCF for Key.A (15): scan code 15, name "A"; Key.F19 answers its keysym.
                    *(int*)p = 15;
                    Assert.Equal(1, NativeInputSystem.IOCTL(NativeInput.KeyboardId, 0x4B594346, (IntPtr)p, 260));
                    Assert.Equal(15, *(int*)p);
                    Assert.Equal("A", new string((char*)(p + 6), 0, *(ushort*)(p + 4)));
                    *(int*)p = 118;
                    NativeInputSystem.IOCTL(NativeInput.KeyboardId, 0x4B594346, (IntPtr)p, 260);
                    Assert.Equal(0xFFD0, *(int*)p);
                    // Everything else fails, on the mouse too.
                    Assert.Equal(-1, NativeInputSystem.IOCTL(NativeInput.KeyboardId, 0x51454E42, (IntPtr)p, 4));
                    Assert.Equal(-1, NativeInputSystem.IOCTL(NativeInput.MouseId, 0x4B424C54, (IntPtr)p, 256));
                }
            }
            finally
            {
                NativeInput.Enabled = false;
            }
        }
    }

    static class StringReverse
    {
        /// <summary>A FourCC's characters as the package prints them (stored little-endian).</summary>
        internal static string Reverse(this string s)
        {
            var chars = s.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
    }
}
