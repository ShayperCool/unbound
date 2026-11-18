using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>
/// Editor tooling APIs that 2D content relies on, recorded in Play mode:
/// TextureGenerator.GenerateTexture (settings defaults per texture type, generated textures,
/// sprites and the postprocessor hooks it runs), the PSD importer package importing Layers.psd,
/// a Material's m_SavedProperties read and written through SerializedObject, and USS var()
/// references whose value has the wrong type for the property. See README.md.
/// </summary>
[InitializeOnLoad]
public static class ToolApisProbe
{
    const string Variable = "TOOL_APIS_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();
    static int s_Frames;
    static VisualElement s_UssRoot;

    static ToolApisProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V2(Vector2 v) => "(" + F(v.x) + "," + F(v.y) + ")";
    static string V4(Vector4 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + ")";
    static string R(Rect r) => "(" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height) + ")";
    static string C(Color c) => "(" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + ")";

    static void Put(string key, object value)
    {
        var text = value switch
        {
            null => "null",
            // Enums as numbers: several of these enums give one value two names.
            Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            float f => F(f),
            bool b => b ? "true" : "false",
            Vector2 v => V2(v),
            Vector4 v => V4(v),
            Rect r => R(r),
            Color c => C(c),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"")
            .Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\"}\n");
    }

    static string Hash(byte[] bytes)
    {
        ulong h = 14695981039346656037UL;
        foreach (var b in bytes) { h ^= b; h *= 1099511628211UL; }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    static void Guard(string key, Action action)
    {
        try { action(); }
        catch (Exception e) { Put(key + ".threw", e.GetType().FullName + ": " + e.Message); }
    }

    static string TakeLogs()
    {
        var text = string.Join(" | ", s_Logs);
        s_Logs.Clear();
        return text;
    }

    static void Run()
    {
        Application.logMessageReceived += (condition, stack, type) => s_Logs.Add(type + ": " + condition.Split('\n')[0]);
        Guard("defaults", Defaults);
        Guard("generate", Generate);
        Guard("postprocessor", Postprocessor);
        Guard("errors", Errors);
        Guard("platforms", Platforms);
        Guard("importer", ImporterIdentity);
        Guard("psd", Psd);
        Guard("material", MaterialProperties);
        Guard("matx", MaterialSheets);
        Guard("imported", ImportedFlags);
        Guard("uss", UssSetup);
        EditorApplication.update += WaitForStyles;
    }

    static void WaitForStyles()
    {
        if (++s_Frames < 5) return;
        EditorApplication.update -= WaitForStyles;
        Guard("uss", UssRead);
        Put("playRequest.seen", string.Join(" | ", ToolApisPlayRequest.Seen));
        Put("logs.end", TakeLogs());
        s_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    // ------------------------------------------------------------------ settings defaults

    static readonly TextureImporterType[] s_Types =
    {
        TextureImporterType.Default, TextureImporterType.NormalMap, TextureImporterType.GUI, TextureImporterType.Sprite,
        TextureImporterType.Cursor, TextureImporterType.Cookie, TextureImporterType.Lightmap, TextureImporterType.SingleChannel,
        TextureImporterType.Shadowmask, TextureImporterType.DirectionalLightmap,
    };

#pragma warning disable 618
    static void ImporterSettings(string key, TextureImporterSettings s)
    {
        if (s == null) { Put(key, null); return; }
        Put(key + ".textureType", s.textureType); Put(key + ".textureShape", s.textureShape);
        Put(key + ".mipmapFilter", s.mipmapFilter); Put(key + ".mipmapEnabled", s.mipmapEnabled);
        Put(key + ".sRGBTexture", s.sRGBTexture); Put(key + ".fadeOut", s.fadeOut); Put(key + ".borderMipmap", s.borderMipmap);
        Put(key + ".mipMapsPreserveCoverage", s.mipMapsPreserveCoverage); Put(key + ".alphaTestReferenceValue", s.alphaTestReferenceValue);
        Put(key + ".mipmapFadeDistanceStart", s.mipmapFadeDistanceStart); Put(key + ".mipmapFadeDistanceEnd", s.mipmapFadeDistanceEnd);
        Put(key + ".convertToNormalMap", s.convertToNormalMap); Put(key + ".heightmapScale", s.heightmapScale);
        Put(key + ".normalMapFilter", s.normalMapFilter); Put(key + ".flipGreenChannel", s.flipGreenChannel);
        Put(key + ".swizzle", $"{(int)s.swizzleR},{(int)s.swizzleG},{(int)s.swizzleB},{(int)s.swizzleA}");
        Put(key + ".alphaSource", s.alphaSource); Put(key + ".singleChannelComponent", s.singleChannelComponent);
        Put(key + ".flipbook", s.flipbookRows + "x" + s.flipbookColumns); Put(key + ".readable", s.readable);
        Put(key + ".streamingMipmaps", s.streamingMipmaps); Put(key + ".streamingMipmapsPriority", s.streamingMipmapsPriority);
        Put(key + ".vtOnly", s.vtOnly); Put(key + ".ignoreMipmapLimit", s.ignoreMipmapLimit); Put(key + ".npotScale", s.npotScale);
        Put(key + ".generateCubemap", s.generateCubemap); Put(key + ".cubemapConvolution", s.cubemapConvolution);
        Put(key + ".seamlessCubemap", s.seamlessCubemap); Put(key + ".filterMode", s.filterMode); Put(key + ".aniso", s.aniso);
        Put(key + ".mipmapBias", s.mipmapBias);
        Put(key + ".wrap", $"{(int)s.wrapMode},{(int)s.wrapModeU},{(int)s.wrapModeV},{(int)s.wrapModeW}");
        Put(key + ".alphaIsTransparency", s.alphaIsTransparency); Put(key + ".ignorePngGamma", s.ignorePngGamma);
        Put(key + ".spriteMode", s.spriteMode); Put(key + ".spritePixelsPerUnit", s.spritePixelsPerUnit);
        Put(key + ".spriteTessellationDetail", s.spriteTessellationDetail); Put(key + ".spriteExtrude", s.spriteExtrude);
        Put(key + ".spriteMeshType", s.spriteMeshType); Put(key + ".spriteAlignment", s.spriteAlignment);
        Put(key + ".spritePivot", s.spritePivot); Put(key + ".spriteBorder", s.spriteBorder);
        Put(key + ".spriteGenerateFallbackPhysicsShape", s.spriteGenerateFallbackPhysicsShape);
        Put(key + ".linearTexture", s.linearTexture); Put(key + ".normalMap", s.normalMap); Put(key + ".lightmap", s.lightmap);
        Put(key + ".textureFormat", s.textureFormat); Put(key + ".maxTextureSize", s.maxTextureSize);
        Put(key + ".compressionQuality", s.compressionQuality); Put(key + ".grayscaleToAlpha", s.grayscaleToAlpha);
        Put(key + ".rgbm", s.rgbm); Put(key + ".cubemapConvolutionSteps", s.cubemapConvolutionSteps);
        Put(key + ".cubemapConvolutionExponent", s.cubemapConvolutionExponent);
    }
#pragma warning restore 618

    static void PlatformSettings(string key, TextureImporterPlatformSettings p)
    {
        if (p == null) { Put(key, null); return; }
        Put(key + ".name", p.name); Put(key + ".overridden", p.overridden); Put(key + ".ignorePlatformSupport", p.ignorePlatformSupport);
        Put(key + ".maxTextureSize", p.maxTextureSize); Put(key + ".resizeAlgorithm", p.resizeAlgorithm); Put(key + ".format", p.format);
        Put(key + ".textureCompression", p.textureCompression); Put(key + ".compressionQuality", p.compressionQuality);
        Put(key + ".crunchedCompression", p.crunchedCompression); Put(key + ".allowsAlphaSplitting", p.allowsAlphaSplitting);
        Put(key + ".androidETC2FallbackOverride", p.androidETC2FallbackOverride);
    }

    static void GenerationSettings(string key, TextureGenerationSettings g)
    {
        Put(key + ".assetPath", g.assetPath); Put(key + ".qualifyForSpritePacking", g.qualifyForSpritePacking);
        Put(key + ".enablePostProcessor", g.enablePostProcessor); Put(key + ".spritePackingTag", g.spritePackingTag);
        Put(key + ".spriteImportData", g.spriteImportData == null ? "null" : g.spriteImportData.Length.ToString(CultureInfo.InvariantCulture));
        Put(key + ".secondarySpriteTextures", g.secondarySpriteTextures == null ? "null" : g.secondarySpriteTextures.Length.ToString(CultureInfo.InvariantCulture));
        var info = g.sourceTextureInformation;
        Put(key + ".source", info == null ? "null" : $"{info.width}x{info.height} alpha {info.containsAlpha} hdr {info.hdr}");
        ImporterSettings(key + ".settings", g.textureImporterSettings);
        PlatformSettings(key + ".platform", g.platformSettings);
    }

    static TextureImporterSettings High() => new TextureImporterSettings
    {
        textureShape = TextureImporterShape.Texture2DArray, mipmapEnabled = true, sRGBTexture = true, borderMipmap = true,
        readable = true, aniso = 5, wrapMode = TextureWrapMode.Mirror, alphaIsTransparency = true,
        alphaSource = TextureImporterAlphaSource.FromGrayScale, npotScale = TextureImporterNPOTScale.ToLarger, spriteMode = 3,
        spriteGenerateFallbackPhysicsShape = true, generateCubemap = TextureImporterGenerateCubemap.Spheremap,
        spritePixelsPerUnit = 32, filterMode = FilterMode.Trilinear,
    };

    static TextureImporterSettings Low() => new TextureImporterSettings
    {
        textureShape = TextureImporterShape.Texture3D, mipmapEnabled = false, sRGBTexture = false, borderMipmap = false,
        readable = false, aniso = 0, wrapMode = TextureWrapMode.MirrorOnce, alphaIsTransparency = false,
        alphaSource = TextureImporterAlphaSource.None, npotScale = TextureImporterNPOTScale.None, spriteMode = 0,
        spriteGenerateFallbackPhysicsShape = false, generateCubemap = TextureImporterGenerateCubemap.None,
        spritePixelsPerUnit = 7, filterMode = FilterMode.Point,
    };

    static void Defaults()
    {
        GenerationSettings("defaults.struct", new TextureGenerationSettings());
        foreach (var type in s_Types)
            GenerationSettings("defaults." + type, new TextureGenerationSettings(type));
        ImporterSettings("settings.new", new TextureImporterSettings());
        foreach (var type in s_Types)
        {
            var s = new TextureImporterSettings();
            s.ApplyTextureType(type);
            ImporterSettings("settings.apply." + type, s);
        }
        // ApplyTextureType on settings that already differ from a new object's, both ways: which
        // fields each type sets, and which it leaves as they were.
        foreach (var type in s_Types)
        {
            var high = High();
            high.ApplyTextureType(type);
            ImporterSettings("settings.applyHigh." + type, high);
            var low = Low();
            low.ApplyTextureType(type);
            ImporterSettings("settings.applyLow." + type, low);
        }
        var changed = High();
        var copy = new TextureImporterSettings();
        changed.CopyTo(copy);
        Put("settings.copyTo.equal", TextureImporterSettings.Equal(changed, copy));
        Put("settings.new.equal", TextureImporterSettings.Equal(new TextureImporterSettings(), new TextureImporterSettings()));
        Put("settings.changed.equal", TextureImporterSettings.Equal(changed, new TextureImporterSettings()));
        PlatformSettings("platform.new", new TextureImporterPlatformSettings());
        var platform = new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 64, format = TextureImporterFormat.RGBA32 };
        var platformCopy = new TextureImporterPlatformSettings();
        platform.CopyTo(platformCopy);
        PlatformSettings("platform.copyTo", platformCopy);
        Put("source.new", $"{new SourceTextureInformation().width}x{new SourceTextureInformation().height} alpha {new SourceTextureInformation().containsAlpha} hdr {new SourceTextureInformation().hdr}");
        var data = new SpriteImportData();
        Put("spriteImportData.new", $"{data.name ?? "null"} {R(data.rect)} {(int)data.alignment} {V2(data.pivot)} {V4(data.border)} {(data.outline == null ? "null" : data.outline.Count.ToString(CultureInfo.InvariantCulture))} {F(data.tessellationDetail)} {data.spriteID ?? "null"}");
    }

    // ------------------------------------------------------------------ generation

    /// <summary>A deterministic RGBA pattern, rows bottom-up: opaque gradients with a transparent
    /// band (alpha 0, colour kept) and a half-transparent block.</summary>
    static Color32 Pixel(int x, int y, int w, int h)
    {
        byte r = (byte)(x * 255 / Math.Max(w - 1, 1)), g = (byte)(y * 255 / Math.Max(h - 1, 1)), b = (byte)((x * 7 + y * 13) % 256);
        byte a = y >= h / 2 && y < h / 2 + 3 ? (byte)0 : x < w / 4 && y < h / 4 ? (byte)128 : (byte)255;
        return new Color32(r, g, b, a);
    }

    static NativeArray<Color32> Buffer(int w, int h)
    {
        var buffer = new NativeArray<Color32>(w * h, Allocator.Temp);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                buffer[y * w + x] = Pixel(x, y, w, h);
        return buffer;
    }

    delegate void Edit(ref TextureGenerationSettings settings);

    static TextureGenerationSettings Settings(TextureImporterType type, int w, int h, Edit edit = null)
    {
        var g = new TextureGenerationSettings(type);
        g.sourceTextureInformation = new SourceTextureInformation { width = w, height = h, containsAlpha = true, hdr = false };
        g.textureImporterSettings.readable = true;
        g.platformSettings.format = TextureImporterFormat.RGBA32;
        edit?.Invoke(ref g);
        return g;
    }

    static void Texture(string key, Texture2D t)
    {
        if (t == null) { Put(key, null); return; }
        Put(key + ".name", t.name); Put(key + ".size", t.width + "x" + t.height); Put(key + ".format", t.format);
        Put(key + ".graphicsFormat", t.graphicsFormat); Put(key + ".mipmapCount", t.mipmapCount);
        Put(key + ".filterMode", t.filterMode); Put(key + ".wrap", $"{(int)t.wrapModeU},{(int)t.wrapModeV},{(int)t.wrapModeW}");
        Put(key + ".anisoLevel", t.anisoLevel); Put(key + ".mipMapBias", t.mipMapBias); Put(key + ".isReadable", t.isReadable);
        Put(key + ".hideFlags", t.hideFlags); Put(key + ".alphaIsTransparency", t.alphaIsTransparency);
        if (!t.isReadable) return;
        var raw = t.GetRawTextureData();
        Put(key + ".rawLength", raw.Length);
        if (GraphicsFormatUtilityIsCompressed(t.format)) return;
        for (int mip = 0; mip < t.mipmapCount; mip++)
        {
            var pixels = t.GetPixels32(mip);
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++) { bytes[i * 4] = pixels[i].r; bytes[i * 4 + 1] = pixels[i].g; bytes[i * 4 + 2] = pixels[i].b; bytes[i * 4 + 3] = pixels[i].a; }
            Put(key + ".mip" + mip, Hash(bytes));
        }
        // A few texels by place: a corner, the transparent band and the half-transparent block.
        int w = t.width, h = t.height;
        foreach (var (x, y) in new[] { (0, 0), (w - 1, h - 1), (w / 2, h / 2 + 1), (1, 1) })
            Put(key + $".texel{x}_{y}", (Color)t.GetPixel(x, y));
    }

    static bool GraphicsFormatUtilityIsCompressed(TextureFormat format) =>
        UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(
            UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, false));

    static void Sprites(string key, Sprite[] sprites)
    {
        if (sprites == null) { Put(key, null); return; }
        Put(key + ".count", sprites.Length);
        for (int i = 0; i < sprites.Length; i++)
        {
            var s = sprites[i];
            var k = key + "." + i;
            if (s == null) { Put(k, null); continue; }
            Put(k + ".name", s.name); Put(k + ".rect", s.rect); Put(k + ".pivot", s.pivot); Put(k + ".border", s.border);
            Put(k + ".pixelsPerUnit", s.pixelsPerUnit); Put(k + ".textureRect", s.textureRect);
            Put(k + ".texture", s.texture == null ? "null" : s.texture.name);
            Put(k + ".packingMode", s.packingMode); Put(k + ".bounds", s.bounds.center + " " + s.bounds.size);
            Put(k + ".vertices", string.Join(";", s.vertices.Select(V2)));
            Put(k + ".triangles", string.Join(",", s.triangles));
            Put(k + ".uv", string.Join(";", s.uv.Select(V2)));
            Put(k + ".spriteID", s.GetSpriteID());
            Put(k + ".physicsShapes", s.GetPhysicsShapeCount());
        }
    }

    static void Output(string key, TextureGenerationOutput o)
    {
        Texture(key + ".texture", o.texture);
        Put(key + ".outputIsTexture", ReferenceEquals(o.output, o.texture));
        Put(key + ".thumbNail", o.thumbNail == null ? "null" : o.thumbNail.width + "x" + o.thumbNail.height + " " + o.thumbNail.format);
        Put(key + ".importWarnings", o.importWarnings == null ? "null" : string.Join(" | ", o.importWarnings));
        Put(key + ".importInspectorWarnings", o.importInspectorWarnings ?? "null");
        Sprites(key + ".sprites", o.sprites);
        Put(key + ".logs", TakeLogs());
    }

    static void Case(string key, TextureGenerationSettings settings, int w, int h)
    {
        var buffer = Buffer(w, h);
        try { Output(key, TextureGenerator.GenerateTexture(settings, buffer)); }
        catch (Exception e) { Put(key + ".threw", e.GetType().FullName + ": " + e.Message); Put(key + ".logs", TakeLogs()); }
        finally { buffer.Dispose(); }
    }

    static SpriteImportData Sprite(string name, Rect rect, SpriteAlignment alignment, Vector2 pivot, Vector4 border, string id) =>
        new SpriteImportData { name = name, rect = rect, alignment = alignment, pivot = pivot, border = border, spriteID = id, tessellationDetail = 0 };

    static void Generate()
    {
        TakeLogs();
        Case("g.default", Settings(TextureImporterType.Default, 64, 32), 64, 32);
        Case("g.noAlpha", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => g.textureImporterSettings.alphaSource = TextureImporterAlphaSource.None), 64, 32);
        Case("g.transparency", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => { g.textureImporterSettings.alphaIsTransparency = true; g.textureImporterSettings.mipmapEnabled = false; }), 64, 32);
        Case("g.linear", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => { g.textureImporterSettings.sRGBTexture = false; g.textureImporterSettings.filterMode = FilterMode.Point; g.textureImporterSettings.wrapMode = TextureWrapMode.Clamp; }), 64, 32);
        Case("g.maxSize", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => g.platformSettings.maxTextureSize = 32), 64, 32);
        Case("g.npot", Settings(TextureImporterType.Default, 37, 21), 37, 21);
        Case("g.npotNone", Settings(TextureImporterType.Default, 37, 21, (ref TextureGenerationSettings g) => g.textureImporterSettings.npotScale = TextureImporterNPOTScale.None), 37, 21);
        Case("g.notReadable", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => g.textureImporterSettings.readable = false), 64, 32);
        Case("g.compressed", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => { g.platformSettings.format = TextureImporterFormat.Automatic; g.platformSettings.textureCompression = TextureImporterCompression.Compressed; }), 64, 32);
        Case("g.uncompressedAutomatic", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => { g.platformSettings.format = TextureImporterFormat.Automatic; g.platformSettings.textureCompression = TextureImporterCompression.Uncompressed; }), 64, 32);
        Case("g.normalMap", Settings(TextureImporterType.NormalMap, 64, 32), 64, 32);
        Case("g.spriteSingle", Settings(TextureImporterType.Sprite, 64, 32, (ref TextureGenerationSettings g) =>
        {
            g.textureImporterSettings.spriteMode = (int)SpriteImportMode.Single;
            g.textureImporterSettings.spriteMeshType = SpriteMeshType.FullRect;
            g.textureImporterSettings.spritePixelsPerUnit = 50;
            g.spriteImportData = new[] { Sprite("Solo", new Rect(0, 0, 64, 32), SpriteAlignment.Custom, new Vector2(0.25f, 0.75f), Vector4.zero, "0123456789abcdef0123456789abcdef") };
        }), 64, 32);
        Case("g.spriteSingleNoData", Settings(TextureImporterType.Sprite, 64, 32, (ref TextureGenerationSettings g) => g.textureImporterSettings.spriteMode = (int)SpriteImportMode.Single), 64, 32);
        Case("g.spriteMultiple", Settings(TextureImporterType.Sprite, 64, 32, (ref TextureGenerationSettings g) =>
        {
            g.textureImporterSettings.spriteMode = (int)SpriteImportMode.Multiple;
            g.textureImporterSettings.spriteMeshType = SpriteMeshType.Tight;
            g.spriteImportData = new[]
            {
                Sprite("Left", new Rect(0, 0, 32, 32), SpriteAlignment.Center, Vector2.zero, Vector4.zero, "11111111111111111111111111111111"),
                Sprite("Right", new Rect(32, 0, 32, 32), SpriteAlignment.Custom, new Vector2(0.2f, 0.1f), new Vector4(1, 2, 3, 4), "22222222222222222222222222222222"),
                Sprite("Outlined", new Rect(8, 4, 16, 20), SpriteAlignment.BottomLeft, Vector2.zero, Vector4.zero, "33333333333333333333333333333333"),
            };
            g.spriteImportData[2].outline = new List<Vector2[]> { new[] { new Vector2(-8, -10), new Vector2(8, -10), new Vector2(0, 10) } };
            g.spriteImportData[1].tessellationDetail = 0.5f;
        }), 64, 32);
        Case("g.spriteMultipleFullRect", Settings(TextureImporterType.Sprite, 64, 32, (ref TextureGenerationSettings g) =>
        {
            g.textureImporterSettings.spriteMode = (int)SpriteImportMode.Multiple;
            g.textureImporterSettings.spriteMeshType = SpriteMeshType.FullRect;
            g.textureImporterSettings.spritePixelsPerUnit = 16;
            g.spriteImportData = new[] { Sprite("Quarter", new Rect(16, 8, 16, 16), SpriteAlignment.TopRight, Vector2.zero, Vector4.zero, "44444444444444444444444444444444") };
        }), 64, 32);
        Case("g.spriteNotSpriteType", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) =>
            g.spriteImportData = new[] { Sprite("Ignored", new Rect(0, 0, 8, 8), SpriteAlignment.Center, Vector2.zero, Vector4.zero, "55555555555555555555555555555555") }), 64, 32);
        // The Color overload: the same pattern as floats.
        var colors = new NativeArray<Color>(64 * 32, Allocator.Temp);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 64; x++)
                colors[y * 64 + x] = Pixel(x, y, 64, 32);
        try
        {
            Output("g.colorOverload", TextureGenerator.GenerateTexture(Settings(TextureImporterType.Default, 64, 32), colors));
            Output("g.colorOverloadLinear", TextureGenerator.GenerateTexture(Settings(TextureImporterType.Default, 64, 32,
                (ref TextureGenerationSettings g) => g.textureImporterSettings.sRGBTexture = false), colors));
        }
        catch (Exception e) { Put("g.colorOverload.threw", e.GetType().FullName + ": " + e.Message); }
        finally { colors.Dispose(); }
        // A sprite's UVs when its mesh is rewritten through SpriteDataAccessExtensions (the 2D
        // Animation package rewrites rigged sprites' meshes this way).
        Guard("spriteMesh", () =>
        {
            var tex = new Texture2D(64, 32);
            var mesh = UnityEngine.Sprite.Create(tex, new Rect(16, 8, 32, 16), new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect);
            void Dump(string step)
            {
                Put("spriteMesh." + step + ".uv", string.Join(";", mesh.uv.Select(V2)));
                Put("spriteMesh." + step + ".texCoord0", string.Join(";", mesh.GetVertexAttribute<Vector2>(UnityEngine.Rendering.VertexAttribute.TexCoord0).ToArray().Select(V2)));
                Put("spriteMesh." + step + ".vertices", string.Join(";", mesh.vertices.Select(V2)));
            }
            Dump("initial");
            mesh.SetVertexCount(3);
            var positions = new NativeArray<Vector3>(new[] { new Vector3(-1, -0.5f, 0), new Vector3(1, -0.5f, 0), new Vector3(0, 0.25f, 0) }, Allocator.Temp);
            mesh.SetVertexAttribute(UnityEngine.Rendering.VertexAttribute.Position, positions);
            mesh.SetIndices(new NativeArray<ushort>(new ushort[] { 0, 1, 2 }, Allocator.Temp));
            Dump("positions");
            mesh.SetVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0, new NativeArray<Vector2>(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1) }, Allocator.Temp));
            Dump("texCoords");
            positions[2] = new Vector3(0.5f, 0.5f, 0);
            mesh.SetVertexAttribute(UnityEngine.Rendering.VertexAttribute.Position, positions);
            Dump("positionsAgain");
        });
        // A sprite renderer a script adds: its materials (no render pipeline here).
        var holder = new GameObject("AddedRenderer");
        var added = holder.AddComponent<SpriteRenderer>();
        Put("spriteRenderer.added.materials", string.Join(",", added.sharedMaterials.Select(m => m == null ? "null" : m.name + "/" + m.shader.name)));
        UnityEngine.Object.DestroyImmediate(holder);
        // Under a render pipeline whose default 2D material differs, and an instance of a
        // renderer made before it was set.
        Guard("spriteRenderer.pipeline", () =>
        {
            var before = new GameObject("BeforePipeline").AddComponent<SpriteRenderer>();
            var pipeline = ScriptableObject.CreateInstance<ToolApisPipelineAsset>();
            pipeline.material = new Material(Shader.Find("Sprites/Default")) { name = "PipelineDefault2D" };
            var previous = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
            try
            {
                var underPipeline = new GameObject("UnderPipeline").AddComponent<SpriteRenderer>();
                Put("spriteRenderer.pipeline.added", string.Join(",", underPipeline.sharedMaterials.Select(m => m == null ? "null" : m.name)));
                var copy = UnityEngine.Object.Instantiate(before.gameObject).GetComponent<SpriteRenderer>();
                Put("spriteRenderer.pipeline.instantiated", string.Join(",", copy.sharedMaterials.Select(m => m == null ? "null" : m.name)));
                UnityEngine.Object.DestroyImmediate(underPipeline.gameObject);
                UnityEngine.Object.DestroyImmediate(copy.gameObject);
            }
            finally
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = previous;
                UnityEngine.Object.DestroyImmediate(before.gameObject);
            }
        });
        // Sprites made by script, outside any atlas.
        var source = new Texture2D(16, 16);
        foreach (var mesh in new[] { SpriteMeshType.FullRect, SpriteMeshType.Tight })
        {
            var created = UnityEngine.Sprite.Create(source, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100, 0, mesh);
            Put("spriteCreate." + mesh + ".packing", $"packed {created.packed} mode {(int)created.packingMode} rotation {(int)created.packingRotation}");
        }
    }

    // ------------------------------------------------------------------ postprocessors

    internal static readonly List<string> s_Calls = new List<string>();

    static void Postprocessor()
    {
        foreach (var enabled in new[] { true, false })
        {
            s_Calls.Clear();
            ToolApisPostprocessor.Active = true;
            Case("post." + enabled, Settings(TextureImporterType.Sprite, 64, 32, (ref TextureGenerationSettings g) =>
            {
                g.assetPath = "Assets/NotThere/Generated.png";
                g.enablePostProcessor = enabled;
                g.textureImporterSettings.spriteMode = (int)SpriteImportMode.Multiple;
                g.spriteImportData = new[] { Sprite("Post", new Rect(0, 0, 16, 16), SpriteAlignment.Center, Vector2.zero, Vector4.zero, "66666666666666666666666666666666") };
            }), 64, 32);
            ToolApisPostprocessor.Active = false;
            Put("post." + enabled + ".calls", string.Join(" | ", s_Calls));
        }
    }

    static void Errors()
    {
        TakeLogs();
        // A buffer shorter than width x height, and an empty source.
        Case("err.shortBuffer", Settings(TextureImporterType.Default, 64, 32, (ref TextureGenerationSettings g) => g.sourceTextureInformation.height = 40), 64, 32);
        Case("err.zeroSize", Settings(TextureImporterType.Default, 0, 0), 64, 32);
    }

    // ------------------------------------------------------------------ importer identity

    /// <summary>The importer AssetImporter.GetAtPath gives during an asset's import (the 2D
    /// Animation package's sprite postprocessor reads the importing PSD importer's data through
    /// it), and afterwards.</summary>
    static void ImporterIdentity()
    {
        const string path = "Assets/Data.toolapis";
        ToolApisImporter.During = "not imported";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        Put("importer.main", AssetDatabase.LoadMainAssetAtPath(path) is TextAsset t ? t.text : "null");
        Put("importer.during", ToolApisImporter.During);
        var after = AssetImporter.GetAtPath(path) as ToolApisImporter;
        Put("importer.after", after == null ? "null" : "imports " + after.imports);
    }

    // ------------------------------------------------------------------ build platforms

    /// <summary>The Editor's internal build platform list, which the 2D packages read to name a
    /// build target's platform settings. Which platforms are installed and the Standalone default
    /// target depend on the machine and are left out.</summary>
    static void Platforms()
    {
        var type = typeof(Editor).Assembly.GetType("UnityEditor.Build.BuildPlatforms");
        var instance = type.GetProperty("instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        var platforms = (Array)type.GetField("buildPlatforms").GetValue(instance);
        Put("platforms.count", platforms.Length);
        for (int i = 0; i < platforms.Length; i++)
        {
            var p = platforms.GetValue(i);
            var t = p.GetType();
            object Get(string n) => t.GetField(n)?.GetValue(p) ?? t.GetProperty(n)?.GetValue(p);
            var named = Get("namedBuildTarget");
            var group = Get("targetGroup");
            bool standalone = (BuildTargetGroup)group == BuildTargetGroup.Standalone;
            Put("platforms." + i, $"{t.Name} name {Get("name")} target {named.GetType().GetProperty("TargetName").GetValue(named)} group {Convert.ToInt32(group, CultureInfo.InvariantCulture)} "
                + $"default {(standalone ? "host" : Convert.ToInt32(Get("defaultTarget"), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))} hide {Get("hideInUi")} "
                + $"subtarget {t.GetField("subtarget")?.GetValue(p) ?? "-"} tooltip {Get("tooltip")} title {((GUIContent)Get("title")).text}");
        }
    }

    // ------------------------------------------------------------------ PSD importer

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    static void Psd()
    {
        TakeLogs();
        const string path = "Assets/Layers.psd";
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        Put("psd.main", main == null ? "null" : main.GetType().Name + " " + main.name);
        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        Put("psd.objects", string.Join(" | ", all.Select(o => o.GetType().Name + " " + o.name).OrderBy(n => n, StringComparer.Ordinal)));
        Texture("psd.texture", all.OfType<Texture2D>().FirstOrDefault());
        Sprites("psd.sprites", all.OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray());
        var prefab = all.OfType<GameObject>().FirstOrDefault(g => g.transform.parent == null);
        if (prefab != null)
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                var sr = t.GetComponent<SpriteRenderer>();
                Put("psd.prefab." + PathOf(t), $"active {t.gameObject.activeSelf} pos {t.localPosition.x.ToString("R", CultureInfo.InvariantCulture)},{t.localPosition.y.ToString("R", CultureInfo.InvariantCulture)},{t.localPosition.z.ToString("R", CultureInfo.InvariantCulture)} "
                    + (sr == null ? "no renderer" : $"sprite {(sr.sprite ? sr.sprite.name : "null")} order {sr.sortingOrder} enabled {sr.enabled} materials {string.Join(",", sr.sharedMaterials.Select(m => m == null ? "null" : m.name))}"));
            }
        Put("psd.logs", TakeLogs());
    }

    // ------------------------------------------------------------------ material serialized properties

    static string Pairs(SerializedProperty array, Func<SerializedProperty, string> value)
    {
        if (array == null) return "null";
        var items = new List<string>();
        for (int i = 0; i < array.arraySize; i++)
        {
            var e = array.GetArrayElementAtIndex(i);
            items.Add(e.FindPropertyRelative("first").stringValue + "=" + value(e.FindPropertyRelative("second")));
        }
        return array.arraySize + ": " + string.Join(";", items);
    }

    static void SavedProperties(string key, Material material)
    {
        var so = new SerializedObject(material);
        var saved = so.FindProperty("m_SavedProperties");
        Put(key + ".saved", saved == null ? "null" : saved.propertyType + " " + saved.type);
        if (saved == null) return;
        Put(key + ".floats", Pairs(saved.FindPropertyRelative("m_Floats"), p => F(p.floatValue)));
        Put(key + ".ints", Pairs(saved.FindPropertyRelative("m_Ints"), p => p.intValue.ToString(CultureInfo.InvariantCulture)));
        Put(key + ".colors", Pairs(saved.FindPropertyRelative("m_Colors"), p => C(p.colorValue)));
        Put(key + ".texEnvs", Pairs(saved.FindPropertyRelative("m_TexEnvs"), p =>
        {
            var tex = p.FindPropertyRelative("m_Texture").objectReferenceValue;
            return (tex == null ? "null" : tex.name) + " " + V2(p.FindPropertyRelative("m_Scale").vector2Value) + " " + V2(p.FindPropertyRelative("m_Offset").vector2Value);
        }));
        Put(key + ".floatsType", saved.FindPropertyRelative("m_Floats")?.arrayElementType ?? "null");
    }

    static void MaterialProperties()
    {
        TakeLogs();
        var material = new Material(Shader.Find("Sprites/Default")) { name = "Probe" };
        material.SetFloat("_Extra", 3.5f);
        material.SetColor("_Color", new Color(0.25f, 0.5f, 0.75f, 1f));
        SavedProperties("mat.initial", material);
        // VFX's material settings pattern: replace the whole float list, then apply.
        var so = new SerializedObject(material);
        var floats = so.FindProperty("m_SavedProperties").FindPropertyRelative("m_Floats");
        floats.arraySize = 2;
        floats.GetArrayElementAtIndex(0).FindPropertyRelative("first").stringValue = "PixelSnap";
        floats.GetArrayElementAtIndex(0).FindPropertyRelative("second").floatValue = 1f;
        floats.GetArrayElementAtIndex(1).FindPropertyRelative("first").stringValue = "_Added";
        floats.GetArrayElementAtIndex(1).FindPropertyRelative("second").floatValue = 7.25f;
        Put("mat.apply", so.ApplyModifiedProperties());
        material.renderQueue = 3100;
        SavedProperties("mat.after", material);
        Put("mat.after.get", $"PixelSnap {F(material.GetFloat("PixelSnap"))} _Added {F(material.GetFloat("_Added"))} _Extra {F(material.GetFloat("_Extra"))} _EnableExternalAlpha {F(material.GetFloat("_EnableExternalAlpha"))}");
        Put("mat.after.has", $"_Added {material.HasFloat("_Added")} _Extra {material.HasFloat("_Extra")} _EnableExternalAlpha {material.HasFloat("_EnableExternalAlpha")} _Color {material.HasColor("_Color")}");
        Put("mat.after.renderQueue", material.renderQueue);
        Put("mat.logs", TakeLogs());
    }

    // ------------------------------------------------------------------ material sheets (matx)

    /// <summary>A serialized property's value: numbers, strings, references by name, arrays and
    /// structs with their parts (a string is one value, not its characters).</summary>
    static string Describe(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
            case SerializedPropertyType.Float: return F(p.floatValue);
            case SerializedPropertyType.Enum: return "enum " + p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.String: return "'" + p.stringValue + "'";
            case SerializedPropertyType.ObjectReference: return p.objectReferenceValue == null ? "null" : p.objectReferenceValue.name;
            case SerializedPropertyType.Color: return C(p.colorValue);
            case SerializedPropertyType.Vector2: return V2(p.vector2Value);
            case SerializedPropertyType.Vector4: return V4(p.vector4Value);
        }
        if (p.isArray)
        {
            var items = new List<string>();
            for (int i = 0; i < p.arraySize; i++) items.Add(Describe(p.GetArrayElementAtIndex(i)));
            return "[" + p.arraySize + ": " + string.Join(";", items) + "]";
        }
        var parts = new List<string>();
        var child = p.Copy();
        var end = p.GetEndProperty();
        if (child.Next(true))
            while (!SerializedProperty.EqualContents(child, end))
            {
                parts.Add(child.name + "=" + Describe(child));
                if (!child.Next(false)) break;
            }
        return p.type + "{" + string.Join(",", parts) + "}";
    }

    /// <summary>The top-level properties (name, type, SerializedPropertyType, array) in the
    /// iterator's order, the visible ones, and each one's value but the sheet's.</summary>
    static void Fields(string key, Material material)
    {
        var so = new SerializedObject(material);
        var it = so.GetIterator();
        var all = new List<string>();
        if (it.Next(true))
            do all.Add($"{it.name}:{it.type}:{(int)it.propertyType}{(it.isArray ? ":array" : "")}");
            while (it.Next(false));
        Put(key + ".fields", string.Join(" ", all));
        var visible = new List<string>();
        it = so.GetIterator();
        if (it.NextVisible(true))
            do visible.Add(it.name);
            while (it.NextVisible(false));
        Put(key + ".visible", string.Join(" ", visible));
        it = so.GetIterator();
        if (it.Next(true))
            do
            {
                if (it.name == "m_SavedProperties") continue;
                Put(key + ".field." + it.name, Describe(it));
            }
            while (it.Next(false));
    }

    /// <summary>The sheet's own tree: every property under m_SavedProperties (a string's characters
    /// left out) with its type, SerializedPropertyType and depth.</summary>
    static void SheetTree(string key, Material material)
    {
        var so = new SerializedObject(material);
        var p = so.FindProperty("m_SavedProperties");
        if (p == null) { Put(key, "null"); return; }
        var rows = new List<string>();
        var end = p.GetEndProperty();
        bool enter = true;
        do
        {
            rows.Add($"{p.propertyPath}:{p.type}:{(int)p.propertyType}:{p.depth}");
            enter = p.propertyType != SerializedPropertyType.String;
        }
        while (p.Next(enter) && !SerializedProperty.EqualContents(p, end));
        Put(key, string.Join(" ", rows));
    }

    static void Sheet(string key, Material material)
    {
        var so = new SerializedObject(material);
        var saved = so.FindProperty("m_SavedProperties");
        if (saved == null) { Put(key, "null"); return; }
        foreach (var list in new[] { "m_TexEnvs", "m_Ints", "m_Floats", "m_Colors" })
            Put(key + "." + list, Describe(saved.FindPropertyRelative(list)));
    }

    static void SetPairs(SerializedProperty array, params (string Name, Action<SerializedProperty> Write)[] entries)
    {
        array.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = array.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("first").stringValue = entries[i].Name;
            entries[i].Write(e.FindPropertyRelative("second"));
        }
    }

    static string Getters(Material m) =>
        $"_Zeta {F(m.GetFloat("_Zeta"))} _Alpha {F(m.GetFloat("_Alpha"))} _glossy {F(m.GetFloat("_glossy"))} _Legacy {m.GetInt("_Legacy")} "
        + $"_Count {m.GetInteger("_Count")} _Steps {m.GetInteger("_Steps")} _Tint {C(m.GetColor("_Tint"))} _Dir {V4(m.GetVector("_Dir"))} "
        + $"_BaseMap {(m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "null")} {V2(m.GetTextureScale("_BaseMap"))} {V2(m.GetTextureOffset("_BaseMap"))} "
        + $"_Bump {(m.GetTexture("_Bump") ? m.GetTexture("_Bump").name : "null")}";

    static string Hases(Material m) =>
        $"_Zeta {m.HasFloat("_Zeta")} _Other {m.HasFloat("_Other")} _Extra {m.HasFloat("_Extra")} _StaleFloat {m.HasFloat("_StaleFloat")} "
        + $"_Steps {m.HasInteger("_Steps")} _StaleInt {m.HasInteger("_StaleInt")} _Dir {m.HasVector("_Dir")} _StaleColor {m.HasColor("_StaleColor")} "
        + $"_Bump {m.HasTexture("_Bump")} _StaleTex {m.HasTexture("_StaleTex")}";

    static string MatSettings(Material m) =>
        $"queue {m.renderQueue} raw {m.rawRenderQueue} gi {(int)m.globalIlluminationFlags} doubleSidedGI {m.doubleSidedGI} instancing {m.enableInstancing} "
        + $"keywords {string.Join(",", m.shaderKeywords)} toggle {m.IsKeywordEnabled("_TOGGLE_ON")} modeA {m.IsKeywordEnabled("_MODE_A")} "
        + $"tag {m.GetTag("RenderType", false, "none")} shadowPass {m.GetShaderPassEnabled("ShadowCaster")}";

    /// <summary>A Material's whole SerializedObject: its fields, the saved property sheet of a new
    /// material, of one whose script set properties and keywords, and of a loaded one whose file
    /// lacks some of its shader's properties and has others; then edits through the SerializedObject
    /// (each list replaced, keywords and settings) and what the material answers afterwards, and how
    /// later script sets, a shader change and a copy carry the sheet on.</summary>
    static void MaterialSheets()
    {
        TakeLogs();
        var shader = Shader.Find("ToolApis/Props");
        Put("matx.shader", shader == null ? "null" : shader.name);
        var sprites = new Material(Shader.Find("Sprites/Default")) { name = "SpritesTree" };
        SheetTree("matx.sprites.tree", sprites);
        Fields("matx.sprites", sprites);

        var fresh = new Material(shader) { name = "Fresh" };
        Sheet("matx.fresh", fresh);
        Fields("matx.fresh", fresh);
        Put("matx.fresh.get", Getters(fresh));
        Put("matx.fresh.settings", MatSettings(fresh));

        var set = new Material(shader) { name = "Set" };
        set.SetFloat("_Zeta", 1.5f);
        set.SetFloat("_Extra", 2f);
        set.SetInteger("_Steps", 5);
        set.SetInteger("_ExtraInt", 6);
        set.SetColor("_Tint", new Color(0.5f, 0.25f, 0.125f, 1f));
        set.SetVector("_ExtraVector", new Vector4(1, 1, 1, 1));
        set.SetTextureScale("_BaseMap", new Vector2(3, 4));
        set.EnableKeyword("_ZED_UNDECLARED");
        set.EnableKeyword("_ALPHA_UNDECLARED");
        set.EnableKeyword("_MODE_B");
        Sheet("matx.set", set);
        Put("matx.set.keywords", Describe(new SerializedObject(set).FindProperty("m_ValidKeywords")) + " " + Describe(new SerializedObject(set).FindProperty("m_InvalidKeywords")));
        Put("matx.set.has", Hases(set));
        Put("matx.set.logs", TakeLogs());

        var partial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Partial.mat");
        Put("matx.partial", partial == null ? "null" : partial.name);
        if (partial != null)
        {
            Sheet("matx.partial", partial);
            Fields("matx.partial", partial);
            Put("matx.partial.get", Getters(partial));
            Put("matx.partial.has", Hases(partial));
            Put("matx.partial.settings", MatSettings(partial));
            Put("matx.partial.logs", TakeLogs());
        }

        // Each list replaced through the SerializedObject: entries the shader has, left out and new.
        var edited = new Material(shader) { name = "Edited" };
        var runtime = new Texture2D(2, 2) { name = "RuntimeTex" };
        var so = new SerializedObject(edited);
        var saved = so.FindProperty("m_SavedProperties");
        SetPairs(saved.FindPropertyRelative("m_Floats"),
            ("_Alpha", p => p.floatValue = 0.75f), ("_Glossiness", p => p.floatValue = 0.5f), ("_Other", p => p.floatValue = 9f));
        SetPairs(saved.FindPropertyRelative("m_Ints"), ("_Count", p => p.intValue = 7));
        SetPairs(saved.FindPropertyRelative("m_Colors"), ("_Tint", p => p.colorValue = new Color(0, 0, 1, 1)), ("_OtherColor", p => p.colorValue = Color.white));
        SetPairs(saved.FindPropertyRelative("m_TexEnvs"), ("_BaseMap", p =>
        {
            p.FindPropertyRelative("m_Texture").objectReferenceValue = runtime;
            p.FindPropertyRelative("m_Scale").vector2Value = new Vector2(2, 3);
            p.FindPropertyRelative("m_Offset").vector2Value = new Vector2(0.5f, 0.25f);
        }));
        Put("matx.edit.apply", so.ApplyModifiedProperties());
        Sheet("matx.edit", edited);
        Put("matx.edit.get", Getters(edited));
        Put("matx.edit.has", Hases(edited));
        Put("matx.edit.logs", TakeLogs());

        // Keywords and settings through the SerializedObject.
        so = new SerializedObject(edited);
        var valid = so.FindProperty("m_ValidKeywords");
        valid.arraySize = 1;
        valid.GetArrayElementAtIndex(0).stringValue = "_MODE_A";
        var invalid = so.FindProperty("m_InvalidKeywords");
        invalid.arraySize = 1;
        invalid.GetArrayElementAtIndex(0).stringValue = "_NOPE";
        so.FindProperty("m_CustomRenderQueue").intValue = 2450;
        so.FindProperty("m_LightmapFlags").intValue = 1;
        so.FindProperty("m_DoubleSidedGI").boolValue = true;
        so.FindProperty("m_EnableInstancingVariants").boolValue = true;
        Put("matx.settings.apply", so.ApplyModifiedProperties());
        Put("matx.settings", MatSettings(edited));
        Fields("matx.settings", edited);
        Put("matx.settings.logs", TakeLogs());

        // What later changes do to the replaced sheet.
        edited.SetFloat("_Zeta", 4f);
        edited.SetFloat("_Undeclared", 1f);
        edited.SetInteger("_Steps", 2);
        Sheet("matx.later", edited);
        Put("matx.later.get", Getters(edited));
        var copy = new Material(edited);
        Sheet("matx.copy", copy);
        edited.shader = Shader.Find("Sprites/Default");
        Sheet("matx.reshaded", edited);
        Put("matx.later.logs", TakeLogs());

        // Which calls give an emptied sheet its shader's properties back: one call each.
        var calls = new (string Name, Action<Material> Call)[]
        {
            ("none", m => { }),
            ("getFloat", m => m.GetFloat("_Zeta")),
            ("setFloatUndeclared", m => m.SetFloat("_Undeclared", 1f)),
            ("setColor", m => m.SetColor("_Tint", Color.green)),
            ("setTexture", m => m.SetTexture("_BaseMap", runtime)),
            ("setTextureOffset", m => m.SetTextureOffset("_BaseMap", Vector2.one)),
            ("setInteger", m => m.SetInteger("_Count", 2)),
            ("enableKeyword", m => m.EnableKeyword("_MODE_A")),
            ("renderQueue", m => m.renderQueue = 2100),
            ("sameShader", m => m.shader = shader),
            ("copyPropertiesFrom", m => m.CopyPropertiesFromMaterial(new Material(shader))),
        };
        foreach (var (name, call) in calls)
        {
            var m = new Material(shader) { name = "Emptied" };
            var empty = new SerializedObject(m);
            var lists = empty.FindProperty("m_SavedProperties");
            foreach (var list in new[] { "m_TexEnvs", "m_Ints", "m_Floats", "m_Colors" })
                lists.FindPropertyRelative(list).arraySize = 0;
            empty.ApplyModifiedProperties();
            call(m);
            var after = new SerializedObject(m).FindProperty("m_SavedProperties");
            Put("matx.fill." + name, string.Join(" ", new[] { "m_TexEnvs", "m_Ints", "m_Floats", "m_Colors" }.Select(l => after.FindPropertyRelative(l).arraySize)));
        }
        Put("matx.fill.logs", TakeLogs());
    }

    // ------------------------------------------------------------------ imported objects' flags

    static string Flags(Object o) => o == null ? "null" : $"{o.GetType().Name} {o.name} {(int)o.hideFlags}";

    /// <summary>The hideFlags of the objects a ScriptedImporter adds (as it adds them and as loaded
    /// afterwards), of a GameObject's child and components, of an instance of it, and of a natively
    /// imported text asset and material.</summary>
    static void ImportedFlags()
    {
        TakeLogs();
        const string path = "Assets/Objects.toolapisobjects";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        Put("imported.during", string.Join(" | ", ToolApisObjectsImporter.During));
        Put("imported.main", Flags(AssetDatabase.LoadMainAssetAtPath(path)));
        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        Put("imported.all", string.Join(" | ", all.Select(Flags).OrderBy(n => n, StringComparer.Ordinal)));
        var root = all.OfType<GameObject>().FirstOrDefault(g => g.transform.parent == null);
        if (root != null)
        {
            Put("imported.root", string.Join(" | ", root.GetComponentsInChildren<Component>(true).Select(c => Flags(c.gameObject) + " " + Flags(c))));
            var instance = Object.Instantiate(root);
            Put("imported.instance", string.Join(" | ", instance.GetComponentsInChildren<Component>(true).Select(c => Flags(c.gameObject) + " " + Flags(c))));
            Object.Destroy(instance);
        }
        var material = all.OfType<Material>().FirstOrDefault();
        if (material != null)
        {
            var copy = new Material(material);
            Put("imported.materialCopy", Flags(copy));
            Put("imported.materialEditable", new SerializedObject(material).FindProperty("m_SavedProperties")?.editable ?? false);
        }
        // Control: an instance of an object made with flags, not imported.
        var made = new GameObject("MadeWithFlags") { hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable };
        made.AddComponent<SpriteRenderer>().hideFlags = HideFlags.NotEditable;
        var madeCopy = Object.Instantiate(made);
        Put("imported.madeInstance", Flags(madeCopy) + " | " + Flags(madeCopy.GetComponent<SpriteRenderer>()) + " | " + Flags(madeCopy.transform));
        Object.Destroy(madeCopy);
        Object.Destroy(made);
        var madeTexture = new Texture2D(2, 2) { name = "MadeTexture", hideFlags = HideFlags.NotEditable };
        Put("imported.madeTextureCopy", Flags(Object.Instantiate(madeTexture)));
        Put("imported.text", Flags(AssetDatabase.LoadMainAssetAtPath("Assets/Notes.txt")));
        Put("imported.mat", Flags(AssetDatabase.LoadMainAssetAtPath("Assets/Materials/Partial.mat")));
        Put("imported.psdAll", string.Join(" | ", AssetDatabase.LoadAllAssetsAtPath("Assets/Layers.psd").Select(Flags).OrderBy(n => n, StringComparer.Ordinal)));
        Put("imported.logs", TakeLogs());
    }

    // ------------------------------------------------------------------ USS var() types

    static readonly string[] s_UssCases = { "bad", "badwidth", "good", "parent/childbad", "later bad", "named", "fallback" };

    static void UssSetup()
    {
        TakeLogs();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1;
        settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
        var go = new GameObject("UssProbe");
        var document = go.AddComponent<UIDocument>();
        document.panelSettings = settings;
        s_UssRoot = document.rootVisualElement;
        s_UssRoot.styleSheets.Add(Resources.Load<StyleSheet>("VarTypes"));
        foreach (var name in s_UssCases)
        {
            var parts = name.Split('/');
            var parent = s_UssRoot;
            foreach (var part in parts)
            {
                var e = new VisualElement { name = part };
                foreach (var c in part.Split(' ')) e.AddToClassList(c);
                parent.Add(e);
                parent = e;
            }
        }
    }

    static void UssRead()
    {
        foreach (var name in s_UssCases)
        {
            var parts = name.Split('/');
            var e = s_UssRoot.Q(parts[parts.Length - 1]);
            var key = "uss." + name.Replace('/', '.').Replace(' ', '_');
            if (e == null) { Put(key, null); continue; }
            var s = e.resolvedStyle;
            Put(key + ".borderColor", $"{C(s.borderTopColor)} {C(s.borderRightColor)} {C(s.borderBottomColor)} {C(s.borderLeftColor)}");
            Put(key + ".borderWidth", $"{F(s.borderTopWidth)} {F(s.borderRightWidth)} {F(s.borderBottomWidth)} {F(s.borderLeftWidth)}");
            Put(key + ".borderRadius", $"{F(s.borderTopLeftRadius)} {F(s.borderBottomRightRadius)}");
            // Elements without an authored width stretch to the panel, whose size is the screen's.
            Put(key + ".height", F(s.height));
            Put(key + ".color", s.color);
        }
        // The panel's missing-theme warning is not part of the question; every other log is kept.
        Put("uss.logs", string.Join(" | ", s_Logs.Where(l => !l.Contains("No Theme Style Sheet"))));
        s_Logs.Clear();
    }
}

/// <summary>Records the hooks TextureGenerator.GenerateTexture runs while the probe asks for them.</summary>
public class ToolApisPostprocessor : AssetPostprocessor
{
    internal static bool Active;

    void Note(string hook, string detail = "") =>
        ToolApisProbe.s_Calls.Add($"{hook} path {assetPath} context {(context == null ? "null" : "set")}{detail}");

    void OnPreprocessTexture() { if (Active) Note("OnPreprocessTexture"); }
    void OnPostprocessTexture(Texture2D texture) { if (Active) Note("OnPostprocessTexture", $" {texture.width}x{texture.height}"); }
    void OnPostprocessSprites(Texture2D texture, Sprite[] sprites) { if (Active) Note("OnPostprocessSprites", $" {texture.width}x{texture.height} {string.Join(",", sprites.Select(s => s.name))}"); }
}
