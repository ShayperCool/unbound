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
/// The Vulkan plugin interface, measured with the probe's Vulkan plugin
/// (Plugins/nativegfx_vulkan.c) on the Vulkan device (NATIVEGFX_VULKAN_OUT): the instance, the
/// recording state of rendering callbacks under each render pass precondition (from Update, a
/// camera's command buffer and an executed command buffer), and the images and buffers behind
/// native pointers, which the plugin clears and fills. Pixels and buffer contents are read back.
/// Last, the images behind the pointers of a Cubemap, a Texture2DArray, a Texture3D and a
/// CubemapArray (plugin.shape.*: a 2D image of 6, 3 and 12 layers, a 3D image).
/// </summary>
public partial class NativeGfxVulkanProbe : MonoBehaviour
{
    const string Plugin = "NativeGfxVulkan";
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_Event();
    [DllImport(Plugin)] static extern int NativeGfxVk_EventBase();
    [DllImport(Plugin)] static extern void NativeGfxVk_SetTexture(int index, IntPtr pointer);
    [DllImport(Plugin)] static extern void NativeGfxVk_SetRenderBuffer(int index, IntPtr pointer);
    [DllImport(Plugin)] static extern void NativeGfxVk_SetBuffer(IntPtr pointer);
    [DllImport(Plugin)] static extern void NativeGfxVk_Step(string step);
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_ShapeEvent();
    [DllImport(Plugin)] static extern void NativeGfxVk_SetShape(int index, IntPtr pointer);
    [DllImport(Plugin)] static extern int NativeGfxVk_Record(byte[] buffer, int capacity);

