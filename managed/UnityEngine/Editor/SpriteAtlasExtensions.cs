using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace UnityEditor.U2D
{
    /// <summary>
    /// A sprite atlas's packing settings. A zero-initialized value has every field cleared; a new
    /// atlas's defaults are blockOffset 1, padding 4, rotation and tight packing on, alpha dilation
    /// off (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor).
    /// </summary>
    public partial struct SpriteAtlasPackingSettings
    {
        int m_BlockOffset;
        int m_Padding;
        bool m_AllowAlphaSplitting;
        bool m_EnableRotation;
        bool m_EnableTightPacking;
        bool m_EnableAlphaDilation;

        internal static SpriteAtlasPackingSettings Default => new SpriteAtlasPackingSettings
        {
            m_BlockOffset = 1,
            m_Padding = 4,
            m_AllowAlphaSplitting = false,
            m_EnableRotation = true,
            m_EnableTightPacking = true,
            m_EnableAlphaDilation = false,
        };

        public int blockOffset { get => m_BlockOffset; set => m_BlockOffset = value; }
        public int padding { get => m_Padding; set => m_Padding = value; }
        public bool enableRotation { get => m_EnableRotation; set => m_EnableRotation = value; }
        public bool enableTightPacking { get => m_EnableTightPacking; set => m_EnableTightPacking = value; }
        public bool enableAlphaDilation { get => m_EnableAlphaDilation; set => m_EnableAlphaDilation = value; }

        internal static SpriteAtlasPackingSettings Read(YNode node)
        {
            var settings = Default;
            if (node == null || node.Kind != YKind.Map) return settings;
            int version = node.Int("serializedVersion", 1);
            if (version <= 1 && node.Get("paddingPower") != null)
            {
                uint power = (uint)Math.Max(0, node.Int("paddingPower", 0));
                settings.m_Padding = power == 0 ? 2 : 1 << (int)power;
            }
            else if (node.Get("padding") != null)
            {
                settings.m_Padding = node.Int("padding", settings.m_Padding);
            }
            settings.m_BlockOffset = node.Int("blockOffset", settings.m_BlockOffset);
            settings.m_AllowAlphaSplitting = node.Bool("allowAlphaSplitting", settings.m_AllowAlphaSplitting);
            settings.m_EnableRotation = node.Bool("enableRotation", settings.m_EnableRotation);
            settings.m_EnableTightPacking = node.Bool("enableTightPacking", settings.m_EnableTightPacking);
            settings.m_EnableAlphaDilation = node.Bool("enableAlphaDilation", settings.m_EnableAlphaDilation);
            return settings;
        }
    }

    /// <summary>
    /// A sprite atlas's texture settings. A zero-initialized value has every field cleared; a new
    /// atlas's defaults are maxTextureSize 2048, anisoLevel 1, Bilinear filtering, mipmaps and
    /// readability off, sRGB on (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/spriteatlaseditor).
    /// </summary>
    public partial struct SpriteAtlasTextureSettings
    {
        int m_AnisoLevel;
        int m_CompressionQuality;
        int m_MaxTextureSize;
        int m_TextureCompression;
        int m_FilterMode;
        bool m_GenerateMipMaps;
        bool m_Readable;
        bool m_CrunchedCompression;
        bool m_sRGB;

        internal static SpriteAtlasTextureSettings Default => new SpriteAtlasTextureSettings
        {
            m_AnisoLevel = 1,
            m_CompressionQuality = 50,
            m_MaxTextureSize = 2048,
            m_TextureCompression = 0,
            m_FilterMode = (int)FilterMode.Bilinear,
            m_GenerateMipMaps = false,
            m_Readable = false,
            m_CrunchedCompression = false,
            m_sRGB = true,
        };

        public int maxTextureSize => m_MaxTextureSize;
        public int anisoLevel { get => m_AnisoLevel; set => m_AnisoLevel = value; }
        public FilterMode filterMode { get => (FilterMode)m_FilterMode; set => m_FilterMode = (int)value; }
        public bool generateMipMaps { get => m_GenerateMipMaps; set => m_GenerateMipMaps = value; }
        public bool readable { get => m_Readable; set => m_Readable = value; }
        public bool sRGB { get => m_sRGB; set => m_sRGB = value; }

        internal static SpriteAtlasTextureSettings Read(YNode node)
        {
            var settings = Default;
            if (node == null || node.Kind != YKind.Map) return settings;
            settings.m_AnisoLevel = node.Int("anisoLevel", settings.m_AnisoLevel);
            settings.m_CompressionQuality = node.Int("compressionQuality", settings.m_CompressionQuality);
            settings.m_MaxTextureSize = node.Int("maxTextureSize", settings.m_MaxTextureSize);
            settings.m_TextureCompression = node.Int("textureCompression", settings.m_TextureCompression);
            settings.m_FilterMode = node.Int("filterMode", settings.m_FilterMode);
            settings.m_GenerateMipMaps = node.Bool("generateMipMaps", settings.m_GenerateMipMaps);
            settings.m_Readable = node.Bool("readable", settings.m_Readable);
            settings.m_CrunchedCompression = node.Bool("crunchedCompression", settings.m_CrunchedCompression);
            int version = node.Int("serializedVersion", 1);
            if (version <= 1 && node.Get("colorSpace") != null)
                settings.m_sRGB = node.Int("colorSpace", -1) == 0;
            else
                settings.m_sRGB = node.Bool("sRGB", settings.m_sRGB);
            return settings;
        }
    }

    /// <summary>
    /// The Editor's view of a sprite atlas (V1 `.spriteatlas` and V2 `.spriteatlasv2` alike), as the
    /// Unity 6000.0.71f1 Editor answers it (tests/projects/spriteatlaseditor): its packables in their
    /// stored order (a folder as its folder asset, a texture, a sprite; a packable whose asset is gone
    /// is left out), its master atlas (a variant's; null otherwise), whether builds include it, and
    /// its packing, texture and per-platform settings. A null atlas throws ArgumentNullException.
    /// </summary>
    public static partial class SpriteAtlasExtensions
    {
        public static Object[] GetPackables(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            var packables = new List<Object>();
            foreach (var packable in spriteAtlas.Packables)
            {
                if (string.IsNullOrEmpty(packable.Guid)) continue;
                var path = AssetDatabase.GUIDToAssetPath(packable.Guid);
                if (string.IsNullOrEmpty(path)) continue;
                var asset = AssetDatabase.IsValidFolder(path)
                    ? AssetDatabase.LoadAssetAtPath(path, typeof(Object))
                    : AssetDatabaseRuntime.Load(packable.Guid, packable.FileId, null);
                if (asset != null) packables.Add(asset);
            }
            return packables.ToArray();
        }

        public static SpriteAtlasPackingSettings GetPackingSettings(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            return spriteAtlas.PackingSettings;
        }

        public static SpriteAtlasTextureSettings GetTextureSettings(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            return spriteAtlas.TextureSettings;
        }

        public static TextureImporterPlatformSettings GetPlatformSettings(this SpriteAtlas spriteAtlas, string buildTarget)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            return spriteAtlas.ReadPlatformSettings(TextureImporter.GetTexturePlatformSerializationName(buildTarget));
        }

        public static SpriteAtlas GetMasterAtlas(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            return spriteAtlas.MasterAtlas;
        }

        public static bool IsIncludeInBuild(this SpriteAtlas spriteAtlas)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            return spriteAtlas.IncludeInBuild;
        }

        public static void SetIncludeInBuild(this SpriteAtlas spriteAtlas, bool value)
        {
            if (spriteAtlas == null) throw new ArgumentNullException(nameof(spriteAtlas));
            spriteAtlas.WriteIncludeInBuild(value);
        }
    }
}
