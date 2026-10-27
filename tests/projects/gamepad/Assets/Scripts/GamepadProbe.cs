using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// A gamepad as the Editor sees one: a virtual Xbox 360 pad made through Linux's uinput (by this
/// script, so the same device and steps play in every engine), pressed and moved frame by frame.
/// Each frame records what the Input System reports (its devices and their descriptions, the
/// gamepad's and joystick's controls) and what legacy Input does (joystick names, joystick
/// buttons, the InputManager's joystick axes and buttons). Writes <c>gamepad.jsonl</c> into
/// <c>GAMEPAD_PROBE_OUT</c>, then quits.
/// </summary>
public class GamepadProbe : MonoBehaviour
{
    const int Create = 5, Destroy = 150, End = 175;

    static readonly StringBuilder s_Out = new StringBuilder();
    int m_Frame;
    int m_Fd = -1;
    string m_LastDevices;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (Environment.GetEnvironmentVariable("GAMEPAD_PROBE_OUT") == null) return;
        var go = new GameObject("GamepadProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<GamepadProbe>();
    }

    // ---------------------------------------------------------------- uinput

    [DllImport("libc", SetLastError = true)] static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] static extern int ioctl(int fd, ulong request, int value);
    [DllImport("libc", SetLastError = true)] static extern int ioctl(int fd, ulong request, byte[] data);
    [DllImport("libc", SetLastError = true)] static extern IntPtr write(int fd, byte[] data, IntPtr count);

    const ulong UI_SET_EVBIT = 0x40045564, UI_SET_KEYBIT = 0x40045565, UI_SET_ABSBIT = 0x40045567;
    const ulong UI_DEV_SETUP = 0x405c5503, UI_ABS_SETUP = 0x401c5504, UI_DEV_CREATE = 0x5501, UI_DEV_DESTROY = 0x5502;
    const int EV_SYN = 0, EV_KEY = 1, EV_ABS = 3;
    const int BTN_A = 0x130, BTN_B = 0x131, BTN_X = 0x133, BTN_Y = 0x134, BTN_TL = 0x136, BTN_TR = 0x137,
        BTN_SELECT = 0x13a, BTN_START = 0x13b, BTN_MODE = 0x13c, BTN_THUMBL = 0x13d, BTN_THUMBR = 0x13e;
    const int ABS_X = 0, ABS_Y = 1, ABS_Z = 2, ABS_RX = 3, ABS_RY = 4, ABS_RZ = 5, ABS_HAT0X = 0x10, ABS_HAT0Y = 0x11;

    static readonly int[] s_Buttons = { BTN_A, BTN_B, BTN_X, BTN_Y, BTN_TL, BTN_TR, BTN_SELECT, BTN_START, BTN_MODE, BTN_THUMBL, BTN_THUMBR };

    bool CreatePad()
    {
        m_Fd = open("/dev/uinput", 0x801); // O_WRONLY | O_NONBLOCK
        if (m_Fd < 0) return false;
        ioctl(m_Fd, UI_SET_EVBIT, EV_KEY);
        foreach (var b in s_Buttons) ioctl(m_Fd, UI_SET_KEYBIT, b);
        ioctl(m_Fd, UI_SET_EVBIT, EV_ABS);
        void Abs(int code, int min, int max, int fuzz, int flat)
        {
            ioctl(m_Fd, UI_SET_ABSBIT, code);
            var setup = new byte[28];
            BitConverter.GetBytes((ushort)code).CopyTo(setup, 0);
            BitConverter.GetBytes(0).CopyTo(setup, 4);
            BitConverter.GetBytes(min).CopyTo(setup, 8);
            BitConverter.GetBytes(max).CopyTo(setup, 12);
            BitConverter.GetBytes(fuzz).CopyTo(setup, 16);
            BitConverter.GetBytes(flat).CopyTo(setup, 20);
            ioctl(m_Fd, UI_ABS_SETUP, setup);
        }
        foreach (var a in new[] { ABS_X, ABS_Y, ABS_RX, ABS_RY }) Abs(a, -32768, 32767, 16, 128);
        Abs(ABS_Z, 0, 255, 0, 0);
        Abs(ABS_RZ, 0, 255, 0, 0);
        Abs(ABS_HAT0X, -1, 1, 0, 0);
        Abs(ABS_HAT0Y, -1, 1, 0, 0);
        var dev = new byte[92];
        BitConverter.GetBytes((ushort)0x03).CopyTo(dev, 0);
        BitConverter.GetBytes((ushort)0x045e).CopyTo(dev, 2);
        BitConverter.GetBytes((ushort)0x028e).CopyTo(dev, 4);
        BitConverter.GetBytes((ushort)0x0110).CopyTo(dev, 6);
        Encoding.ASCII.GetBytes("Microsoft X-Box 360 pad").CopyTo(dev, 8);
        if (ioctl(m_Fd, UI_DEV_SETUP, dev) < 0) return false;
        return ioctl(m_Fd, UI_DEV_CREATE, 0) >= 0;
    }

    void Emit(int type, int code, int value)
    {
        var e = new byte[24];
        BitConverter.GetBytes((ushort)type).CopyTo(e, 16);
        BitConverter.GetBytes((ushort)code).CopyTo(e, 18);
        BitConverter.GetBytes(value).CopyTo(e, 20);
        write(m_Fd, e, (IntPtr)e.Length);
    }

    void Send(params (int Type, int Code, int Value)[] events)
    {
        if (m_Fd < 0) return;
        foreach (var e in events) Emit(e.Type, e.Code, e.Value);
        Emit(EV_SYN, 0, 0);
    }

    /// <summary>The steps, by frame.</summary>
    void Step(int frame)
    {
        switch (frame)
        {
            case Create: Record("create", $"\"ok\":{B(CreatePad())}"); break;
            case 60: Send((EV_KEY, BTN_A, 1)); break;
            case 64: Send((EV_KEY, BTN_A, 0)); break;
            case 68: Send((EV_KEY, BTN_B, 1), (EV_KEY, BTN_Y, 1)); break;
            case 72: Send((EV_KEY, BTN_B, 0), (EV_KEY, BTN_Y, 0)); break;
            case 76: Send((EV_ABS, ABS_X, 32767)); break;
            case 80: Send((EV_ABS, ABS_Y, -32768)); break;
            case 84: Send((EV_ABS, ABS_X, -16384), (EV_ABS, ABS_Y, 8192)); break;
            case 88: Send((EV_ABS, ABS_X, 0), (EV_ABS, ABS_Y, 0)); break;
            case 92: Send((EV_ABS, ABS_RX, 20000), (EV_ABS, ABS_RY, -20000)); break;
            case 96: Send((EV_ABS, ABS_RX, 0), (EV_ABS, ABS_RY, 0)); break;
            case 100: Send((EV_ABS, ABS_Z, 255)); break;
            case 104: Send((EV_ABS, ABS_RZ, 128)); break;
            case 108: Send((EV_ABS, ABS_Z, 0), (EV_ABS, ABS_RZ, 0)); break;
            case 112: Send((EV_ABS, ABS_HAT0X, 1)); break;
            case 116: Send((EV_ABS, ABS_HAT0Y, -1)); break;
            case 120: Send((EV_ABS, ABS_HAT0X, 0), (EV_ABS, ABS_HAT0Y, 0)); break;
            case 124: Send((EV_KEY, BTN_START, 1), (EV_KEY, BTN_TL, 1), (EV_KEY, BTN_THUMBR, 1)); break;
            case 128: Send((EV_KEY, BTN_START, 0), (EV_KEY, BTN_TL, 0), (EV_KEY, BTN_THUMBR, 0)); break;
            case 132: Send((EV_KEY, BTN_SELECT, 1), (EV_KEY, BTN_X, 1)); break;
            case 136: Send((EV_KEY, BTN_SELECT, 0), (EV_KEY, BTN_X, 0)); break;
            case Destroy:
                if (m_Fd >= 0)
                {
                    ioctl(m_Fd, UI_DEV_DESTROY, 0);
                    close(m_Fd);
                    m_Fd = -1;
                }
                break;
        }
    }

    // ---------------------------------------------------------------- recording

    static string J(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string B(bool v) => v ? "true" : "false";
    void Record(string kind, string body) => s_Out.Append("{\"frame\":").Append(m_Frame).Append(",\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    static string Button(ButtonControl b) => b == null ? "null" : $"[{B(b.isPressed)},{B(b.wasPressedThisFrame)},{B(b.wasReleasedThisFrame)},{F(b.ReadValue())}]";
    static string Vec(Vector2Control c) => c == null ? "null" : $"[{F(c.ReadValue().x)},{F(c.ReadValue().y)}]";

    void Update()
    {
        // Frames take at least 16 ms, so the pad's hotplug (udev, then SDL) lands on the same
        // frames however fast the frames run.
        System.Threading.Thread.Sleep(16);
        m_Frame++;
        Step(m_Frame);
        var devices = string.Join(",", InputSystem.devices.Select(d =>
            $"{{\"name\":{J(d.name)},\"layout\":{J(d.layout)},\"type\":{J(d.GetType().FullName)},\"interface\":{J(d.description.interfaceName)},\"product\":{J(d.description.product)}," +
            $"\"manufacturer\":{J(d.description.manufacturer)},\"deviceClass\":{J(d.description.deviceClass)},\"version\":{J(d.description.version)},\"serial\":{J(d.description.serial)}," +
            $"\"capabilities\":{J(d.description.capabilities)},\"controls\":{d.allControls.Count}}}"));
        if (devices != m_LastDevices)
        {
            m_LastDevices = devices;
            Record("devices", $"\"devices\":[{devices}],\"gamepads\":{Gamepad.all.Count},\"joysticks\":{Joystick.all.Count}");
        }
        var pad = Gamepad.current;
        if (pad != null && pad.added)
        {
            Record("gamepad", $"\"name\":{J(pad.name)},\"south\":{Button(pad.buttonSouth)},\"east\":{Button(pad.buttonEast)},\"west\":{Button(pad.buttonWest)},\"north\":{Button(pad.buttonNorth)}," +
                $"\"lb\":{Button(pad.leftShoulder)},\"rb\":{Button(pad.rightShoulder)},\"lt\":{Button(pad.leftTrigger)},\"rt\":{Button(pad.rightTrigger)},\"start\":{Button(pad.startButton)},\"select\":{Button(pad.selectButton)}," +
                $"\"ls\":{Button(pad.leftStickButton)},\"rs\":{Button(pad.rightStickButton)},\"left\":{Vec(pad.leftStick)},\"right\":{Vec(pad.rightStick)},\"dpad\":{Vec(pad.dpad)}," +
                $"\"lastUpdate\":{F((float)(pad.lastUpdateTime > 0 ? 1 : 0))}");
        }
        var stick = Joystick.current;
        if (stick != null && stick.added)
            Record("joystick", $"\"name\":{J(stick.name)},\"trigger\":{Button(stick.trigger)},\"stick\":{Vec(stick.stick)}");
        var names = Input.GetJoystickNames();
        var keys = Enumerable.Range(0, 12).Where(i => Input.GetKey(KeyCode.JoystickButton0 + i)).Select(i => i.ToString(CultureInfo.InvariantCulture));
        var downs = Enumerable.Range(0, 12).Where(i => Input.GetKeyDown(KeyCode.JoystickButton0 + i)).Select(i => i.ToString(CultureInfo.InvariantCulture));
        var joy1 = Enumerable.Range(0, 12).Where(i => Input.GetKey(KeyCode.Joystick1Button0 + i)).Select(i => i.ToString(CultureInfo.InvariantCulture));
        string Axis(string axis) { try { return F(Input.GetAxisRaw(axis)); } catch (Exception e) { return J(e.GetType().Name); } }
        string Btn(string button) { try { return B(Input.GetButton(button)); } catch (Exception e) { return J(e.GetType().Name); } }
        Record("legacy", $"\"names\":[{string.Join(",", names.Select(J))}],\"keys\":[{string.Join(",", keys)}],\"downs\":[{string.Join(",", downs)}],\"joy1\":[{string.Join(",", joy1)}]," +
            $"\"horizontal\":{Axis("Horizontal")},\"vertical\":{Axis("Vertical")},\"fire1\":{Btn("Fire1")},\"jump\":{Btn("Jump")},\"submit\":{Btn("Submit")},\"cancel\":{Btn("Cancel")}");
        if (m_Frame == End)
        {
            var dir = Environment.GetEnvironmentVariable("GAMEPAD_PROBE_OUT");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "gamepad.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
    }

    void OnDestroy()
    {
        if (m_Fd >= 0)
        {
            ioctl(m_Fd, UI_DEV_DESTROY, 0);
            close(m_Fd);
        }
    }
}
