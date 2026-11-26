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
            // An oldest-layout sprite section of a measured shape is converted whole by any write
            // (ImporterMeta.LayOut); the file's own section is what that conversion reads.
            var fileSection = section.Clone();
            bool oldest = type == "TextureImporter" && layout != null && layout.ConvertsOldest(section);
            if (type == "TextureImporter") MatchPlatforms(section, settings);
            Patch(section, settings, "", unwritten);
            SetCommonScalars(section, common);
            // A common setting the file lacks is not patched in: said, unless the section is laid
            // out (the layout places it).
            foreach (var (key, value) in CommonScalars(common))
                if (!string.IsNullOrEmpty(value) && section.Get(key) == null) unwritten.Add(key);
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
                older = LayOut(root, type, layout, settings, s => SetCommonScalars(s, common), unwritten,
                    oldest ? new OldestSource(fileSection, saved ?? settings) : null);
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
                        // (With the file's own common settings: a given one it lacks is a change;
                        // an oldest section read as its importer reads it, with the entries it adds.)
                        var plainSettings = plain.Get(type).ToNode();
                        if (oldest) TextureImporter.AddMissingPlatforms(plainSettings, TextureImportPlatforms);
                        LayOut(plain, type, layout, plainSettings, _ => { }, new List<string>(),
                            oldest ? new OldestSource(plain.Get(type).Clone(), plain.Get(type).ToNode()) : null);
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
            foreach (var (key, value) in CommonScalars(common)) SetScalar(section, key, value);
        }

        /// <summary>The settings every importer's section ends with, by key.</summary>
        internal static (string Key, string Value)[] CommonScalars((string UserData, string Bundle, string Variant) common) =>
            new[] { ("userData", common.UserData), ("assetBundleName", common.Bundle), ("assetBundleVariant", common.Variant) };

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
        /// <param name="oldest">The file's section, an oldest-layout sprite's (<see cref="ImporterLayout.ConvertsOldest"/>),
        /// and the importer's settings without the entries it added itself: the section is then
        /// converted from the file's own as the Editor reads it, with the importer's changes.</param>
        internal static List<string> LayOut(YText root, string importerType, ImporterLayout layout, YNode settings, Action<YText> common, List<string> unwritten,
            OldestSource oldest = null)
        {
            var older = new List<string>();
            var arranged = layout.Arrange((oldest != null ? oldest.File : root.Get(importerType)).Clone(), older, oldest != null);
            var placed = new List<string>();
            // The oldest layout's settings are written as the Editor converts them.
            Patch(arranged, oldest != null ? ImporterLayout.ReadAsOldest(settings, oldest.File) : settings, "", placed);
            common(arranged);
            if (oldest != null) layout.OldestPlatforms(arranged, oldest.File, oldest.Saved, settings, older);
            older.AddRange(layout.Normalize(arranged));
            if (older.Count > 0) return older;
            root.Set(importerType, arranged);
            // Settings of neither the file nor the release's layout (the file's own others are left out).
            unwritten.RemoveAll(key => !placed.Contains(key));
            return older;
        }

        /// <summary>What an oldest-layout section's conversion reads: the file's section, and the
        /// importer's settings without the entries it added itself (<see cref="AssetImporter.SavedSettings"/>).</summary>
        internal sealed record OldestSource(YText File, YNode Saved);

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
            // hand-written .meta of a few keys) as the Editor reads it: its missing keys at the
            // layout's defaults, its textureSettings without their own version as defaults, the keys
            // the release lacks left out (matches the Unity 6000.0.71f1 Editor,
            // tests/projects/spritemeta Bare*, Save*); its platform list is the importer's
            // (OldestPlatforms). Another such section keeps its layout (unmeasured).
            bool oldest = oldestSprite && ConvertsOldest(file);
            return Arrange(file, m_Section, null, "", current, unmeasured, oldest);
        }

        /// <summary>Whether an oldest-layout texture section is of a shape whose conversion is
        /// measured (tests/projects/spritemeta Bare*, Save*): a sprite's (<see cref="IsOldestSprite"/>),
        /// of no older texture format than the release's default (another one changes more,
        /// BareLegacy's alpha usage: not implemented), each platform entry of a serialized version
        /// (one without reads other defaults, BarePlat: not implemented), and its other maps
        /// without the serialized version the release gives them only where they hold the
        /// release's defaults (whether their values are kept is not measured).</summary>
        internal bool ConvertsOldest(YText section)
        {
            if (m_Importer != "TextureImporter" || !IsOldestSprite(section)) return false;
            if (section.Get("textureFormat") is { } format && (format.Kind != YKind.Scalar || format.Text != m_Section.Get("textureFormat")?.Text)) return false;
            if (section.Get("platformSettings") is { Kind: YKind.Seq } list)
                foreach (var entry in list.Items)
                    if (entry.Kind != YKind.Map || entry.Get("serializedVersion") == null || entry.Get("buildTarget") == null) return false;
            for (int i = 0; i < section.Keys.Count; i++)
            {
                var key = section.Keys[i];
                if (key == "textureSettings" || section.Values[i] is not { Kind: YKind.Map } map || map.Get("serializedVersion") != null) continue;
                if (m_Section.Get(key) is not { Kind: YKind.Map } own || own.Get("serializedVersion") == null) continue;
                foreach (var inner in map.Keys)
                    if (own.Get(inner) is not { } given || TextOf(given) != TextOf(map.Get(inner))) return false;
            }
            return true;
        }

        static string TextOf(YText value)
        {
            var root = YText.Map();
            root.Add("v", value);
            return UnityYamlText.WriteTopLevel(root);
        }

        /// <summary>An oldest-layout section's platform list as the Editor's importer holds it and a
        /// save writes it (tests/projects/spritemeta Bare*, Save*): the file's entries (laid out,
        /// those the importer cleared left out, its values patched in), then a default entry and
        /// each platform the import adds that the file lacks, uncompressed with the file's maximum
        /// size and compression quality, unless the importer cleared it. An entry the importer
        /// set or changed that the file lacks is not measured: named in <paramref name="unmeasured"/>.</summary>
        internal void OldestPlatforms(YText arranged, YText file, YNode saved, YNode settings, List<string> unmeasured)
        {
            if (m_PlatformEntry == null || arranged.Get("platformSettings") is not { Kind: YKind.Seq } list) return;
            var own = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<YText>();
            var unplaced = new List<string>();
            foreach (var item in list.Items)
            {
                var name = item.Kind == YKind.Map ? item.Get("buildTarget")?.Text : null;
                if (name == null) continue;
                own.Add(name);
                if (FindEntry(saved, name) is not { } kept) continue;
                ImporterMeta.Patch(item, kept, "platformSettings[" + name + "].", unplaced);
                entries.Add(item);
            }
            unmeasured.AddRange(unplaced.Select(path => path + ": not in the release's entry"));
            bool listed = settings?.Get("platformSettings") is { Kind: YKind.Seq };
            void Add(string name)
            {
                if (own.Contains(name)) return;
                if (FindEntry(saved, name) != null) unmeasured.Add("platformSettings: " + name + " set or changed on an oldest-layout section");
                else if (name == TextureImporterPlatformSettings.DefaultPlatformName || !listed || FindEntry(settings, name) != null) entries.Add(OldestEntry(name, file));
            }
            // The default entry is added even when the file has one (BareFull).
            if (FindEntry(saved, TextureImporterPlatformSettings.DefaultPlatformName) != null && !own.Contains(TextureImporterPlatformSettings.DefaultPlatformName))
                unmeasured.Add("platformSettings: " + TextureImporterPlatformSettings.DefaultPlatformName + " set or changed on an oldest-layout section");
            else entries.Add(OldestEntry(TextureImporterPlatformSettings.DefaultPlatformName, file));
            foreach (var name in ImporterMeta.TextureImportPlatforms ?? Array.Empty<string>()) Add(name);
            if (saved?.Get("platformSettings") is { Kind: YKind.Seq } given)
                foreach (var entry in given.Items)
                    if (entry.Kind == YKind.Map && entry.Get("buildTarget")?.AsString() is { } name && !own.Contains(name)
                        && name != TextureImporterPlatformSettings.DefaultPlatformName && !(ImporterMeta.TextureImportPlatforms?.Contains(name) ?? false))
                        unmeasured.Add("platformSettings: " + name + " set or changed on an oldest-layout section");
            list.Items.Clear();
            list.Items.AddRange(entries);
            list.Flow = entries.Count == 0;
        }

        static YNode FindEntry(YNode settings, string name)
        {
            if (settings?.Get("platformSettings") is not { Kind: YKind.Seq } list) return null;
            foreach (var item in list.Items)
                if (item.Kind == YKind.Map && item.Get("buildTarget")?.AsString() == name) return item;
            return null;
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

        /// <summary>A platform entry an oldest-layout section's importer adds: the release's entry
        /// for <paramref name="target"/> without compression, of the file's maximum size and
        /// compression quality (tests/projects/spritemeta BareSize).</summary>
        YText OldestEntry(string target, YText file)
        {
            var entry = m_PlatformEntry.Clone();
            entry.Set("buildTarget", YText.Scalar(target));
            if (entry.Get("textureCompression") != null) entry.Set("textureCompression", YText.Scalar("0"));
            foreach (var key in s_OldestEntryKeys)
                if (entry.Get(key) != null && file.Get(key) is { Kind: YKind.Scalar } value) entry.Set(key, value.Clone());
            return entry;
        }

        /// <summary>The section's settings an oldest-layout section's added platform entries take.</summary>
        static readonly string[] s_OldestEntryKeys = { "maxTextureSize", "compressionQuality" };

        /// <summary>Keys the Editor writes with these values into a texture section of the oldest
        /// layout, whatever the file holds (the settings of textures older than the keys:
        /// tests/projects/spritemeta Bare*, BareFull's are 0 in its file).</summary>
        static readonly Dictionary<string, string> s_OldestDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["applyGammaDecoding"] = "1",
            ["cookieLightType"] = "1",
        };

        /// <summary>The settings an importer of an oldest-layout sprite section writes into its
        /// conversion (<see cref="Arrange(YText, List{string}, bool)"/>, which reads the file's
        /// textureSettings without their own serialized version as defaults, tests/projects/spritemeta
        /// end.BareWrap, and the keys of <see cref="s_OldestDefaults"/> with those values): of those,
        /// only the ones the importer changed from the file's (a key the importer holds as the file
        /// does reads as the Editor's importer reads it); the platform entries left out
        /// (<see cref="OldestPlatforms"/>). Other settings as they are.</summary>
        internal static YNode ReadAsOldest(YNode body, YText file)
        {
            if (body == null || body.Kind != YKind.Map) return body;
            bool readAsDefaults = file.Get("textureSettings") is { Kind: YKind.Map } fileSettings && fileSettings.Get("serializedVersion") == null;
            var keys = new List<string>();
            var values = new List<YNode>();
            for (int i = 0; i < body.Keys.Length; i++)
            {
                var key = body.Keys[i];
                var value = body.Values[i];
                if (key == "platformSettings") continue;
                if (s_OldestDefaults.ContainsKey(key) && value.Kind == YKind.Scalar && file.Get(key) is { Kind: YKind.Scalar } own && own.Text == value.Scalar) continue;
                if (readAsDefaults && key == "textureSettings")
                {
                    value = Changed(value, file.Get(key));
                    if (value == null) continue;
                }
                keys.Add(key);
                values.Add(value);
            }
            return new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
        }

        /// <summary>The scalars of <paramref name="settings"/> that differ from <paramref name="file"/>'s; null for none.</summary>
        static YNode Changed(YNode settings, YText file)
        {
            if (settings.Kind != YKind.Map) return settings;
            var keys = new List<string>();
            var values = new List<YNode>();
            for (int i = 0; i < settings.Keys.Length; i++)
            {
                var own = file?.Get(settings.Keys[i]);
                if (settings.Values[i].Kind == YKind.Scalar && own is { Kind: YKind.Scalar } && own.Text == settings.Values[i].Scalar) continue;
                keys.Add(settings.Keys[i]);
                values.Add(settings.Values[i]);
            }
            return keys.Count == 0 ? null : new YNode { Kind = YKind.Map, Keys = keys.ToArray(), Values = values.ToArray() };
        }

        /// <param name="current">Whether the file is of the release's serialized version.</param>
        /// <param name="oldest">Whether it is a texture section of no serialized version.</param>
        YText Arrange(YText file, YText layout, string key, string at, bool current, List<string> unmeasured, bool oldest = false)
        {
            if (oldest && key == "textureSettings" && file.Kind == YKind.Map && file.Get("serializedVersion") == null) return layout.Clone();
            // A map of the release's serialized version inside an oldest section is read alike (its
            // missing keys at the defaults, spritemeta SaveClear, SaveData); one of another is laid out as any.
            if (oldest && file.Kind == YKind.Map && file.Get("serializedVersion") is { } inner
                && !ReferenceEquals(layout, m_Section) && layout.Get("serializedVersion")?.Text != inner.Text) oldest = false;
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
                // The oldest layout's platform list is the file's (none: empty), then the importer's (OldestPlatforms).
                if (value == null && oldest && name == "platformSettings" && m_PlatformEntry != null) value = YText.Seq(flow: true);
                if (oldest && at.Length == 0 && s_OldestDefaults.TryGetValue(name, out var oldestValue)) value = YText.Scalar(oldestValue);
                // A sprite's NPOT scaling the file lacks is none (spritemeta save.SaveData, SaveClear, SaveBundle).
                if (value == null && oldest && at.Length == 0 && name == "nPOTScale") value = YText.Scalar(((int)TextureImporterNPOTScale.None).ToString());
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
