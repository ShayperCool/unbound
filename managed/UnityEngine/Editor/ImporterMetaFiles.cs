using System;
using System.Collections.Generic;
using System.IO;
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
            ImporterMeta.Write(this);
            ImporterMeta.Reimport(this);
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
    /// fields (the bundle names lowercased), nothing else touched; a file whose settings did
    /// not change is not written. Saved by SaveAndReimport, WriteImportSettingsIfDirty, and by an
    /// import whose preprocessors changed the settings.
    /// </summary>
    internal static class ImporterMeta
    {
        internal static bool Write(AssetImporter importer)
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
            var unwritten = new List<string>();
            Patch(section, importer.m_Settings, "", unwritten);
            if (unwritten.Count > 0)
                Debug.LogWarning($"Unbound does not write these {importer.m_SettingsType} settings of '{path}' into its .meta yet (their place in it is not measured): {string.Join(", ", unwritten)}");
            SetScalar(section, "userData", importer.userData);
            SetScalar(section, "assetBundleName", importer.assetBundleName?.ToLowerInvariant());
            SetScalar(section, "assetBundleVariant", importer.assetBundleVariant?.ToLowerInvariant());
            var written = UnityYamlText.WriteTopLevel(root);
            importer.m_ImportedSettings = MetaSettings.ToJson(importer.m_Settings);
            if (written == text) return false;
            File.WriteAllText(meta, written);
            return true;
        }

        /// <summary>The asset imported again with the importer's settings: its loaded objects
        /// dropped, the import given the settings, the preprocessors run again on the next load.</summary>
        internal static void Reimport(AssetImporter importer)
        {
            var path = importer.assetPath;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;
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
        }

        /// <summary>Each scalar of <paramref name="settings"/> (the importer's tree) into the same
        /// place of <paramref name="text"/> where it differs; entries the file does not have stay
        /// out (their place in the Editor's layout is not known).</summary>
        static void Patch(YText text, YNode settings, string at, List<string> unwritten)
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
