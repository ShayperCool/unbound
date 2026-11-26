using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $SPRITEMETA_OUT (tests/value_probe.py rows): in a project whose default
/// behaviour mode is 2D, the .meta files the Editor leaves after the first import of new textures
/// (no .meta) and of textures whose .meta makes them sprites without a sprite ID (or without NPOT
/// scaling off, or with only the type and sprite mode), beside a Default texture as the control;
/// then each texture's importer settings and sprites in Play mode. GUID lines read "guid: (guid)";
/// the per-platform entries of the installation's platform modules are left out of a text
/// (`.platforms` says whether they were the installed ones).
/// </summary>
public class SpriteMetaProbe : MonoBehaviour
{
    const string Variable = "SPRITEMETA_OUT";
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static readonly Regex s_Guid = new Regex("(?m)^guid: [0-9a-f]{32}$");

    // The same list and entry pattern as tests/projects/projectwrites (ProjectWritesProbe.cs), the
    // entries limited to the installed modules' names; keep them in step.
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

    /// <summary>A .meta's text with its GUID and the installed modules' platform entries left out.</summary>
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
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("SpriteMetaProbe").AddComponent<SpriteMetaProbe>();
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

    static readonly string[] Textures = { "Control", "EmptyId", "IdNpot", "EmptyIdPot0", "Bare", "New", "NewNpot", "NewGaps",
        "BareWrap", "BarePpu", "BareDefault", "BareFull",
        "SliceL", "SliceDiag", "SliceDot", "SliceEdge", "SliceFaint", "SliceFive", "SliceNear", "SliceHole", "SliceNpot",
        "SliceThin", "SliceFlat", "SliceAlone3", "SliceAlone4", "SliceTwoBigDot", "SliceBigNear", "SliceBigGap2", "SliceSmallBetween", "SliceAreaSmall",
        "SliceRowMerge", "SliceTopDot", "SliceEmpty", "SliceChain", "SliceMany",
        "OrderRows", "OrderRowsDot", "OrderStair", "OrderStairDot", "NearChebyshev", "NearCentre", "NearTieRow" };

    /// <summary>Textures written in Play mode into Assets/Session and imported by ImportAsset (their
    /// sources' bytes), the last by Refresh.</summary>
    static readonly (string Name, string Source)[] Session = { ("SessNew", "New"), ("SessNpot", "NewNpot"), ("SessGaps", "NewGaps"), ("SessRefresh", "SliceFive") };

    static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static void Run()
    {
        foreach (var name in Textures) Meta("open/" + name, "Assets/Textures/" + name + ".png");
        foreach (var name in Textures) Describe(name, "Assets/Textures/" + name + ".png");
        // New textures an API imports during the session.
        Try("session/folder", () => AssetDatabase.CreateFolder("Assets", "Session").Length > 0 ? "created" : "none");
        foreach (var (name, source) in Session)
        {
            File.Copy("Assets/Textures/" + source + ".png", "Assets/Session/" + name + ".png");
            if (name != "SessRefresh") Try("session/import " + name, () => { AssetDatabase.ImportAsset("Assets/Session/" + name + ".png"); return "done"; });
        }
        Try("session/refresh", () => { AssetDatabase.Refresh(); return "done"; });
        foreach (var (name, _) in Session)
        {
            Meta("session/" + name, "Assets/Session/" + name + ".png");
            Describe(name, "Assets/Session/" + name + ".png");
        }
        // What the project's files are once every import is done (a later write would show here).
        foreach (var name in Textures) Meta("end/" + name, "Assets/Textures/" + name + ".png");
    }

    static void Describe(string name, string path)
    {
        {
            Try("importer/" + name, () =>
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                return "type " + importer.textureType + " mode " + importer.spriteImportMode + " npot " + importer.npotScale
                    + " wrap " + importer.wrapModeU + "," + importer.wrapModeV + "," + importer.wrapModeW
                    + " mipmaps " + importer.mipmapEnabled + " alphaIsTransparency " + importer.alphaIsTransparency
                    + " ppu " + F(importer.spritePixelsPerUnit);
            });
            Try("sprites/" + name, () =>
            {
                var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                return "main " + (main == null ? "null" : main.GetType().Name) + " count " + sprites.Length + string.Concat(sprites.Select(s =>
                    " | " + s.name + " rect " + F(s.rect.x) + "," + F(s.rect.y) + "," + F(s.rect.width) + "," + F(s.rect.height)
                    + " pivot " + F(s.pivot.x) + "," + F(s.pivot.y) + " ppu " + F(s.pixelsPerUnit)
                    + " id " + UnityEditor.U2D.SpriteEditorExtension.GetSpriteID(s)));
            });
        }
    }
}
