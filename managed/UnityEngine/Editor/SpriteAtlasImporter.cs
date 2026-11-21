using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEditor.U2D
{
    /// <summary>
    /// The importer of a V2 sprite atlas (`.spriteatlasv2`), reading and writing its per-platform
    /// <see cref="TextureImporterPlatformSettings"/> and <see cref="SpriteAtlasPackingSettings"/>
    /// in the asset's `.meta` (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
    /// v2importerplatform, v2importerplatformset and v2importerpacking).
    /// </summary>
    [HelpURL("https://docs.unity3d.com/6000.0/Documentation/Manual/sprite/atlas/v2/sprite-atlas-v2.html")]
    public sealed partial class SpriteAtlasImporter : AssetImporter
    {
        List<TextureImporterPlatformSettings> m_PlatformSettings;
        bool m_PlatformSettingsModified;
        SpriteAtlasPackingSettings m_PackingSettings = SpriteAtlasPackingSettings.Default;
        bool m_PackingSettingsModified;

        public SpriteAtlasImporter() { }

        internal override void ReadSettings(YNode body)
        {
            m_PlatformSettings = TextureImporterPlatformSettings.ReadList(body?.Get("platformSettings"), prefixed: false);
            m_PlatformSettingsModified = false;
            m_PackingSettings = SpriteAtlasPackingSettings.Read(body?.Get("packingSettings"));
            m_PackingSettingsModified = false;
        }

        /// <summary>This importer's own <see cref="SpriteAtlasPackingSettings"/> from its `.meta`
        /// (a variant importer reads and writes its own `.meta` independently of its master, while
        /// the imported variant <see cref="SpriteAtlas"/> inherits its master's packing settings;
        /// setting stores all fields verbatim and unconditionally marks this importer dirty;
        /// matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerpacking).</summary>
        public SpriteAtlasPackingSettings packingSettings
        {
            get
            {
                CheckImporterState("get_packingSettings");
                return m_PackingSettings;
            }
            set
            {
                CheckImporterState("set_packingSettings");
                m_PackingSettings = value;
                m_PackingSettingsModified = true;
                EditorUtility.SetDirty(this);
                Changed();
            }
        }

        /// <summary>Returns a fresh copy of this importer's platform settings for
        /// <paramref name="buildTarget"/> (after normalizing the build target name), or fresh
        /// Editor defaults named after the normalized target when absent.</summary>
        public TextureImporterPlatformSettings GetPlatformSettings(string buildTarget)
        {
            buildTarget = TextureImporter.GetTexturePlatformSerializationName(buildTarget);
            CheckImporterState("GetPlatformSettings_Internal");
            return TextureImporterPlatformSettings.FindOrDefault(m_PlatformSettings, buildTarget);
        }

        /// <summary>Normalizes <paramref name="src"/>.<see cref="TextureImporterPlatformSettings.name"/>
        /// in place, replaces any matching platform entry in place or appends a defensive copy when
        /// overridden or named <c>DefaultTexturePlatform</c>, and unconditionally marks this importer
        /// dirty (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerplatformset).</summary>
        public void SetPlatformSettings(TextureImporterPlatformSettings src)
        {
            src.name = TextureImporter.GetTexturePlatformSerializationName(src.name);
            CheckImporterState("SetPlatformSettings_Internal");
            TextureImporterPlatformSettings.SetOrAdd(ref m_PlatformSettings, src);
            m_PlatformSettingsModified = true;
            EditorUtility.SetDirty(this);
            Changed();
        }

        internal override bool UsesDirtyFlagForWrite => true;

        internal override bool WriteCustomMeta(YText section)
        {
            if (!m_PlatformSettingsModified && !m_PackingSettingsModified) return false;
            int textureIndex = section.Keys.IndexOf("textureSettings");
            if (textureIndex < 0)
            {
                int afterExternal = section.Keys.IndexOf("externalObjects");
                textureIndex = afterExternal >= 0 ? afterExternal + 1 : 0;
                section.Keys.Insert(textureIndex, "textureSettings");
                section.Values.Insert(textureIndex, SpriteAtlasTextureSettings.Default.ToYText());
            }
            int platformIndex = section.Keys.IndexOf("platformSettings");
            if (m_PlatformSettingsModified || platformIndex < 0)
            {
                var platformText = TextureImporterPlatformSettings.ToYTextList(m_PlatformSettings, prefixed: false);
                if (platformIndex >= 0)
                {
                    section.Values[platformIndex] = platformText;
                }
                else
                {
                    platformIndex = textureIndex + 1;
                    section.Keys.Insert(platformIndex, "platformSettings");
                    section.Values.Insert(platformIndex, platformText);
                }
            }
            int packingIndex = section.Keys.IndexOf("packingSettings");
            if (m_PackingSettingsModified || packingIndex < 0)
            {
                var packingText = m_PackingSettings.ToYText();
                if (packingIndex >= 0)
                {
                    section.Values[packingIndex] = packingText;
                }
                else
                {
                    packingIndex = platformIndex + 1;
                    section.Keys.Insert(packingIndex, "packingSettings");
                    section.Values.Insert(packingIndex, packingText);
                }
            }
            return true;
        }

        internal override void ClearModifiedState()
        {
            m_PlatformSettingsModified = false;
            m_PackingSettingsModified = false;
            base.ClearModifiedState();
        }

        /// <summary>Evicts the cached importer, reloads this V2 sprite atlas from disk, and
        /// reimports any loaded V2 variant whose master references <paramref name="guid"/>
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerpacking).</summary>
        internal static void ReimportAsset(string path, string guid)
        {
            AssetImporter.Evict(path);
            AssetDatabaseRuntime.Invalidate(guid);
            AssetDatabaseRuntime.LoadFile(guid);
            var dependentGuids = new List<string>();
            foreach (var obj in ObjectRegistry.All(typeof(SpriteAtlas)))
            {
                if (obj is SpriteAtlas variant && variant.IsFromImporter && variant.isVariant
                    && string.Equals(variant.MasterAtlasRef.Guid, guid, StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(variant, out string varGuid, out _)
                    && !string.IsNullOrEmpty(varGuid)
                    && !string.Equals(varGuid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    dependentGuids.Add(varGuid);
                }
            }
            foreach (var varGuid in dependentGuids)
            {
                AssetDatabaseRuntime.Invalidate(varGuid);
                AssetDatabaseRuntime.LoadFile(varGuid);
            }
        }
    }
}
