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
/// GPU buffers and readbacks: Append, Consume and Counter buffers (CopyCount, SetCounterValue),
/// indirect dispatches, buffer copies, command-buffer buffer data, SetData/GetData overloads and
/// AsyncGPUReadback of buffers and textures, with the frame each request completes and calls back.
/// Every case records its values, exceptions and log messages. Buffer contents written by
/// Append/Counter kernels are recorded sorted (their order is the GPU's).
/// </summary>
public class GpuBuffersProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    const uint Sentinel = 0xAAAA0000u;

    readonly StringBuilder records = new StringBuilder();
    /// <summary>The exact frames of readback events (timing.jsonl): when a request completes
    /// depends on the GPU, so values.jsonl only tells the request's frame from a later one.</summary>
    readonly StringBuilder timing = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<Watch> watches = new List<Watch>();
    bool capturing;
    string directory;
    int frame0;
    ComputeShader cs;
    int kAppend, kConsume, kCount, kUncount, kGroups, kPair;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("GPUBUFFERS_PROBE_OUT") == null) return;
        new GameObject("GpuBuffersProbe").AddComponent<GpuBuffersProbe>();
    }

    int Frame => Time.frameCount - frame0;

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string U(IEnumerable<uint> values) => "[" + string.Join(",", values) + "]";
    static string Sorted(uint[] values) => U(values.OrderBy(v => v));
    static string Hex(NativeArray<byte> data)
    {
        var s = new StringBuilder(data.Length * 2);
        for (int i = 0; i < data.Length; i++) s.Append(data[i].ToString("x2"));
        return Q(s.ToString());
    }
    static string Error(Exception e) => Q(e.GetType().Name + ": " + e.Message);

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
        catch (Exception e) { Value(key + ".error", Error(e)); }
        capturing = false;
        if (logs.Count > 0) Value(key + ".log", "[" + string.Join(",", logs.Select(Q)) + "]");
        logs.Clear();
    }

    void Start()
    {
        directory = Environment.GetEnvironmentVariable("GPUBUFFERS_PROBE_OUT");
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        int code = 0;
        yield return null;
        IEnumerator steps = null;
        try
        {
            Debug.Log("GPUBUFFERS_DEVICE " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName + " " + SystemInfo.graphicsDeviceVersion);
            Value("caps", "{\"computeShaders\":" + B(SystemInfo.supportsComputeShaders) + ",\"asyncReadback\":" + B(SystemInfo.supportsAsyncGPUReadback) + "}");
            cs = Resources.Load<ComputeShader>("BufferProbe");
            kAppend = cs.FindKernel("Append");
            kConsume = cs.FindKernel("Consume");
            kCount = cs.FindKernel("Count");
            kUncount = cs.FindKernel("Uncount");
            kGroups = cs.FindKernel("Groups");
            kPair = cs.FindKernel("AppendPair");
            Append();
            Consume();
            Counter();
            Indirect();
            Copy();
            CommandBufferData();
            SetGetData();
            steps = Readbacks();
        }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 1;
        }
        while (steps != null)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; break; }
            if (!more) break;
            yield return steps.Current;
        }
        Value("complete", B(code == 0));
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());
        File.WriteAllText(Path.Combine(directory, "timing.jsonl"), timing.ToString());
        Debug.Log("GPUBUFFERS_DONE " + directory + " " + code);
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

    // ------------------------------------------------------------ helpers

    static uint[] Fill(int n, Func<int, uint> f)
    {
        var a = new uint[n];
        for (int i = 0; i < n; i++) a[i] = f(i);
        return a;
    }

    static uint[] Read(ComputeBuffer b)
    {
        var a = new uint[b.count * b.stride / 4];
        b.GetData(a);
        return a;
    }

    static uint[] Read(GraphicsBuffer b)
    {
        var a = new uint[b.count * b.stride / 4];
        b.GetData(a);
        return a;
    }

    /// <summary>The hidden counter of an Append/Counter buffer, through CopyCount into a fresh
    /// argument buffer of sentinels (all four words recorded).</summary>
    string Count(ComputeBuffer b)
    {
        using var args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
        args.SetData(Fill(4, i => 0xFFFFFFFFu));
        ComputeBuffer.CopyCount(b, args, 0);
        return U(Read(args));
    }

    string Count(GraphicsBuffer b)
    {
        using var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 4, 4);
        args.SetData(Fill(4, i => 0xFFFFFFFFu));
        GraphicsBuffer.CopyCount(b, args, 0);
        return U(Read(args));
    }

    void RunAppend(ComputeBuffer b, uint modulo, uint baseValue, int groups = 1)
    {
        cs.SetInt("_Modulo", (int)modulo);
        cs.SetInt("_Base", (int)baseValue);
        cs.SetBuffer(kAppend, "_Append", b);
        cs.Dispatch(kAppend, groups, 1, 1);
    }

    void RunAppend(GraphicsBuffer b, uint modulo, uint baseValue)
    {
        cs.SetInt("_Modulo", (int)modulo);
        cs.SetInt("_Base", (int)baseValue);
        cs.SetBuffer(kAppend, "_Append", b);
        cs.Dispatch(kAppend, 1, 1, 1);
    }

    // ------------------------------------------------------------ append / consume / counter

    void Append()
    {
        var b = new ComputeBuffer(64, 4, ComputeBufferType.Append);
        b.SetData(Fill(64, i => Sentinel + (uint)i));
        Try("append.cb.created", () => Value("append.cb.created", Count(b)));
        Try("append.cb.first", () => { RunAppend(b, 3, 0); Value("append.cb.first.count", Count(b)); Value("append.cb.first.data", Sorted(Read(b))); });
        Try("append.cb.again", () => { RunAppend(b, 3, 1000); Value("append.cb.again.count", Count(b)); Value("append.cb.again.data", Sorted(Read(b))); });
        Try("append.cb.reset", () => { b.SetCounterValue(0); RunAppend(b, 8, 2000); Value("append.cb.reset.count", Count(b)); Value("append.cb.reset.data", Sorted(Read(b))); });
        Try("append.cb.start10", () => { b.SetCounterValue(10); RunAppend(b, 16, 3000); Value("append.cb.start10.count", Count(b)); Value("append.cb.start10.data", Sorted(Read(b))); });
        Try("append.cb.setdata", () => { b.SetData(Fill(64, i => Sentinel + 0x100u + (uint)i)); Value("append.cb.setdata.count", Count(b)); });
        Try("append.cb.set70", () => { b.SetCounterValue(70); Value("append.cb.set70.count", Count(b)); });
        Try("append.cb.copyoffset", () =>
        {
            b.SetCounterValue(9);
            using var args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
            args.SetData(Fill(4, i => 0xFFFFFFFFu));
            ComputeBuffer.CopyCount(b, args, 8);
            Value("append.cb.copyoffset", U(Read(args)));
        });
        Try("append.cb.copyunaligned", () =>
        {
            using var args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
            args.SetData(Fill(4, i => 0xFFFFFFFFu));
            ComputeBuffer.CopyCount(b, args, 2);
            Value("append.cb.copyunaligned", U(Read(args)));
        });
        Try("append.cb.copybeyond", () =>
        {
            using var args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
            args.SetData(Fill(4, i => 0xFFFFFFFFu));
            ComputeBuffer.CopyCount(b, args, 16);
            Value("append.cb.copybeyond", U(Read(args)));
        });
        Try("append.cb.copyraw", () =>
        {
            using var raw = new ComputeBuffer(4, 4, ComputeBufferType.Raw);
            raw.SetData(Fill(4, i => 0xFFFFFFFFu));
            ComputeBuffer.CopyCount(b, raw, 4);
            Value("append.cb.copyraw", U(Read(raw)));
        });
        Try("append.cb.copydefault", () =>
        {
            using var plain = new ComputeBuffer(4, 4);
            plain.SetData(Fill(4, i => 0xFFFFFFFFu));
            ComputeBuffer.CopyCount(b, plain, 0);
            Value("append.cb.copydefault", U(Read(plain)));
        });
        b.Release();

        var g = new GraphicsBuffer(GraphicsBuffer.Target.Append, 64, 4);
        g.SetData(Fill(64, i => Sentinel + (uint)i));
        Try("append.gb.first", () => { RunAppend(g, 4, 0); Value("append.gb.first.count", Count(g)); Value("append.gb.first.data", Sorted(Read(g))); });
        Try("append.gb.start5", () => { g.SetCounterValue(5); RunAppend(g, 32, 500); Value("append.gb.start5.count", Count(g)); Value("append.gb.start5.data", Sorted(Read(g))); });
        Try("append.gb.mixed", () =>
        {
            using var args = new ComputeBuffer(2, 4, ComputeBufferType.IndirectArguments);
            args.SetData(Fill(2, i => 0xFFFFFFFFu));
            GraphicsBuffer.CopyCount(g, args, 4);
            Value("append.gb.mixed", U(Read(args)));
        });
        g.Release();

        Try("append.cbgroups", () =>
        {
            using var big = new ComputeBuffer(256, 4, ComputeBufferType.Append);
            big.SetData(Fill(256, i => Sentinel + (uint)i));
            big.SetCounterValue(0);
            RunAppend(big, 5, 0, 3);
            Value("append.cbgroups.count", Count(big));
            Value("append.cbgroups.data", Sorted(Read(big)));
        });

        Try("append.pair", () =>
        {
            using var pairs = new ComputeBuffer(16, 8, ComputeBufferType.Append);
            pairs.SetData(Fill(32, i => Sentinel + (uint)i));
            pairs.SetCounterValue(0);
            cs.SetInt("_Modulo", 4);
            cs.SetInt("_Base", 7);
            cs.SetBuffer(kPair, "_AppendPair", pairs);
            cs.Dispatch(kPair, 1, 1, 1);
            Value("append.pair.count", Count(pairs));
            var words = Read(pairs);
            var list = new List<string>();
            for (int i = 0; i < words.Length; i += 2) list.Add(words[i] + ":" + words[i + 1]);
            list.Sort(StringComparer.Ordinal);
            Value("append.pair.data", "[" + string.Join(",", list.Select(Q)) + "]");
        });

        Try("append.boundonly", () =>
        {
            // Bound to a kernel, never dispatched.
            using var fresh = new ComputeBuffer(8, 4, ComputeBufferType.Append);
            cs.SetBuffer(kAppend, "_Append", fresh);
            fresh.SetCounterValue(4);
            Value("append.boundonly", Count(fresh));
        });
        Try("append.plainbind", () =>
        {
            // Dispatched as a plain RW buffer of a kernel that has no counter.
            using var fresh = new ComputeBuffer(40, 4, ComputeBufferType.Append);
            fresh.SetData(new uint[40]);
            cs.SetInt("_Base", 0);
            cs.SetBuffer(kGroups, "_Out", fresh);
            cs.Dispatch(kGroups, 1, 1, 1);
            fresh.SetCounterValue(5);
            Value("append.plainbind", Count(fresh));
        });
        Try("append.afterfirst", () =>
        {
            // A dispatch, then the count, a new value and the count again.
            using var fresh = new ComputeBuffer(64, 4, ComputeBufferType.Append);
            fresh.SetCounterValue(3);
            RunAppend(fresh, 32, 0);
            Value("append.afterfirst.count", Count(fresh));
            fresh.SetCounterValue(11);
            Value("append.afterfirst.set", Count(fresh));
        });
        Try("append.default.copycount", () =>
        {
            using var plain = new ComputeBuffer(8, 4);
            plain.SetData(Fill(8, i => (uint)i));
            Value("append.default.copycount", Count(plain));
        });
        Try("append.default.setcounter", () =>
        {
            using var plain = new ComputeBuffer(8, 4);
            plain.SetData(Fill(8, i => (uint)i));
            plain.SetCounterValue(3);
            Value("append.default.setcounter.data", U(Read(plain)));
        });
        Try("append.gbdefault.copycount", () =>
        {
            using var plain = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
            Value("append.gbdefault.copycount", Count(plain));
        });
    }

    void Consume()
    {
        Try("consume", () =>
        {
            using var b = new ComputeBuffer(64, 4, ComputeBufferType.Append);
            using var output = new ComputeBuffer(8, 4);
            b.SetData(Fill(64, i => 100u + (uint)i));
            output.SetData(Fill(8, i => Sentinel));
            b.SetCounterValue(16);
            cs.SetBuffer(kConsume, "_Consume", b);
            cs.SetBuffer(kConsume, "_Out", output);
            cs.Dispatch(kConsume, 1, 1, 1);
            Value("consume.out", Sorted(Read(output)));
            Value("consume.count", Count(b));
            cs.Dispatch(kConsume, 1, 1, 1);
            Value("consume.again.out", Sorted(Read(output)));
            Value("consume.again.count", Count(b));
        });
    }

    void RunCount(ComputeBuffer b, uint baseValue)
    {
        cs.SetInt("_Base", (int)baseValue);
        cs.SetBuffer(kCount, "_Counter", b);
        cs.Dispatch(kCount, 1, 1, 1);
    }

    void Counter()
    {
        var b = new ComputeBuffer(32, 4, ComputeBufferType.Counter);
        b.SetData(Fill(32, i => Sentinel + (uint)i));
        Try("counter.created", () => Value("counter.created", Count(b)));
        Try("counter.first", () => { RunCount(b, 1000); Value("counter.first.count", Count(b)); Value("counter.first.data", Sorted(Read(b))); });
        Try("counter.again", () => { RunCount(b, 2000); Value("counter.again.count", Count(b)); Value("counter.again.data", Sorted(Read(b))); });
        Try("counter.down", () =>
        {
            using var output = new ComputeBuffer(4, 4);
            output.SetData(Fill(4, i => Sentinel));
            cs.SetInt("_Base", 0);
            cs.SetBuffer(kUncount, "_Counter", b);
            cs.SetBuffer(kUncount, "_Out", output);
            cs.Dispatch(kUncount, 1, 1, 1);
            Value("counter.down.out", Sorted(Read(output)));
            Value("counter.down.count", Count(b));
        });
        Try("counter.set2", () => { b.SetCounterValue(2); RunCount(b, 3000); Value("counter.set2.count", Count(b)); Value("counter.set2.data", Sorted(Read(b))); });
        b.Release();

        Try("counter.gb", () =>
        {
            using var g = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter, 32, 4);
            g.SetData(Fill(32, i => Sentinel + (uint)i));
            cs.SetInt("_Base", 4000);
            cs.SetBuffer(kCount, "_Counter", g);
            cs.Dispatch(kCount, 1, 1, 1);
            Value("counter.gb.count", Count(g));
            Value("counter.gb.data", Sorted(Read(g)));
            g.SetCounterValue(1);
            Value("counter.gb.set1", Count(g));
        });
        Try("counter.cbtype", () =>
        {
            using var c = new ComputeBuffer(4, 4, ComputeBufferType.Counter | ComputeBufferType.Structured);
            c.SetCounterValue(3);
            Value("counter.cbtype", Count(c));
        });
    }

    // ------------------------------------------------------------ indirect dispatch

    uint[] RunGroups(Action dispatch, uint baseValue = 0)
    {
        using var output = new ComputeBuffer(40, 4);
        output.SetData(new uint[40]);
        cs.SetInt("_Base", (int)baseValue);
        cs.SetBuffer(kGroups, "_Out", output);
        dispatch();
        return Read(output);
    }

    void Indirect()
    {
        using var args = new ComputeBuffer(6, 4, ComputeBufferType.IndirectArguments);
        args.SetData(new uint[] { 2, 1, 1, 3, 2, 2 });
        Try("indirect.cb", () => Value("indirect.cb", U(RunGroups(() => cs.DispatchIndirect(kGroups, args)))));
        Try("indirect.cb.offset", () => Value("indirect.cb.offset", U(RunGroups(() => cs.DispatchIndirect(kGroups, args, 12)))));
        Try("indirect.cb.direct", () => Value("indirect.cb.direct", U(RunGroups(() => cs.Dispatch(kGroups, 3, 2, 2)))));
        Try("indirect.copycount", () =>
        {
            using var app = new ComputeBuffer(8, 4, ComputeBufferType.Append);
            using var a3 = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
            app.SetCounterValue(5);
            a3.SetData(new uint[] { 0, 1, 1 });
            ComputeBuffer.CopyCount(app, a3, 0);
            Value("indirect.copycount", U(RunGroups(() => cs.DispatchIndirect(kGroups, a3))));
        });
        Try("indirect.gb", () =>
        {
            using var g = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 6, 4);
            g.SetData(new uint[] { 4, 1, 1, 1, 3, 1 });
            Value("indirect.gb", U(RunGroups(() => cs.DispatchIndirect(kGroups, g))));
            Value("indirect.gb.offset", U(RunGroups(() => cs.DispatchIndirect(kGroups, g, 12))));
        });
        Try("indirect.structured", () =>
        {
            using var s = new ComputeBuffer(3, 4);
            s.SetData(new uint[] { 2, 1, 1 });
            Value("indirect.structured", U(RunGroups(() => cs.DispatchIndirect(kGroups, s))));
        });
        Try("indirect.gbstructured", () =>
        {
            using var s = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 4);
            s.SetData(new uint[] { 2, 1, 1 });
            Value("indirect.gbstructured", U(RunGroups(() => cs.DispatchIndirect(kGroups, s))));
        });
        Try("indirect.unaligned", () => Value("indirect.unaligned", U(RunGroups(() => cs.DispatchIndirect(kGroups, args, 2)))));
        Try("indirect.beyond", () => Value("indirect.beyond", U(RunGroups(() => cs.DispatchIndirect(kGroups, args, 16)))));
        Try("indirect.null", () => Value("indirect.null", U(RunGroups(() => cs.DispatchIndirect(kGroups, (ComputeBuffer)null)))));
        Try("indirect.zero", () =>
        {
            using var z = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
            z.SetData(new uint[] { 0, 1, 1 });
            Value("indirect.zero", U(RunGroups(() => cs.DispatchIndirect(kGroups, z))));
        });
        Try("indirect.gpu", () =>
        {
            // Arguments written by a kernel: an Append count of 3 copied in.
            using var app = new ComputeBuffer(64, 4, ComputeBufferType.Append);
            using var a3 = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
            app.SetCounterValue(0);
            RunAppend(app, 22, 0);
            a3.SetData(new uint[] { 0, 2, 1 });
            ComputeBuffer.CopyCount(app, a3, 0);
            Value("indirect.gpu", U(RunGroups(() => cs.DispatchIndirect(kGroups, a3))));
        });
        Try("indirect.cmd", () =>
        {
            Value("indirect.cmd", U(RunGroups(() =>
            {
                using var cmd = new CommandBuffer();
                cmd.DispatchCompute(cs, kGroups, args, 12);
                Graphics.ExecuteCommandBuffer(cmd);
            })));
        });
        Try("indirect.cmd.gb", () =>
        {
            using var g = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, 4);
            g.SetData(new uint[] { 2, 2, 1 });
            Value("indirect.cmd.gb", U(RunGroups(() =>
            {
                using var cmd = new CommandBuffer();
                cmd.DispatchCompute(cs, kGroups, g, 0);
                Graphics.ExecuteCommandBuffer(cmd);
            })));
        });
        Try("indirect.cmd.late", () =>
        {
            // The arguments change after recording: the command reads them when it runs.
            using var g = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
            g.SetData(new uint[] { 1, 1, 1 });
            using var cmd = new CommandBuffer();
            cmd.DispatchCompute(cs, kGroups, g, 0);
            g.SetData(new uint[] { 3, 1, 1 });
            Value("indirect.cmd.late", U(RunGroups(() => Graphics.ExecuteCommandBuffer(cmd))));
        });
    }

    // ------------------------------------------------------------ copies

    void Copy()
    {
        const GraphicsBuffer.Target S = GraphicsBuffer.Target.Structured;
        GraphicsBuffer Make(GraphicsBuffer.Target t, int count, int stride, uint first)
        {
            var g = new GraphicsBuffer(t, count, stride);
            g.SetData(Fill(count * stride / 4, i => first + (uint)i));
            return g;
        }
        void Case(string key, GraphicsBuffer.Target st, int sc, int ss, GraphicsBuffer.Target dt, int dc, int ds)
        {
            Try(key, () =>
            {
                using var a = Make(st, sc, ss, 1);
                using var b = Make(dt, dc, ds, Sentinel);
                Graphics.CopyBuffer(a, b);
                Value(key, U(Read(b)));
            });
        }
        const GraphicsBuffer.Target CS = GraphicsBuffer.Target.CopySource, CD = GraphicsBuffer.Target.CopyDestination;
        Case("copy.flags", S | CS, 16, 4, S | CD, 16, 4);
        Case("copy.noflags", S, 16, 4, S, 16, 4);
        Case("copy.nodest", S | CS, 16, 4, S, 16, 4);
        Case("copy.swapped", S | CD, 16, 4, S | CS, 16, 4);
        Case("copy.both", S | CS | CD, 16, 4, S | CS | CD, 16, 4);
        Case("copy.raw", GraphicsBuffer.Target.Raw | CS, 16, 4, GraphicsBuffer.Target.Raw | CD, 16, 4);
        Case("copy.stride", S | CS, 16, 4, S | CD, 8, 8);
        Case("copy.smaller", S | CS, 8, 4, S | CD, 16, 4);
        Case("copy.larger", S | CS, 16, 4, S | CD, 8, 4);
        Case("copy.vertex", GraphicsBuffer.Target.Vertex | CS, 16, 4, S | CD, 16, 4);
        Case("copy.index", GraphicsBuffer.Target.Index | CS, 16, 4, GraphicsBuffer.Target.Index | CD, 16, 4);
        Case("copy.args", GraphicsBuffer.Target.IndirectArguments | CS, 4, 4, S | CD, 4, 4);
        Case("copy.append", GraphicsBuffer.Target.Append | CS, 16, 4, S | CD, 16, 4);
        Case("copy.constant", GraphicsBuffer.Target.Constant | CS, 16, 4, S | CD, 16, 4);
        Try("copy.self", () =>
        {
            using var a = Make(S | CS | CD, 8, 4, 1);
            Graphics.CopyBuffer(a, a);
            Value("copy.self", U(Read(a)));
        });
        Try("copy.null", () =>
        {
            using var a = Make(S | CS, 8, 4, 1);
            Graphics.CopyBuffer(a, null);
        });
        Try("copy.nullsource", () =>
        {
            using var b = Make(S | CD, 8, 4, 1);
            Graphics.CopyBuffer(null, b);
        });
        Try("copy.released", () =>
        {
            var a = Make(S | CS, 8, 4, 1);
            using var b = Make(S | CD, 8, 4, Sentinel);
            a.Release();
            Graphics.CopyBuffer(a, b);
            Value("copy.released", U(Read(b)));
        });
        Try("copy.cmd", () =>
        {
            using var a = Make(S | CS, 8, 4, 1);
            using var b = Make(S | CD, 8, 4, Sentinel);
            using var cmd = new CommandBuffer();
            cmd.CopyBuffer(a, b);
            Value("copy.cmd.before", U(Read(b)));
            a.SetData(Fill(8, i => 50u + (uint)i));
            Graphics.ExecuteCommandBuffer(cmd);
            Value("copy.cmd.after", U(Read(b)));
        });
        Try("copy.cmd.nodest", () =>
        {
            using var a = Make(S | CS, 8, 4, 1);
            using var b = Make(S, 8, 4, Sentinel);
            using var cmd = new CommandBuffer();
            cmd.CopyBuffer(a, b);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("copy.cmd.nodest", U(Read(b)));
        });
        Try("copy.cmd.larger", () =>
        {
            using var a = Make(S | CS, 16, 4, 1);
            using var b = Make(S | CD, 8, 4, Sentinel);
            using var cmd = new CommandBuffer();
            cmd.CopyBuffer(a, b);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("copy.cmd.larger", U(Read(b)));
        });
    }

    // ------------------------------------------------------------ command buffer data

    void CommandBufferData()
    {
        GraphicsBuffer Gb() { var g = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4); g.SetData(Fill(8, i => Sentinel + (uint)i)); return g; }
        ComputeBuffer Cb() { var c = new ComputeBuffer(8, 4); c.SetData(Fill(8, i => Sentinel + (uint)i)); return c; }

        Try("cmddata.array", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            var data = Fill(8, i => 10u + (uint)i);
            cmd.SetBufferData(g, data);
            data[0] = 99;
            Value("cmddata.array.before", U(Read(g)));
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.array.after", U(Read(g)));
            Graphics.ExecuteCommandBuffer(cmd);
            g.SetData(Fill(8, i => 1u));
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.array.twice", U(Read(g)));
        });
        Try("cmddata.range", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            cmd.SetBufferData(g, Fill(8, i => 10u + (uint)i), 2, 4, 3);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.range", U(Read(g)));
        });
        Try("cmddata.native", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            var na = new NativeArray<uint>(Fill(8, i => 20u + (uint)i), Allocator.Persistent);
            cmd.SetBufferData(g, na);
            na[1] = 77;
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.native", U(Read(g)));
            na.Dispose();
        });
        Try("cmddata.nativerange", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            var na = new NativeArray<uint>(Fill(8, i => 30u + (uint)i), Allocator.Persistent);
            cmd.SetBufferData(g, na, 1, 5, 2);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.nativerange", U(Read(g)));
            na.Dispose();
        });
        Try("cmddata.list", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            var list = Fill(8, i => 40u + (uint)i).ToList();
            cmd.SetBufferData(g, list, 0, 6, 2);
            list[0] = 5;
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.list", U(Read(g)));
        });
        Try("cmddata.bytes", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            cmd.SetBufferData(g, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, 4, 4);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.bytes", U(Read(g)));
        });
        Try("cmddata.cb", () =>
        {
            using var c = Cb();
            using var cmd = new CommandBuffer();
            cmd.SetBufferData(c, Fill(8, i => 60u + (uint)i));
            var na = new NativeArray<uint>(Fill(4, i => 70u + (uint)i), Allocator.Persistent);
            cmd.SetBufferData(c, na, 0, 4, 4);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.cb", U(Read(c)));
            na.Dispose();
        });
        Try("cmddata.overflow", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            cmd.SetBufferData(g, Fill(8, i => 10u + (uint)i), 0, 6, 4);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.overflow", U(Read(g)));
        });
        Try("cmddata.badrange", () =>
        {
            using var g = Gb();
            using var cmd = new CommandBuffer();
            cmd.SetBufferData(g, Fill(4, i => 10u + (uint)i), 2, 0, 4);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.badrange", U(Read(g)));
        });
        Try("cmddata.counter", () =>
        {
            using var app = new ComputeBuffer(8, 4, ComputeBufferType.Append);
            using var args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
            args.SetData(Fill(4, i => 0xFFFFFFFFu));
            using var cmd = new CommandBuffer();
            cmd.SetBufferCounterValue(app, 7);
            cmd.CopyCounterValue(app, args, 4);
            Value("cmddata.counter.before", U(Read(args)));
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.counter.after", U(Read(args)));
        });
        Try("cmddata.counter.gb", () =>
        {
            using var app = new GraphicsBuffer(GraphicsBuffer.Target.Append, 8, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 4, 4);
            args.SetData(Fill(4, i => 0xFFFFFFFFu));
            using var cmd = new CommandBuffer();
            cmd.SetBufferCounterValue(app, 3);
            cmd.CopyCounterValue(app, args, 8);
            cmd.SetBufferCounterValue(app, 6);
            cmd.CopyCounterValue(app, args, 12);
            Graphics.ExecuteCommandBuffer(cmd);
            Value("cmddata.counter.gb", U(Read(args)));
        });
    }

    // ------------------------------------------------------------ SetData / GetData overloads

    void SetGetData()
    {
        ComputeBuffer Cb() { var c = new ComputeBuffer(8, 4); c.SetData(Fill(8, i => Sentinel + (uint)i)); return c; }
        GraphicsBuffer Gb() { var g = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4); g.SetData(Fill(8, i => Sentinel + (uint)i)); return g; }
        Try("setdata.native", () =>
        {
            using var c = Cb();
            using var na = new NativeArray<uint>(Fill(8, i => 1u + (uint)i), Allocator.Temp);
            c.SetData(na);
            Value("setdata.native.all", U(Read(c)));
            c.SetData(na, 1, 4, 3);
            Value("setdata.native.range", U(Read(c)));
        });
        Try("setdata.gbnative", () =>
        {
            using var g = Gb();
            using var na = new NativeArray<uint>(Fill(4, i => 100u + (uint)i), Allocator.Temp);
            g.SetData(na, 2, 0, 2);
            Value("setdata.gbnative", U(Read(g)));
        });
        Try("setdata.list", () =>
        {
            using var c = Cb();
            c.SetData(Fill(8, i => 200u + (uint)i).ToList(), 3, 1, 2);
            Value("setdata.list", U(Read(c)));
        });
        Try("setdata.bytes", () =>
        {
            using var c = Cb();
            c.SetData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, 4, 4);
            Value("setdata.bytes", U(Read(c)));
        });
        Try("setdata.wide", () =>
        {
            using var c = Cb();
            c.SetData(new ulong[] { 0x1111111122222222ul, 0x3333333344444444ul }, 0, 1, 2);
            Value("setdata.wide", U(Read(c)));
        });
        Try("setdata.overflow", () =>
        {
            using var c = Cb();
            c.SetData(Fill(8, i => 1u), 0, 6, 4);
            Value("setdata.overflow", U(Read(c)));
        });
        Try("setdata.badrange", () =>
        {
            using var c = Cb();
            c.SetData(Fill(4, i => 1u), 2, 0, 4);
            Value("setdata.badrange", U(Read(c)));
        });
        Try("setdata.nativeoverflow", () =>
        {
            using var c = Cb();
            using var na = new NativeArray<uint>(4, Allocator.Temp);
            c.SetData(na, 0, 6, 4);
            Value("setdata.nativeoverflow", U(Read(c)));
        });
        Try("getdata.range", () =>
        {
            using var c = Cb();
            var a = Fill(6, i => 1u);
            c.GetData(a, 1, 2, 3);
            Value("getdata.range", U(a));
        });
        Try("getdata.bytes", () =>
        {
            using var c = Cb();
            var a = new byte[6];
            c.GetData(a, 0, 5, 6);
            Value("getdata.bytes", "[" + string.Join(",", a) + "]");
        });
        Try("getdata.overflow", () =>
        {
            using var c = Cb();
            var a = Fill(6, i => 1u);
            c.GetData(a, 0, 6, 4);
            Value("getdata.overflow", U(a));
        });
        Try("getdata.released", () =>
        {
            var c = Cb();
            c.Release();
            var a = Fill(2, i => 1u);
            c.GetData(a);
            Value("getdata.released", U(a));
        });
    }

    // ------------------------------------------------------------ readbacks

    /// <summary>A readback under watch: its state changes, frame by frame, as events
    /// "&lt;phase&gt;&lt;frame&gt;:&lt;done&gt;&lt;error&gt;" (phase r: right after the request, u: Update,
    /// c: in its callback, w: after a wait), and its data when done.</summary>
    sealed class Watch
    {
        public string Key;
        public AsyncGPUReadbackRequest Request;
        public int Start;
        public bool Seen, Done, Error, Finished;
        public readonly List<string> Events = new List<string>();
        public readonly List<string> Frames = new List<string>();
        public Func<string> Extra;
        /// <summary>Command-buffer requests have no request to poll: only their callback reports.</summary>
        public bool Polled = true;
    }

    void Event(Watch w, string phase, AsyncGPUReadbackRequest r)
    {
        bool d = r.done, e = r.hasError;
        if (phase == "u" && w.Seen && d == w.Done && e == w.Error) return;
        w.Seen = true;
        w.Done = d;
        w.Error = e;
        int frame = Frame - w.Start;
        string state = ":" + (d ? "d" : "-") + (e ? "e" : "-");
        w.Events.Add(phase + (frame == 0 ? "0" : "n") + state);
        w.Frames.Add(phase + frame + state);
    }

    void Data(Watch w, string suffix, AsyncGPUReadbackRequest r)
    {
        string key = w.Key + suffix;
        try
        {
            Value(key + ".shape", "[" + r.width + "," + r.height + "," + r.depth + "," + r.layerCount + "," + r.layerDataSize + "]");
            var layers = new List<string>();
            for (int i = 0; i < Math.Max(1, r.layerCount); i++) layers.Add(Hex(r.GetData<byte>(i)));
            Value(key + ".data", "[" + string.Join(",", layers) + "]");
        }
        catch (Exception e) { Value(key + ".dataerror", Error(e)); }
        if (w.Extra != null) Value(key + ".extra", w.Extra());
    }

    Watch Make(string key, Func<Action<AsyncGPUReadbackRequest>, AsyncGPUReadbackRequest> request, bool callback = true, bool polled = true)
    {
        var w = new Watch { Key = key, Start = Frame, Polled = polled };
        logs.Clear();
        capturing = true;
        try
        {
            Action<AsyncGPUReadbackRequest> cb = null;
            if (callback)
                cb = r =>
                {
                    Event(w, "c", r);
                    if (r.done && !r.hasError) Data(w, ".callback", r);
                    if (!w.Polled) w.Finished = true;
                };
            w.Request = request(cb);
            if (polled) Event(w, "r", w.Request);
            watches.Add(w);
        }
        catch (Exception e)
        {
            Value(key + ".error", Error(e));
            w.Finished = true;
        }
        capturing = false;
        if (logs.Count > 0) Value(key + ".log", "[" + string.Join(",", logs.Select(Q)) + "]");
        logs.Clear();
        return w;
    }

    void Update()
    {
        foreach (var w in watches)
        {
            if (w.Finished || !w.Polled) continue;
            Event(w, "u", w.Request);
            if (w.Done)
            {
                w.Finished = true;
                Data(w, "", w.Request);
            }
        }
    }

    /// <summary>Waits for the watched requests (at most 20 frames), then records their events.</summary>
    IEnumerator Settle(params Watch[] ws)
    {
        for (int i = 0; i < 20 && ws.Any(w => !w.Finished); i++) yield return null;
        yield return null;
        foreach (var w in ws)
        {
            if (!w.Finished) w.Events.Add("timeout");
            w.Finished = true;
            if (w.Events.Count > 0) Value(w.Key + ".events", "[" + string.Join(",", w.Events.Select(Q)) + "]");
            if (w.Frames.Count > 0) timing.Append("{\"k\":\"").Append(w.Key).Append(".frames\",\"v\":[").Append(string.Join(",", w.Frames.Select(Q))).Append("]}\n");
        }
        watches.Clear();
    }

    static Color32 C(int r, int g, int b, int a) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

    IEnumerator Readbacks()
    {
        frame0 = Time.frameCount;
        var buf = new ComputeBuffer(16, 4);
        buf.SetData(Fill(16, i => (uint)i * 7u + 1u));

        var w = Make("rb.buffer", cb => AsyncGPUReadback.Request(buf), false);
        yield return Settle(w);
        w = Make("rb.callback", cb => AsyncGPUReadback.Request(buf, cb));
        yield return Settle(w);
        w = Make("rb.range", cb => AsyncGPUReadback.Request(buf, 16, 8, cb));
        yield return Settle(w);
        w = Make("rb.range.beyond", cb => AsyncGPUReadback.Request(buf, 64, 8, cb));
        yield return Settle(w);
        w = Make("rb.range.unaligned", cb => AsyncGPUReadback.Request(buf, 6, 2, cb));
        yield return Settle(w);
        w = Make("rb.range.zero", cb => AsyncGPUReadback.Request(buf, 0, 0, cb));
        yield return Settle(w);
        w = Make("rb.gb", cb =>
        {
            var g = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            g.SetData(new uint[] { 5, 6, 7, 8 });
            var r = AsyncGPUReadback.Request(g, cb);
            g.Release();
            return r;
        });
        yield return Settle(w);

        // Writes after the request.
        w = Make("rb.order", cb =>
        {
            var r = AsyncGPUReadback.Request(buf, cb);
            buf.SetData(Fill(16, i => 1000u + (uint)i));
            return r;
        });
        yield return Settle(w);
        buf.SetData(Fill(16, i => (uint)i * 7u + 1u));

        // Early data access, and access frames later.
        w = Make("rb.early", cb =>
        {
            var r = AsyncGPUReadback.Request(buf, cb);
            try { Value("rb.early.getdata", Hex(r.GetData<byte>())); }
            catch (Exception e) { Value("rb.early.getdata.error", Error(e)); }
            return r;
        });
        yield return Settle(w);
        var late = w.Request;
        Try("rb.later1", () => Value("rb.later1", Hex(late.GetData<byte>())));
        Try("rb.later1.layer", () => Value("rb.later1.layer", Hex(late.GetData<byte>(1))));
        Try("rb.later1.type", () => Value("rb.later1.type", late.GetData<Vector3>().Length.ToString()));
        yield return null;
        yield return null;
        Try("rb.later3", () => Value("rb.later3", Hex(late.GetData<byte>())));
        Value("rb.later3.state", "[" + B(late.done) + "," + B(late.hasError) + "]");

        // Waiting.
        var order = new List<string>();
        w = Make("rb.wait", cb => AsyncGPUReadback.Request(buf, r => { order.Add("callback"); cb(r); }));
        order.Add("waiting");
        w.Request.WaitForCompletion();
        order.Add("waited");
        Event(w, "w", w.Request);
        yield return Settle(w);
        Value("rb.wait.order", "[" + string.Join(",", order.Select(Q)) + "]");

        order.Clear();
        var w1 = Make("rb.waitall.a", cb => AsyncGPUReadback.Request(buf, r => { order.Add("a"); cb(r); }));
        var w2 = Make("rb.waitall.b", cb => AsyncGPUReadback.Request(buf, 8, 0, r => { order.Add("b"); cb(r); }));
        order.Add("waiting");
        AsyncGPUReadback.WaitAllRequests();
        order.Add("waited");
        Event(w1, "w", w1.Request);
        Event(w2, "w", w2.Request);
        yield return Settle(w1, w2);
        Value("rb.waitall.order", "[" + string.Join(",", order.Select(Q)) + "]");

        order.Clear();
        w = Make("rb.update", cb => AsyncGPUReadback.Request(buf, r => { order.Add("callback"); cb(r); }));
        order.Add("updating");
        w.Request.Update();
        order.Add("updated");
        Event(w, "w", w.Request);
        yield return Settle(w);
        Value("rb.update.order", "[" + string.Join(",", order.Select(Q)) + "]");

        // Into a native array.
        var output = new NativeArray<uint>(Fill(16, i => 0xEEEEEEEEu), Allocator.Persistent);
        w = Make("rb.native", cb =>
        {
            var r = AsyncGPUReadback.RequestIntoNativeArray(ref output, buf, cb);
            try { Value("rb.native.atrequest", U(output.ToArray())); }
            catch (Exception e) { Value("rb.native.atrequest.error", Error(e)); }
            return r;
        });
        var o1 = output;
        w.Extra = () => U(o1.ToArray());
        yield return Settle(w);
        Value("rb.native.final", U(output.ToArray()));
        output.Dispose();

        var bigger = new NativeArray<uint>(Fill(20, i => 0xEEEEEEEEu), Allocator.Persistent);
        w = Make("rb.native.larger", cb => AsyncGPUReadback.RequestIntoNativeArray(ref bigger, buf, cb));
        yield return Settle(w);
        Value("rb.native.larger.final", U(bigger.ToArray()));
        bigger.Dispose();

        var small = new NativeArray<uint>(Fill(4, i => 0xEEEEEEEEu), Allocator.Persistent);
        w = Make("rb.native.small", cb => AsyncGPUReadback.RequestIntoNativeArray(ref small, buf, cb));
        yield return Settle(w);
        Value("rb.native.small.final", U(small.ToArray()));
        small.Dispose();

        var ranged = new NativeArray<uint>(Fill(4, i => 0xEEEEEEEEu), Allocator.Persistent);
        w = Make("rb.native.range", cb => AsyncGPUReadback.RequestIntoNativeArray(ref ranged, buf, 8, 16, cb));
        yield return Settle(w);
        Value("rb.native.range.final", U(ranged.ToArray()));
        ranged.Dispose();

        // Released sources.
        w = Make("rb.released", cb =>
        {
            var gone = new ComputeBuffer(4, 4);
            gone.Release();
            return AsyncGPUReadback.Request(gone, cb);
        });
        yield return Settle(w);
        w = Make("rb.null", cb => AsyncGPUReadback.Request((ComputeBuffer)null, cb));
        yield return Settle(w);

        // Command buffers.
        w = Make("rb.cmd", cb =>
        {
            using var cmd = new CommandBuffer();
            cmd.SetBufferData(buf, Fill(16, i => 500u + (uint)i));
            cmd.RequestAsyncReadback(buf, cb);
            cmd.SetBufferData(buf, Fill(16, i => 600u + (uint)i));
            Graphics.ExecuteCommandBuffer(cmd);
            return default;
        }, true, false);
        yield return Settle(w);
        Value("rb.cmd.final", U(Read(buf)));
        buf.SetData(Fill(16, i => (uint)i * 7u + 1u));

        order.Clear();
        w = Make("rb.cmd.wait", cb =>
        {
            using var cmd = new CommandBuffer();
            cmd.RequestAsyncReadback(buf, 8, 4, r => { order.Add("callback"); cb(r); });
            cmd.WaitAllAsyncReadbackRequests();
            order.Add("executing");
            Graphics.ExecuteCommandBuffer(cmd);
            order.Add("executed");
            return default;
        }, true, false);
        yield return Settle(w);
        Value("rb.cmd.wait.order", "[" + string.Join(",", order.Select(Q)) + "]");

        var cmdOut = new NativeArray<uint>(Fill(16, i => 0xEEEEEEEEu), Allocator.Persistent);
        w = Make("rb.cmd.native", cb =>
        {
            using var cmd = new CommandBuffer();
            cmd.RequestAsyncReadbackIntoNativeArray(ref cmdOut, buf, cb);
            Graphics.ExecuteCommandBuffer(cmd);
            try { Value("rb.cmd.native.atrequest", U(cmdOut.ToArray())); }
            catch (Exception e) { Value("rb.cmd.native.atrequest.error", Error(e)); }
            return default;
        }, true, false);
        yield return Settle(w);
        Value("rb.cmd.native.final", U(cmdOut.ToArray()));
        cmdOut.Dispose();
        buf.Release();

        // Textures.
        var tex = new Texture2D(8, 8, TextureFormat.RGBA32, true, true);
        for (int mip = 0; mip < tex.mipmapCount; mip++)
        {
            int s = Math.Max(1, 8 >> mip);
            var px = new Color32[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = C(i * 3 + mip * 40, i % s * 20, i / s * 20, 255 - mip * 30);
            tex.SetPixels32(px, mip);
        }
        tex.Apply(false);
        Value("rb.tex.mips", tex.mipmapCount.ToString());
        foreach (int mip in new[] { 0, 1, 3, 4 })
        {
            int m = mip;
            w = Make("rb.tex.mip" + m, cb => AsyncGPUReadback.Request(tex, m, cb));
            yield return Settle(w);
        }
        w = Make("rb.tex.region", cb => AsyncGPUReadback.Request(tex, 0, 2, 3, 1, 4, 0, 1, cb));
        yield return Settle(w);
        w = Make("rb.tex.region.mip1", cb => AsyncGPUReadback.Request(tex, 1, 1, 2, 2, 2, 0, 1, cb));
        yield return Settle(w);
        w = Make("rb.tex.region.beyond", cb => AsyncGPUReadback.Request(tex, 0, 6, 4, 0, 2, 0, 1, cb));
        yield return Settle(w);
        w = Make("rb.tex.region.depth", cb => AsyncGPUReadback.Request(tex, 0, 0, 2, 0, 2, 0, 2, cb));
        yield return Settle(w);
        foreach (var format in new[] { TextureFormat.RGBAFloat, TextureFormat.RGBAHalf, TextureFormat.BGRA32, TextureFormat.ARGB32, TextureFormat.R8, TextureFormat.RGB24 })
        {
            var f = format;
            w = Make("rb.tex.format." + f, cb => AsyncGPUReadback.Request(tex, 1, f, cb));
            yield return Settle(w);
        }
        w = Make("rb.tex.gformat.srgb", cb => AsyncGPUReadback.Request(tex, 1, GraphicsFormat.R8G8B8A8_SRGB, cb));
        yield return Settle(w);
        w = Make("rb.tex.gformat.r32", cb => AsyncGPUReadback.Request(tex, 1, GraphicsFormat.R32_SFloat, cb));
        yield return Settle(w);
        var texOut = new NativeArray<Color32>(64, Allocator.Persistent);
        w = Make("rb.tex.native", cb => AsyncGPUReadback.RequestIntoNativeArray(ref texOut, tex, 0, cb));
        yield return Settle(w);
        Value("rb.tex.native.final", Q(string.Join(" ", texOut.Take(4).Select(c => c.r + "," + c.g + "," + c.b + "," + c.a))));
        texOut.Dispose();
        tex.Apply(false, true);
        w = Make("rb.tex.unreadable", cb => AsyncGPUReadback.Request(tex, 1, cb));
        yield return Settle(w);
        UnityEngine.Object.Destroy(tex);

        var array = new Texture2DArray(4, 4, 3, TextureFormat.RGBA32, false, true);
        for (int slice = 0; slice < 3; slice++)
        {
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = C(slice * 80 + i, i * 10, slice * 50, 200 + slice);
            array.SetPixels32(px, slice, 0);
        }
        array.Apply(false);
        w = Make("rb.array.full", cb => AsyncGPUReadback.Request(array, 0, cb));
        yield return Settle(w);
        w = Make("rb.array.layers", cb => AsyncGPUReadback.Request(array, 0, 0, 4, 0, 4, 1, 2, cb));
        yield return Settle(w);
        w = Make("rb.array.region", cb => AsyncGPUReadback.Request(array, 0, 1, 2, 1, 2, 0, 3, cb));
        yield return Settle(w);
        w = Make("rb.array.beyond", cb => AsyncGPUReadback.Request(array, 0, 0, 4, 0, 4, 2, 2, cb));
        yield return Settle(w);
        UnityEngine.Object.Destroy(array);

        var volume = new Texture3D(4, 4, 4, TextureFormat.RGBA32, false);
        var vpx = new Color32[64];
        for (int i = 0; i < 64; i++) vpx[i] = C(i * 4, (i % 16) * 16, (i / 16) * 60, 255);
        volume.SetPixels32(vpx);
        volume.Apply(false);
        w = Make("rb.volume.full", cb => AsyncGPUReadback.Request(volume, 0, cb));
        yield return Settle(w);
        w = Make("rb.volume.slices", cb => AsyncGPUReadback.Request(volume, 0, 0, 4, 0, 4, 1, 2, cb));
        yield return Settle(w);
        w = Make("rb.volume.beyond", cb => AsyncGPUReadback.Request(volume, 0, 0, 4, 0, 4, 3, 2, cb));
        yield return Settle(w);
        UnityEngine.Object.Destroy(volume);

        var rt = new RenderTexture(8, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { useMipMap = true, autoGenerateMips = false };
        rt.Create();
        Graphics.SetRenderTarget(rt, 0);
        GL.Clear(true, true, new Color(0.25f, 0.5f, 0.75f, 1f));
        Graphics.SetRenderTarget(rt, 1);
        GL.Clear(true, true, new Color(1f, 0f, 0.5f, 0.25f));
        RenderTexture.active = null;
        w = Make("rb.rt.mip0", cb => AsyncGPUReadback.Request(rt, 0, cb));
        yield return Settle(w);
        w = Make("rb.rt.mip1", cb => AsyncGPUReadback.Request(rt, 1, cb));
        yield return Settle(w);
        w = Make("rb.rt.cmd", cb =>
        {
            using var cmd = new CommandBuffer();
            cmd.SetRenderTarget(rt, 0);
            cmd.ClearRenderTarget(true, true, new Color(0f, 1f, 0f, 0.5f));
            cmd.RequestAsyncReadback(rt, 0, cb);
            cmd.ClearRenderTarget(true, true, new Color(1f, 1f, 1f, 1f));
            Graphics.ExecuteCommandBuffer(cmd);
            return default;
        }, true, false);
        yield return Settle(w);
        w = Make("rb.rt.float", cb => AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAFloat, cb));
        yield return Settle(w);
        rt.Release();

        var rtf = new RenderTexture(4, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        rtf.Create();
        Graphics.SetRenderTarget(rtf);
        GL.Clear(true, true, new Color(1.5f, -2f, 0.25f, 1f));
        RenderTexture.active = null;
        w = Make("rb.rtfloat", cb => AsyncGPUReadback.Request(rtf, 0, cb));
        yield return Settle(w);
        w = Make("rb.rtfloat.bytes", cb => AsyncGPUReadback.Request(rtf, 0, TextureFormat.RGBA32, cb));
        yield return Settle(w);
        rtf.Release();

        var rta = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { dimension = TextureDimension.Tex2DArray, volumeDepth = 3 };
        rta.Create();
        for (int slice = 0; slice < 3; slice++)
        {
            Graphics.SetRenderTarget(rta, 0, CubemapFace.Unknown, slice);
            GL.Clear(true, true, new Color(slice * 0.25f, 1f - slice * 0.25f, 0.5f, 1f));
        }
        RenderTexture.active = null;
        w = Make("rb.rtarray", cb => AsyncGPUReadback.Request(rta, 0, 0, 4, 0, 4, 0, 3, cb));
        yield return Settle(w);
        w = Make("rb.rtarray.default", cb => AsyncGPUReadback.Request(rta, 0, cb));
        yield return Settle(w);
        rta.Release();
    }
}
