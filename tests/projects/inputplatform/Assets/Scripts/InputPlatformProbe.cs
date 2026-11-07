using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;

public class InputPlatformProbe : MonoBehaviour
{
    public static Action<int> finished;
    readonly StringBuilder output = new StringBuilder();
    string directory;
    static string Q(string s) => s == null ? "null" : "\"" + Escape(s) + "\"";
    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 32 || c > 126) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        return b.ToString();
    }
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector2 v) => "[" + F(v.x) + "," + F(v.y) + "]";
    static string R(Resolution r) => "[" + r.width + "," + r.height + "," + r.refreshRateRatio.numerator + "," + r.refreshRateRatio.denominator + "]";
    static string D(DisplayInfo d) => "[" + d.width + "," + d.height + "," + Q(d.name) + "," + d.refreshRate.numerator + "," + d.refreshRate.denominator + "," + d.workArea.x + "," + d.workArea.y + "," + d.workArea.width + "," + d.workArea.height + "]";
    void Add(string key, string value)
    {
        output.Append("{\"k\":").Append(Q(key)).Append(",\"v\":").Append(value).Append("}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
    }
    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return Q(e.GetType().Name + ": " + e.Message); }
    }
    void Worker(string key, Func<string> f)
    {
        string result = null;
        var t = new Thread(() => result = Try(f));
        t.Start();
        if (!t.Join(3000)) throw new Exception("worker call blocked: " + key);
        Add(key, result);
    }
    Process BeginClipboard(string mode, string text = "")
    {
        var start = new ProcessStartInfo("python3") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true };
        start.Arguments = "\"" + Environment.GetEnvironmentVariable("INPUTPLATFORM_CONTROL") + "\" " + mode;
        var process = Process.Start(start);
        process.StandardInput.Write(text);
        process.StandardInput.Close();
        return process;
    }
    string Clipboard(string mode, string text = "")
    {
        using (var process = BeginClipboard(mode, text))
        {
            if (!process.WaitForExit(5000) || process.ExitCode != 0) throw new Exception("clipboard helper failed: " + mode);
            return process.StandardOutput.ReadToEnd();
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("INPUTPLATFORM_OUT") != null)
            new GameObject("InputPlatformProbe").AddComponent<InputPlatformProbe>();
    }
    void OnEnable() => Application.logMessageReceived += Failed;
    void OnDisable() => Application.logMessageReceived -= Failed;
    void Failed(string message, string stack, LogType type)
    {
        if (type != LogType.Exception || directory == null) return;
        Add("fatal", Q(message));
        if (finished != null) finished(1); else EditorApplication.Exit(1);
    }
    IEnumerator Start()
    {
        directory = Environment.GetEnvironmentVariable("INPUTPLATFORM_OUT");
        yield return null;
        yield return null;
        if (Environment.GetEnvironmentVariable("INPUTPLATFORM_ENGINE") == "1")
        {
            Clipboard("focus", Process.GetCurrentProcess().Id.ToString());
            for (int i = 0; i < 5; i++) yield return null;
            UnityEngine.Debug.Log("inputplatform focus=" + Application.isFocused + " mouseEnabled=" + Mouse.current.enabled);
        }
        File.WriteAllText(Path.Combine(directory, "host-display.txt"), Clipboard("display"));
        Add("currentResolution", R(Screen.currentResolution));
        var modes = new List<string>();
        foreach (var r in Screen.resolutions) modes.Add(R(r));
        Add("resolutions", "[" + string.Join(",", modes) + "]");
        Add("dpi", F(Screen.dpi));
        Add("mainWindowDisplayInfo", D(Screen.mainWindowDisplayInfo));
        var layout = new List<DisplayInfo> { Screen.mainWindowDisplayInfo };
        Screen.GetDisplayLayout(layout);
        modes.Clear(); foreach (var d in layout) modes.Add(D(d));
        Add("displayLayout", "[" + string.Join(",", modes) + "]");
        Add("nullDisplayLayout", Try(() => { Screen.GetDisplayLayout(null); return "true"; }));
        modes.Clear(); foreach (var d in Display.displays) modes.Add("[" + d.systemWidth + "," + d.systemHeight + "," + d.renderingWidth + "," + d.renderingHeight + "," + (d.active ? "true" : "false") + "]");
        Add("displays", "[" + string.Join(",", modes) + "]");
        Add("screen", "[" + Screen.width + "," + Screen.height + "]");
        if (Environment.GetEnvironmentVariable("INPUTPLATFORM_DISPLAY_ONLY") == "1")
        {
            Add("complete", "true");
            if (finished != null) finished(0); else EditorApplication.Exit(0);
            yield break;
        }
        string[] samples = { "", "ascii\nline\r\nend\t", "héllo 世界 😀", new string('x', 1048576), null };
        for (int i = 0; i < samples.Length; i++)
        {
            string text = samples[i];
            Add("clipboard.set." + i, Try(() => { GUIUtility.systemCopyBuffer = text; return "true"; }));
            string got = GUIUtility.systemCopyBuffer;
            Add("clipboard." + i, "[" + (got == text ? "true" : "false") + "," + got.Length + "," + (EditorGUIUtility.systemCopyBuffer == got ? "true" : "false") + "]");
            using (var reader = BeginClipboard("get"))
            {
                var read = reader.StandardOutput.ReadToEndAsync();
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!read.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (!read.IsCompleted || !reader.WaitForExit(1000) || reader.ExitCode != 0) throw new Exception("external clipboard reader failed");
                Add("clipboard.externalRead." + i, read.Result == got ? "true" : "false");
            }
        }
        EditorGUIUtility.systemCopyBuffer = "editor setter";
        Add("clipboard.editorSetter", Q(GUIUtility.systemCopyBuffer));
        Clipboard("set", "external 世界 😀");
        for (int i = 0; i < 5; i++) yield return null;
        Add("clipboard.external", Q(GUIUtility.systemCopyBuffer));
        Clipboard("nontext");
        for (int i = 0; i < 5; i++) yield return null;
        Add("clipboard.nontext", Q(GUIUtility.systemCopyBuffer));
        Clipboard("clear");
        for (int i = 0; i < 5; i++) yield return null;
        Add("clipboard.empty", Q(GUIUtility.systemCopyBuffer));
        Worker("thread.clipboard.get", () => Q(GUIUtility.systemCopyBuffer));
        Worker("thread.clipboard.set", () => { GUIUtility.systemCopyBuffer = "worker"; return "true"; });
        Worker("thread.editorClipboard.get", () => Q(EditorGUIUtility.systemCopyBuffer));
        Worker("thread.editorClipboard.set", () => { EditorGUIUtility.systemCopyBuffer = "worker"; return "true"; });
        Add("ime.defaults", "[" + (int)Input.imeCompositionMode + "," + Q(Input.compositionString) + "," + V(Input.compositionCursorPos) + "," + (Input.imeIsSelected ? "true" : "false") + "]");
        foreach (var mode in new[] { IMECompositionMode.On, IMECompositionMode.Off, IMECompositionMode.Auto })
        {
            Input.imeCompositionMode = mode;
            Input.compositionCursorPos = new Vector2(21, 35);
            yield return null;
            Add("ime." + mode, "[" + (int)Input.imeCompositionMode + "," + Q(Input.compositionString) + "," + V(Input.compositionCursorPos) + "," + (Input.imeIsSelected ? "true" : "false") + "]");
        }
        Add("cursor.legacyWarp", "[" + (typeof(Cursor).GetMethod("WarpCursorPosition") != null ? "true" : "false") + "," + (typeof(Input).GetMethod("WarpMousePosition") != null ? "true" : "false") + "]");
        foreach (var target in new[] { new Vector2(120, 80), new Vector2(240, 140) })
        {
            Mouse.current.WarpCursorPosition(target);
            Add("warp." + target.x + ".immediate", V(Input.mousePosition));
            for (int i = 0; i < 10; i++) yield return null;
            Add("warp." + target.x + ".legacy", V(Input.mousePosition));
            Add("warp." + target.x + ".mouse", V(Mouse.current.position.ReadValue()));
        }
        Add("complete", "true");
        if (finished != null) finished(0); else EditorApplication.Exit(0);
    }
}
