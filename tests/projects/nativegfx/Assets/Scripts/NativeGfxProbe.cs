using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The low-level native plugin interface, measured with the probe's own plugin
/// (Plugins/nativegfx_probe.c): what UnityPluginLoad gets, rendering callbacks issued from camera
/// command buffers, OnPostRender, Update and executed command buffers (their order against the
/// scripts, thread and bound GL target), texture updates, custom blits, external textures and the
/// native pointers of textures and render buffers. Pixels the plugin draws are read back.
/// </summary>
public class NativeGfxProbe : MonoBehaviour
{
    const string Plugin = "NativeGfxProbe";
    [DllImport(Plugin)] static extern IntPtr NativeGfx_RenderEvent();
    [DllImport(Plugin)] static extern IntPtr NativeGfx_RenderEventAndData();
    [DllImport(Plugin)] static extern IntPtr NativeGfx_TextureUpdate();
    [DllImport(Plugin)] static extern IntPtr NativeGfx_CustomBlit();
    [DllImport(Plugin)] static extern int NativeGfx_EventBase();
    [DllImport(Plugin)] static extern void NativeGfx_SetName(int index, IntPtr name);
    [DllImport(Plugin)] static extern void NativeGfx_SetRenderBuffer(int index, IntPtr buffer);
    [DllImport(Plugin)] static extern IntPtr NativeGfx_Created(int index);
    [DllImport(Plugin)] static extern void NativeGfx_Mark(string text);
    [DllImport(Plugin)] static extern int NativeGfx_Record(byte[] buffer, int capacity);

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    string output;
    int eventBase;
    IntPtr render, renderData, textureUpdate, customBlit, data;
    Camera cam;
    RenderTexture target, blitTarget, readback;
    Texture2D written, updated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("NATIVEGFX_PROBE_OUT") == null) return;
        new GameObject("NativeGfxProbe").AddComponent<NativeGfxProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string L(IEnumerable<string> values) => "[" + string.Join(",", values.Select(Q)) + "]";

    void OnLog(string condition, string stack, LogType type) => logs.Add(type + ": " + condition);

    T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }

    void Start()
    {
        output = Environment.GetEnvironmentVariable("NATIVEGFX_PROBE_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    /// <summary>The texture's pixels as rows (bottom first) of runs "rrggbbaa*count".</summary>
    string Pixels(Texture source, int width, int height)
    {
        var previous = RenderTexture.active;
        if (readback == null || readback.width != width || readback.height != height)
        {
            if (readback != null) { readback.Release(); DestroyImmediate(readback); }
            readback = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            readback.Create();
        }
        Graphics.Blit(source, readback);
        var cpu = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        RenderTexture.active = readback;
        cpu.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        RenderTexture.active = previous;
        var pixels = cpu.GetPixels32();
        DestroyImmediate(cpu);
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            var row = new StringBuilder();
            int x = 0;
            while (x < width)
            {
                var c = pixels[y * width + x];
                int n = 1;
                while (x + n < width && pixels[y * width + x + n].Equals(c)) n++;
                if (row.Length > 0) row.Append(' ');
                row.Append($"{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}*{n}");
                x += n;
            }
            rows.Add(row.ToString());
        }
        return L(rows);
    }

    string RenderTargetPixels(RenderTexture rt)
    {
        // A target read as it is: copied, not blitted (no sampling).
        var copy = new RenderTexture(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        copy.Create();
        Graphics.CopyTexture(rt, 0, 0, copy, 0, 0);
        var result = Pixels(copy, rt.width, rt.height);
        copy.Release();
        DestroyImmediate(copy);
        return result;
    }

    /// <summary>Records what a step logs (<c>.logs</c>), when it logs anything.</summary>
    void Logged(string key, Action body)
    {
        logs.Clear();
        body();
        if (logs.Count > 0) Value(key + ".logs", L(logs));
        logs.Clear();
    }

    void Execute(string name, Action<CommandBuffer> record)
    {
        using var cmd = new CommandBuffer { name = name };
        Logged("record." + name, () => record(cmd));
        NativeGfx_Mark("execute " + name);
        Logged("execute." + name, () => Graphics.ExecuteCommandBuffer(cmd));
        NativeGfx_Mark("executed " + name);
    }

    /// <summary>The plugin's record: its load (<c>plugin.load</c>), the interfaces it found (one
    /// key each) and everything after (<c>plugin.sequence</c>: script marks, callbacks).</summary>
    void PluginRecord()
    {
        var buffer = new byte[1 << 20];
        int length = NativeGfx_Record(buffer, buffer.Length);
        var lines = Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1)).Split('\n').Where(s => s.Length > 0).ToList();
        int done = lines.IndexOf("load done");
        var load = done < 0 ? lines : lines.Take(done + 1).ToList();
        foreach (var line in load.Where(l => l.StartsWith("interface ")))
        {
            var entry = line.Substring("interface ".Length);
            int equals = entry.IndexOf('=');
            Value("interface." + entry.Substring(0, equals), Q(entry.Substring(equals + 1)));
        }
        Value("plugin.load", L(load.Where(l => !l.StartsWith("interface "))));
        Value("plugin.sequence", L(done < 0 ? new List<string>() : lines.Skip(done + 1)));
    }

    IEnumerator Run()
    {
        yield return null;
        int code = 0;
        try { Setup(); }
        catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; }
        for (int frame = 1; frame <= 3 && code == 0; frame++)
        {
            try { Frame(frame); }
            catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; }
            yield return null;
        }
        if (code == 0)
        {
            try { Finish(); }
            catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; }
        }
        PluginRecord();
        if (logs.Count > 0) Value("logs", L(logs));
        NativeGfx_Mark("exit");
        Value("complete", B(code == 0));
        File.WriteAllText(output, records.ToString());
        Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    void Setup()
    {
        // The first call into the plugin loads it (UnityPluginLoad runs inside this call).
        logs.Clear();
        NativeGfx_Mark("start");
        Value("load.logs", L(logs));
        logs.Clear();
        eventBase = NativeGfx_EventBase();
        Value("device.type", Q(SystemInfo.graphicsDeviceType.ToString()));
        Value("device.multithreaded", B(SystemInfo.graphicsMultiThreaded));
        Value("device.threading", Q(SystemInfo.renderingThreadingMode.ToString()));
        Value("device.uvstartsattop", B(SystemInfo.graphicsUVStartsAtTop));
        Value("device.reversedz", B(SystemInfo.usesReversedZBuffer));
        Value("event.base", eventBase.ToString());
        render = NativeGfx_RenderEvent();
        renderData = NativeGfx_RenderEventAndData();
        textureUpdate = NativeGfx_TextureUpdate();
        customBlit = NativeGfx_CustomBlit();
        data = Marshal.AllocHGlobal(4);
        Marshal.WriteInt32(data, 1234);

        target = Keep(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "camera" });
        // A render texture's native pointers before and after it is created.
        Value("ptr.rt.uncreated.texture", B(target.GetNativeTexturePtr() != IntPtr.Zero));
        Value("ptr.rt.uncreated.created", B(target.IsCreated()));
        Value("ptr.rt.uncreated.depth", B(target.GetNativeDepthBufferPtr() != IntPtr.Zero));
        target.Create();
        IntPtr targetName = target.GetNativeTexturePtr();
        Value("ptr.rt.texture", B(targetName != IntPtr.Zero));
        Value("ptr.rt.stable", B(target.GetNativeTexturePtr() == targetName));
        IntPtr depth = target.GetNativeDepthBufferPtr();
        Value("ptr.rt.depth", B(depth != IntPtr.Zero));
        Value("ptr.rt.depth.texture", B(depth == targetName));
        IntPtr color = target.colorBuffer.GetNativeRenderBufferPtr();
        IntPtr depthBuffer = target.depthBuffer.GetNativeRenderBufferPtr();
        Value("ptr.rt.colorbuffer", B(color != IntPtr.Zero));
        Value("ptr.rt.colorbuffer.texture", B(color == targetName));
        Value("ptr.rt.depthbuffer", B(depthBuffer != IntPtr.Zero));
        Value("ptr.rt.depthbuffer.distinct", B(depthBuffer != color));
        Value("ptr.rt.depthbuffer.depthptr", B(depthBuffer == depth));
        Value("ptr.rt.colorbuffer.stable", B(target.colorBuffer.GetNativeRenderBufferPtr() == color));
        var noDepth = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32));
        noDepth.Create();
        Value("ptr.rt.nodepth.depth", B(noDepth.GetNativeDepthBufferPtr() != IntPtr.Zero));
        Value("ptr.rt.nodepth.depthbuffer", B(noDepth.depthBuffer.GetNativeRenderBufferPtr() != IntPtr.Zero));
        NativeGfx_SetName(8, noDepth.GetNativeDepthBufferPtr());

        blitTarget = Keep(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "blit" });
        blitTarget.Create();
        written = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, false, false) { name = "written", filterMode = FilterMode.Point });
        written.SetPixels32(Enumerable.Repeat(new Color32(40, 80, 120, 255), 64).ToArray());
        written.Apply();
        updated = Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "updated", filterMode = FilterMode.Point });
        updated.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 255), 16).ToArray());
        updated.Apply();
        var plain = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "plain" });
        var writtenName = written.GetNativeTexturePtr();
        Value("ptr.tex.texture", B(writtenName != IntPtr.Zero));
        Value("ptr.tex.stable", B(written.GetNativeTexturePtr() == writtenName));
        Value("ptr.tex.distinct", B(writtenName != targetName && writtenName != updated.GetNativeTexturePtr()));
        Value("ptr.tex.unapplied", B(plain.GetNativeTexturePtr() != IntPtr.Zero));
        // Released and created again.
        var cycled = Keep(new RenderTexture(4, 4, 0));
        cycled.Create();
        Value("ptr.rt.released", B(ReleasedPointer(cycled)));

        NativeGfx_SetName(0, targetName);
        NativeGfx_SetName(1, blitTarget.GetNativeTexturePtr());
        NativeGfx_SetName(2, writtenName);
        NativeGfx_SetName(3, updated.GetNativeTexturePtr());
        NativeGfx_SetName(4, depth);
        NativeGfx_SetName(6, plain.GetNativeTexturePtr());
        NativeGfx_SetRenderBuffer(0, blitTarget.colorBuffer.GetNativeRenderBufferPtr());
        NativeGfx_SetRenderBuffer(1, blitTarget.depthBuffer.GetNativeRenderBufferPtr());
        NativeGfx_SetRenderBuffer(2, color);

        var go = new GameObject("NativeGfxCamera");
        go.SetActive(false);
        cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 1, 1);
        cam.cullingMask = 0;
        // Drawn straight into the target: no HDR or MSAA intermediate.
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.targetTexture = target;
        var hooks = go.AddComponent<NativeGfxCameraHooks>();
        hooks.render = render;
        hooks.eventBase = eventBase;
        go.SetActive(true);
        // Two callbacks of one buffer, in their order.
        var opaque = new CommandBuffer { name = "nativegfx.opaque" };
        opaque.IssuePluginEvent(render, eventBase + 0);
        opaque.IssuePluginEventAndData(renderData, eventBase + 1, data);
        cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, opaque);
    }

    bool ReleasedPointer(RenderTexture rt)
    {
        var before = rt.GetNativeTexturePtr();
        rt.Release();
        bool zero = rt.GetNativeTexturePtr() == IntPtr.Zero;
        rt.Create();
        return zero && rt.GetNativeTexturePtr() != IntPtr.Zero && before != IntPtr.Zero;
    }

    void Frame(int frame)
    {
        NativeGfx_Mark("frame " + frame);
        cam.Render();
        NativeGfx_Mark("rendered " + frame);
        if (frame == 1)
        {
            // Outside any camera: GL.IssuePluginEvent from a script, then an executed buffer.
            GL.IssuePluginEvent(render, eventBase + 1);
            NativeGfx_Mark("issued gl");
            Execute("create", cmd => cmd.IssuePluginEvent(render, eventBase + 2));
            Value("written.gpu", Pixels(written, 8, 8));
            Value("written.cpu", Q(ColorUtility.ToHtmlStringRGBA(written.GetPixel(2, 2))));
        }
        else if (frame == 2)
        {
            External();
            TextureUpdates();
            CustomBlit();
            Execute("describe", cmd => cmd.IssuePluginEvent(render, eventBase + 4));
            // A GL error the callback leaves is reported after it.
            Execute("glerror", cmd => cmd.IssuePluginEvent(render, eventBase + 6));
        }
    }

    void External()
    {
        var first = NativeGfx_Created(0);
        Value("external.created", B(first != IntPtr.Zero));
        var external = Keep(Texture2D.CreateExternalTexture(4, 4, TextureFormat.RGBA32, false, true, first));
        external.filterMode = FilterMode.Point;
        Value("external.ptr", B(external.GetNativeTexturePtr() == first));
        Value("external.size", Q(external.width + "x" + external.height + " " + external.format + " " + external.mipmapCount));
        Value("external.pixels", Pixels(external, 4, 4));
        Execute("create2", cmd => cmd.IssuePluginEvent(render, eventBase + 3));
        var second = NativeGfx_Created(1);
        external.UpdateExternalTexture(second);
        Value("external.updated.ptr", B(external.GetNativeTexturePtr() == second));
        Value("external.updated.pixels", Pixels(external, 4, 4));
        // A Unity texture's own pointer: the external texture shows that texture.
        var alias = Keep(Texture2D.CreateExternalTexture(8, 8, TextureFormat.RGBA32, false, false, written.GetNativeTexturePtr()));
        alias.filterMode = FilterMode.Point;
        Value("external.alias.ptr", B(alias.GetNativeTexturePtr() == written.GetNativeTexturePtr()));
        Value("external.alias.pixels", Pixels(alias, 8, 8));
        NativeGfx_SetName(5, external.GetNativeTexturePtr());
        NativeGfx_SetName(7, first);
    }

    void TextureUpdates()
    {
#pragma warning disable 618
        Execute("update", cmd => cmd.IssuePluginCustomTextureUpdate(textureUpdate, updated, 11));
        Value("update.pixels", Pixels(updated, 4, 4));
        Execute("update1", cmd => cmd.IssuePluginCustomTextureUpdateV1(textureUpdate, updated, 12));
        Value("update1.pixels", Pixels(updated, 4, 4));
#pragma warning restore 618
        Execute("update2", cmd => cmd.IssuePluginCustomTextureUpdateV2(textureUpdate, updated, 13));
        Value("update2.pixels", Pixels(updated, 4, 4));
        Value("update.cpu", Q(ColorUtility.ToHtmlStringRGBA(updated.GetPixel(1, 1))));
    }

    void CustomBlit()
    {
        var previous = RenderTexture.active;
        RenderTexture.active = blitTarget;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = previous;
        var source = Keep(new RenderTexture(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "blitsource" });
        source.Create();
        NativeGfx_SetName(9, source.GetNativeTexturePtr());
        Execute("blit", cmd => cmd.IssuePluginCustomBlit(customBlit, 3, source, blitTarget, 77, 5));
        Value("blit.pixels", RenderTargetPixels(blitTarget));
        // A Texture2D source: the blit's source is not a render texture.
        Execute("blit.texture", cmd => cmd.IssuePluginCustomBlit(customBlit, 4, written, blitTarget, 78, 6));
    }

    void Finish()
    {
        Value("camera.pixels", RenderTargetPixels(target));
        Value("written.final", Pixels(written, 8, 8));
        foreach (var o in made) if (o != null) DestroyImmediate(o);
        Marshal.FreeHGlobal(data);
    }
}

/// <summary>The probe camera's script callbacks: they mark the record, and OnPostRender issues a
/// rendering event while the camera's target is still bound.</summary>
public class NativeGfxCameraHooks : MonoBehaviour
{
    [DllImport("NativeGfxProbe")] static extern void NativeGfx_Mark(string text);
    internal IntPtr render;
    internal int eventBase;

    void OnPreRender() => NativeGfx_Mark("OnPreRender");

    void OnPostRender()
    {
        NativeGfx_Mark("OnPostRender");
        GL.IssuePluginEvent(render, eventBase + 5);
        NativeGfx_Mark("OnPostRender issued");
    }
}
