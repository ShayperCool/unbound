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
/// The Metal plugin interface, measured with the probe's Metal plugin (Plugins/nativegfx_metal.m)
/// on the Metal device (NATIVEGFX_METAL_OUT): the interfaces and device at load, the command
/// buffer, command encoder and render pass descriptor of rendering callbacks (from Update, a
/// camera's command buffer and an executed command buffer), drawing with the current render
/// encoder, ending it and blitting with the current command buffer inside a camera's pass, blitting
/// with a command buffer of the plugin's own from the queue (V2), committing the current one (in
/// a callback and from a script), the
/// textures behind native pointers and render buffers, and an external texture of the plugin's.
/// Pixels are read back.
/// </summary>
public class NativeGfxMetalProbe : MonoBehaviour
{
    const string Plugin = "NativeGfxMetal";
    [DllImport(Plugin)] static extern IntPtr NativeGfxMtl_Event();
    [DllImport(Plugin)] static extern int NativeGfxMtl_EventBase();
    [DllImport(Plugin)] static extern void NativeGfxMtl_Step(string step);
    [DllImport(Plugin)] static extern void NativeGfxMtl_SetColor(float r, float g, float b, float a);
    [DllImport(Plugin)] static extern void NativeGfxMtl_SetTexture(int index, IntPtr pointer);
    [DllImport(Plugin)] static extern void NativeGfxMtl_SetRenderBuffer(int index, IntPtr pointer);
    [DllImport(Plugin)] static extern int NativeGfxMtl_DeviceName(byte[] buffer, int capacity);
    [DllImport(Plugin)] static extern void NativeGfxMtl_Describe(string what, IntPtr pointer);
    [DllImport(Plugin)] static extern void NativeGfxMtl_DescribeRenderBuffer(string what, IntPtr handle);
    [DllImport(Plugin)] static extern IntPtr NativeGfxMtl_MakeTexture();
    [DllImport(Plugin)] static extern void NativeGfxMtl_ScriptCommit();
    [DllImport(Plugin)] static extern void NativeGfxMtl_ScriptPeek();
    [DllImport(Plugin)] static extern int NativeGfxMtl_Record(byte[] buffer, int capacity);

