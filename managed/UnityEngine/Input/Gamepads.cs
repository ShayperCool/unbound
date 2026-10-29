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
        /// <summary>The Unity family (<c>6000.0</c>), UTF-8: SDL gets that release's controller database.</summary>
        static byte[] s_Family;
        static byte[] s_State = new byte[1024];
        /// <summary>The attached set's generation the last description was read at; the devices are
        /// described (names, ids: <c>gamepads-poll</c>) only when it moves.</summary>
        static uint s_Generation;
        static bool s_Described;

        internal static void Poll()
        {
            Added.Clear();
            Removed.Clear();
            if (s_Unavailable) return;
            int size;
            try
            {
                if (s_Family == null)
                {
                    var family = Application.unityVersion;
                    int second = family.IndexOf('.', family.IndexOf('.') + 1);
                    if (second > 0) family = family.Substring(0, second);
                    s_Family = System.Text.Encoding.UTF8.GetBytes(family);
                }
                size = Native.GamepadsState(s_Family, s_State);
                if (size > s_State.Length)
                {
                    s_State = new byte[size * 2];
                    size = Native.GamepadsState(s_Family, s_State);
                }
            }
            catch (Exception e)
            {
                s_Unavailable = true;
                Debug.LogWarning($"Unbound: gamepads are not read: {e.Message}");
                return;
            }
            if (size < 8 || size > s_State.Length) return; // no SDL, or the set grew again meanwhile
            uint generation = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(s_State);
            if (s_Described && generation == s_Generation)
            {
                ReadState(s_State.AsSpan(0, size));
                return;
            }
            Describe();
        }

        /// <summary>The devices attached and detached, their names and ids, and their input.</summary>
        static void Describe()
        {
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(Native.Call("gamepads-poll", JsonSerializer.Serialize(new { family = System.Text.Encoding.UTF8.GetString(s_Family) }))).RootElement;
            }
            catch (Exception e)
            {
                s_Unavailable = true;
                Debug.LogWarning($"Unbound: gamepads are not read: {e.Message}");
                return;
            }
            s_Described = true;
            s_Generation = root.GetProperty("generation").GetUInt32();
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

        /// <summary>The frame's input of the described devices (the layout of the native
        /// <c>gamepads::state</c>).</summary>
        static void ReadState(ReadOnlySpan<byte> state)
        {
            int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(state.Slice(4));
            int at = 8;
            static int Int(ReadOnlySpan<byte> s, ref int offset)
            {
                int v = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(s.Slice(offset));
                offset += 4;
                return v;
            }
            static short Short(ReadOnlySpan<byte> s, ref int offset)
            {
                short v = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(s.Slice(offset));
                offset += 2;
                return v;
            }
            for (int k = 0; k < count; k++)
            {
                int id = Int(state, ref at), axes = Int(state, ref at), buttons = Int(state, ref at), hats = Int(state, ref at);
                bool isPad = Int(state, ref at) != 0;
                uint padButtons = (uint)Int(state, ref at);
                Device device = null;
                foreach (var d in Attached)
                    if (d.Id == id) device = d;
                if (device != null)
                {
                    device.PadButtons = isPad ? padButtons : 0;
                    for (int i = 0; i < 6; i++) device.PadAxes[i] = Short(state, ref at);
                    if (device.Axes.Length != axes) device.Axes = new short[axes];
                    for (int i = 0; i < axes; i++) device.Axes[i] = Short(state, ref at);
                    if (device.Buttons.Length != buttons) device.Buttons = new bool[buttons];
                    for (int i = 0; i < buttons; i++) device.Buttons[i] = state[at++] != 0;
                    if (device.Hats.Length != hats) device.Hats = new byte[hats];
                    for (int i = 0; i < hats; i++) device.Hats[i] = state[at++];
                }
                else
                {
                    at += 12 + 2 * axes + buttons + hats;
                }
                at = (at + 3) & ~3;
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
