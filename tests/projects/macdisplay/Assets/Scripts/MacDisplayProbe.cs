using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// The display queries on macOS: Screen.currentResolution, Screen.resolutions, Screen.dpi,
/// Display.main's sizes, Screen.mainWindowDisplayInfo and Screen.GetDisplayLayout (`unity.*`),
/// and the facts of the host's main display they are taken from, read from CoreGraphics,
/// CoreVideo and AppKit in the same process (`os.*`). Writes values.jsonl in MAC_DISPLAY_OUT on
/// the fifth frame and exits.
/// </summary>
public sealed class MacDisplayProbe : MonoBehaviour
{
    const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string CV = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(CG)] static extern uint CGMainDisplayID();
    [DllImport(CG)] static extern UIntPtr CGDisplayPixelsWide(uint display);
    [DllImport(CG)] static extern UIntPtr CGDisplayPixelsHigh(uint display);
    [DllImport(CG)] static extern IntPtr CGDisplayCopyDisplayMode(uint display);
    [DllImport(CG)] static extern IntPtr CGDisplayCopyAllDisplayModes(uint display, IntPtr options);
    [DllImport(CG)] static extern UIntPtr CGDisplayModeGetPixelWidth(IntPtr mode);
    [DllImport(CG)] static extern UIntPtr CGDisplayModeGetPixelHeight(IntPtr mode);
    [DllImport(CG)] static extern CGSize CGDisplayScreenSize(uint display);
    [DllImport(CF)] static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CF)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CV)] static extern int CVDisplayLinkCreateWithCGDisplay(uint display, out IntPtr link);
    [DllImport(CV)] static extern CVTime CVDisplayLinkGetNominalOutputVideoRefreshPeriod(IntPtr link);
    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern CGRect SendRect(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern double SendDouble(IntPtr receiver, IntPtr selector);
    [StructLayout(LayoutKind.Sequential)] struct CGSize { public double Width, Height; }
    [StructLayout(LayoutKind.Sequential)] struct CGRect { public double X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] struct CVTime { public long Value; public int Scale; public int Flags; }

    readonly StringBuilder m_Out = new StringBuilder();
    int m_Frame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("MacDisplayProbe").AddComponent<MacDisplayProbe>();

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Rate(RefreshRate rate) => rate.numerator + "/" + rate.denominator;
    static string Info(DisplayInfo info) => $"'{info.name}' {info.width}x{info.height} @{Rate(info.refreshRate)} work {info.workArea.x},{info.workArea.y},{info.workArea.width},{info.workArea.height}";

    void Update()
    {
        if (++m_Frame < 5) return;
        try
        {
            Host();
            var current = Screen.currentResolution;
            Rec("unity.currentResolution", $"{current.width}x{current.height} @{Rate(current.refreshRateRatio)}");
            var resolutions = new List<string>();
            foreach (var r in Screen.resolutions) resolutions.Add($"{r.width}x{r.height} @{Rate(r.refreshRateRatio)}");
            Rec("unity.resolutions", string.Join(", ", resolutions));
            Rec("unity.dpi", Screen.dpi.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Rec("unity.display.main", $"system {Display.main.systemWidth}x{Display.main.systemHeight} rendering {Display.main.renderingWidth}x{Display.main.renderingHeight} displays {Display.displays.Length}");
            Rec("unity.mainWindowDisplayInfo", Info(Screen.mainWindowDisplayInfo));
            var layout = new List<DisplayInfo>();
            Screen.GetDisplayLayout(layout);
            var entries = new List<string>();
            foreach (var info in layout) entries.Add(Info(info));
            Rec("unity.displayLayout", string.Join(" | ", entries));
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        }
        catch (Exception e) { Rec("fatal", e.GetType().Name + ": " + e.Message); }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("MAC_DISPLAY_OUT"), "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
        enabled = false;
    }

    /// <summary>The main display's facts: when they match the reference's, the display queries
    /// are compared (run.py).</summary>
    void Host()
    {
        uint display = CGMainDisplayID();
        var mode = CGDisplayCopyDisplayMode(display);
        Rec("os.mode", $"{CGDisplayModeGetPixelWidth(mode)}x{CGDisplayModeGetPixelHeight(mode)} points {CGDisplayPixelsWide(display)}x{CGDisplayPixelsHigh(display)}");
        var sizes = new List<string>();
        var all = CGDisplayCopyAllDisplayModes(display, IntPtr.Zero);
        for (long i = 0; i < CFArrayGetCount(all); i++)
        {
            var m = CFArrayGetValueAtIndex(all, i);
            string size = $"{CGDisplayModeGetPixelWidth(m)}x{CGDisplayModeGetPixelHeight(m)}";
            if (!sizes.Contains(size)) sizes.Add(size);
        }
        Rec("os.modes", string.Join(", ", sizes));
        var millimetres = CGDisplayScreenSize(display);
        Rec("os.millimetres", $"{millimetres.Width:R}x{millimetres.Height:R}");
        if (CVDisplayLinkCreateWithCGDisplay(display, out var link) == 0)
        {
            var period = CVDisplayLinkGetNominalOutputVideoRefreshPeriod(link);
            Rec("os.refreshPeriod", $"{period.Value}/{period.Scale}");
        }
        var screen = Send(objc_getClass("NSScreen"), sel_registerName("mainScreen"));
        var frame = SendRect(screen, sel_registerName("frame"));
        var visible = SendRect(screen, sel_registerName("visibleFrame"));
        Rec("os.screen", $"frame {frame.X},{frame.Y},{frame.Width},{frame.Height} visible {visible.X},{visible.Y},{visible.Width},{visible.Height} scale {SendDouble(screen, sel_registerName("backingScaleFactor"))}");
    }
}
