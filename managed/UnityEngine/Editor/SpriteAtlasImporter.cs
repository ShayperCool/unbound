using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.U2D
{
    /// <summary>
    /// The importer of a V2 sprite atlas (`.spriteatlasv2`), reading and writing its per-platform
    /// <see cref="TextureImporterPlatformSettings"/> in the asset's `.meta` (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v2importerplatform and
    /// v2importerplatformset).
    /// </summary>
    [HelpURL("https://docs.unity3d.com/6000.0/Documentation/Manual/sprite/atlas/v2/sprite-atlas-v2.html")]
    public sealed partial class SpriteAtlasImporter : AssetImporter
    {
        List<TextureImporterPlatformSettings> m_PlatformSettings;
        bool m_PlatformSettingsModified;

        public SpriteAtlasImporter() { }

        internal override void ReadSettings(YNode body)
        {
            m_PlatformSettings = TextureImporterPlatformSettings.ReadList(body?.Get("platformSettings"), prefixed: false);
            m_PlatformSettingsModified = false;
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
            if (!m_PlatformSettingsModified) return false;
            int textureIndex = section.Keys.IndexOf("textureSettings");
            if (textureIndex < 0)
            {
                int afterExternal = section.Keys.IndexOf("externalObjects");
                textureIndex = afterExternal >= 0 ? afterExternal + 1 : 0;
                section.Keys.Insert(textureIndex, "textureSettings");
                section.Values.Insert(textureIndex, SpriteAtlasTextureSettings.Default.ToYText());
            }
            var platformText = TextureImporterPlatformSettings.ToYTextList(m_PlatformSettings, prefixed: false);
            int platformIndex = section.Keys.IndexOf("platformSettings");
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
            if (section.Keys.IndexOf("packingSettings") < 0)
            {
                int packingIndex = platformIndex + 1;
                section.Keys.Insert(packingIndex, "packingSettings");
                section.Values.Insert(packingIndex, SpriteAtlasPackingSettings.Default.ToYText());
            }
            return true;
        }

        internal override void ClearModifiedState()
        {
            m_PlatformSettingsModified = false;
            base.ClearModifiedState();
        }
    }
}
