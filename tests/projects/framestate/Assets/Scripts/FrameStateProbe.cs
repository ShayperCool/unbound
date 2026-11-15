using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;

/// <summary>
/// What a frame boundary carries over (FRAMESTATE_OUT): the active render texture a script left
/// set in one frame's Update, LateUpdate or end-of-frame coroutine, read in the next frame
/// (EarlyUpdate and Update), with the screen camera rendering and with no camera rendering; and
/// the matrices a command buffer executed outside cameras draws with (at the start of Update,
/// after a camera rendered, with its own identity matrices, inside GL.LoadOrtho, and whether its
/// own matrices last past it), with GL.modelview read alongside. Pixels are read back.
/// </summary>
public class FrameStateProbe : MonoBehaviour
{
    public static bool Finished;
    static FrameStateProbe instance;
    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    string output;
    int firstFrame = -1;
    Camera screen;
    Mesh quad, corner;
    Material white, matrices;
    bool eofLeaves;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("FRAMESTATE_OUT") == null) return;
        Application.runInBackground = true;
        var go = new GameObject("FrameStateProbe");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<FrameStateProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string L(IEnumerable<string> values) => "[" + string.Join(",", values.Select(Q)) + "]";
    static string Active() => RenderTexture.active == null ? "null" : RenderTexture.active.name;
    static string M(Matrix4x4 m) => "[" + string.Join(",", Enumerable.Range(0, 16).Select(i => m[i % 4, i / 4].ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";
    T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }
    void OnLog(string condition, string stack, LogType type) => logs.Add(type + ": " + condition);
    int Step => firstFrame < 0 ? -1 : Time.frameCount - firstFrame;

    void Awake()
    {
        output = Environment.GetEnvironmentVariable("FRAMESTATE_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Application.logMessageReceived += OnLog;
        // A screen camera rendering every frame, looking at nothing.
        screen = new GameObject("FrameStateScreen").AddComponent<Camera>();
        screen.cullingMask = 0;
        screen.clearFlags = CameraClearFlags.SolidColor;
        screen.backgroundColor = Color.gray;
        DontDestroyOnLoad(screen.gameObject);
        // The next frame's EarlyUpdate, before any script's Update.
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.EarlyUpdate)) continue;
            var systems = loop.subSystemList[i].subSystemList.ToList();
            systems.Insert(0, new PlayerLoopSystem { type = typeof(FrameStateProbe), updateDelegate = EarlyUpdate });
            loop.subSystemList[i].subSystemList = systems.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
        StartCoroutine(EndOfFrames());
    }

    static void EarlyUpdate()
    {
        var self = instance;
        if (self == null || Finished) return;
        switch (self.Step)
        {
            case 1: self.Value("active.update.early", Q(Active())); break;
            case 3: self.Value("active.late.early", Q(Active())); break;
            case 5: self.Value("active.nocamera.early", Q(Active())); break;
            case 7: self.Value("active.endofframe.early", Q(Active())); break;
            case 8: self.Value("matrices.early", self.DumpNow("early")); break;
        }
    }

    IEnumerator EndOfFrames()
    {
        while (!Finished)
        {
            yield return new WaitForEndOfFrame();
            if (Step == 8) Value("matrices.endofframe", DumpNow("endofframe"));
            if (Step == 6)
            {
                // Left active by the end-of-frame coroutine; read in the next frame.
                RenderTexture.active = Target("eofleft");
                eofLeaves = true;
                Value("active.endofframe.same", Q(Active()));
            }
        }
    }

    void Update()
    {
        if (Finished) return;
        if (firstFrame < 0)
        {
            firstFrame = Time.frameCount;
            Value("active.initial", Q(Active()));
        }
        try
        {
            switch (Step)
            {
                case 0:
                    // Left active by Update; read in LateUpdate, at the end of the frame and in the next.
                    RenderTexture.active = Target("updateleft");
                    break;
                case 1:
                    Value("active.update.next", Q(Active()));
                    Value("gl.modelview.update", M(GL.modelview));
                    RenderTexture.active = null;
                    break;
                case 3:
                    Value("active.late.next", Q(Active()));
                    RenderTexture.active = null;
                    break;
                case 4:
                    // No camera renders this frame.
                    screen.enabled = false;
                    RenderTexture.active = Target("nocamera");
                    break;
                case 5:
                    Value("active.nocamera.next", Q(Active()));
                    RenderTexture.active = null;
                    screen.enabled = true;
                    break;
                case 7:
                    // Batchmode Editors do not run end-of-frame coroutines.
                    Value("active.endofframe.next", Q(eofLeaves ? Active() : "no end of frame"));
                    RenderTexture.active = null;
                    break;
                case 8:
                    OutsideDraws();
                    break;
                case 9:
                    Finish();
                    break;
            }
        }
        catch (Exception e)
        {
            Value("error", Q(e.ToString()));
            Finish();
        }
    }

    void LateUpdate()
    {
        if (Finished) return;
        if (Step == 0) Value("active.update.late", Q(Active()));
        if (Step == 2) RenderTexture.active = Target("lateleft");
        if (Step == 8) Value("matrices.late", DumpNow("late"));
    }

    RenderTexture Target(string name)
    {
        var texture = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = name });
        texture.Create();
        return texture;
    }

    /// <summary>A texture's pixels as rows (bottom first) of runs "rrggbbaa*count".</summary>
    string Pixels(Texture source)
    {
        const int size = 8;
        var previous = RenderTexture.active;
        var readback = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, readback);
        var cpu = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        RenderTexture.active = readback;
        cpu.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(readback);
        var pixels = cpu.GetPixels32();
        DestroyImmediate(cpu);
        var rows = new List<string>();
        for (int y = 0; y < size; y++)
        {
            var row = new StringBuilder();
            int x = 0;
            while (x < size)
            {
                var c = pixels[y * size + x];
                int n = 1;
                while (x + n < size && pixels[y * size + x + n].Equals(c)) n++;
                if (row.Length > 0) row.Append(' ');
                row.Append($"{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}*{n}");
                x += n;
            }
            rows.Add(row.ToString());
        }
        return L(rows);
    }

    /// <summary>A command buffer that clears its target and draws a mesh with the identity matrix
    /// (optionally after setting identity view and projection), executed outside cameras.</summary>
    RenderTexture Execute(string key, Mesh mesh, bool identity)
    {
        var target = Target("outside" + key);
        using (var cmd = new CommandBuffer { name = "outside " + key })
        {
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(true, true, Color.black);
            if (identity) cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMesh(mesh, Matrix4x4.identity, white, 0, 0);
            // The same buffer, with the same matrices: what they are.
            var dump = Dump();
            cmd.SetRenderTarget(dump);
            cmd.DrawMesh(quad, Matrix4x4.identity, matrices, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("matrices." + key, Read(dump));
        }
        return target;
    }

    /// <summary>The matrices a command buffer executed now draws with.</summary>
    string DumpNow(string key)
    {
        Setup();
        var dump = Dump();
        using (var cmd = new CommandBuffer { name = "dump " + key })
        {
            cmd.SetRenderTarget(dump);
            cmd.DrawMesh(quad, Matrix4x4.identity, matrices, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
        }
        return Read(dump);
    }

    RenderTexture Dump()
    {
        var texture = Keep(new RenderTexture(16, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { name = "dump" });
        texture.Create();
        return texture;
    }

    /// <summary>A matrix dump (Hidden/FrameStateMatrices) as rows-first element lists.</summary>
    string Read(RenderTexture dump)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(16, 4, TextureFormat.RGBAFloat, false, true);
        RenderTexture.active = dump;
        cpu.ReadPixels(new Rect(0, 0, 16, 4), 0, 0);
        RenderTexture.active = previous;
        var pixels = cpu.GetPixels();
        DestroyImmediate(cpu);
        var values = new float[4, 16];
        var seen = new bool[4, 16];
        foreach (var c in pixels)
        {
            int m = Mathf.RoundToInt(c.g), row = Mathf.RoundToInt(c.b), column = Mathf.RoundToInt(c.a);
            if (m < 0 || m > 3 || row < 0 || row > 3 || column < 0 || column > 3) return Q("unreadable");
            values[m, row * 4 + column] = c.r;
            seen[m, row * 4 + column] = true;
        }
        var names = new[] { "vp", "v", "p", "m" };
        var parts = new List<string>();
        for (int m = 0; m < 4; m++)
        {
            var row = new List<string>();
            for (int i = 0; i < 16; i++)
                row.Add(seen[m, i] ? values[m, i].ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "null");
            parts.Add(Q(names[m]) + ":[" + string.Join(",", row) + "]");
        }
        return "{" + string.Join(",", parts) + "}";
    }

    /// <summary>A quad over clip space (white, no culling, no depth test) drawn by command buffers
    /// executed outside cameras: at the start of Update (after the screen camera rendered the
    /// previous frame), after a camera at (0, 0, -5) rendered, and with identity view and
    /// projection; a quad over clip space's upper right quarter plain and inside GL.LoadOrtho;
    /// then DrawMeshNow of that quarter after a buffer that set its own matrices.</summary>
    void Setup()
    {
        if (white != null) return;
        white = Keep(new Material(Shader.Find("Hidden/Internal-Colored")) { color = Color.white });
        white.SetInt("_Cull", 0);
        white.SetInt("_ZTest", (int)CompareFunction.Always);
        matrices = Keep(new Material(Shader.Find("Hidden/FrameStateMatrices")));
        quad = Keep(new Mesh { name = "quad" });
        quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        corner = Keep(new Mesh { name = "corner" });
        corner.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) };
        corner.triangles = new[] { 0, 1, 2, 0, 2, 3 };
    }

    void OutsideDraws()
    {
        Setup();
        Value("screen", "[" + Screen.width + "," + Screen.height + "]");
        Value("outside.fresh", Pixels(Execute("fresh", quad, false)));
        var camera = new GameObject("outside camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(0f, 0f, -5f);
        camera.targetTexture = Target("outsidecamera");
        camera.cullingMask = 0;
        camera.Render();
        Value("gl.modelview.aftercamera", M(GL.modelview));
        Value("outside.aftercamera", Pixels(Execute("aftercamera", quad, false)));
        Value("outside.identity", Pixels(Execute("identity", quad, true)));
        DestroyImmediate(camera.gameObject);
        Value("outside.corner", Pixels(Execute("corner", corner, false)));
        GL.PushMatrix();
        GL.LoadOrtho();
        var ortho = Execute("ortho", corner, false);
        GL.PopMatrix();
        Value("outside.ortho", Pixels(ortho));
        var after = Target("outsideafter");
        using (var cmd = new CommandBuffer { name = "outside after" })
        {
            cmd.SetRenderTarget(after);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Scale(new Vector3(0.5f, 0.5f, 1f)));
            Graphics.ExecuteCommandBuffer(cmd);
        }
        var previous = RenderTexture.active;
        RenderTexture.active = after;
        white.SetPass(0);
        Graphics.DrawMeshNow(corner, Matrix4x4.identity);
        var dump = Dump();
        RenderTexture.active = dump;
        matrices.SetPass(0);
        Graphics.DrawMeshNow(quad, Matrix4x4.identity);
        RenderTexture.active = previous;
        Value("outside.after", Pixels(after));
        Value("matrices.after", Read(dump));
    }

    void Finish()
    {
        if (Finished) return;
        RenderTexture.active = null;
        Value("device", Q(SystemInfo.graphicsDeviceType.ToString()));
        Value("logs", L(logs));
        Value("complete", "1");
        File.WriteAllText(output, records.ToString());
        Application.logMessageReceived -= OnLog;
        foreach (var o in made) if (o != null) DestroyImmediate(o);
        Finished = true;
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
