// Enums of the texture importer API, as the release's reference assemblies declare them
// (Unbound.ApiTools.StubGenerator, Unity 6000.0).

namespace UnityEditor
{
    public enum TextureImporterType
    {
        Default = 0,
        NormalMap = 1,
        GUI = 2,
        Sprite = 8,
        Cursor = 7,
        Cookie = 4,
        Lightmap = 6,
        SingleChannel = 10,
        Shadowmask = 11,
        DirectionalLightmap = 12,
        [global::System.Obsolete("Use Default (UnityUpgradable) -> Default", true)] Image = -2147483648,
        [global::System.Obsolete("Use NormalMap (UnityUpgradable) -> NormalMap", true)] Bump = -1,
        [global::System.Obsolete("Use importer.textureShape = TextureImporterShape.TextureCube", true)] Cubemap = -3,
        [global::System.Obsolete("Use a texture setup as a cubemap with glossy reflection instead", true)] Reflection = -3,
        [global::System.Obsolete("Use Default instead. All texture types now have an Advanced foldout (UnityUpgradable) -> Default", true)] Advanced = -5,
        [global::System.Obsolete("HDRI is not supported anymore", true)] HDRI = -9,
    }

    [global::System.Flags]
    public enum TextureImporterShape
    {
        Texture2D = 1,
        TextureCube = 2,
        Texture2DArray = 4,
        Texture3D = 8,
    }

    public enum TextureImporterMipFilter
    {
        BoxFilter = 0,
        KaiserFilter = 1,
    }

    public enum TextureImporterNormalFilter
    {
        Standard = 0,
        Sobel = 1,
    }

    public enum TextureImporterSwizzle
    {
        R = 0,
        G = 1,
        B = 2,
        A = 3,
        OneMinusR = 4,
        OneMinusG = 5,
        OneMinusB = 6,
        OneMinusA = 7,
        Zero = 8,
        One = 9,
    }

    public enum TextureImporterAlphaSource
    {
        None = 0,
        FromInput = 1,
        FromGrayScale = 2,
    }

    public enum TextureImporterSingleChannelComponent
    {
        Alpha = 0,
        Red = 1,
    }

    public enum TextureImporterNPOTScale
    {
        None = 0,
        ToNearest = 1,
        ToLarger = 2,
        ToSmaller = 3,
    }

    public enum TextureImporterGenerateCubemap
    {
        [global::System.Obsolete("This value is deprecated (use TextureImporter.textureShape instead).")] None = 0,
        Spheremap = 1,
        Cylindrical = 2,
        [global::System.Obsolete("Obscure shperemap modes are not supported any longer (use TextureImporterGenerateCubemap.Spheremap instead).")] SimpleSpheremap = 3,
        [global::System.Obsolete("Obscure shperemap modes are not supported any longer (use TextureImporterGenerateCubemap.Spheremap instead).")] NiceSpheremap = 4,
        FullCubemap = 5,
        AutoCubemap = 6,
    }

    public enum TextureImporterCubemapConvolution
    {
        None = 0,
        Specular = 1,
        Diffuse = 2,
    }