    // The plugin's events after its reserved base.
    const int State = 0, Draw = 1, EndBlit = 2, Commit = 3, QueueBlit = 4, Copy = 5, Mutate = 6, Peek = 7, EndPeek = 8,
        MutateAgain = 9;

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    // The screen copy controls' messages, kept apart from the earlier ones.
    List<string> screenLogs;
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    string output;
    IntPtr callback;
    int eventBase;
    RenderTexture screenSource;
    RenderTexture target, cameraTarget, secondTarget, executed, executedMsaa, executedCopy, mutated, drawn, again;
    Mesh quad;
    Material colored;
    Texture2D texture;
    Camera firstCamera, secondCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("NATIVEGFX_METAL_OUT") == null) return;
        new GameObject("NativeGfxMetalProbe").AddComponent<NativeGfxMetalProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string L(IEnumerable<string> values) => "[" + string.Join(",", values.Select(Q)) + "]";
    void OnLog(string condition, string stack, LogType type) => (screenLogs ?? logs).Add(type + ": " + condition);
    T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }

    void Start()
    {
        output = Environment.GetEnvironmentVariable("NATIVEGFX_METAL_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    /// <summary>A texture's pixels as rows (bottom first) of runs "rrggbbaa*count".</summary>
    string Pixels(Texture source, int width, int height)
    {
        var previous = RenderTexture.active;
        var readback = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, readback);
        var cpu = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        RenderTexture.active = readback;
        cpu.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(readback);
        var pixels = cpu.GetPixels32();
        DestroyImmediate(cpu);
        return Rows(pixels, width, height);
    }

    static string Rows(Color32[] pixels, int width, int height)
    {
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

    static void Clear(RenderTexture texture, Color color)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(true, true, color);
        RenderTexture.active = previous;
    }

    RenderTexture Target(string name, int depth, RenderTextureFormat format = RenderTextureFormat.ARGB32,
        RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear, int samples = 1)
    {
        var texture = Keep(new RenderTexture(8, 8, depth, format, readWrite) { name = name, antiAliasing = samples });
        texture.Create();
        return texture;
    }

    Camera MakeCamera(string name, RenderTexture texture, params int[] events)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.enabled = false;
        camera.targetTexture = texture;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 0;
        var cmd = new CommandBuffer { name = name };
        foreach (var id in events) cmd.IssuePluginEvent(callback, eventBase + id);
        camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, cmd);
        return camera;
    }

    void Setup()
    {
        callback = NativeGfxMtl_Event();
        eventBase = NativeGfxMtl_EventBase();
        Value("device", Q(SystemInfo.graphicsDeviceType.ToString()));
        var name = new byte[256];
        int length = NativeGfxMtl_DeviceName(name, name.Length);
        Value("device.name", B(Encoding.UTF8.GetString(name, 0, Math.Min(length, name.Length - 1)) == SystemInfo.graphicsDeviceName));

        target = Target("target", 0);
        Clear(target, Color.white);
        cameraTarget = Target("camera", 24);
        secondTarget = Target("second", 24);
        executed = Target("executed", 0);
        executedMsaa = Target("executedmsaa", 0, samples: 4);
        executedCopy = Target("executedcopy", 0);
        mutated = Target("mutated", 0);
        drawn = Target("drawn", 0);
        again = Target("again", 0);
        quad = Keep(new Mesh { name = "quad" });
        quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        colored = Keep(new Material(Shader.Find("Hidden/Internal-Colored")) { color = Color.white });
        var multisampled = Target("multisampled", 24, samples: 4);
        var srgbTarget = Target("srgbtarget", 0, readWrite: RenderTextureReadWrite.sRGB);
        var half = Target("half", 0, RenderTextureFormat.ARGBHalf);
        var single = Target("single", 0, RenderTextureFormat.RFloat);
        var depthOnly = Target("depthonly", 24, RenderTextureFormat.Depth);
        texture = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { name = "texture" });
        texture.Apply();
        var srgb = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, false, false) { name = "srgb" });
        srgb.Apply();
        var mipped = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, true, true) { name = "mipped" });
        mipped.Apply();
        var rgb = Keep(new Texture2D(8, 8, TextureFormat.RGB24, false, true) { name = "rgb" });
        rgb.Apply();
        var cube = Keep(new Cubemap(8, TextureFormat.RGBA32, true) { name = "cube" });
        cube.Apply(false);
        var array = Keep(new Texture2DArray(8, 4, 3, TextureFormat.RGBA32, false, true) { name = "array" });
        array.Apply(false);
        var volume = Keep(new Texture3D(4, 4, 2, TextureFormat.RGBA32, false) { name = "volume" });
        volume.Apply(false);

        var pointers = new (string, IntPtr)[]
        {
            ("target", target.GetNativeTexturePtr()),
            ("camera", cameraTarget.GetNativeTexturePtr()),
            ("camera.depth", cameraTarget.GetNativeDepthBufferPtr()),
            ("second", secondTarget.GetNativeTexturePtr()),
            ("second.depth", secondTarget.GetNativeDepthBufferPtr()),
            ("executed", executed.GetNativeTexturePtr()),
            ("multisampled", multisampled.GetNativeTexturePtr()),
            ("multisampled.depth", multisampled.GetNativeDepthBufferPtr()),
            ("srgbtarget", srgbTarget.GetNativeTexturePtr()),
            ("half", half.GetNativeTexturePtr()),
            ("single", single.GetNativeTexturePtr()),
            ("depthonly", depthOnly.GetNativeTexturePtr()),
            ("depthonly.depth", depthOnly.GetNativeDepthBufferPtr()),
            ("texture", texture.GetNativeTexturePtr()),
            ("srgb", srgb.GetNativeTexturePtr()),
            ("mipped", mipped.GetNativeTexturePtr()),
        };
        for (int i = 0; i < pointers.Length; i++)
        {
            NativeGfxMtl_SetTexture(i, pointers[i].Item2);
            NativeGfxMtl_Describe($"t{i}.{pointers[i].Item1}", pointers[i].Item2);
        }
        foreach (var (what, shape) in new (string, Texture)[] { ("rgb", rgb), ("cube", cube), ("array", array), ("volume", volume) })
            NativeGfxMtl_Describe(what, shape.GetNativeTexturePtr());
        var buffers = new (string, IntPtr)[]
        {
            ("camera.color", cameraTarget.colorBuffer.GetNativeRenderBufferPtr()),
            ("camera.depth", cameraTarget.depthBuffer.GetNativeRenderBufferPtr()),
            ("multisampled.color", multisampled.colorBuffer.GetNativeRenderBufferPtr()),
            ("multisampled.depth", multisampled.depthBuffer.GetNativeRenderBufferPtr()),
            ("target.color", target.colorBuffer.GetNativeRenderBufferPtr()),
        };
        for (int i = 0; i < buffers.Length; i++)
        {
            NativeGfxMtl_SetRenderBuffer(i, buffers[i].Item2);
            NativeGfxMtl_DescribeRenderBuffer($"b{i}.{buffers[i].Item1}", buffers[i].Item2);
        }

        firstCamera = MakeCamera("first", cameraTarget, Draw);
        // Ending Unity's encoder inside the camera's pass: is the pass there for the next draw?
        // Without HDR and MSAA the camera draws into its target itself.
        secondCamera = MakeCamera("second", secondTarget, EndBlit, State, Draw);
        secondCamera.allowHDR = false;
        secondCamera.allowMSAA = false;
    }

    void Issue(int id) => GL.IssuePluginEvent(callback, eventBase + id);

    void Frame(int frame)
    {
        switch (frame)
        {
            case 1:
                NativeGfxMtl_Step("update");
                NativeGfxMtl_SetColor(0f, 1f, 0f, 1f);
                Issue(State);
                Issue(Draw);
                break;
            case 2:
                // The work before (the target's clear) is committed first, then the plugin's own
                // command buffer writes into the target.
                NativeGfxMtl_Step("commit");
                Issue(Commit);
                Issue(QueueBlit);
                Issue(State);
                break;
            case 3:
                NativeGfxMtl_Step("camera");
                NativeGfxMtl_SetColor(1f, 0f, 0f, 1f);
                firstCamera.Render();
                NativeGfxMtl_Step("camera2");
                NativeGfxMtl_SetColor(0f, 0f, 1f, 1f);
                secondCamera.Render();
                break;
            case 4:
                // From the script itself: the current command buffer, committed and waited on.
                NativeGfxMtl_Step("script");
                NativeGfxMtl_SetColor(1f, 1f, 0f, 1f);
                NativeGfxMtl_ScriptCommit();
                // A texture updated just before an event: the plugin copies what was uploaded.
                texture.SetPixels32(Enumerable.Repeat(new Color32(255, 0, 128, 255), 64).ToArray());
                texture.Apply();
                NativeGfxMtl_Step("copy");
                Issue(Copy);
                NativeGfxMtl_SetColor(0f, 1f, 1f, 1f);
                using (var cmd = new CommandBuffer { name = "executed" })
                {
                    cmd.SetRenderTarget(executed);
                    cmd.ClearRenderTarget(true, true, Color.yellow);
                    cmd.IssuePluginEvent(callback, eventBase + Draw);
                    NativeGfxMtl_Step("execute");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // A multisampled target: what the plugin draws into its samples is what is
                // sampled after (resolved).
                NativeGfxMtl_SetColor(1f, 0f, 1f, 1f);
                using (var cmd = new CommandBuffer { name = "executed msaa" })
                {
                    cmd.SetRenderTarget(executedMsaa);
                    cmd.ClearRenderTarget(true, true, Color.yellow);
                    cmd.IssuePluginEvent(callback, eventBase + Draw);
                    // Sampled in the same buffer, right after the plugin drew.
                    cmd.Blit(executedMsaa, executedCopy);
                    NativeGfxMtl_Step("execute.msaa");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // A plugin changes the descriptor it is given; the next event's encoder keeps the
                // target's contents.
                using (var cmd = new CommandBuffer { name = "mutated" })
                {
                    cmd.SetRenderTarget(mutated);
                    cmd.ClearRenderTarget(true, true, Color.green);
                    cmd.IssuePluginEvent(callback, eventBase + Mutate);
                    cmd.IssuePluginEvent(callback, eventBase + State);
                    NativeGfxMtl_Step("mutate");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // Something drawn into the target before the event: the descriptor before the
                // encoder, and after the plugin ends it.
                using (var cmd = new CommandBuffer { name = "drawn" })
                {
                    cmd.SetRenderTarget(drawn);
                    cmd.ClearRenderTarget(true, true, Color.blue);
                    cmd.DrawMesh(quad, Matrix4x4.identity, colored, 0, 0);
                    cmd.IssuePluginEvent(callback, eventBase + Peek);
                    cmd.IssuePluginEvent(callback, eventBase + EndPeek);
                    NativeGfxMtl_Step("drawn");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // Events after one that ended the encoder, and after one that drew through it.
                using (var cmd = new CommandBuffer { name = "ended" })
                {
                    cmd.SetRenderTarget(drawn);
                    cmd.ClearRenderTarget(true, true, Color.blue);
                    cmd.DrawMesh(quad, Matrix4x4.identity, colored, 0, 0);
                    cmd.IssuePluginEvent(callback, eventBase + EndPeek);
                    cmd.IssuePluginEvent(callback, eventBase + Peek);
                    NativeGfxMtl_Step("ended");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                using (var cmd = new CommandBuffer { name = "plugin drew" })
                {
                    cmd.SetRenderTarget(drawn);
                    cmd.ClearRenderTarget(true, true, Color.blue);
                    cmd.IssuePluginEvent(callback, eventBase + Draw);
                    cmd.IssuePluginEvent(callback, eventBase + Peek);
                    NativeGfxMtl_Step("plugindrew");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // Only cleared: the same.
                using (var cmd = new CommandBuffer { name = "cleared" })
                {
                    cmd.SetRenderTarget(drawn);
                    cmd.ClearRenderTarget(true, true, Color.blue);
                    cmd.IssuePluginEvent(callback, eventBase + Peek);
                    NativeGfxMtl_Step("cleared");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // A change to the descriptor, the encoder ended and asked for again in one event.
                using (var cmd = new CommandBuffer { name = "again" })
                {
                    cmd.SetRenderTarget(again);
                    cmd.ClearRenderTarget(true, true, Color.green);
                    cmd.IssuePluginEvent(callback, eventBase + MutateAgain);
                    NativeGfxMtl_Step("again");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                using (var cmd = new CommandBuffer { name = "plain" })
                {
                    cmd.IssuePluginEvent(callback, eventBase + State);
                    NativeGfxMtl_Step("execute.plain");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // The screen as the target: the descriptor before the encoder, after the plugin
                // ends it (in the same and the next event), and the encoder asked for after that.
                using (var cmd = new CommandBuffer { name = "screen" })
                {
                    cmd.IssuePluginEvent(callback, eventBase + Peek);
                    cmd.IssuePluginEvent(callback, eventBase + EndPeek);
                    cmd.IssuePluginEvent(callback, eventBase + Peek);
                    cmd.IssuePluginEvent(callback, eventBase + State);
                    NativeGfxMtl_Step("screen");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                // The same from the script, outside callbacks.
                NativeGfxMtl_Step("outside");
                NativeGfxMtl_ScriptPeek();
                // CommitCurrentCommandBuffer inside a render texture's pass: where the next
                // encoder goes.
                using (var cmd = new CommandBuffer { name = "commit rt" })
                {
                    cmd.SetRenderTarget(drawn);
                    cmd.ClearRenderTarget(true, true, Color.blue);
                    cmd.DrawMesh(quad, Matrix4x4.identity, colored, 0, 0);
                    cmd.IssuePluginEvent(callback, eventBase + Commit);
                    cmd.IssuePluginEvent(callback, eventBase + State);
                    NativeGfxMtl_Step("commitrt");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                break;
            case 5:
                ScreenCopies();
                break;
            case 6:
                ScreenCameraCopies();
                break;
        }
    }

    /// <summary>The screen (BGRA8Unorm on Metal) and RGBA8 render textures, copied both ways with
    /// <c>CommandBuffer.CopyTexture</c> from an executed buffer, outside cameras: the screen
    /// cleared to a color whose red and blue differ, its corner copied into a render texture,
    /// another render texture's corner copied into the screen at (2, 2).</summary>
    void ScreenCopies()
    {
        screenLogs = new List<string>();
        var copied = Target("screencopy", 0);
        Clear(copied, Color.black);
        screenSource = Target("screensource", 0);
        Clear(screenSource, new Color(0.25f, 0.5f, 1f, 1f));
        using (var cmd = new CommandBuffer { name = "screen copy" })
        {
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cmd.ClearRenderTarget(true, true, new Color(1f, 0.5f, 0.25f, 0.75f));
            cmd.CopyTexture(BuiltinRenderTextureType.CameraTarget, 0, 0, 0, 0, 8, 8, copied, 0, 0, 0, 0);
            cmd.CopyTexture(screenSource, 0, 0, 0, 0, 4, 4, BuiltinRenderTextureType.CameraTarget, 0, 0, 2, 2);
            Graphics.ExecuteCommandBuffer(cmd);
        }
        Value("screencopy.texture.pixels", Pixels(copied, 8, 8));
        Value("screencopy.logs", L(screenLogs));
        screenLogs = null;
    }

    /// <summary>The same copies inside a camera drawing to the screen (no HDR, no MSAA), from its
    /// command buffer after everything: the target the plugin finds there, the camera target's
    /// corner copied out, another corner copied in at (2, 2), and the camera target drawn into a
    /// render texture after that.</summary>
    void ScreenCameraCopies()
    {
        screenLogs = new List<string>();
        var copied = Target("screencameracopy", 0);
        Clear(copied, Color.black);
        var drawnBack = Target("screencameradrawn", 0);
        Clear(drawnBack, Color.black);
        var camera = new GameObject("screen camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(1f, 0.5f, 0.25f, 0.75f);
        camera.cullingMask = 0;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        var cmd = new CommandBuffer { name = "screen camera copy" };
        cmd.IssuePluginEvent(callback, eventBase + State);
        cmd.CopyTexture(BuiltinRenderTextureType.CameraTarget, 0, 0, 0, 0, 8, 8, copied, 0, 0, 0, 0);
        cmd.CopyTexture(screenSource, 0, 0, 0, 0, 4, 4, BuiltinRenderTextureType.CameraTarget, 0, 0, 2, 2);
        cmd.Blit(BuiltinRenderTextureType.CameraTarget, drawnBack);
        camera.AddCommandBuffer(CameraEvent.AfterEverything, cmd);
        NativeGfxMtl_Step("screencamera");
        camera.Render();
        Value("screencamera.copy.pixels", Pixels(copied, 8, 8));
        Value("screencamera.drawn.pixels", Pixels(drawnBack, 8, 8));
        Value("screencamera.logs", L(screenLogs));
        screenLogs = null;
        cmd.Release();
        DestroyImmediate(camera.gameObject);
    }

    void Finish()
    {
        Value("target.pixels", Pixels(target, 8, 8));
        Value("camera.pixels", Pixels(cameraTarget, 8, 8));
        Value("second.pixels", Pixels(secondTarget, 8, 8));
        Value("executed.pixels", Pixels(executed, 8, 8));
        Value("executedmsaa.pixels", Pixels(executedMsaa, 8, 8));
        Value("executedcopy.pixels", Pixels(executedCopy, 8, 8));
        Value("mutated.pixels", Pixels(mutated, 8, 8));
        Value("again.pixels", Pixels(again, 8, 8));
        NativeGfxMtl_SetColor(0f, 0f, 1f, 1f);
        var pointer = NativeGfxMtl_MakeTexture();
        var external = Texture2D.CreateExternalTexture(4, 4, TextureFormat.RGBA32, false, true, pointer);
        Value("external.pixels", Pixels(external, 4, 4));
        Value("external.pointer", B(external.GetNativeTexturePtr() == pointer));
        foreach (var o in made) if (o != null) DestroyImmediate(o);
    }

    /// <summary>The plugin's record, one key per line: <c>plugin.&lt;what&gt;[.&lt;step&gt;]</c>
    /// (numbered when repeated).</summary>
    void PluginRecord()
    {
        var bytes = new byte[1 << 20];
        int length = NativeGfxMtl_Record(bytes, bytes.Length);
        var lines = Encoding.UTF8.GetString(bytes, 0, Math.Min(length, bytes.Length - 1)).Split('\n').Where(s => s.Length > 0);
        var seen = new Dictionary<string, int>();
        foreach (var line in lines)
        {
            var words = line.Split(' ');
            var key = "plugin." + words[0];
            var step = words.FirstOrDefault(w => w.StartsWith("step="));
            if (step != null) key += "." + step.Substring(5);
            seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 1;
            if (seen[key] > 1) key += "." + seen[key];
            Value(key, Q(line));
        }
    }

    IEnumerator Run()
    {
        yield return null;
        int code = 0;
        try { Setup(); }
        catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; }
        for (int frame = 1; frame <= 6 && code == 0; frame++)
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
        Value("complete", B(code == 0));
        File.WriteAllText(output, records.ToString());
        Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
