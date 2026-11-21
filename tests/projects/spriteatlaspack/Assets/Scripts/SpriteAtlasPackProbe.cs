using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what the Unity 6000.0 Editor answers for UnityEditor.U2D.SpriteAtlasUtility
/// across V1 AlwaysOnAtlas (mode 4), V1 BuildTimeOnlyAtlas (mode 3), V2 SpriteAtlasV2 (mode 5),
/// and Disabled (mode 0): construction, invalid/null/destroyed/empty/non-persistent inputs,
/// PackAtlases on master/variant/excluded/duplicate/modified atlases, PackAllAtlases, and
/// CleanupAtlasPacking.
/// </summary>
static class SpriteAtlasPackProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("SPRITE_ATLAS_PACK_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var writtenDir = Path.Combine(directory, "written");
        Directory.CreateDirectory(writtenDir);

        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key))
            .Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (Exception e) { Put(key, "throws " + e.GetType().Name + ":" + e.Message); }
        }

        string CaptureLogs(Action action, out Exception thrown)
        {
            var logs = new List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) =>
                logs.Add(type + ":" + condition.Replace("\r", "").Replace("\n", "\\n"));
            Application.logMessageReceived += handler;
            thrown = null;
            try { action(); }
            catch (Exception e) { thrown = e; }
            finally { Application.logMessageReceived -= handler; }
            return logs.Count == 0 ? "<none>" : string.Join("||", logs);
        }

        var paths = AssetDatabase.FindAssets("t:SpriteAtlas", new[] { "Assets/Atlases" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        var byStem = new Dictionary<string, (string path, SpriteAtlas atlas, string origAsset, string origMeta)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, atlas, File.ReadAllText(path), File.ReadAllText(path + ".meta"));
        }

        var singleTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Single.png");
        var singleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Single.png");
        var sheetTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Sheet.png");
        var sheetSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Sheet.png").OfType<Sprite>()
            .OrderBy(s => s.name, StringComparer.Ordinal).First();

        string AtlasSummary(SpriteAtlas atlas, string probeSpriteName, Texture2D sourceTex, Sprite probeSprite)
        {
            if (atlas == null) return "<null>";
            var clone = string.IsNullOrEmpty(probeSpriteName) ? null : atlas.GetSprite(probeSpriteName);
            string cloneInfo;
            if (clone == null)
            {
                cloneInfo = "clone=<null>";
            }
            else
            {
                bool hasTex = clone.texture != null;
                bool isAtlasPage = hasTex && !ReferenceEquals(clone.texture, sourceTex);
                cloneInfo = $"clone=(packed={clone.packed}|hasTex={hasTex}|page={isAtlasPage}|scale={clone.spriteAtlasTextureScale})";
                Object.DestroyImmediate(clone);
            }
            bool canBind = probeSprite != null && atlas.CanBindTo(probeSprite);
            return $"count={atlas.spriteCount}|canBind={canBind}|{cloneInfo}|dirty={EditorUtility.IsDirty(atlas)}";
        }

        string DiskState(string stem)
        {
            var e = byStem[stem];
            bool assetSame = File.ReadAllText(e.path) == e.origAsset;
            bool metaSame = File.ReadAllText(e.path + ".meta") == e.origMeta;
            return $"assetSame={assetSame}|metaSame={metaSame}";
        }

        var main = byStem["Main"].atlas;
        var second = byStem["Second"].atlas;
        var variant = byStem["Variant"].atlas;
        var excluded = byStem["Excluded"].atlas;

        Put("atlases", string.Join("|", paths));

        // 1. Construction & initial state before any explicit pack call.
        Check("ctor.default", () =>
        {
            var util = new SpriteAtlasUtility();
            return $"created={util != null}|type={util.GetType().FullName}";
        });

        Check("initial.state", () =>
            $"main=({AtlasSummary(main, "Single", singleTex, singleSprite)})|second=({AtlasSummary(second, sheetSprite.name, sheetTex, sheetSprite)})|variant=({AtlasSummary(variant, "Single", singleTex, singleSprite)})|excluded=({AtlasSummary(excluded, "FolderLeaf", null, null)})|singlePacked={singleSprite.packed}|sheetPacked={sheetSprite.packed}");

        // 2. Invalid & edge inputs to PackAtlases.
        Check("pack.nullArray", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(null, BuildTarget.StandaloneLinux64), out var ex);
            string param = ex is ArgumentException ae ? ae.ParamName : "<none>";
            string msg = ex != null ? ex.Message.Split('\n')[0].Trim() : "<none>";
            return $"ex={ex?.GetType().Name ?? "none"}|param={param}|msg={msg}|logs={logs}";
        });

        Check("pack.nullElement", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { null }, BuildTarget.StandaloneLinux64), out var ex);
            string param = ex is ArgumentException ae ? ae.ParamName : "<none>";
            string msg = ex != null ? ex.Message.Split('\n')[0].Trim() : "<none>";
            return $"ex={ex?.GetType().Name ?? "none"}|param={param}|msg={msg}|logs={logs}";
        });

        Check("pack.mixedNullElement", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { main, null }, BuildTarget.StandaloneLinux64), out var ex);
            string param = ex is ArgumentException ae ? ae.ParamName : "<none>";
            string msg = ex != null ? ex.Message.Split('\n')[0].Trim() : "<none>";
            return $"ex={ex?.GetType().Name ?? "none"}|param={param}|msg={msg}|logs={logs}";
        });

        Check("pack.destroyedElement", () =>
        {
            var dead = new SpriteAtlas();
            Object.DestroyImmediate(dead);
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { dead }, BuildTarget.StandaloneLinux64), out var ex);
            string param = ex is ArgumentException ae ? ae.ParamName : "<none>";
            string msg = ex != null ? ex.Message.Split('\n')[0].Trim() : "<none>";
            return $"ex={ex?.GetType().Name ?? "none"}|param={param}|msg={msg}|logs={logs}";
        });

        Check("pack.emptyArray", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(Array.Empty<SpriteAtlas>(), BuildTarget.StandaloneLinux64), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}";
        });

        Check("pack.nonPersistent", () =>
        {
            var temp = new SpriteAtlas { name = "InMemoryAtlas" };
            temp.Add(new Object[] { singleTex });
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new[] { temp }, BuildTarget.StandaloneLinux64), out var ex);
            int count = temp.spriteCount;
            Object.DestroyImmediate(temp);
            return $"ex={ex?.GetType().Name ?? "none"}|count={count}|logs={logs}";
        });

        // 3. Single persistent atlas: PackAtlases(new[] { main }, StandaloneLinux64)
        Check("pack.mainOnly", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new[] { main }, BuildTarget.StandaloneLinux64), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|main=({AtlasSummary(main, "Single", singleTex, singleSprite)})|second=({AtlasSummary(second, sheetSprite.name, sheetTex, sheetSprite)})|mainDisk=({DiskState("Main")})|secondDisk=({DiskState("Second")})";
        });

        // 4. Variant alone & master + variant & duplicates & excluded
        Check("pack.variantOnly", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new[] { variant }, BuildTarget.StandaloneLinux64), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|variant=({AtlasSummary(variant, "Single", singleTex, singleSprite)})|variantDisk=({DiskState("Variant")})";
        });

        Check("pack.masterAndVariantAndDup", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new[] { main, variant, main }, BuildTarget.StandaloneLinux64, false), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|main=({AtlasSummary(main, "Single", singleTex, singleSprite)})|variant=({AtlasSummary(variant, "Single", singleTex, singleSprite)})|mainDisk=({DiskState("Main")})|variantDisk=({DiskState("Variant")})";
        });

        Check("pack.excluded", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new[] { excluded }, BuildTarget.StandaloneLinux64), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|excluded=({AtlasSummary(excluded, "FolderLeaf", null, null)})|excludedDisk=({DiskState("Excluded")})";
        });

        // 5. Alternate BuildTarget
        Check("pack.androidTarget", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new[] { main }, BuildTarget.Android), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|main=({AtlasSummary(main, "Single", singleTex, singleSprite)})";
        });

        // 6. PackAllAtlases
        Check("packAll.linux", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAllAtlases(BuildTarget.StandaloneLinux64), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|main=({AtlasSummary(main, "Single", singleTex, singleSprite)})|second=({AtlasSummary(second, sheetSprite.name, sheetTex, sheetSprite)})|variant=({AtlasSummary(variant, "Single", singleTex, singleSprite)})|excluded=({AtlasSummary(excluded, "FolderLeaf", null, null)})";
        });

        // 6b. A sprite loaded again after the atlases were packed (FolderBig, packed by Excluded,
        // its texture imported again).
        Check("afterPack.lateLoadedSprite", () =>
        {
            AssetDatabase.ImportAsset("Assets/Sprites/Folder/FolderBig.png", ImportAssetOptions.ForceUpdate);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Folder/FolderBig.png");
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Folder/FolderBig.png");
            return sprite == null ? "<null>"
                : $"packed={sprite.packed}|hasTex={sprite.texture != null}|source={ReferenceEquals(sprite.texture, source)}";
        });

        // 7. Dirty in-memory Add on Second, then PackAtlases (without SaveAssets)
        Check("pack.afterInMemoryAdd", () =>
        {
            second.Add(new Object[] { singleTex });
            bool dirtyBefore = EditorUtility.IsDirty(second);
            var logs = CaptureLogs(() => SpriteAtlasUtility.PackAtlases(new[] { second }, BuildTarget.StandaloneLinux64), out var ex);
            bool dirtyAfter = EditorUtility.IsDirty(second);
            var afterSecond = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(byStem["Second"].path);
            if (File.ReadAllText(byStem["Second"].path) != byStem["Second"].origAsset)
                File.Copy(byStem["Second"].path, Path.Combine(writtenDir, Path.GetFileName(byStem["Second"].path)), true);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|dirtyBefore={dirtyBefore}|dirtyAfter={dirtyAfter}|hasSingle={afterSecond.GetSprite("Single") != null}|count={afterSecond.spriteCount}|secondDisk=({DiskState("Second")})";
        });

        // The whole .spriteatlas the pack wrote for Second.
        Check("pack.afterInMemoryAdd.written", () => File.ReadAllText(byStem["Second"].path));

        // 8. CleanupAtlasPacking
        Check("cleanup", () =>
        {
            var logs = CaptureLogs(() => SpriteAtlasUtility.CleanupAtlasPacking(), out var ex);
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|main=({AtlasSummary(main, "Single", singleTex, singleSprite)})";
        });

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

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
