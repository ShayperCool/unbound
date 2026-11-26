using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $TEXTUREPLATFORMS_OUT (tests/value_probe.py rows): what
/// TextureImporter's per-platform settings API reads, stores and clears, the .meta files it saves,
/// the imported size with the active platform's settings, and PNG gamma (ignorePngGamma) in the
/// imported pixels. GUID lines read "guid: (guid)".
/// </summary>
public class TexturePlatformsProbe : MonoBehaviour
{
    const string Variable = "TEXTUREPLATFORMS_OUT";
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static readonly Regex s_Guid = new Regex("(?m)^guid: [0-9a-f]{32}$");

    static void Meta(string key, string path)
    {
        var meta = path + ".meta";
        Row(key, File.Exists(meta) ? s_Guid.Replace(File.ReadAllText(meta), "guid: (guid)") : "<none>");
    }

    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        // The type alone: the runtime words its own messages (Mono's and CoreCLR's differ).
        catch (Exception e) { result = "throws " + e.GetType().FullName; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static string Describe(TextureImporterPlatformSettings s) => s == null ? "null" :
        "name " + (s.name == null ? "null" : "'" + s.name + "'") + " overridden " + s.overridden + " ignorePlatformSupport " + s.ignorePlatformSupport
        + " max " + s.maxTextureSize + " resize " + s.resizeAlgorithm + " format " + (int)s.format + " compression " + s.textureCompression
        + " quality " + s.compressionQuality + " crunched " + s.crunchedCompression + " alphaSplit " + s.allowsAlphaSplitting
        + " etc2Fallback " + s.androidETC2FallbackOverride;

    static TextureImporter Importer(string name) => (TextureImporter)AssetImporter.GetAtPath(Path(name));
    static string Path(string name) => "Assets/Textures/" + name + ".png";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("TexturePlatformsProbe").AddComponent<TexturePlatformsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(System.IO.Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static readonly string[] Queries =
    {
        "DefaultTexturePlatform", "defaulttextureplatform", "Standalone", "StandaloneOSX", "StandaloneWindows64", "Android",
        "android", "iOS", "iPhone", "WebGL", "tvOS", "Foo", "", null,
    };

    static void Run()
    {
        Get();
        Set();
        SetOff();
        Legacy();
        Clear();
        Defaults();
        Load();
        Size();
        Gamma();
    }

    static void Get()
    {
        var importer = Importer("Get");
        foreach (var query in Queries)
            Try("get/" + (query ?? "(null)"), () => Describe(importer.GetPlatformTextureSettings(query)));
        Try("get/default", () => Describe(importer.GetDefaultPlatformTextureSettings()));
        Try("get/properties", () => "max " + importer.maxTextureSize + " compression " + importer.textureCompression + " quality " + importer.compressionQuality
            + " crunched " + importer.crunchedCompression + " alphaSplit " + importer.allowAlphaSplitting + " etc2Fallback " + importer.androidETC2FallbackOverride);
        foreach (var query in new[] { "Android", "iPhone", "WebGL", "Standalone" })
            Try("get/out/" + query, () =>
            {
                bool overridden = importer.GetPlatformTextureSettings(query, out int max, out TextureImporterFormat format, out int quality, out bool split);
                bool three = importer.GetPlatformTextureSettings(query, out int max3, out TextureImporterFormat format3);
                return "returns " + overridden + " max " + max + " format " + (int)format + " quality " + quality + " split " + split
                    + " | three " + three + " max " + max3 + " format " + (int)format3;
            });
        Try("get/dirty", () => EditorUtility.IsDirty(importer).ToString());
        // A copy each time.
        Try("get/copy", () =>
        {
            var first = importer.GetPlatformTextureSettings("Android");
            first.maxTextureSize = 32;
            return Describe(importer.GetPlatformTextureSettings("Android"));
        });
        // The default entry changed through a property, before a save: what a platform without an
        // entry reads then.
        Try("get/afterProperty", () =>
        {
            importer.maxTextureSize = 8192;
            return Describe(importer.GetPlatformTextureSettings("iOS")) + " | " + Describe(importer.GetPlatformTextureSettings("Standalone"));
        });
        Meta("get/meta", Path("Get"));
    }

    static TextureImporterPlatformSettings Settings(string name, bool overridden, int max, TextureImporterFormat format = TextureImporterFormat.Automatic, int quality = 50) =>
        new TextureImporterPlatformSettings
        {
            name = name, overridden = overridden, maxTextureSize = max, format = format, compressionQuality = quality,
            resizeAlgorithm = TextureResizeAlgorithm.Bilinear, androidETC2FallbackOverride = AndroidETC2FallbackOverride.Quality32BitDownscaled,
            ignorePlatformSupport = true,
        };

    static void Set()
    {
        var importer = Importer("Set");
        void Step(string key, TextureImporterPlatformSettings s, string read)
        {
            Try("set/" + key, () =>
            {
                importer.SetPlatformTextureSettings(s);
                return "argument " + (s == null ? "null" : "'" + s.name + "'") + " | read " + Describe(importer.GetPlatformTextureSettings(read))
                    + " | dirty " + EditorUtility.IsDirty(importer);
            });
        }
        Step("Android", Settings("Android", true, 512, TextureImporterFormat.ETC2_RGBA8, 80), "Android");
        Step("iPhone", Settings("iPhone", true, 256, TextureImporterFormat.ASTC_6x6), "iOS");
        Step("WebGL", Settings("WebGL", true, 128), "WebGL");
        Step("Foo", Settings("Foo", true, 64), "Foo");
        Step("tvOS", Settings("tvOS", false, 1024), "tvOS");
        Step("StandaloneOSX", Settings("StandaloneOSX", false, 1024, TextureImporterFormat.RGBA32), "Standalone");
        Step("DefaultTexturePlatform", Settings("DefaultTexturePlatform", false, 1024, TextureImporterFormat.Automatic, 30), "DefaultTexturePlatform");
        Step("empty", Settings("", true, 32), "");
        Step("nullName", Settings(null, true, 32), "Android");
        Step("null", null, "Android");
        Try("set/properties", () => "max " + importer.maxTextureSize + " quality " + importer.compressionQuality);
        Try("set/save", () => { importer.SaveAndReimport(); return "saved"; });
        Meta("set/meta", Path("Set"));
        var again = Importer("Set");
        foreach (var query in new[] { "Android", "iOS", "WebGL", "Foo", "tvOS", "Standalone" })
            Try("set/after/" + query, () => Describe(again.GetPlatformTextureSettings(query)));
    }

    static void SetOff()
    {
        // An overridden entry stored again not overridden, and a missing one not overridden.
        var importer = Importer("SetOff");
        Try("setoff/Android", () =>
        {
            importer.SetPlatformTextureSettings(Settings("Android", false, 2048));
            return Describe(importer.GetPlatformTextureSettings("Android"));
        });
        Try("setoff/iOS", () =>
        {
            importer.SetPlatformTextureSettings(Settings("iOS", false, 512));
            return Describe(importer.GetPlatformTextureSettings("iOS"));
        });
        Try("setoff/save", () => { importer.SaveAndReimport(); return "saved"; });
        Meta("setoff/meta", Path("SetOff"));
    }

    static void Legacy()
    {
        var importer = Importer("Legacy");
#pragma warning disable 618
        Try("legacy/five", () =>
        {
            importer.SetPlatformTextureSettings("Android", 256, TextureImporterFormat.ETC2_RGBA8, 70, true);
            return Describe(importer.GetPlatformTextureSettings("Android"));
        });
        Try("legacy/three", () =>
        {
            importer.SetPlatformTextureSettings("iPhone", 128, TextureImporterFormat.ASTC_6x6);
            return Describe(importer.GetPlatformTextureSettings("iOS"));
        });
#pragma warning restore 618
        Try("legacy/save", () => { importer.SaveAndReimport(); return "saved"; });
        Meta("legacy/meta", Path("Legacy"));
    }

    static void Clear()
    {
        var importer = Importer("Clear");
        foreach (var (query, read) in new[]
                 {
                     ("Android", "Android"), ("iPhone", "Android"), ("StandaloneWindows64", "Standalone"), ("WebGL", "WebGL"),
                     ("tvOS", "tvOS"), ("Foo", "Foo"), ("", "Android"), (null, "Android"),
                 })
            Try("clear/" + (query ?? "(null)"), () =>
            {
                bool dirty = EditorUtility.IsDirty(importer);
                importer.ClearPlatformTextureSettings(query);
                return (dirty ? "" : "dirty " + EditorUtility.IsDirty(importer) + " | ") + Describe(importer.GetPlatformTextureSettings(read));
            });
        Try("clear/save", () => { importer.SaveAndReimport(); return "saved"; });
        Meta("clear/meta", Path("Clear"));

        var other = Importer("ClearDefault");
        Try("clear/missing", () =>
        {
            other.ClearPlatformTextureSettings("tvOS");
            return "dirty " + EditorUtility.IsDirty(other);
        });
        Try("clear/DefaultTexturePlatform", () =>
        {
            other.ClearPlatformTextureSettings("DefaultTexturePlatform");
            return Describe(other.GetDefaultPlatformTextureSettings()) + " | max " + other.maxTextureSize + " quality " + other.compressionQuality;
        });
        Try("clear/DefaultTexturePlatform/save", () => { other.SaveAndReimport(); return "saved"; });
        Meta("clear/DefaultTexturePlatform/meta", Path("ClearDefault"));
    }

    static void Defaults()
    {
        // The default platform's fields through the importer's own properties.
        var importer = Importer("DefaultProps");
        Try("props/set", () =>
        {
            importer.allowAlphaSplitting = true;
            importer.androidETC2FallbackOverride = AndroidETC2FallbackOverride.Quality16Bit;
            importer.crunchedCompression = true;
            return Describe(importer.GetDefaultPlatformTextureSettings());
        });
        Try("props/save", () => { importer.SaveAndReimport(); return "saved"; });
        Meta("props/meta", Path("DefaultProps"));
    }

    static void Load()
    {
        // Android is missing from LoadCopy's file: what the importer reads for it, before and after
        // its default entry changes.
        var copy = Importer("LoadCopy");
        Try("load/copy/before", () => Describe(copy.GetPlatformTextureSettings("Android")));
        Try("load/copy/after", () =>
        {
            copy.maxTextureSize = 1024;
            copy.compressionQuality = 10;
            return Describe(copy.GetPlatformTextureSettings("Android")) + " | " + Describe(copy.GetDefaultPlatformTextureSettings());
        });
        Try("load/copy/save", () => { copy.SaveAndReimport(); return "saved"; });
        Meta("load/copy/meta", Path("LoadCopy"));
        // Android and Standalone missing; a setting of another kind changed.
        var order = Importer("LoadOrder");
        Try("load/order/save", () => { order.isReadable = true; order.SaveAndReimport(); return "saved"; });
        Meta("load/order/meta", Path("LoadOrder"));
        // No default entry.
        var none = Importer("LoadNoDefault");
        Try("load/nodefault", () => Describe(none.GetDefaultPlatformTextureSettings()) + " | max " + none.maxTextureSize
            + " | " + Describe(none.GetPlatformTextureSettings("Standalone")) + " | " + Describe(none.GetPlatformTextureSettings("tvOS")));
        Try("load/nodefault/save", () => { none.isReadable = true; none.SaveAndReimport(); return "saved"; });
        Meta("load/nodefault/meta", Path("LoadNoDefault"));
    }

    static string Loaded(string name)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Path(name));
        return texture == null ? "null" : texture.width + "x" + texture.height + " " + texture.format;
    }

