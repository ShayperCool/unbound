using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEditor.U2D
{
    /// <summary>
    /// The importer of a V2 sprite atlas (`.spriteatlasv2`), reading and writing its per-platform
    /// <see cref="TextureImporterPlatformSettings"/>, <see cref="SpriteAtlasPackingSettings"/>,
    /// <see cref="SpriteAtlasTextureSettings"/>, <see cref="variantScale"/> and
    /// <see cref="includeInBuild"/> in the asset's `.meta` (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/spriteatlaseditor v2importerplatform, v2importerplatformset,
    /// v2importerpacking, v2importertexture, v2importervariantscale and v2importerinclude).
    /// </summary>
    [HelpURL("https://docs.unity3d.com/6000.0/Documentation/Manual/sprite/atlas/v2/sprite-atlas-v2.html")]
    public sealed partial class SpriteAtlasImporter : AssetImporter
    {
        List<TextureImporterPlatformSettings> m_PlatformSettings;
        bool m_PlatformSettingsModified;
        SpriteAtlasPackingSettings m_PackingSettings = SpriteAtlasPackingSettings.Default;
        bool m_PackingSettingsModified;
        SpriteAtlasTextureSettings m_TextureSettings = SpriteAtlasTextureSettings.Default;
        bool m_TextureSettingsModified;
        float m_VariantScale = 1f;
        bool m_VariantScaleModified;
        bool m_IncludeInBuild = true;
        bool m_IncludeInBuildModified;

        public SpriteAtlasImporter() { }

        internal override void ReadSettings(YNode body)
        {
            m_PlatformSettings = TextureImporterPlatformSettings.ReadList(body?.Get("platformSettings"), prefixed: false);
            m_PlatformSettingsModified = false;
            m_PackingSettings = SpriteAtlasPackingSettings.Read(body?.Get("packingSettings"));
            m_PackingSettingsModified = false;
            m_TextureSettings = SpriteAtlasTextureSettings.Read(body?.Get("textureSettings"));
            m_TextureSettingsModified = false;
            m_VariantScale = body?.Float("variantMultiplier", 1f) ?? 1f;
            m_VariantScaleModified = false;
            m_IncludeInBuild = body?.Bool("bindAsDefault", true) ?? true;
            m_IncludeInBuildModified = false;
        }

        /// <summary>This importer's <c>variantMultiplier</c> from its `.meta` (defaults to 1 when
        /// omitted; setting stores the value verbatim without clamping and marks this importer dirty
        /// only when <c>m_VariantScale != value</c>, so same finite values and <c>-0f</c> on
        /// <c>0f</c> are no-ops while <c>NaN</c> always dirties; matches the Unity 6000.0.71f1
        /// Editor, tests/projects/spriteatlaseditor v2importervariantscale).</summary>
        public float variantScale
        {
            get
            {
                CheckImporterState("get_variantScale");
                return m_VariantScale;
            }
            set
            {
                CheckImporterState("set_variantScale");
                if (m_VariantScale == value) return;
                m_VariantScale = value;
                m_VariantScaleModified = true;
                EditorUtility.SetDirty(this);
                Changed();
            }
        }

        /// <summary>Whether builds include this sprite atlas (<c>bindAsDefault</c> in its `.meta`,
        /// defaulting to true when omitted; setting marks this importer dirty only when the value
        /// changes; matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        /// v2importerinclude).</summary>
        public bool includeInBuild
        {
            get
            {
                CheckImporterState("get_includeInBuild");
                return m_IncludeInBuild;
            }
            set
            {
                CheckImporterState("set_includeInBuild");
                if (m_IncludeInBuild == value) return;
                m_IncludeInBuild = value;
                m_IncludeInBuildModified = true;
                EditorUtility.SetDirty(this);
                Changed();
            }
        }

        /// <summary>This importer's own <see cref="SpriteAtlasTextureSettings"/> from its `.meta`
        /// (defaults when omitted, legacy v1 <c>colorSpace</c> mapped to <c>sRGB</c>, variant
        /// independent of its master; setting stores all fields verbatim and unconditionally marks
        /// this importer dirty; matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v2importertexture).</summary>
        public SpriteAtlasTextureSettings textureSettings
        {
            get
            {
                CheckImporterState("get_textureSettings");
                return m_TextureSettings;
            }
            set
            {
                CheckImporterState("set_textureSettings");
                m_TextureSettings = value;
                m_TextureSettingsModified = true;
                EditorUtility.SetDirty(this);
                Changed();
            }
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
            if (!m_PlatformSettingsModified && !m_PackingSettingsModified && !m_TextureSettingsModified
                && !m_VariantScaleModified && !m_IncludeInBuildModified) return false;
            int textureIndex = section.Keys.IndexOf("textureSettings");
            if (m_TextureSettingsModified || textureIndex < 0)
            {
                var textureText = m_TextureSettings.ToYText();
                if (textureIndex >= 0)
                {
                    section.Values[textureIndex] = textureText;
                }
                else
                {
                    int afterExternal = section.Keys.IndexOf("externalObjects");
                    textureIndex = afterExternal >= 0 ? afterExternal + 1 : 0;
                    section.Keys.Insert(textureIndex, "textureSettings");
                    section.Values.Insert(textureIndex, textureText);
                }
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
            int secondaryIndex = section.Keys.IndexOf("secondaryTextureSettings");
            if (secondaryIndex < 0)
            {
                secondaryIndex = packingIndex + 1;
                section.Keys.Insert(secondaryIndex, "secondaryTextureSettings");
                section.Values.Insert(secondaryIndex, YText.Map(flow: true));
            }
            int scaleIndex = section.Keys.IndexOf("variantMultiplier");
            if (m_VariantScaleModified || scaleIndex < 0)
            {
                var scaleText = YText.Scalar(UnityYamlWriter.Writer.Number(m_VariantScale));
                if (scaleIndex >= 0)
                {
                    section.Values[scaleIndex] = scaleText;
                }
                else
                {
                    scaleIndex = secondaryIndex + 1;
                    section.Keys.Insert(scaleIndex, "variantMultiplier");
                    section.Values.Insert(scaleIndex, scaleText);
                }
            }
            int bindIndex = section.Keys.IndexOf("bindAsDefault");
            if (m_IncludeInBuildModified || bindIndex < 0)
            {
                var bindText = YText.Scalar(m_IncludeInBuild ? "1" : "0");
                if (bindIndex >= 0)
                {
                    section.Values[bindIndex] = bindText;
                }
                else
                {
                    bindIndex = scaleIndex + 1;
                    section.Keys.Insert(bindIndex, "bindAsDefault");
                    section.Values.Insert(bindIndex, bindText);
                }
            }
            return true;
        }

        internal override void ClearModifiedState()
        {
            m_PlatformSettingsModified = false;
            m_PackingSettingsModified = false;
            m_TextureSettingsModified = false;
            m_VariantScaleModified = false;
            m_IncludeInBuildModified = false;
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
