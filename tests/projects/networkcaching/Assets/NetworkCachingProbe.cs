using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// AssetBundle downloads through the AssetBundle cache, the Caching/Cache API and audio
/// downloads, against the loopback server (server.py). Every record is one observable value;
/// keys starting with "os." are decided by the operating system (paths) and are compared only
/// with a reference of the same platform.
/// </summary>
public class NetworkCachingProbe : MonoBehaviour
{
    static string Out;
    StreamWriter log;
    string url;
    string defaultPath;
    readonly List<string> logs = new List<string>();
    readonly Dictionary<string, uint> crcs = new Dictionary<string, uint>();
    readonly List<AssetBundle> loaded = new List<AssetBundle>();

    static readonly Hash128 H1 = Hash128.Parse("0123456789abcdef0123456789abcdef");
    static readonly Hash128 H2 = Hash128.Parse("fedcba9876543210fedcba9876543210");
    static readonly Hash128 H3 = Hash128.Parse("00000000000000000000000000000003");
    static readonly Hash128 H4 = Hash128.Parse("44444444444444444444444444444444");
    static readonly Hash128 H5 = Hash128.Parse("55555555555555555555555555555555");
    static readonly Hash128 H6 = Hash128.Parse("66666666666666666666666666666666");
    static readonly Hash128 H7 = Hash128.Parse("77777777777777777777777777777777");
    static readonly Hash128 H9 = Hash128.Parse("99999999999999999999999999999999");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Out = Environment.GetEnvironmentVariable("NETWORK_CACHING_OUT");
        if (!string.IsNullOrEmpty(Out)) new GameObject("NetworkCachingProbe").AddComponent<NetworkCachingProbe>();
    }

    [Serializable] class Record { public string key; public string value; }

    void W(string key, object value)
    {
        string text = value switch
        {
            null => "null",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        // The output directory and the server's port change between runs.
        text = text.Replace(Out, "<out>");
        if (url != null) text = text.Replace(url, "<url>/");
        log.WriteLine(JsonUtility.ToJson(new Record { key = key, value = text }));
        log.Flush();
    }

    /// <summary>The messages logged since the last call, one line each.</summary>
    string Logs()
    {
        lock (logs)
        {
            var text = string.Join(" | ", logs);
            logs.Clear();
            return text;
        }
    }

    static object Try(Func<object> f)
    {
        try { return f(); }
        catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message; }
    }

    string Rel(string path)
    {
        if (path == null) return "null";
        path = path.Replace('\\', '/');
        if (path.StartsWith(Out.Replace('\\', '/'), StringComparison.Ordinal)) return "<out>" + path.Substring(Out.Length);
        if (defaultPath != null && path == defaultPath) return "<default>";
        return path;
    }

    string Describe(Cache c) => $"valid={c.valid} index={c.index} readOnly={c.readOnly} path={Rel(c.path)} " +
        $"expiration={c.expirationDelay} max={c.maximumAvailableStorageSpace} occupied={c.spaceOccupied} " +
        $"free={(c.spaceFree == c.maximumAvailableStorageSpace - c.spaceOccupied ? "max-occupied" : c.spaceFree > 0 ? "positive" : c.spaceFree.ToString())}";

    string Paths()
    {
        var paths = new List<string>();
        Caching.GetAllCachePaths(paths);
        return string.Join(",", paths.Select(Rel));
    }

    static string Versions(string name)
    {
        var list = new List<Hash128>();
        Caching.GetCachedVersions(name, list);
        return string.Join(",", list.Select(h => h.ToString()));
    }

    /// <summary>The cache directory's files (relative, sorted) with their sizes, __info files
    /// without them (they hold times).</summary>
    string Files(string root)
    {
        if (!Directory.Exists(root)) return "none";
        return string.Join(",", Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => f.Substring(root.Length + 1).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.GetFileName(f) == "__info" ? f : f + ":" + new FileInfo(Path.Combine(root, f)).Length));
    }

    int Requests(string path) => File.Exists(Path.Combine(Out, "requests.log"))
        ? File.ReadAllLines(Path.Combine(Out, "requests.log")).Count(l => l == path) : 0;

    string Bundle(string key) => url + "bundles/" + key;

    IEnumerator Start()
    {
        log = new StreamWriter(Path.Combine(Out, "records.jsonl"));
        url = File.ReadAllText(Path.Combine(Out, "url"));
        foreach (var line in File.ReadAllLines(Path.Combine(Application.dataPath, "../Content/bundles/crc.txt")))
        {
            var parts = line.Split(' ');
            if (parts.Length == 2) crcs[parts[0]] = uint.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        // Threaded: Unity logs some download messages from its worker threads.
        Application.logMessageReceivedThreaded += (condition, stack, type) =>
        {
            lock (logs) logs.Add(type + ": " + condition.Split('\n')[0]);
        };
        yield return null;
        Defaults();
        var a = Caches();
        var end = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < end && !Enumerable.Range(0, Caching.cacheCount).All(i => Caching.GetCacheAt(i).ready)) yield return null;
        W("ready.all", string.Join(",", Enumerable.Range(0, Caching.cacheCount).Select(i => Caching.GetCacheAt(i).ready)));
        yield return Bundles(a);
        yield return Expiration();
        yield return SpaceLimit();
        yield return Audio();
        Unload();
        W("done", Logs());
        log.Dispose();
        File.WriteAllText(Path.Combine(Out, "finished"), "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ Caching and Cache

    void Defaults()
    {
        W("caching.ready", Caching.ready);
        W("caching.cacheCount", Caching.cacheCount);
        W("caching.compressionEnabled", Caching.compressionEnabled);
        var d = Caching.defaultCache;
        defaultPath = d.path?.Replace('\\', '/');
        W("os.platform", Application.platform);
        W("os.defaultPath", defaultPath?.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/'), "~"));
        W("default.isCurrent", d == Caching.currentCacheForWriting);
        W("default.isAt0", d == Caching.GetCacheAt(0));
        W("default.clear", Caching.ClearCache());
        W("default", Describe(d));
        W("default.paths", Paths());
        W("invalid.valid", default(Cache).valid);
        W("invalid.index", Try(() => default(Cache).index));
        W("invalid.ready", Try(() => default(Cache).ready));
        W("invalid.path", Try(() => default(Cache).path));
        W("invalid.occupied", Try(() => default(Cache).spaceOccupied));
        W("invalid.equalsDefault", default(Cache) == d);
        W("defaults.logs", Logs());
    }

    Cache Caches()
    {
        string A = Path.Combine(Out, "cacheA"), B = Path.Combine(Out, "cacheB"), R = Path.Combine(Out, "cacheR");
        foreach (var dir in new[] { A, B, R }) Directory.CreateDirectory(dir);
        W("add.missing", Try(() => Describe(Caching.AddCache(Path.Combine(Out, "missing")))));
        W("add.missing.exists", Directory.Exists(Path.Combine(Out, "missing")));
        W("add.empty", Try(() => Describe(Caching.AddCache(""))));
        W("add.null", Try(() => Describe(Caching.AddCache(null))));
        W("add.count0", Caching.cacheCount);
        var a = Caching.AddCache(A);
        W("add.a", Describe(a));
        W("add.a.files", Files(A));
        W("add.again", Try(() => Describe(Caching.AddCache(A))));
        W("add.slash", Try(() => Describe(Caching.AddCache(A + "/"))));
        var b = Caching.AddCache(B);
        var r = Caching.AddCache(R);
        W("add.b", Describe(b));
        W("add.r", Describe(r));
        W("add.count", Caching.cacheCount);
        W("add.paths", Paths());
        W("add.logs", Logs());
        W("at.-1", Try(() => Describe(Caching.GetCacheAt(-1))));
        W("at.count", Try(() => Describe(Caching.GetCacheAt(Caching.cacheCount))));
        W("at.1", Caching.GetCacheAt(1) == a);
        W("byPath.b", Try(() => Describe(Caching.GetCacheByPath(B))));
        W("byPath.slash", Try(() => Describe(Caching.GetCacheByPath(B + "/"))));
        W("byPath.missing", Try(() => Describe(Caching.GetCacheByPath(Path.Combine(Out, "nowhere")))));
        W("byPath.default", Caching.GetCacheByPath(defaultPath) == Caching.defaultCache);
        W("byPath.empty", Try(() => Describe(Caching.GetCacheByPath(""))));
        W("at.logs", Logs());
        Caching.MoveCacheBefore(b, a);
        W("order.bBeforeA", Paths());
        Caching.MoveCacheAfter(b, r);
        W("order.bAfterR", Paths());
        Caching.MoveCacheBefore(r, Caching.defaultCache);
        W("order.rBeforeDefault", Paths());
        W("order.indices", $"{Caching.defaultCache.index} {a.index} {b.index} {r.index}");
        W("order.default", Caching.defaultCache.index);
        Caching.MoveCacheAfter(r, b);
        W("order.restored", Paths());
        W("order.same", Try(() => { Caching.MoveCacheBefore(a, a); return Paths(); }));
        W("order.invalid", Try(() => { Caching.MoveCacheBefore(default(Cache), a); return Paths(); }));
        W("order.logs", Logs());
        W("current.r", Try(() => { Caching.currentCacheForWriting = r; return Rel(Caching.currentCacheForWriting.path); }));
        W("current.invalid", Try(() => { Caching.currentCacheForWriting = default(Cache); return Rel(Caching.currentCacheForWriting.path); }));
        Caching.currentCacheForWriting = a;
        W("current.a", Caching.currentCacheForWriting == a);
        W("current.logs", Logs());
        W("expiration.default", b.expirationDelay);
        b.expirationDelay = 100;
        W("expiration.100", b.expirationDelay);
        b.expirationDelay = 13000000;
        W("expiration.over", b.expirationDelay);
        W("expiration.over.logs", Logs());
        b.expirationDelay = -5;
        W("expiration.negative", b.expirationDelay);
        b.expirationDelay = 0;
        W("expiration.zero", b.expirationDelay);
        W("expiration.r", Try(() => { r.expirationDelay = 50; return r.expirationDelay; }));
        W("expiration.invalid", Try(() => { var c = default(Cache); c.expirationDelay = 50; return c.expirationDelay; }));
        W("expiration.caching", Caching.expirationDelay == Caching.currentCacheForWriting.expirationDelay);
        W("expiration.logs", Logs());
        var max = b.maximumAvailableStorageSpace;
        b.maximumAvailableStorageSpace = 1000;
        W("max.1000", Describe(b));
        b.maximumAvailableStorageSpace = -1;
        W("max.negative", Describe(b));
        b.maximumAvailableStorageSpace = 0;
        W("max.zero", Describe(b));
        W("max.r", Try(() => { r.maximumAvailableStorageSpace = 10; return r.maximumAvailableStorageSpace; }));
        b.maximumAvailableStorageSpace = max;
        b.expirationDelay = 12960000;
        W("max.restored", Describe(b));
        W("max.caching", Caching.maximumAvailableDiskSpace == Caching.currentCacheForWriting.maximumAvailableStorageSpace);
        W("max.logs", Logs());
        W("remove.default", Try(() => Caching.RemoveCache(Caching.defaultCache)));
        W("remove.invalid", Try(() => Caching.RemoveCache(default(Cache))));
        var T = Path.Combine(Out, "cacheT");
        Directory.CreateDirectory(T);
        var t = Caching.AddCache(T);
        W("remove.t", Try(() => Caching.RemoveCache(t)));
        W("remove.t.valid", t.valid);
        W("remove.t.path", Try(() => t.path));
        W("remove.t.again", Try(() => Caching.RemoveCache(t)));
        W("remove.t.exists", Directory.Exists(T));
        W("remove.missingDir", Try(() => Describe(Caching.AddCache(Path.Combine(Out, "cacheB2")))));
        Directory.CreateDirectory(Path.Combine(Out, "cacheC"));
        var c2 = Caching.AddCache(Path.Combine(Out, "cacheC"));
        Caching.currentCacheForWriting = c2;
        W("remove.current", Try(() => Caching.RemoveCache(c2)));
        W("remove.current.after", Try(() => Rel(Caching.currentCacheForWriting.path)));
        Caching.currentCacheForWriting = a;
        // A directory without write permission makes a read-only cache.
        var RO = Path.Combine(Out, "cacheRO");
        var ro = new DirectoryInfo(RO);
        ro.Create();
        ro.Attributes |= FileAttributes.ReadOnly;
        var readOnly = Caching.AddCache(RO);
        W("readonly.add", Try(() => Describe(readOnly)));
        W("readonly.current", Try(() => { Caching.currentCacheForWriting = readOnly; return Rel(Caching.currentCacheForWriting.path); }));
        W("readonly.clear", Try(() => readOnly.ClearCache()));
        W("readonly.logs", Logs());
        Caching.currentCacheForWriting = a;
        W("readonly.remove", Caching.RemoveCache(readOnly));
        ro.Attributes &= ~FileAttributes.ReadOnly;
        W("remove.count", Caching.cacheCount);
        W("remove.paths", Paths());
        W("remove.logs", Logs());
        return a;
    }

    // ------------------------------------------------------------------ AssetBundle downloads

    void Unload()
    {
        foreach (var bundle in loaded) if (bundle != null) bundle.Unload(true);
        loaded.Clear();
    }

    /// <summary>Sends an AssetBundle request and records the request and the bundle it gives.</summary>
    IEnumerator Get(string key, UnityWebRequest request, string requestPath = null, bool keep = false)
    {
        var handler = (DownloadHandlerAssetBundle)request.downloadHandler;
        int before = requestPath == null ? 0 : Requests(requestPath);
        yield return request.SendWebRequest();
        W(key + ".result", $"{request.result} code={request.responseCode} error={request.error ?? "null"} " +
            $"bytes={request.downloadedBytes} progress={request.downloadProgress.ToString("R", CultureInfo.InvariantCulture)} complete={handler.isDownloadComplete}");
        if (requestPath != null) W(key + ".requests", Requests(requestPath) - before);
        W(key + ".handlerError", handler.error ?? "null");
        W(key + ".data", Try(() => handler.data?.Length));
        var bundle = handler.assetBundle;
        W(key + ".bundle", bundle == null ? "null" : bundle.name + ":" + string.Join(",", bundle.GetAllAssetNames()));
        if (bundle != null)
        {
            var text = bundle.LoadAsset<TextAsset>(bundle.GetAllAssetNames()[0]);
            W(key + ".text", text == null ? "null" : text.text.Length.ToString(CultureInfo.InvariantCulture));
        }
        W(key + ".same", Try(() => ReferenceEquals(bundle, DownloadHandlerAssetBundle.GetContent(request))));
        W(key + ".logs", Logs());
        if (bundle != null) loaded.Add(bundle);
        request.Dispose();
        if (!keep) Unload();
    }

    IEnumerator Bundles(Cache a)
    {
        string A = Path.Combine(Out, "cacheA");
        string alpha = Bundle("alpha?v=1"), beta = Bundle("beta.bundle"), gamma = Bundle("gamma.unity3d");
        // The first download keeps the bundle loaded for the in-use controls.
        yield return Get("b1", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, 0), "/bundles/alpha?v=1", keep: true);
        W("b1.cached", Caching.IsVersionCached(alpha, H1));
        W("b1.cachedOtherUrl", Caching.IsVersionCached("http://example.invalid/dir/alpha.bundle?x=2", H1));
        W("b1.cachedPlainName", Caching.IsVersionCached("alpha", H1));
        W("b1.cachedNamed", Caching.IsVersionCached(new CachedAssetBundle("alpha", H1)));
        W("b1.cachedOtherHash", Caching.IsVersionCached(alpha, H2));
        W("b1.versions", Versions("alpha"));
        W("b1.versionsUrl", Versions(alpha));
        W("b1.files", Files(A));
        W("b1.cache", Describe(a));
        W("b1.after.logs", Logs());
        yield return Get("b1.loaded", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, 0), "/bundles/alpha?v=1");
        yield return Get("b1.hit", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, 0), "/bundles/alpha?v=1");
        yield return Get("b1.hitQuery", UnityWebRequestAssetBundle.GetAssetBundle(Bundle("alpha?other"), H1, 0), "/bundles/alpha?other");
        // A nonzero CRC with a cached version: the good one, again, then a wrong one.
        yield return Get("b1.hitGoodCrc", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, crcs["alpha"]), "/bundles/alpha?v=1");
        W("b1.hitGoodCrc.cached", Caching.IsVersionCached(alpha, H1));
        yield return Get("b1.hitGoodCrc.again", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, crcs["alpha"]), "/bundles/alpha?v=1");
        yield return Get("b1.hitBadCrc", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, 12345), "/bundles/alpha?v=1");
        W("b1.hitBadCrc.cached", Caching.IsVersionCached(alpha, H1));
        W("b1.hitBadCrc.versions", Versions("alpha"));
        yield return Get("b1.afterBadCrc", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, 0), "/bundles/alpha?v=1");
        yield return Get("b2", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H2, 0), "/bundles/alpha?v=1");
        W("b2.versions", Versions("alpha"));
        yield return Get("plain", UnityWebRequestAssetBundle.GetAssetBundle(alpha), "/bundles/alpha?v=1");
        yield return Get("plain.again", UnityWebRequestAssetBundle.GetAssetBundle(alpha, 0), "/bundles/alpha?v=1");
        W("plain.versions", Versions("alpha"));

        yield return Get("v7", UnityWebRequestAssetBundle.GetAssetBundle(beta, 7, 0), "/bundles/beta.bundle");
        W("v7.versions", Versions("beta"));
        W("v7.cachedHash", Caching.IsVersionCached(beta, new Hash128(0, 0, 0, 7)));
