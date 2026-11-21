using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.U2D
{
    /// <summary>
    /// The importer of a V2 sprite atlas (`.spriteatlasv2`), reading its per-platform
    /// <see cref="TextureImporterPlatformSettings"/> from the asset's `.meta` (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v2importerplatform).
    /// </summary>
    [HelpURL("https://docs.unity3d.com/6000.0/Documentation/Manual/sprite/atlas/v2/sprite-atlas-v2.html")]
    public sealed partial class SpriteAtlasImporter : AssetImporter
    {
        List<TextureImporterPlatformSettings> m_PlatformSettings;

        public SpriteAtlasImporter() { }

        internal override void ReadSettings(YNode body)
        {
            m_PlatformSettings = TextureImporterPlatformSettings.ReadList(body?.Get("platformSettings"), prefixed: false);
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
    }
}
