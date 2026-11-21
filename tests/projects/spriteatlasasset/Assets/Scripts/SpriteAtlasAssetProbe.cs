using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what the Unity 6000.0 Editor answers for UnityEditor.U2D.SpriteAtlasAsset:
/// construction defaults, Load/Save contracts and exact YAML output, import/.meta timing,
/// in-memory mutators (SetIsVariant, SetMasterAtlas/GetMasterAtlas, Add, Remove, SetScriptablePacker),
/// and the nine Obsolete methods.
/// </summary>
static class SpriteAtlasAssetProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("SPRITE_ATLAS_ASSET_OUT");
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

        void SaveCopy(string projectRelativePath, string outName)
        {
            if (File.Exists(projectRelativePath))
                File.Copy(projectRelativePath, Path.Combine(writtenDir, outName), true);
        }

        string NormalizeMeta(string text) =>
            Regex.Replace(text, @"(?m)^guid: [0-9a-f]{32}$", "guid: <GUID>");

        var masterAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>("Assets/Atlases/Master.spriteatlasv2");
        var variantAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>("Assets/Atlases/Variant.spriteatlasv2");
        var folder = AssetDatabase.LoadAssetAtPath<Object>("Assets/Sprites/Folder");
        var singleTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Single.png");
        var singleSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Single.png");
        var sheetTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Sheet.png");
        var sheetSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Sheet.png").OfType<Sprite>()
            .OrderBy(s => s.name, StringComparer.Ordinal).First();
        var packerAsset = AssetDatabase.LoadAssetAtPath<ProbePacker>("Assets/Atlases/TestPacker.asset");

        Put("fixtures", string.Join("|", new[]
        {
            "master=" + Describe(masterAtlas),
            "variant=" + Describe(variantAtlas),
            "folder=" + Describe(folder),
            "singleTex=" + Describe(singleTex),
            "singleSprite=" + Describe(singleSprite),
            "sheetTex=" + Describe(sheetTex),
            "sheetSprite=" + Describe(sheetSprite),
            "packerAsset=" + Describe(packerAsset),
        }));

        // ---------------------------------------------------------------- 1. Construction & defaults
        var fresh = new SpriteAtlasAsset();
        Check("ctor.defaults", () =>
            $"name={fresh.name}|hideFlags={fresh.hideFlags}|isVariant={fresh.isVariant}|master={Describe(fresh.GetMasterAtlas())}|dirty={EditorUtility.IsDirty(fresh)}|dirtyCount={EditorUtility.GetDirtyCount(fresh)}|contains={AssetDatabase.Contains(fresh)}|path={AssetDatabase.GetAssetPath(fresh)}");

        // ---------------------------------------------------------------- 2. Load contracts
        Check("load.null", () =>
        {
            SpriteAtlasAsset r = null;
            var logs = CaptureLogs(() => r = SpriteAtlasAsset.Load(null), out var ex);
            return ex != null
                ? $"throws={ex.GetType().Name}|logs={logs}"
                : $"result={Describe(r)}|logs={logs}";
        });
        Check("load.empty", () =>
        {
            SpriteAtlasAsset r = null;
            var logs = CaptureLogs(() => r = SpriteAtlasAsset.Load(""), out var ex);
            return ex != null
                ? $"throws={ex.GetType().Name}|logs={logs}"
                : $"result={Describe(r)}|logs={logs}";
        });
        Check("load.missing", () =>
        {
            SpriteAtlasAsset r = null;
            var logs = CaptureLogs(() => r = SpriteAtlasAsset.Load("Assets/Atlases/DoesNotExist.spriteatlasv2"), out var ex);
            return ex != null
                ? $"throws={ex.GetType().Name}|logs={logs}"
                : $"result={Describe(r)}|logs={logs}";
        });
        Check("load.v1", () =>
        {
            SpriteAtlasAsset r = null;
            var logs = CaptureLogs(() => r = SpriteAtlasAsset.Load("Assets/Atlases/Legacy.spriteatlas"), out var ex);
            return ex != null
                ? $"throws={ex.GetType().Name}|logs={logs}"
                : $"result={Describe(r)}|logs={logs}";
        });
        Check("load.nonAtlas", () =>
        {
            SpriteAtlasAsset r = null;
            var logs = CaptureLogs(() => r = SpriteAtlasAsset.Load("Assets/Atlases/TestPacker.asset"), out var ex);
            return ex != null
                ? $"throws={ex.GetType().Name}|logs={logs}"
                : $"result={Describe(r)}|logs={logs}";
        });
        Check("load.master", () =>
        {
            SpriteAtlasAsset a = null;
            SpriteAtlasAsset b = null;
            var logs = CaptureLogs(() =>
            {
                a = SpriteAtlasAsset.Load("Assets/Atlases/Master.spriteatlasv2");
                b = SpriteAtlasAsset.Load("Assets/Atlases/Master.spriteatlasv2");
            }, out var ex);
            if (ex != null) return $"throws={ex.GetType().Name}|logs={logs}";
            return $"loaded={a != null}|distinct={a != null && b != null && !ReferenceEquals(a, b)}|name={a.name}|hideFlags={a.hideFlags}|isVariant={a.isVariant}|master={Describe(a.GetMasterAtlas())}|dirty={EditorUtility.IsDirty(a)}|dirtyCount={EditorUtility.GetDirtyCount(a)}|contains={AssetDatabase.Contains(a)}|path={AssetDatabase.GetAssetPath(a)}|logs={logs}";
        });
        Check("load.variant", () =>
        {
            SpriteAtlasAsset v = null;
            var logs = CaptureLogs(() => v = SpriteAtlasAsset.Load("Assets/Atlases/Variant.spriteatlasv2"), out var ex);
            if (ex != null) return $"throws={ex.GetType().Name}|logs={logs}";
            return $"loaded={v != null}|name={v.name}|hideFlags={v.hideFlags}|isVariant={v.isVariant}|master={Describe(v.GetMasterAtlas())}|dirty={EditorUtility.IsDirty(v)}|dirtyCount={EditorUtility.GetDirtyCount(v)}|contains={AssetDatabase.Contains(v)}|path={AssetDatabase.GetAssetPath(v)}|logs={logs}";
        });

        // ---------------------------------------------------------------- 3. In-memory mutators
        Check("mut.isVariantAndMaster", () =>
        {
            var a = new SpriteAtlasAsset();
            int d0 = EditorUtility.GetDirtyCount(a);
            a.SetIsVariant(false);
            int dSameFalse = EditorUtility.GetDirtyCount(a) - d0;
            a.SetIsVariant(true);
            int dToTrue = EditorUtility.GetDirtyCount(a) - d0;
            a.SetIsVariant(true);
            int dSameTrue = EditorUtility.GetDirtyCount(a) - d0;
            string logsSetMaster = CaptureLogs(() => a.SetMasterAtlas(masterAtlas), out _);
            int dAfterMaster = EditorUtility.GetDirtyCount(a) - d0;
            string masterWhileVariant = Describe(a.GetMasterAtlas());
            a.SetIsVariant(false);
            int dToFalse = EditorUtility.GetDirtyCount(a) - d0;
            string masterAfterFalse = Describe(a.GetMasterAtlas());
            string logsVariantAsMaster = CaptureLogs(() => a.SetMasterAtlas(variantAtlas), out _);
            int dAfterVariantMaster = EditorUtility.GetDirtyCount(a) - d0;
            string masterIsVariant = Describe(a.GetMasterAtlas());
            a.SetMasterAtlas(null);
            int dAfterNull = EditorUtility.GetDirtyCount(a) - d0;
            string masterAfterNull = Describe(a.GetMasterAtlas());

            var temp = new SpriteAtlas();
            a.SetMasterAtlas(temp);
            bool tempSet = a.GetMasterAtlas() == temp;
            Object.DestroyImmediate(temp);
            bool tempClearedAfterDestroy = a.GetMasterAtlas() == null;

            return $"dSameFalse={dSameFalse}|dToTrue={dToTrue}|dSameTrue={dSameTrue}|dAfterMaster={dAfterMaster}|logsSetMaster={logsSetMaster}|masterWhileVariant={masterWhileVariant}|dToFalse={dToFalse}|isVariantAfterFalse={a.isVariant}|masterAfterFalse={masterAfterFalse}|dAfterVariantMaster={dAfterVariantMaster}|logsVariantAsMaster={logsVariantAsMaster}|masterIsVariant={masterIsVariant}|dAfterNull={dAfterNull}|masterAfterNull={masterAfterNull}|tempSet={tempSet}|tempClearedAfterDestroy={tempClearedAfterDestroy}";
        });

        Check("mut.addAndRemove", () =>
        {
            var a = new SpriteAtlasAsset();
            int d0 = EditorUtility.GetDirtyCount(a);
            string logsAddNull = CaptureLogs(() => a.Add(null), out var exAddNull);
            int d1 = EditorUtility.GetDirtyCount(a) - d0;
            string logsAddEmpty = CaptureLogs(() => a.Add(Array.Empty<Object>()), out var exAddEmpty);
            int d2 = EditorUtility.GetDirtyCount(a) - d0;

            var invalidMat = new Material(Shader.Find("Sprites/Default")) { name = "BadMat" };
            string logsAddMixed = CaptureLogs(() => a.Add(new Object[] { singleTex, singleSprite, folder, singleTex, null, invalidMat }), out _);
            int d3 = EditorUtility.GetDirtyCount(a) - d0;

            string logsRemNull = CaptureLogs(() => a.Remove(null), out var exRemNull);
            int d4 = EditorUtility.GetDirtyCount(a) - d0;
            string logsRemEmpty = CaptureLogs(() => a.Remove(Array.Empty<Object>()), out var exRemEmpty);
            int d5 = EditorUtility.GetDirtyCount(a) - d0;
            string logsRemMixed = CaptureLogs(() => a.Remove(new Object[] { singleTex, invalidMat }), out _);
            int d6 = EditorUtility.GetDirtyCount(a) - d0;

            Object.DestroyImmediate(invalidMat);
            return $"exAddNull={exAddNull?.GetType().Name ?? "none"}|logsAddNull={logsAddNull}|d1={d1}|exAddEmpty={exAddEmpty?.GetType().Name ?? "none"}|logsAddEmpty={logsAddEmpty}|d2={d2}|logsAddMixed={logsAddMixed}|d3={d3}|exRemNull={exRemNull?.GetType().Name ?? "none"}|logsRemNull={logsRemNull}|d4={d4}|exRemEmpty={exRemEmpty?.GetType().Name ?? "none"}|logsRemEmpty={logsRemEmpty}|d5={d5}|logsRemMixed={logsRemMixed}|d6={d6}";
        });

        Check("mut.scriptablePacker", () =>
        {
            var a = new SpriteAtlasAsset();
            int d0 = EditorUtility.GetDirtyCount(a);
            string logsNull = CaptureLogs(() => a.SetScriptablePacker(null), out _);
            int d1 = EditorUtility.GetDirtyCount(a) - d0;
            string logsAsset = CaptureLogs(() => a.SetScriptablePacker(packerAsset), out _);
            int d2 = EditorUtility.GetDirtyCount(a) - d0;
            var memPacker = ScriptableObject.CreateInstance<ProbePacker>();
            string logsMem = CaptureLogs(() => a.SetScriptablePacker(memPacker), out _);
            int d3 = EditorUtility.GetDirtyCount(a) - d0;
            Object.DestroyImmediate(memPacker);
            return $"logsNull={logsNull}|d1={d1}|logsAsset={logsAsset}|d2={d2}|logsMem={logsMem}|d3={d3}";
        });

        // ---------------------------------------------------------------- 4. Obsolete methods (all 9)
        Check("obsolete.all", () =>
        {
#pragma warning disable CS0618
            var a = new SpriteAtlasAsset();
            int d0 = EditorUtility.GetDirtyCount(a);
            string logs = CaptureLogs(() =>
            {
                a.SetVariantScale(0.25f);
                a.SetIncludeInBuild(false);
                a.SetPackingSettings(new SpriteAtlasPackingSettings { padding = 8, blockOffset = 2, enableRotation = true, enableTightPacking = true, enableAlphaDilation = true });
                a.SetTextureSettings(new SpriteAtlasTextureSettings { anisoLevel = 8, filterMode = FilterMode.Trilinear, generateMipMaps = true, readable = true, sRGB = true });
                a.SetPlatformSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = 512 });
                a.SetPlatformSettings(null);
            }, out var ex);
            int d1 = EditorUtility.GetDirtyCount(a) - d0;
            bool inc = a.IsIncludeInBuild();
            var pack = a.GetPackingSettings();
            var tex = a.GetTextureSettings();
            var platAndroid = a.GetPlatformSettings("Android");
            var platNull = a.GetPlatformSettings(null);
            int d2 = EditorUtility.GetDirtyCount(a) - d0;