    // The plugin's events after its reserved base (render pass precondition).
    const int DontCare = 0, Outside = 1, Inside = 2, OutsideState = 3, AccessInside = 4, LeaveInside = 5;

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    string output;
    IntPtr callback;
    int eventBase;
    Texture2D texture, mipped, srgbTexture;
    RenderTexture renderTexture, multisampled, inner, outer, srgbTarget, cameraTarget;
    ComputeBuffer buffer;
    Camera probeCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("NATIVEGFX_VULKAN_OUT") == null) return;
        new GameObject("NativeGfxVulkanProbe").AddComponent<NativeGfxVulkanProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string L(IEnumerable<string> values) => "[" + string.Join(",", values.Select(Q)) + "]";
    void OnLog(string condition, string stack, LogType type) => logs.Add(type + ": " + condition);
    T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }

    void Start()
    {
        output = Environment.GetEnvironmentVariable("NATIVEGFX_VULKAN_OUT");
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

    void Issue(string step, int id)
    {
        NativeGfxVk_Step(step);
        GL.IssuePluginEvent(callback, eventBase + id);
    }

    void Setup()
    {
        callback = NativeGfxVk_Event();
        eventBase = NativeGfxVk_EventBase();
        Value("device", Q(SystemInfo.graphicsDeviceType.ToString()));
        texture = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { name = "texture", filterMode = FilterMode.Point });
        texture.SetPixels32(Enumerable.Repeat(new Color32(0, 255, 0, 255), 64).ToArray());
        texture.Apply();
        renderTexture = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "rendertexture" });
        renderTexture.Create();
        var previous = RenderTexture.active;
        RenderTexture.active = renderTexture;
        GL.Clear(true, true, Color.white);
        RenderTexture.active = previous;
        buffer = new ComputeBuffer(4, 4);
        buffer.SetData(new uint[4]);
        mipped = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, true, true) { name = "mipped", filterMode = FilterMode.Point });
        mipped.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 255), 64).ToArray());
        mipped.Apply(true);
        multisampled = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "multisampled", antiAliasing = 2 });
        multisampled.Create();
        inner = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "inner" });
        inner.Create();
        outer = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "outer" });
        outer.Create();
        foreach (var cleared in new[] { inner, outer })
        {
            RenderTexture.active = cleared;
            GL.Clear(true, true, Color.black);
        }
        RenderTexture.active = previous;
        srgbTexture = Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false, false) { name = "srgb" });
        srgbTexture.Apply();
        srgbTarget = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { name = "srgbtarget" });
        srgbTarget.Create();
        NativeGfxVk_SetTexture(0, texture.GetNativeTexturePtr());
        NativeGfxVk_SetTexture(1, renderTexture.GetNativeTexturePtr());
        NativeGfxVk_SetTexture(2, mipped.GetNativeTexturePtr());
        NativeGfxVk_SetTexture(3, inner.GetNativeTexturePtr());
        NativeGfxVk_SetTexture(4, outer.GetNativeTexturePtr());
        NativeGfxVk_SetTexture(5, srgbTexture.GetNativeTexturePtr());
        NativeGfxVk_SetTexture(6, srgbTarget.GetNativeTexturePtr());
        NativeGfxVk_SetRenderBuffer(0, renderTexture.colorBuffer.GetNativeRenderBufferPtr());
        NativeGfxVk_SetRenderBuffer(1, multisampled.colorBuffer.GetNativeRenderBufferPtr());
        NativeGfxVk_SetBuffer(buffer.GetNativeBufferPtr());

        cameraTarget = Keep(new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "camera" });
        cameraTarget.Create();
        var go = new GameObject("NativeGfxVulkanCamera");
        probeCamera = go.AddComponent<Camera>();
        probeCamera.enabled = false;
        probeCamera.targetTexture = cameraTarget;
        probeCamera.clearFlags = CameraClearFlags.SolidColor;
        probeCamera.backgroundColor = Color.black;
        probeCamera.cullingMask = 0;
        var cmd = new CommandBuffer { name = "nativegfx vulkan" };
        cmd.IssuePluginEvent(callback, eventBase + Inside);
        cmd.IssuePluginEvent(callback, eventBase + DontCare);
        cmd.IssuePluginEvent(callback, eventBase + AccessInside);
        cmd.IssuePluginEvent(callback, eventBase + LeaveInside);
        // An event that ends the pass, then one that does not care: is the pass resumed?
        cmd.IssuePluginEvent(callback, eventBase + OutsideState);
        cmd.IssuePluginEvent(callback, eventBase + DontCare);
        probeCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, cmd);
    }

    void Frame(int frame)
    {
        switch (frame)
        {
            case 1:
                Issue("update", Outside);
                Issue("update", DontCare);
                break;
            case 2:
                Issue("frame2", OutsideState);
                break;
            case 3:
                Issue("frame3", OutsideState);
                break;
            case 4:
                NativeGfxVk_Step("camera");
                probeCamera.Render();
                using (var cmd = new CommandBuffer { name = "executed" })
                {
                    cmd.IssuePluginEvent(callback, eventBase + OutsideState);
                    cmd.IssuePluginEvent(callback, eventBase + DontCare);
                    NativeGfxVk_Step("execute");
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                break;
            case 5:
                // The camera again: its render pass is the same object.
                NativeGfxVk_Step("camera2");
                probeCamera.Render();
                Shapes();
                break;
        }
    }

    /// <summary>The images of textures of the other shapes (<c>plugin.shape.*</c>), after
    /// everything else.</summary>
    void Shapes()
    {
        var cube = Keep(new Cubemap(8, TextureFormat.RGBA32, true) { name = "cube" });
        cube.Apply(false);
        var array = Keep(new Texture2DArray(8, 4, 3, TextureFormat.RGBA32, false, true) { name = "array" });
        array.Apply(false);
        var volume = Keep(new Texture3D(4, 4, 2, TextureFormat.RGBA32, false) { name = "volume" });
        volume.Apply(false);
        var cubeArray = Keep(new CubemapArray(4, 2, TextureFormat.RGBA32, false) { name = "cubearray" });
        cubeArray.Apply(false);
        NativeGfxVk_SetShape(0, cube.GetNativeTexturePtr());
        NativeGfxVk_SetShape(1, array.GetNativeTexturePtr());
        NativeGfxVk_SetShape(2, volume.GetNativeTexturePtr());
        NativeGfxVk_SetShape(3, cubeArray.GetNativeTexturePtr());
        NativeGfxVk_Step("shapes");
        GL.IssuePluginEvent(NativeGfxVk_ShapeEvent(), 0);
    }

    void Finish()
    {
        Value("texture.pixels", Pixels(texture, 8, 8));
        Value("rendertexture.pixels", Pixels(renderTexture, 8, 8));
        Value("camera.pixels", Pixels(cameraTarget, 8, 8));
        Value("mips.pixels", Pixels(mipped, 8, 8));
        var mip1 = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "mip1" });
        mip1.Create();
        Graphics.CopyTexture(mipped, 0, 1, mip1, 0, 0);
        Value("mips.mip1.pixels", Pixels(mip1, 4, 4));
        Value("inner.pixels", Pixels(inner, 8, 8));
        Value("outer.pixels", Pixels(outer, 8, 8));
        var words = new uint[4];
        buffer.GetData(words);
        Value("buffer.words", "[" + string.Join(",", words.Select(w => "\"" + w.ToString("x8") + "\"")) + "]");
        buffer.Release();
        foreach (var o in made) if (o != null) DestroyImmediate(o);
    }

    /// <summary>The plugin's record, one key per line: <c>plugin.&lt;what&gt;[.&lt;step&gt;]</c>
    /// (numbered when repeated).</summary>
    void PluginRecord()
    {
        var bytes = new byte[1 << 20];
        int length = NativeGfxVk_Record(bytes, bytes.Length);
        var lines = Encoding.UTF8.GetString(bytes, 0, Math.Min(length, bytes.Length - 1)).Split('\n').Where(s => s.Length > 0);
        var seen = new Dictionary<string, int>();
        foreach (var line in lines)
        {
            var words = line.Split(' ');
            var key = "plugin." + words[0].Split('=')[0];
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
        for (int frame = 1; frame <= 5 && code == 0; frame++)
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
        DeviceControls();
        MsaaControls.Run(Value);
        ExternalControls.Run(Value, action =>
        {
            Application.logMessageReceived -= OnLog;
            try { action(); }
            finally { Application.logMessageReceived += OnLog; }
        });
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
