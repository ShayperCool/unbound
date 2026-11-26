using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $GUIDONLYMETAS_OUT (tests/value_probe.py rows): textures, sounds and a
/// model whose .meta holds their GUID alone, as the project opened with them (the files written,
/// the imported objects, their importers' settings) and after a changed setting is saved. GUID
/// lines read "guid: (guid)"; the per-platform entries of the installation's platform modules are
/// left out of a text (`.platforms` says whether they were the installed ones).
/// </summary>
public class GuidOnlyMetasProbe : MonoBehaviour
{
    const string Variable = "GUIDONLYMETAS_OUT";
    const string Folder = "Assets/Guid";
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static readonly Regex s_Guid = new Regex("(?m)^guid: [0-9a-f]{32}$");

    // The same list and entry pattern as tests/projects/importermeta; keep them in step.
    static IEnumerable<string> Installed()
    {
        var platforms = new (BuildTargetGroup group, BuildTarget target, string name)[]
        {
            (BuildTargetGroup.Android, BuildTarget.Android, "Android"), (BuildTargetGroup.iOS, BuildTarget.iOS, "iOS"),
            (BuildTargetGroup.WebGL, BuildTarget.WebGL, "WebGL"), (BuildTargetGroup.WSA, BuildTarget.WSAPlayer, "Windows Store Apps"),
            (BuildTargetGroup.tvOS, BuildTarget.tvOS, "tvOS"), (BuildTargetGroup.VisionOS, BuildTarget.VisionOS, "VisionOS"),
        };
        return platforms.Where(p => BuildPipeline.IsBuildTargetSupported(p.group, p.target)).Select(p => p.name);
    }

    static Regex s_InstalledEntry;

    static void Meta(string key, string path)
    {
        var meta = path + ".meta";
        if (!File.Exists(meta)) { Row(key, "<none>"); return; }
        var text = File.ReadAllText(meta);
        var removed = new List<string>();
        text = s_InstalledEntry.Replace(text, m => { removed.Add(m.Groups[2].Value); return ""; });
        Row(key, s_Guid.Replace(text, "guid: (guid)"));
        if (text.Contains("platformSettings:"))
            Row(key + ".platforms", string.Join(",", removed) == string.Join(",", Installed()) ? "the installed modules" : "differ: " + string.Join(",", removed));
    }

    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("GuidOnlyMetasProbe").AddComponent<GuidOnlyMetasProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        var names = string.Join("|", Installed().Select(Regex.Escape));
        s_InstalledEntry = new Regex("(?m)^( *)- serializedVersion: [0-9]+\n\\1  buildTarget: (" + (names.Length > 0 ? names : "(?!)") + ")\n(?:\\1  .*\n)*");
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static readonly string[] Textures = { "Square", "Wide", "Strip", "TexSaved" };
    static readonly string[] Sounds = { "Beep", "BeepSaved" };

    static string Texture(string path)
    {
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        if (main is not Texture t) return main == null ? "null" : main.GetType().Name;
        var s = t.GetType().Name + " " + t.dimension + " " + t.width + "x" + t.height + " mips " + t.mipmapCount + " filter " + t.filterMode
            + " wrap " + t.wrapModeU + "," + t.wrapModeV + "," + t.wrapModeW + " aniso " + t.anisoLevel + " readable " + t.isReadable + " " + t.graphicsFormat;
        if (t is Texture2D t2) s += " format " + t2.format;
        if (t is Cubemap cube) s += " format " + cube.format;
        return s;
    }

    static string TextureImporterValues(TextureImporter i)
    {
        var d = i.GetDefaultPlatformTextureSettings();
        var settings = new TextureImporterSettings();
        i.ReadTextureSettings(settings);
        return "type " + i.textureType + " shape " + i.textureShape + " compression " + i.textureCompression + " max " + i.maxTextureSize
            + " mips " + i.mipmapEnabled + " sRGB " + i.sRGBTexture + " readable " + i.isReadable + " npot " + i.npotScale + " alpha " + i.alphaSource
            + " transparency " + i.alphaIsTransparency + " wrap " + i.wrapMode + " filter " + i.filterMode + " aniso " + i.anisoLevel
            + " generateCubemap " + i.generateCubemap + " convolution " + settings.cubemapConvolution + " seamless " + settings.seamlessCubemap
            + " ignorePngGamma " + i.ignorePngGamma + " | default " + d.maxTextureSize + " " + d.textureCompression + " " + (int)d.format + " " + d.compressionQuality;
    }

