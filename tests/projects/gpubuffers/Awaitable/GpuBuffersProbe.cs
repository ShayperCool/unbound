using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Replaces the main probe only in a disposable project copy. ProbeRunner is reused.
public class GpuBuffersProbe : MonoBehaviour
{
    public static Action<int> finished;
    readonly StringBuilder values = new StringBuilder();
    readonly StringBuilder timing = new StringBuilder();
    readonly List<string> logs = new List<string>();
    int mainThread;
    string directory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("GPUBUFFERS_PROBE_OUT") != null)
            new GameObject("ReadbackAwaitableProbe").AddComponent<GpuBuffersProbe>();
    }

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string Error(Exception e) => Q(e.GetType().Name + ": " + e.Message);
    void Value(string key, string json)
    {
        lock (values) values.Append("{\"k\":").Append(Q(key)).Append(",\"v\":").Append(json).Append("}\n");
    }
    static void Event(List<string> events, string value) { lock (events) events.Add(value); }
    void Try(string key, Func<string> read)
    {
        try { Value(key, read()); }
        catch (Exception e) { Value(key, Error(e)); }
    }
    string State(AsyncGPUReadbackRequest r) => (r.done ? "d" : "-") + (r.hasError ? "e" : "-");
    string ThreadKind => Thread.CurrentThread.ManagedThreadId == mainThread ? "main" : "other";
    void Data(string key, AsyncGPUReadbackRequest r)
    {
        // Request-owned arrays are borrowed; do not dispose them.
        Try(key, () => Q(string.Concat(r.GetData<byte>().ToArray().Select(b => b.ToString("x2")))));
    }
    void Snapshot(string key, AsyncGPUReadbackRequest r)
    {
        Value(key + ".state", Q(State(r)));
        Data(key + ".data", r);
        Data(key + ".dataAgain", r);
    }
    void Log(string message, string stack, LogType type) => logs.Add(type + ": " + message);

    sealed class Observation
    {
        public volatile bool Done;
        public bool HasRequest;
        public AsyncGPUReadbackRequest Request;
    }

    async void Consume(string key, int frame, List<string> events,
        Awaitable<AsyncGPUReadbackRequest> wait, Observation result)
    {
        try
        {
            var request = await wait;
            result.Request = request;
            result.HasRequest = true;
            Event(events, "a" + (Time.frameCount - frame) + ":" + ThreadKind + ":" + State(request));
            Value(key + ".await.outcome", Q("request"));
            Snapshot(key + ".await", request);
        }
        catch (Exception e)
        {
            Event(events, "a" + (Time.frameCount - frame) + ":" + ThreadKind + ":throw");
            Value(key + ".await.outcome", Error(e));
        }
        // No other Awaitable is obtained between first and second consumption.
        Try(key + ".consumeAgain", () => Q(State(wait.GetAwaiter().GetResult())));
        result.Done = true;
    }

    IEnumerator Pair(string key, Func<Action<AsyncGPUReadbackRequest>, AsyncGPUReadbackRequest> callback,
        Func<Awaitable<AsyncGPUReadbackRequest>> requestAsync)
    {
        logs.Clear();
        int frame = Time.frameCount;
        var events = new List<string>();
        var cb = new Observation();
        var aw = new Observation();
        try
        {
            var request = callback(r =>
            {
                cb.Request = r;
                cb.HasRequest = true;
                Event(events, "c" + (Time.frameCount - frame) + ":" + ThreadKind + ":" + State(r));
                Snapshot(key + ".callback", r);
                cb.Done = true;
            });
            Value(key + ".callback.return", Q(State(request)));
        }
        catch (Exception e) { Value(key + ".callback.throw", Error(e)); cb.Done = true; }
        try
        {
            var wait = requestAsync();
            Value(key + ".async.returnCompleted", B(wait.GetAwaiter().IsCompleted));
            Event(events, "r0:returned");
            Consume(key, frame, events, wait, aw);
        }
        catch (Exception e) { Value(key + ".async.throw", Error(e)); aw.Done = true; }

        while ((!cb.Done || !aw.Done) && Time.frameCount - frame < 8) yield return null;
        Value(key + ".finished", B(cb.Done && aw.Done));
        timing.Append("{\"k\":").Append(Q(key)).Append(",\"v\":[").Append(string.Join(",", events.Select(Q))).Append("]}\n");
        if (!cb.Done || !aw.Done) throw new TimeoutException(key);

        // Check request data after expiration without allocating another Awaitable.
        yield return null;
        yield return null;
        if (cb.HasRequest) Snapshot(key + ".callback.expired", cb.Request);
        if (aw.HasRequest) Snapshot(key + ".await.expired", aw.Request);
        Value(key + ".logs", "[" + string.Join(",", logs.Select(Q)) + "]");
    }

    IEnumerator Cases()
    {
        using (var compute = new ComputeBuffer(4, 4))
        using (var graphics = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4))
        {
            var words = new uint[] { 0x01020304, 0x11223344, 0x55667788, 0xAABBCCDD };
            compute.SetData(words);
            graphics.SetData(words);
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(new[] { new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8) });
            texture.Apply();
            try
            {
                yield return Pair("compute", cb => AsyncGPUReadback.Request(compute, cb), () => AsyncGPUReadback.RequestAsync(compute));
                yield return Pair("compute.range", cb => AsyncGPUReadback.Request(compute, 8, 4, cb), () => AsyncGPUReadback.RequestAsync(compute, 8, 4));
                yield return Pair("graphics", cb => AsyncGPUReadback.Request(graphics, cb), () => AsyncGPUReadback.RequestAsync(graphics));
                yield return Pair("graphics.range", cb => AsyncGPUReadback.Request(graphics, 8, 4, cb), () => AsyncGPUReadback.RequestAsync(graphics, 8, 4));
                yield return Pair("texture", cb => AsyncGPUReadback.Request(texture, 0, cb), () => AsyncGPUReadback.RequestAsync(texture));
                yield return Pair("texture.format", cb => AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, cb), () => AsyncGPUReadback.RequestAsync(texture, 0, TextureFormat.RGBA32));
                yield return Pair("texture.graphicsFormat", cb => AsyncGPUReadback.Request(texture, 0, GraphicsFormat.R8G8B8A8_UNorm, cb), () => AsyncGPUReadback.RequestAsync(texture, 0, GraphicsFormat.R8G8B8A8_UNorm));
                yield return Pair("texture.region", cb => AsyncGPUReadback.Request(texture, 0, 1, 1, 0, 1, 0, 1, cb), () => AsyncGPUReadback.RequestAsync(texture, 0, 1, 1, 0, 1, 0, 1));
                yield return Pair("texture.region.format", cb => AsyncGPUReadback.Request(texture, 0, 1, 1, 0, 1, 0, 1, TextureFormat.RGBA32, cb), () => AsyncGPUReadback.RequestAsync(texture, 0, 1, 1, 0, 1, 0, 1, TextureFormat.RGBA32));
                yield return Pair("texture.region.graphicsFormat", cb => AsyncGPUReadback.Request(texture, 0, 1, 1, 0, 1, 0, 1, GraphicsFormat.R8G8B8A8_UNorm, cb), () => AsyncGPUReadback.RequestAsync(texture, 0, 1, 1, 0, 1, 0, 1, GraphicsFormat.R8G8B8A8_UNorm));
                yield return Pair("invalid.range", cb => AsyncGPUReadback.Request(compute, 20, 0, cb), () => AsyncGPUReadback.RequestAsync(compute, 20, 0));
                yield return Pair("invalid.mip", cb => AsyncGPUReadback.Request(texture, 4, cb), () => AsyncGPUReadback.RequestAsync(texture, 4));
                yield return Pair("invalid.null", cb => AsyncGPUReadback.Request((ComputeBuffer)null, cb), () => AsyncGPUReadback.RequestAsync((ComputeBuffer)null));
                using (var released = new ComputeBuffer(1, 4))
                {
                    released.Release();
                    yield return Pair("invalid.released", cb => AsyncGPUReadback.Request(released, cb), () => AsyncGPUReadback.RequestAsync(released));
                }
            }
            finally { Destroy(texture); }
        }
    }

    IEnumerator Start()
    {
        directory = Environment.GetEnvironmentVariable("GPUBUFFERS_PROBE_OUT");
        Directory.CreateDirectory(directory);
        mainThread = Thread.CurrentThread.ManagedThreadId;
        Application.logMessageReceived += Log;
        int code = 0;
        // Drive nested enumerators here so any failure still writes all observations.
        var steps = new Stack<IEnumerator>();
        steps.Push(Cases());
        while (steps.Count > 0)
        {
            object next = null;
            try
            {
                if (!steps.Peek().MoveNext()) { (steps.Pop() as IDisposable)?.Dispose(); continue; }
                next = steps.Peek().Current;
            }
            catch (Exception e) { Value("fatal", Error(e)); code = 1; break; }
            if (next is IEnumerator child) steps.Push(child);
            else yield return next;
        }
        while (steps.Count > 0) (steps.Pop() as IDisposable)?.Dispose();
        Value("complete", B(code == 0));
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), values.ToString());
        File.WriteAllText(Path.Combine(directory, "timing.jsonl"), timing.ToString());
        Application.logMessageReceived -= Log;
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
}
