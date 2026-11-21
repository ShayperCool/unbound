using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.U2D
{
    /// <summary>
    /// The settings the Editor keeps for a sprite atlas: its texture, per-platform and packing
    /// settings, variant scale and "Include in Build". A V1 atlas keeps them in its `.spriteatlas`
    /// (`m_EditorData`, platform fields prefixed `m_`), a V2 atlas in its SpriteAtlasImporter's
    /// `.meta` (unprefixed), under the same keys in the same order (matches the Unity 6000.0.71f1
    /// Editor, tests/projects/spriteatlaseditor).
    /// </summary>
    internal sealed class SpriteAtlasEditorSettings
    {
        /// <summary>The keys of a V1 atlas's `m_EditorData` and of a SpriteAtlasImporter section in
        /// the order the Editor writes them.</summary>
        internal static readonly string[] KeyOrder =
        {
            "externalObjects", "serializedVersion", "textureSettings", "platformSettings", "packingSettings",
            "secondaryTextureSettings", "variantMultiplier", "packables", "totalSpriteSurfaceArea", "bindAsDefault",
            "isAtlasV2", "cachedData", "packedSpriteRenderDataKeys", "userData", "assetBundleName", "assetBundleVariant",
        };

        internal SpriteAtlasTextureSettings Texture = SpriteAtlasTextureSettings.Default;
        internal List<TextureImporterPlatformSettings> Platforms;
        internal SpriteAtlasPackingSettings Packing = SpriteAtlasPackingSettings.Default;
        internal float VariantScale = 1f;
        internal bool IncludeInBuild = true;

        /// <summary>The settings in <paramref name="map"/>, Editor defaults where it has none.</summary>
        internal static SpriteAtlasEditorSettings Read(YNode map, bool prefixed) => new SpriteAtlasEditorSettings
        {
            Texture = SpriteAtlasTextureSettings.Read(map?.Get("textureSettings")),
            Platforms = TextureImporterPlatformSettings.ReadList(map?.Get("platformSettings"), prefixed),
            Packing = SpriteAtlasPackingSettings.Read(map?.Get("packingSettings")),
            VariantScale = map?.Float("variantMultiplier", 1f) ?? 1f,
            IncludeInBuild = map?.Bool("bindAsDefault", true) ?? true,
        };

        /// <summary>A fresh copy of the platform settings for <paramref name="buildTarget"/>, or
        /// Editor defaults named after it.</summary>
        internal TextureImporterPlatformSettings PlatformSettings(string buildTarget) =>
            TextureImporterPlatformSettings.FindOrDefault(Platforms, buildTarget);

        internal void SetPlatformSettings(TextureImporterPlatformSettings src) =>
            TextureImporterPlatformSettings.SetOrAdd(ref Platforms, src);

        /// <summary>Writes every setting into <paramref name="map"/>, each key in its place, as the
        /// Editor writes the whole object (an empty `secondaryTextureSettings` where it has none).</summary>
        internal void Write(YText map, bool prefixed)
        {
            map.SetInOrder("textureSettings", Texture.ToYText(), KeyOrder);
            map.SetInOrder("platformSettings", TextureImporterPlatformSettings.ToYTextList(Platforms, prefixed), KeyOrder);
            map.SetInOrder("packingSettings", Packing.ToYText(), KeyOrder);
            if (map.Get("secondaryTextureSettings") == null)
                map.SetInOrder("secondaryTextureSettings", YText.Map(flow: true), KeyOrder);
            map.SetInOrder("variantMultiplier", YText.Scalar(UnityYamlWriter.Writer.Number(VariantScale)), KeyOrder);
            map.SetInOrder("bindAsDefault", YText.Scalar(IncludeInBuild ? "1" : "0"), KeyOrder);
        }
    }
}
