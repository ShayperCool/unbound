using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;

namespace UnityEditor
{
    public partial class AssetImporter
    {
        /// <summary>Writes the settings into the asset's <c>.meta</c> and imports the asset again
        /// with them (preprocessors run again; the asset loads anew).</summary>
        public void SaveAndReimport()
        {
            CheckImporterState("SaveAndReimport");
            using var write = SessionImports.Write();
            ImporterMeta.Write(this);
            ImporterMeta.Reimport(this);
            // Reimported (and reported so) whether or not its settings changed.
            SessionImports.Imported(assetPath);
        }

        public void SetAssetBundleNameAndVariant(string assetBundleName, string assetBundleVariant)
        {
            CheckImporterState("SetAssetBundleNameAndVariant");
            SetBundle(assetBundleName, assetBundleVariant);
        }
    }

    public sealed partial class AssetDatabase
    {
        /// <summary>Writes the importer's changed settings into the asset's <c>.meta</c>; whether
        /// it wrote.</summary>
        public static bool WriteImportSettingsIfDirty(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            return importer != null && ImporterMeta.Write(importer);
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>
    /// An importer's settings saved into its asset's <c>.meta</c> as the Editor saves them
    /// (matches the Unity 6000.0.71f1 Editor, tests/projects/projectwrites 08.importer and
    /// 10.preprocessor, tests/projects/assetpostprocessors meta.*, tests/projects/importermeta,
    /// tests/projects/scenesave label-metas): the file's text with each changed setting's value
    /// replaced, the common <c>userData</c> and AssetBundle fields (the bundle names lowercased),
    /// and for a texture, audio or model importer the whole section then in the release's layout
    /// (<see cref="ImporterLayout"/>: the keys measured missing at their defaults), a texture's
    /// platform entries as its importer holds them (<see cref="UnityEditor.TextureImporter"/>,
    /// tests/projects/textureplatforms); a file whose settings did not change is not written.
    /// Saved by SaveAndReimport, WriteImportSettingsIfDirty, and by an
    /// import whose preprocessors changed the settings.
    /// </summary>
    internal static class ImporterMeta
    {
        internal static bool Write(AssetImporter importer)
        {
            try { return WriteSettings(importer); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"Unbound: writing the .meta of {importer.assetPath}: {e.Message}");
                return false;
            }
        }

        static bool WriteSettings(AssetImporter importer)
        {
            if (importer?.m_Settings == null || string.IsNullOrEmpty(importer.m_SettingsType)) return false;
            var path = importer.assetPath;
            if (string.IsNullOrEmpty(path) || !AssetDatabase.Editable(path)) return false;
            var meta = AssetDatabase.FileOf(path) + ".meta";
            if (!File.Exists(meta)) return false;
            var text = File.ReadAllText(meta);
            // A texture's or a sound's .meta holding its GUID alone is written with the oldest
            // settings its importer reads there (tests/projects/guidonlymetas save.*), the change
            // patched in; the other importers' saves of such a file are not measured (not written).
            if ((importer.m_SettingsType == "TextureImporter" || importer.m_SettingsType == "AudioImporter")
                && AssetPaths.HoldsGuidAlone(text) && AssetDatabase.LabelMetaSection(path) is { Length: > 0 } added)
                text = AssetPaths.WithImporterSection(text, added);
            var root = UnityYamlText.ParseTopLevel(text);
            var section = root.Get(importer.m_SettingsType);
            if (section == null || section.Kind != YKind.Map) return false;
            // (The importer holds the AssetBundle names in lower case already.)
            var common = (importer.userData, importer.assetBundleName, importer.assetBundleVariant);
            if (importer.UsesDirtyFlagForWrite)
            {
                bool wasDirty = EditorUtility.IsDirty(importer) || importer.m_SettingsChanged;
                SetCommonScalars(section, common);
                // An importer written for any change writes its own settings whole.
                if (wasDirty || UnityYamlText.WriteTopLevel(root) != text.Replace("\r\n", "\n"))
                    importer.WriteCustomMeta(section);
                var customWritten = UnityYamlText.WriteTopLevel(root);
                bool textChanged = customWritten != text.Replace("\r\n", "\n");
                if (!textChanged && !wasDirty) return false;
                if (textChanged) { File.WriteAllText(meta, customWritten); UnityEditor.ProjectImports.Wrote(meta); }
                importer.ClearModifiedState();
                return textChanged;
            }
            var unwritten = new List<string>();
            bool texture = importer.m_SettingsType == "TextureImporter";
            var layout = texture ? ImporterLayout.ForTexture(TextureTemplate()) : ImporterLayout.Of(importer.m_SettingsType);
            var written = Rewritten(text, importer.m_SettingsType, importer.m_Settings, importer.SavedSettings(), common, layout, unwritten,
                out var older, out bool laidOut);
            importer.m_ImportedSettings = MetaSettings.ToJson(importer.m_Settings);
            if (older.Count > 0)
                Debug.LogWarning($"Unbound saves the {importer.m_SettingsType} settings of '{path}' in the layout the file has ({string.Join(", ", older)}): "
                    + "the Editor writes them in the release's layout after converting such settings, which is not measured");
            if (unwritten.Count > 0)
                Debug.LogWarning($"Unbound does not write these {importer.m_SettingsType} settings of '{path}' into its .meta yet (their place in it is not measured): {string.Join(", ", unwritten)}");
            if (written == null) return false;
            // The importer then holds the settings as written (its getters and the import read
            // them, a later save compares with them).
            if (laidOut && UnityYamlText.ParseTopLevel(written).Get(importer.m_SettingsType) is { } savedSection)
            {
                importer.m_Settings = savedSection.ToNode();
                importer.m_ImportedSettings = MetaSettings.ToJson(importer.m_Settings);
            }
            File.WriteAllText(meta, written);
            UnityEditor.ProjectImports.Wrote(meta);
            importer.SettingsWritten();
            return true;
        }

        /// <summary>The <c>.meta</c> text with the importer's settings patched into its section
        /// (<paramref name="type"/>), or null when nothing is to be written; the settings whose
        /// place is not known go to <paramref name="unwritten"/>. <paramref name="textureTemplate"/>:
        /// the texture section at its defaults with its platforms ("" but for a texture of a
        /// measured release); <paramref name="saved"/>: the settings without what the importer
        /// added itself (<see cref="AssetImporter.SavedSettings"/>).</summary>
        internal static string Rewritten(string text, string type, YNode settings, YNode saved, (string UserData, string Bundle, string Variant) common,
            string textureTemplate, List<string> unwritten) =>
            Rewritten(text, type, settings, saved, common, type == "TextureImporter" ? ImporterLayout.ForTexture(textureTemplate) : null,
                unwritten, out _, out _);

        /// <summary>The same with the importer's <paramref name="layout"/> (null where it is not
        /// measured); in <paramref name="older"/> what kept the file's layout, whether the section
        /// was laid out.</summary>
        internal static string Rewritten(string text, string type, YNode settings, YNode saved, (string UserData, string Bundle, string Variant) common,
            ImporterLayout layout, List<string> unwritten, out List<string> older, out bool laidOut)
        {
            older = new List<string>();
            laidOut = false;
            // What the importer added itself on loading its settings (a texture's platforms) is no
            // change: without one the file is not written (tests/projects/importermeta same.*).
            if (saved != null && !ReferenceEquals(saved, settings))
            {
                var unchanged = new List<string>();
                if (Rewritten(text, type, saved, null, common, layout, unchanged, out older, out _) == null)
                {
                    unwritten.AddRange(unchanged);
                    return null;
                }
                older = new List<string>();
            }
            var root = UnityYamlText.ParseTopLevel(text);
            var section = root.Get(type);
            if (section == null || section.Kind != YKind.Map) return null;
            // An oldest-layout sprite section whose only change is its first import's (the measured
            // slice; what the importer added itself on loading, the platforms, is no change) keeps
            // the file's platform entries, after which its layout adds the others
            // (ImporterLayout.Arrange); any other change of such a section keeps its layout.
            bool oldest = type == "TextureImporter" && layout != null && ImporterLayout.IsOldestSprite(section)
                && ImporterLayout.OnlyFirstImportChanges(section, saved ?? settings);
            if (type == "TextureImporter" && !oldest) MatchPlatforms(section, settings);
            Patch(section, settings, "", unwritten);
            SetCommonScalars(section, common);
            var original = text.Replace("\r\n", "\n");
            var written = UnityYamlText.WriteTopLevel(root);
            // Settings that did not change write nothing (tests/projects/projectwrites 06.crlf
            // unchanged), and neither do settings the file lacks where it is not laid out.
            if (written == original && !(layout != null && unwritten.Count > 0)) return null;
            if (layout != null)
            {
                // A save writes the importer's settings whole in the release's layout
                // (tests/projects/importermeta, tests/projects/scenesave label-metas); a file of a
                // structure that is not measured keeps its layout (ImporterMeta.LayOut).
                var missing = new List<string>(unwritten);
                older = LayOut(root, type, layout, settings, s => SetCommonScalars(s, common), unwritten, oldest);
                if (older.Count > 0)
                {
                    if (written == original) return null;
                }
                else
                {
                    laidOut = true;
                    if (written == original)
                    {
                        // A missing setting given the value its default reads is no change (unmeasured).
                        var plain = UnityYamlText.ParseTopLevel(text);
                        LayOut(plain, type, layout, plain.Get(type).ToNode(), s => SetCommonScalars(s, common), new List<string>());
                        if (UnityYamlText.WriteTopLevel(root) == UnityYamlText.WriteTopLevel(plain))
                        {
                            // Still reported: whether the Editor writes the section then is not known.
                            foreach (var key in missing)
                                if (!unwritten.Contains(key)) unwritten.Add(key + " (given the value its default reads)");
                            laidOut = false;
                            return null;
                        }
                    }
                }
            }
            written = UnityYamlText.WriteTopLevel(root);
            return written == original ? null : written;
        }

        /// <summary>The asset imported again with the importer's settings: its loaded objects
        /// dropped, the import given the settings, the preprocessors run again on the next load.</summary>
        internal static void Reimport(AssetImporter importer)
        {
            var path = importer.assetPath;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;
            if (importer is UnityEditor.U2D.SpriteAtlasImporter)
            {
                UnityEditor.U2D.SpriteAtlasImporter.ReimportAsset(path, guid);
                AssetPostprocessing.ForgetPreprocessed(path);
                return;
            }
            try
            {
                Native.Call("import-settings", "{\"guid\":" + System.Text.Json.JsonSerializer.Serialize(guid) + ",\"importer\":"
                    + System.Text.Json.JsonSerializer.Serialize(importer.m_SettingsType) + ",\"settings\":" + MetaSettings.ToJson(importer.m_Settings) + "}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: {path} imports with its .meta's settings: {e.Message}");
            }
            AssetDatabaseRuntime.Invalidate(guid);
            AssetPostprocessing.ForgetPreprocessed(path);
            importer.Reimported();
        }

        static string[] s_TextureImportPlatforms;
        static bool? s_TextureFilesMeasured;

        /// <summary>For tests: the platforms <see cref="TextureImportPlatforms"/> answers.</summary>
        internal static string[] s_TextureImportPlatformsForTests;

        /// <summary>The platforms a texture imports for besides the default: Standalone, then the
        /// installation's platform modules (matches the Unity 6000.0.71f1 Editor, whose importer adds
        /// their missing entries, tests/projects/textureplatforms load.*, scenesave label-metas
        /// kept.*); null where that is not measured (another release family, no native side).</summary>
        internal static IReadOnlyList<string> TextureImportPlatforms
        {
            get
            {
                if (s_TextureImportPlatformsForTests != null) return s_TextureImportPlatformsForTests;
                if (!Native.IsReady) return null;
                s_TextureFilesMeasured ??= TextureTemplate().Length > 0;
                if (s_TextureFilesMeasured != true) return null;
                if (s_TextureImportPlatforms == null)
                {
                    using var modules = System.Text.Json.JsonDocument.Parse(Native.Call("texture-platforms", "{}"));
                    var names = new List<string> { "Standalone" };
                    foreach (var module in modules.RootElement.EnumerateArray())
                        if (module.GetProperty("name").GetString() is { } name && !names.Contains(name)) names.Add(name);
                    s_TextureImportPlatforms = names.ToArray();
                }
                return s_TextureImportPlatforms;
            }
        }

        /// <summary>The section's platform entries made those of <paramref name="settings"/>, in
        /// their order by <c>buildTarget</c>: an entry the file has keeps its text (the values are
        /// patched in after), one it lacks is added as the settings hold it, one the settings lack
        /// is removed (tests/projects/textureplatforms set.*, clear.*, load.*).</summary>
        static void MatchPlatforms(YText section, YNode settings)
        {
            if (settings?.Get("platformSettings") is not { Kind: YKind.Seq } wanted || section.Get("platformSettings") is not { Kind: YKind.Seq } list) return;
            var remaining = new List<YText>(list.Items);
            var items = new List<YText>(wanted.Items.Length);
            foreach (var entry in wanted.Items)
            {
                var name = entry.Kind == YKind.Map ? entry.Get("buildTarget")?.AsString() : null;
                int at = name == null ? -1 : remaining.FindIndex(item => item.Kind == YKind.Map && item.Get("buildTarget")?.Text == name);
                if (at >= 0)
                {
                    items.Add(remaining[at]);
                    remaining.RemoveAt(at);
                }
                else items.Add(FromNode(entry));
            }
            list.Items.Clear();
            list.Items.AddRange(items);
            list.Flow = items.Count == 0;
        }

        /// <summary>The text of a block map or sequence of scalars as the managed reader holds it.</summary>
        static YText FromNode(YNode node)
        {
            switch (node?.Kind ?? YKind.Null)
            {
                case YKind.Map:
                    var map = YText.Map();
                    for (int i = 0; i < node.Keys.Length; i++) map.Add(node.Keys[i], FromNode(node.Values[i]));
                    return map;
                case YKind.Seq:
                    var seq = YText.Seq(flow: node.Items.Length == 0);
                    foreach (var item in node.Items) seq.Items.Add(FromNode(item));
                    return seq;
                case YKind.Scalar:
                    return YText.Scalar(Plain(node.Scalar));
                default:
                    return YText.Scalar("");
            }
        }

        static void SetCommonScalars(YText section, (string UserData, string Bundle, string Variant) common)
        {
            SetScalar(section, "userData", common.UserData);
            SetScalar(section, "assetBundleName", common.Bundle);
            SetScalar(section, "assetBundleVariant", common.Variant);
        }

        static string s_TextureTemplate;

        static string TextureTemplate() =>
            s_TextureTemplate ??= System.Text.Json.JsonDocument.Parse(Native.Call("texture-meta-template", "{}")).RootElement.GetProperty("section").GetString() ?? "";

        /// <summary>Each scalar of <paramref name="settings"/> (the importer's tree) into the same
        /// place of <paramref name="text"/> where it differs; entries the file does not have stay
        /// out (their place in the Editor's layout is not known).</summary>
        internal static void Patch(YText text, YNode settings, string at, List<string> unwritten)
        {
            if (text == null || settings == null) return;
            switch (settings.Kind)
            {
                case YKind.Map when text.Kind == YKind.Map:
                    for (int i = 0; i < settings.Keys.Length; i++)
                    {
                        var key = settings.Keys[i];
                        var child = text.Get(key);
                        if (child == null)
                        {
                            unwritten.Add(at + key);
                            continue;
                        }
                        if (child.Kind == YKind.Scalar && settings.Values[i].Kind == YKind.Scalar)
                        {
                            if (child.Text != settings.Values[i].Scalar) Replace(text, key, settings.Values[i].Scalar);
                        }
                        else Patch(child, settings.Values[i], at + key + ".", unwritten);
                    }
                    if (text.Flow) text.Raw = null;
                    break;
                case YKind.Seq when text.Kind == YKind.Seq:
                    for (int i = 0; i < settings.Items.Length; i++)
                    {
                        if (i >= text.Items.Count)
                        {
                            unwritten.Add(at + "[" + i + "]");
                            continue;
                        }
                        if (text.Items[i].Kind == YKind.Scalar && settings.Items[i].Kind == YKind.Scalar)
                        {
                            if (text.Items[i].Text != settings.Items[i].Scalar) text.Items[i] = YText.Scalar(Plain(settings.Items[i].Scalar));
                        }
                        else Patch(text.Items[i], settings.Items[i], at + "[" + i + "].", unwritten);
                    }
                    break;
            }
        }

        static void Replace(YText map, string key, string value)
        {
            map.Set(key, YText.Scalar(Plain(value)));
            if (map.Flow) map.Raw = null;
        }

        /// <summary>The importer's section of <paramref name="root"/> (its settings already patched
        /// in) replaced by the same settings in the release's layout, with the settings the file
        /// lacked placed and <paramref name="unwritten"/> left with those of neither; unless the
        /// file's structure is one the layout is not measured for (a serialized version that is
        /// neither the release's nor a measured older one, or none, a missing key other than the
        /// measured ones, a value of another kind, no platform list): the section then stays as it
        /// is, and what was found is returned.</summary>
        /// <param name="oldest">The section is an oldest-layout sprite's written for its first
        /// import (<see cref="ImporterLayout.IsOldestSprite"/>, <see cref="ImporterLayout.OnlyFirstImportChanges"/>).</param>
        internal static List<string> LayOut(YText root, string importerType, ImporterLayout layout, YNode settings, Action<YText> common, List<string> unwritten,
            bool oldest = false)
        {
            var older = new List<string>();
            var arranged = layout.Arrange(root.Get(importerType).Clone(), older, oldest);
            var placed = new List<string>();
            // The oldest layout's settings are written as the Editor converts them.
            Patch(arranged, oldest ? ImporterLayout.ReadAsOldest(importerType, settings) : settings, "", placed);
            common(arranged);
            older.AddRange(layout.Normalize(arranged));
            if (older.Count > 0) return older;
            root.Set(importerType, arranged);
            // Settings of neither the file nor the release's layout (the file's own others are left out).
            unwritten.RemoveAll(key => !placed.Contains(key));
            return older;
        }

        static void SetScalar(YText map, string key, string value)
        {
            var current = map.Get(key);
            if (current == null || current.Kind != YKind.Scalar || current.Text == (value ?? "")) return;
            map.Set(key, YText.Scalar(Plain(value ?? "")));
        }

        /// <summary>A value as a plain YAML scalar, quoted where the plain form would read as
        /// something else.</summary>
        static string Plain(string value) => UnityYamlWriter.Scalar(value);
    }

