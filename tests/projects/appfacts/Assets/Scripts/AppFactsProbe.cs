using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

/// <summary>
/// Application, Debug, Profiler and SystemInfo facts in Play mode.
///
/// <c>values.jsonl</c> holds what must be the same on every run and machine: scene-loading
/// state, stack traces written by <c>Debug.ExtractStackTraceNoAlloc</c>, the runtime memory size
/// of objects the game creates (textures, meshes, audio clips, render textures, materials,
/// components), the graphics driver figure's changes, the temp allocator and what
/// <c>Application.OpenURL</c> runs. <c>machine.jsonl</c> holds what the machine decides (the
/// device's identity, log path, licence, startup logs), checked against what the OS reports.
///
/// OpenURL runs only when the first <c>xdg-open</c>/<c>open</c> on PATH is the recording stub in
/// <c>APPFACTS_OPENURL_STUBS</c>, so no browser is ever started.
/// </summary>
public class AppFactsProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    readonly StringBuilder values = new StringBuilder();
    readonly StringBuilder machine = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<string> loading = new List<string>();
    string directory, project;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("APPFACTS_PROBE_OUT") == null) return;
        var go = new GameObject("AppFactsProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<AppFactsProbe>();
    }

    string Norm(string s)
    {
        if (s == null) return null;
        if (!string.IsNullOrEmpty(project)) s = s.Replace(project, "<project>");
        return s;
    }

    static string Q(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c == '\r') b.Append("\\r");
            else if (c == '\t') b.Append("\\t");
            else if (c < 32 || c > 126) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string B(bool b) => b ? "true" : "false";
    static string N(long n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static string A(IEnumerable<string> items) => "[" + string.Join(",", items.Select(Q)) + "]";

    void Value(string k, string json) => values.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(json).Append("}\n");
    void Machine(string k, string json) => machine.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(json).Append("}\n");

    void OnLog(string condition, string stack, LogType type) => logs.Add(type + ": " + Norm(condition));

    /// <summary>The logs since the last call, recorded under the case's key.</summary>
    void Logs(string k)
    {
        Value(k + ".logs", A(logs));
        logs.Clear();
    }

    void Try(string k, Action action)
    {
        try { action(); }
        catch (Exception e) { Value(k + ".threw", Q(e.GetType().FullName + ": " + Norm(e.Message))); }
    }

    IEnumerator Start()
    {
        directory = Environment.GetEnvironmentVariable("APPFACTS_PROBE_OUT");
        project = Path.GetDirectoryName(Application.dataPath);
        Application.logMessageReceived += OnLog;
        int code = 0;
        var steps = new Func<IEnumerator>[] { Loading, Stacks, Sizes, DriverMemory, OpenUrls, MachineFacts };
        foreach (var step in steps)
        {
            var e = step();
            while (true)
            {
                bool more;
                try { more = e.MoveNext(); }
                catch (Exception ex) { Value("fatal", Q(ex.ToString())); code = 1; break; }
                if (!more) break;
                yield return e.Current;
            }
            Flush();
            if (code != 0) break;
        }
        Logs("tail");
        Value("complete", B(code == 0));
        Flush();
        Debug.Log("APPFACTS_DONE " + code);
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

    void Flush()
    {
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), values.ToString());
        File.WriteAllText(Path.Combine(directory, "machine.jsonl"), machine.ToString());
    }

    // ------------------------------------------------------------------ scene loading

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) =>
        loading.Add("sceneLoaded " + scene.name + " " + mode + " isLoadingLevel=" + B(Application.isLoadingLevel));

    void OnSceneUnloaded(Scene scene) =>
        loading.Add("sceneUnloaded " + scene.name + " isLoadingLevel=" + B(Application.isLoadingLevel));

    /// <summary>isLoadingLevel before, during and after asynchronous, held and synchronous loads.
    /// Per-frame answers are collapsed into runs, so the number of loading frames does not count.</summary>
    IEnumerator Loading()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        Value("loading.start", B(Application.isLoadingLevel));

        var op = SceneManager.LoadSceneAsync("Second", LoadSceneMode.Additive);
        loading.Add("async call isLoadingLevel=" + B(Application.isLoadingLevel));
        yield return Frames(op, "async");
        loading.Add("async done isLoadingLevel=" + B(Application.isLoadingLevel));
        Value("loading.async", A(Collapse(loading)));
        loading.Clear();

        var unload = SceneManager.UnloadSceneAsync("Second");
        loading.Add("unload call isLoadingLevel=" + B(Application.isLoadingLevel));
        yield return Frames(unload, "unload");
        Value("loading.unload", A(Collapse(loading)));
        loading.Clear();

        op = SceneManager.LoadSceneAsync("Second", LoadSceneMode.Additive);
        op.allowSceneActivation = false;
        loading.Add("held call isLoadingLevel=" + B(Application.isLoadingLevel));
        // How many frames reach 0.9 depends on the disk: only the answers at 0.9 are recorded.
        for (int i = 0; i < 600 && op.progress < 0.9f; i++) yield return null;
        for (int i = 0; i < 3; i++)
        {
            loading.Add("held 0.9 isLoadingLevel=" + B(Application.isLoadingLevel) + " isDone=" + B(op.isDone));
            yield return null;
        }
        op.allowSceneActivation = true;
        loading.Add("allowed isLoadingLevel=" + B(Application.isLoadingLevel));
        yield return Frames(op, "held");
        Value("loading.held", A(Collapse(loading)));
        loading.Clear();
        yield return Frames(SceneManager.UnloadSceneAsync("Second"), "unload2");
        loading.Clear();

        SceneManager.LoadScene("Second", LoadSceneMode.Additive);
        loading.Add("sync call isLoadingLevel=" + B(Application.isLoadingLevel));
        yield return null;
        loading.Add("sync next frame isLoadingLevel=" + B(Application.isLoadingLevel));
        yield return null;
        loading.Add("sync second frame isLoadingLevel=" + B(Application.isLoadingLevel));
        Value("loading.sync", A(Collapse(loading)));
        loading.Clear();
        yield return Frames(SceneManager.UnloadSceneAsync("Second"), "unload3");
        loading.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Logs("loading");
    }

    IEnumerator Frames(AsyncOperation op, string name)
    {
        for (int i = 0; i < 600 && !op.isDone; i++)
        {
            loading.Add(name + " frame isLoadingLevel=" + B(Application.isLoadingLevel));
            yield return null;
        }
        loading.Add(name + " isDone=" + B(op.isDone) + " isLoadingLevel=" + B(Application.isLoadingLevel));
    }

    static List<string> Collapse(List<string> items)
    {
        var result = new List<string>();
        foreach (var item in items)
            if (result.Count == 0 || result[result.Count - 1] != item) result.Add(item);
        return result;
    }

    // ------------------------------------------------------------------ stack traces

    bool stackPending;

    /// <summary>Measured from Update, whose stack is the script's frame alone.</summary>
    IEnumerator Stacks()
    {
        stackPending = true;
        while (stackPending) yield return null;
        Logs("stack");
    }

    void Update()
    {
        if (!stackPending) return;
        stackPending = false;
        StackLevel2();
        Value("stack.utility", Q(Norm(StackTraceUtility.ExtractStackTrace())));
    }

    void StackLevel2()
    {
        foreach (int size in new[] { 4096, 64, 1 })
            foreach (var folder in new[] { "", project + "/", "Assets/" })
            {
                string k = "stack.noalloc." + size + "." + (folder == "" ? "empty" : folder == "Assets/" ? "assets" : "project");
                try { NoAlloc(k, size, folder); }
                catch (Exception e) { Value(k + ".threw", Q(e.GetType().FullName + ": " + Norm(e.Message))); }
            }
    }

    unsafe void NoAlloc(string k, int size, string folder)
    {
        // Padded far past bufferMax: what is written beyond it is measured, not a heap overrun.
        var buffer = new byte[size + 65536];
        for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xAA;
        int n;
        fixed (byte* p = buffer) n = Debug.ExtractStackTraceNoAlloc(p, size, folder);
        int written = buffer.Length;
        while (written > 0 && buffer[written - 1] == 0xAA) written--;
        Value(k + ".return", N(n));
        Value(k + ".written", N(written));
        int text = 0;
        while (text < written && buffer[text] != 0) text++;
        Value(k + ".text", Q(Norm(Encoding.UTF8.GetString(buffer, 0, text))));
    }

    // ------------------------------------------------------------------ runtime memory sizes

    void Size(string k, UnityEngine.Object o) => Try("size." + k, () => Value("size." + k, N(Profiler.GetRuntimeMemorySizeLong(o))));

    static readonly (string name, TextureFormat format)[] TextureFormats =
    {
        ("RGBA32", TextureFormat.RGBA32), ("RGB24", TextureFormat.RGB24), ("Alpha8", TextureFormat.Alpha8),
        ("R16", TextureFormat.R16), ("RGB565", TextureFormat.RGB565), ("RGBAHalf", TextureFormat.RGBAHalf),
        ("RGBAFloat", TextureFormat.RGBAFloat), ("DXT1", TextureFormat.DXT1), ("DXT5", TextureFormat.DXT5),
        ("BC7", TextureFormat.BC7), ("ETC2_RGBA8", TextureFormat.ETC2_RGBA8), ("ASTC_6x6", TextureFormat.ASTC_6x6),
    };

    static readonly (int w, int h)[] TextureSizes = { (1, 1), (4, 4), (64, 64), (100, 60), (256, 128) };

    IEnumerator Sizes()
    {
        Size("null", null);
        foreach (var (name, format) in TextureFormats)
            foreach (var (w, h) in TextureSizes)
                foreach (bool mips in new[] { false, true })
                {
                    string k = "texture2d." + name + "." + w + "x" + h + (mips ? ".mips" : "");
                    Try(k, () =>
                    {
                        var t = new Texture2D(w, h, format, mips);
                        Size(k + ".new", t);
                        t.Apply(false, false);
                        Size(k + ".applied", t);
                        if (w == 64)
                        {
                            t.Apply(false, true);
                            Size(k + ".unreadable", t);
                        }
                        Destroy(t);
                    });
                }
        Logs("size.texture2d");
        yield return null;

        Try("texture2d.linear", () =>
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            Size("texture2d.linear", t);
            t.Reinitialize(128, 32);
            Size("texture2d.reinitialized", t);
            Destroy(t);
        });
        Try("texture2darray", () => { var t = new Texture2DArray(64, 64, 4, TextureFormat.RGBA32, false); Size("texture2darray", t); t.Apply(); Size("texture2darray.applied", t); Destroy(t); });
        Try("texture3d", () => { var t = new Texture3D(16, 16, 16, TextureFormat.RGBA32, false); Size("texture3d", t); t.Apply(); Size("texture3d.applied", t); Destroy(t); });
        Try("cubemap", () => { var t = new Cubemap(64, TextureFormat.RGBA32, false); Size("cubemap", t); t.Apply(); Size("cubemap.applied", t); Destroy(t); });
        Try("cubemap.mips", () => { var t = new Cubemap(64, TextureFormat.RGBA32, true); Size("cubemap.mips", t); Destroy(t); });

        foreach (var (k, w, h, depth, format, msaa, mips) in new[]
        {
            ("rt.64x64.d0", 64, 64, 0, RenderTextureFormat.ARGB32, 1, false),
            ("rt.64x64.d16", 64, 64, 16, RenderTextureFormat.ARGB32, 1, false),
            ("rt.64x64.d24", 64, 64, 24, RenderTextureFormat.ARGB32, 1, false),
            ("rt.64x64.d32", 64, 64, 32, RenderTextureFormat.ARGB32, 1, false),
            ("rt.256x128.d24.float", 256, 128, 24, RenderTextureFormat.ARGBFloat, 1, false),
            ("rt.64x64.d24.half", 64, 64, 24, RenderTextureFormat.ARGBHalf, 1, false),
            ("rt.64x64.d24.msaa4", 64, 64, 24, RenderTextureFormat.ARGB32, 4, false),
            ("rt.64x64.d0.mips", 64, 64, 0, RenderTextureFormat.ARGB32, 1, true),
            ("rt.64x64.depth", 64, 64, 24, RenderTextureFormat.Depth, 1, false),
        })
            Try(k, () =>
            {
                var rt = new RenderTexture(w, h, depth, format) { antiAliasing = msaa, useMipMap = mips };
                Size(k + ".new", rt);
                rt.Create();
                Size(k + ".created", rt);
                rt.Release();
                Size(k + ".released", rt);
                Destroy(rt);
            });
        Logs("size.rt");
        yield return null;

        foreach (int n in new[] { 3, 4, 100, 1000, 70000 })
            foreach (string attributes in new[] { "p", "pn", "pnt", "pntc", "pntcg", "pt2" })
            {
                string k = "mesh." + n + "." + attributes;
                Try(k, () => MeshCase(k, n, attributes));
            }
        Try("mesh.empty", () => { var m = new Mesh(); Size("mesh.empty", m); Destroy(m); });
        Try("mesh.submeshes", () =>
        {
            var m = new Mesh();
            m.vertices = Positions(100);
            m.subMeshCount = 2;
            m.SetTriangles(Triangles(50), 0);
            m.SetTriangles(Triangles(50), 1);
            Size("mesh.submeshes", m);
            Destroy(m);
        });
        Logs("size.mesh");
        yield return null;

        foreach (var (k, length, channels, frequency, stream) in new[]
        {
            ("clip.1.1.44100", 1, 1, 44100, false),
            ("clip.10.1.44100", 10, 1, 44100, false),
            ("clip.22.1.44100", 22, 1, 44100, false),
            ("clip.23.1.44100", 23, 1, 44100, false),
            ("clip.100.1.44100", 100, 1, 44100, false),
            ("clip.1.2.44100", 1, 2, 44100, false),
            ("clip.100.2.44100", 100, 2, 44100, false),
            ("clip.100.4.44100", 100, 4, 44100, false),
            ("clip.100.6.44100", 100, 6, 44100, false),
            ("clip.1000.1.44100.stream", 1000, 1, 44100, true),
            ("clip.100000.1.8000.stream", 100000, 1, 8000, true),
            ("clip.44100.4.44100.stream", 44100, 4, 44100, true),
            ("clip.1000.1.8000", 1000, 1, 8000, false),
            ("clip.44100.1.44100", 44100, 1, 44100, false),
            ("clip.44100.2.44100", 44100, 2, 44100, false),
            ("clip.22050.2.22050", 22050, 2, 22050, false),
            ("clip.441000.2.44100", 441000, 2, 44100, false),
            ("clip.44100.1.44100.stream", 44100, 1, 44100, true),
            ("clip.44100.2.48000.stream", 44100, 2, 48000, true),
        })
            Try(k, () =>
            {
                var clip = AudioClip.Create("probe", length, channels, frequency, stream);
                Size(k, clip);
                if (!stream)
                {
                    clip.SetData(new float[Math.Min(length * channels, 1024)], 0);
                    Size(k + ".data", clip);
                }
                Destroy(clip);
            });
        Logs("size.clip");
        yield return null;

        var go = new GameObject("SizeProbe");
        Size("gameobject", go);
        Size("gameobject.transform", go.transform);
        var named = new GameObject("A much longer game object name for the size probe");
        Size("gameobject.longname", named);
        Size("gameobject.boxcollider", go.AddComponent<BoxCollider>());
        Size("gameobject.meshfilter", go.AddComponent<MeshFilter>());
        Size("gameobject.meshrenderer", go.AddComponent<MeshRenderer>());
        Size("gameobject.light", go.AddComponent<Light>());
        Size("gameobject.camera", go.AddComponent<Camera>());
        Size("gameobject.audiosource", go.AddComponent<AudioSource>());
        Size("gameobject.withcomponents", go);
        Size("probe.behaviour", this);
        Size("probe.gameobject", gameObject);
        var child = new GameObject("Child");
        child.transform.SetParent(go.transform);
        Size("gameobject.withchild.transform", go.transform);
        Size("rectTransform", new GameObject("Rect", typeof(RectTransform)).transform);
        Try("material.unlit", () => { var m = new Material(Shader.Find("Unlit/Color")); Size("material.unlit", m); Destroy(m); });
        Try("material.standard", () => { var m = new Material(Shader.Find("Standard")); Size("material.standard", m); Destroy(m); });
        Try("textasset", () => { Size("textasset.empty", new TextAsset("")); Size("textasset.1000", new TextAsset(new string('x', 1000))); Size("textasset.1", new TextAsset("y")); Size("textasset.utf8", new TextAsset("\u00e9\u4e2d")); });
        Try("sprite", () =>
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var s = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
            Size("sprite.64", s);
            Destroy(s);
            Destroy(t);
        });
        Try("destroyed", () =>
        {
            var t = new Texture2D(64, 64);
            DestroyImmediate(t);
            Size("destroyed.texture", t);
        });
        Destroy(go);
        Destroy(named);
        Logs("size.objects");

        Try("tempallocator", () =>
        {
            Value("tempallocator.size", N(Profiler.GetTempAllocatorSize()));
            Value("tempallocator.set.1mb", B(Profiler.SetTempAllocatorRequestedSize(1 << 20)));
            Value("tempallocator.size.after1mb", N(Profiler.GetTempAllocatorSize()));
            Value("tempallocator.set.100", B(Profiler.SetTempAllocatorRequestedSize(100)));
            Value("tempallocator.size.after100", N(Profiler.GetTempAllocatorSize()));
            Value("tempallocator.set.0", B(Profiler.SetTempAllocatorRequestedSize(0)));
            Value("tempallocator.size.after0", N(Profiler.GetTempAllocatorSize()));
        });
        Logs("tempallocator");
        yield return null;
    }

    static Vector3[] Positions(int n)
    {
        var v = new Vector3[n];
        for (int i = 0; i < n; i++) v[i] = new Vector3(i % 7, i / 7, i % 3);
        return v;
    }

    static int[] Triangles(int n)
    {
        var t = new List<int>();
        for (int i = 0; i + 2 < n; i++) { t.Add(0); t.Add(i + 1); t.Add(i + 2); }
        return t.ToArray();
    }

    void MeshCase(string k, int n, string attributes)
    {
        var m = new Mesh();
        if (n > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = Positions(n);
        if (attributes.Contains("n")) m.normals = Enumerable.Repeat(Vector3.up, n).ToArray();
        if (attributes.Contains("t") && !attributes.Contains("t2")) m.uv = Enumerable.Repeat(Vector2.one, n).ToArray();
        if (attributes.Contains("t2")) { m.uv = Enumerable.Repeat(Vector2.one, n).ToArray(); m.uv2 = Enumerable.Repeat(Vector2.zero, n).ToArray(); }
        if (attributes.Contains("c")) m.colors32 = Enumerable.Repeat(new Color32(1, 2, 3, 4), n).ToArray();
        if (attributes.Contains("g")) m.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), n).ToArray();
        Size(k + ".vertices", m);
        m.triangles = Triangles(n);
        Size(k + ".triangles", m);
        m.UploadMeshData(false);
        Size(k + ".uploaded", m);
        m.UploadMeshData(true);
        Size(k + ".unreadable", m);
        Destroy(m);
    }

    // ------------------------------------------------------------------ graphics driver memory

    /// <summary>How the driver figure moves when textures and render textures come and go; its
    /// absolute value (everything the Editor holds) is machine data.</summary>
    IEnumerator DriverMemory()
    {
        long start = Profiler.GetAllocatedMemoryForGraphicsDriver();
        Machine("profiler.graphicsDriver.start", N(start));
        long last = start;
        void Step(string k)
        {
            long now = Profiler.GetAllocatedMemoryForGraphicsDriver();
            Value("gfxdriver." + k, N(now - last));
            last = now;
        }
        Step("same");
        var t = new Texture2D(256, 128, TextureFormat.RGBA32, false);
        Step("texture.new");
        t.Apply(false, false);
        Step("texture.applied");
        var tm = new Texture2D(256, 128, TextureFormat.RGBA32, true);
        Step("texture.mips.new");
        tm.Apply(false, true);
        Step("texture.mips.unreadable");
        var c = new Texture2D(256, 128, TextureFormat.DXT5, false);
        c.Apply();
        Step("texture.dxt5");
        var rt = new RenderTexture(256, 128, 24, RenderTextureFormat.ARGB32);
        Step("rt.new");
        rt.Create();
        Step("rt.created");
        var rtf = new RenderTexture(128, 64, 0, RenderTextureFormat.ARGBFloat) { antiAliasing = 4 };
        rtf.Create();
        Step("rt.float.msaa4");
        rt.Release();
        Step("rt.released");
        DestroyImmediate(rt);
        Step("rt.destroyed");
        DestroyImmediate(rtf);
        Step("rt.float.destroyed");
        DestroyImmediate(t);
        Step("texture.destroyed");
        DestroyImmediate(tm);
        Step("texture.mips.destroyed");
        DestroyImmediate(c);
        Step("texture.dxt5.destroyed");
        // Mesh buffers are accounted when their upload completes, at no fixed point within the
        // frame, and across frames the figure follows everything the Editor draws: machine data.
        var mesh = new Mesh { vertices = Positions(1000), triangles = Triangles(1000) };
        long before = Profiler.GetAllocatedMemoryForGraphicsDriver();
        mesh.UploadMeshData(false);
        Machine("profiler.graphicsDriver.mesh.uploaded", N(Profiler.GetAllocatedMemoryForGraphicsDriver() - before));
        DestroyImmediate(mesh);
        Logs("gfxdriver");
        yield return null;
    }

    // ------------------------------------------------------------------ OpenURL

    IEnumerator OpenUrls()
    {
        string log = Environment.GetEnvironmentVariable("APPFACTS_OPENURL_LOG");
        string stubs = Environment.GetEnvironmentVariable("APPFACTS_OPENURL_STUBS");
        string opener = Application.platform == RuntimePlatform.OSXEditor ? "open" : "xdg-open";
        string found = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
            .Select(d => Path.Combine(d, opener)).FirstOrDefault(File.Exists);
        if (log == null || stubs == null || found == null || Path.GetDirectoryName(found) != stubs.TrimEnd('/'))
        {
            Value("fatal", Q("OpenURL is not stubbed: " + opener + " resolves to " + found));
            throw new InvalidOperationException("OpenURL is not stubbed");
        }
        var urls = new[]
        {
            ("https", "https://example.com/path?a=1&b=two#frag"),
            ("space", "https://example.com/with space"),
            ("file", "file:///tmp/appfacts-missing.txt"),
            ("mailto", "mailto:someone@example.com"),
            ("custom", "appfacts-scheme://open/this"),
            ("relative", "relative/path.txt"),
            ("quote", "https://example.com/'quote\"\\"),
            ("empty", ""),
            ("null", null),
        };
        foreach (var (k, url) in urls)
        {
            Try("openurl." + k, () => Application.OpenURL(url));
            Logs("openurl." + k);
            var deadline = Time.realtimeSinceStartup + 2f;
            int seen = File.Exists(log) ? File.ReadAllLines(log).Length : 0;
            while (Time.realtimeSinceStartup < deadline && (File.Exists(log) ? File.ReadAllLines(log).Length : 0) == seen)
                yield return null;
            var lines = File.Exists(log) ? File.ReadAllLines(log) : new string[0];
            Value("openurl." + k + ".runs", A(lines.Skip(seen).Select(Norm)));
        }
    }

    // ------------------------------------------------------------------ machine facts

    IEnumerator MachineFacts()
    {
        Machine("application.consoleLogPath", Q(Norm(Application.consoleLogPath)));
        Machine("application.buildGUID", Q(Application.buildGUID));
        Machine("application.HasProLicense", B(Application.HasProLicense()));
        Machine("application.isBatchMode", B(Application.isBatchMode));
        var startup = Debug.RetrieveStartupLogs();
        Machine("debug.startupLogs.count", N(startup.Length));
        Machine("debug.startupLogs", A(startup.Take(20).Select(l => l.logType + " " + l.timestamp + " " + Norm(l.message))));
        Machine("systeminfo.deviceModel", Q(SystemInfo.deviceModel));
        Machine("systeminfo.deviceName", Q(SystemInfo.deviceName));
        Machine("systeminfo.deviceType", Q(SystemInfo.deviceType.ToString()));
        Machine("systeminfo.deviceUniqueIdentifier", Q(SystemInfo.deviceUniqueIdentifier));
        Machine("systeminfo.operatingSystem", Q(SystemInfo.operatingSystem));
        Machine("systeminfo.operatingSystemFamily", Q(SystemInfo.operatingSystemFamily.ToString()));
        Machine("systeminfo.processorType", Q(SystemInfo.processorType));
        Machine("systeminfo.processorModel", Q(SystemInfo.processorModel));
        Machine("systeminfo.processorManufacturer", Q(SystemInfo.processorManufacturer));
        Machine("systeminfo.processorFrequency", N(SystemInfo.processorFrequency));
        Machine("systeminfo.processorCount", N(SystemInfo.processorCount));
        Machine("systeminfo.systemMemorySize", N(SystemInfo.systemMemorySize));
        Machine("systeminfo.batteryLevel", F(SystemInfo.batteryLevel));
        Machine("systeminfo.batteryStatus", Q(SystemInfo.batteryStatus.ToString()));
        Machine("systeminfo.graphicsDeviceName", Q(SystemInfo.graphicsDeviceName));
        Machine("systeminfo.graphicsDeviceVendor", Q(SystemInfo.graphicsDeviceVendor));
        Machine("systeminfo.graphicsDeviceVersion", Q(SystemInfo.graphicsDeviceVersion));
        Machine("systeminfo.graphicsDeviceID", N(SystemInfo.graphicsDeviceID));
        Machine("systeminfo.graphicsDeviceVendorID", N(SystemInfo.graphicsDeviceVendorID));
        Machine("systeminfo.graphicsMemorySize", N(SystemInfo.graphicsMemorySize));
        Machine("systeminfo.graphicsDeviceType", Q(SystemInfo.graphicsDeviceType.ToString()));
        Machine("systeminfo.graphicsShaderLevel", N(SystemInfo.graphicsShaderLevel));
        Machine("profiler.supported", B(Profiler.supported));
        Machine("profiler.enabled", B(Profiler.enabled));
        Logs("machine");
        yield return null;
    }
}
