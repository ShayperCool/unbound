using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            case 110:
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