    /// <summary>
    /// The layout of an importer's settings the Editor writes whenever it saves them: its keys in
    /// the release's order, a key the file lacks at its default (for the keys measured missing,
    /// <see cref="s_MeasuredMissing"/>), a key the release does not have left out, each per-platform texture entry laid out alike, the release's serialized versions
    /// and platform names (matches the Unity 6000.0.71f1 Editor, tests/projects/importermeta: texture,
    /// audio and model importers). Values of the file's keys are kept as read, sequences other than
    /// the texture's platform entries (sprites, clips, name tables) and the entries of empty
    /// collections (external objects) as the file has them. The layouts are those of a new asset's
    /// .meta (crates/unbound-assets new_meta), for the measured family; a file of an older serialized
    /// version than measured is not laid out (<see cref="ImporterMeta"/>).
    /// </summary>
    internal sealed class ImporterLayout
    {
        /// <summary>Platform names of older releases the Editor writes by their current name
        /// (tests/projects/importermeta change.SwitchName).</summary>
        static readonly Dictionary<string, string> s_PlatformNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Nintendo Switch"] = "Switch",
        };

        /// <summary>Older serialized versions whose settings the Editor writes in the release's
        /// layout unchanged, by importer and version (tests/projects/importermeta change.Version12).</summary>
        static readonly HashSet<(string, long)> s_MeasuredVersions = new HashSet<(string, long)> { ("TextureImporter", 12) };