    public enum TextureImporterFormat
    {
        Automatic = -1,
        [global::System.Obsolete("Enum member TextureImporterFormat.AutomaticCompressed is obsolete. Use the TextureImporter.textureCompression property instead.", true)] AutomaticCompressed = -1,
        [global::System.Obsolete("Enum member TextureImporterFormat.Automatic16bit is obsolete. Use TextureImporter.textureCompression property instead.", true)] Automatic16bit = -2,
        [global::System.Obsolete("Enum member TextureImporterFormat.AutomaticTruecolor is obsolete. Use the TextureImporter.textureCompression property instead.", true)] AutomaticTruecolor = -3,
        [global::System.Obsolete("Enum member TextureImporterFormat.AutomaticCrunched is obsolete. Use the TextureImporter.crunchedCompression property instead.", true)] AutomaticCrunched = -5,
        [global::System.Obsolete("Enum member TextureImporterFormat.AutomaticHDR is obsolete. HDR is handled automatically now.", true)] AutomaticHDR = -6,
        [global::System.Obsolete("Enum member TextureImporterFormat.AutomaticCompressedHDR is obsolete. HDR is handled automatically now.", true)] AutomaticCompressedHDR = -7,
        DXT1 = 10,
        DXT5 = 12,
        RGB16 = 7,
        RGB24 = 3,
        Alpha8 = 1,
        R16 = 9,
        R8 = 63,
        RG16 = 62,
        ARGB16 = 2,
        RGBA32 = 4,
        ARGB32 = 5,
        RGBA16 = 13,
        RHalf = 15,
        RGHalf = 16,
        RGBAHalf = 17,
        RFloat = 18,
        RGFloat = 19,
        RGBAFloat = 20,
        RGB9E5 = 22,
        BC4 = 26,
        BC5 = 27,
        BC6H = 24,
        BC7 = 25,
        DXT1Crunched = 28,
        DXT5Crunched = 29,
        PVRTC_RGB2 = 30,
        PVRTC_RGBA2 = 31,
        PVRTC_RGB4 = 32,
        PVRTC_RGBA4 = 33,
        ETC_RGB4 = 34,
        [global::System.Obsolete("Enum member ATC_RGB4 is obsolete. Use ETC_RGB4 (UnityUpgradable) -> ETC_RGB4", true)] ATC_RGB4 = 35,
        [global::System.Obsolete("Enum member ATC_RGBA8 is obsolete. Use ETC2_RGBA8 (UnityUpgradable) -> ETC2_RGBA8", true)] ATC_RGBA8 = 36,
        EAC_R = 41,
        EAC_R_SIGNED = 42,
        EAC_RG = 43,
        EAC_RG_SIGNED = 44,
        ETC2_RGB4 = 45,
        ETC2_RGB4_PUNCHTHROUGH_ALPHA = 46,
        ETC2_RGBA8 = 47,
        ASTC_4x4 = 48,
        ASTC_5x5 = 49,
        ASTC_6x6 = 50,
        ASTC_8x8 = 51,
        ASTC_10x10 = 52,
        ASTC_12x12 = 53,
        [global::System.Obsolete("Enum member ASTC_RGB_4x4 is obsolete. Use ASTC_4x4 (UnityUpgradable) -> ASTC_4x4", true)] ASTC_RGB_4x4 = -48,
        [global::System.Obsolete("Enum member ASTC_RGB_5x5 is obsolete. Use ASTC_5x5 (UnityUpgradable) -> ASTC_5x5", true)] ASTC_RGB_5x5 = -49,
        [global::System.Obsolete("Enum member ASTC_RGB_6x6 is obsolete. Use ASTC_6x6 (UnityUpgradable) -> ASTC_6x6", true)] ASTC_RGB_6x6 = -50,
        [global::System.Obsolete("Enum member ASTC_RGB_8x8 is obsolete. Use ASTC_8x8 (UnityUpgradable) -> ASTC_8x8", true)] ASTC_RGB_8x8 = -51,
        [global::System.Obsolete("Enum member ASTC_RGB_10x10 is obsolete. Use ASTC_10x10 (UnityUpgradable) -> ASTC_10x10", true)] ASTC_RGB_10x10 = -52,
        [global::System.Obsolete("Enum member ASTC_RGB_12x12 is obsolete. Use ASTC_12x12 (UnityUpgradable) -> ASTC_12x12", true)] ASTC_RGB_12x12 = -53,
        [global::System.Obsolete("Enum member ASTC_RGBA_4x4 is obsolete. Use ASTC_4x4 (UnityUpgradable) -> ASTC_4x4", true)] ASTC_RGBA_4x4 = -54,
        [global::System.Obsolete("Enum member ASTC_RGBA_5x5 is obsolete. Use ASTC_5x5 (UnityUpgradable) -> ASTC_5x5", true)] ASTC_RGBA_5x5 = -55,
        [global::System.Obsolete("Enum member ASTC_RGBA_6x6 is obsolete. Use ASTC_6x6 (UnityUpgradable) -> ASTC_6x6", true)] ASTC_RGBA_6x6 = -56,
        [global::System.Obsolete("Enum member ASTC_RGBA_8x8 is obsolete. Use ASTC_8x8 (UnityUpgradable) -> ASTC_8x8", true)] ASTC_RGBA_8x8 = -57,
        [global::System.Obsolete("Enum member ASTC_RGBA_10x10 is obsolete. Use ASTC_10x10 (UnityUpgradable) -> ASTC_10x10", true)] ASTC_RGBA_10x10 = -58,
        [global::System.Obsolete("Enum member ASTC_RGBA_12x12 is obsolete. Use ASTC_12x12 (UnityUpgradable) -> ASTC_12x12", true)] ASTC_RGBA_12x12 = -59,
        [global::System.Obsolete("Enum member ETC_RGB4_3DS is obsolete. Nintendo 3DS is no longer supported.", true)] ETC_RGB4_3DS = -60,
        [global::System.Obsolete("Enum member ETC_RGBA8_3DS is obsolete. Nintendo 3DS is no longer supported.", true)] ETC_RGBA8_3DS = -61,
        ETC_RGB4Crunched = 64,
        ETC2_RGBA8Crunched = 65,
        ASTC_HDR_4x4 = 66,
        ASTC_HDR_5x5 = 67,
        ASTC_HDR_6x6 = 68,
        ASTC_HDR_8x8 = 69,
        ASTC_HDR_10x10 = 70,
        ASTC_HDR_12x12 = 71,
        RG32 = 72,
        RGB48 = 73,
        RGBA64 = 74,
        R8_SIGNED = 75,
        RG16_SIGNED = 76,
        RGB24_SIGNED = 77,
        RGBA32_SIGNED = 78,
        R16_SIGNED = 79,
        RG32_SIGNED = 80,
        RGB48_SIGNED = 81,
        RGBA64_SIGNED = 82,
    }

    public enum TextureImporterRGBMMode
    {
        Auto = 0,
        On = 1,
        Off = 2,
        Encoded = 3,
    }

    public enum TextureResizeAlgorithm
    {
        Mitchell = 0,
        Bilinear = 1,
    }

    public enum TextureImporterCompression
    {
        Uncompressed = 0,
        Compressed = 1,
        CompressedHQ = 2,
        CompressedLQ = 3,
    }

    public enum AndroidETC2FallbackOverride
    {
        UseBuildSettings = 0,
        Quality32Bit = 1,
        Quality16Bit = 2,
        Quality32BitDownscaled = 3,
    }

    public enum SpriteImportMode
    {
        None = 0,
        Single = 1,
        Multiple = 2,
        Polygon = 3,
    }
}
