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

        if (paths.Any(p => p.StartsWith("Assets/PackingAtlases/", StringComparison.Ordinal)))
            RecordPackingSettings(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/TextureAtlases/", StringComparison.Ordinal)))
            RecordTextureSettings(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/PlatformSetAtlases/", StringComparison.Ordinal)))
            RecordSetPlatformSettings(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/AddAtlases/", StringComparison.Ordinal)))
            RecordAddPackables(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/RemoveAtlases/", StringComparison.Ordinal)))
            RecordRemovePackables(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/VariantScaleAtlases/", StringComparison.Ordinal)))
            RecordVariantScale(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/IsVariantAtlases/", StringComparison.Ordinal)))
            RecordIsVariant(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/MasterAtlasAtlases/", StringComparison.Ordinal)))
            RecordMasterAtlas(paths, none, Check);

        if (paths.Any(p => p.StartsWith("Assets/ImporterPlatformAtlases/", StringComparison.Ordinal)))
            RecordImporterPlatformSettings(paths, Check);

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string PackablesBlock(string file)
    {
        var lines = File.ReadAllLines(file);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed == "packables: []") return "[]";
            if (trimmed != "packables:") continue;
            int indent = lines[i].Length - trimmed.Length;
            var items = new System.Collections.Generic.List<string>();
            for (int j = i + 1; j < lines.Length; j++)
            {
                string t = lines[j].TrimStart();
                int childIndent = lines[j].Length - t.Length;
                if (childIndent < indent || (childIndent == indent && !t.StartsWith("- ", StringComparison.Ordinal))) break;
                items.Add(lines[j].Trim());
            }
            return string.Join(";", items);
        }
        return "<absent>";
    }

    static void RecordAddPackables(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Sprites/Folder");
        var singleTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Single.png");
        var singleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Single.png");
        var sheetSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Sheet.png").OfType<Sprite>()
            .OrderBy(s => s.name, StringComparer.Ordinal).First();
        var runtimeTex = new Texture2D(4, 4) { name = "RuntimeTex" };
        var runtimeSprite = Sprite.Create(runtimeTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        runtimeSprite.name = "RuntimeSprite";
        var invalidGo = new GameObject("InvalidGo");
        var invalidRt = new RenderTexture(4, 4, 0) { name = "InvalidRt" };

        string Packables(SpriteAtlas a) => string.Join(",", a.GetPackables().Select(Describe));

        string Dirty(SpriteAtlas a, int baseDirty) =>
            $"dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string Unrelated(SpriteAtlas a) =>
            $"texture=({DescribeTexture(a.GetTextureSettings())})|packing=({DescribePacking(a.GetPackingSettings())})|android=({DescribePlatform(a.GetPlatformSettings("Android"))})|include={a.IsIncludeInBuild()}";

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetBlock = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetBlock
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetPackables={PackablesBlock(path)}|metaPackables={PackablesBlock(path + ".meta")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaPackables={PackablesBlock(path + ".meta")}";
        }

        string CaptureLogs(Action action)
        {
            var logs = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition);
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return string.Join(";", logs);
        }

        check("null.add.valid", () => { none.Add(new UnityEngine.Object[] { singleTex }); return "ok"; });
        check("null.add.nullArray", () => { none.Add(null); return "ok"; });

        var mem = new SpriteAtlas();
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.add.nullArray", () =>
        {
            var logs = CaptureLogs(() => mem.Add(null));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.add.emptyArray", () =>
        {
            var logs = CaptureLogs(() => mem.Add(Array.Empty<UnityEngine.Object>()));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.add.supportedTypes", () =>
        {
            var logs = CaptureLogs(() => mem.Add(new UnityEngine.Object[] { folder, singleTex, sheetSprite }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.add.duplicates", () =>
        {
            var logs = CaptureLogs(() => mem.Add(new UnityEngine.Object[] { singleTex, singleTex, folder }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.add.mixedValidAndInvalid", () =>
        {
            var logs = CaptureLogs(() => mem.Add(new UnityEngine.Object[] { null, singleSprite, invalidGo, invalidRt, mem, folder }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.add.runtimeObjects", () =>
        {
            var logs = CaptureLogs(() => mem.Add(new UnityEngine.Object[] { runtimeTex, runtimeSprite }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        AssetDatabase.SaveAssets();
        check("new.add.afterSave", () => $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}");

        if (byStem.TryGetValue("Master", out var master))
        {
            var origA = File.ReadAllBytes(master.path);
            var origM = File.ReadAllBytes(master.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(master.atlas);
            check(master.key + ".add.initial", () =>
                $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            check(master.key + ".add.nullAndEmpty", () =>
            {
                var logs = CaptureLogs(() =>
                {
                    master.atlas.Add(null);
                    master.atlas.Add(Array.Empty<UnityEngine.Object>());
                });
                return $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{FileState(master.path, origA, origM)}";
            });

            check(master.key + ".add.supportedAndDup", () =>
            {
                var logs = CaptureLogs(() => master.atlas.Add(new UnityEngine.Object[] { folder, singleTex, singleSprite, sheetSprite }));
                return $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{FileState(master.path, origA, origM)}";
            });

            AssetDatabase.SaveAssets();
            check(master.key + ".add.afterSave", () =>
                $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            check(master.key + ".add.mixedInvalidAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => master.atlas.Add(new UnityEngine.Object[] { null, invalidGo, invalidRt, folder }));
                AssetDatabase.SaveAssetIfDirty(master.atlas);
                return $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}";
            });

            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path);
            check(master.key + ".add.afterReimport", () =>
                $"sameRef={ReferenceEquals(master.atlas, reloaded)}|packables=[{Packables(reloaded)}]|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(master.path, origA, origM)}");
            master = (master.path, master.key, reloaded);
        }

        if (byStem.TryGetValue("Empty", out var empty))
        {
            var origA = File.ReadAllBytes(empty.path);
            var origM = File.ReadAllBytes(empty.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(empty.atlas);
            check(empty.key + ".add.initial", () =>
                $"packables=[{Packables(empty.atlas)}]|{Dirty(empty.atlas, baseDirty)}|{FileState(empty.path, origA, origM)}");

            check(empty.key + ".add.onlyInvalid", () =>
            {
                var logs = CaptureLogs(() => empty.atlas.Add(new UnityEngine.Object[] { null, invalidGo }));
                return $"packables=[{Packables(empty.atlas)}]|{Dirty(empty.atlas, baseDirty)}|logs=[{logs}]|{FileState(empty.path, origA, origM)}";
            });

            check(empty.key + ".add.persistentAndRuntimeSave", () =>
            {
                var logs = CaptureLogs(() => empty.atlas.Add(new UnityEngine.Object[] { singleTex, runtimeTex, singleSprite }));
                AssetDatabase.SaveAssetIfDirty(empty.atlas);
                return $"packables=[{Packables(empty.atlas)}]|{Dirty(empty.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(empty.atlas)}|{FileState(empty.path, origA, origM)}";
            });
        }

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterBaseDirty = byStem.ContainsKey("Master") ? EditorUtility.GetDirtyCount(master.atlas) : 0;
            check(variant.key + ".add.initial", () =>
                $"packables=[{Packables(variant.atlas)}]|{Dirty(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            check(variant.key + ".add.modified", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.Add(new UnityEngine.Object[] { singleTex, folder }));
                return $"packables=[{Packables(variant.atlas)}]|{Dirty(variant.atlas, baseDirty)}|masterPackables=[{Packables(master.atlas)}]|masterDirtyDelta={EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            AssetDatabase.SaveAssetIfDirty(variant.atlas);
            check(variant.key + ".add.afterSaveIfDirty", () =>
                $"packables=[{Packables(variant.atlas)}]|{Dirty(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
            check(variant.key + ".add.afterReimport", () =>
                $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|packables=[{Packables(reloaded)}]|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(variant.path, origA, origM)}");
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            var origA = File.ReadAllBytes(omitted.path);
            var origM = File.ReadAllBytes(omitted.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omitted.atlas);
            check(omitted.key + ".add.initial", () =>
                $"packables=[{Packables(omitted.atlas)}]|{Dirty(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetBlock: false)}");

            check(omitted.key + ".add.modified", () =>
            {
                var logs = CaptureLogs(() => omitted.atlas.Add(new UnityEngine.Object[] { singleSprite, folder }));
                return $"packables=[{Packables(omitted.atlas)}]|{Dirty(omitted.atlas, baseDirty)}|logs=[{logs}]|{FileState(omitted.path, origA, origM, includeAssetBlock: false)}";
            });

            AssetDatabase.SaveAssetIfDirty(omitted.atlas);
            check(omitted.key + ".add.afterSaveIfDirty", () =>
                $"packables=[{Packables(omitted.atlas)}]|{Dirty(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetBlock: true)}|assetTexture={YamlBlock(omitted.path, "textureSettings:")}|assetPacking={YamlBlock(omitted.path, "packingSettings:")}");
        }
    }

    static void RecordRemovePackables(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Sprites/Folder");
        var singleTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Single.png");
        var singleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Single.png");
        var sheetSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Sheet.png").OfType<Sprite>()
            .OrderBy(s => s.name, StringComparer.Ordinal).First();
        var runtimeTex = new Texture2D(4, 4) { name = "RuntimeTex" };
        var runtimeSprite = Sprite.Create(runtimeTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        runtimeSprite.name = "RuntimeSprite";
        var doomedTex = new Texture2D(2, 2) { name = "DoomedTex" };
        var invalidGo = new GameObject("InvalidGo");
        var invalidRt = new RenderTexture(4, 4, 0) { name = "InvalidRt" };

        string Packables(SpriteAtlas a) => string.Join(",", a.GetPackables().Select(Describe));

        string Dirty(SpriteAtlas a, int baseDirty) =>
            $"dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string Unrelated(SpriteAtlas a) =>
            $"texture=({DescribeTexture(a.GetTextureSettings())})|packing=({DescribePacking(a.GetPackingSettings())})|android=({DescribePlatform(a.GetPlatformSettings("Android"))})|include={a.IsIncludeInBuild()}";

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetBlock = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetBlock
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetPackables={PackablesBlock(path)}|metaPackables={PackablesBlock(path + ".meta")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaPackables={PackablesBlock(path + ".meta")}";
        }

        string CaptureLogs(Action action)
        {
            var logs = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition);
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return string.Join(";", logs);
        }

        check("null.remove.valid", () => { none.Remove(new UnityEngine.Object[] { singleTex }); return "ok"; });
        check("null.remove.nullArray", () => { none.Remove(null); return "ok"; });

        var mem = new SpriteAtlas();
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.remove.nullArray", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(null));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.remove.emptyArray", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(Array.Empty<UnityEngine.Object>()));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        mem.Add(new UnityEngine.Object[] { folder, singleTex, singleSprite, singleTex, sheetSprite, runtimeTex, runtimeSprite, doomedTex });
        UnityEngine.Object.DestroyImmediate(doomedTex);
        check("new.remove.duplicatesAndOrder", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(new UnityEngine.Object[] { singleTex }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.remove.missingAndInvalid", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(new UnityEngine.Object[] { singleTex, invalidGo, invalidRt, mem }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.remove.nullDoesNotRemoveDestroyed", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(new UnityEngine.Object[] { null }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.remove.destroyedByInstanceId", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(new UnityEngine.Object[] { doomedTex }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.remove.runtimeObjects", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(new UnityEngine.Object[] { runtimeTex, runtimeSprite }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        check("new.remove.remainingToEmpty", () =>
        {
            var logs = CaptureLogs(() => mem.Remove(new UnityEngine.Object[] { folder, sheetSprite, singleSprite }));
            return $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}|logs=[{logs}]";
        });
        AssetDatabase.SaveAssets();
        check("new.remove.afterSave", () => $"packables=[{Packables(mem)}]|{Dirty(mem, memBase)}");

        if (byStem.TryGetValue("Master", out var master))
        {
            var origA = File.ReadAllBytes(master.path);
            var origM = File.ReadAllBytes(master.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(master.atlas);
            check(master.key + ".remove.initial", () =>
                $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            check(master.key + ".remove.nullAndEmpty", () =>
            {
                var logs = CaptureLogs(() =>
                {
                    master.atlas.Remove(null);
                    master.atlas.Remove(Array.Empty<UnityEngine.Object>());
                });
                return $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{FileState(master.path, origA, origM)}";
            });

            check(master.key + ".remove.texKeepsSpriteAndSwapOrder", () =>
            {
                var logs = CaptureLogs(() => master.atlas.Remove(new UnityEngine.Object[] { singleTex }));
                return $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{FileState(master.path, origA, origM)}";
            });

            check(master.key + ".remove.repeatAndInvalidNoop", () =>
            {
                var logs = CaptureLogs(() => master.atlas.Remove(new UnityEngine.Object[] { singleTex, invalidGo, invalidRt }));
                return $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{FileState(master.path, origA, origM)}";
            });

            AssetDatabase.SaveAssets();
            check(master.key + ".remove.afterSave", () =>
                $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            check(master.key + ".remove.clearAllAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => master.atlas.Remove(new UnityEngine.Object[] { folder, singleSprite, sheetSprite }));
                AssetDatabase.SaveAssetIfDirty(master.atlas);
                return $"packables=[{Packables(master.atlas)}]|{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}";
            });

            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path);
            check(master.key + ".remove.afterReimport", () =>
                $"sameRef={ReferenceEquals(master.atlas, reloaded)}|packables=[{Packables(reloaded)}]|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(master.path, origA, origM)}");
            master = (master.path, master.key, reloaded);
        }

        if (byStem.TryGetValue("NullAndMissing", out var nm))
        {
            var origM = File.ReadAllBytes(nm.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(nm.atlas);
            string MetaOnly() => $"metaChanged={!File.ReadAllBytes(nm.path + ".meta").SequenceEqual(origM)}|metaPackables={PackablesBlock(nm.path + ".meta")}";
            check(nm.key + ".remove.initial", () =>
                $"packables=[{Packables(nm.atlas)}]|{Dirty(nm.atlas, baseDirty)}|{MetaOnly()}");

            check(nm.key + ".remove.nullEntryAndSave", () =>
            {
                var logs = CaptureLogs(() => nm.atlas.Remove(new UnityEngine.Object[] { null }));
                AssetDatabase.SaveAssetIfDirty(nm.atlas);
                return $"packables=[{Packables(nm.atlas)}]|{Dirty(nm.atlas, baseDirty)}|logs=[{logs}]|{MetaOnly()}";
            });

            check(nm.key + ".remove.addAndRemoveRuntimeSave", () =>
            {
                var doomed2 = new Texture2D(2, 2) { name = "Doomed2" };
                nm.atlas.Add(new UnityEngine.Object[] { runtimeTex, doomed2 });
                UnityEngine.Object.DestroyImmediate(doomed2);
                var logs = CaptureLogs(() => nm.atlas.Remove(new UnityEngine.Object[] { runtimeTex, doomed2, singleSprite }));
                AssetDatabase.SaveAssetIfDirty(nm.atlas);
                return $"packables=[{Packables(nm.atlas)}]|{Dirty(nm.atlas, baseDirty)}|logs=[{logs}]|{MetaOnly()}";
            });
        }

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterBaseDirty = byStem.ContainsKey("Master") ? EditorUtility.GetDirtyCount(master.atlas) : 0;
            check(variant.key + ".remove.initial", () =>
                $"packables=[{Packables(variant.atlas)}]|{Dirty(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            check(variant.key + ".remove.modified", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.Remove(new UnityEngine.Object[] { folder }));
                return $"packables=[{Packables(variant.atlas)}]|{Dirty(variant.atlas, baseDirty)}|masterPackables=[{Packables(master.atlas)}]|masterDirtyDelta={EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            AssetDatabase.SaveAssetIfDirty(variant.atlas);
            check(variant.key + ".remove.afterSaveIfDirty", () =>
                $"packables=[{Packables(variant.atlas)}]|{Dirty(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
            check(variant.key + ".remove.afterReimport", () =>
                $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|packables=[{Packables(reloaded)}]|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(variant.path, origA, origM)}");
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            var origA = File.ReadAllBytes(omitted.path);
            var origM = File.ReadAllBytes(omitted.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omitted.atlas);
            check(omitted.key + ".remove.initial", () =>
                $"packables=[{Packables(omitted.atlas)}]|{Dirty(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetBlock: false)}");

            check(omitted.key + ".remove.modified", () =>
            {
                var logs = CaptureLogs(() => omitted.atlas.Remove(new UnityEngine.Object[] { singleSprite }));
                return $"packables=[{Packables(omitted.atlas)}]|{Dirty(omitted.atlas, baseDirty)}|logs=[{logs}]|{FileState(omitted.path, origA, origM, includeAssetBlock: false)}";
            });

            AssetDatabase.SaveAssetIfDirty(omitted.atlas);
            check(omitted.key + ".remove.afterSaveIfDirty", () =>
                $"packables=[{Packables(omitted.atlas)}]|{Dirty(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|metaChanged={!File.ReadAllBytes(omitted.path + ".meta").SequenceEqual(origM)}|assetPackables={PackablesBlock(omitted.path)}|metaPackables={PackablesBlock(omitted.path + ".meta")}|assetTexture={YamlBlock(omitted.path, "textureSettings:")}|assetPacking={YamlBlock(omitted.path, "packingSettings:")}");
        }
    }

    static string VariantScaleLine(string file)
    {
        foreach (var line in File.ReadAllLines(file))
            if (line.TrimStart().StartsWith("variantMultiplier:", StringComparison.Ordinal))
                return line.Trim();
        return "<absent>";
    }

    static void RecordVariantScale(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        string Dirty(SpriteAtlas a, int baseDirty) =>
            $"dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string Unrelated(SpriteAtlas a) =>
            $"isVariant={a.isVariant}|master={Describe(a.GetMasterAtlas())}|packables=[{string.Join(",", a.GetPackables().Select(Describe))}]|texture=({DescribeTexture(a.GetTextureSettings())})|packing=({DescribePacking(a.GetPackingSettings())})|android=({DescribePlatform(a.GetPlatformSettings("Android"))})|include={a.IsIncludeInBuild()}";

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetLine = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetLine
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetScale={VariantScaleLine(path)}|metaScale={VariantScaleLine(path + ".meta")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaScale={VariantScaleLine(path + ".meta")}";
        }

        string CaptureLogs(Action action)
        {
            var logs = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition);
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return string.Join(";", logs);
        }

        check("null.setVariantScale.valid", () => { none.SetVariantScale(0.5f); return "ok"; });
        check("null.setVariantScale.outOfRange", () => { none.SetVariantScale(-1f); return "ok"; });

        var mem = new SpriteAtlas();
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.variantScale.sameDefault", () =>
        {
            var logs = CaptureLogs(() => mem.SetVariantScale(1f));
            return $"{Dirty(mem, memBase)}|isVariant={mem.isVariant}|logs=[{logs}]";
        });
        check("new.variantScale.valid", () =>
        {
            var logs = CaptureLogs(() => mem.SetVariantScale(0.5f));
            return $"{Dirty(mem, memBase)}|isVariant={mem.isVariant}|logs=[{logs}]";
        });
        check("new.variantScale.extremes", () =>
        {
            var logs = CaptureLogs(() =>
            {
                mem.SetVariantScale(-2f);
                mem.SetVariantScale(0f);
                mem.SetVariantScale(0.05f);
                mem.SetVariantScale(1.5f);
                mem.SetVariantScale(float.PositiveInfinity);
                mem.SetVariantScale(float.NegativeInfinity);
                mem.SetVariantScale(float.NaN);
            });
            return $"{Dirty(mem, memBase)}|isVariant={mem.isVariant}|logs=[{logs}]";
        });
        AssetDatabase.SaveAssets();
        check("new.variantScale.afterSave", () => $"{Dirty(mem, memBase)}|isVariant={mem.isVariant}");

        byStem.TryGetValue("Master", out var master);

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterBaseDirty = master.atlas != null ? EditorUtility.GetDirtyCount(master.atlas) : 0;

            check(variant.key + ".variantScale.initial", () =>
                $"{Dirty(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            check(variant.key + ".variantScale.sameRepeat", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetVariantScale(0.5f));
                return $"{Dirty(variant.atlas, baseDirty)}|masterDirtyDelta={EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".variantScale.validAndSave", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetVariantScale(0.75f));
                AssetDatabase.SaveAssets();
                return $"{Dirty(variant.atlas, baseDirty)}|masterDirtyDelta={EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty}|logs=[{logs}]|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}";
            });

            void TestExtreme(string label, float input)
            {
                check(variant.key + ".variantScale." + label, () =>
                {
                    var logs = CaptureLogs(() => variant.atlas.SetVariantScale(input));
                    AssetDatabase.SaveAssetIfDirty(variant.atlas);
                    return $"{Dirty(variant.atlas, baseDirty)}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
                });
            }

            TestExtreme("clampZero", 0f);
            TestExtreme("clampNegative", -5f);
            TestExtreme("clampBelowMin", 0.05f);
            TestExtreme("clampNegInf", float.NegativeInfinity);
            TestExtreme("clampAboveMax", 2.5f);
            TestExtreme("clampPosInf", float.PositiveInfinity);
            TestExtreme("nan", float.NaN);
            TestExtreme("restoreValid", 0.25f);

            AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
            check(variant.key + ".variantScale.afterReimport", () =>
                $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(variant.path, origA, origM)}");
        }

        if (master.atlas != null)
        {
            var origA = File.ReadAllBytes(master.path);
            var origM = File.ReadAllBytes(master.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(master.atlas);

            check(master.key + ".variantScale.initial", () =>
                $"{Dirty(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            check(master.key + ".variantScale.sameRepeat", () =>
            {
                var logs = CaptureLogs(() => master.atlas.SetVariantScale(1f));
                return $"{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{FileState(master.path, origA, origM)}";
            });

            check(master.key + ".variantScale.nonVariantSave", () =>
            {
                var logs = CaptureLogs(() => master.atlas.SetVariantScale(0.4f));
                AssetDatabase.SaveAssets();
                return $"{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}";
            });

            check(master.key + ".variantScale.nonVariantClampSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => master.atlas.SetVariantScale(0.01f));
                AssetDatabase.SaveAssetIfDirty(master.atlas);
                return $"{Dirty(master.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}";
            });

            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path);
            check(master.key + ".variantScale.afterReimport", () =>
                $"sameRef={ReferenceEquals(master.atlas, reloaded)}|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(master.path, origA, origM)}");
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            var origA = File.ReadAllBytes(omitted.path);
            var origM = File.ReadAllBytes(omitted.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omitted.atlas);

            check(omitted.key + ".variantScale.initial", () =>
                $"{Dirty(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetLine: false)}");

            check(omitted.key + ".variantScale.modifiedAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => omitted.atlas.SetVariantScale(0.6f));
                AssetDatabase.SaveAssetIfDirty(omitted.atlas);
                return $"{Dirty(omitted.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(omitted.atlas)}|metaChanged={!File.ReadAllBytes(omitted.path + ".meta").SequenceEqual(origM)}|assetScale={VariantScaleLine(omitted.path)}|metaScale={VariantScaleLine(omitted.path + ".meta")}|assetTexture={YamlBlock(omitted.path, "textureSettings:")}|assetPacking={YamlBlock(omitted.path, "packingSettings:")}|assetPackables={PackablesBlock(omitted.path)}";
            });
        }
    }

    static string TopFieldLine(string file, string keyPrefix)
    {
        foreach (var line in File.ReadAllLines(file))
            if (line.TrimStart().StartsWith(keyPrefix, StringComparison.Ordinal))
                return line.Trim();
        return "<absent>";
    }

    static void RecordIsVariant(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        string State(SpriteAtlas a, int baseDirty) =>
            $"isVariant={a.isVariant}|master={Describe(a.GetMasterAtlas())}|tag={a.tag}|dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string Unrelated(SpriteAtlas a) =>
            $"packables=[{string.Join(",", a.GetPackables().Select(Describe))}]|texture=({DescribeTexture(a.GetTextureSettings())})|packing=({DescribePacking(a.GetPackingSettings())})|android=({DescribePlatform(a.GetPlatformSettings("Android"))})|include={a.IsIncludeInBuild()}";

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetFields = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetFields
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetIsVariant={TopFieldLine(path, "m_IsVariant:")}|assetMaster={TopFieldLine(path, "m_MasterAtlas:")}|assetTag={TopFieldLine(path, "m_Tag:")}|assetScale={VariantScaleLine(path)}|metaScale={VariantScaleLine(path + ".meta")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaScale={VariantScaleLine(path + ".meta")}";
        }

        string CaptureLogs(Action action)
        {
            var logs = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition);
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return string.Join(";", logs);
        }

        check("null.setIsVariant.true", () => { none.SetIsVariant(true); return "ok"; });
        check("null.setIsVariant.false", () => { none.SetIsVariant(false); return "ok"; });

        var mem = new SpriteAtlas { name = "MemAtlas" };
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.isVariant.sameFalse", () =>
        {
            var logs = CaptureLogs(() => mem.SetIsVariant(false));
            return $"{State(mem, memBase)}|logs=[{logs}]";
        });
        check("new.isVariant.toTrue", () =>
        {
            var logs = CaptureLogs(() => mem.SetIsVariant(true));
            return $"{State(mem, memBase)}|logs=[{logs}]";
        });
        check("new.isVariant.sameTrue", () =>
        {
            var logs = CaptureLogs(() => mem.SetIsVariant(true));
            return $"{State(mem, memBase)}|logs=[{logs}]";
        });
        check("new.isVariant.backToFalse", () =>
        {
            var logs = CaptureLogs(() => mem.SetIsVariant(false));
            return $"{State(mem, memBase)}|logs=[{logs}]";
        });
        AssetDatabase.SaveAssets();
        check("new.isVariant.afterSave", () => State(mem, memBase));

        byStem.TryGetValue("Master", out var master);

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterBaseDirty = master.atlas != null ? EditorUtility.GetDirtyCount(master.atlas) : 0;

            check(variant.key + ".isVariant.initial", () =>
                $"{State(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            check(variant.key + ".isVariant.sameTrue", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetIsVariant(true));
                return $"{State(variant.atlas, baseDirty)}|masterDirtyDelta={EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".isVariant.toFalse", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetIsVariant(false));
                return $"{State(variant.atlas, baseDirty)}|masterDirtyDelta={EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty}|logs=[{logs}]|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".isVariant.sameFalse", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetIsVariant(false));
                return $"{State(variant.atlas, baseDirty)}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            AssetDatabase.SaveAssets();
            check(variant.key + ".isVariant.afterSaveFalse", () =>
                $"{State(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            check(variant.key + ".isVariant.backToTrueAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetIsVariant(true));
                AssetDatabase.SaveAssetIfDirty(variant.atlas);
                return $"{State(variant.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}";
            });

            AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
            check(variant.key + ".isVariant.afterReimport", () =>
                $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|isVariant={reloaded.isVariant}|master={Describe(reloaded.GetMasterAtlas())}|tag={reloaded.tag}|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(variant.path, origA, origM)}");
        }

        if (master.atlas != null)
        {
            var origA = File.ReadAllBytes(master.path);
            var origM = File.ReadAllBytes(master.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(master.atlas);

            check(master.key + ".isVariant.initial", () =>
                $"{State(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            check(master.key + ".isVariant.sameFalse", () =>
            {
                var logs = CaptureLogs(() => master.atlas.SetIsVariant(false));
                return $"{State(master.atlas, baseDirty)}|logs=[{logs}]|{FileState(master.path, origA, origM)}";
            });

            check(master.key + ".isVariant.toTrueAndSave", () =>
            {
                var logs = CaptureLogs(() => master.atlas.SetIsVariant(true));
                AssetDatabase.SaveAssets();
                return $"{State(master.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}";
            });

            check(master.key + ".isVariant.backToFalseAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => master.atlas.SetIsVariant(false));
                AssetDatabase.SaveAssetIfDirty(master.atlas);
                return $"{State(master.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}";
            });

            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path);
            check(master.key + ".isVariant.afterReimport", () =>
                $"sameRef={ReferenceEquals(master.atlas, reloaded)}|isVariant={reloaded.isVariant}|master={Describe(reloaded.GetMasterAtlas())}|tag={reloaded.tag}|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(master.path, origA, origM)}");
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            var origA = File.ReadAllBytes(omitted.path);
            var origM = File.ReadAllBytes(omitted.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omitted.atlas);

            check(omitted.key + ".isVariant.initial", () =>
                $"{State(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetFields: false)}");

            check(omitted.key + ".isVariant.sameFalse", () =>
            {
                var logs = CaptureLogs(() => omitted.atlas.SetIsVariant(false));
                return $"{State(omitted.atlas, baseDirty)}|logs=[{logs}]|{FileState(omitted.path, origA, origM, includeAssetFields: false)}";
            });

            check(omitted.key + ".isVariant.toTrueAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => omitted.atlas.SetIsVariant(true));
                AssetDatabase.SaveAssetIfDirty(omitted.atlas);
                return $"{State(omitted.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(omitted.atlas)}|metaChanged={!File.ReadAllBytes(omitted.path + ".meta").SequenceEqual(origM)}|assetIsVariant={TopFieldLine(omitted.path, "m_IsVariant:")}|assetMaster={TopFieldLine(omitted.path, "m_MasterAtlas:")}|assetTag={TopFieldLine(omitted.path, "m_Tag:")}|assetTexture={YamlBlock(omitted.path, "textureSettings:")}|assetPacking={YamlBlock(omitted.path, "packingSettings:")}";
            });
        }
    }

    static void RecordMasterAtlas(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        string State(SpriteAtlas a, int baseDirty) =>
            $"isVariant={a.isVariant}|master={Describe(a.GetMasterAtlas())}|tag={a.tag}|dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string Unrelated(SpriteAtlas a) =>
            $"packables=[{string.Join(",", a.GetPackables().Select(Describe))}]|texture=({DescribeTexture(a.GetTextureSettings())})|packing=({DescribePacking(a.GetPackingSettings())})|android=({DescribePlatform(a.GetPlatformSettings("Android"))})|include={a.IsIncludeInBuild()}";

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetFields = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetFields
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetIsVariant={TopFieldLine(path, "m_IsVariant:")}|assetMaster={TopFieldLine(path, "m_MasterAtlas:")}|assetTag={TopFieldLine(path, "m_Tag:")}|assetScale={VariantScaleLine(path)}|metaScale={VariantScaleLine(path + ".meta")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaScale={VariantScaleLine(path + ".meta")}";
        }

        string CaptureLogs(Action action)
        {
            var logs = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition);
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return string.Join(";", logs);
        }

        byStem.TryGetValue("MasterA", out var masterA);
        byStem.TryGetValue("MasterB", out var masterB);
        byStem.TryGetValue("OtherVariant", out var otherVariant);

        check("null.setMaster.valid", () => { none.SetMasterAtlas(masterA.atlas); return "ok"; });
        check("null.setMaster.nullValue", () => { none.SetMasterAtlas(null); return "ok"; });

        var memNonVar = new SpriteAtlas { name = "MemNonVar" };
        int memNonVarBase = EditorUtility.GetDirtyCount(memNonVar);
        check("new.setMaster.nonVariantValid", () =>
        {
            var logs = CaptureLogs(() => memNonVar.SetMasterAtlas(masterA.atlas));
            return $"{State(memNonVar, memNonVarBase)}|logs=[{logs}]";
        });
        check("new.setMaster.nonVariantNull", () =>
        {
            var logs = CaptureLogs(() => memNonVar.SetMasterAtlas(null));
            return $"{State(memNonVar, memNonVarBase)}|logs=[{logs}]";
        });

        var memVar = new SpriteAtlas { name = "MemVar" };
        memVar.SetIsVariant(true);
        int memVarBase = EditorUtility.GetDirtyCount(memVar);
        check("new.setMaster.nullWhenAlreadyNull", () =>
        {
            var logs = CaptureLogs(() => memVar.SetMasterAtlas(null));
            return $"{State(memVar, memVarBase)}|logs=[{logs}]";
        });
        check("new.setMaster.assignPersistent", () =>
        {
            var logs = CaptureLogs(() => memVar.SetMasterAtlas(masterA.atlas));
            return $"{State(memVar, memVarBase)}|logs=[{logs}]";
        });
        check("new.setMaster.sameRepeat", () =>
        {
            var logs = CaptureLogs(() => memVar.SetMasterAtlas(masterA.atlas));
            return $"{State(memVar, memVarBase)}|logs=[{logs}]";
        });
        check("new.setMaster.assignTransientMaster", () =>
        {
            var logs = CaptureLogs(() => memVar.SetMasterAtlas(memNonVar));
            return $"{State(memVar, memVarBase)}|logs=[{logs}]";
        });
        check("new.setMaster.selfClears", () =>
        {
            var logs = CaptureLogs(() => memVar.SetMasterAtlas(memVar));
            return $"{State(memVar, memVarBase)}|logs=[{logs}]";
        });
        check("new.setMaster.variantAsMasterClears", () =>
        {
            memVar.SetMasterAtlas(masterB.atlas);
            var logs = CaptureLogs(() => memVar.SetMasterAtlas(otherVariant.atlas));
            return $"{State(memVar, memVarBase)}|logs=[{logs}]";
        });
        AssetDatabase.SaveAssets();
        check("new.setMaster.afterSave", () => State(memVar, memVarBase));

        if (masterA.atlas != null)
        {
            var origA = File.ReadAllBytes(masterA.path);
            var origM = File.ReadAllBytes(masterA.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(masterA.atlas);
            check(masterA.key + ".setMaster.nonVariantValid", () =>
            {
                var logs = CaptureLogs(() => masterA.atlas.SetMasterAtlas(masterB.atlas));
                return $"{State(masterA.atlas, baseDirty)}|logs=[{logs}]|{FileState(masterA.path, origA, origM)}";
            });
            check(masterA.key + ".setMaster.nonVariantNull", () =>
            {
                var logs = CaptureLogs(() => masterA.atlas.SetMasterAtlas(null));
                return $"{State(masterA.atlas, baseDirty)}|logs=[{logs}]|{FileState(masterA.path, origA, origM)}";
            });
        }

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterABaseDirty = EditorUtility.GetDirtyCount(masterA.atlas);
            int masterBBaseDirty = EditorUtility.GetDirtyCount(masterB.atlas);

            check(variant.key + ".setMaster.initial", () =>
                $"{State(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            check(variant.key + ".setMaster.sameRepeat", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetMasterAtlas(masterA.atlas));
                return $"{State(variant.atlas, baseDirty)}|masterADirtyDelta={EditorUtility.GetDirtyCount(masterA.atlas) - masterABaseDirty}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".setMaster.changeToMasterBAndSave", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetMasterAtlas(masterB.atlas));
                AssetDatabase.SaveAssets();
                return $"{State(variant.atlas, baseDirty)}|masterADirtyDelta={EditorUtility.GetDirtyCount(masterA.atlas) - masterABaseDirty}|masterBDirtyDelta={EditorUtility.GetDirtyCount(masterB.atlas) - masterBBaseDirty}|logs=[{logs}]|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".setMaster.selfClearsAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetMasterAtlas(variant.atlas));
                AssetDatabase.SaveAssetIfDirty(variant.atlas);
                return $"{State(variant.atlas, baseDirty)}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".setMaster.variantAsMasterClearsAndSaveIfDirty", () =>
            {
                variant.atlas.SetMasterAtlas(masterA.atlas);
                var logs = CaptureLogs(() => variant.atlas.SetMasterAtlas(otherVariant.atlas));
                AssetDatabase.SaveAssetIfDirty(variant.atlas);
                return $"{State(variant.atlas, baseDirty)}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".setMaster.clearNullWhenAlreadyNull", () =>
            {
                var logs = CaptureLogs(() => variant.atlas.SetMasterAtlas(null));
                return $"{State(variant.atlas, baseDirty)}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".setMaster.assignAndClearNullAndSave", () =>
            {
                variant.atlas.SetMasterAtlas(masterB.atlas);
                var logs = CaptureLogs(() => variant.atlas.SetMasterAtlas(null));
                AssetDatabase.SaveAssetIfDirty(variant.atlas);
                return $"{State(variant.atlas, baseDirty)}|logs=[{logs}]|{FileState(variant.path, origA, origM)}";
            });

            check(variant.key + ".setMaster.assignFinalAndReimport", () =>
            {
                variant.atlas.SetMasterAtlas(masterB.atlas);
                AssetDatabase.SaveAssetIfDirty(variant.atlas);
                AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
                var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
                return $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|isVariant={reloaded.isVariant}|master={Describe(reloaded.GetMasterAtlas())}|tag={reloaded.tag}|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(variant.path, origA, origM)}";
            });
        }

        if (otherVariant.atlas != null)
        {
            var origA = File.ReadAllBytes(otherVariant.path);
            var origM = File.ReadAllBytes(otherVariant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(otherVariant.atlas);

            check(otherVariant.key + ".setMaster.assignTransientSave", () =>
            {
                var logs = CaptureLogs(() => otherVariant.atlas.SetMasterAtlas(memNonVar));
                AssetDatabase.SaveAssetIfDirty(otherVariant.atlas);
                return $"{State(otherVariant.atlas, baseDirty)}|logs=[{logs}]|{FileState(otherVariant.path, origA, origM)}";
            });

            check(otherVariant.key + ".setMaster.cyclicVariantRejected", () =>
            {
                var currentVariant = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(byStem["Variant"].path);
                var logs = CaptureLogs(() => otherVariant.atlas.SetMasterAtlas(currentVariant));
                AssetDatabase.SaveAssetIfDirty(otherVariant.atlas);
                return $"{State(otherVariant.atlas, baseDirty)}|logs=[{logs}]|{FileState(otherVariant.path, origA, origM)}";
            });
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            var origA = File.ReadAllBytes(omitted.path);
            var origM = File.ReadAllBytes(omitted.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omitted.atlas);

            check(omitted.key + ".setMaster.initial", () =>
                $"{State(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetFields: false)}");

            check(omitted.key + ".setMaster.assignMasterAAndSaveIfDirty", () =>
            {
                var logs = CaptureLogs(() => omitted.atlas.SetMasterAtlas(masterA.atlas));
                AssetDatabase.SaveAssetIfDirty(omitted.atlas);
                return $"{State(omitted.atlas, baseDirty)}|logs=[{logs}]|{Unrelated(omitted.atlas)}|metaChanged={!File.ReadAllBytes(omitted.path + ".meta").SequenceEqual(origM)}|assetIsVariant={TopFieldLine(omitted.path, "m_IsVariant:")}|assetMaster={TopFieldLine(omitted.path, "m_MasterAtlas:")}|assetTag={TopFieldLine(omitted.path, "m_Tag:")}|assetTexture={YamlBlock(omitted.path, "textureSettings:")}|assetPacking={YamlBlock(omitted.path, "packingSettings:")}";
            });
        }
    }

    static string PlatformBlock(string file)
    {
        var lines = File.ReadAllLines(file);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed == "platformSettings: []") return "[]";
            if (trimmed != "platformSettings:") continue;
            int indent = lines[i].Length - trimmed.Length;
            var items = new System.Collections.Generic.List<string>();
            for (int j = i + 1; j < lines.Length; j++)
            {
                string t = lines[j].TrimStart();
                int childIndent = lines[j].Length - t.Length;
                if (childIndent < indent || (childIndent == indent && !t.StartsWith("- ", StringComparison.Ordinal))) break;
                items.Add(lines[j].Trim());
            }
            return string.Join(";", items);
        }
        return "<absent>";
    }

    static void RecordSetPlatformSettings(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        string Dirty(SpriteAtlas a, int baseDirty) =>
            $"dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string Unrelated(SpriteAtlas a) =>
            $"texture=({DescribeTexture(a.GetTextureSettings())})|packing=({DescribePacking(a.GetPackingSettings())})|include={a.IsIncludeInBuild()}";

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetBlock = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetBlock
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetPlatform={PlatformBlock(path)}|metaPlatform={PlatformBlock(path + ".meta")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaPlatform={PlatformBlock(path + ".meta")}";
        }

        check("null.setPlatform.validSrc", () =>
        {
            var src = new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 256 };
            string thrown = "none";
            try { none.SetPlatformSettings(src); }
            catch (Exception e) { thrown = e.GetType().Name; }
            return $"thrown={thrown}|srcNameAfter={src.name ?? "<null>"}";
        });
        check("null.setPlatform.nullSrc", () => { none.SetPlatformSettings(null); return "ok"; });

        var mem = new SpriteAtlas();
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.platform.nullSrc", () =>
        {
            string thrown = "none";
            try { mem.SetPlatformSettings(null); }
            catch (Exception e) { thrown = e.GetType().Name; }
            return $"thrown={thrown}|{Dirty(mem, memBase)}";
        });
        var memWeb = new TextureImporterPlatformSettings { name = "WebGL", overridden = false, maxTextureSize = 256 };
        mem.SetPlatformSettings(memWeb);
        check("new.platform.absentNotOverridden", () =>
            $"{DescribePlatform(mem.GetPlatformSettings("WebGL"))}|{Dirty(mem, memBase)}");
        var memDef = new TextureImporterPlatformSettings { name = "DefaultTexturePlatform", overridden = false, maxTextureSize = 512, compressionQuality = 77 };
        mem.SetPlatformSettings(memDef);
        memDef.maxTextureSize = 64;
        check("new.platform.defaultNotOverridden", () =>
            $"{DescribePlatform(mem.GetPlatformSettings("DefaultTexturePlatform"))}|{Dirty(mem, memBase)}");
        var memIos = new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 256, format = TextureImporterFormat.ASTC_6x6 };
        mem.SetPlatformSettings(memIos);
        string memIosNameAfter = memIos.name ?? "<null>";
        memIos.maxTextureSize = 32;
        memIos.name = "Mutated";
        check("new.platform.aliasAndCopy", () =>
            $"srcNameAfter={memIosNameAfter}|{DescribePlatform(mem.GetPlatformSettings("iOS"))}|{DescribePlatform(mem.GetPlatformSettings("iPhone"))}|{Dirty(mem, memBase)}");
        AssetDatabase.SaveAssets();
        check("new.platform.afterSave", () =>
            $"{DescribePlatform(mem.GetPlatformSettings("iOS"))}|{Dirty(mem, memBase)}");

        if (byStem.TryGetValue("Master", out var master))
        {
            var origA = File.ReadAllBytes(master.path);
            var origM = File.ReadAllBytes(master.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(master.atlas);
            check(master.key + ".setPlatform.initial", () =>
                $"def=({DescribePlatform(master.atlas.GetPlatformSettings("DefaultTexturePlatform"))})|and=({DescribePlatform(master.atlas.GetPlatformSettings("Android"))})|ios=({DescribePlatform(master.atlas.GetPlatformSettings("iOS"))})|{Dirty(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            // Same-value repeat still marks dirty.
            master.atlas.SetPlatformSettings(master.atlas.GetPlatformSettings("Android"));
            check(master.key + ".setPlatform.sameRepeat", () =>
                $"and=({DescribePlatform(master.atlas.GetPlatformSettings("Android"))})|{Dirty(master.atlas, baseDirty)}|{FileState(master.path, origA, origM)}");

            // Absent non-Default platform with overridden=false is not inserted, but still marks dirty.
            var webOff = new TextureImporterPlatformSettings { name = "WebGL", overridden = false, maxTextureSize = 256 };
            master.atlas.SetPlatformSettings(webOff);
            check(master.key + ".setPlatform.absentNotOverridden", () =>
                $"web=({DescribePlatform(master.atlas.GetPlatformSettings("WebGL"))})|{Dirty(master.atlas, baseDirty)}|{FileState(master.path, origA, origM)}");

            // Absent DefaultTexturePlatform with overridden=false IS inserted.
            var defOff = new TextureImporterPlatformSettings
            {
                name = "DefaultTexturePlatform",
                overridden = false,
                maxTextureSize = 512,
                textureCompression = TextureImporterCompression.CompressedHQ,
                compressionQuality = 75,
            };
            master.atlas.SetPlatformSettings(defOff);
            check(master.key + ".setPlatform.defaultNotOverridden", () =>
                $"def=({DescribePlatform(master.atlas.GetPlatformSettings("DefaultTexturePlatform"))})|{Dirty(master.atlas, baseDirty)}");

            // Replace existing iPhone/iOS in place via alias, checking in-place name normalization and defensive copy.
            var ios = new TextureImporterPlatformSettings
            {
                name = "iPhone",
                overridden = true,
                maxTextureSize = 256,
                resizeAlgorithm = TextureResizeAlgorithm.Bilinear,
                format = TextureImporterFormat.ASTC_6x6,
                textureCompression = TextureImporterCompression.CompressedLQ,
                compressionQuality = 33,
                crunchedCompression = true,
                allowsAlphaSplitting = true,
                ignorePlatformSupport = false,
                androidETC2FallbackOverride = AndroidETC2FallbackOverride.Quality16Bit,
            };
            master.atlas.SetPlatformSettings(ios);
            string iosNameAfter = ios.name ?? "<null>";
            ios.maxTextureSize = 32;
            ios.overridden = false;
            ios.name = "Mutated";
            check(master.key + ".setPlatform.aliasReplaceInPlace", () =>
                $"srcNameAfter={iosNameAfter}|ios=({DescribePlatform(master.atlas.GetPlatformSettings("iOS"))})|iphone=({DescribePlatform(master.atlas.GetPlatformSettings("iPhone"))})|{Dirty(master.atlas, baseDirty)}");

            // Append Standalone via StandaloneWindows64 alias.
            var win = new TextureImporterPlatformSettings
            {
                name = "StandaloneWindows64",
                overridden = true,
                maxTextureSize = 4096,
                format = TextureImporterFormat.DXT5,
                compressionQuality = 90,
            };
            master.atlas.SetPlatformSettings(win);
            string winNameAfter = win.name ?? "<null>";
            check(master.key + ".setPlatform.aliasAppendStandalone", () =>
                $"srcNameAfter={winNameAfter}|standalone=({DescribePlatform(master.atlas.GetPlatformSettings("Standalone"))})|linux=({DescribePlatform(master.atlas.GetPlatformSettings("StandaloneLinux64"))})|{Dirty(master.atlas, baseDirty)}");

            // Replace existing Android in place with overridden=false (stays in list with overridden=false).
            var andOff = master.atlas.GetPlatformSettings("Android");
            andOff.overridden = false;
            andOff.maxTextureSize = 128;
            master.atlas.SetPlatformSettings(andOff);
            check(master.key + ".setPlatform.replaceOverriddenFalse", () =>
                $"and=({DescribePlatform(master.atlas.GetPlatformSettings("Android"))})|{Dirty(master.atlas, baseDirty)}");

            // Persist via SaveAssets: check exact entry ordering and unrelated settings preservation.
            AssetDatabase.SaveAssets();
            check(master.key + ".setPlatform.afterSave", () =>
                $"and=({DescribePlatform(master.atlas.GetPlatformSettings("Android"))})|ios=({DescribePlatform(master.atlas.GetPlatformSettings("iOS"))})|def=({DescribePlatform(master.atlas.GetPlatformSettings("DefaultTexturePlatform"))})|std=({DescribePlatform(master.atlas.GetPlatformSettings("Standalone"))})|{Dirty(master.atlas, baseDirty)}|{Unrelated(master.atlas)}|{FileState(master.path, origA, origM)}");

            // Edge values: unknown target name, negative size/quality, null name -> "".
            var unknown = new TextureImporterPlatformSettings { name = "CustomTarget", overridden = true, maxTextureSize = -16, compressionQuality = -5 };
            master.atlas.SetPlatformSettings(unknown);
            var empty = new TextureImporterPlatformSettings { name = null, overridden = true, maxTextureSize = 64 };
            master.atlas.SetPlatformSettings(empty);
            string emptyNameAfter = empty.name ?? "<null>";
            AssetDatabase.SaveAssetIfDirty(master.atlas);
            check(master.key + ".setPlatform.afterSaveEdge", () =>
                $"emptyNameAfter={emptyNameAfter}|custom=({DescribePlatform(master.atlas.GetPlatformSettings("CustomTarget"))})|empty=({DescribePlatform(master.atlas.GetPlatformSettings(""))})|nullTarget=({DescribePlatform(master.atlas.GetPlatformSettings(null))})|{Dirty(master.atlas, baseDirty)}|{FileState(master.path, origA, origM)}");

            // Reimport behavior: V1 keeps instance/saved; V2 reloads fresh clean instance reverted to .meta.
            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path);
            check(master.key + ".setPlatform.afterReimport", () =>
                $"sameRef={ReferenceEquals(master.atlas, reloaded)}|and=({DescribePlatform(reloaded.GetPlatformSettings("Android"))})|ios=({DescribePlatform(reloaded.GetPlatformSettings("iOS"))})|std=({DescribePlatform(reloaded.GetPlatformSettings("Standalone"))})|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(master.path, origA, origM)}");
            master = (master.path, master.key, reloaded);
        }

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterBaseDirty = byStem.ContainsKey("Master") ? EditorUtility.GetDirtyCount(master.atlas) : 0;
            check(variant.key + ".setPlatform.initial", () =>
                $"and=({DescribePlatform(variant.atlas.GetPlatformSettings("Android"))})|{Dirty(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            var varAnd = variant.atlas.GetPlatformSettings("Android");
            varAnd.maxTextureSize = 256;
            varAnd.compressionQuality = 22;
            variant.atlas.SetPlatformSettings(varAnd);
            var varIos = new TextureImporterPlatformSettings { name = "iPhone", overridden = true, maxTextureSize = 128 };
            variant.atlas.SetPlatformSettings(varIos);
            check(variant.key + ".setPlatform.modified", () =>
                $"and=({DescribePlatform(variant.atlas.GetPlatformSettings("Android"))})|ios=({DescribePlatform(variant.atlas.GetPlatformSettings("iOS"))})|{Dirty(variant.atlas, baseDirty)}|masterAnd=({DescribePlatform(master.atlas.GetPlatformSettings("Android"))})|masterDirtyDelta={EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty}|{FileState(variant.path, origA, origM)}");

            var masterEdit = master.atlas.GetPlatformSettings("Android");
            masterEdit.maxTextureSize = 4096;
            masterEdit.overridden = true;
            master.atlas.SetPlatformSettings(masterEdit);
            check(variant.key + ".setPlatform.afterMasterMemEdit", () =>
                $"and=({DescribePlatform(variant.atlas.GetPlatformSettings("Android"))})|masterAnd=({DescribePlatform(master.atlas.GetPlatformSettings("Android"))})|{Dirty(variant.atlas, baseDirty)}");

            AssetDatabase.SaveAssetIfDirty(variant.atlas);
            check(variant.key + ".setPlatform.afterSaveIfDirty", () =>
                $"and=({DescribePlatform(variant.atlas.GetPlatformSettings("Android"))})|ios=({DescribePlatform(variant.atlas.GetPlatformSettings("iOS"))})|{Dirty(variant.atlas, baseDirty)}|{Unrelated(variant.atlas)}|{FileState(variant.path, origA, origM)}");

            AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
            check(variant.key + ".setPlatform.afterReimport", () =>
                $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|and=({DescribePlatform(reloaded.GetPlatformSettings("Android"))})|ios=({DescribePlatform(reloaded.GetPlatformSettings("iOS"))})|dirty={EditorUtility.IsDirty(reloaded)}|{Unrelated(reloaded)}|{FileState(variant.path, origA, origM)}");
        }

        if (byStem.TryGetValue("Legacy", out var legacy))
        {
            var origA = File.ReadAllBytes(legacy.path);
            var origM = File.ReadAllBytes(legacy.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(legacy.atlas);
            check(legacy.key + ".setPlatform.initial", () =>
                $"and=({DescribePlatform(legacy.atlas.GetPlatformSettings("Android"))})|wsa=({DescribePlatform(legacy.atlas.GetPlatformSettings("WindowsStoreApps"))})|ps5=({DescribePlatform(legacy.atlas.GetPlatformSettings("PS5"))})|{Dirty(legacy.atlas, baseDirty)}|{Unrelated(legacy.atlas)}|{FileState(legacy.path, origA, origM, includeAssetBlock: false)}");

            var ps5 = legacy.atlas.GetPlatformSettings("PS5");
            ps5.compressionQuality = 55;
            legacy.atlas.SetPlatformSettings(ps5);
            var web = new TextureImporterPlatformSettings { name = "Web", overridden = true, maxTextureSize = 1024 };
            legacy.atlas.SetPlatformSettings(web);
            check(legacy.key + ".setPlatform.modified", () =>
                $"ps5=({DescribePlatform(legacy.atlas.GetPlatformSettings("PS5"))})|web=({DescribePlatform(legacy.atlas.GetPlatformSettings("WebGL"))})|{Dirty(legacy.atlas, baseDirty)}|{FileState(legacy.path, origA, origM, includeAssetBlock: false)}");

            AssetDatabase.SaveAssetIfDirty(legacy.atlas);
            check(legacy.key + ".setPlatform.afterSaveIfDirty", () =>
                $"and=({DescribePlatform(legacy.atlas.GetPlatformSettings("Android"))})|wsa=({DescribePlatform(legacy.atlas.GetPlatformSettings("WindowsStoreApps"))})|ps5=({DescribePlatform(legacy.atlas.GetPlatformSettings("PS5"))})|web=({DescribePlatform(legacy.atlas.GetPlatformSettings("WebGL"))})|{Dirty(legacy.atlas, baseDirty)}|{Unrelated(legacy.atlas)}|{FileState(legacy.path, origA, origM, includeAssetBlock: true)}|assetTexture={YamlBlock(legacy.path, "textureSettings:")}|assetPacking={YamlBlock(legacy.path, "packingSettings:")}");
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            var origA = File.ReadAllBytes(omitted.path);
            var origM = File.ReadAllBytes(omitted.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omitted.atlas);
            check(omitted.key + ".setPlatform.initial", () =>
                $"and=({DescribePlatform(omitted.atlas.GetPlatformSettings("Android"))})|{Dirty(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetBlock: false)}");

            omitted.atlas.SetPlatformSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = 512, compressionQuality = 65 });
            check(omitted.key + ".setPlatform.modified", () =>
                $"and=({DescribePlatform(omitted.atlas.GetPlatformSettings("Android"))})|{Dirty(omitted.atlas, baseDirty)}|{FileState(omitted.path, origA, origM, includeAssetBlock: false)}");

            AssetDatabase.SaveAssetIfDirty(omitted.atlas);
            check(omitted.key + ".setPlatform.afterSaveIfDirty", () =>
                $"and=({DescribePlatform(omitted.atlas.GetPlatformSettings("Android"))})|{Dirty(omitted.atlas, baseDirty)}|{Unrelated(omitted.atlas)}|{FileState(omitted.path, origA, origM, includeAssetBlock: true)}|assetTexture={YamlBlock(omitted.path, "textureSettings:")}|assetPacking={YamlBlock(omitted.path, "packingSettings:")}");
        }
    }

    static string YamlBlock(string file, string keyLine)
    {
        var lines = File.ReadAllLines(file);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart() != keyLine) continue;
            int indent = lines[i].Length - lines[i].TrimStart().Length;
            var items = new System.Collections.Generic.List<string>();
            for (int j = i + 1; j < lines.Length; j++)
            {
                int childIndent = lines[j].Length - lines[j].TrimStart().Length;
                if (childIndent <= indent) break;
                items.Add(lines[j].Trim());
            }
            return string.Join(";", items);
        }
        return "<absent>";
    }

    static void RecordTextureSettings(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        string State(SpriteAtlas a, int baseDirty) =>
            $"{DescribeTexture(a.GetTextureSettings())}|dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string Unrelated(SpriteAtlas a) =>
            $"packing=({DescribePacking(a.GetPackingSettings())})|android=({DescribePlatform(a.GetPlatformSettings("Android"))})|include={a.IsIncludeInBuild()}";

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetBlock = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetBlock
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetTexture={YamlBlock(path, "textureSettings:")}|metaTexture={YamlBlock(path + ".meta", "textureSettings:")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaTexture={YamlBlock(path + ".meta", "textureSettings:")}";
        }

        check("null.setTexture", () => { none.SetTextureSettings(default); return "ok"; });

        var mem = new SpriteAtlas();
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.texture.initial", () => State(mem, memBase));
        mem.SetTextureSettings(mem.GetTextureSettings());
        check("new.texture.sameRepeat", () => State(mem, memBase));
        var memCopy = mem.GetTextureSettings();
        memCopy.anisoLevel = 8;
        memCopy.filterMode = FilterMode.Trilinear;
        memCopy.generateMipMaps = true;
        memCopy.readable = true;
        memCopy.sRGB = false;
        mem.SetTextureSettings(memCopy);
        check("new.texture.modifiedCopy", () => State(mem, memBase));
        mem.SetTextureSettings(new SpriteAtlasTextureSettings
        {
            anisoLevel = -5,
            filterMode = (FilterMode)99,
            generateMipMaps = true,
            readable = false,
            sRGB = true,
        });
        check("new.texture.edgeValues", () => State(mem, memBase));
        mem.SetTextureSettings(default);
        check("new.texture.zeroed", () => State(mem, memBase));
        AssetDatabase.SaveAssets();
        check("new.texture.afterSave", () => State(mem, memBase));

        if (byStem.TryGetValue("Master", out var master))
        {
            var origA = File.ReadAllBytes(master.path);
            var origM = File.ReadAllBytes(master.path + ".meta");
            var origSettings = master.atlas.GetTextureSettings();
            int baseDirty = EditorUtility.GetDirtyCount(master.atlas);
            check(master.key + ".setTexture.initial", () =>
                State(master.atlas, baseDirty) + "|" + Unrelated(master.atlas) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetTextureSettings(origSettings);
            check(master.key + ".setTexture.sameRepeat", () =>
                State(master.atlas, baseDirty) + "|" + FileState(master.path, origA, origM));
            var customCopy = origSettings;
            customCopy.anisoLevel = 6;
            customCopy.filterMode = FilterMode.Trilinear;
            customCopy.generateMipMaps = true;
            customCopy.readable = true;
            customCopy.sRGB = false;
            master.atlas.SetTextureSettings(customCopy);
            check(master.key + ".setTexture.modifiedCopy", () =>
                State(master.atlas, baseDirty) + "|" + Unrelated(master.atlas) + "|" + FileState(master.path, origA, origM));
            AssetDatabase.SaveAssets();
            check(master.key + ".setTexture.afterSave", () =>
                State(master.atlas, baseDirty) + "|" + Unrelated(master.atlas) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                anisoLevel = -5,
                filterMode = (FilterMode)99,
                generateMipMaps = false,
                readable = true,
                sRGB = false,
            });
            AssetDatabase.SaveAssetIfDirty(master.atlas);
            check(master.key + ".setTexture.afterSaveEdge", () =>
                State(master.atlas, baseDirty) + "|" + Unrelated(master.atlas) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetTextureSettings(origSettings);
            AssetDatabase.SaveAssetIfDirty(master.atlas);
            check(master.key + ".setTexture.afterRoundtrip", () =>
                State(master.atlas, baseDirty) + "|" + Unrelated(master.atlas) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetTextureSettings(customCopy);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path);
            check(master.key + ".setTexture.afterReimport", () =>
                $"sameRef={ReferenceEquals(master.atlas, reloaded)}|{DescribeTexture(reloaded.GetTextureSettings())}|dirty={EditorUtility.IsDirty(reloaded)}|" +
                Unrelated(reloaded) + "|" + FileState(master.path, origA, origM));
            reloaded.SetTextureSettings(origSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            master = (master.path, master.key, AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path));
        }

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterBaseDirty = byStem.ContainsKey("Master") ? EditorUtility.GetDirtyCount(master.atlas) : 0;
            check(variant.key + ".setTexture.initial", () =>
                State(variant.atlas, baseDirty) + "|" + Unrelated(variant.atlas) + "|" + FileState(variant.path, origA, origM));
            var variantCustom = variant.atlas.GetTextureSettings();
            variantCustom.anisoLevel = 12;
            variantCustom.filterMode = FilterMode.Trilinear;
            variantCustom.generateMipMaps = false;
            variantCustom.readable = false;
            variantCustom.sRGB = true;
            variant.atlas.SetTextureSettings(variantCustom);
            check(variant.key + ".setTexture.modified", () =>
                State(variant.atlas, baseDirty) + "|master=" + DescribeTexture(master.atlas.GetTextureSettings()) +
                "|masterDirtyDelta=" + (EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty) + "|" +
                FileState(variant.path, origA, origM));
            var masterEdit = master.atlas.GetTextureSettings();
            masterEdit.anisoLevel = 15;
            masterEdit.filterMode = FilterMode.Point;
            masterEdit.sRGB = false;
            master.atlas.SetTextureSettings(masterEdit);
            check(variant.key + ".setTexture.afterMasterMemEdit", () =>
                State(variant.atlas, baseDirty) + "|master=" + DescribeTexture(master.atlas.GetTextureSettings()));
            AssetDatabase.SaveAssetIfDirty(variant.atlas);
            check(variant.key + ".setTexture.afterSaveIfDirty", () =>
                State(variant.atlas, baseDirty) + "|" + Unrelated(variant.atlas) + "|" + FileState(variant.path, origA, origM));
            AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
            check(variant.key + ".setTexture.afterReimport", () =>
                $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|{DescribeTexture(reloaded.GetTextureSettings())}|dirty={EditorUtility.IsDirty(reloaded)}|" +
                Unrelated(reloaded) + "|" + FileState(variant.path, origA, origM));
        }

        if (byStem.TryGetValue("Legacy", out var legacy))
        {
            var origA = File.ReadAllBytes(legacy.path);
            var origM = File.ReadAllBytes(legacy.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(legacy.atlas);
            check(legacy.key + ".setTexture.initial", () =>
                State(legacy.atlas, baseDirty) + "|" + Unrelated(legacy.atlas) + "|" + FileState(legacy.path, origA, origM, includeAssetBlock: false));
            var s = legacy.atlas.GetTextureSettings();
            s.anisoLevel = 7;
            s.sRGB = false;
            legacy.atlas.SetTextureSettings(s);
            check(legacy.key + ".setTexture.modified", () =>
                State(legacy.atlas, baseDirty) + "|" + FileState(legacy.path, origA, origM, includeAssetBlock: false));
            AssetDatabase.SaveAssetIfDirty(legacy.atlas);
            check(legacy.key + ".setTexture.afterSaveIfDirty", () =>
                State(legacy.atlas, baseDirty) + "|" + Unrelated(legacy.atlas) + "|" +
                FileState(legacy.path, origA, origM, includeAssetBlock: true) +
                "|assetPacking=" + YamlBlock(legacy.path, "packingSettings:"));
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            var origA = File.ReadAllBytes(omitted.path);
            var origM = File.ReadAllBytes(omitted.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(omitted.atlas);
            check(omitted.key + ".setTexture.initial", () =>
                State(omitted.atlas, baseDirty) + "|" + Unrelated(omitted.atlas) + "|" + FileState(omitted.path, origA, origM, includeAssetBlock: false));
            var s = omitted.atlas.GetTextureSettings();
            s.anisoLevel = 3;
            s.filterMode = FilterMode.Point;
            s.readable = true;
            omitted.atlas.SetTextureSettings(s);
            check(omitted.key + ".setTexture.modified", () =>
                State(omitted.atlas, baseDirty) + "|" + FileState(omitted.path, origA, origM, includeAssetBlock: false));
            AssetDatabase.SaveAssetIfDirty(omitted.atlas);
            check(omitted.key + ".setTexture.afterSaveIfDirty", () =>
                State(omitted.atlas, baseDirty) + "|" + Unrelated(omitted.atlas) + "|" +
                FileState(omitted.path, origA, origM, includeAssetBlock: true) +
                "|assetPacking=" + YamlBlock(omitted.path, "packingSettings:"));
        }
    }

    static void RecordPackingSettings(string[] paths, SpriteAtlas none, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, Path.GetFileName(path), atlas);
        }

        string State(SpriteAtlas a, int baseDirty) =>
            $"{DescribePacking(a.GetPackingSettings())}|dirty={EditorUtility.IsDirty(a)}|dirtyDelta={EditorUtility.GetDirtyCount(a) - baseDirty}";

        string PackingBlock(string file)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart() != "packingSettings:") continue;
                int indent = lines[i].Length - lines[i].TrimStart().Length;
                var items = new System.Collections.Generic.List<string>();
                for (int j = i + 1; j < lines.Length; j++)
                {
                    int childIndent = lines[j].Length - lines[j].TrimStart().Length;
                    if (childIndent <= indent) break;
                    items.Add(lines[j].Trim());
                }
                return string.Join(";", items);
            }
            return "<absent>";
        }

        string FileState(string path, byte[] origAsset, byte[] origMeta, bool includeAssetBlock = true)
        {
            bool assetChanged = !File.ReadAllBytes(path).SequenceEqual(origAsset);
            bool metaChanged = !File.ReadAllBytes(path + ".meta").SequenceEqual(origMeta);
            return includeAssetBlock
                ? $"assetChanged={assetChanged}|metaChanged={metaChanged}|assetPacking={PackingBlock(path)}|metaPacking={PackingBlock(path + ".meta")}"
                : $"assetChanged={assetChanged}|metaChanged={metaChanged}|metaPacking={PackingBlock(path + ".meta")}";
        }

        check("null.setPacking", () => { none.SetPackingSettings(default); return "ok"; });

        var mem = new SpriteAtlas();
        int memBase = EditorUtility.GetDirtyCount(mem);
        check("new.packing.initial", () => State(mem, memBase));
        mem.SetPackingSettings(mem.GetPackingSettings());
        check("new.packing.sameRepeat", () => State(mem, memBase));
        mem.SetPackingSettings(new SpriteAtlasPackingSettings
        {
            blockOffset = 5,
            padding = 16,
            enableRotation = false,
            enableTightPacking = false,
            enableAlphaDilation = true,
        });
        check("new.packing.modified", () => State(mem, memBase));
        mem.SetPackingSettings(new SpriteAtlasPackingSettings
        {
            blockOffset = -3,
            padding = -7,
            enableRotation = true,
            enableTightPacking = false,
            enableAlphaDilation = true,
        });
        check("new.packing.edgeValues", () => State(mem, memBase));
        mem.SetPackingSettings(default);
        check("new.packing.zeroed", () => State(mem, memBase));
        AssetDatabase.SaveAssets();
        check("new.packing.afterSave", () => State(mem, memBase));

        if (byStem.TryGetValue("Master", out var master))
        {
            var origA = File.ReadAllBytes(master.path);
            var origM = File.ReadAllBytes(master.path + ".meta");
            var origSettings = master.atlas.GetPackingSettings();
            int baseDirty = EditorUtility.GetDirtyCount(master.atlas);
            check(master.key + ".setPacking.initial", () => State(master.atlas, baseDirty) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetPackingSettings(origSettings);
            check(master.key + ".setPacking.sameRepeat", () => State(master.atlas, baseDirty) + "|" + FileState(master.path, origA, origM));
            var custom = new SpriteAtlasPackingSettings
            {
                blockOffset = 3,
                padding = 12,
                enableRotation = false,
                enableTightPacking = false,
                enableAlphaDilation = true,
            };
            master.atlas.SetPackingSettings(custom);
            check(master.key + ".setPacking.modified", () => State(master.atlas, baseDirty) + "|" + FileState(master.path, origA, origM));
            AssetDatabase.SaveAssets();
            check(master.key + ".setPacking.afterSave", () => State(master.atlas, baseDirty) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                blockOffset = -2,
                padding = 0,
                enableRotation = true,
                enableTightPacking = true,
                enableAlphaDilation = false,
            });
            AssetDatabase.SaveAssetIfDirty(master.atlas);
            check(master.key + ".setPacking.afterSaveEdge", () => State(master.atlas, baseDirty) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetPackingSettings(origSettings);
            AssetDatabase.SaveAssetIfDirty(master.atlas);
            check(master.key + ".setPacking.afterRoundtrip", () => State(master.atlas, baseDirty) + "|" + FileState(master.path, origA, origM));
            master.atlas.SetPackingSettings(custom);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path);
            check(master.key + ".setPacking.afterReimport", () =>
                $"sameRef={ReferenceEquals(master.atlas, reloaded)}|{DescribePacking(reloaded.GetPackingSettings())}|dirty={EditorUtility.IsDirty(reloaded)}|" +
                FileState(master.path, origA, origM));
            // Restore master in memory/disk before variant checks so master state is clean and known.
            reloaded.SetPackingSettings(origSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(master.path, ImportAssetOptions.ForceUpdate);
            master = (master.path, master.key, AssetDatabase.LoadAssetAtPath<SpriteAtlas>(master.path));
        }

        if (byStem.TryGetValue("Variant", out var variant))
        {
            var origA = File.ReadAllBytes(variant.path);
            var origM = File.ReadAllBytes(variant.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(variant.atlas);
            int masterBaseDirty = byStem.ContainsKey("Master") ? EditorUtility.GetDirtyCount(master.atlas) : 0;
            check(variant.key + ".setPacking.initial", () => State(variant.atlas, baseDirty) + "|" + FileState(variant.path, origA, origM));
            var variantCustom = new SpriteAtlasPackingSettings
            {
                blockOffset = 7,
                padding = 32,
                enableRotation = false,
                enableTightPacking = true,
                enableAlphaDilation = true,
            };
            variant.atlas.SetPackingSettings(variantCustom);
            check(variant.key + ".setPacking.modified", () =>
                State(variant.atlas, baseDirty) + "|master=" + DescribePacking(master.atlas.GetPackingSettings()) +
                "|masterDirtyDelta=" + (EditorUtility.GetDirtyCount(master.atlas) - masterBaseDirty) + "|" +
                FileState(variant.path, origA, origM));
            master.atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                blockOffset = 9,
                padding = 2,
                enableRotation = true,
                enableTightPacking = false,
                enableAlphaDilation = false,
            });
            check(variant.key + ".setPacking.afterMasterMemEdit", () =>
                State(variant.atlas, baseDirty) + "|master=" + DescribePacking(master.atlas.GetPackingSettings()));
            AssetDatabase.SaveAssetIfDirty(variant.atlas);
            check(variant.key + ".setPacking.afterSaveIfDirty", () => State(variant.atlas, baseDirty) + "|" + FileState(variant.path, origA, origM));
            AssetDatabase.ImportAsset(variant.path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(variant.path);
            check(variant.key + ".setPacking.afterReimport", () =>
                $"sameRef={ReferenceEquals(variant.atlas, reloaded)}|{DescribePacking(reloaded.GetPackingSettings())}|dirty={EditorUtility.IsDirty(reloaded)}|" +
                FileState(variant.path, origA, origM));
        }

        if (byStem.TryGetValue("Legacy", out var legacy))
        {
            var origA = File.ReadAllBytes(legacy.path);
            var origM = File.ReadAllBytes(legacy.path + ".meta");
            int baseDirty = EditorUtility.GetDirtyCount(legacy.atlas);
            check(legacy.key + ".setPacking.initial", () => State(legacy.atlas, baseDirty) + "|" + FileState(legacy.path, origA, origM, includeAssetBlock: false));
            var s = legacy.atlas.GetPackingSettings();
            s.padding = 6;
            legacy.atlas.SetPackingSettings(s);
            check(legacy.key + ".setPacking.modified", () => State(legacy.atlas, baseDirty) + "|" + FileState(legacy.path, origA, origM, includeAssetBlock: false));
            AssetDatabase.SaveAssetIfDirty(legacy.atlas);
            check(legacy.key + ".setPacking.afterSaveIfDirty", () => State(legacy.atlas, baseDirty) + "|" + FileState(legacy.path, origA, origM, includeAssetBlock: true));
        }
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

    static void RecordImporterPlatformSettings(string[] paths, Action<string, Func<string>> check)
    {
        var byStem = new System.Collections.Generic.Dictionary<string, (string path, string key, SpriteAtlasImporter importer, SpriteAtlas atlas)>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            string key = Path.GetFileName(path);
            var raw1 = AssetImporter.GetAtPath(path);
            var raw2 = AssetImporter.GetAtPath(path);
            check(key + ".importer.lookup", () =>
                raw1 == null
                    ? "<null>"
                    : $"type={raw1.GetType().FullName}|isSpriteAtlasImporter={raw1 is SpriteAtlasImporter}|sameInstance={ReferenceEquals(raw1, raw2)}|assetPath={raw1.assetPath}");
            if (raw1 is SpriteAtlasImporter importer)
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                byStem[Path.GetFileNameWithoutExtension(path)] = (path, key, importer, atlas);
            }
        }

        check("control.textureImporter", () =>
        {
            var raw = AssetImporter.GetAtPath("Assets/Sprites/Single.png");
            return raw == null
                ? "<null>"
                : $"type={raw.GetType().FullName}|isSpriteAtlasImporter={raw is SpriteAtlasImporter}";
        });
        check("control.missingPath", () =>
        {
            var raw = AssetImporter.GetAtPath("Assets/ImporterPlatformAtlases/DoesNotExist.spriteatlasv2");
            return raw == null ? "<null>" : raw.GetType().FullName;
        });
        check("new.importer.getPlatform.Android", () =>
        {
            var uncreated = new SpriteAtlasImporter();
            return DescribePlatform(uncreated.GetPlatformSettings("Android"));
        });
        check("new.importer.getPlatform.null", () =>
        {
            var uncreated = new SpriteAtlasImporter();
            return DescribePlatform(uncreated.GetPlatformSettings(null));
        });

        string Label(string target) => target == null ? "<null>" : target.Length == 0 ? "<empty>" : target;

        void Query(string prefix, SpriteAtlasImporter importer, params string[] targets)
        {
            foreach (var target in targets)
                check(prefix + ".importerPlatform." + Label(target), () => DescribePlatform(importer.GetPlatformSettings(target)));
        }

        if (byStem.TryGetValue("Defaults", out var defaults))
        {
            Query(defaults.key, defaults.importer,
                "DefaultTexturePlatform", "Android", "iOS", "iPhone", "Standalone", "StandaloneWindows64",
                "WebGL", "", null, "UnknownPlatform");
        }

        if (byStem.TryGetValue("Omitted", out var omitted))
        {
            Query(omitted.key, omitted.importer,
                "DefaultTexturePlatform", "Android", "iOS", "StandaloneWindows64", "", null, "UnknownPlatform");
        }

        if (byStem.TryGetValue("Custom", out var custom))
        {
            Query(custom.key, custom.importer,
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

            check(custom.key + ".importerPlatform.identityAndMutationStored", () =>
            {
                var a = custom.importer.GetPlatformSettings("Android");
                var b = custom.importer.GetPlatformSettings("Android");
                bool distinct = !ReferenceEquals(a, b);
                a.name = "Mutated";
                a.maxTextureSize = 32;
                a.overridden = false;
                a.compressionQuality = 1;
                var c = custom.importer.GetPlatformSettings("Android");
                return $"distinct={distinct}|b=({DescribePlatform(b)})|c=({DescribePlatform(c)})";
            });

            check(custom.key + ".importerPlatform.identityAndMutationAbsent", () =>
            {
                var a = custom.importer.GetPlatformSettings("WebGL");
                var b = custom.importer.GetPlatformSettings("WebGL");
                bool distinct = !ReferenceEquals(a, b);
                a.name = "MutatedAbsent";
                a.maxTextureSize = 64;
                a.overridden = true;
                var c = custom.importer.GetPlatformSettings("WebGL");
                return $"distinct={distinct}|b=({DescribePlatform(b)})|c=({DescribePlatform(c)})";
            });

            check(custom.key + ".importerPlatform.unaffectedByInMemoryAtlasEdit", () =>
            {
                var edited = custom.atlas.GetPlatformSettings("Android");
                edited.maxTextureSize = 128;
                edited.compressionQuality = 11;
                custom.atlas.SetPlatformSettings(edited);
                return $"atlas=({DescribePlatform(custom.atlas.GetPlatformSettings("Android"))})|importer=({DescribePlatform(custom.importer.GetPlatformSettings("Android"))})";
            });
        }

        if (byStem.TryGetValue("CustomVariant", out var variant))
            Query(variant.key, variant.importer, "DefaultTexturePlatform", "Android", "iPhone", "Standalone", "PS5");

        if (byStem.TryGetValue("Legacy", out var legacy))
            Query(legacy.key, legacy.importer,
                "DefaultTexturePlatform", "Android", "Standalone", "StandaloneWindows64");

        if (byStem.TryGetValue("LegacyV3", out var legacyV3))
            Query(legacyV3.key, legacyV3.importer,
                "iPhone", "iOS", "WSAPlayer", "Windows Store Apps", "Switch", "Standalone", "StandaloneWindows64");
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