        /// <summary>Keys a file of the release's serialized version may lack that the Editor then
        /// writes at the layout's default, by importer and path (a platform entry's keys as
        /// "platformSettings[]."): those the release's older patches did not write and those
        /// measured (tests/projects/importermeta change.Missing, change.GuiNoWrap, change.DefaultNoWrap,
        /// change.Assets/Audio/Missing.wav, change.Assets/Models/Missing.obj; tests/projects/scenesave
        /// label-metas kept.*, missingSetting.*). A file lacking
        /// another key, or of an older version lacking any, keeps its layout: what the Editor and
        /// the import read for it is not measured.</summary>
        static readonly HashSet<(string, string)> s_MeasuredMissing = new HashSet<(string, string)>
        {
            ("TextureImporter", "mipmaps.mipMapsPreserveCoverage"),
            ("TextureImporter", "flipbookRows"),
            ("TextureImporter", "flipbookColumns"),
            ("TextureImporter", "swizzle"),
            ("TextureImporter", "streamingMipmaps"),
            ("TextureImporter", "ignorePngGamma"),
            ("TextureImporter", "platformSettings[].forceMaximumCompressionQuality_BC6H_BC7"),
            ("TextureImporter", "spriteSheet.spriteCustomMetadata"),
            ("TextureImporter", "textureSettings.wrapU"),
            ("TextureImporter", "textureSettings.wrapV"),
            ("TextureImporter", "textureSettings.wrapW"),
            ("AudioImporter", "loadInBackground"),
            ("AudioImporter", "defaultSettings.preloadAudioData"),
            ("ModelImporter", "animations.removeConstantScaleCurves"),
            ("ModelImporter", "animations.importConstraints"),
        };