    static void Size()
    {
        var importer = Importer("Size");
        Try("size/open", () => Loaded("Size"));
        Try("size/overridden", () =>
        {
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 32 });
            importer.SaveAndReimport();
            return Loaded("Size");
        });
        Try("size/cleared", () =>
        {
            importer.ClearPlatformTextureSettings("Standalone");
            importer.SaveAndReimport();
            return Loaded("Size");
        });
        Meta("size/meta", Path("Size"));
    }

    static string Pixels(string name)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Path(name));
        if (texture == null) return "null";
        var sb = new StringBuilder(texture.width + "x" + texture.height + " " + texture.format + " ");
        foreach (var c in texture.GetPixels32()) sb.Append(c.r.ToString("x2")).Append(c.g.ToString("x2")).Append(c.b.ToString("x2")).Append(c.a.ToString("x2"));
        return sb.ToString();
    }

    static void Gamma()
    {
        foreach (var name in new[]
                 {
                     "GammaNone", "Gamma100000", "Gamma45455", "Gamma50000", "Gamma220000", "Gamma47000", "Gamma43000", "Gamma0", "Gamma100000Srgb",
                     "Gamma100000Ignored", "Gamma100000Linear", "Gamma100000Rgb", "Gamma100000Gray", "Gamma100000Palette", "Gamma100000Deep",
                     "GammaNoneDeep", "Gamma100000Toggle",
                 })
        {
            Try("gamma/" + name + "/ignorePngGamma", () => Importer(name).ignorePngGamma.ToString());
            Try("gamma/" + name, () => Pixels(name));
        }
        Try("gamma/Gamma100000Toggle/set", () =>
        {
            var importer = Importer("Gamma100000Toggle");
            importer.ignorePngGamma = true;
            importer.SaveAndReimport();
            return Pixels("Gamma100000Toggle");
        });
        Meta("gamma/Gamma100000Toggle/meta", Path("Gamma100000Toggle"));
    }
}
