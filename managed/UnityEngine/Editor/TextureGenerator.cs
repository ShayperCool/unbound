using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UnityEditor.AssetImporters
{
    /// <summary>The source image's size and content.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial class SourceTextureInformation
    {
        int m_Width;
        int m_Height;
        bool m_SourceContainsAlpha;
        bool m_SourceWasHDR;

        public SourceTextureInformation() { }

        public int width { get => m_Width; set => m_Width = value; }
        public int height { get => m_Height; set => m_Height = value; }
        public bool containsAlpha { get => m_SourceContainsAlpha; set => m_SourceContainsAlpha = value; }
        public bool hdr { get => m_SourceWasHDR; set => m_SourceWasHDR = value; }
    }

    /// <summary>One sprite TextureGenerator cuts from the image.</summary>
    public partial struct SpriteImportData
    {
        string m_Name;
        Rect m_Rect;
        SpriteAlignment m_Alignment;
        Vector2 m_Pivot;
        Vector4 m_Border;
        string m_CustomData;
        List<Vector2[]> m_Outline;
        float m_TessellationDetail;
        string m_SpriteID;

        public string name { get => m_Name; set => m_Name = value; }
        public Rect rect { get => m_Rect; set => m_Rect = value; }
        public SpriteAlignment alignment { get => m_Alignment; set => m_Alignment = value; }
        public Vector2 pivot { get => m_Pivot; set => m_Pivot = value; }
        public Vector4 border { get => m_Border; set => m_Border = value; }
        internal string customData { get => m_CustomData; set => m_CustomData = value; }
        public List<Vector2[]> outline { get => m_Outline; set => m_Outline = value; }
        public float tessellationDetail { get => m_TessellationDetail; set => m_TessellationDetail = value; }
        public string spriteID { get => m_SpriteID; set => m_SpriteID = value; }
    }

    /// <summary>How TextureGenerator.GenerateTexture makes a texture and its sprites.</summary>
    public partial struct TextureGenerationSettings
    {
        string m_AssetPath;
        bool m_QualifyForSpritePacking;
        bool m_EnablePostProcessor;
        TextureImporterSettings m_Settings;
        TextureImporterPlatformSettings m_PlatformSettings;
        SourceTextureInformation m_SourceTextureInformation;
        SpriteImportData[] m_SpriteImportData;
        string m_SpritePackingTag;
        SecondarySpriteTexture[] m_SecondarySpriteTextures;

        /// <summary>
        /// The common settings for a texture type: the default platform's settings, an empty
        /// source, postprocessors on, and importer settings whose 2D shape, mip fade, alpha test,
        /// height scale, cubemap layout and single 100-pixels-per-unit Tight sprite settings are
        /// the importer's, with what the type adds (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/toolapis defaults: this table is not ApplyTextureType's).
        /// </summary>
        public TextureGenerationSettings(TextureImporterType type)
        {
            m_AssetPath = "";
            m_QualifyForSpritePacking = false;
            m_EnablePostProcessor = true;
            m_SpritePackingTag = "";
            m_SpriteImportData = null;
            m_SecondarySpriteTextures = null;
            m_PlatformSettings = new TextureImporterPlatformSettings();
            m_SourceTextureInformation = new SourceTextureInformation();
            var s = new TextureImporterSettings
            {
                textureType = type,
                textureShape = TextureImporterShape.Texture2D,
                alphaTestReferenceValue = 0.5f,
                mipmapFadeDistanceStart = 1,
                mipmapFadeDistanceEnd = 3,
                heightmapScale = 0.25f,
                generateCubemap = TextureImporterGenerateCubemap.AutoCubemap,
                spriteMode = (int)SpriteImportMode.Single,
                spritePixelsPerUnit = 100,
                spriteTessellationDetail = -1,
                spriteExtrude = 1,
                spriteMeshType = SpriteMeshType.Tight,
                spritePivot = new Vector2(0.5f, 0.5f),
            };
            switch (type)
            {
                case TextureImporterType.GUI:
                    s.mipmapEnabled = false;
                    s.sRGBTexture = false;
                    s.aniso = 1;
                    s.wrapMode = TextureWrapMode.Clamp;
                    s.alphaIsTransparency = true;
                    break;
                case TextureImporterType.Sprite:
                    s.mipmapEnabled = false;
                    s.wrapMode = TextureWrapMode.Clamp;
                    s.alphaIsTransparency = true;
                    break;
                case TextureImporterType.Cursor:
                    s.mipmapEnabled = false;
                    s.readable = true;
                    s.aniso = 1;
                    s.wrapMode = TextureWrapMode.Clamp;
                    s.alphaIsTransparency = true;
                    break;
                case TextureImporterType.Cookie:
                    s.npotScale = TextureImporterNPOTScale.ToNearest;
                    s.borderMipmap = true;
                    s.wrapMode = TextureWrapMode.Clamp;
                    break;
                case TextureImporterType.Lightmap:
                    s.npotScale = TextureImporterNPOTScale.ToNearest;
                    s.alphaSource = TextureImporterAlphaSource.None;
                    break;
                case TextureImporterType.Shadowmask:
                case TextureImporterType.DirectionalLightmap:
                    s.npotScale = TextureImporterNPOTScale.ToNearest;
                    s.sRGBTexture = false;
                    s.alphaSource = TextureImporterAlphaSource.None;
                    break;
                case TextureImporterType.NormalMap:
                case TextureImporterType.SingleChannel:
                    s.npotScale = TextureImporterNPOTScale.ToNearest;
                    s.sRGBTexture = false;
                    break;
                default:
                    s.npotScale = TextureImporterNPOTScale.ToNearest;
                    break;
            }
            m_Settings = s;
        }

        public string assetPath { get => m_AssetPath; set => m_AssetPath = value; }
        public bool qualifyForSpritePacking { get => m_QualifyForSpritePacking; set => m_QualifyForSpritePacking = value; }
        public bool enablePostProcessor { get => m_EnablePostProcessor; set => m_EnablePostProcessor = value; }
        public TextureImporterSettings textureImporterSettings { get => m_Settings; set => m_Settings = value; }
        public TextureImporterPlatformSettings platformSettings { get => m_PlatformSettings; set => m_PlatformSettings = value; }
        public SourceTextureInformation sourceTextureInformation { get => m_SourceTextureInformation; set => m_SourceTextureInformation = value; }
        public SpriteImportData[] spriteImportData { get => m_SpriteImportData; set => m_SpriteImportData = value; }
        public string spritePackingTag { get => m_SpritePackingTag; set => m_SpritePackingTag = value; }
        public SecondarySpriteTexture[] secondarySpriteTextures { get => m_SecondarySpriteTextures; set => m_SecondarySpriteTextures = value; }
    }

    /// <summary>What TextureGenerator.GenerateTexture made.</summary>
    public partial struct TextureGenerationOutput
    {
        Texture m_Texture;
        string m_ImportInspectorWarnings;
        string[] m_ImportWarnings;
        Texture2D m_ThumbNail;
        Sprite[] m_Sprites;

        internal TextureGenerationOutput(Texture texture, Texture2D thumbNail, Sprite[] sprites)
        {
            m_Texture = texture;
            m_ThumbNail = thumbNail;
            m_Sprites = sprites;
            m_ImportInspectorWarnings = "";
            m_ImportWarnings = Array.Empty<string>();
        }

        public Texture2D texture => m_Texture as Texture2D;
        public Texture output => m_Texture;
        public string importInspectorWarnings => m_ImportInspectorWarnings;
        public string[] importWarnings => m_ImportWarnings;
        public Texture2D thumbNail => m_ThumbNail;
        public Sprite[] sprites => m_Sprites;
    }

    /// <summary>
    /// Textures and sprites from a script's colour buffer, as an importer makes them from a file
    /// with the same settings (matches the Unity 6000.0.71f1 Editor, tests/projects/toolapis):
    /// the texture takes the settings' format, size, mips and sampler state and no name; sprites
    /// come from the sprite data of a Sprite texture only (a single sprite from the first entry,
    /// over the whole image); with <c>enablePostProcessor</c> the project's postprocessors see
    /// <c>OnPostprocessTexture</c> and then, when there are sprites, <c>OnPostprocessSprites</c>,
    /// with the settings' asset path and no context, and no preprocess hook, as the Scripting API
    /// documents.
    /// </summary>
    public static partial class TextureGenerator
    {
        /// <summary>The key prefix of a generated texture's native texels.</summary>
        internal const string KeyPrefix = "generated:";

        /// <summary>The thumbnail's size: 16 x 16 for every measured texture (its texels are
        /// unmeasured: Unbound samples the source's nearest texels).</summary>
        const int k_ThumbnailSize = 16;

        const string k_SizeMismatch = "Image data byte count does not match requested texture import format";

        static List<(int Order, Type Type, MethodInfo Method)> s_TextureHooks, s_SpriteHooks;

        public static TextureGenerationOutput GenerateTexture(TextureGenerationSettings settings, NativeArray<Color32> colorBuffer)
        {
            var source = Validate(settings, colorBuffer.IsCreated ? colorBuffer.Length : -1);
            return Generate(settings, source, MemoryMarshal.AsBytes(colorBuffer.AsReadOnlySpan()));
        }

        public static TextureGenerationOutput GenerateTexture(TextureGenerationSettings settings, NativeArray<Color> colorBuffer)
        {
            // Float colours are linear: in a gamma-space project they are gamma-encoded and
            // rounded to bytes, whatever the texture's sRGB setting, and then import as bytes do
            // (tests/projects/toolapis g.colorOverload, g.colorOverloadLinear). Linear-space
            // projects and HDR sources are unmeasured.
            var source = Validate(settings, colorBuffer.IsCreated ? colorBuffer.Length : -1);
            if (source.hdr)
                throw new NotSupportedException("Unbound: TextureGenerator.GenerateTexture of an HDR source is not supported yet");
            if (QualitySettings.activeColorSpace != ColorSpace.Gamma)
                throw new NotSupportedException("Unbound: TextureGenerator.GenerateTexture of Color texels in a linear-space project is not supported yet (the Color32 overload is)");
            var texels = new Color32[colorBuffer.Length];
            for (int i = 0; i < texels.Length; i++)
            {
                var c = colorBuffer[i];
                texels[i] = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), c.a);
            }
            return Generate(settings, source, MemoryMarshal.AsBytes(texels.AsSpan()));
        }

        static SourceTextureInformation Validate(TextureGenerationSettings settings, int length)
        {
            if (settings.textureImporterSettings == null) throw new ArgumentNullException("settings.textureImporterSettings");
            if (settings.platformSettings == null) throw new ArgumentNullException("settings.platformSettings");
            var source = settings.sourceTextureInformation ?? throw new ArgumentNullException("settings.sourceTextureInformation");
            if (source.width <= 0 || source.height <= 0 || length != source.width * source.height)
                throw new ArgumentException(k_SizeMismatch);
            return source;
        }

        static TextureGenerationOutput Generate(TextureGenerationSettings settings, SourceTextureInformation source, ReadOnlySpan<byte> texels)
        {
            var s = settings.textureImporterSettings;
            var key = KeyPrefix + Guid.NewGuid().ToString("N");
            // The texture takes the asset path's file name (none without a path).
            var name = System.IO.Path.GetFileNameWithoutExtension(settings.assetPath ?? "");
            var request = JsonSerializer.Serialize(new { key, name, source.width, source.height, meta = Meta(settings) });
            ulong handle = RenderHandles.Next();
            var info = Native.GenerateTexture(handle, request, texels);
            var texture = Texture2D.FromImport(handle, key, name, info);
            texture.hideFlags = HideFlags.None;
            texture.IsPersistentAsset = false;
            texture.alphaIsTransparency = s.alphaIsTransparency;
            var sprites = info.SpriteCount > 0
                ? Unbound.Runtime.AssetImporters.ImportedSprites(texture, key, name).ConvertAll(p => p.Sprite).ToArray()
                : Array.Empty<Sprite>();
            var output = new TextureGenerationOutput(texture, Thumbnail(source, texels), sprites);
            if (settings.enablePostProcessor)
            {
                s_TextureHooks ??= AssetPostprocessorHooks.Find("OnPostprocessTexture", new[] { typeof(Texture2D) });
                s_SpriteHooks ??= AssetPostprocessorHooks.Find("OnPostprocessSprites", new[] { typeof(Texture2D), typeof(Sprite[]) });
                AssetPostprocessorHooks.Run(s_TextureHooks, settings.assetPath, null, new object[] { texture });
                if (sprites.Length > 0)
                    AssetPostprocessorHooks.Run(s_SpriteHooks, settings.assetPath, null, new object[] { texture, sprites });
            }
            return output;
        }

        /// <summary>Forgets a destroyed generated texture's texels.</summary>
        internal static void Release(string key)
        {
            try { Native.Call("generated-texture-release", JsonSerializer.Serialize(new { key })); }
            catch (InvalidOperationException) { }
        }

        static Texture2D Thumbnail(SourceTextureInformation source, ReadOnlySpan<byte> texels)
        {
            var thumbnail = new Texture2D(k_ThumbnailSize, k_ThumbnailSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[k_ThumbnailSize * k_ThumbnailSize];
            for (int y = 0; y < k_ThumbnailSize; y++)
                for (int x = 0; x < k_ThumbnailSize; x++)
                {
                    int i = (y * source.height / k_ThumbnailSize) * source.width + x * source.width / k_ThumbnailSize;
                    pixels[y * k_ThumbnailSize + x] = MemoryMarshal.Cast<byte, Color32>(texels)[i];
                }
            thumbnail.SetPixels32(pixels);
            thumbnail.Apply();
            return thumbnail;
        }

        // ------------------------------------------------------------------ the settings as a .meta

        /// <summary>The settings as a texture's <c>.meta</c> would give them to the importer: the
        /// one platform setting is the default platform's, and the sprite data a sprite sheet.</summary>
        static string Meta(TextureGenerationSettings settings)
        {
            var s = settings.textureImporterSettings;
            var p = settings.platformSettings;
            var y = new StringBuilder("TextureImporter:\n");
            void Line(int depth, string key, object value) =>
                y.Append(' ', depth * 2).Append(key).Append(": ").Append(Scalar(value)).Append('\n');
            y.Append("  mipmaps:\n");
            Line(2, "mipMapMode", (int)s.mipmapFilter);
            Line(2, "enableMipMap", s.mipmapEnabled);
            Line(2, "sRGBTexture", s.sRGBTexture);
            Line(2, "fadeOut", s.fadeOut);
            Line(2, "borderMipMap", s.borderMipmap);
            Line(2, "mipMapsPreserveCoverage", s.mipMapsPreserveCoverage);
            Line(2, "alphaTestReferenceValue", s.alphaTestReferenceValue);
            Line(2, "mipMapFadeDistanceStart", s.mipmapFadeDistanceStart);
            Line(2, "mipMapFadeDistanceEnd", s.mipmapFadeDistanceEnd);
            y.Append("  bumpmap:\n");
            Line(2, "convertToNormalMap", s.convertToNormalMap);
            Line(2, "heightScale", s.heightmapScale);
            Line(2, "normalMapFilter", (int)s.normalMapFilter);
            Line(2, "flipGreenChannel", s.flipGreenChannel);
            Line(1, "isReadable", s.readable);
            Line(1, "generateCubemap", (int)s.generateCubemap);
            Line(1, "cubemapConvolution", (int)s.cubemapConvolution);
            y.Append("  textureSettings:\n");
            Line(2, "filterMode", (int)s.filterMode);
            Line(2, "aniso", s.aniso);
            Line(2, "mipBias", s.mipmapBias);
            Line(2, "wrapU", (int)s.wrapModeU);
            Line(2, "wrapV", (int)s.wrapModeV);
            Line(2, "wrapW", (int)s.wrapModeW);
            Line(1, "nPOTScale", (int)s.npotScale);
            bool sprite = s.textureType == TextureImporterType.Sprite;
            var data = settings.spriteImportData;
            // A single sprite is the first entry over the whole image: a sheet of one.
            int mode = !sprite || data == null || data.Length == 0 ? 0
                : s.spriteMode == (int)SpriteImportMode.Polygon ? s.spriteMode
                : (int)SpriteImportMode.Multiple;
            Line(1, "spriteMode", mode);
            Line(1, "spriteExtrude", s.spriteExtrude);
            Line(1, "spriteMeshType", (int)s.spriteMeshType);
            Line(1, "alignment", s.spriteAlignment);
            Line(1, "spritePivot", s.spritePivot);
            Line(1, "spritePixelsToUnits", s.spritePixelsPerUnit);
            Line(1, "spriteBorder", s.spriteBorder);
            Line(1, "spriteGenerateFallbackPhysicsShape", s.spriteGenerateFallbackPhysicsShape);
            Line(1, "alphaUsage", (int)s.alphaSource);
            Line(1, "alphaIsTransparency", s.alphaIsTransparency);
            Line(1, "spriteTessellationDetail", s.spriteTessellationDetail);
            Line(1, "textureType", (int)s.textureType);
            Line(1, "textureShape", (int)s.textureShape);
            Line(1, "singleChannelComponent", (int)s.singleChannelComponent);
            Line(1, "flipbookRows", s.flipbookRows);
            Line(1, "flipbookColumns", s.flipbookColumns);
            Line(1, "ignorePngGamma", s.ignorePngGamma);
            y.Append("  platformSettings:\n");
            y.Append("  - buildTarget: ").Append(TextureImporterPlatformSettings.DefaultPlatformName).Append('\n');
            Line(2, "maxTextureSize", p.maxTextureSize);
            Line(2, "resizeAlgorithm", (int)p.resizeAlgorithm);
            Line(2, "textureFormat", (int)p.format);
            Line(2, "textureCompression", (int)p.textureCompression);
            Line(2, "compressionQuality", p.compressionQuality);
            Line(2, "crunchedCompression", p.crunchedCompression);
            Line(2, "allowsAlphaSplitting", p.allowsAlphaSplitting);
            if (mode == (int)SpriteImportMode.Multiple)
            {
                y.Append("  spriteSheet:\n    sprites:\n");
                bool single = s.spriteMode == (int)SpriteImportMode.Single;
                var source = settings.sourceTextureInformation;
                for (int i = 0; i < (single ? 1 : data.Length); i++)
                {
                    var d = data[i];
                    var rect = single ? new Rect(0, 0, source.width, source.height) : d.rect;
                    y.Append("    - name: ").Append(Scalar(d.name ?? "")).Append('\n');
                    y.Append("      rect: {x: ").Append(Scalar(rect.x)).Append(", y: ").Append(Scalar(rect.y))
                        .Append(", width: ").Append(Scalar(rect.width)).Append(", height: ").Append(Scalar(rect.height)).Append("}\n");
                    Line(3, "alignment", (int)d.alignment);
                    Line(3, "pivot", d.pivot);
                    Line(3, "border", d.border);
                    Line(3, "tessellationDetail", d.tessellationDetail);
                    Line(3, "spriteID", d.spriteID ?? "");
                    if (d.outline != null && d.outline.Count > 0)
                    {
                        y.Append("      outline:\n");
                        foreach (var path in d.outline)
                        {
                            if (path == null || path.Length == 0) continue;
                            for (int j = 0; j < path.Length; j++)
                                y.Append(j == 0 ? "      - - " : "        - ").Append(Scalar(path[j])).Append('\n');
                        }
                    }
                }
            }
            return y.ToString();
        }

        static string Scalar(object value) => value switch
        {
            bool b => b ? "1" : "0",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint u => u.ToString(CultureInfo.InvariantCulture),
            Vector2 v => "{x: " + Scalar(v.x) + ", y: " + Scalar(v.y) + "}",
            Vector4 v => "{x: " + Scalar(v.x) + ", y: " + Scalar(v.y) + ", z: " + Scalar(v.z) + ", w: " + Scalar(v.w) + "}",
            string t => "\"" + t.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"",
            _ => throw new ArgumentException("no meta form for " + value),
        };
    }
}