        static readonly System.Text.RegularExpressions.Regex s_EntryIndex = new System.Text.RegularExpressions.Regex(@"\[\d+\]");

        static readonly Dictionary<string, ImporterLayout> s_Layouts = new Dictionary<string, ImporterLayout>(StringComparer.Ordinal);

        readonly string m_Importer;
        readonly YText m_Section;
        readonly YText m_PlatformEntry;

        /// <param name="platformEntry">One per-platform entry of the layout; null: the first of the
        /// section's own platform list, if it has one.</param>
        internal ImporterLayout(string importer, string section, string platformEntry)
        {
            m_Importer = importer;
            m_Section = UnityYamlText.ParseTopLevel(section).Get(importer)
                ?? throw new FormatException($"the layout of {importer} has no {importer} section");
            var own = m_Section.Get("platformSettings");
            if (platformEntry != null)
                m_PlatformEntry = UnityYamlText.ParseTopLevel("platformSettings:\n" + platformEntry).Get("platformSettings")?.Items.FirstOrDefault()
                    ?? throw new FormatException($"the layout of {importer} has no platform entry");
            else m_PlatformEntry = own?.Kind == YKind.Seq ? own.Items.FirstOrDefault(entry => entry.Kind == YKind.Map) : null;
        }

        /// <summary>A texture's layout from its new-asset section with its platforms
        /// (<c>texture-meta-template</c>); null for "" (a release family whose files are not measured).</summary>
        internal static ImporterLayout ForTexture(string template) =>
            string.IsNullOrEmpty(template) ? null : new ImporterLayout("TextureImporter", template, null);

