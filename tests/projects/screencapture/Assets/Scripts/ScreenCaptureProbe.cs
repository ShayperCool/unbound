using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// ScreenCapture and screen readback in Play mode (a 64x32 game view).
///
/// A camera clears to <c>(k/8, 0.5, 0.75, 0.6)</c>, k the frame number modulo 8, so an image
/// shows which frame it holds; a quad over the left half draws a ramp (red u, green v, blue 0.5,
/// alpha 0.25 + 0.5u). A marker (magenta, alpha 0.8, unconverted) can be drawn over the top
/// right corner with GL at the end of a frame.
///
/// <c>read.*</c>: <c>Texture2D.ReadPixels</c> of the screen into float, byte and linear byte
/// textures, at the end of a frame and in Update. <c>asTexture.*</c>:
/// <c>CaptureScreenshotAsTexture</c> in Update, at the end of a frame, after a marker drawn at the
/// end of the frame, for superSize 0..3 and -1 and each stereo mode. <c>rt.*</c>:
/// <c>CaptureScreenshotIntoRenderTexture</c> into byte (sRGB and linear), float, larger and
/// smaller targets, read right after the call and in the next frame, also called in Update.
/// <c>file.*</c>: <c>CaptureScreenshot</c> to absolute paths, when each file exists (right after
/// the call, the end of the frame, the next frames), the file's signature, PNG header and chunk
/// types, and the decoded image; called in Update with a marker drawn at the end of that frame,
/// at the end of a frame before the marker, with superSize 2, stereo modes, twice in one frame,
/// with a .jpg name, without an extension, and to a relative path (the working directory).
/// Every case records its log messages and exceptions. In Gamma color space the Editor crashes
/// when CaptureScreenshotAsTexture reads the screen in Update: the two calls in Update are
/// skipped there (<c>.skipped</c>).
/// </summary>
public class ScreenCaptureProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    const int W = 64, H = 32;
    static readonly Color Sentinel = new Color(0.125f, 0.25f, 0.375f, 0.5f);

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    bool capturing;
    string directory;
    Camera cam;
    Material marker;
    int frames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("SCREENCAPTURE_PROBE_OUT") == null) return;
        new GameObject("ScreenCaptureProbe").AddComponent<ScreenCaptureProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";

    void OnLog(string condition, string stack, LogType type)
    {
        if (capturing) logs.Add(type + ": " + condition);
    }

    void BeginLogs() { logs.Clear(); capturing = true; }
    void EndLogs(string key)
    {
        capturing = false;
        var s = new StringBuilder("[");
        for (int i = 0; i < logs.Count; i++) s.Append(i > 0 ? "," : "").Append(Q(logs[i]));
        Value(key + ".log", s.Append("]").ToString());
        logs.Clear();
    }

    void Flush() => File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());

    void Start()
    {
        directory = Environment.GetEnvironmentVariable("SCREENCAPTURE_PROBE_OUT");
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += OnLog;
        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 0, -10);
        cam.orthographic = true;
        cam.orthographicSize = 1;
        cam.nearClipPlane = 1;
        cam.farClipPlane = 20;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.aspect = 2;
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-2, -1, 0), new Vector3(-2, 1, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        mesh.RecalculateBounds();
        var ramp = new GameObject("Ramp");
        ramp.AddComponent<MeshFilter>().sharedMesh = mesh;
        ramp.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Probe/Ramp"));
        marker = new Material(Shader.Find("Probe/Marker"));
        StartCoroutine(Run());
    }

    void Update()
    {
        frames++;
        cam.backgroundColor = new Color((frames % 8) / 8f, 0.5f, 0.75f, 0.6f);
    }

    /// <summary>The marker over the top right 8x8 pixels of the screen, drawn with GL.</summary>
    void DrawMarker()
    {
        var saved = RenderTexture.active;
        RenderTexture.active = null;
        GL.PushMatrix();
        GL.LoadOrtho();
        marker.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Vertex3(56f / W, 24f / H, 0); GL.Vertex3(56f / W, 1, 0); GL.Vertex3(1, 1, 0); GL.Vertex3(1, 24f / H, 0);
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = saved;
    }

    void Pixels(string key, Texture2D t)
    {
        var pixels = t.GetPixels();
        using (var w = new BinaryWriter(File.Create(Path.Combine(directory, key + ".rgba"))))
            foreach (var c in pixels) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
        Value(key + ".pixels", pixels.Length.ToString());
    }

    void Describe(string key, Texture2D t)
    {
        if (t == null) { Value(key + ".texture", "null"); return; }
        Value(key + ".texture", "{\"size\":[" + t.width + "," + t.height + "],\"format\":" + Q(t.format.ToString())
            + ",\"graphicsFormat\":" + Q(t.graphicsFormat.ToString()) + ",\"mipmapCount\":" + t.mipmapCount
            + ",\"isReadable\":" + B(t.isReadable) + ",\"isDataSRGB\":" + B(t.isDataSRGB)
            + ",\"filterMode\":" + Q(t.filterMode.ToString()) + ",\"wrapMode\":" + Q(t.wrapMode.ToString())
            + ",\"anisoLevel\":" + t.anisoLevel + ",\"name\":" + Q(t.name) + "}");
        if (!t.isReadable) return;
        // A texture can claim to be readable without CPU data.
        try { Pixels(key, t); }
        catch (Exception e) { Value(key + ".pixelsException", Q(e.GetType().Name + ": " + e.Message)); }
    }

    void Frame(string key) => Value(key + ".frame", (frames % 8).ToString());

    void ReadScreen(string key, TextureFormat format, bool linear)
    {
        RenderTexture.active = null;
        var image = new Texture2D(W, H, format, false, linear);
        BeginLogs();
        image.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        image.Apply();
        EndLogs(key);
        Pixels(key, image);
        DestroyImmediate(image);
    }

    void AsTexture(string key, Func<Texture2D> capture)
    {
        Frame(key);
        BeginLogs();
        Texture2D t = null;
        try { t = capture(); }
        catch (Exception e) { Value(key + ".exception", Q(e.GetType().Name + ": " + e.Message)); }
        EndLogs(key);
        Describe(key, t);
        if (t != null) DestroyImmediate(t);
    }

    void ReadTarget(string key, RenderTexture rt)
    {
        var saved = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        RenderTexture.active = saved;
        Pixels(key, image);
        DestroyImmediate(image);
    }

    static RenderTexture Target(int width, int height, RenderTextureFormat format, RenderTextureReadWrite readWrite)
    {
        var rt = new RenderTexture(width, height, 0, format, readWrite);
        rt.Create();
        var saved = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Sentinel);
        RenderTexture.active = saved;
        return rt;
    }

    void IntoTarget(string key, RenderTexture rt)
    {
        Frame(key);
        BeginLogs();
        try { ScreenCapture.CaptureScreenshotIntoRenderTexture(rt); }
        catch (Exception e) { Value(key + ".exception", Q(e.GetType().Name + ": " + e.Message)); }
        EndLogs(key);
        if (rt != null) ReadTarget(key + ".immediate", rt);
    }

    string Exists(params string[] paths)
    {
        var s = new StringBuilder("[");
        for (int i = 0; i < paths.Length; i++) s.Append(i > 0 ? "," : "").Append(B(File.Exists(paths[i])));
        return s.Append("]").ToString();
    }

    /// <summary>The file's first eight bytes, its PNG header and chunk types, and the image it decodes to.</summary>
    void ReadFile(string key, string path)
    {
        if (!File.Exists(path)) { Value(key + ".file", "null"); return; }
        var bytes = File.ReadAllBytes(path);
        var sig = new StringBuilder();
        for (int i = 0; i < Math.Min(8, bytes.Length); i++) sig.Append(bytes[i].ToString("x2"));
        Value(key + ".signature", Q(sig.ToString()));
        if (bytes.Length >= 33 && sig.ToString() == "89504e470d0a1a0a")
        {
            int U32(int o) => (bytes[o] << 24) | (bytes[o + 1] << 16) | (bytes[o + 2] << 8) | bytes[o + 3];
            Value(key + ".ihdr", "[" + U32(16) + "," + U32(20) + "," + bytes[24] + "," + bytes[25] + "," + bytes[26] + "," + bytes[27] + "," + bytes[28] + "]");
            var chunks = new StringBuilder("[");
            int at = 8, n = 0;
            while (at + 8 <= bytes.Length)
            {
                int length = U32(at);
                string type = Encoding.ASCII.GetString(bytes, at + 4, 4);
                if (n == 0 || type != "IDAT" || !chunks.ToString().EndsWith("\"IDAT\"")) chunks.Append(n++ > 0 ? "," : "").Append(Q(type));
                at += 12 + length;
                if (type == "IEND") break;
            }
            Value(key + ".chunks", chunks.Append("]").ToString());
        }
        var t = new Texture2D(2, 2);
        Value(key + ".decoded", B(t.LoadImage(bytes)));
        Describe(key + ".image", t);
        DestroyImmediate(t);
    }

    IEnumerator Run()
    {
        int code = 0;
        var steps = Steps();
        while (true)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; break; }
            if (!more) break;
            yield return steps.Current;
        }
        Value("complete", B(code == 0));
        Flush();
        Debug.Log("SCREENCAPTURE_DONE " + directory + " " + code);
        Application.logMessageReceived -= OnLog;
        if (finished != null) finished(code);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }

    IEnumerator Steps()
    {
        for (int i = 0; i < 20; i++) yield return null;
        Value("screen", "[" + Screen.width + "," + Screen.height + "]");
        Value("colorSpace", Q(QualitySettings.activeColorSpace.ToString()));

        // Screen readback.
        yield return new WaitForEndOfFrame();
        Frame("read.endOfFrame");
        ReadScreen("read.endOfFrame.float", TextureFormat.RGBAFloat, true);
        ReadScreen("read.endOfFrame.byte", TextureFormat.RGBA32, false);
        ReadScreen("read.endOfFrame.byteLinear", TextureFormat.RGBA32, true);
        yield return null;
        Frame("read.update");
        ReadScreen("read.update.float", TextureFormat.RGBAFloat, true);
        Flush();

        // CaptureScreenshotAsTexture.
        yield return null;
        // In Gamma the Editor crashes when the capture reads the screen outside the frame.
        bool gamma = QualitySettings.activeColorSpace == ColorSpace.Gamma;
        if (gamma) Value("asTexture.update.skipped", "true");
        else AsTexture("asTexture.update", () => ScreenCapture.CaptureScreenshotAsTexture());
        yield return new WaitForEndOfFrame();
        AsTexture("asTexture.endOfFrame", () => ScreenCapture.CaptureScreenshotAsTexture());
        AsTexture("asTexture.endOfFrame.again", () => ScreenCapture.CaptureScreenshotAsTexture());
        yield return null;
        yield return new WaitForEndOfFrame();
        DrawMarker();
        AsTexture("asTexture.marker", () => ScreenCapture.CaptureScreenshotAsTexture());
        foreach (int size in new[] { 0, 1, 2, 3, -1 })
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            AsTexture("asTexture.superSize" + size, () => ScreenCapture.CaptureScreenshotAsTexture(size));
        }
        foreach (ScreenCapture.StereoScreenCaptureMode mode in Enum.GetValues(typeof(ScreenCapture.StereoScreenCaptureMode)))
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            AsTexture("asTexture.stereo" + mode, () => ScreenCapture.CaptureScreenshotAsTexture(mode));
        }
        Flush();

        // CaptureScreenshotIntoRenderTexture.
        var targets = new (string, int, int, RenderTextureFormat, RenderTextureReadWrite)[]
        {
            ("rt.byte", W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB),
            ("rt.byteLinear", W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear),
            ("rt.float", W, H, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            ("rt.larger", 2 * W, 2 * H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB),
            ("rt.smaller", W / 2, H / 2, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB),
        };
        foreach (var (key, width, height, format, readWrite) in targets)
        {
            var rt = Target(width, height, format, readWrite);
            yield return null;
            yield return new WaitForEndOfFrame();
            IntoTarget(key, rt);
            yield return null;
            ReadTarget(key + ".next", rt);
            rt.Release();
            DestroyImmediate(rt);
        }
        if (gamma) Value("rt.update.skipped", "true");
        else
        {
            var rt = Target(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            yield return null;
            IntoTarget("rt.update", rt);
            yield return new WaitForEndOfFrame();
            ReadTarget("rt.update.endOfFrame", rt);
            yield return null;
            ReadTarget("rt.update.next", rt);
            rt.Release();
            DestroyImmediate(rt);
        }
        yield return new WaitForEndOfFrame();
        IntoTarget("rt.null", null);
        Flush();

        // CaptureScreenshot.
        string Path_(string name) => Path.Combine(directory, name);
        {
            string path = Path_("file-update.png");
            yield return null;
            Frame("file.update");
            BeginLogs();
            ScreenCapture.CaptureScreenshot(path);
            EndLogs("file.update");
            var seen = new List<string> { Exists(path) };
            yield return new WaitForEndOfFrame();
            seen.Add(Exists(path));
            DrawMarker();
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                seen.Add(Exists(path));
                yield return new WaitForEndOfFrame();
                seen.Add(Exists(path));
            }
            Value("file.update.exists", "[" + string.Join(",", seen) + "]");
            ReadFile("file.update", path);
        }
        {
            string path = Path_("file-endOfFrame.png");
            yield return null;
            yield return new WaitForEndOfFrame();
            Frame("file.endOfFrame");
            BeginLogs();
            ScreenCapture.CaptureScreenshot(path);
            EndLogs("file.endOfFrame");
            var seen = new List<string> { Exists(path) };
            DrawMarker();
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                seen.Add(Exists(path));
                yield return new WaitForEndOfFrame();
                seen.Add(Exists(path));
            }
            Value("file.endOfFrame.exists", "[" + string.Join(",", seen) + "]");
            ReadFile("file.endOfFrame", path);
        }
        var calls = new List<(string, string[], Action)>
        {
            ("file.superSize2", new[] { "file-superSize2.png" }, () => ScreenCapture.CaptureScreenshot(Path_("file-superSize2.png"), 2)),
            ("file.superSize0", new[] { "file-superSize0.png" }, () => ScreenCapture.CaptureScreenshot(Path_("file-superSize0.png"), 0)),
            ("file.twice", new[] { "file-twiceA.png", "file-twiceB.png" }, () =>
            {
                ScreenCapture.CaptureScreenshot(Path_("file-twiceA.png"));
                ScreenCapture.CaptureScreenshot(Path_("file-twiceB.png"));
            }),
            ("file.jpg", new[] { "file-jpg.jpg" }, () => ScreenCapture.CaptureScreenshot(Path_("file-jpg.jpg"))),
            ("file.noExtension", new[] { "file-noExtension" }, () => ScreenCapture.CaptureScreenshot(Path_("file-noExtension"))),
        };
        foreach (ScreenCapture.StereoScreenCaptureMode mode in Enum.GetValues(typeof(ScreenCapture.StereoScreenCaptureMode)))
        {
            string name = "file-stereo" + mode + ".png";
            var m = mode;
            calls.Add(("file.stereo" + mode, new[] { name }, () => ScreenCapture.CaptureScreenshot(Path_(name), m)));
        }
        foreach (var (key, names, call) in calls)
        {
            yield return null;
            Frame(key);
            BeginLogs();
            try { call(); }
            catch (Exception e) { Value(key + ".exception", Q(e.GetType().Name + ": " + e.Message)); }
            EndLogs(key);
            for (int i = 0; i < 3; i++) yield return null;
            foreach (var name in names) ReadFile(key + "." + name, Path_(name));
        }
        {
            // A relative path: the working directory (the project folder in the Editor).
            const string name = "screencapture-probe-relative.png";
            string path = Path.Combine(Directory.GetCurrentDirectory(), name);
            Value("file.relative.before", B(File.Exists(path)));
            yield return null;
            Frame("file.relative");
            BeginLogs();
            ScreenCapture.CaptureScreenshot(name);
            EndLogs("file.relative");
            for (int i = 0; i < 3; i++) yield return null;
            ReadFile("file.relative", path);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
