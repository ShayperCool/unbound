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

        if (paths.Any(p => p.StartsWith("Assets/IncludeAtlases/", StringComparison.Ordinal)))
            RecordIncludeInBuild(paths, none, Check);

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static void RecordIncludeInBuild(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        string State(SpriteAtlas a, int baseDirty) =>
            $"include={a.IsIncludeInBuild()}|dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string BindLine(string file)
        {
            foreach (var line in File.ReadAllLines(file))
                if (line.TrimStart().StartsWith("bindAsDefault:", StringComparison.Ordinal))
                    return line;
            return "<absent>";
        }

        string FileState(string path, byte[] origAsset, byte[] origMeta)
        {
            var curAsset = File.ReadAllBytes(path);
            var curMeta = File.ReadAllBytes(path + ".meta");
            bool assetChanged = !curAsset.SequenceEqual(origAsset);
            bool metaChanged = !curMeta.SequenceEqual(origMeta);
            return $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetBind={BindLine(path)}|metaBind={BindLine(path + ".meta")}";
        }

        check("null.setInclude.true", () => { none.SetIncludeInBuild(true); return "ok"; });
        check("null.setInclude.false", () => { none.SetIncludeInBuild(false); return "ok"; });

        var mem = new SpriteAtlas();
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.include.initial", () => State(mem, memBase));
        mem.SetIncludeInBuild(true);
        check("new.include.sameTrue", () => State(mem, memBase));
        mem.SetIncludeInBuild(false);
        check("new.include.setFalse", () => State(mem, memBase));
        mem.SetIncludeInBuild(false);
        check("new.include.sameFalse", () => State(mem, memBase));
        mem.SetIncludeInBuild(true);
        check("new.include.setTrue", () => State(mem, memBase));
        AssetDatabase.SaveAssets();
        check("new.include.afterSave", () => State(mem, memBase));

        if (byStem.TryGetValue("Included", out var inc))
        {
            var origA = File.ReadAllBytes(inc.path);
            var origM = File.ReadAllBytes(inc.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(inc.atlas);
            check(inc.key + ".setInclude.initial", () => State(inc.atlas, baseDirty) + "|" + FileState(inc.path, origA, origM));
            inc.atlas.SetIncludeInBuild(true);
            check(inc.key + ".setInclude.sameTrue", () => State(inc.atlas, baseDirty) + "|" + FileState(inc.path, origA, origM));
            inc.atlas.SetIncludeInBuild(false);
            check(inc.key + ".setInclude.setFalse", () => State(inc.atlas, baseDirty) + "|" + FileState(inc.path, origA, origM));
            inc.atlas.SetIncludeInBuild(false);
            check(inc.key + ".setInclude.sameFalse", () => State(inc.atlas, baseDirty) + "|" + FileState(inc.path, origA, origM));
            AssetDatabase.SaveAssets();
            check(inc.key + ".setInclude.afterSave", () => State(inc.atlas, baseDirty) + "|" + FileState(inc.path, origA, origM));
            AssetDatabase.ImportAsset(inc.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(inc.path);
            check(inc.key + ".setInclude.afterReimport", () =>
                $"sameRef={ReferenceEquals(inc.atlas, reloaded)}|include={reloaded.IsIncludeInBuild()}|dirty={EditorUtility.IsDirty(reloaded)}|" +
                FileState(inc.path, origA, origM));
        }

        if (byStem.TryGetValue("Excluded", out var exc))
        {
            var origA = File.ReadAllBytes(exc.path);
            var origM = File.ReadAllBytes(exc.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(exc.atlas);
            check(exc.key + ".setInclude.initial", () => State(exc.atlas, baseDirty) + "|" + FileState(exc.path, origA, origM));
            exc.atlas.SetIncludeInBuild(false);
            check(exc.key + ".setInclude.sameFalse", () => State(exc.atlas, baseDirty) + "|" + FileState(exc.path, origA, origM));
            exc.atlas.SetIncludeInBuild(true);
            check(exc.key + ".setInclude.setTrue", () => State(exc.atlas, baseDirty) + "|" + FileState(exc.path, origA, origM));
            AssetDatabase.SaveAssets();
            check(exc.key + ".setInclude.afterSaveTrue", () => State(exc.atlas, baseDirty) + "|" + FileState(exc.path, origA, origM));
            exc.atlas.SetIncludeInBuild(false);
            check(exc.key + ".setInclude.setBackFalse", () => State(exc.atlas, baseDirty) + "|" + FileState(exc.path, origA, origM));
            AssetDatabase.SaveAssetIfDirty(exc.atlas);
            check(exc.key + ".setInclude.afterSaveIfDirty", () => State(exc.atlas, baseDirty) + "|" + FileState(exc.path, origA, origM));
        }

        if (byStem.TryGetValue("Omitted", out var omi))
        {
            var origA = File.ReadAllBytes(omi.path);
            var origM = File.ReadAllBytes(omi.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omi.atlas);
            string PreSaveFileState() =>
                $"assetChanged={!File.ReadAllBytes(omi.path).SequenceEqual(origA)}|metaChanged={!File.ReadAllBytes(omi.path + ".meta").SequenceEqual(origM)}|metaBind={BindLine(omi.path + ".meta")}";
            check(omi.key + ".setInclude.initial", () => State(omi.atlas, baseDirty) + "|" + PreSaveFileState());
            omi.atlas.SetIncludeInBuild(false);
            check(omi.key + ".setInclude.unsavedFalse", () => State(omi.atlas, baseDirty) + "|" + PreSaveFileState());
            AssetDatabase.ImportAsset(omi.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(omi.path);
            check(omi.key + ".setInclude.afterUnsavedReimport", () =>
                $"sameRef={ReferenceEquals(omi.atlas, reloaded)}|include={reloaded.IsIncludeInBuild()}|dirty={EditorUtility.IsDirty(reloaded)}|" +
                PreSaveFileState());
            reloaded.SetIncludeInBuild(false);
            AssetDatabase.SaveAssets();
            check(omi.key + ".setInclude.afterSaveFalse", () =>
                $"include={reloaded.IsIncludeInBuild()}|dirty={EditorUtility.IsDirty(reloaded)}|" +
                FileState(omi.path, origA, origM));
        }
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