        /// <summary>The release's layout of the importer's settings; null for an importer whose
        /// layout is not measured, and for another release family.</summary>
        internal static ImporterLayout Of(string importer)
        {
            if (string.IsNullOrEmpty(importer)) return null;
            if (s_Layouts.TryGetValue(importer, out var known)) return known;
            using var reply = System.Text.Json.JsonDocument.Parse(Native.Call("importer-layout",
                System.Text.Json.JsonSerializer.Serialize(new { importer })));
            var section = reply.RootElement.GetProperty("section");
            var entry = reply.RootElement.GetProperty("platformEntry");
            var layout = section.ValueKind == System.Text.Json.JsonValueKind.String
                ? new ImporterLayout(importer, section.GetString(), entry.ValueKind == System.Text.Json.JsonValueKind.String ? entry.GetString() : null)
                : null;
            s_Layouts[importer] = layout;
            return layout;
        }

        /// <summary>The settings <paramref name="file"/> holds, in the release's layout; in
        /// <paramref name="unmeasured"/> what of the file's structure that is not measured for (a
        /// missing key other than the measured ones of a file of the release's version, among
        /// them a missing serialized version or platform list, a value of another kind than the
        /// release's), as "at: what".</summary>
        internal YText Arrange(YText file, List<string> unmeasured, bool oldestSprite = false)
        {
            bool current = file.Get("serializedVersion") is { Kind: YKind.Scalar } version && m_Section.Get("serializedVersion") is { } own
                && version.Text == own.Text;
            // A sprite's texture section without a serialized version (the oldest layout, a
            // hand-written .meta of a few keys): its missing keys at the layout's defaults, its
            // textureSettings without their own version as defaults, and a default platform entry
            // added after the file's (matches the Unity 6000.0.71f1 Editor, tests/projects/spritemeta
            // Bare, BareWrap, BarePpu, BareFull). Another such section keeps its layout (unmeasured).
            bool oldest = oldestSprite && m_Importer == "TextureImporter" && IsOldestSprite(file);
            var arranged = Arrange(file, m_Section, null, "", current, unmeasured, oldest);
            if (oldest && m_PlatformEntry != null && arranged.Get("platformSettings") is { Kind: YKind.Seq } platforms)
            {
                // After the file's entries: a default one, then each platform the import adds that
                // the file lacks, without compression.
                platforms.Items.Add(OldestEntry("DefaultTexturePlatform"));
                foreach (var name in ImporterMeta.TextureImportPlatforms ?? Array.Empty<string>())
                    if (!platforms.Items.Any(item => item.Kind == YKind.Map && item.Get("buildTarget")?.Text == name))
                        platforms.Items.Add(OldestEntry(name));
                platforms.Flow = false;
            }
            return arranged;
        }