#pragma warning restore CS0618
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|d1={d1}|d2={d2}|inc={inc}|pack=({DescribePacking(pack)})|tex=({DescribeTexture(tex)})|platAndroid=({DescribePlatform(platAndroid)})|platNull=({DescribePlatform(platNull)})";
        });

        // ---------------------------------------------------------------- 5. Save contracts & exact YAML
        Check("save.nullAsset", () =>
        {
            string target = "Assets/Atlases/NullSave.spriteatlasv2";
            var logs = CaptureLogs(() => SpriteAtlasAsset.Save(null, target), out var ex);
            string paramName = ex is ArgumentException ae ? ae.ParamName : "<none>";
            return $"ex={ex?.GetType().Name ?? "none"}|param={paramName}|exists={File.Exists(target)}|logs={logs}";
        });

        Check("save.destroyedAsset", () =>
        {
            string target = "Assets/Atlases/DestroyedSave.spriteatlasv2";
            var destroyed = new SpriteAtlasAsset();
            Object.DestroyImmediate(destroyed);
            var logs = CaptureLogs(() => SpriteAtlasAsset.Save(destroyed, target), out var ex);
            string paramName = ex is ArgumentException ae ? ae.ParamName : "<none>";
            return $"ex={ex?.GetType().Name ?? "none"}|param={paramName}|exists={File.Exists(target)}|logs={logs}";
        });

        Check("save.emptyNew", () =>
        {
            string target = "Assets/Atlases/SavedEmpty.spriteatlasv2";
            var a = new SpriteAtlasAsset();
            int dBefore = EditorUtility.GetDirtyCount(a);
            bool dirtyBefore = EditorUtility.IsDirty(a);
            var logs = CaptureLogs(() => SpriteAtlasAsset.Save(a, target), out var ex);
            int dAfter = EditorUtility.GetDirtyCount(a);
            bool dirtyAfter = EditorUtility.IsDirty(a);
            SaveCopy(target, "SavedEmpty.spriteatlasv2");
            bool fileExists = File.Exists(target);
            bool metaExists = File.Exists(target + ".meta");
            string guid = AssetDatabase.AssetPathToGUID(target);
            var loadedAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(target);
            var loadedAsset = SpriteAtlasAsset.Load(target);
            string yaml = fileExists ? File.ReadAllText(target).Replace("\r\n", "\\r\\n").Replace("\n", "\\n") : "<missing>";
            return $"ex={ex?.GetType().Name ?? "none"}|logs={logs}|dirtyBefore={dirtyBefore}|dirtyAfter={dirtyAfter}|dDelta={dAfter - dBefore}|file={fileExists}|meta={metaExists}|guid={guid}|atlas={Describe(loadedAtlas)}|reloaded={loadedAsset != null}|reloadedName={loadedAsset?.name}|reloadedIsVar={loadedAsset?.isVariant}|yaml={yaml}";
        });

        Check("save.populatedNewAndImport", () =>
        {
            string target = "Assets/Atlases/SavedNew.spriteatlasv2";
            var a = new SpriteAtlasAsset { name = "CustomName" };
            a.Add(new Object[] { singleTex, singleSprite, folder, sheetSprite });
            a.Remove(new Object[] { singleTex });
            var logsSave = CaptureLogs(() => SpriteAtlasAsset.Save(a, target), out var exSave);
            SaveCopy(target, "SavedNew.spriteatlasv2");
            string yaml = File.Exists(target) ? File.ReadAllText(target).Replace("\r\n", "\\r\\n").Replace("\n", "\\n") : "<missing>";
            bool metaBeforeImport = File.Exists(target + ".meta");

            AssetDatabase.ImportAsset(target);
            bool metaAfterImport = File.Exists(target + ".meta");
            SaveCopy(target + ".meta", "SavedNew.spriteatlasv2.meta");
            string rawMeta = metaAfterImport ? File.ReadAllText(target + ".meta") : "";
            bool metaGuidValid = metaAfterImport && Regex.IsMatch(rawMeta, @"(?m)^guid: [0-9a-f]{32}$");
            string metaText = metaAfterImport ? NormalizeMeta(rawMeta).Replace("\r\n", "\\r\\n").Replace("\n", "\\n") : "<missing>";
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(target);
            string packables = atlas != null ? string.Join("|", atlas.GetPackables().Select(Describe)) : "<null>";
            var importer = AssetImporter.GetAtPath(target) as SpriteAtlasImporter;
            return $"exSave={exSave?.GetType().Name ?? "none"}|logsSave={logsSave}|metaBefore={metaBeforeImport}|metaAfter={metaAfterImport}|metaGuidValid={metaGuidValid}|atlas={Describe(atlas)}|isVar={atlas?.isVariant}|packables=({packables})|hasImporter={importer != null}|yaml={yaml}|meta={metaText}";
        });

        Check("save.withScriptablePacker", () =>
        {
            string target = "Assets/Atlases/SavedPacker.spriteatlasv2";
            var a = new SpriteAtlasAsset();
            a.SetScriptablePacker(packerAsset);
            var logsSave = CaptureLogs(() => SpriteAtlasAsset.Save(a, target), out var exSave);
            SaveCopy(target, "SavedPacker.spriteatlasv2");
            string yaml = File.Exists(target) ? File.ReadAllText(target).Replace("\r\n", "\\r\\n").Replace("\n", "\\n") : "<missing>";
            var reloaded = SpriteAtlasAsset.Load(target);
            return $"exSave={exSave?.GetType().Name ?? "none"}|logsSave={logsSave}|reloaded={reloaded != null}|yaml={yaml}";
        });

        Check("save.variantNewAndImport", () =>
        {
            string target = "Assets/Atlases/SavedVariant.spriteatlasv2";
            var a = new SpriteAtlasAsset();
            a.SetIsVariant(true);
            a.SetMasterAtlas(masterAtlas);
            var logsSave = CaptureLogs(() => SpriteAtlasAsset.Save(a, target), out var exSave);
            SaveCopy(target, "SavedVariant.spriteatlasv2");
            string yaml = File.Exists(target) ? File.ReadAllText(target).Replace("\r\n", "\\r\\n").Replace("\n", "\\n") : "<missing>";

            AssetDatabase.ImportAsset(target);
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(target);
            return $"exSave={exSave?.GetType().Name ?? "none"}|logsSave={logsSave}|atlas={Describe(atlas)}|isVar={atlas?.isVariant}|master={Describe(atlas?.GetMasterAtlas())}|yaml={yaml}";
        });

        Check("save.overwriteExisting", () =>
        {
            string target = "Assets/Atlases/Master.spriteatlasv2";
            string origMeta = File.ReadAllText(target + ".meta");
            var atlasBefore = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(target);
            string packBefore = string.Join("|", atlasBefore.GetPackables().Select(Describe));

            var loaded = SpriteAtlasAsset.Load(target);
            loaded.Add(new Object[] { sheetTex });
            loaded.Remove(new Object[] { singleTex });
            SpriteAtlasAsset.Save(loaded, target);
            SaveCopy(target, "Master.afterSave.spriteatlasv2");

            bool metaUnchangedAfterSave = File.ReadAllText(target + ".meta") == origMeta;
            string packRightAfterSave = string.Join("|", AssetDatabase.LoadAssetAtPath<SpriteAtlas>(target).GetPackables().Select(Describe));
            string yaml = File.ReadAllText(target).Replace("\r\n", "\\r\\n").Replace("\n", "\\n");

            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
            bool metaUnchangedAfterImport = File.ReadAllText(target + ".meta") == origMeta;
            string packAfterImport = string.Join("|", AssetDatabase.LoadAssetAtPath<SpriteAtlas>(target).GetPackables().Select(Describe));
            return $"packBefore=({packBefore})|metaUnchangedAfterSave={metaUnchangedAfterSave}|packRightAfterSave=({packRightAfterSave})|metaUnchangedAfterImport={metaUnchangedAfterImport}|packAfterImport=({packAfterImport})|yaml={yaml}";
        });

        Check("save.nestedDir", () =>
        {
            string target = "Assets/Atlases/NestedSub/Nested.spriteatlasv2";
            var a = new SpriteAtlasAsset();
            int dBefore = EditorUtility.GetDirtyCount(a);
            var entries = new List<(LogType type, string msg)>();
            Application.LogCallback handler = (condition, stackTrace, type) => entries.Add((type, condition));
            Application.logMessageReceived += handler;
            Exception ex = null;
            try { SpriteAtlasAsset.Save(a, target); }
            catch (Exception e) { ex = e; }
            finally { Application.logMessageReceived -= handler; }
            bool exists = File.Exists(target);
            bool dirExists = Directory.Exists("Assets/Atlases/NestedSub");
            bool dirtyAfter = EditorUtility.IsDirty(a);
            int dDelta = EditorUtility.GetDirtyCount(a) - dBefore;
            bool hasError = entries.Any(e => e.type == LogType.Error);
            bool mentionsTarget = entries.Any(e => e.type == LogType.Error && e.msg.Contains(target));
            return $"ex={ex?.GetType().Name ?? "none"}|exists={exists}|dirExists={dirExists}|dirtyAfter={dirtyAfter}|dDelta={dDelta}|hasError={hasError}|mentionsTarget={mentionsTarget}";
        });

        Check("save.nullPath", () =>
        {
            var a = new SpriteAtlasAsset();
            var entries = new List<(LogType type, string msg)>();
            Application.LogCallback handler = (condition, stackTrace, type) => entries.Add((type, condition));
            Application.logMessageReceived += handler;
            Exception ex = null;
            try { SpriteAtlasAsset.Save(a, null); }
            catch (Exception e) { ex = e; }
            finally { Application.logMessageReceived -= handler; }
            return $"ex={ex?.GetType().Name ?? "none"}|dirtyAfter={EditorUtility.IsDirty(a)}|hasError={entries.Any(e => e.type == LogType.Error)}";
        });

        Check("save.emptyPath", () =>
        {
            var a = new SpriteAtlasAsset();
            var entries = new List<(LogType type, string msg)>();
            Application.LogCallback handler = (condition, stackTrace, type) => entries.Add((type, condition));
            Application.logMessageReceived += handler;
            Exception ex = null;
            try { SpriteAtlasAsset.Save(a, ""); }
            catch (Exception e) { ex = e; }
            finally { Application.logMessageReceived -= handler; }
            return $"ex={ex?.GetType().Name ?? "none"}|dirtyAfter={EditorUtility.IsDirty(a)}|hasError={entries.Any(e => e.type == LogType.Error)}";
        });

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string DescribePlatform(TextureImporterPlatformSettings s) => s == null
        ? "<null>"
        : $"name={s.name ?? "<null>"}|overridden={s.overridden}|ignorePlatformSupport={s.ignorePlatformSupport}|maxTextureSize={s.maxTextureSize}|resizeAlgorithm={s.resizeAlgorithm}|format={(int)s.format}|textureCompression={s.textureCompression}|compressionQuality={s.compressionQuality}|crunchedCompression={s.crunchedCompression}|allowsAlphaSplitting={s.allowsAlphaSplitting}|androidETC2FallbackOverride={s.androidETC2FallbackOverride}";

    static string DescribePacking(SpriteAtlasPackingSettings s) =>
        $"blockOffset={s.blockOffset}|padding={s.padding}|enableRotation={s.enableRotation}|enableTightPacking={s.enableTightPacking}|enableAlphaDilation={s.enableAlphaDilation}";

    static string DescribeTexture(SpriteAtlasTextureSettings s) =>
        $"maxTextureSize={s.maxTextureSize}|anisoLevel={s.anisoLevel}|filterMode={s.filterMode}|generateMipMaps={s.generateMipMaps}|readable={s.readable}|sRGB={s.sRGB}";

    static string Describe(Object o) =>
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
