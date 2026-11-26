using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// A texture importer's per-platform settings: the entries of its .meta's
    /// <c>platformSettings</c> list, found by their <c>buildTarget</c> (matches the Unity 6000.0.71f1
    /// Editor, tests/projects/textureplatforms). The importer adds the entries of the platforms it
    /// imports for that its .meta lacks when it loads (Standalone and the installation's platform
    /// modules as copies of the default entry as loaded, the default entry at its defaults), and one
    /// such entry a read asks for; Set stores an entry in its place or after the others, whatever
    /// platform it names; Clear removes one. The public methods are Unity's own C# around these.
    /// </summary>
    public sealed partial class TextureImporter
    {
        const string PlatformList = "platformSettings";

        /// <summary>The entries the importer added itself (not the .meta's), each as it was added:
        /// while unchanged they are no change to save (tests/projects/importermeta same.*); one changed
        /// since (its default edited through a property, any entry through a SerializedObject) is.</summary>
        readonly Dictionary<string, string> m_AddedPlatforms = new Dictionary<string, string>(StringComparer.Ordinal);

        internal override void ReadSettings(YNode body)
        {
            m_AddedPlatforms.Clear();
            foreach (var name in AddMissingPlatforms(m_Settings, ImporterMeta.TextureImportPlatforms)) Added(name);
            // The settings the asset imported with hold the added entries too (a preprocessor's
            // save compares with them).
            m_ImportedSettings = MetaSettings.ToJson(m_Settings);
        }

        void Added(string name)
        {
            if (Find(List(), name) is { } entry) m_AddedPlatforms[name] = MetaSettings.ToJson(entry);
        }

        /// <summary>The settings without the entries the importer added itself and nothing changed
        /// since: what the .meta holds unless something changed.</summary>
        internal override YNode SavedSettings()
        {
            if (m_AddedPlatforms.Count == 0 || m_Settings == null || !(m_Settings.Get(PlatformList) is { Kind: YKind.Seq } list)) return m_Settings;
            var copy = MetaSettings.Clone(m_Settings);
            var kept = new List<YNode>();
            foreach (var item in list.Items)
                if (!(NameOf(item) is { } name && m_AddedPlatforms.TryGetValue(name, out var added) && added == MetaSettings.ToJson(item)))
                    kept.Add(MetaSettings.Clone(item));
            SetList(copy, kept.ToArray());
            return copy;
        }

        internal override void SettingsWritten() => m_AddedPlatforms.Clear();

        /// <summary>Adds to <paramref name="settings"/> the entries its platform list lacks of the
        /// platforms in <paramref name="platforms"/> (null: none, where that is not measured), then
        /// the default entry; the names added. Each is the default entry as it is then, renamed
        /// (tests/projects/textureplatforms load.copy, load.order); the default entry at its
        /// defaults (load.nodefault). A missing default and a missing platform together are not
        /// measured (the default comes last). Settings without a platform list are left as they are.</summary>
        internal static List<string> AddMissingPlatforms(YNode settings, IReadOnlyList<string> platforms)
        {
            var added = new List<string>();
            if (platforms == null || settings == null || !(settings.Get(PlatformList) is { Kind: YKind.Seq } list)) return added;
            var defaults = Find(list, TextureImporterPlatformSettings.DefaultPlatformName);
            foreach (var name in platforms)
                if (Find(list, name) == null && added.IndexOf(name) < 0)
                {
                    list = Append(settings, list, NewEntry(defaults, name));
                    added.Add(name);
                }
            if (defaults == null)
            {
                Append(settings, list, NewEntry(null, TextureImporterPlatformSettings.DefaultPlatformName));
                added.Add(TextureImporterPlatformSettings.DefaultPlatformName);
            }
            return added;
        }

        static string NameOf(YNode entry) => entry?.Kind == YKind.Map ? entry.Get("buildTarget")?.AsString() : null;

        static YNode Find(YNode list, string name)
        {
            if (list == null || list.Kind != YKind.Seq) return null;
            foreach (var item in list.Items)
                if (NameOf(item) == name) return item;
            return null;
        }

        /// <summary>A new entry named <paramref name="name"/>: a copy of <paramref name="defaults"/>,
        /// or the platform settings' defaults without it.</summary>
        static YNode NewEntry(YNode defaults, string name)
        {
            var entry = defaults != null ? MetaSettings.Clone(defaults) : new TextureImporterPlatformSettings().ToYText(prefixed: false).ToNode();
            MetaSettings.Set(entry, "buildTarget", name);
            return entry;
        }

        static YNode Append(YNode settings, YNode list, YNode entry)
        {
            var items = new YNode[list.Items.Length + 1];
            Array.Copy(list.Items, items, list.Items.Length);
            items[^1] = entry;
            return SetList(settings, items);
        }

        static YNode SetList(YNode settings, YNode[] items)
        {
            var list = new YNode { Kind = YKind.Seq, Items = items };
            for (int i = 0; i < settings.Keys.Length; i++)
                if (settings.Keys[i] == PlatformList)
                {
                    settings.Values[i] = list;
                    return list;
                }
            var keys = new string[settings.Keys.Length + 1];
            var values = new YNode[settings.Values.Length + 1];
            Array.Copy(settings.Keys, keys, settings.Keys.Length);
            Array.Copy(settings.Values, values, settings.Values.Length);
            keys[^1] = PlatformList;
            values[^1] = list;
            settings.Keys = keys;
            settings.Values = values;
            return list;
        }

        YNode List() => m_Settings?.Get(PlatformList) is { Kind: YKind.Seq } list ? list : null;

        /// <summary>The entry read as the list's first entry's serialized version reads it (as
        /// <see cref="TextureImporterPlatformSettings.ReadList"/>).</summary>
        static TextureImporterPlatformSettings Read(YNode list, YNode entry)
        {
            int version = 1;
            foreach (var item in list.Items)
                if (item?.Kind == YKind.Map) { version = item.Int("serializedVersion", 1); break; }
            return TextureImporterPlatformSettings.ReadEntry(entry, prefixed: false, version);
        }

        /// <summary>The settings of <paramref name="platform"/> (a serialization name): its entry; a
        /// platform the importer imports for without one gets one, a copy of the default entry
        /// (tests/projects/textureplatforms clear.*, clear.DefaultTexturePlatform); any other
        /// platform reads the default entry (get.*).</summary>
        TextureImporterPlatformSettings PlatformSettings(string platform)
        {
            var list = List();
            var entry = Find(list, platform);
            if (entry == null && list != null && IsImportPlatform(platform))
            {
                entry = NewEntry(Find(list, TextureImporterPlatformSettings.DefaultPlatformName), platform);
                list = Append(m_Settings, list, entry);
                Added(platform);
            }
            entry ??= Find(list, TextureImporterPlatformSettings.DefaultPlatformName);
            return entry == null ? new TextureImporterPlatformSettings() : Read(list, entry);
        }

        static bool IsImportPlatform(string platform)
        {
            if (platform == TextureImporterPlatformSettings.DefaultPlatformName) return ImporterMeta.TextureImportPlatforms != null;
            var platforms = ImporterMeta.TextureImportPlatforms;
            if (platforms == null) return false;
            foreach (var name in platforms)
                if (name == platform) return true;
            return false;
        }

        /// <summary>Stores <paramref name="settings"/> (its name a serialization name) in the entry of
        /// its name, else after the others, overridden or not and whatever the platform
        /// (tests/projects/textureplatforms set.*, setoff.*); the importer is dirty.</summary>
        void StorePlatformSettings(TextureImporterPlatformSettings settings)
        {
            var name = settings.name ?? "";
            var node = settings.ToYText(prefixed: false).ToNode();
            var list = List();
            var entry = Find(list, name);
            if (entry != null)
            {
                // The entry keeps its place, serialized version and other keys.
                for (int i = 0; i < node.Keys.Length; i++)
                    if (node.Keys[i] != "serializedVersion") MetaSettings.Set(entry, node.Keys[i], node.Values[i].Scalar);
            }
            else if (list != null) Append(m_Settings, list, node);
            else if (m_Settings != null) SetList(m_Settings, new[] { node });
            m_AddedPlatforms.Remove(name);
            Changed();
            EditorUtility.SetDirty(this);
        }

        /// <summary>Removes the entry of <paramref name="platform"/> (a serialization name); the
        /// importer is dirty, whether it had one or not (tests/projects/textureplatforms clear.*,
        /// clear.missing).</summary>
        void RemovePlatformSettings(string platform)
        {
            var list = List();
            var entry = Find(list, platform);
            if (entry != null)
            {
                var items = new List<YNode>(list.Items);
                items.Remove(entry);
                SetList(m_Settings, items.ToArray());
                m_AddedPlatforms.Remove(platform);
                Changed();
            }
            EditorUtility.SetDirty(this);
        }

        // The public API, as Unity's TextureImporter.bindings.cs (6000.0) writes it around the
        // methods above.

        public bool GetPlatformTextureSettings(string platform, out int maxTextureSize, out TextureImporterFormat textureFormat, out int compressionQuality, out bool etc1AlphaSplitEnabled)
        {
            TextureImporterPlatformSettings settings = GetPlatformTextureSettings(platform);
            maxTextureSize = settings.maxTextureSize;
            textureFormat = settings.format;
            compressionQuality = settings.compressionQuality;
            etc1AlphaSplitEnabled = settings.allowsAlphaSplitting;
            return settings.overridden;
        }

        public bool GetPlatformTextureSettings(string platform, out int maxTextureSize, out TextureImporterFormat textureFormat, out int compressionQuality) =>
            GetPlatformTextureSettings(platform, out maxTextureSize, out textureFormat, out compressionQuality, out _);

        public bool GetPlatformTextureSettings(string platform, out int maxTextureSize, out TextureImporterFormat textureFormat) =>
            GetPlatformTextureSettings(platform, out maxTextureSize, out textureFormat, out _, out _);

        /// <summary>A copy of the platform's settings, named after the platform; a platform read from
        /// another entry's is not overridden.</summary>
        public TextureImporterPlatformSettings GetPlatformTextureSettings(string platform)
        {
            platform = GetTexturePlatformSerializationName(platform);
            CheckImporterState("GetPlatformTextureSetting_Internal");
            TextureImporterPlatformSettings dest = PlatformSettings(platform);
            if (platform != dest.name)
            {
                dest.name = platform;
                dest.overridden = false;
            }
            return dest;
        }

        public TextureImporterPlatformSettings GetDefaultPlatformTextureSettings() =>
            GetPlatformTextureSettings(TextureImporterPlatformSettings.DefaultPlatformName);

        [Obsolete("Use UnityEditor.TextureImporter.SetPlatformTextureSettings(TextureImporterPlatformSettings) instead.")]
        public void SetPlatformTextureSettings(string platform, int maxTextureSize, TextureImporterFormat textureFormat, int compressionQuality, bool allowsAlphaSplit)
        {
            TextureImporterPlatformSettings dest = GetPlatformTextureSettings(platform);
            dest.overridden = true;
            dest.maxTextureSize = maxTextureSize;
            dest.format = textureFormat;
            dest.compressionQuality = compressionQuality;
            dest.allowsAlphaSplitting = allowsAlphaSplit;
            SetPlatformTextureSettings(dest);
        }

        [Obsolete("Use UnityEditor.TextureImporter.SetPlatformTextureSettings(TextureImporterPlatformSettings) instead.")]
        public void SetPlatformTextureSettings(string platform, int maxTextureSize, TextureImporterFormat textureFormat) =>
            SetPlatformTextureSettings(platform, maxTextureSize, textureFormat, false);

        [Obsolete("Use UnityEditor.TextureImporter.SetPlatformTextureSettings(TextureImporterPlatformSettings) instead.")]
        public void SetPlatformTextureSettings(string platform, int maxTextureSize, TextureImporterFormat textureFormat, bool allowsAlphaSplit)
        {
            TextureImporterPlatformSettings dest = GetPlatformTextureSettings(platform);
            dest.overridden = true;
            dest.maxTextureSize = maxTextureSize;
            dest.format = textureFormat;
            dest.allowsAlphaSplitting = allowsAlphaSplit;
            SetPlatformTextureSettings(dest);
        }

        public void SetPlatformTextureSettings(TextureImporterPlatformSettings platformSettings)
        {
            platformSettings.name = GetTexturePlatformSerializationName(platformSettings.name);
            CheckImporterState("SetPlatformTextureSettings_Internal");
            StorePlatformSettings(platformSettings);
        }

        public void ClearPlatformTextureSettings(string platform)
        {
            platform = GetTexturePlatformSerializationName(platform);
            CheckImporterState("ClearPlatformTextureSettings_Internal");
            RemovePlatformSettings(platform);
        }

        /// <summary>Whether a PNG's gamma (its gAMA chunk) is left out of the import (the .meta's
        /// <c>ignorePngGamma</c>; tests/projects/textureplatforms gamma.*).</summary>
        public bool ignorePngGamma
        {
            get { CheckImporterState("get_ignorePngGamma"); return B("ignorePngGamma"); }
            set { CheckImporterState("set_ignorePngGamma"); SetB("ignorePngGamma", value); }
        }

        public bool allowAlphaSplitting
        {
            get { CheckImporterState("get_allowAlphaSplitting"); return B(Default + "allowsAlphaSplitting"); }
            set { CheckImporterState("set_allowAlphaSplitting"); SetB(Default + "allowsAlphaSplitting", value); }
        }

        public AndroidETC2FallbackOverride androidETC2FallbackOverride
        {
            get { CheckImporterState("get_androidETC2FallbackOverride"); return (AndroidETC2FallbackOverride)I(Default + "androidETC2FallbackOverride"); }
            set { CheckImporterState("set_androidETC2FallbackOverride"); SetI(Default + "androidETC2FallbackOverride", (int)value); }
        }
    }
}