        /// <summary>Whether a texture section is the measured slice of the oldest layout: no
        /// serialized version, a sprite (textureType 8), and textureSettings, if any, of their own
        /// version or of the wrap keys alone (spritemeta BareWrap; the older textureSettings' other
        /// keys are not measured).</summary>
        internal static bool IsOldestSprite(YText section)
        {
            if (section == null || section.Kind != YKind.Map || section.Get("serializedVersion") != null) return false;
            if (section.Get("textureType")?.Text != "8") return false;
            if (section.Get("textureSettings") is { Kind: YKind.Map } settings && settings.Get("serializedVersion") == null)
                foreach (var key in settings.Keys)
                    if (key is not ("wrapU" or "wrapV" or "wrapW")) return false;
            return true;
        }

        /// <summary>Whether the importer's <paramref name="settings"/> differ from an oldest-layout
        /// section's keys only by what a sprite's first import sets (its sprite ID and NPOT
        /// scaling, tests/projects/spritemeta Bare*): the change whose write is measured.</summary>
        internal static bool OnlyFirstImportChanges(YText section, YNode settings)
        {
            if (section == null || settings == null) return false;
            // The settings with the first import's keys put back as the file has them, patched into
            // the file as a save patches them: no text change, and nothing but those keys unwritten.
            var probe = MetaSettings.Clone(settings);
            foreach (var key in FirstImportKeys)
            {
                var dot = key.IndexOf('.');
                var own = dot < 0 ? section.Get(key) : section.Get(key.Substring(0, dot))?.Get(key.Substring(dot + 1));
                if (own != null && own.Kind == YKind.Scalar) MetaSettings.Set(probe, key, own.Text);
            }
            var copy = section.Clone();
            var unwritten = new List<string>();
            ImporterMeta.Patch(copy, probe, "", unwritten);
            if (UnityYamlText.WriteTopLevel(Wrap(copy)) != UnityYamlText.WriteTopLevel(Wrap(section))) return false;
            foreach (var path in unwritten)
            {
                if (System.Array.IndexOf(FirstImportKeys, path) >= 0) continue;
                // A sprite sheet the file lacks, holding only the sprite ID.
                if (path == "spriteSheet" && MetaSettings.Find(settings, "spriteSheet") is { Kind: YKind.Map } sheet
                    && sheet.Keys.All(k => k == "spriteID")) continue;
                return false;
            }
            return true;
        }

