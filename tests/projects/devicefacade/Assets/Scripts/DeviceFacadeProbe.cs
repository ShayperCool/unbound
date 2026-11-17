using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>Records, in Play mode without the Device Simulator, how each UnityEngine.Device member
/// answers relative to its UnityEngine counterpart: "same" or "differs" (values that depend on the
/// machine are not written), and controls where state is changed through one class and read
/// through the other.</summary>
public class DeviceFacadeProbe : MonoBehaviour
{
    static readonly StringBuilder records = new StringBuilder();
    static string output;

    static void Record(string key, string value) =>
        records.Append("{\"key\":\"").Append(key).Append("\",\"value\":\"").Append(value).Append("\"}\n");

    static string Describe(Exception e) =>
        "throws " + (e is TargetInvocationException && e.InnerException != null ? e.InnerException.GetType().Name : e.GetType().Name);

    static bool Equal(object a, object b)
    {
        if (a is Array x && b is Array y)
            return x.Length == y.Length && x.Cast<object>().SequenceEqual(y.Cast<object>());
        return Equals(a, b);
    }

    /// <summary>Live measurements: two reads may differ, so both are only checked to be positive.</summary>
    static readonly HashSet<string> live = new HashSet<string> { "SystemInfo.processorFrequency" };

    static void Compare(string key, Func<object> device, Func<object> engine)
    {
        object a = null, b = null;
        string ea = null, eb = null;
        try { a = device(); } catch (Exception e) { ea = Describe(e); }
        try { b = engine(); } catch (Exception e) { eb = Describe(e); }
        if (ea != null || eb != null)
            Record(key, (ea ?? "returns") + " / " + (eb ?? "returns"));
        else
            Record(key, live.Contains(key)
                ? (Convert.ToDouble(a, CultureInfo.InvariantCulture) > 0 && Convert.ToDouble(b, CultureInfo.InvariantCulture) > 0 ? "both positive" : "not both positive")
                : Equal(a, b) ? "same" : "differs");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        output = Environment.GetEnvironmentVariable("DEVICE_FACADE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        new GameObject("DeviceFacadeProbe").AddComponent<DeviceFacadeProbe>();
    }

    void Awake() => StartCoroutine(Run());

    IEnumerator Run()
    {
        for (int i = 0; i < 5; i++) yield return null;
        try
        {
            Properties(typeof(UnityEngine.Device.Application), typeof(Application));
            Properties(typeof(UnityEngine.Device.Screen), typeof(Screen));
            Properties(typeof(UnityEngine.Device.SystemInfo), typeof(SystemInfo));
            Methods();
            Controls();
        }
        catch (Exception e) { Record("failure", Describe(e) + " " + e.Message); }
        Record("complete", "1");
        File.WriteAllText(output, records.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Every public static property of the Device class, by name, against the same-named
    /// property of the UnityEngine class. A member missing on either side is recorded as such.</summary>
    static void Properties(Type device, Type engine)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        foreach (var property in device.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var key = device.Name + "." + property.Name;
            var counterpart = engine.GetProperty(property.Name, flags);
            if (counterpart == null) { Record(key, "no counterpart"); continue; }
            Compare(key, () => property.GetValue(null), () => counterpart.GetValue(null));
            Record(key + ".settable", (property.CanWrite ? "device" : "-") + "/" + (counterpart.CanWrite ? "engine" : "-"));
        }
        foreach (var e in device.GetEvents(flags).OrderBy(e => e.Name, StringComparer.Ordinal))
            Record(device.Name + ".event." + e.Name, engine.GetEvent(e.Name, flags) != null ? "counterpart" : "no counterpart");
    }

    static void Methods()
    {
        Compare("Application.CanStreamedLevelBeLoaded(Probe)", () => UnityEngine.Device.Application.CanStreamedLevelBeLoaded("Probe"), () => Application.CanStreamedLevelBeLoaded("Probe"));
        Compare("Application.CanStreamedLevelBeLoaded(Missing)", () => UnityEngine.Device.Application.CanStreamedLevelBeLoaded("Missing"), () => Application.CanStreamedLevelBeLoaded("Missing"));
        Compare("Application.CanStreamedLevelBeLoaded(0)", () => UnityEngine.Device.Application.CanStreamedLevelBeLoaded(0), () => Application.CanStreamedLevelBeLoaded(0));
        Record("Application.CanStreamedLevelBeLoaded(Probe).value", UnityEngine.Device.Application.CanStreamedLevelBeLoaded("Probe") ? "true" : "false");
        Record("Application.CanStreamedLevelBeLoaded(Missing).value", UnityEngine.Device.Application.CanStreamedLevelBeLoaded("Missing") ? "true" : "false");
        Compare("Application.GetStackTraceLogType(Warning)", () => UnityEngine.Device.Application.GetStackTraceLogType(LogType.Warning), () => Application.GetStackTraceLogType(LogType.Warning));
        Compare("Application.HasProLicense", () => UnityEngine.Device.Application.HasProLicense(), () => Application.HasProLicense());
        var probe = GameObject.Find("DeviceFacadeProbe");
        Compare("Application.IsPlaying(probe)", () => UnityEngine.Device.Application.IsPlaying(probe), () => Application.IsPlaying(probe));
        Compare("Screen.GetDisplayLayout.Count", () => Layout(UnityEngine.Device.Screen.GetDisplayLayout), () => Layout(Screen.GetDisplayLayout));
        Compare("Screen.GetDisplayLayout(null)", () => { UnityEngine.Device.Screen.GetDisplayLayout(null); return 0; }, () => { Screen.GetDisplayLayout(null); return 0; });
        Compare("SystemInfo.SupportsTextureFormat(RGBA32)", () => UnityEngine.Device.SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32), () => SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32));
        Compare("SystemInfo.SupportsRenderTextureFormat(ARGBHalf)", () => UnityEngine.Device.SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf), () => SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf));
        Compare("SystemInfo.SupportsBlendingOnRenderTextureFormat(ARGB32)", () => UnityEngine.Device.SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGB32), () => SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGB32));
        Compare("SystemInfo.SupportsRandomWriteOnRenderTextureFormat(ARGBFloat)", () => UnityEngine.Device.SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat), () => SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat));
        Compare("SystemInfo.IsFormatSupported(R8G8B8A8_SRGB,Render)", () => UnityEngine.Device.SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormatUsage.Render), () => SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormatUsage.Render));
        Compare("SystemInfo.GetCompatibleFormat(R8G8B8_UNorm,Render)", () => UnityEngine.Device.SystemInfo.GetCompatibleFormat(GraphicsFormat.R8G8B8_UNorm, GraphicsFormatUsage.Render), () => SystemInfo.GetCompatibleFormat(GraphicsFormat.R8G8B8_UNorm, GraphicsFormatUsage.Render));
        Compare("SystemInfo.GetGraphicsFormat(LDR)", () => UnityEngine.Device.SystemInfo.GetGraphicsFormat(DefaultFormat.LDR), () => SystemInfo.GetGraphicsFormat(DefaultFormat.LDR));
        var desc = new RenderTextureDescriptor(64, 64, RenderTextureFormat.ARGB32, 24) { msaaSamples = 8 };
        Compare("SystemInfo.GetRenderTextureSupportedMSAASampleCount", () => UnityEngine.Device.SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc), () => SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc));
    }

    static int Layout(Action<List<DisplayInfo>> read)
    {
        var list = new List<DisplayInfo>();
        read(list);
        return list.Count;
    }

    /// <summary>State written through one class and read through the other, from a value that differs
    /// from the current one, so a facade that keeps its own copy would read differently.</summary>
    static void Controls()
    {
        int frameRate = Application.targetFrameRate;
        UnityEngine.Device.Application.targetFrameRate = frameRate == 23 ? 24 : 23;
        Record("control.targetFrameRate.deviceSet.engineRead", Application.targetFrameRate.ToString(CultureInfo.InvariantCulture));
        Application.targetFrameRate = 41;
        Record("control.targetFrameRate.engineSet.deviceRead", UnityEngine.Device.Application.targetFrameRate.ToString(CultureInfo.InvariantCulture));
        Application.targetFrameRate = frameRate;

        bool background = Application.runInBackground;
        UnityEngine.Device.Application.runInBackground = !background;
        Record("control.runInBackground.deviceSet.engineRead", Application.runInBackground == !background ? "changed" : "unchanged");
        Application.runInBackground = background;

        // Only NeverSleep (-1) and SystemSetting (-2) are valid; Unity documents other values as SystemSetting.
        int sleep = Screen.sleepTimeout;
        Record("control.sleepTimeout.initial", sleep.ToString(CultureInfo.InvariantCulture));
        UnityEngine.Device.Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Record("control.sleepTimeout.deviceSetNever.engineRead", Screen.sleepTimeout.ToString(CultureInfo.InvariantCulture));
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        Record("control.sleepTimeout.engineSetSystem.deviceRead", UnityEngine.Device.Screen.sleepTimeout.ToString(CultureInfo.InvariantCulture));
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.sleepTimeout = 19;
        Record("control.sleepTimeout.engineSetSeconds.engineRead", Screen.sleepTimeout.ToString(CultureInfo.InvariantCulture));
        Screen.sleepTimeout = sleep;

        var trace = Application.GetStackTraceLogType(LogType.Warning);
        var other = trace == StackTraceLogType.Full ? StackTraceLogType.None : StackTraceLogType.Full;
        UnityEngine.Device.Application.SetStackTraceLogType(LogType.Warning, other);
        Record("control.stackTraceLogType.deviceSet.engineRead", Application.GetStackTraceLogType(LogType.Warning) == other ? "changed" : "unchanged");
        Application.SetStackTraceLogType(LogType.Warning, trace);

        int seen = 0;
        Application.LogCallback handler = (message, stack, type) => { if (message == "devicefacade-log") seen++; };
        UnityEngine.Device.Application.logMessageReceived += handler;
        Debug.Log("devicefacade-log");
        Record("control.logMessageReceived.deviceAdd", seen.ToString(CultureInfo.InvariantCulture));
        UnityEngine.Device.Application.logMessageReceived -= handler;
        Debug.Log("devicefacade-log");
        Record("control.logMessageReceived.deviceRemove", seen.ToString(CultureInfo.InvariantCulture));
        Application.logMessageReceived += handler;
        UnityEngine.Device.Application.logMessageReceived -= handler;
        Debug.Log("devicefacade-log");
        Record("control.logMessageReceived.engineAdd.deviceRemove", seen.ToString(CultureInfo.InvariantCulture));
        Application.logMessageReceived -= handler;
    }
}