    static string Clip(string path)
    {
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        if (main is not AudioClip c) return main == null ? "null" : main.GetType().Name;
        return "AudioClip load " + c.loadType + " preload " + c.preloadAudioData + " background " + c.loadInBackground + " ambisonic " + c.ambisonic
            + " channels " + c.channels + " frequency " + c.frequency + " samples " + c.samples;
    }

    static string AudioImporterValues(AudioImporter i)
    {
        var s = i.defaultSampleSettings;
        return "mono " + i.forceToMono + " background " + i.loadInBackground + " ambisonic " + i.ambisonic
            + " | default load " + s.loadType + " format " + s.compressionFormat + " quality " + s.quality + " rate " + s.sampleRateSetting
            + " override " + s.sampleRateOverride + " preload " + s.preloadAudioData;
    }

    static string Files() => string.Join("\n", Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories)
        .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal));

    static void Run()
    {
        // What opening the project left.
        Row("open/files", Files());
        foreach (var path in Directory.GetFiles(Folder).Where(p => !p.EndsWith(".meta")).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal))
            Meta("open/" + path, path);
        foreach (var name in Textures)
        {
            var path = Folder + "/" + name + ".png";
            Try("texture/" + name, () => Texture(path));
            Try("texture/" + name + "/importer", () => AssetImporter.GetAtPath(path) is TextureImporter i ? TextureImporterValues(i) : AssetImporter.GetAtPath(path)?.GetType().Name ?? "null");
        }
        foreach (var name in Sounds)
        {
            var path = Folder + "/" + name + ".wav";
            Try("sound/" + name, () => Clip(path));
            Try("sound/" + name + "/importer", () => AssetImporter.GetAtPath(path) is AudioImporter i ? AudioImporterValues(i) : AssetImporter.GetAtPath(path)?.GetType().Name ?? "null");
        }
        const string model = Folder + "/Tri.obj";
        Try("model/Tri", () => string.Join("\n", AssetDatabase.LoadAllAssetsAtPath(model).Select(o => o.GetType().Name + " " + o.name).OrderBy(s => s, StringComparer.Ordinal)));
        Try("model/Tri/renderer", () =>
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(model);
            var r = go == null ? null : go.GetComponentInChildren<MeshRenderer>();
            return r == null ? "none" : string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name + " " + AssetDatabase.GetAssetPath(m) + " " + m.shader.name));
        });
        Try("model/Tri/importer", () => AssetImporter.GetAtPath(model) is ModelImporter i
            ? "materials " + i.materialImportMode + " location " + i.materialLocation + " naming " + i.materialName + " search " + i.materialSearch
              + " scale " + i.globalScale + " fileScale " + i.useFileScale + " animation " + i.importAnimation + " type " + i.animationType + " readable " + i.isReadable
              + " normals " + i.importNormals + " tangents " + i.importTangents + " colliders " + i.addCollider
            : AssetImporter.GetAtPath(model)?.GetType().Name ?? "null");

        // A changed setting saved.
        Try("save/TexSaved", () =>
        {
            var i = (TextureImporter)AssetImporter.GetAtPath(Folder + "/TexSaved.png");
            i.isReadable = true;
            i.SaveAndReimport();
            return Texture(Folder + "/TexSaved.png");
        });
        Meta("save/TexSaved/meta", Folder + "/TexSaved.png");
        Try("save/BeepSaved", () =>
        {
            var i = (AudioImporter)AssetImporter.GetAtPath(Folder + "/BeepSaved.wav");
            i.forceToMono = true;
            i.SaveAndReimport();
            return Clip(Folder + "/BeepSaved.wav");
        });
        Meta("save/BeepSaved/meta", Folder + "/BeepSaved.wav");
        Row("end/files", Files());
    }
}
