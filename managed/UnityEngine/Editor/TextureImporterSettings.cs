using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// A texture importer's settings as one object, as importers that make textures themselves
    /// keep them (the PSD importer serializes one into its .meta; TextureGenerator takes one).
    /// The fields are the serialized settings: a new object is mostly zero, with mipmaps and sRGB
    /// on, alpha from the input and the identity swizzle (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/toolapis settings.new).
    /// </summary>
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public sealed partial class TextureImporterSettings
    {
        /// <summary>The identity swizzle, one channel index per byte from R up.</summary>
        const uint k_IdentitySwizzle = 0x03020100;

        [SerializeField] int m_AlphaSource = (int)TextureImporterAlphaSource.FromInput;
        [SerializeField] int m_MipMapMode;
        [SerializeField] int m_EnableMipMap = 1;
        [SerializeField] int m_FadeOut;
        [SerializeField] int m_BorderMipMap;
        [SerializeField] int m_MipMapsPreserveCoverage;
        [SerializeField] float m_AlphaTestReferenceValue;
        [SerializeField] int m_MipMapFadeDistanceStart;
        [SerializeField] int m_MipMapFadeDistanceEnd;
        [SerializeField] int m_ConvertToNormalMap;
        [SerializeField] float m_HeightScale;
        [SerializeField] int m_NormalMapFilter;
        [SerializeField] int m_FlipGreenChannel;
        [SerializeField] uint m_Swizzle = k_IdentitySwizzle;
        [SerializeField] int m_IsReadable;
        [SerializeField] int m_StreamingMipmaps;
        [SerializeField] int m_StreamingMipmapsPriority;
        [SerializeField] int m_VTOnly;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("m_IgnoreMasterTextureLimit")] int m_IgnoreMipmapLimit;
        [SerializeField] int m_NPOTScale;
        [SerializeField] int m_SRGBTexture = 1;
        [SerializeField] int m_SpriteMode;
        [SerializeField] uint m_SpriteExtrude;
        [SerializeField] int m_SpriteMeshType;
        [SerializeField] int m_Alignment;
        [SerializeField] Vector2 m_SpritePivot;
        [SerializeField] float m_SpritePixelsToUnits;
        [SerializeField] Vector4 m_SpriteBorder;
        [SerializeField] int m_SpriteGenerateFallbackPhysicsShape;
        [SerializeField] int m_GenerateCubemap;
        [SerializeField] int m_CubemapConvolution;
        [SerializeField] int m_SeamlessCubemap;
        [SerializeField] int m_AlphaIsTransparency;
        [SerializeField] float m_SpriteTessellationDetail;
        [SerializeField] int m_TextureType;
        [SerializeField] int m_TextureShape;
        [SerializeField] int m_SingleChannelComponent;
        [SerializeField] int m_FlipbookRows;
        [SerializeField] int m_FlipbookColumns;
        [SerializeField] int m_IgnorePngGamma;
        [SerializeField] int m_CookieMode;
        [SerializeField] int m_FilterMode;
        [SerializeField] int m_Aniso;
        [SerializeField] float m_MipBias;
        [SerializeField] int m_WrapU;
        [SerializeField] int m_WrapV;
        [SerializeField] int m_WrapW;
        [SerializeField] int m_NormalMap;
        [SerializeField] int m_TextureFormat;
        [SerializeField] int m_MaxTextureSize;
        [SerializeField] int m_Lightmap;
        [SerializeField] int m_CompressionQuality;
        [SerializeField] int m_LinearTexture;
        [SerializeField] int m_GrayScaleToAlpha;
        [SerializeField] int m_RGBM;
        [SerializeField] int m_CubemapConvolutionSteps;
        [SerializeField] float m_CubemapConvolutionExponent;
        [SerializeField] int m_MaxTextureSizeSet;
        [SerializeField] int m_CompressionQualitySet;
        [SerializeField] int m_TextureFormatSet;
        [SerializeField] int m_ApplyGammaDecoding;

        public TextureImporterSettings() { }

        static readonly System.Reflection.FieldInfo[] s_Fields = typeof(TextureImporterSettings).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>Whether every setting of the two is the same.</summary>
        public static bool Equal(TextureImporterSettings a, TextureImporterSettings b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            foreach (var field in s_Fields)
                if (!Equals(field.GetValue(a), field.GetValue(b))) return false;
            return true;
        }

        public void CopyTo(TextureImporterSettings target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            foreach (var field in s_Fields)
                field.SetValue(target, field.GetValue(this));
        }

        [Obsolete("ApplyTextureType(TextureImporterType, bool) is deprecated, use ApplyTextureType(TextureImporterType)")]
        public void ApplyTextureType(TextureImporterType type, bool applyAll) => ApplyTextureType(type);

        /// <summary>
        /// Sets the settings a texture type implies and leaves the others as they are (matches
        /// the Unity 6000.0.71f1 Editor, tests/projects/toolapis settings.applyHigh/applyLow: each
        /// type applied to two objects whose settings differ both ways).
        /// </summary>
        public void ApplyTextureType(TextureImporterType type)
        {
            textureType = type;
            switch (type)
            {
                case TextureImporterType.NormalMap:
                    mipmapEnabled = true;
                    sRGBTexture = false;
                    alphaIsTransparency = false;
                    break;
                case TextureImporterType.GUI:
                    mipmapEnabled = false;
                    npotScale = TextureImporterNPOTScale.None;
                    aniso = 1;
                    wrapMode = TextureWrapMode.Clamp;
                    sRGBTexture = true;
                    alphaIsTransparency = true;
                    break;
                case TextureImporterType.Sprite:
                    mipmapEnabled = false;
                    npotScale = TextureImporterNPOTScale.None;
                    wrapMode = TextureWrapMode.Clamp;
                    sRGBTexture = true;
                    alphaIsTransparency = true;
                    spriteMode = (int)SpriteImportMode.Multiple;
                    spriteGenerateFallbackPhysicsShape = true;
                    break;
                case TextureImporterType.Cursor:
                    mipmapEnabled = false;
                    npotScale = TextureImporterNPOTScale.None;
                    aniso = 1;
                    wrapMode = TextureWrapMode.Clamp;
                    sRGBTexture = true;
                    readable = true;
                    alphaIsTransparency = true;
                    break;
                case TextureImporterType.Cookie:
                    textureShape = TextureImporterShape.Texture2D;
                    generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                    aniso = 0;
                    wrapMode = TextureWrapMode.Clamp;
                    borderMipmap = true;
                    break;
                case TextureImporterType.Lightmap:
                    alphaSource = TextureImporterAlphaSource.None;
                    npotScale = TextureImporterNPOTScale.ToNearest;
                    wrapMode = TextureWrapMode.Clamp;
                    alphaIsTransparency = false;
                    sRGBTexture = true;
                    break;
                case TextureImporterType.SingleChannel:
                    sRGBTexture = false;
                    break;
                case TextureImporterType.Shadowmask:
                    sRGBTexture = false;
                    alphaSource = TextureImporterAlphaSource.None;
                    npotScale = TextureImporterNPOTScale.ToNearest;
                    wrapMode = TextureWrapMode.Clamp;
                    alphaIsTransparency = false;
                    break;
                case TextureImporterType.DirectionalLightmap:
                    sRGBTexture = false;
                    alphaSource = TextureImporterAlphaSource.FromInput;
                    wrapMode = TextureWrapMode.Clamp;
                    alphaIsTransparency = false;
                    break;
            }
        }

        static bool B(int v) => v != 0;
        static int I(bool v) => v ? 1 : 0;

        public TextureImporterType textureType { get => (TextureImporterType)m_TextureType; set => m_TextureType = (int)value; }
        public TextureImporterShape textureShape { get => (TextureImporterShape)m_TextureShape; set => m_TextureShape = (int)value; }
        public TextureImporterMipFilter mipmapFilter { get => (TextureImporterMipFilter)m_MipMapMode; set => m_MipMapMode = (int)value; }
        public bool mipmapEnabled { get => B(m_EnableMipMap); set => m_EnableMipMap = I(value); }
        [Obsolete("Texture mips are now always generated in linear space")]
        [Unbound.EditorFaithful("the release's obsolete setter is ignored: mips are always generated in linear space")]
        public bool generateMipsInLinearSpace { get => true; set { } }
        public bool sRGBTexture { get => B(m_SRGBTexture); set => m_SRGBTexture = I(value); }
        public bool fadeOut { get => B(m_FadeOut); set => m_FadeOut = I(value); }
        public bool borderMipmap { get => B(m_BorderMipMap); set => m_BorderMipMap = I(value); }
        public bool mipMapsPreserveCoverage { get => B(m_MipMapsPreserveCoverage); set => m_MipMapsPreserveCoverage = I(value); }
        public float alphaTestReferenceValue { get => m_AlphaTestReferenceValue; set => m_AlphaTestReferenceValue = value; }
        public int mipmapFadeDistanceStart { get => m_MipMapFadeDistanceStart; set => m_MipMapFadeDistanceStart = value; }
        public int mipmapFadeDistanceEnd { get => m_MipMapFadeDistanceEnd; set => m_MipMapFadeDistanceEnd = value; }
        public bool convertToNormalMap { get => B(m_ConvertToNormalMap); set => m_ConvertToNormalMap = I(value); }
        public float heightmapScale { get => m_HeightScale; set => m_HeightScale = value; }
        public TextureImporterNormalFilter normalMapFilter { get => (TextureImporterNormalFilter)m_NormalMapFilter; set => m_NormalMapFilter = (int)value; }
        public bool flipGreenChannel { get => B(m_FlipGreenChannel); set => m_FlipGreenChannel = I(value); }
        public TextureImporterSwizzle swizzleR { get => Swizzle(0); set => Swizzle(0, value); }
        public TextureImporterSwizzle swizzleG { get => Swizzle(1); set => Swizzle(1, value); }
        public TextureImporterSwizzle swizzleB { get => Swizzle(2); set => Swizzle(2, value); }
        public TextureImporterSwizzle swizzleA { get => Swizzle(3); set => Swizzle(3, value); }
        internal uint swizzleRaw { get => m_Swizzle; set => m_Swizzle = value; }

        TextureImporterSwizzle Swizzle(int channel) => (TextureImporterSwizzle)((m_Swizzle >> (channel * 8)) & 0xFF);

        void Swizzle(int channel, TextureImporterSwizzle value) =>
            m_Swizzle = (m_Swizzle & ~(0xFFu << (channel * 8))) | (((uint)value & 0xFF) << (channel * 8));

        public TextureImporterAlphaSource alphaSource { get => (TextureImporterAlphaSource)m_AlphaSource; set => m_AlphaSource = (int)value; }
        public TextureImporterSingleChannelComponent singleChannelComponent { get => (TextureImporterSingleChannelComponent)m_SingleChannelComponent; set => m_SingleChannelComponent = (int)value; }
        public int flipbookRows { get => m_FlipbookRows; set => m_FlipbookRows = value; }
        public int flipbookColumns { get => m_FlipbookColumns; set => m_FlipbookColumns = value; }
        public bool readable { get => B(m_IsReadable); set => m_IsReadable = I(value); }
        public bool streamingMipmaps { get => B(m_StreamingMipmaps); set => m_StreamingMipmaps = I(value); }
        public int streamingMipmapsPriority { get => m_StreamingMipmapsPriority; set => m_StreamingMipmapsPriority = value; }
        public bool vtOnly { get => B(m_VTOnly); set => m_VTOnly = I(value); }
        public bool ignoreMipmapLimit { get => B(m_IgnoreMipmapLimit); set => m_IgnoreMipmapLimit = I(value); }
        public TextureImporterNPOTScale npotScale { get => (TextureImporterNPOTScale)m_NPOTScale; set => m_NPOTScale = (int)value; }
        public TextureImporterGenerateCubemap generateCubemap { get => (TextureImporterGenerateCubemap)m_GenerateCubemap; set => m_GenerateCubemap = (int)value; }
        public TextureImporterCubemapConvolution cubemapConvolution { get => (TextureImporterCubemapConvolution)m_CubemapConvolution; set => m_CubemapConvolution = (int)value; }
        public bool seamlessCubemap { get => B(m_SeamlessCubemap); set => m_SeamlessCubemap = I(value); }
        public FilterMode filterMode { get => (FilterMode)m_FilterMode; set => m_FilterMode = (int)value; }
        public int aniso { get => m_Aniso; set => m_Aniso = value; }
        public float mipmapBias { get => m_MipBias; set => m_MipBias = value; }
        /// <summary>The U axis's wrap mode; setting it sets every axis's.</summary>
        public TextureWrapMode wrapMode { get => (TextureWrapMode)m_WrapU; set => m_WrapU = m_WrapV = m_WrapW = (int)value; }
        public TextureWrapMode wrapModeU { get => (TextureWrapMode)m_WrapU; set => m_WrapU = (int)value; }
        public TextureWrapMode wrapModeV { get => (TextureWrapMode)m_WrapV; set => m_WrapV = (int)value; }
        public TextureWrapMode wrapModeW { get => (TextureWrapMode)m_WrapW; set => m_WrapW = (int)value; }
        public bool alphaIsTransparency { get => B(m_AlphaIsTransparency); set => m_AlphaIsTransparency = I(value); }
        public bool ignorePngGamma { get => B(m_IgnorePngGamma); set => m_IgnorePngGamma = I(value); }
        public int spriteMode { get => m_SpriteMode; set => m_SpriteMode = value; }
        public float spritePixelsPerUnit { get => m_SpritePixelsToUnits; set => m_SpritePixelsToUnits = value; }
        [Obsolete("Use spritePixelsPerUnit property instead.")]
        public float spritePixelsToUnits { get => m_SpritePixelsToUnits; set => m_SpritePixelsToUnits = value; }
        public float spriteTessellationDetail { get => m_SpriteTessellationDetail; set => m_SpriteTessellationDetail = value; }
        public uint spriteExtrude { get => m_SpriteExtrude; set => m_SpriteExtrude = value; }
        public SpriteMeshType spriteMeshType { get => (SpriteMeshType)m_SpriteMeshType; set => m_SpriteMeshType = (int)value; }
        public int spriteAlignment { get => m_Alignment; set => m_Alignment = value; }
        public Vector2 spritePivot { get => m_SpritePivot; set => m_SpritePivot = value; }
        public Vector4 spriteBorder { get => m_SpriteBorder; set => m_SpriteBorder = value; }
        public bool spriteGenerateFallbackPhysicsShape { get => B(m_SpriteGenerateFallbackPhysicsShape); set => m_SpriteGenerateFallbackPhysicsShape = I(value); }

        // The obsolete views read the current settings (tests/projects/toolapis: linearTexture is
        // never sRGBTexture, normalMap and lightmap follow textureType, grayscaleToAlpha the alpha
        // source). Their setters are unmeasured: each sets the setting it reads.
        [Obsolete("Use sRGBTexture instead")]
        public bool linearTexture { get => !sRGBTexture; set => sRGBTexture = !value; }
        [Obsolete("Check importer.textureType against TextureImporterType.NormalMap instead. Getter will work as expected. Setter will set textureType to NormalMap if true, nothing otherwise")]
        public bool normalMap { get => textureType == TextureImporterType.NormalMap; set { if (value) textureType = TextureImporterType.NormalMap; } }
        [Obsolete("Texture format can only be overridden on a per platform basis. See [[TextureImporterPlatformSettings]]")]
        public TextureImporterFormat textureFormat { get => (TextureImporterFormat)m_TextureFormat; set => m_TextureFormat = (int)value; }
        [Obsolete("Texture max size can only be overridden on a per platform basis. See [[TextureImporter.maxTextureSize]] for Default platform or [[TextureImporterPlatformSettings]]")]
        public int maxTextureSize { get => m_MaxTextureSize; set => m_MaxTextureSize = value; }
        [Obsolete("Check importer.textureType against TextureImporterType.Lightmap instead. Getter will work as expected. Setter will set textureType to Lightmap if true, nothing otherwise.")]
        public bool lightmap { get => textureType == TextureImporterType.Lightmap; set { if (value) textureType = TextureImporterType.Lightmap; } }
        [Obsolete("RGBM is no longer a user's choice but has become an implementation detail hidden to the user.")]
        public TextureImporterRGBMMode rgbm { get => (TextureImporterRGBMMode)m_RGBM; set => m_RGBM = (int)value; }
        [Obsolete("Use UnityEditor.TextureImporter.alphaSource instead")]
        public bool grayscaleToAlpha
        {
            get => alphaSource == TextureImporterAlphaSource.FromGrayScale;
            set => alphaSource = value ? TextureImporterAlphaSource.FromGrayScale : TextureImporterAlphaSource.FromInput;
        }
        [Obsolete("Not used anymore. The right values are automatically picked by the importer.")]
        public int cubemapConvolutionSteps { get => m_CubemapConvolutionSteps; set => m_CubemapConvolutionSteps = value; }
        [Obsolete("Not used anymore. The right values are automatically picked by the importer.")]
        public float cubemapConvolutionExponent { get => m_CubemapConvolutionExponent; set => m_CubemapConvolutionExponent = value; }
        [Obsolete("Texture compression can only be overridden on a per platform basis. See [[TextureImporter.compressionQuality]] for Default platform or [[TextureImporterPlatformSettings]]")]
        public int compressionQuality { get => m_CompressionQuality; set => m_CompressionQuality = value; }
    }

    /// <summary>
    /// A texture importer's settings for one platform. A new object is the default platform's:
    /// 2048 pixels at most, Mitchell resizing, an automatic compressed format of quality 50
    /// (matches the Unity 6000.0.71f1 Editor, tests/projects/toolapis platform.new).
    /// </summary>
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public sealed partial class TextureImporterPlatformSettings
    {
        /// <summary>The name of the settings every platform without its own takes.</summary>
        internal const string DefaultPlatformName = "DefaultTexturePlatform";

        [SerializeField] string m_Name = DefaultPlatformName;
        [SerializeField] int m_Overridden;
        [SerializeField] int m_IgnorePlatformSupport;
        [SerializeField] int m_MaxTextureSize = 2048;
        [SerializeField] int m_ResizeAlgorithm;
        [SerializeField] int m_TextureFormat = (int)TextureImporterFormat.Automatic;
        [SerializeField] int m_TextureCompression = (int)TextureImporterCompression.Compressed;
        [SerializeField] int m_CompressionQuality = 50;
        [SerializeField] internal int m_ForceMaximumCompressionQuality_BC6H_BC7;
        [SerializeField] int m_CrunchedCompression;
        [SerializeField] int m_AllowsAlphaSplitting;
        [SerializeField] int m_AndroidETC2FallbackOverride;

        public TextureImporterPlatformSettings() { }

        public void CopyTo(TextureImporterPlatformSettings target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            target.m_Name = m_Name;
            target.m_Overridden = m_Overridden;
            target.m_IgnorePlatformSupport = m_IgnorePlatformSupport;
            target.m_MaxTextureSize = m_MaxTextureSize;
            target.m_ResizeAlgorithm = m_ResizeAlgorithm;
            target.m_TextureFormat = m_TextureFormat;
            target.m_TextureCompression = m_TextureCompression;
            target.m_CompressionQuality = m_CompressionQuality;
            target.m_ForceMaximumCompressionQuality_BC6H_BC7 = m_ForceMaximumCompressionQuality_BC6H_BC7;
            target.m_CrunchedCompression = m_CrunchedCompression;
            target.m_AllowsAlphaSplitting = m_AllowsAlphaSplitting;
            target.m_AndroidETC2FallbackOverride = m_AndroidETC2FallbackOverride;
        }

        public string name { get => m_Name; set => m_Name = value; }
        public bool overridden { get => m_Overridden != 0; set => m_Overridden = value ? 1 : 0; }
        public bool ignorePlatformSupport { get => m_IgnorePlatformSupport != 0; set => m_IgnorePlatformSupport = value ? 1 : 0; }
        public int maxTextureSize { get => m_MaxTextureSize; set => m_MaxTextureSize = value; }
        public TextureResizeAlgorithm resizeAlgorithm { get => (TextureResizeAlgorithm)m_ResizeAlgorithm; set => m_ResizeAlgorithm = (int)value; }
        public TextureImporterFormat format { get => (TextureImporterFormat)m_TextureFormat; set => m_TextureFormat = (int)value; }
        public TextureImporterCompression textureCompression { get => (TextureImporterCompression)m_TextureCompression; set => m_TextureCompression = (int)value; }
        public int compressionQuality { get => m_CompressionQuality; set => m_CompressionQuality = value; }
        internal int forceMaximumCompressionQuality_BC6H_BC7 { get => m_ForceMaximumCompressionQuality_BC6H_BC7; set => m_ForceMaximumCompressionQuality_BC6H_BC7 = value; }
        public bool crunchedCompression { get => m_CrunchedCompression != 0; set => m_CrunchedCompression = value ? 1 : 0; }
        public bool allowsAlphaSplitting { get => m_AllowsAlphaSplitting != 0; set => m_AllowsAlphaSplitting = value ? 1 : 0; }
        public AndroidETC2FallbackOverride androidETC2FallbackOverride { get => (AndroidETC2FallbackOverride)m_AndroidETC2FallbackOverride; set => m_AndroidETC2FallbackOverride = (int)value; }

        const string StandalonePlatformName = "Standalone";

        // Canonical texture platform serialization names and lookup aliases for Unity 6000.0
        // (tests/projects/spriteatlaseditor v1platform/v2platform).
        static readonly Dictionary<string, (int TargetId, string CanonicalName, bool IsStandalone)> s_PlatformTargets =
            new Dictionary<string, (int, string, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                // Standalone desktop targets share the "Standalone" texture platform.
                ["Standalone"] = (1, StandalonePlatformName, true),
                ["StandaloneOSX"] = (2, StandalonePlatformName, true),
                ["OSXUniversal"] = (2, StandalonePlatformName, true),
                ["StandaloneWindows"] = (5, StandalonePlatformName, true),
                ["Win"] = (5, StandalonePlatformName, true),
                ["StandaloneWindows64"] = (19, StandalonePlatformName, true),
                ["Win64"] = (19, StandalonePlatformName, true),
                ["StandaloneLinux64"] = (24, StandalonePlatformName, true),
                ["Linux64"] = (24, StandalonePlatformName, true),
                // Non-standalone targets resolve to their canonical platform name.
                ["iOS"] = (9, "iOS", false),
                ["iPhone"] = (9, "iOS", false),
                ["Android"] = (13, "Android", false),
                ["WebGL"] = (20, "WebGL", false),
                ["Web"] = (20, "WebGL", false),
                ["WSAPlayer"] = (21, "WindowsStoreApps", false),
                ["WindowsStoreApps"] = (21, "WindowsStoreApps", false),
                ["Windows Store Apps"] = (21, "WindowsStoreApps", false),
                ["WSA"] = (21, "WindowsStoreApps", false),
                ["PS4"] = (31, "PS4", false),
                ["XboxOne"] = (33, "XboxOne", false),
                ["tvOS"] = (37, "tvOS", false),
                ["AppleTV"] = (37, "tvOS", false),
                ["Switch"] = (38, "Switch", false),
                ["CloudRendering"] = (41, "CloudRendering", false),
                ["LinuxHeadlessSimulation"] = (41, "CloudRendering", false),
                ["GameCoreScarlett"] = (42, "GameCoreScarlett", false),
                ["GameCoreXboxSeries"] = (42, "GameCoreScarlett", false),
                ["GameCoreXboxOne"] = (43, "GameCoreXboxOne", false),
                ["PS5"] = (44, "PS5", false),
                ["EmbeddedLinux"] = (45, "EmbeddedLinux", false),
                ["QNX"] = (46, "QNX", false),
                ["VisionOS"] = (47, "VisionOS", false),
                ["Switch2"] = (48, "Switch2", false),
                ["Kepler"] = (49, "Kepler", false),
                // Legacy/deprecated build target names kept for older serialized assets.
                ["StandaloneOSXIntel"] = (4, "OSXIntel", false),
                ["OSXIntel"] = (4, "OSXIntel", false),
                ["WebStreamed"] = (7, "WebStreamed", false),
                ["PS3"] = (10, "PS3", false),
                ["Linux"] = (17, "Linux", false),
                ["LinuxUniversal"] = (25, "LinuxUniversal", false),
                ["WP8"] = (26, "WP8", false),
                ["OSXIntel64"] = (27, "OSXIntel64", false),
                ["BlackBerry"] = (28, "BlackBerry", false),
                ["Tizen"] = (29, "Tizen", false),
                ["PSP2"] = (30, "PSP2", false),
                ["PSM"] = (32, "PSM", false),
                ["SamsungTV"] = (34, "SamsungTV", false),
                ["N3DS"] = (35, "N3DS", false),
                ["WiiU"] = (36, "WiiU", false),
                ["Lumin"] = (39, "Lumin", false),
                ["Stadia"] = (40, "Stadia", false),
            };

        // BuildTargetGroup names upgraded to build target names in serializedVersion 3 entries.
        static readonly Dictionary<string, string> s_BuildTargetGroupToPlatformName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["iPhone"] = "iOS",
                ["iOS"] = "iOS",
                ["Android"] = "Android",
                ["WebGL"] = "WebGL",
                ["Windows Store Apps"] = "WindowsStoreApps",
                ["WSA"] = "WindowsStoreApps",
                ["Metro"] = "WindowsStoreApps",
                ["PS4"] = "PS4",
                ["PS5"] = "PS5",
                ["XboxOne"] = "XboxOne",
                ["tvOS"] = "tvOS",
                ["VisionOS"] = "VisionOS",
                ["Nintendo Switch"] = "Switch",
                ["Switch"] = "Switch",
                ["Nintendo Switch 2"] = "Switch2",
                ["Switch2"] = "Switch2",
                ["LinuxHeadlessSimulation"] = "CloudRendering",
                ["CloudRendering"] = "CloudRendering",
                ["GameCoreScarlett"] = "GameCoreScarlett",
                ["GameCoreXboxSeries"] = "GameCoreScarlett",
                ["GameCoreXboxOne"] = "GameCoreXboxOne",
                ["EmbeddedLinux"] = "EmbeddedLinux",
                ["QNX"] = "QNX",
                ["Kepler"] = "Kepler",
            };

        internal static string NormalizePlatformName(string platformName)
        {
            if (string.IsNullOrEmpty(platformName)) return "";
            return s_PlatformTargets.TryGetValue(platformName, out var info) ? info.CanonicalName : platformName;
        }

        static bool MatchesPlatform(string storedName, string normalizedQuery)
        {
            if (string.Equals(normalizedQuery, StandalonePlatformName, StringComparison.OrdinalIgnoreCase))
                return string.Equals(storedName, StandalonePlatformName, StringComparison.Ordinal);
            if (s_PlatformTargets.TryGetValue(normalizedQuery, out var queryInfo))
            {
                if (queryInfo.IsStandalone)
                    return string.Equals(storedName, StandalonePlatformName, StringComparison.Ordinal);
                return storedName != null
                    && s_PlatformTargets.TryGetValue(storedName, out var storedInfo)
                    && storedInfo.TargetId == queryInfo.TargetId;
            }
            return string.Equals(storedName, normalizedQuery, StringComparison.Ordinal);
        }

        internal static List<TextureImporterPlatformSettings> ReadList(YNode listNode, bool prefixed)
        {
            if (listNode == null || listNode.Kind != YKind.Seq || listNode.Items.Length == 0)
                return null;
            var entries = new List<TextureImporterPlatformSettings>(listNode.Items.Length);
            foreach (var item in listNode.Items)
            {
                if (item == null || item.Kind != YKind.Map) continue;
                entries.Add(ReadEntry(item, prefixed));
            }
            return entries.Count > 0 ? entries : null;
        }

        static TextureImporterPlatformSettings ReadEntry(YNode node, bool prefixed)
        {
            var settings = new TextureImporterPlatformSettings();
            int version = node.Int("serializedVersion", 1);

            string buildTargetKey = prefixed ? "m_BuildTarget" : "buildTarget";
            string name = node.Get(buildTargetKey) != null ? node.Str(buildTargetKey, "") : DefaultPlatformName;
            if (version == 3
                && !string.Equals(name, StandalonePlatformName, StringComparison.OrdinalIgnoreCase)
                && s_BuildTargetGroupToPlatformName.TryGetValue(name, out var upgraded))
            {
                name = upgraded;
            }
            string serializedName = NormalizePlatformName(name);
            if (!string.Equals(serializedName, StandalonePlatformName, StringComparison.OrdinalIgnoreCase))
                name = serializedName;
            settings.m_Name = name;

            settings.m_MaxTextureSize = node.Int(prefixed ? "m_MaxTextureSize" : "maxTextureSize", settings.m_MaxTextureSize);
            settings.m_ResizeAlgorithm = node.Int(prefixed ? "m_ResizeAlgorithm" : "resizeAlgorithm", settings.m_ResizeAlgorithm);

            int textureFormat = node.Int(prefixed ? "m_TextureFormat" : "textureFormat", settings.m_TextureFormat);
            if (version <= 1)
            {
                if (textureFormat == 35) textureFormat = 34;
                else if (textureFormat == 36) textureFormat = 47;
            }
            settings.m_TextureFormat = textureFormat;

            settings.m_TextureCompression = node.Int(prefixed ? "m_TextureCompression" : "textureCompression", settings.m_TextureCompression);
            settings.m_CompressionQuality = node.Int(prefixed ? "m_CompressionQuality" : "compressionQuality", settings.m_CompressionQuality);
            settings.m_CrunchedCompression = node.Bool(prefixed ? "m_CrunchedCompression" : "crunchedCompression", settings.m_CrunchedCompression != 0) ? 1 : 0;
            settings.m_AllowsAlphaSplitting = node.Bool(prefixed ? "m_AllowsAlphaSplitting" : "allowsAlphaSplitting", settings.m_AllowsAlphaSplitting != 0) ? 1 : 0;
            settings.m_Overridden = node.Bool(prefixed ? "m_Overridden" : "overridden", settings.m_Overridden != 0) ? 1 : 0;
            settings.m_IgnorePlatformSupport = node.Bool(prefixed ? "m_IgnorePlatformSupport" : "ignorePlatformSupport", settings.m_IgnorePlatformSupport != 0) ? 1 : 0;
            settings.m_AndroidETC2FallbackOverride = node.Int(prefixed ? "m_AndroidETC2FallbackOverride" : "androidETC2FallbackOverride", settings.m_AndroidETC2FallbackOverride);

            if (version <= 2)
            {
                settings.m_ForceMaximumCompressionQuality_BC6H_BC7 = 1;
                if (textureFormat is 24 or 25)
                    settings.m_CompressionQuality = 100;
            }
            else
            {
                settings.m_ForceMaximumCompressionQuality_BC6H_BC7 =
                    node.Bool(prefixed ? "m_ForceMaximumCompressionQuality_BC6H_BC7" : "forceMaximumCompressionQuality_BC6H_BC7", false) ? 1 : 0;
            }
            return settings;
        }

        internal static TextureImporterPlatformSettings FindOrDefault(List<TextureImporterPlatformSettings> entries, string buildTarget)
        {
            string canonical = NormalizePlatformName(buildTarget);
            var result = new TextureImporterPlatformSettings();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (MatchesPlatform(entry.m_Name, canonical))
                    {
                        entry.CopyTo(result);
                        if ((result.m_TextureFormat is 24 or 25) && result.m_ForceMaximumCompressionQuality_BC6H_BC7 != 0)
                            result.m_CompressionQuality = 100;
                        return result;
                    }
                }
            }
            result.m_Name = canonical;
            return result;
        }
    }
}
