using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Command buffer and Graphics members measured by readbacks: random write targets of fragment
/// programs (Graphics and CommandBuffer, by register, with and without a material's buffer,
/// cleared, kept), ConvertTexture (formats, sizes, elements, refused conversions),
/// GetTemporaryRTArray (descriptors, slices, camera-relative sizes) and readbacks into native
/// slices. Every case records its values, exceptions (<c>.error</c>) and log messages (<c>.log</c>).
/// </summary>
public class CmdBuffersProbe : MonoBehaviour
{
    const uint Sentinel = 0xAAAA0000u;

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    bool capturing;
    string output;
    Material uav, uavAuto;
    RenderTexture target;
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    readonly List<IDisposable> disposables = new List<IDisposable>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("CMDBUFFERS_PROBE_OUT") == null) return;
        new GameObject("CmdBuffersProbe").AddComponent<CmdBuffersProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string U(IEnumerable<uint> values) => "[" + string.Join(",", values) + "]";
    static string Hex(NativeArray<byte> data)
    {
        var s = new StringBuilder(data.Length * 2);
        for (int i = 0; i < data.Length; i++) s.Append(data[i].ToString("x2"));
        return Q(s.ToString());
    }

    void OnLog(string condition, string stack, LogType type)
    {
        if (capturing) logs.Add(type + ": " + condition);
    }

    /// <summary>Runs a case, recording what it throws (<c>.error</c>) and logs (<c>.log</c>).</summary>
    void Try(string key, Action body)
    {
        logs.Clear();
        capturing = true;
        try { body(); }
        catch (Exception e) { Value(key + ".error", Q(e.GetType().Name + ": " + e.Message)); }
        capturing = false;
        if (logs.Count > 0) Value(key + ".log", "[" + string.Join(",", logs.Select(Q)) + "]");
        logs.Clear();
    }

    T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }

    void Start()
    {
        output = Environment.GetEnvironmentVariable("CMDBUFFERS_PROBE_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return null;
        yield return null;
        int code = 0;
        try
        {
            Debug.Log("CMDBUFFERS_DEVICE " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
            uav = Keep(new Material(Resources.Load<Shader>("UavWrite")));
            uavAuto = Keep(new Material(Resources.Load<Shader>("UavWriteAuto")));
            target = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32));
            target.Create();
            RandomWriteTargets();
            CommandRandomWriteTargets();
            ConvertTextures();
            TemporaryArrays();
            Slices();
            Instanced();
            IndexedDraws();
            ComputeParams();
        }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 1;
        }
        // Fences span frames: run step by step, a failure recorded as fatal.
        var fences = Fences();
        while (code == 0)
        {
            bool more;
            try { more = fences.MoveNext(); }
            catch (Exception e)
            {
                Value("fatal", Q(e.ToString()));
                code = 1;
                break;
            }
            if (!more) break;
            yield return fences.Current;
        }
        foreach (var d in disposables) d.Dispose();
        foreach (var o in made) if (o != null) DestroyImmediate(o);
        Value("complete", B(code == 0));
        File.WriteAllText(output, records.ToString());
        Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    // ------------------------------------------------------------ random write targets

    ComputeBuffer Fresh(int count = 16)
    {
        var b = new ComputeBuffer(count, 4);
        disposables.Add(b);
        b.SetData(Enumerable.Repeat(Sentinel, count).ToArray());
        return b;
    }

    GraphicsBuffer FreshGraphics(int count = 16)
    {
        var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
        disposables.Add(b);
        b.SetData(Enumerable.Repeat(Sentinel, count).ToArray());
        return b;
    }

    static string Words(ComputeBuffer b) { var a = new uint[b.count]; b.GetData(a); return U(a); }
    static string Words(GraphicsBuffer b) { var a = new uint[b.count]; b.GetData(a); return U(a); }

    /// <summary>One full-target draw of <paramref name="m"/> (its pass 0) into the 4x4 target.</summary>
    void DrawNow(Material m, float tag)
    {
        m.SetFloat("_Tag", tag);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        m.SetPass(0);
        Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
        RenderTexture.active = previous;
    }

    Material append;

    /// <summary>An Append buffer of 48 sentinels given to <paramref name="draw"/>, then its
    /// count (CopyCount) and its words in order (what was appended, sorted: the GPU's order).</summary>
    void AppendCase(string key, int tag, Action<ComputeBuffer> draw)
    {
        var b = new ComputeBuffer(48, 4, ComputeBufferType.Append);
        disposables.Add(b);
        b.SetData(Enumerable.Repeat(Sentinel, 48).ToArray());
        var count = new ComputeBuffer(1, 4, ComputeBufferType.Raw);
        disposables.Add(count);
        Try(key, () => { draw(b); Graphics.ClearRandomWriteTargets(); });
        ComputeBuffer.CopyCount(b, count, 0);
        var n = new uint[1];
        count.GetData(n);
        var words = new uint[48];
        b.GetData(words);
        Value(key + ".count", n[0].ToString());
        Value(key, U(words.OrderBy(w => w)));
    }

    void RandomWriteTargets()
    {
        append = Keep(new Material(Resources.Load<Shader>("UavAppend")));
        // The register the shader declares (u1).
        var a = Fresh();
        Try("uav.slot1", () => { Graphics.SetRandomWriteTarget(1, a); DrawNow(uav, 1); Graphics.ClearRandomWriteTargets(); });
        Value("uav.slot1", Words(a));

        var b = Fresh();
        Try("uav.slot2", () => { Graphics.SetRandomWriteTarget(2, b); DrawNow(uav, 2); Graphics.ClearRandomWriteTargets(); });
        Value("uav.slot2", Words(b));

        // A material's buffer of the name, with and without a target at the register.
        var m1 = Fresh();
        Try("uav.material", () => { uav.SetBuffer("_Out", m1); DrawNow(uav, 3); });
        Value("uav.material", Words(m1));
        var m2 = Fresh();
        var c = Fresh();
        Try("uav.both", () => { uav.SetBuffer("_Out", m2); Graphics.SetRandomWriteTarget(1, c); DrawNow(uav, 4); Graphics.ClearRandomWriteTargets(); });
        Value("uav.both.material", Words(m2));
        Value("uav.both.target", Words(c));
        // The material's buffer again once the target is cleared.
        var m3 = Fresh();
        Try("uav.after", () => { uav.SetBuffer("_Out", m3); DrawNow(uav, 5); });
        Value("uav.after", Words(m3));
        var plain = Keep(new Material(uav.shader));

        // Cleared before the draw, kept over several draws, replaced.
        var d = Fresh();
        Try("uav.cleared", () => { Graphics.SetRandomWriteTarget(1, d); Graphics.ClearRandomWriteTargets(); DrawNow(plain, 6); });
        Value("uav.cleared", Words(d));
        var e = Fresh();
        Try("uav.kept", () => { Graphics.SetRandomWriteTarget(1, e); DrawNow(plain, 7); DrawNow(plain, 8); Graphics.ClearRandomWriteTargets(); });
        Value("uav.kept", Words(e));
        var f1 = Fresh();
        var f2 = Fresh();
        Try("uav.replaced", () => { Graphics.SetRandomWriteTarget(1, f1); Graphics.SetRandomWriteTarget(1, f2); DrawNow(plain, 9); Graphics.ClearRandomWriteTargets(); });
        Value("uav.replaced.first", Words(f1));
        Value("uav.replaced.second", Words(f2));

        // A graphics buffer, both counter overloads.
        var g = FreshGraphics();
        Try("uav.graphicsbuffer", () => { Graphics.SetRandomWriteTarget(1, g, true); DrawNow(plain, 10); Graphics.ClearRandomWriteTargets(); });
        Value("uav.graphicsbuffer", Words(g));
        var h = Fresh();
        Try("uav.preserve", () => { Graphics.SetRandomWriteTarget(1, h, false); DrawNow(plain, 11); Graphics.ClearRandomWriteTargets(); });
        Value("uav.preserve", Words(h));

        // No register declared: which index reaches it.
        var auto1 = Fresh();
        Try("uav.auto1", () => { Graphics.SetRandomWriteTarget(1, auto1); DrawNow(uavAuto, 12); Graphics.ClearRandomWriteTargets(); });
        Value("uav.auto1", Words(auto1));

        // Append buffers: the counter restarts from 0 unless preserved, and keeps counting
        // over draws while the target stays set; a material's buffer of the name counts too.
        AppendCase("uav.append.reset", 13, b2 => { b2.SetCounterValue(5); Graphics.SetRandomWriteTarget(1, b2, false); DrawNow(append, 13); });
        AppendCase("uav.append.preserve", 14, b2 => { b2.SetCounterValue(5); Graphics.SetRandomWriteTarget(1, b2, true); DrawNow(append, 14); });
        AppendCase("uav.append.twice", 15, b2 => { Graphics.SetRandomWriteTarget(1, b2); DrawNow(append, 15); DrawNow(append, 15); });
        AppendCase("uav.append.material", 16, b2 => { var m = Keep(new Material(append)); m.SetBuffer("_App", b2); DrawNow(m, 16); });
        AppendCase("cmd.uav.append", 17, b2 => Execute(cmd => { cmd.SetRandomWriteTarget(1, b2, false); DrawCmd(cmd, append, 17); }));
        AppendCase("cmd.uav.append.reset", 30, b2 => { b2.SetCounterValue(5); Execute(cmd => { cmd.SetRandomWriteTarget(1, b2, false); DrawCmd(cmd, append, 30); }); });
        AppendCase("cmd.uav.append.preserve", 18, b2 => { b2.SetCounterValue(3); Execute(cmd => { cmd.SetRandomWriteTarget(1, b2, true); DrawCmd(cmd, append, 18); }); });

        // A texture set at an index takes the place of a buffer there.
        var t1 = Fresh();
        var rwTexture = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true });
        rwTexture.Create();
        Try("uav.texturereplaces", () => { Graphics.SetRandomWriteTarget(1, t1); Graphics.SetRandomWriteTarget(1, rwTexture); DrawNow(plain, 19); Graphics.ClearRandomWriteTargets(); });
        Value("uav.texturereplaces", Words(t1));

        // An Append program without a buffer of the name, and with a buffer that has no counter.
        Try("uav.append.unset", () => DrawNow(Keep(new Material(append)), 31));
        var noCounter = Fresh(48);
        Try("uav.append.nocounter", () => { Graphics.SetRandomWriteTarget(1, noCounter); DrawNow(append, 32); Graphics.ClearRandomWriteTargets(); });
        { var w = new uint[48]; noCounter.GetData(w); Value("uav.append.nocounter", U(w.OrderBy(v => v))); }

        // Arguments.
        var x = Fresh();
        Try("uav.arg.index0", () => { Graphics.SetRandomWriteTarget(0, x); Graphics.ClearRandomWriteTargets(); });
        Try("uav.arg.negative", () => { Graphics.SetRandomWriteTarget(-1, x); Graphics.ClearRandomWriteTargets(); });
        Try("uav.arg.index7", () => { Graphics.SetRandomWriteTarget(7, x); Graphics.ClearRandomWriteTargets(); });
        Try("uav.arg.index8", () => { Graphics.SetRandomWriteTarget(8, x); Graphics.ClearRandomWriteTargets(); });
        Try("uav.arg.nullbuffer", () => { Graphics.SetRandomWriteTarget(1, (ComputeBuffer)null); Graphics.ClearRandomWriteTargets(); });
        Try("uav.arg.nullgraphics", () => { Graphics.SetRandomWriteTarget(1, (GraphicsBuffer)null, false); Graphics.ClearRandomWriteTargets(); });
        Try("uav.arg.nulltexture", () => { Graphics.SetRandomWriteTarget(1, (RenderTexture)null); Graphics.ClearRandomWriteTargets(); });
        var released = new ComputeBuffer(16, 4);
        released.Release();
        Try("uav.arg.released", () => { Graphics.SetRandomWriteTarget(1, released); Graphics.ClearRandomWriteTargets(); });
        var noRandom = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32));
        noRandom.Create();
        Try("uav.arg.texture", () => { Graphics.SetRandomWriteTarget(1, noRandom); Graphics.ClearRandomWriteTargets(); });
        var random = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true });
        random.Create();
        Try("uav.arg.randomtexture", () => { Graphics.SetRandomWriteTarget(1, random); Graphics.ClearRandomWriteTargets(); });
        var uncreated = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true });
        Try("uav.arg.uncreated", () => { Graphics.SetRandomWriteTarget(1, uncreated); Graphics.ClearRandomWriteTargets(); });
        Value("uav.arg.uncreated.created", B(uncreated.IsCreated()));
    }

    void Execute(Action<CommandBuffer> record)
    {
        using (var cmd = new CommandBuffer { name = "probe" })
        {
            record(cmd);
            Graphics.ExecuteCommandBuffer(cmd);
        }
    }

    /// <summary>A full-target draw of the command buffer (pass 0 into the 4x4 target).</summary>
    void DrawCmd(CommandBuffer cmd, Material m, float tag)
    {
        var block = new MaterialPropertyBlock();
        block.SetFloat("_Tag", tag);
        cmd.SetRenderTarget(target);
        cmd.DrawProcedural(Matrix4x4.identity, m, 0, MeshTopology.Triangles, 3, 1, block);
    }

    void CommandRandomWriteTargets()
    {
        var plain = Keep(new Material(uav.shader));
        var a = Fresh();
        Try("cmd.uav.slot1", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, a); DrawCmd(cmd, plain, 21); cmd.ClearRandomWriteTargets(); }));
        Value("cmd.uav.slot1", Words(a));
        var b = Fresh();
        Try("cmd.uav.slot2", () => Execute(cmd => { cmd.SetRandomWriteTarget(2, b); DrawCmd(cmd, plain, 22); cmd.ClearRandomWriteTargets(); }));
        Value("cmd.uav.slot2", Words(b));
        var g = FreshGraphics();
        Try("cmd.uav.graphicsbuffer", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, g); DrawCmd(cmd, plain, 23); cmd.ClearRandomWriteTargets(); }));
        Value("cmd.uav.graphicsbuffer", Words(g));
        var c = Fresh();
        Try("cmd.uav.preserve", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, c, true); DrawCmd(cmd, plain, 24); cmd.ClearRandomWriteTargets(); }));
        Value("cmd.uav.preserve", Words(c));
        var d = Fresh();
        Try("cmd.uav.cleared", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, d); cmd.ClearRandomWriteTargets(); DrawCmd(cmd, plain, 25); }));
        Value("cmd.uav.cleared", Words(d));

        // A target the buffer leaves set, seen by a later draw outside it.
        var e = Fresh();
        Try("cmd.uav.left", () => { Execute(cmd => cmd.SetRandomWriteTarget(1, e)); DrawNow(plain, 26); Graphics.ClearRandomWriteTargets(); });
        Value("cmd.uav.left", Words(e));
        // A target set outside, seen inside a buffer.
        var f = Fresh();
        Try("cmd.uav.outside", () => { Graphics.SetRandomWriteTarget(1, f); Execute(cmd => DrawCmd(cmd, plain, 27)); Graphics.ClearRandomWriteTargets(); });
        Value("cmd.uav.outside", Words(f));
        // The draw's property block buffer with a target set.
        var p = Fresh();
        var q = Fresh();
        Try("cmd.uav.block", () => Execute(cmd =>
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat("_Tag", 28);
            block.SetBuffer("_Out", p);
            cmd.SetRandomWriteTarget(1, q);
            cmd.SetRenderTarget(target);
            cmd.DrawProcedural(Matrix4x4.identity, plain, 0, MeshTopology.Triangles, 3, 1, block);
            cmd.ClearRandomWriteTargets();
        }));
        Value("cmd.uav.block.block", Words(p));
        Value("cmd.uav.block.target", Words(q));

        // A texture set at the index in the buffer takes the place of a buffer there.
        var t1 = Fresh();
        var rwTexture = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true });
        rwTexture.Create();
        Try("cmd.uav.texturereplaces", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, t1); cmd.SetRandomWriteTarget(1, rwTexture); DrawCmd(cmd, plain, 29); cmd.ClearRandomWriteTargets(); }));
        Value("cmd.uav.texturereplaces", Words(t1));

        var x = Fresh();
        Try("cmd.uav.arg.negative", () => Execute(cmd => { cmd.SetRandomWriteTarget(-1, x); cmd.ClearRandomWriteTargets(); }));
        Try("cmd.uav.arg.index8", () => Execute(cmd => { cmd.SetRandomWriteTarget(8, x); cmd.ClearRandomWriteTargets(); }));
        Try("cmd.uav.arg.nullbuffer", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, (ComputeBuffer)null); cmd.ClearRandomWriteTargets(); }));
        Try("cmd.uav.arg.nullgraphics", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, (GraphicsBuffer)null); cmd.ClearRandomWriteTargets(); }));
        var noRandom = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32));
        noRandom.Create();
        Try("cmd.uav.arg.texture", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, noRandom); cmd.ClearRandomWriteTargets(); }));
        var random = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true });
        random.Create();
        Try("cmd.uav.arg.randomtexture", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, random); cmd.ClearRandomWriteTargets(); }));
        Try("cmd.uav.arg.builtin", () => Execute(cmd => { cmd.SetRandomWriteTarget(1, BuiltinRenderTextureType.CameraTarget); cmd.ClearRandomWriteTargets(); }));
    }

    // ------------------------------------------------------------ DrawMeshInstanced

    static Vector4 Rgb(int r, int g, int b) => new Vector4(r / 255f, g / 255f, b / 255f, 1);

    void Instanced()
    {
        var strip = Keep(new RenderTexture(8, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        strip.Create();
        var quad = Keep(new Mesh());
        quad.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var m = Keep(new Material(Resources.Load<Shader>("Instanced")) { enableInstancing = true });
        m.SetVector("_Color", Rgb(255, 20, 40));
        // Instance i covers the strip's pixel columns 2i and 2i+1 (identity view and projection).
        var mats = Enumerable.Range(0, 4).Select(i => Matrix4x4.TRS(new Vector3(-1 + 0.5f * i, -1, 0), Quaternion.identity, new Vector3(0.5f, 2, 1))).ToArray();
        var colors = Enumerable.Range(0, 4).Select(i => Rgb(50 * (i + 1), 200, 10 + i)).ToArray();
        void Inst(string key, Action<CommandBuffer> record)
        {
            Execute(cmd => { cmd.SetRenderTarget(strip); cmd.ClearRenderTarget(false, true, Color.clear); });
            Try(key, () => Execute(cmd =>
            {
                cmd.SetRenderTarget(strip);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                record(cmd);
            }));
            Value(key, Read(strip));
        }
        Inst("cmd.instanced.all", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, mats));
        Inst("cmd.instanced.count", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, mats, 2));
        var block = new MaterialPropertyBlock();
        block.SetVectorArray("_Color", colors);
        Inst("cmd.instanced.block", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, mats, 4, block));
        var scalar = new MaterialPropertyBlock();
        scalar.SetVector("_Color", Rgb(7, 77, 177));
        Inst("cmd.instanced.blockscalar", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, mats, 4, scalar));
        var ids = Keep(new Material(Resources.Load<Shader>("InstancedIds")) { enableInstancing = true });
        Inst("cmd.instanced.rawid", cmd => cmd.DrawMeshInstanced(quad, 0, ids, 0, mats, 4));
        var palette = new MaterialPropertyBlock();
        palette.SetFloat("_UseArray", 1);
        palette.SetVectorArray("_Palette", colors);
        palette.SetVectorArray("_Tint", new[] { Vector4.one, Vector4.one, Vector4.one, Vector4.one });
        Inst("cmd.instanced.uniformarray", cmd => cmd.DrawMeshInstanced(quad, 0, ids, 0, mats, 4, palette));
        Inst("cmd.instanced.allpasses", cmd => cmd.DrawMeshInstanced(quad, 0, m, -1, mats, 3, block));
        // Matrices and block changed after recording, before the buffer runs.
        var later = (Matrix4x4[])mats.Clone();
        var laterBlock = new MaterialPropertyBlock();
        laterBlock.SetVectorArray("_Color", colors);
        Inst("cmd.instanced.recorded", cmd =>
        {
            cmd.DrawMeshInstanced(quad, 0, m, 0, later, 4, laterBlock);
            later[0] = Matrix4x4.TRS(new Vector3(5, 5, 0), Quaternion.identity, Vector3.one);
            laterBlock.SetVectorArray("_Color", colors.Select(c => Rgb(1, 2, 3)).ToArray());
        });
        var many = Enumerable.Repeat(mats[1], 1100).ToArray();
        Inst("cmd.instanced.max", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, many, 1023));
        var off = Keep(new Material(m) { enableInstancing = false });
        Inst("cmd.instanced.noinstancing", cmd => cmd.DrawMeshInstanced(quad, 0, off, 0, mats));
        Inst("cmd.instanced.arg.mesh", cmd => cmd.DrawMeshInstanced(null, 0, m, 0, mats));
        Inst("cmd.instanced.arg.material", cmd => cmd.DrawMeshInstanced(quad, 0, null, 0, mats));
        Inst("cmd.instanced.arg.matrices", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, null));
        Inst("cmd.instanced.arg.negative", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, mats, -1));
        Inst("cmd.instanced.arg.zero", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, mats, 0));
        Inst("cmd.instanced.arg.1024", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, many, 1024));
        Inst("cmd.instanced.arg.pastarray", cmd => cmd.DrawMeshInstanced(quad, 0, m, 0, mats, 5));
        Inst("cmd.instanced.arg.submesh", cmd => cmd.DrawMeshInstanced(quad, 1, m, 0, mats));
        Inst("cmd.instanced.arg.submeshnegative", cmd => cmd.DrawMeshInstanced(quad, -1, m, 0, mats));
        Inst("cmd.instanced.arg.pass", cmd => cmd.DrawMeshInstanced(quad, 0, m, 3, mats));
    }

    // ------------------------------------------------------------ indexed procedural draws

    void IndexedDraws()
    {
        var grid = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        grid.Create();
        var mat = Keep(new Material(Resources.Load<Shader>("IndexedGrid")));
        mat.SetFloat("_Tag", 10);
        GraphicsBuffer Buffer<T>(GraphicsBuffer.Target t, int stride, T[] data) where T : struct
        {
            var b = new GraphicsBuffer(t, data.Length, stride);
            disposables.Add(b);
            b.SetData(data);
            return b;
        }
        // Cells (0,0) and (2,1) of the grid; cell (3,2) with 32-bit indices past 16 bits.
        var idx16 = Buffer(GraphicsBuffer.Target.Index, 2, new ushort[] { 0, 1, 10, 0, 10, 9, 11, 12, 21, 11, 21, 20 });
        var idx32 = Buffer(GraphicsBuffer.Target.Index, 4, new uint[] { 81021, 81022, 81031, 81021, 81031, 81030 });
        var structured = Buffer(GraphicsBuffer.Target.Structured, 4, new uint[] { 0, 1, 10, 0, 10, 9 });
        // Index count, instances, start index, base vertex, start instance.
        var args = Buffer(GraphicsBuffer.Target.IndirectArguments, 4, new uint[] { 6, 1, 6, 9, 0 });
        var args32 = Buffer(GraphicsBuffer.Target.IndirectArguments, 4, new uint[] { 6, 1, 0, 1, 0 });
        var cargs = new ComputeBuffer(10, 4, ComputeBufferType.IndirectArguments);
        disposables.Add(cargs);
        cargs.SetData(new uint[] { 0, 0, 0, 0, 0, 6, 2, 0, 0, 0 });
        MaterialPropertyBlock Tag(float tag) { var b = new MaterialPropertyBlock(); b.SetFloat("_Tag", tag); return b; }
        var I = Matrix4x4.identity;
        void Clear() => Execute(cmd => { cmd.SetRenderTarget(grid); cmd.ClearRenderTarget(false, true, Color.clear); });
        void Grid(string key, Action<CommandBuffer> record)
        {
            Clear();
            Try(key, () => Execute(cmd => { cmd.SetRenderTarget(grid); record(cmd); }));
            Value(key, Read(grid));
        }
        void Now(string key, Action draw)
        {
            Clear();
            Try(key, () =>
            {
                var previous = RenderTexture.active;
                RenderTexture.active = grid;
                mat.SetPass(0);
                draw();
                RenderTexture.active = previous;
            });
            Value(key, Read(grid));
        }
        Grid("cmd.indexed.u16", cmd => cmd.DrawProcedural(idx16, I, mat, 0, MeshTopology.Triangles, 12));
        Grid("cmd.indexed.count", cmd => cmd.DrawProcedural(idx16, I, mat, 0, MeshTopology.Triangles, 6));
        Grid("cmd.indexed.instances", cmd => cmd.DrawProcedural(idx16, I, mat, 0, MeshTopology.Triangles, 6, 2, Tag(20)));
        Grid("cmd.indexed.u32", cmd => cmd.DrawProcedural(idx32, I, mat, 0, MeshTopology.Triangles, 6));
        Grid("cmd.indirect.indexed", cmd => cmd.DrawProceduralIndirect(idx16, I, mat, 0, MeshTopology.Triangles, args));
        Grid("cmd.indirect.indexed.compute", cmd => cmd.DrawProceduralIndirect(idx16, I, mat, 0, MeshTopology.Triangles, cargs, 20, Tag(30)));
        Grid("cmd.indirect.indexed.u32", cmd => cmd.DrawProceduralIndirect(idx32, I, mat, 0, MeshTopology.Triangles, args32, 0));
        Grid("cmd.indexed.arg.null", cmd => cmd.DrawProcedural((GraphicsBuffer)null, I, mat, 0, MeshTopology.Triangles, 6));
        Grid("cmd.indexed.arg.material", cmd => cmd.DrawProcedural(idx16, I, null, 0, MeshTopology.Triangles, 6));
        Grid("cmd.indexed.arg.structured", cmd => cmd.DrawProcedural(structured, I, mat, 0, MeshTopology.Triangles, 6));
        Grid("cmd.indexed.arg.past", cmd => cmd.DrawProcedural(idx16, I, mat, 0, MeshTopology.Triangles, 18));
        Grid("cmd.indexed.arg.zero", cmd => cmd.DrawProcedural(idx16, I, mat, 0, MeshTopology.Triangles, 0));
        Grid("cmd.indirect.indexed.arg.null", cmd => cmd.DrawProceduralIndirect(null, I, mat, 0, MeshTopology.Triangles, args));
        Grid("cmd.indirect.indexed.arg.args", cmd => cmd.DrawProceduralIndirect(idx16, I, mat, 0, MeshTopology.Triangles, (GraphicsBuffer)null));
        Now("graphics.indexed.now", () => Graphics.DrawProceduralNow(MeshTopology.Triangles, idx16, 12, 2));
        Now("graphics.indirect.indexed.now", () => Graphics.DrawProceduralIndirectNow(MeshTopology.Triangles, idx16, args));
    }

    // ------------------------------------------------------------ compute constant buffers, buffer handles

    void ComputeParams()
    {
        var cs = Resources.Load<ComputeShader>("ConstantRead");
        int read = cs.FindKernel("Read"), fill = cs.FindKernel("Fill");
        int align = SystemInfo.constantBufferOffsetAlignment;
        Value("cbuffer.alignment", align.ToString());
        GraphicsBuffer Constant(params uint[] words)
        {
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Constant, words.Length, 4);
            disposables.Add(b);
            b.SetData(words);
            return b;
        }
        void Const(string key, Action<CommandBuffer> record)
        {
            var output = Fresh(4);
            Try(key, () => Execute(cmd =>
            {
                record(cmd);
                cmd.SetComputeBufferParam(cs, read, "_Out", output);
                cmd.DispatchCompute(cs, read, 1, 1, 1);
            }));
            Value(key, Words(output));
        }
        var gcb = Constant(1, 2, 3, 4);
        Const("cmd.cbuffer.graphics", cmd => cmd.SetComputeConstantBufferParam(cs, "Params", gcb, 0, 16));
        var ccb = new ComputeBuffer(4, 4, ComputeBufferType.Constant);
        disposables.Add(ccb);
        ccb.SetData(new uint[] { 5, 6, 7, 8 });
        Const("cmd.cbuffer.compute", cmd => cmd.SetComputeConstantBufferParam(cs, Shader.PropertyToID("Params"), ccb, 0, 16));
        var big = Constant(Enumerable.Range(0, align / 4 + 4).Select(i => (uint)(1000 + i)).ToArray());
        Const("cmd.cbuffer.offset", cmd => cmd.SetComputeConstantBufferParam(cs, "Params", big, align, 16));
        var changed = Constant(1, 1, 1, 1);
        Const("cmd.cbuffer.recorded", cmd => { cmd.SetComputeConstantBufferParam(cs, "Params", changed, 0, 16); changed.SetData(new uint[] { 9, 10, 11, 12 }); });

        // GPU-generated words copied into a constant buffer before the next kernel reads it.
        var gpuSource = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, 4, 4);
        var gpuConstant = new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 4, 4);
        disposables.Add(gpuSource);
        disposables.Add(gpuConstant);
        Const("cmd.cbuffer.gpucopy", cmd =>
        {
            cmd.SetComputeIntParam(cs, "_Tag", 33);
            cmd.SetComputeBufferParam(cs, fill, "_Out", gpuSource);
            cmd.DispatchCompute(cs, fill, 1, 1, 1);
            cmd.CopyBuffer(gpuSource, gpuConstant);
            cmd.SetComputeConstantBufferParam(cs, "Params", gpuConstant, 0, 16);
        });

        var h1 = FreshGraphics(4);
        var h2 = FreshGraphics(4);
        Value("handle.same", B(h1.bufferHandle == h1.bufferHandle));
        Value("handle.differs", B(h1.bufferHandle != h2.bufferHandle));
        Value("handle.notdefault", B(h1.bufferHandle != default(GraphicsBufferHandle)));
        Try("cmd.handle", () => Execute(cmd =>
        {
            cmd.SetComputeIntParam(cs, "_Tag", 7);
            cmd.SetComputeBufferParam(cs, fill, "_Out", h1.bufferHandle);
            cmd.DispatchCompute(cs, fill, 1, 1, 1);
        }));
        Value("cmd.handle", Words(h1));
        Try("cmd.handle.id", () => Execute(cmd =>
        {
            cmd.SetComputeIntParam(cs, "_Tag", 8);
            cmd.SetComputeBufferParam(cs, fill, Shader.PropertyToID("_Out"), h2.bufferHandle);
            cmd.DispatchCompute(cs, fill, 1, 1, 1);
        }));
        Value("cmd.handle.id", Words(h2));
    }

    // ------------------------------------------------------------ graphics fences

    IEnumerator Fences()
    {
        Value("fence.supported", B(SystemInfo.supportsGraphicsFence));
        Value("fence.asynccompute", B(SystemInfo.supportsAsyncCompute));
        Try("fence.default", () => Value("fence.default.passed", B(default(GraphicsFence).passed)));
        var cmd = new CommandBuffer { name = "fence" };
        var unexecutedBuffer = new CommandBuffer { name = "unexecuted" };
        GraphicsFence f = default, asyncFence = default, unexecuted = default, immediate = default;
        Try("fence.create", () =>
        {
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(false, true, Color.red);
            f = cmd.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.PixelProcessing);
        });
        Try("fence.recorded", () => Value("fence.recorded.passed", B(f.passed)));
        Try("fence.execute", () => Graphics.ExecuteCommandBuffer(cmd));
        Try("fence.executed", () => Value("fence.executed.passed", B(f.passed)));
        Try("fence.completed", () =>
        {
            AsyncGPUReadback.Request(target).WaitForCompletion();
            Value("fence.completed.passed", B(f.passed));
        });
        Try("fence.async", () =>
        {
            using (var c = new CommandBuffer())
            {
                asyncFence = c.CreateAsyncGraphicsFence();
                Value("fence.async.recorded", B(asyncFence.passed));
                Graphics.ExecuteCommandBuffer(c);
            }
        });
        Try("fence.async.stage", () => { using (var c = new CommandBuffer()) c.CreateAsyncGraphicsFence(SynchronisationStage.VertexProcessing); });
        // CPU synchronisation fences: before their buffer runs, once the GPU is done, never run.
        GraphicsFence cpu = default, cpuUnexecuted = default, cpuImmediate = default;
        Try("fence.cpu", () =>
        {
            using (var c = new CommandBuffer())
            {
                c.SetRenderTarget(target);
                c.ClearRenderTarget(false, true, Color.green);
                cpu = c.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
                Value("fence.cpu.recorded", B(cpu.passed));
                Graphics.ExecuteCommandBuffer(c);
            }
        });
        Try("fence.cpu.completed", () =>
        {
            AsyncGPUReadback.Request(target).WaitForCompletion();
            Value("fence.cpu.completed.passed", B(cpu.passed));
        });
        Try("fence.cpu.unexecuted", () =>
        {
            cpuUnexecuted = unexecutedBuffer.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.ComputeProcessing);
            Value("fence.cpu.unexecuted.passed", B(cpuUnexecuted.passed));
        });
        Try("fence.cpu.wait", () => { using (var c = new CommandBuffer()) { c.WaitOnAsyncGraphicsFence(cpu); Graphics.ExecuteCommandBuffer(c); } });
        Try("fence.wait", () => { using (var c = new CommandBuffer()) { c.WaitOnAsyncGraphicsFence(f); Graphics.ExecuteCommandBuffer(c); } });
        Try("fence.wait.stage", () => { using (var c = new CommandBuffer()) { c.WaitOnAsyncGraphicsFence(f, SynchronisationStage.PixelProcessing); Graphics.ExecuteCommandBuffer(c); } });
        Try("fence.wait.flags", () => { using (var c = new CommandBuffer()) { c.WaitOnAsyncGraphicsFence(f, SynchronisationStageFlags.VertexProcessing); Graphics.ExecuteCommandBuffer(c); } });
        Try("fence.wait.default", () => { using (var c = new CommandBuffer()) { c.WaitOnAsyncGraphicsFence(default(GraphicsFence)); Graphics.ExecuteCommandBuffer(c); } });
        Try("fence.graphics", () =>
        {
            immediate = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.PixelProcessing);
            Graphics.WaitOnAsyncGraphicsFence(immediate);
            Graphics.WaitOnAsyncGraphicsFence(immediate, SynchronisationStage.PixelProcessing);
        });
        Try("fence.graphics.async", () => Graphics.WaitOnAsyncGraphicsFence(Graphics.CreateAsyncGraphicsFence()));
        Try("fence.graphics.cpu", () =>
        {
            cpuImmediate = Graphics.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
            AsyncGPUReadback.Request(target).WaitForCompletion();
            Value("fence.graphics.cpu.completed", B(cpuImmediate.passed));
        });
        Try("fence.unexecuted", () => unexecuted = unexecutedBuffer.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.PixelProcessing));
        yield return null;
        Try("fence.frame1.cpu", () => Value("fence.frame1.cpu.passed", B(cpu.passed)));
        yield return null;
        Try("fence.frame2.cpu", () => Value("fence.frame2.cpu.passed", B(cpu.passed)));
        yield return null;
        Try("fence.frames", () =>
        {
            Value("fence.frames.executed", B(f.passed));
            Value("fence.frames.async", B(asyncFence.passed));
            Value("fence.frames.immediate", B(immediate.passed));
            Value("fence.frames.unexecuted", B(unexecuted.passed));
        });
        Try("fence.frames.cpu", () =>
        {
            Value("fence.frames.cpu.executed", B(cpu.passed));
            Value("fence.frames.cpu.immediate", B(cpuImmediate.passed));
            Value("fence.frames.cpu.unexecuted", B(cpuUnexecuted.passed));
        });
        cmd.Release();
        unexecutedBuffer.Release();
        Try("fence.released", () => Value("fence.released.passed", B(f.passed)));
        Try("fence.released.cpu", () => Value("fence.released.cpu.passed", B(cpuUnexecuted.passed)));
        Try("fence.released.unexecuted", () => Value("fence.released.unexecuted.passed", B(unexecuted.passed)));
    }

    // ------------------------------------------------------------ ConvertTexture

    static Texture2D Source(int w, int h, TextureFormat format = TextureFormat.RGBA32, bool linear = false)
    {
        var t = new Texture2D(w, h, format, false, linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)(20 + 37 * i), (byte)(200 - 23 * i), (byte)(64 + 11 * i), (byte)(255 - 9 * i));
        t.SetPixels32(px);
        t.Apply(false);
        return t;
    }

    static string Read(Texture t, int mip = 0)
    {
        var request = AsyncGPUReadback.Request(t, mip);
        request.WaitForCompletion();
        if (request.hasError) return Q("error");
        var s = new StringBuilder("[");
        for (int layer = 0; layer < request.layerCount; layer++)
        {
            if (layer > 0) s.Append(',');
            s.Append(Hex(request.GetData<byte>(layer)));
        }
        return s.Append(']').ToString();
    }

    void Convert(string key, Texture src, Texture dst, Action<CommandBuffer> record = null, bool read = true)
    {
        Try(key, () => Execute(cmd => { if (record != null) record(cmd); else cmd.ConvertTexture(src, dst); }));
        if (read) Value(key, Read(dst));
    }

    void ConvertTextures()
    {
        var src = Keep(Source(4, 4));
        Convert("convert.half", src, Keep(new Texture2D(4, 4, TextureFormat.RGBAHalf, false, true)));
        Convert("convert.float", src, Keep(new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true)));
        Convert("convert.r8", src, Keep(new Texture2D(4, 4, TextureFormat.R8, false, true)));
        Convert("convert.linear", src, Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false, true)));
        Convert("convert.srgb", Keep(Source(4, 4, linear: true)), Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false, false)));
        Convert("convert.bgra", src, Keep(new Texture2D(4, 4, TextureFormat.BGRA32, false, false)), read: false);
        Convert("convert.up", Keep(Source(2, 2)), Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false, false)));
        Convert("convert.rgb24", src, Keep(new Texture2D(4, 4, TextureFormat.RGB24, false, false)));
        Convert("convert.compressed", src, Keep(new Texture2D(4, 4, TextureFormat.DXT1, false, false)), read: false);

        // Elements: a cubemap face into an array slice, a 2D texture into a cubemap face.
        var cube = Keep(new Cubemap(2, TextureFormat.RGBA32, false));
        for (int face = 0; face < 6; face++)
            cube.SetPixels(Enumerable.Repeat(new Color(face / 5f, 1 - face / 5f, .5f, 1), 4).ToArray(), (CubemapFace)face);
        cube.Apply(false);
        var array = Keep(new Texture2DArray(2, 2, 3, TextureFormat.RGBA32, false, false));
        array.SetPixels32(Enumerable.Repeat(new Color32(1, 2, 3, 4), 4).ToArray(), 0);
        array.SetPixels32(Enumerable.Repeat(new Color32(1, 2, 3, 4), 4).ToArray(), 1);
        array.SetPixels32(Enumerable.Repeat(new Color32(1, 2, 3, 4), 4).ToArray(), 2);
        array.Apply(false);
        Convert("convert.cubeface", null, array, cmd => cmd.ConvertTexture(cube, (int)CubemapFace.NegativeX, array, 2));
        var small = Keep(Source(2, 2));
        Convert("convert.toslice", null, array, cmd => cmd.ConvertTexture(small, 0, array, 1));
        var cubeDst = Keep(new Cubemap(2, TextureFormat.RGBA32, false));
        for (int face = 0; face < 6; face++) cubeDst.SetPixels(Enumerable.Repeat(new Color(0, 0, 0, 0), 4).ToArray(), (CubemapFace)face);
        cubeDst.Apply(false);
        Convert("convert.toface", null, cubeDst, cmd => cmd.ConvertTexture(Keep(Source(2, 2)), 0, cubeDst, (int)CubemapFace.PositiveZ));
        var flat = Keep(new Texture2D(2, 2, TextureFormat.RGBA32, false, false));
        flat.SetPixels32(Enumerable.Repeat(new Color32(5, 6, 7, 8), 4).ToArray());
        flat.Apply(false);
        Convert("convert.cubeto2d", null, flat, cmd => cmd.ConvertTexture(cube, 1, flat, 0));
        Convert("convert.element.out", null, array, cmd => cmd.ConvertTexture(src, 0, array, 3));
        Convert("convert.cubetocube", null, cubeDst, cmd => cmd.ConvertTexture(cube, (int)CubemapFace.NegativeX, cubeDst, (int)CubemapFace.PositiveY));
        var cubeDst2 = Keep(new Cubemap(2, TextureFormat.RGBA32, false));
        for (int face = 0; face < 6; face++) cubeDst2.SetPixels(Enumerable.Repeat(new Color(0, 0, 0, 0), 4).ToArray(), (CubemapFace)face);
        cubeDst2.Apply(false);
        Convert("convert.cubetocube.z", null, cubeDst2, cmd => cmd.ConvertTexture(cube, (int)CubemapFace.PositiveZ, cubeDst2, (int)CubemapFace.NegativeZ));
        var cubes = Keep(new CubemapArray(2, 2, TextureFormat.RGBA32, false));
        for (int i = 0; i < 12; i++) cubes.SetPixels32(Enumerable.Repeat(new Color32(3, 3, 3, 3), 4).ToArray(), (CubemapFace)(i % 6), i / 6);
        cubes.Apply(false);
        Convert("convert.tocubearray", null, cubes, cmd => cmd.ConvertTexture(small, 0, cubes, 7));

        // Render textures on either side.
        var rt = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32));
        rt.Create();
        Graphics.Blit(src, rt);
        Convert("convert.rtsource", rt, Keep(new Texture2D(4, 4, TextureFormat.RGBAHalf, false, true)));
        var rtDst = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf));
        rtDst.Create();
        Convert("convert.rtdest", src, rtDst);
        Try("convert.null", () => Execute(cmd => cmd.ConvertTexture((Texture)null, src)));
        Try("convert.builtin", () => Execute(cmd => cmd.ConvertTexture(src, BuiltinRenderTextureType.CameraTarget)));

        // Graphics.ConvertTexture's answers for the same cases.
        Try("graphics.convert", () =>
        {
            var dst = Keep(new Texture2D(4, 4, TextureFormat.RGBAHalf, false, true));
            Value("graphics.convert.half.result", B(Graphics.ConvertTexture(src, dst)));
            Value("graphics.convert.half", Read(dst));
            var r24 = Keep(new Texture2D(4, 4, TextureFormat.RGB24, false, false));
            Value("graphics.convert.rgb24.result", B(Graphics.ConvertTexture(src, r24)));
            var dxt = Keep(new Texture2D(4, 4, TextureFormat.DXT1, false, false));
            Value("graphics.convert.compressed.result", B(Graphics.ConvertTexture(src, dxt)));
            var rtd = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf));
            rtd.Create();
            Value("graphics.convert.rtdest.result", B(Graphics.ConvertTexture(src, rtd)));
            Value("graphics.convert.rtdest", Read(rtd));
            var slice = Keep(new Texture2DArray(2, 2, 3, TextureFormat.RGBA32, false, false));
            slice.SetPixels32(Enumerable.Repeat(new Color32(1, 2, 3, 4), 4).ToArray(), 0);
            slice.SetPixels32(Enumerable.Repeat(new Color32(1, 2, 3, 4), 4).ToArray(), 1);
            slice.SetPixels32(Enumerable.Repeat(new Color32(1, 2, 3, 4), 4).ToArray(), 2);
            slice.Apply(false);
            Value("graphics.convert.cubeface.result", B(Graphics.ConvertTexture(cube, (int)CubemapFace.NegativeY, slice, 1)));
            Value("graphics.convert.cubeface", Read(slice));
        });
    }

    // ------------------------------------------------------------ GetTemporaryRTArray

    static string Describe(Texture t)
    {
        if (!(t is RenderTexture rt)) return Q(t == null ? "null" : t.GetType().Name);
        return "{\"dimension\":" + Q(rt.dimension.ToString()) + ",\"width\":" + rt.width + ",\"height\":" + rt.height
            + ",\"volumeDepth\":" + rt.volumeDepth + ",\"format\":" + Q(rt.graphicsFormat.ToString())
            + ",\"depthStencil\":" + Q(rt.depthStencilFormat.ToString()) + ",\"filter\":" + Q(rt.filterMode.ToString())
            + ",\"msaa\":" + rt.antiAliasing + ",\"randomWrite\":" + B(rt.enableRandomWrite) + ",\"dynamic\":" + B(rt.useDynamicScale)
            + ",\"mips\":" + B(rt.useMipMap) + ",\"srgb\":" + B(rt.sRGB) + "}";
    }

    void TemporaryArrays()
    {
        int id = Shader.PropertyToID("_ProbeArray");
        void Desc(string key, Action<CommandBuffer> get)
        {
            Shader.SetGlobalTexture(id, Texture2D.blackTexture);
            Try(key, () => Execute(get));
            Value(key, Describe(Shader.GetGlobalTexture(id)));
        }
        Desc("tarray.short", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 3));
        Desc("tarray.depth", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 2, 24));
        Desc("tarray.filter", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 2, 16, FilterMode.Bilinear));
        Desc("tarray.rtformat", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 2, 0, FilterMode.Point, RenderTextureFormat.RFloat));
        Desc("tarray.readwrite", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 2, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        Desc("tarray.rtfull", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 2, 0, FilterMode.Trilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, 1, true));
        Desc("tarray.graphics", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 2, 0, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat));
        Desc("tarray.graphicsfull", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 2, 0, FilterMode.Bilinear, GraphicsFormat.R32_SFloat, 1, true, false));
        Desc("tarray.one", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 1));
        Desc("tarray.zero", cmd => cmd.GetTemporaryRTArray(id, 4, 2, 0));
        Desc("tarray.again", cmd => { cmd.GetTemporaryRTArray(id, 4, 2, 3); cmd.GetTemporaryRTArray(id, 2, 2, 2); });
        Desc("tarray.released", cmd => { cmd.GetTemporaryRTArray(id, 4, 2, 3); cmd.ReleaseTemporaryRT(id); });

        // Slices drawn into, copied out of the temporary while it lives.
        var copy = Keep(new Texture2DArray(2, 2, 2, TextureFormat.RGBA32, false, false));
        copy.SetPixels32(Enumerable.Repeat(new Color32(9, 9, 9, 9), 4).ToArray(), 0);
        copy.SetPixels32(Enumerable.Repeat(new Color32(9, 9, 9, 9), 4).ToArray(), 1);
        copy.Apply(false);
        Try("tarray.slices", () => Execute(cmd =>
        {
            cmd.GetTemporaryRTArray(id, 2, 2, 3);
            cmd.SetRenderTarget(new RenderTargetIdentifier(id), 0, CubemapFace.Unknown, 1);
            cmd.ClearRenderTarget(false, true, new Color(1, 0, 0, 1));
            cmd.SetRenderTarget(new RenderTargetIdentifier(id), 0, CubemapFace.Unknown, 2);
            cmd.ClearRenderTarget(false, true, new Color(0, 1, 0, .5f));
            cmd.CopyTexture(id, 1, copy, 0);
            cmd.CopyTexture(id, 2, copy, 1);
            cmd.ReleaseTemporaryRT(id);
        }));
        Value("tarray.slices", Read(copy));
        var copy2 = Keep(new Texture2DArray(2, 2, 2, TextureFormat.RGBA32, false, false));
        copy2.SetPixels32(Enumerable.Repeat(new Color32(9, 9, 9, 9), 4).ToArray(), 0);
        copy2.SetPixels32(Enumerable.Repeat(new Color32(9, 9, 9, 9), 4).ToArray(), 1);
        copy2.Apply(false);
        var small2 = Keep(Source(2, 2));
        Try("tarray.blit", () => Execute(cmd =>
        {
            cmd.GetTemporaryRTArray(id, 2, 2, 2);
            cmd.Blit(small2, id, 0, 1);
            cmd.CopyTexture(id, 1, copy2, 1);
        }));
        Value("tarray.blit", Read(copy2));

        // Sizes relative to a camera's.
        var go = new GameObject("ProbeCamera");
        made.Add(go);
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.cullingMask = 0;
        var camTarget = Keep(new RenderTexture(8, 6, 16, RenderTextureFormat.ARGB32));
        camTarget.Create();
        cam.targetTexture = camTarget;
        int id2 = Shader.PropertyToID("_ProbeArrayHalf");
        var events = new CommandBuffer { name = "probe camera" };
        events.GetTemporaryRTArray(id, -1, -1, 2);
        events.GetTemporaryRTArray(id2, -2, -3, 2);
        cam.AddCommandBuffer(CameraEvent.AfterEverything, events);
        Shader.SetGlobalTexture(id, Texture2D.blackTexture);
        Shader.SetGlobalTexture(id2, Texture2D.blackTexture);
        Try("tarray.camera", () => cam.Render());
        Value("tarray.camera.full", Describe(Shader.GetGlobalTexture(id)));
        Value("tarray.camera.divided", Describe(Shader.GetGlobalTexture(id2)));
        cam.RemoveAllCommandBuffers();
        events.Release();
    }

    // ------------------------------------------------------------ readbacks into slices

    struct Pair { public uint a, b; }

    void Slices()
    {
        var buffer = new ComputeBuffer(4, 4);
        disposables.Add(buffer);
        buffer.SetData(new uint[] { 11, 22, 33, 44 });
        var gbuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
        disposables.Add(gbuffer);
        gbuffer.SetData(new uint[] { 55, 66, 77, 88 });

        NativeArray<uint> Words8()
        {
            var a = new NativeArray<uint>(8, Allocator.Persistent);
            for (int i = 0; i < a.Length; i++) a[i] = 0xEEu;
            disposables.Add(a);
            return a;
        }
        void Wait(string key, AsyncGPUReadbackRequest r)
        {
            r.WaitForCompletion();
            Value(key + ".state", "{\"done\":" + B(r.done) + ",\"error\":" + B(r.hasError) + "}");
        }

        var a1 = Words8();
        Try("slice.buffer", () => { var s = new NativeSlice<uint>(a1, 2, 4); Wait("slice.buffer", AsyncGPUReadback.RequestIntoNativeSlice(ref s, buffer)); });
        Value("slice.buffer", U(a1));
        var a2 = Words8();
        Try("slice.range", () => { var s = new NativeSlice<uint>(a2, 5, 2); Wait("slice.range", AsyncGPUReadback.RequestIntoNativeSlice(ref s, buffer, 8, 4)); });
        Value("slice.range", U(a2));
        var a3 = Words8();
        Try("slice.graphics", () => { var s = new NativeSlice<uint>(a3, 1, 4); Wait("slice.graphics", AsyncGPUReadback.RequestIntoNativeSlice(ref s, gbuffer)); });
        Value("slice.graphics", U(a3));
        var a4 = Words8();
        Try("slice.small", () => { var s = new NativeSlice<uint>(a4, 0, 3); Wait("slice.small", AsyncGPUReadback.RequestIntoNativeSlice(ref s, buffer)); });
        Value("slice.small", U(a4));
        var a5 = Words8();
        Try("slice.larger", () => { var s = new NativeSlice<uint>(a5, 0, 6); Wait("slice.larger", AsyncGPUReadback.RequestIntoNativeSlice(ref s, buffer)); });
        Value("slice.larger", U(a5));

        // A strided slice (every other word).
        var pairs = new NativeArray<Pair>(4, Allocator.Persistent);
        disposables.Add(pairs);
        for (int i = 0; i < 4; i++) pairs[i] = new Pair { a = 0xEE, b = 0xDD };
        Try("slice.strided", () => { var s = pairs.Slice().SliceWithStride<uint>(0); Wait("slice.strided", AsyncGPUReadback.RequestIntoNativeSlice(ref s, buffer)); });
        Value("slice.strided", U(pairs.SelectMany(p => new[] { p.a, p.b })));

        // Textures.
        var tex = Keep(Source(2, 2));
        var bytes = new NativeArray<byte>(24, Allocator.Persistent);
        disposables.Add(bytes);
        Try("slice.texture", () => { var s = new NativeSlice<byte>(bytes, 4, 16); Wait("slice.texture", AsyncGPUReadback.RequestIntoNativeSlice(ref s, tex)); });
        Value("slice.texture", Hex(bytes));
        var bytes2 = new NativeArray<byte>(24, Allocator.Persistent);
        disposables.Add(bytes2);
        Try("slice.region", () => { var s = new NativeSlice<byte>(bytes2, 2, 8); Wait("slice.region", AsyncGPUReadback.RequestIntoNativeSlice(ref s, tex, 0, 1, 1, 0, 2, 0, 1, TextureFormat.RGBA32)); });
        Value("slice.region", Hex(bytes2));
        var bytes3 = new NativeArray<byte>(40, Allocator.Persistent);
        disposables.Add(bytes3);
        Try("slice.format", () => { var s = new NativeSlice<byte>(bytes3, 4, 32); Wait("slice.format", AsyncGPUReadback.RequestIntoNativeSlice(ref s, tex, 0, GraphicsFormat.R16G16B16A16_SFloat)); });
        Value("slice.format", Hex(bytes3));

        // Command buffer requests: written when the command runs.
        var a6 = Words8();
        var a7 = Words8();
        var bytes4 = new NativeArray<byte>(24, Allocator.Persistent);
        disposables.Add(bytes4);
        var calls = new List<string>();
        Try("cmd.slice", () =>
        {
            var s6 = new NativeSlice<uint>(a6, 3, 4);
            var s7 = new NativeSlice<uint>(a7, 4, 2);
            var s8 = new NativeSlice<byte>(bytes4, 8, 16);
            using (var cmd = new CommandBuffer())
            {
                cmd.RequestAsyncReadbackIntoNativeSlice(ref s6, buffer, r => calls.Add("buffer:" + B(r.hasError)));
                cmd.RequestAsyncReadbackIntoNativeSlice(ref s7, gbuffer, 8, 8, r => calls.Add("range:" + B(r.hasError)));
                cmd.RequestAsyncReadbackIntoNativeSlice(ref s8, tex, r => calls.Add("texture:" + B(r.hasError)));
                buffer.SetData(new uint[] { 1, 2, 3, 4 });
                Graphics.ExecuteCommandBuffer(cmd);
                buffer.SetData(new uint[] { 5, 6, 7, 8 });
            }
            AsyncGPUReadback.WaitAllRequests();
        });
        Value("cmd.slice.buffer", U(a6));
        Value("cmd.slice.range", U(a7));
        Value("cmd.slice.texture", Hex(bytes4));
        Value("cmd.slice.calls", "[" + string.Join(",", calls.Select(Q)) + "]");
    }
}
