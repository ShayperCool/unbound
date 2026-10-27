using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The gamepads and joysticks attached, as the Linux Editor reads them: through SDL, with
    /// Unity's controller database (the native side, <c>gamepads-poll</c>). Polled once per frame,
    /// before the frame's input: the legacy Input Manager reads the raw joystick (its buttons and
    /// axes in SDL's order), the Input System gets the pads SDL maps as <c>Gamepad</c>s
    /// (<see cref="NativeInput"/>). Recorded against the Editor in tests/projects/gamepad.
    /// </summary>
    internal static class Gamepads
    {
        internal sealed class Device
        {
            internal int Id;
            internal string Name, Manufacturer;
            internal int Vendor, Product, Version;
            internal short[] Axes = Array.Empty<short>();
            internal bool[] Buttons = Array.Empty<bool>();
            internal byte[] Hats = Array.Empty<byte>();
            /// <summary>Whether SDL maps the device as a game controller.</summary>
            internal bool IsGamepad;
            /// <summary>The controller's buttons (SDL_GameControllerButton bits) and axes
            /// (left x, left y, right x, right y, left trigger, right trigger).</summary>
            internal uint PadButtons;
            internal short[] PadAxes = new short[6];
        }

        /// <summary>The attached devices, in the order SDL found them (legacy joystick numbers).</summary>
        internal static readonly List<Device> Attached = new List<Device>();
        internal static readonly List<Device> Added = new List<Device>();
        internal static readonly List<int> Removed = new List<int>();

        static bool s_Unavailable;

        internal static void Poll()
        {
            Added.Clear();
            Removed.Clear();
            if (s_Unavailable) return;
            JsonElement root;
            try
            {
                var family = Application.unityVersion;
                int second = family.IndexOf('.', family.IndexOf('.') + 1);
                if (second > 0) family = family.Substring(0, second);
                root = JsonDocument.Parse(Native.Call("gamepads-poll", JsonSerializer.Serialize(new { family }))).RootElement;
            }
            catch (Exception e)
            {
                s_Unavailable = true;
                Debug.LogWarning($"Unbound: gamepads are not read: {e.Message}");
                return;
            }
            foreach (var id in root.GetProperty("removed").EnumerateArray())
            {
                int removed = id.GetInt32();
                Attached.RemoveAll(d => d.Id == removed);
                Removed.Add(removed);
            }
            foreach (var e in root.GetProperty("devices").EnumerateArray())
            {
                int id = e.GetProperty("id").GetInt32();
                var device = Attached.Find(d => d.Id == id);
                if (device == null)
                {
                    device = new Device
                    {
                        Id = id,
                        Name = e.GetProperty("name").GetString(),
                        Manufacturer = e.GetProperty("manufacturer").GetString(),
                        Vendor = e.GetProperty("vendor").GetInt32(),
                        Product = e.GetProperty("product").GetInt32(),
                        Version = e.GetProperty("version").GetInt32(),
                        IsGamepad = e.TryGetProperty("padButtons", out _),
                    };
                    Attached.Add(device);
                    Added.Add(device);
                }
                device.Axes = Shorts(e.GetProperty("axes"));
                var buttons = e.GetProperty("buttons");
                if (device.Buttons.Length != buttons.GetArrayLength()) device.Buttons = new bool[buttons.GetArrayLength()];
                int b = 0;
                foreach (var v in buttons.EnumerateArray()) device.Buttons[b++] = v.GetInt32() != 0;
                var hats = e.GetProperty("hats");
                if (device.Hats.Length != hats.GetArrayLength()) device.Hats = new byte[hats.GetArrayLength()];
                int h = 0;
                foreach (var v in hats.EnumerateArray()) device.Hats[h++] = (byte)v.GetInt32();
                if (device.IsGamepad)
                {
                    device.PadButtons = e.GetProperty("padButtons").GetUInt32();
                    device.PadAxes = Shorts(e.GetProperty("padAxes"));
                }
            }
        }

        static short[] Shorts(JsonElement array)
        {
            var values = new short[array.GetArrayLength()];
            int i = 0;
            foreach (var v in array.EnumerateArray()) values[i++] = (short)v.GetInt32();
            return values;
        }

        /// <summary>Legacy joystick <paramref name="joystick"/>'s (1-based) button.</summary>
        internal static bool Button(int joystick, int button)
        {
            if (joystick < 1 || joystick > Attached.Count) return false;
            var buttons = Attached[joystick - 1].Buttons;
            return button >= 0 && button < buttons.Length && buttons[button];
        }

        /// <summary>Legacy joystick axis: <c>axis/32767</c>, clamped (measured); joystick 0 is any
        /// joystick (the one pushed furthest).</summary>
        internal static float Axis(int joystick, int axis)
        {
            float best = 0f;
            for (int j = 0; j < Attached.Count; j++)
            {
                if (joystick != 0 && joystick != j + 1) continue;
                var axes = Attached[j].Axes;
                if (axis < 0 || axis >= axes.Length) continue;
                float v = Mathf.Clamp(axes[axis] / 32767f, -1f, 1f);
                if (Mathf.Abs(v) > Mathf.Abs(best)) best = v;
            }
            return best;
        }
    }
}
