using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class InputHostConfigControl : MonoBehaviour
{
    readonly List<string> rows = new List<string>();
    readonly List<string> events = new List<string>();
    int frame, configurations;
    string output;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        var path = Environment.GetEnvironmentVariable("INPUT_HOST_CONFIG_OUT");
        if (path != null) new GameObject("Input host configuration").AddComponent<InputHostConfigControl>().output = path;
    }

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        InputSystem.onEvent += (evt, device) => {
            if (device is Keyboard) events.Add(evt.type.ToString());
        };
        InputSystem.onDeviceChange += (device, change) => {
            if (device is Keyboard && change == InputDeviceChange.ConfigurationChanged) configurations++;
        };
    }

    void Put(string name, string value) => rows.Add(name + "=" + value);

    void Read(string step)
    {
        var keyboard = Keyboard.current;
        Put(step + ".layout", keyboard.keyboardLayout);
        Put(step + ".Y", keyboard.yKey.displayName);
        Put(step + ".Z", keyboard.zKey.displayName);
        Put(step + ".events", string.Join(",", events));
        Put(step + ".configurations", configurations.ToString());
        events.Clear();
        configurations = 0;
    }

    static void Run(string program, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false });
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(program + " exited " + process.ExitCode);
    }

    const int CoreKeyboard = 0x100;
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr displayName);
    [DllImport("libX11.so.6")] static extern int XkbLockGroup(IntPtr display, int device, int group);
    [DllImport("libX11.so.6")] static extern int XkbGetState(IntPtr display, int device, IntPtr state);
    [DllImport("libX11.so.6")] static extern int XSync(IntPtr display, int discard);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);

    static void SelectGroup(int group)
    {
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) throw new InvalidOperationException("could not open the probe display");
        try
        {
            if (XkbLockGroup(display, CoreKeyboard, group) == 0)
                throw new InvalidOperationException("could not select the keyboard layout");
            XSync(display, 0);
            // XkbStateRec starts with the active group byte.
            var state = Marshal.AllocHGlobal(32);
            try
            {
                if (XkbGetState(display, CoreKeyboard, state) != 0 || Marshal.ReadByte(state) != group)
                    throw new InvalidOperationException("the requested keyboard layout is inactive");
            }
            finally
            {
                Marshal.FreeHGlobal(state);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

#if UNITY_EDITOR
    int pauseTicks;
    int inputUpdates, queuedGamepadEvents;
    Gamepad syntheticPad;

    void BeginPause()
    {
        syntheticPad = InputSystem.AddDevice<Gamepad>();
        InputSystem.onEvent += (evt, device) => {
            if (device == syntheticPad && evt.type == StateEvent.Type) queuedGamepadEvents++;
        };
        InputSystem.onAfterUpdate += () => {
            if (UnityEditor.EditorApplication.isPaused) inputUpdates++;
        };
        UnityEditor.EditorApplication.update += UpdatePaused;
        UnityEditor.EditorApplication.isPaused = true;
    }

    void UpdatePaused()
    {
        switch (++pauseTicks)
        {
            case 2:
                InputSystem.QueueStateEvent(syntheticPad, new GamepadState().WithButton(GamepadButton.South));
                break;
            case 5:
                Put("beforeMap.updates", inputUpdates.ToString());
                Put("beforeMap.gamepadEvents", queuedGamepadEvents.ToString());
                break;
            case 6: Run("setxkbmap", "-layout de"); break;
            case 15:
                InputSystem.QueueStateEvent(syntheticPad, new GamepadState());
                break;
            case 30:
                Put("afterMap.updates", inputUpdates.ToString());
                Put("afterMap.gamepadEvents", queuedGamepadEvents.ToString());
                Read("whilePaused");
                UnityEditor.EditorApplication.update -= UpdatePaused;
                UnityEditor.EditorApplication.isPaused = false;
                break;
        }
    }
#endif

    void Update()
    {
        switch (++frame)
        {
            case 10: Run("xdotool", "mousemove 600 400 click 1"); break;
            case 20: Read("initial"); break;
            case 30: Run("xdotool", "keydown --delay 0 a keyup --delay 0 a"); break;
            case 45: Read("firstKey"); break;
            case 50: Run("xdotool", "keydown --delay 0 b keyup --delay 0 b"); break;
            case 65: Read("nextKey"); break;
            case 70: Run("setxkbmap", "-layout de"); break;
            case 85: Read("layoutChange"); break;
            case 90: Run("xmodmap", "-e \"keycode 29 = y Y\""); break;
            case 105: Read("mappingChange"); break;
            case 110: Run("setxkbmap", "-layout us,de"); break;
            case 125: Read("multipleGroups"); break;
            case 130: SelectGroup(1); break;
            case 145: Read("groupChanged"); break;
            case 150: SelectGroup(0); break;
            case 165: Read("groupRestored"); break;
#if UNITY_EDITOR
            case 170: BeginPause(); break;
            case 185: Read("afterPause"); break;
#endif
            case 190:
                rows.Add("complete");
                File.WriteAllLines(output, rows);
                if (Environment.GetEnvironmentVariable("INPUT_HOST_CONFIG_UNBOUND") == "1")
                    Application.Quit(0);
#if UNITY_EDITOR
                else
                    UnityEditor.EditorApplication.Exit(0);
#endif
                break;
        }
    }
}
