using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Records, in Play mode, what the Editor's sprite atlas extensions answer for each sprite atlas of
/// the project (in asset path order): its packables (type, asset path and name of each, in order;
/// a packable whose asset is gone answers null), its master atlas and whether builds include it,
/// beside the atlas's variant flag and sprite count. Also the answers for a null atlas.
/// </summary>
static class SpriteAtlasEditorProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("SPRITE_ATLAS_EDITOR_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key))
            .Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (Exception e) { Put(key, "throws " + e.GetType().Name); }
        }

        var paths = AssetDatabase.FindAssets("t:SpriteAtlas").Select(AssetDatabase.GUIDToAssetPath)
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Put("atlases", string.Join("|", paths));
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            string key = Path.GetFileName(path);
            Put(key + ".loaded", (atlas != null).ToString());
            if (atlas == null) continue;
            Check(key + ".isVariant", () => atlas.isVariant.ToString());
            Check(key + ".spriteCount", () => atlas.spriteCount.ToString());
            Check(key + ".packables", () => string.Join("|", atlas.GetPackables().Select(Describe)));
            Check(key + ".master", () => Describe(atlas.GetMasterAtlas()));
            Check(key + ".includeInBuild", () => atlas.IsIncludeInBuild().ToString());
        }
        SpriteAtlas none = null;
        Check("null.packables", () => string.Join("|", none.GetPackables().Select(Describe)));
        Check("null.master", () => Describe(none.GetMasterAtlas()));
        Check("null.includeInBuild", () => none.IsIncludeInBuild().ToString());

        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas == null) continue;
            string key = Path.GetFileName(path);
            Check(key + ".packingSettings", () => DescribePacking(atlas.GetPackingSettings()));
            Check(key + ".textureSettings", () => DescribeTexture(atlas.GetTextureSettings()));
        }
        var fresh = new SpriteAtlas();
        Check("new.packingSettings", () => DescribePacking(fresh.GetPackingSettings()));
        Check("new.textureSettings", () => DescribeTexture(fresh.GetTextureSettings()));
        Check("null.packingSettings", () => DescribePacking(none.GetPackingSettings()));
        Check("null.textureSettings", () => DescribeTexture(none.GetTextureSettings()));

        if (paths.Any(p => p.StartsWith("Assets/PlatformAtlases/", StringComparison.Ordinal)))
            RecordPlatformSettings(paths, fresh, none, Check);

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static void RecordPlatformSettings(string[] paths, SpriteAtlas fresh, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (Path.GetFileName(path), atlas);
        }

        string Label(string target) => target == null ? "<null>" : target.Length == 0 ? "<empty>" : target;

        void Query(string prefix, SpriteAtlas atlas, params string[] targets)
        {
            foreach (var target in targets)
                check(prefix + ".platform." + Label(target), () => DescribePlatform(atlas.GetPlatformSettings(target)));
        }

        if (byStem.TryGetValue("Defaults", out var defaults))
        {
            Query(defaults.key, defaults.atlas,
                "DefaultTexturePlatform", "Android", "iOS", "iPhone", "Standalone", "StandaloneWindows64",
                "WebGL", "", null, "UnknownPlatform");
        }

        if (byStem.TryGetValue("Custom", out var custom))
        {
            Query(custom.key, custom.atlas,
                "DefaultTexturePlatform", "defaulttextureplatform",
                "Android", "android", "ANDROID",
                "iPhone", "iOS", "ios", "iphone",
                "Standalone", "standalone", "StandaloneWindows", "StandaloneWindows64",
                "StandaloneOSX", "StandaloneLinux64", "Win", "Win64", "OSXUniversal", "Linux64",
                "WebGL", "webgl", "Web",
                "WSAPlayer", "Windows Store Apps", "WSA", "UWP",
                "tvOS", "PS4", "PS5", "XboxOne", "Switch", "VisionOS",
                "GameCoreXboxSeries", "GameCoreScarlett", "GameCoreXboxOne",
                "EmbeddedLinux", "QNX", "LinuxHeadlessSimulation", "CloudRendering",
                "Switch2", "Kepler", "Server",
                "", null, "UnknownPlatform");

            check(custom.key + ".platform.mutationIsolation", () =>
            {
                var copy = custom.atlas.GetPlatformSettings("Android");
                copy.name = "Mutated";
                copy.maxTextureSize = 32;
                copy.overridden = false;
                copy.compressionQuality = 1;
                return DescribePlatform(custom.atlas.GetPlatformSettings("Android"));
            });
        }

        if (byStem.TryGetValue("CustomVariant", out var variant))
            Query(variant.key, variant.atlas, "DefaultTexturePlatform", "Android", "iPhone", "Standalone");

        if (byStem.TryGetValue("Legacy", out var legacy))
            Query(legacy.key, legacy.atlas,
                "DefaultTexturePlatform", "Android", "Standalone", "StandaloneWindows64");

        if (byStem.TryGetValue("LegacyV3", out var legacyV3))
            Query(legacyV3.key, legacyV3.atlas,
                "iPhone", "iOS", "WSAPlayer", "Windows Store Apps", "Switch", "Standalone", "StandaloneWindows64");

        Query("new", fresh, "DefaultTexturePlatform", "Android", "iOS", "StandaloneWindows64", "", null, "UnknownPlatform");
        Query("null", none, "DefaultTexturePlatform", "Android", null);
    }

    static string DescribePlatform(TextureImporterPlatformSettings s) => s == null
        ? "<null>"
        : $"name={s.name ?? "<null>"}|overridden={s.overridden}|ignorePlatformSupport={s.ignorePlatformSupport}|maxTextureSize={s.maxTextureSize}|resizeAlgorithm={s.resizeAlgorithm}|format={(int)s.format}|textureCompression={s.textureCompression}|compressionQuality={s.compressionQuality}|crunchedCompression={s.crunchedCompression}|allowsAlphaSplitting={s.allowsAlphaSplitting}|androidETC2FallbackOverride={s.androidETC2FallbackOverride}";

    static string DescribePacking(SpriteAtlasPackingSettings s) =>
        $"blockOffset={s.blockOffset}|padding={s.padding}|enableRotation={s.enableRotation}|enableTightPacking={s.enableTightPacking}|enableAlphaDilation={s.enableAlphaDilation}";

    static string DescribeTexture(SpriteAtlasTextureSettings s) =>
        $"maxTextureSize={s.maxTextureSize}|anisoLevel={s.anisoLevel}|filterMode={s.filterMode}|generateMipMaps={s.generateMipMaps}|readable={s.readable}|sRGB={s.sRGB}";

    static string Describe(UnityEngine.Object o) =>
        o == null ? "<null>" : o.GetType().Name + ":" + AssetDatabase.GetAssetPath(o) + ":" + o.name;

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s ?? "<null>")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
