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
            this.assetBundleName = assetBundleName;
            this.assetBundleVariant = assetBundleVariant;
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
    /// 10.preprocessor, tests/projects/assetpostprocessors meta.*): the file's text with each
    /// changed setting's value replaced in place, the common <c>userData</c> and AssetBundle
    /// fields (the bundle names lowercased), nothing else touched but a texture section of the
    /// current version that lacks settings, which is written whole (tests/projects/scenesave
    /// label-metas); a file whose settings did not change is not written. Saved by SaveAndReimport, WriteImportSettingsIfDirty, and by an
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
            var root = UnityYamlText.ParseTopLevel(text);
            var section = root.Get(importer.m_SettingsType);
            if (section == null || section.Kind != YKind.Map) return false;
            if (importer.UsesDirtyFlagForWrite)
            {
                bool wasDirty = EditorUtility.IsDirty(importer) || importer.m_SettingsChanged;
                SetScalar(section, "userData", importer.userData);
                SetScalar(section, "assetBundleName", importer.assetBundleName?.ToLowerInvariant());
                SetScalar(section, "assetBundleVariant", importer.assetBundleVariant?.ToLowerInvariant());
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
            // A texture's section that lacks settings of its version gets them first, as the
            // Editor writes the whole section (tests/projects/scenesave label-metas kept.*,
            // missingSetting.*, missingPlatform.*).
            if (importer.m_SettingsType == "TextureImporter") FillTextureSection(section);
            var unwritten = new List<string>();
            Patch(section, importer.m_Settings, "", unwritten);
            if (unwritten.Count > 0)
                Debug.LogWarning($"Unbound does not write these {importer.m_SettingsType} settings of '{path}' into its .meta yet (their place in it is not measured): {string.Join(", ", unwritten)}");
            SetScalar(section, "userData", importer.userData);
            SetScalar(section, "assetBundleName", importer.assetBundleName?.ToLowerInvariant());
            SetScalar(section, "assetBundleVariant", importer.assetBundleVariant?.ToLowerInvariant());
            var written = UnityYamlText.WriteTopLevel(root);
            importer.m_ImportedSettings = MetaSettings.ToJson(importer.m_Settings);
            if (written == text.Replace("\r\n", "\n")) return false;
            // A texture's settings written again list every installed platform module (the
            // Editor adds the missing ones at their defaults: tests/projects/projectwrites 06.crlf).
            if (importer.m_SettingsType == "TextureImporter" && AddInstalledPlatforms(section))
                written = UnityYamlText.WriteTopLevel(root);
            File.WriteAllText(meta, written);
            UnityEditor.ProjectImports.Wrote(meta);
            return true;
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

        sealed class Platform
        {
            public string Name { get; set; }
            public string Entry { get; set; }
        }

        static Platform[] s_Platforms;

        static string s_TextureTemplate;

        /// <summary>The settings a texture's section of the current version lacks, at their
        /// defaults in the Editor's order, and the platforms it lacks (DefaultTexturePlatform,
        /// Standalone, then the installed modules) after its own. A section of another version, or none, is left
        /// as it is (its settings would take that version's values: unmeasured).</summary>
        static void FillTextureSection(YText section)
        {
            s_TextureTemplate ??= System.Text.Json.JsonDocument.Parse(Native.Call("texture-meta-template", "{}")).RootElement.GetProperty("section").GetString() ?? "";
            FillTextureSection(section, s_TextureTemplate);
        }

        /// <summary><paramref name="templateText"/>: the TextureImporter section at its defaults
        /// ("" for a release family whose files are not measured: nothing is added).</summary>
        internal static void FillTextureSection(YText section, string templateText)
        {
            if (string.IsNullOrEmpty(templateText)) return;
            var template = UnityYamlText.ParseTopLevel(templateText).Get("TextureImporter");
            if (template?.Kind != YKind.Map || section.Get("serializedVersion")?.Text != template.Get("serializedVersion")?.Text) return;
            Fill(section, template);
        }

        static void Fill(YText text, YText template)
        {
            for (int i = 0; i < template.Keys.Count; i++)
            {
                var key = template.Keys[i];
                var want = template.Values[i];
                var have = text.Get(key);
                if (have == null)
                {
                    text.SetInOrder(key, want, template.Keys);
                    if (text.Flow) text.Raw = null;
                }
                else if (have.Kind == YKind.Map && want.Kind == YKind.Map) Fill(have, want);
                else if (have.Kind == YKind.Seq && want.Kind == YKind.Seq && key == "platformSettings") AppendMissingPlatforms(have, want.Items);
            }
        }

        /// <summary>The platform entries a list lacks, after the list's own, in the order given
        /// (label-metas kept.*: DefaultTexturePlatform, Android, then Standalone); whether any was
        /// added.</summary>
        static bool AppendMissingPlatforms(YText list, IEnumerable<YText> entries)
        {
            bool added = false;
            foreach (var entry in entries)
            {
                if (entry?.Get("buildTarget")?.Text is not string name || IndexOfPlatform(list, name) >= 0) continue;
                list.Items.Add(entry);
                added = true;
            }
            return added;
        }

        static int IndexOfPlatform(YText list, string name)
        {
            for (int i = 0; i < list.Items.Count; i++)
                if (list.Items[i].Kind == YKind.Map && list.Items[i].Get("buildTarget")?.Text == name) return i;
            return -1;
        }

        static bool AddInstalledPlatforms(YText section)
        {
            var list = section.Get("platformSettings");
            if (list == null || list.Kind != YKind.Seq) return false;
            s_Platforms ??= System.Text.Json.JsonSerializer.Deserialize<Platform[]>(Native.Call("texture-platforms", "{}"),
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }) ?? Array.Empty<Platform>();
            return AppendMissingPlatforms(list, s_Platforms.Select(platform =>
            {
                var entry = UnityYamlText.ParseTopLevel("platformSettings:\n" + platform.Entry).Get("platformSettings");
                return entry?.Kind == YKind.Seq && entry.Items.Count == 1 ? entry.Items[0] : null;
            }));
        }

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
}
