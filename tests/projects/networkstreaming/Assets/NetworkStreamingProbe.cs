using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class NetworkStreamingProbe : MonoBehaviour
{
    const int First = 32768, Total = 40960;
    static string Out;
    static int Main;
    StreamWriter log;
    string url;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Out = Environment.GetEnvironmentVariable("NETWORK_STREAMING_OUT");
        if (!string.IsNullOrEmpty(Out)) new GameObject("NetworkStreamingProbe").AddComponent<NetworkStreamingProbe>();
    }

    [Serializable] class Record { public string key; public string value; }
    void Write(string key, object value)
    {
        log.WriteLine(JsonUtility.ToJson(new Record { key = key, value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) }));
        log.Flush();
    }
    void Release(string name) => File.WriteAllText(Path.Combine(Out, name + ".release"), "");
    IEnumerator Wait(Func<bool> predicate, float seconds = 4)
    {
        var end = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
    }
    IEnumerator Start()
    {
        Main = Thread.CurrentThread.ManagedThreadId;
        log = new StreamWriter(Path.Combine(Out, "records.jsonl"));
        url = File.ReadAllText(Path.Combine(Out, "url"));
        foreach (string mode in new[] { "buffered", "custom-progress", "unbuffered", "unknown", "http-error", "abort", "reject", "truncated", "timeout", "dispose", "handler-dispose", "empty" })
            yield return Script(mode);
        foreach (string mode in new[] { "normal", "append", "abort-keep", "abort-remove", "append-abort-remove", "truncated-keep", "truncated-remove", "dispose-remove", "empty", "append-empty", "bad-path" })
            yield return DownloadFile(mode);
        // The standard buffered handler must continue working through the same request path.
        yield return Buffer();
        log.Dispose();
        File.WriteAllText(Path.Combine(Out, "finished"), "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    sealed class Handler : DownloadHandlerScript
    {
        public int bytes, calls, lengths, completes;
        public ulong length;
        public bool main = true, order = true, same = true, bounded = true, content = true;
        public bool reject;
        public bool customProgress;
        protected override float GetProgress() => customProgress ? 0.25f : base.GetProgress();
        public readonly byte[] buffer;
        public readonly List<byte> received = new List<byte>();
        public Handler() { }
        public Handler(byte[] buffer) : base(buffer) { this.buffer = buffer; }
        protected override void ReceiveContentLengthHeader(ulong n)
        {
            main &= Thread.CurrentThread.ManagedThreadId == Main;
            order &= calls == 0 && completes == 0;
            lengths++; length = n;
        }
        protected override bool ReceiveData(byte[] data, int n)
        {
            main &= Thread.CurrentThread.ManagedThreadId == Main;
            order &= lengths > 0 && completes == 0;
            same &= buffer == null || ReferenceEquals(data, buffer);
            bounded &= n > 0 && n <= data.Length && (buffer == null || n <= buffer.Length);
            for (int i = 0; i < n; i++) received.Add(data[i]);
            calls++; bytes += n;
            return !reject;
        }
        protected override void CompleteContent()
        {
            main &= Thread.CurrentThread.ManagedThreadId == Main;
            completes++;
        }
    }

    IEnumerator Script(string mode)
    {
        string name = "script-" + mode;
        var h = mode == "unbuffered" ? new Handler() : new Handler(new byte[1024]);
        h.reject = mode == "reject";
        h.customProgress = mode == "custom-progress";
        var r = new UnityWebRequest(url + name, "GET", h, null);
        if (mode == "timeout") r.timeout = 1;
        bool eventMain = true, eventAfterContent = false;
        int events = 0;
        var op = r.SendWebRequest();
        op.completed += _ => { events++; eventMain &= Thread.CurrentThread.ManagedThreadId == Main; eventAfterContent = h.completes > 0; };
        bool empty = mode == "empty";
        yield return Wait(() => empty ? r.isDone : h.bytes >= First || r.isDone);
        Write(name + ".partial", h.bytes >= First && !r.isDone);
        if (!empty && mode != "reject")
        {
            Write(name + ".partial-bytes", r.downloadedBytes);
            Write(name + ".partial-progress", Math.Round(r.downloadProgress, 3));
            Write(name + ".partial-handler-done", h.isDone);
        }
        int atDispose = h.bytes;
        bool disposed = mode == "dispose";
        if (mode == "abort") r.Abort();
        if (disposed) r.Dispose();
        if (mode == "handler-dispose") h.Dispose();
        if (mode == "timeout") yield return Wait(() => r.isDone);
        Release(name);
        if (disposed)
        {
            yield return Wait(() => events > 0, 1);
            Write(name + ".post-dispose-bytes", h.bytes - atDispose);
        }
        else
        {
            yield return Wait(() => r.isDone);
            yield return Wait(() => events > 0);
            Write(name + ".done", r.isDone);
            Write(name + ".result", r.result);
            Write(name + ".error", r.error ?? "<null>");
            Write(name + ".downloaded", r.downloadedBytes);
            Write(name + ".progress", Math.Round(r.downloadProgress, 3));
            Write(name + ".handler-done", Read(() => h.isDone));
            Write(name + ".handler-error", Read(() => h.error ?? "<null>"));
        }
        Write(name + ".received", h.bytes);
        Write(name + ".length-count", h.lengths);
        Write(name + ".length", h.length);
        Write(name + ".complete-count", h.completes);
        Write(name + ".main", h.main && eventMain);
        Write(name + ".order", h.order);
        Write(name + ".buffer", h.same && h.bounded);
        bool content = true;
        for (int i = 0; i < h.received.Count; i++) content &= h.received[i] == (byte)(i < First ? i % 251 : (i - First) % 239);
        Write(name + ".content", content);
        Write(name + ".events", events);
        Write(name + ".event-after-content", eventAfterContent);
        r.Dispose();
    }

    string Read(Func<object> read)
    {
        try { return Convert.ToString(read()); }
        catch (Exception e) { return e.GetType().Name; }
    }

    long Size(string path) => File.Exists(path) ? new FileInfo(path).Length : -1;
    IEnumerator DownloadFile(string mode)
    {
        string name = "file-" + mode;
        bool append = mode.StartsWith("append"), empty = mode.Contains("empty");
        string path = Path.Combine(Out, name + ".bin");
        File.WriteAllBytes(path, new byte[] { 7, 8, 9 });
        if (mode == "bad-path") { File.Delete(path); Directory.CreateDirectory(path); }
        DownloadHandlerFile h = null;
        string constructorError = null;
        try { h = new DownloadHandlerFile(path, append) { removeFileOnAbort = mode.Contains("remove") }; }
        catch (Exception e) { constructorError = e.GetType().Name; }
        if (mode == "bad-path")
        {
            Write(name + ".constructor-error", constructorError ?? "<none>");
            h?.Dispose();
            yield break;
        }
        Write(name + ".size-created", Size(path));
        var r = new UnityWebRequest(url + name, "GET", h, null);
        int events = 0;
        r.SendWebRequest().completed += _ => events++;
        yield return Wait(() => empty ? r.isDone : r.downloadedBytes >= First || r.isDone);
        Write(name + ".partial", Size(path) >= First && !r.isDone);
        Write(name + ".partial-size", Size(path));
        if (mode.Contains("abort")) r.Abort();
        if (mode == "dispose-remove") r.Dispose();
        Release(name);
        yield return Wait(() => events > 0);
        if (mode != "dispose-remove")
        {
            Write(name + ".done", r.isDone);
            Write(name + ".result", r.result);
            Write(name + ".handler-done", h.isDone);
        }
        Write(name + ".size-done", Size(path));
        Write(name + ".events", events);
        r.Dispose();
    }

    IEnumerator Buffer()
    {
        const string name = "buffer-normal";
        using (var r = UnityWebRequest.Get(url + name))
        {
            r.SendWebRequest();
            yield return Wait(() => r.downloadedBytes >= First);
            Write(name + ".partial", !r.isDone && r.downloadHandler.data.Length == First);
            Release(name);
            yield return Wait(() => r.isDone);
            Write(name + ".result", r.result);
            Write(name + ".bytes", r.downloadHandler.data.Length);
        }
    }
}