#pragma warning disable 618
        W("v7.cachedInt", Caching.IsVersionCached(beta, 7));
        W("v7.cachedInt8", Caching.IsVersionCached(beta, 8));
#pragma warning restore 618
        yield return Get("v7.hit", UnityWebRequestAssetBundle.GetAssetBundle(beta, 7, 0), "/bundles/beta.bundle");
        yield return Get("v0", UnityWebRequestAssetBundle.GetAssetBundle(beta, 0, 0), "/bundles/beta.bundle");
        W("v0.versions", Versions("beta"));
        yield return Get("v0.again", UnityWebRequestAssetBundle.GetAssetBundle(beta, 0, 0), "/bundles/beta.bundle");
        yield return Get("h0", UnityWebRequestAssetBundle.GetAssetBundle(beta, default(Hash128), 0), "/bundles/beta.bundle");
        W("h0.versions", Versions("beta"));

        yield return Get("named", UnityWebRequestAssetBundle.GetAssetBundle(gamma, new CachedAssetBundle("custom", H3), 0), "/bundles/gamma.unity3d");
        W("named.versions", Versions("custom"));
        W("named.versionsUrlName", Versions("gamma"));
        W("named.cachedUrl", Caching.IsVersionCached(gamma, H3));
        W("named.cached", Caching.IsVersionCached(new CachedAssetBundle("custom", H3)));
        W("named.files", Files(A));
        W("named.cache", Describe(a));
        yield return Get("named.hit", UnityWebRequestAssetBundle.GetAssetBundle(Bundle("elsewhere"), new CachedAssetBundle("custom", H3), 0), "/bundles/elsewhere");
        yield return Get("named.empty", UnityWebRequestAssetBundle.GetAssetBundle(gamma, new CachedAssetBundle("", H4), 0), "/bundles/gamma.unity3d");
        W("named.empty.versions", Versions("gamma"));
        Caching.compressionEnabled = false;
        yield return Get("raw", UnityWebRequestAssetBundle.GetAssetBundle(gamma, new CachedAssetBundle("custom2", H3), 0), "/bundles/gamma.unity3d");
        Caching.compressionEnabled = true;
        W("raw.files", Files(A));
        W("raw.cache", Describe(a));
        Caching.compressionEnabled = false;
        yield return Get("raw.beta", UnityWebRequestAssetBundle.GetAssetBundle(beta, new CachedAssetBundle("custom3", H3), 0), "/bundles/beta.bundle");
        Caching.compressionEnabled = true;
        W("raw.beta.files", Files(A));
        Caching.compressionEnabled = false;
        yield return Get("raw.alpha", UnityWebRequestAssetBundle.GetAssetBundle(alpha, new CachedAssetBundle("custom4", H3), 0), "/bundles/alpha?v=1");
        Caching.compressionEnabled = true;
        W("raw.alpha.files", Files(Path.Combine(A, "custom4")));
        yield return Get("raw.alpha.hit", UnityWebRequestAssetBundle.GetAssetBundle(alpha, new CachedAssetBundle("custom4", H3), 0), "/bundles/alpha?v=1");

        yield return Get("crc.ok", UnityWebRequestAssetBundle.GetAssetBundle(beta, H4, crcs["beta.bundle"]), "/bundles/beta.bundle");
        yield return Get("crc.ok.again", UnityWebRequestAssetBundle.GetAssetBundle(beta, H4, crcs["beta.bundle"]), "/bundles/beta.bundle");
        yield return Get("crc.ok.zero", UnityWebRequestAssetBundle.GetAssetBundle(beta, H4, 0), "/bundles/beta.bundle");
        yield return Get("crc.bad", UnityWebRequestAssetBundle.GetAssetBundle(beta, H5, crcs["beta.bundle"] ^ 1), "/bundles/beta.bundle");
        yield return Get("crc.badAlpha", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H5, crcs["alpha"] ^ 1), "/bundles/alpha?v=1");
        yield return Get("crc.badGamma", UnityWebRequestAssetBundle.GetAssetBundle(gamma, H5, crcs["gamma.unity3d"] ^ 1), "/bundles/gamma.unity3d");
        yield return Get("crc.badPlain", UnityWebRequestAssetBundle.GetAssetBundle(alpha, crcs["alpha"] ^ 1), "/bundles/alpha?v=1");
        yield return Get("crc.goodGamma", UnityWebRequestAssetBundle.GetAssetBundle(gamma, H6, crcs["gamma.unity3d"]), "/bundles/gamma.unity3d");
        W("crc.versions", Versions("alpha") + " / " + Versions("gamma"));
        W("crc.bad.versions", Versions("beta"));
        yield return Get("crc.bad.again", UnityWebRequestAssetBundle.GetAssetBundle(beta, H5, 0), "/bundles/beta.bundle");
        yield return Get("missing", UnityWebRequestAssetBundle.GetAssetBundle(Bundle("nothere"), H1, 0), "/bundles/nothere");
        W("missing.cached", Caching.IsVersionCached(Bundle("nothere"), H1));
        W("missing.files", Files(A));
        var notBundle = UnityWebRequestAssetBundle.GetAssetBundle(url + "audio/tone.wav", H1, 0);
        yield return Get("garbage", notBundle, "/audio/tone.wav");
        yield return Get("garbage.plain", UnityWebRequestAssetBundle.GetAssetBundle(url + "audio/tone.wav"), "/audio/tone.wav");
        var garbageAuto = UnityWebRequestAssetBundle.GetAssetBundle(url + "audio/tone.wav");
        ((DownloadHandlerAssetBundle)garbageAuto.downloadHandler).autoLoadAssetBundle = true;
        yield return Get("garbage.auto", garbageAuto, "/audio/tone.wav");
        W("garbage.cached", Caching.IsVersionCached(url + "audio/tone.wav", H1));

        var auto = UnityWebRequestAssetBundle.GetAssetBundle(alpha, H6, 0);
        ((DownloadHandlerAssetBundle)auto.downloadHandler).autoLoadAssetBundle = true;
        yield return Get("auto", auto, "/bundles/alpha?v=1");
        var autoHit = UnityWebRequestAssetBundle.GetAssetBundle(alpha, H6, 0);
        ((DownloadHandlerAssetBundle)autoHit.downloadHandler).autoLoadAssetBundle = true;
        yield return Get("auto.hit", autoHit, "/bundles/alpha?v=1");

        // Lookups search every cache; writing goes to the current one.
        var b = Caching.GetCacheByPath(Path.Combine(Out, "cacheB"));
        Caching.currentCacheForWriting = b;
        yield return Get("inB", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H7, 0), "/bundles/alpha?v=1");
        Caching.currentCacheForWriting = a;
        W("inB.files", Files(Path.Combine(Out, "cacheB")));
        W("inB.cached", Caching.IsVersionCached(alpha, H7));
        W("inB.versions", Versions("alpha"));
        yield return Get("inB.hit", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H7, 0), "/bundles/alpha?v=1");
        W("inB.hit.filesA", Files(A));

        W("mark.cached", Caching.MarkAsUsed(alpha, H1));
        W("mark.missing", Caching.MarkAsUsed(alpha, H9));
        W("mark.named", Caching.MarkAsUsed(new CachedAssetBundle("custom", H3)));
        W("mark.inB", Caching.MarkAsUsed(alpha, H7));
        W("mark.logs", Logs());

        W("clear.version", Caching.ClearCachedVersion("alpha", H1));
        W("clear.version.again", Caching.ClearCachedVersion("alpha", H1));
        W("clear.version.versions", Versions("alpha"));
        W("clear.others", Caching.ClearOtherCachedVersions("beta", H4));
        W("clear.others.versions", Versions("beta"));
        W("clear.others.none", Caching.ClearOtherCachedVersions("nothing", H4));
        W("clear.all", Caching.ClearAllCachedVersions("custom"));
        W("clear.all.versions", Versions("custom"));
        W("clear.all.none", Caching.ClearAllCachedVersions("nothing"));
        W("clear.files", Files(A));
        W("clear.logs", Logs());

        yield return Get("inuse", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H2, 0), "/bundles/alpha?v=1", keep: true);
        W("inuse.version", Caching.ClearCachedVersion("alpha", H2));
        W("inuse.version.logs", Logs());
        W("inuse.others", Caching.ClearOtherCachedVersions("alpha", H6));
        W("inuse.others.versions", Versions("alpha"));
        W("inuse.all", Caching.ClearAllCachedVersions("alpha"));
        W("inuse.all.versions", Versions("alpha"));
        W("inuse.clear", Caching.ClearCache());
        W("inuse.clear.logs", Logs());
        W("inuse.clear.versions", Versions("alpha"));
        W("inuse.clear.files", Files(A));
        Unload();
        W("inuse.after", Caching.ClearCache());
        W("inuse.after.files", Files(A) + " / " + Files(Path.Combine(Out, "cacheB")));
        W("inuse.after.cache", Describe(a));
        W("inuse.after.logs", Logs());
    }

    /// <summary>Waits by the system clock, which cache times follow.</summary>
    static IEnumerator WaitWallClock(double seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end) yield return null;
    }

    Cache Fresh(string name)
    {
        var dir = Path.Combine(Out, name);
        Directory.CreateDirectory(dir);
        var cache = Caching.AddCache(dir);
        Caching.currentCacheForWriting = cache;
        return cache;
    }

    IEnumerator Expiration()
    {
        string alpha = Bundle("alpha?v=1"), beta = Bundle("beta.bundle");
        // ClearCache(expiration) removes what has not been used for longer than expiration.
        W("expire.clearAll", Caching.ClearCache());
        var x = Fresh("cacheX");
        yield return Get("expire.alpha", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, 0));
        yield return WaitWallClock(3.5);
        yield return Get("expire.beta", UnityWebRequestAssetBundle.GetAssetBundle(beta, H1, 0));
        W("expire.3600", x.ClearCache(3600));
        W("expire.3600.versions", Versions("alpha") + " / " + Versions("beta"));
        W("expire.2", x.ClearCache(2));
        W("expire.2.versions", Versions("alpha") + " / " + Versions("beta"));
        W("expire.2.cache", Describe(x));
        W("expire.logs", Logs());
        // A use refreshes the time: marked and reloaded entries stay.
        W("used.clearAll", Caching.ClearCache());
        var y = Fresh("cacheY");
        yield return Get("used.alpha", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H1, 0));
        yield return Get("used.beta", UnityWebRequestAssetBundle.GetAssetBundle(beta, H1, 0));
        yield return Get("used.gamma", UnityWebRequestAssetBundle.GetAssetBundle(Bundle("gamma.unity3d"), H1, 0));
        yield return WaitWallClock(3.5);
        W("used.mark", Caching.MarkAsUsed(alpha, H1));
        yield return Get("used.reload", UnityWebRequestAssetBundle.GetAssetBundle(beta, H1, 0), "/bundles/beta.bundle");
        W("used.2", Caching.ClearCache(2));
        W("used.2.versions", Versions("alpha") + " / " + Versions("beta") + " / " + Versions("gamma"));
        W("used.cache", Describe(y));
        // An expiration delay removes stale entries when the cache takes a new one.
        W("delay.clearAll", Caching.ClearCache());
        var e = Fresh("cacheE");
        e.expirationDelay = 1;
        yield return Get("delay.alpha", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H2, 0));
        yield return WaitWallClock(3.5);
        W("delay.waited", Versions("alpha"));
        yield return Get("delay.beta", UnityWebRequestAssetBundle.GetAssetBundle(beta, H2, 0));
        W("delay.versions", Versions("alpha") + " / " + Versions("beta"));
        W("delay.cache", Describe(e));
        W("delay.logs", Logs());
        // Reading a cache's index again drops what has expired.
        Caching.currentCacheForWriting = Caching.defaultCache;
        W("delay.remove", Caching.RemoveCache(e));
        var reopened = Caching.AddCache(Path.Combine(Out, "cacheE"));
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!reopened.ready && DateTime.UtcNow < end) yield return null;
        W("delay.reopen", Versions("alpha") + " " + Describe(reopened));
        W("delay.reopen.files", Files(Path.Combine(Out, "cacheE")));
        W("delay.reopen.logs", Logs());
    }

    IEnumerator SpaceLimit()
    {
        string alpha = Bundle("alpha?v=1"), beta = Bundle("beta.bundle"), gamma = Bundle("gamma.unity3d");
        W("space.clearAll", Caching.ClearCache());
        var s = Fresh("cacheS");
        yield return Get("space.alpha", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H3, 0));
        long one = s.spaceOccupied;
        yield return WaitWallClock(1.2);
        yield return Get("space.beta", UnityWebRequestAssetBundle.GetAssetBundle(beta, H3, 0));
        long two = s.spaceOccupied;
        W("space.sizes", one + " " + two);
        // Room for the newest entry only: taking one more removes the least recently used.
        s.maximumAvailableStorageSpace = two;
        yield return WaitWallClock(1.2);
        W("space.markAlpha", Caching.MarkAsUsed(alpha, H3));
        yield return Get("space.gamma", UnityWebRequestAssetBundle.GetAssetBundle(gamma, H3, 0));
        W("space.versions", Versions("alpha") + " / " + Versions("beta") + " / " + Versions("gamma"));
        W("space.cache", Describe(s));
        W("space.logs", Logs());
        // Less room than one bundle: the bundle loads but is not kept.
        W("tiny.clearAll", Caching.ClearCache());
        var t = Fresh("cacheTiny");
        t.maximumAvailableStorageSpace = 16;
        yield return Get("tiny", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H4, 0), "/bundles/alpha?v=1");
        W("tiny.versions", Versions("alpha"));
        W("tiny.cache", Describe(t));
        W("tiny.files", Files(Path.Combine(Out, "cacheTiny")));
        yield return Get("tiny.again", UnityWebRequestAssetBundle.GetAssetBundle(alpha, H4, 0), "/bundles/alpha?v=1");
        // Lowering the limit below what is stored does not remove anything by itself.
        s.maximumAvailableStorageSpace = 16;
        W("space.lowered", Versions("alpha") + " / " + Versions("beta") + " / " + Versions("gamma"));
        W("space.lowered.cache", Describe(s));
        W("space.lowered.logs", Logs());
        // The room a download needs: its uncompressed archive when the cache stores it in another
        // form, else the downloaded archive.
        var limits = new (string bundle, long max)[]
        {
            ("gamma.unity3d", 4823), ("gamma.unity3d", 4824), ("gamma.unity3d", 4950), ("gamma.unity3d", 4951),
            ("alpha?v=1", 2911), ("alpha?v=1", 2912), ("beta.bundle", 1209), ("beta.bundle", 1210),
        };
        for (int i = 0; i < limits.Length; i++)
        {
            var (bundle, max) = limits[i];
            var hash = new Hash128(0, 0, 0, (uint)(100 + i));
            var z = Fresh("cacheZ" + i);
            z.maximumAvailableStorageSpace = max;
            yield return Get($"limit.{bundle}.{max}", UnityWebRequestAssetBundle.GetAssetBundle(Bundle(bundle), hash, 0));
            W($"limit.{bundle}.{max}.cached", Caching.IsVersionCached(Bundle(bundle), hash) + " " + Describe(z));
        }
        Caching.compressionEnabled = false;
        var raw = Fresh("cacheZraw");
        raw.maximumAvailableStorageSpace = 1209;
        yield return Get("limit.raw.beta", UnityWebRequestAssetBundle.GetAssetBundle(beta, H9, 0));
        W("limit.raw.beta.cached", Caching.IsVersionCached(beta, H9) + " " + Describe(raw));
        raw.maximumAvailableStorageSpace = 2944;
        yield return Get("limit.raw.beta2", UnityWebRequestAssetBundle.GetAssetBundle(beta, H9, 0));
        W("limit.raw.beta2.cached", Caching.IsVersionCached(beta, H9) + " " + Describe(raw));
        Caching.compressionEnabled = true;
        Caching.currentCacheForWriting = Caching.defaultCache;
    }

    // ------------------------------------------------------------------ audio downloads

    IEnumerator Clip(string key, string file, AudioType type, bool stream = false, bool compressed = false)
    {
        var request = UnityWebRequestMultimedia.GetAudioClip(url + file, type);
        var handler = (DownloadHandlerAudioClip)request.downloadHandler;
        if (stream) handler.streamAudio = true;
        if (compressed) handler.compressed = true;
        W(key + ".flags", $"stream={handler.streamAudio} compressed={handler.compressed}");
        W(key + ".before", Try(() => handler.audioClip == null ? "null" : "clip"));
        yield return request.SendWebRequest();
        W(key + ".result", $"{request.result} code={request.responseCode} error={request.error ?? "null"} bytes={request.downloadedBytes} done={handler.isDone}");
        W(key + ".handlerError", handler.error ?? "null");
        W(key + ".data", Try(() => handler.data?.Length));
        W(key + ".text", Try(() => handler.text?.Length));
        var clip = ClipOf(handler, out var failure);
        W(key + ".clip", failure ?? DescribeClip(clip));
        W(key + ".same", Try(() => ReferenceEquals(clip, handler.audioClip)));
        W(key + ".content", Try(() => ReferenceEquals(clip, DownloadHandlerAudioClip.GetContent(request))));
        if (clip != null)
        {
            W(key + ".samples", Samples(clip));
            W(key + ".loadAudioData", clip.LoadAudioData() + " " + clip.loadState);
        }
        W(key + ".logs", Logs());
        request.Dispose();
        W(key + ".disposed", clip == null ? "null" : (clip != null) + " " + clip.loadState);
        if (clip != null) Destroy(clip);
    }

    static AudioClip ClipOf(DownloadHandlerAudioClip handler, out string failure)
    {
        failure = null;
        try { return handler.audioClip; }
        catch (Exception e) { failure = "throws " + e.GetType().Name + ": " + e.Message; return null; }
    }

    static string DescribeClip(AudioClip clip) => clip == null ? "null" :
        $"name='{clip.name}' length={clip.length.ToString("R", CultureInfo.InvariantCulture)} samples={clip.samples} " +
        $"channels={clip.channels} frequency={clip.frequency} loadType={clip.loadType} loadState={clip.loadState} " +
        $"preload={clip.preloadAudioData} background={clip.loadInBackground} ambisonic={clip.ambisonic} hide={clip.hideFlags}";

    /// <summary>GetData of the whole clip: its result, its peak, its mean square and a few values (4 decimals).</summary>
    static string Samples(AudioClip clip)
    {
        var data = new float[Math.Max(1, clip.samples * clip.channels)];
        bool ok = clip.GetData(data, 0);
        double square = 0, peak = 0;
        foreach (var v in data) { square += v * v; peak = Math.Max(peak, Math.Abs(v)); }
        string At(int i) => i < data.Length ? data[i].ToString("F4", CultureInfo.InvariantCulture) : "-";
        int firstNonZero = Array.FindIndex(data, v => v != 0);
        return FormattableString.Invariant($"ok={ok} peak={peak:F4} meanSquare={square / data.Length:F5} first={firstNonZero} ") +
            $"at={At(1)},{At(100)},{At(1001)},{At(data.Length / 2)}";
    }

    IEnumerator Audio()
    {
        var files = new (string file, AudioType type)[]
        {
            ("audio/tone.wav", AudioType.WAV), ("audio/tone24.wav", AudioType.WAV),
            ("audio/stereo.ogg", AudioType.OGGVORBIS), ("audio/tone.mp3", AudioType.MPEG),
            ("audio/adpcm.wav", AudioType.WAV),
        };
        foreach (var (file, type) in files)
        {
            var name = Path.GetFileName(file);
            yield return Clip("audio." + name, file, type);
            yield return Clip("audio." + name + ".compressed", file, type, compressed: true);
            yield return Clip("audio." + name + ".stream", file, type, stream: true);
        }
        // MPEG lengths: with and without tags and an Info header, longer, stereo, VBR, MPEG-2.
        foreach (var name in new[] { "plain.mp3", "long.mp3", "vbr.mp3", "low.mp3" })
            yield return Clip("audio." + name, "audio/" + name, AudioType.MPEG);
        yield return Clip("audio.unknown.wav", "audio/tone.wav", AudioType.UNKNOWN);
        yield return Clip("audio.unknown.ogg", "audio/stereo.ogg", AudioType.UNKNOWN);
        yield return Clip("audio.mismatch.ogg", "audio/stereo.ogg", AudioType.WAV);
        yield return Clip("audio.mismatch.wav", "audio/tone.wav", AudioType.MPEG);
        yield return Clip("audio.notaudio", "audio/notaudio.wav", AudioType.WAV);
        yield return Clip("audio.missing", "audio/nothere.wav", AudioType.WAV);
        yield return Clip("audio.both", "audio/tone.wav", AudioType.WAV, stream: true, compressed: true);

        var flags = (DownloadHandlerAudioClip)UnityWebRequestMultimedia.GetAudioClip(url + "audio/tone.wav", AudioType.WAV).downloadHandler;
        flags.compressed = true;
        flags.streamAudio = true;
        W("flags.compressedThenStream", $"stream={flags.streamAudio} compressed={flags.compressed} logs={Logs()}");
        flags.compressed = false;
        flags.compressed = true;
        W("flags.streamThenCompressed", $"stream={flags.streamAudio} compressed={flags.compressed} logs={Logs()}");
        flags.Dispose();
        var plainHandler = new DownloadHandlerAudioClip(url + "audio/tone.wav", AudioType.WAV);
        W("flags.constructed", $"stream={plainHandler.streamAudio} compressed={plainHandler.compressed} clip={Try(() => plainHandler.audioClip == null ? "null" : "clip")} logs={Logs()}");
        plainHandler.Dispose();

        yield return Partial("partial.stream", true);
        yield return Partial("partial.plain", false);
    }

    /// <summary>A clip read while only the first part of its file has arrived.</summary>
    IEnumerator Partial(string key, bool stream)
    {
        var gate = "long.wav-" + key;
        var request = UnityWebRequestMultimedia.GetAudioClip(url + "gated/audio/long.wav?" + key, AudioType.WAV);
        var handler = (DownloadHandlerAudioClip)request.downloadHandler;
        handler.streamAudio = stream;
        var operation = request.SendWebRequest();
        var end = DateTime.UtcNow.AddSeconds(10);
        while ((!File.Exists(Path.Combine(Out, gate + ".sent")) || request.downloadedBytes < 16384) && DateTime.UtcNow < end)
            yield return null;
        for (int i = 0; i < 5; i++) yield return null;
        W(key + ".partial", $"bytes={request.downloadedBytes} done={request.isDone} handlerDone={handler.isDone}");
        var clip = ClipOf(handler, out var failure);
        W(key + ".partial.clip", failure ?? DescribeClip(clip));
        W(key + ".partial.logs", Logs());
        File.WriteAllText(Path.Combine(Out, gate + ".release"), "");
        yield return operation;
        W(key + ".result", $"{request.result} bytes={request.downloadedBytes}");
        var after = ClipOf(handler, out failure);
        W(key + ".clip", failure ?? DescribeClip(after));
        W(key + ".sameAsPartial", clip != null && ReferenceEquals(clip, after));
        if (after != null) W(key + ".samples", Samples(after));
        W(key + ".logs", Logs());
        request.Dispose();
    }
}