        /// <summary>What a sprite's first import sets: its sprite ID and NPOT scaling.</summary>
        static readonly string[] FirstImportKeys = { "spriteSheet.spriteID", "nPOTScale" };

        static YText Wrap(YText section)
        {
            var root = YText.Map();
            root.Add("TextureImporter", section);
            return root;
        }

        /// <summary>A platform entry the oldest texture layout gets: the release's entry for
        /// <paramref name="target"/> without compression.</summary>
        YText OldestEntry(string target)
        {
            var entry = m_PlatformEntry.Clone();
            entry.Set("buildTarget", YText.Scalar(target));
            if (entry.Get("textureCompression") != null) entry.Set("textureCompression", YText.Scalar("0"));
            return entry;
        }

        /// <summary>Keys the Editor writes with these values into a texture section of the oldest
        /// layout, whatever the file holds (the settings of textures older than the keys:
        /// tests/projects/spritemeta Bare*, BareFull's are 0 in its file).</summary>
        static readonly Dictionary<string, string> s_OldestDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["applyGammaDecoding"] = "1",
            ["cookieLightType"] = "1",
        };

        /// <summary>The settings an importer of an oldest-layout sprite section (<see cref="IsOldestSprite"/>)
        /// writes into it: its textureSettings without their own serialized version left out (the
        /// layout's defaults are written; tests/projects/spritemeta end.BareWrap), its platform
        /// entries left out (the file's are kept, the layout adds the others), the keys of
        /// <see cref="s_OldestDefaults"/> with those values. Other settings as they are.</summary>
        internal static YNode ReadAsOldest(string importer, YNode body)
        {
            if (importer != "TextureImporter" || body == null || body.Kind != YKind.Map || body.Get("serializedVersion") != null) return body;
            bool dropSettings = body.Get("textureSettings") is { Kind: YKind.Map } settings && settings.Get("serializedVersion") == null;
            var keys = new List<string>();
            var values = new List<YNode>();
            for (int i = 0; i < body.Keys.Length; i++)
            {
                // The platform entries are the file's, then those the layout adds (Arrange).
                if ((dropSettings && body.Keys[i] == "textureSettings") || body.Keys[i] == "platformSettings" || s_OldestDefaults.ContainsKey(body.Keys[i])) continue;
                keys.Add(body.Keys[i]);
                values.Add(body.Values[i]);
            }
            // The keys the oldest layout is read with (s_OldestDefaults).
            foreach (var (key, value) in s_OldestDefaults)
            {
                keys.Add(key);
                values.Add(new YNode { Kind = YKind.Scalar, Scalar = value });
            }
            return new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
        }

        /// <param name="current">Whether the file is of the release's serialized version.</param>
        /// <param name="oldest">Whether it is a texture section of no serialized version.</param>
        YText Arrange(YText file, YText layout, string key, string at, bool current, List<string> unmeasured, bool oldest = false)
        {
            if (oldest && key == "textureSettings" && file.Kind == YKind.Map && file.Get("serializedVersion") == null) return layout.Clone();
            // A map of its own serialized version inside an oldest section is laid out as any.
            if (oldest && key != null && file.Kind == YKind.Map && file.Get("serializedVersion") != null) oldest = false;
            if (key == "platformSettings" && file.Kind == YKind.Seq && m_PlatformEntry != null)
            {
                var entries = YText.Seq(flow: file.Flow);
                for (int k = 0; k < file.Items.Count; k++)
                {
                    var item = file.Items[k];
                    entries.Items.Add(item.Kind == YKind.Map ? Arrange(item, m_PlatformEntry, null, at + "[" + k + "]", current, unmeasured, oldest) : item);
                }
                return entries;
            }
            // An empty collection holds entries of either form (a map's entries are written as a
            // list of pairs, `externalObjects`; an empty one as {}): the file's are kept.
            if (layout.Kind is YKind.Map or YKind.Seq && layout.Keys.Count == 0 && layout.Items.Count == 0) return file;
            if (file.Kind != layout.Kind)
            {
                unmeasured.Add(at + ": a " + file.Kind + " where the release has a " + layout.Kind);
                return layout.Clone();
            }
            if (layout.Kind != YKind.Map || layout.Flow) return file;
            var map = YText.Map();
            for (int i = 0; i < layout.Keys.Count; i++)
            {
                var name = layout.Keys[i];
                var value = file.Get(name);
                var path = at.Length == 0 ? name : at + "." + name;
                if (value == null && !oldest && (!current || !s_MeasuredMissing.Contains((m_Importer, s_EntryIndex.Replace(path, "[]")))))
                    unmeasured.Add(path + ": missing");
                // The oldest layout's platform list is the file's (none: empty), before the default entry.
                if (value == null && oldest && name == "platformSettings" && m_PlatformEntry != null) value = YText.Seq(flow: true);
                if (oldest && at.Length == 0 && s_OldestDefaults.TryGetValue(name, out var oldestValue)) value = YText.Scalar(oldestValue);
                map.Add(name, value == null ? layout.Values[i].Clone() : Arrange(value, layout.Values[i], name, path, current, unmeasured, oldest));
            }
            return map;
        }

        /// <summary>The release's serialized versions and platform names in an arranged section;
        /// each version that is neither the release's nor a measured older one, as "at
        /// serializedVersion from -> to".</summary>
        internal List<string> Normalize(YText section)
        {
            var unmeasured = new List<string>();
            Normalize(section, m_Section, "", unmeasured);
            return unmeasured;
        }

        void Normalize(YText map, YText layout, string at, List<string> unmeasured)
        {
            if (map.Kind != YKind.Map || layout.Kind != YKind.Map) return;
            for (int i = 0; i < map.Keys.Count; i++)
            {
                var key = map.Keys[i];
                var value = map.Values[i];
                if (key == "serializedVersion" && layout.Get(key) is { Kind: YKind.Scalar } version && value.Kind == YKind.Scalar && value.Text != version.Text)
                {
                    long.TryParse(value.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var from);
                    if (!(at.Length == 0 && s_MeasuredVersions.Contains((m_Importer, from))))
                        unmeasured.Add($"{(at.Length == 0 ? "" : at + ".")}serializedVersion {value.Text} -> {version.Text}");
                    map.Values[i] = version.Clone();
                }
                else if (key == "buildTarget" && value.Kind == YKind.Scalar && s_PlatformNames.TryGetValue(value.Text, out var current))
                    map.Values[i] = YText.Scalar(current);
                else if (key == "platformSettings" && value.Kind == YKind.Seq && m_PlatformEntry != null)
                    for (int k = 0; k < value.Items.Count; k++) Normalize(value.Items[k], m_PlatformEntry, at + key + "[" + k + "]", unmeasured);
                else if (layout.Get(key) is { Kind: YKind.Map } child)
                    Normalize(value, child, (at.Length == 0 ? "" : at + ".") + key, unmeasured);
            }
        }
    }
}
