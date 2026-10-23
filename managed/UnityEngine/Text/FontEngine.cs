using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

// TextCore's font engine: the face TextMeshPro's dynamic font assets load glyphs from, and the
// packing and rendering of those glyphs into their atlas textures. Faces and glyphs come from the
// native side (`font-open`, `font_render_glyph`); the values match Unity's FreeType-based engine
// (glyph metrics in 1/64 pixel, SDF encoded `0.5 + d / (2 (padding + 1))`).

namespace UnityEngine.TextCore.LowLevel
{
    public enum FontEngineError
    {
        Success = 0,
        Invalid_File_Path = 1,
        Invalid_File_Format = 2,
        Invalid_File_Structure = 3,
        Invalid_File = 4,
        Invalid_Table = 8,
        Invalid_Glyph_Index = 16,
        Invalid_Character_Code = 17,
        Invalid_Pixel_Size = 23,
        Invalid_Library = 33,
        Invalid_Face = 35,
        Invalid_Library_or_Face = 41,
        Atlas_Generation_Cancelled = 100,
        Invalid_SharedTextureData = 101,
        OpenTypeLayoutLookup_Mismatch = 116,
    }

    [Flags]
    public enum GlyphLoadFlags
    {
        LOAD_DEFAULT = 0,
        LOAD_NO_SCALE = 1,
        LOAD_NO_HINTING = 2,
        LOAD_RENDER = 4,
        LOAD_NO_BITMAP = 8,
        LOAD_FORCE_AUTOHINT = 32,
        LOAD_MONOCHROME = 4096,
        LOAD_NO_AUTOHINT = 32768,
        LOAD_COLOR = 1048576,
        LOAD_COMPUTE_METRICS = 2097152,
        LOAD_BITMAP_METRICS_ONLY = 4194304,
    }

    public enum GlyphPackingMode
    {
        BestShortSideFit = 0,
        BestLongSideFit = 1,
        BestAreaFit = 2,
        BottomLeftRule = 3,
        ContactPointRule = 4,
    }

    /// <summary>The bits are GlyphRasterModes: 1 8-bit, 2 mono, 8 hinted, 32 SDF, 64 SDFAA,
    /// 65536 color, 4096.. 32768 the SDF oversampling.</summary>
    public enum GlyphRenderMode
    {
        DEFAULT = 0,
        SMOOTH_HINTED = 4121,
        SMOOTH = 4117,
        COLOR_HINTED = 69656,
        COLOR = 69652,
        RASTER_HINTED = 4122,
        RASTER = 4118,
        SDF = 4134,
        SDF8 = 8230,
        SDF16 = 16422,
        SDF32 = 32806,
        SDFAA_HINTED = 4169,
        SDFAA = 4165,
    }

    internal partial struct FontReference
    {
        public string familyName;
        public string styleName;
        public int faceIndex;
        public string filePath;
    }

    public sealed partial class FontEngine
    {
        const int k_RasterModeMono = 2;
        const int k_RasterModeSdf = 32;
        const int k_RasterModeSdfaa = 64;
        const int k_RasterModeColor = 65536;
        /// <summary>The size a face loads at when none is given.</summary>
        const float k_DefaultPointSize = 90f;

        sealed class FaceJson
        {
            public int UnitsPerEm { get; set; }
            public int Ascender { get; set; }
            public int Descender { get; set; }
            public int LineGap { get; set; }
            public string Family { get; set; }
            public string Style { get; set; }
            public int CapHeight { get; set; }
            public int XHeight { get; set; }
            public int[] Underline { get; set; }
            public int SpaceAdvance { get; set; }
            public int GlyphCount { get; set; }
            public bool Hinted { get; set; }
            public int FaceCount { get; set; } = 1;
        }

        /// <summary>A loaded face: the native handle, where it came from (to open its other faces)
        /// and its description.</summary>
        sealed class LoadedFace
        {
            internal ulong Handle;
            internal string FilePath;
            internal byte[] Data;
            internal int FaceIndex;
            internal FaceJson Info;
        }

        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        static readonly Dictionary<ulong, FaceJson> s_Descriptions = new Dictionary<ulong, FaceJson>();
        static LoadedFace s_Face;
        static float s_PointSize = k_DefaultPointSize;

        public static FontEngineError InitializeFontEngine() => FontEngineError.Success;

        public static FontEngineError DestroyFontEngine()
        {
            s_Face = null;
            return FontEngineError.Success;
        }

        internal static void SendCancellationRequest() { }

        internal static bool isProcessingDone => true;

        internal static float generationProgress => 1f;

        // ---- Faces ----

        public static FontEngineError LoadFontFace(string filePath) => LoadFontFace(filePath, s_PointSize, 0);

        public static FontEngineError LoadFontFace(string filePath, int pointSize) => LoadFontFace(filePath, (float)pointSize, 0);

        public static FontEngineError LoadFontFace(string filePath, float pointSize, int faceIndex)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return FontEngineError.Invalid_File_Path;
            var source = NativeGlyphSource.Open(new { file = filePath, faceIndex }, quiet: true);
            if (source == null) return FontEngineError.Invalid_File_Format;
            return Load(new LoadedFace { Handle = source.Handle, FilePath = filePath, FaceIndex = faceIndex }, pointSize);
        }

        public static FontEngineError LoadFontFace(byte[] sourceFontFile) => LoadFontFace(sourceFontFile, s_PointSize, 0);

        public static FontEngineError LoadFontFace(byte[] sourceFontFile, int pointSize) => LoadFontFace(sourceFontFile, (float)pointSize, 0);

        public static FontEngineError LoadFontFace(byte[] sourceFontFile, float pointSize, int faceIndex)
        {
            if (sourceFontFile == null || sourceFontFile.Length == 0) return FontEngineError.Invalid_File;
            var source = NativeGlyphSource.Open(new { data = Convert.ToBase64String(sourceFontFile), faceIndex }, quiet: true);
            if (source == null) return FontEngineError.Invalid_File_Format;
            return Load(new LoadedFace { Handle = source.Handle, Data = sourceFontFile, FaceIndex = faceIndex }, pointSize);
        }

        public static FontEngineError LoadFontFace(Font font) => LoadFontFace(font, s_PointSize, 0);

        public static FontEngineError LoadFontFace(Font font, int pointSize) => LoadFontFace(font, (float)pointSize, 0);

        /// <summary>The face of a Font asset (its font file), or of the first OS font it stands for.</summary>
        public static FontEngineError LoadFontFace(Font font, float pointSize, int faceIndex)
        {
            if (font == null) return FontEngineError.Invalid_File;
            var source = font.m_Source as NativeGlyphSource ?? (font.m_Source as ChainGlyphSource)?.First as NativeGlyphSource;
            if (source == null) return FontEngineError.Invalid_File;
            return Load(new LoadedFace { Handle = source.Handle, FaceIndex = faceIndex }, pointSize);
        }

        public static FontEngineError LoadFontFace(string familyName, string styleName) => LoadFontFace(familyName, styleName, s_PointSize);

        public static FontEngineError LoadFontFace(string familyName, string styleName, float pointSize)
        {
            if (!TryGetSystemFontReference(familyName, styleName, out var reference)) return FontEngineError.Invalid_Face;
            return LoadFontFace(reference.filePath, pointSize, reference.faceIndex);
        }

        static FontEngineError Load(LoadedFace face, float pointSize)
        {
            face.Info = Describe(face.Handle);
            if (face.Info == null) return FontEngineError.Invalid_Face;
            s_Face = face;
            return SetFaceSize(pointSize);
        }

        static FaceJson Describe(ulong handle)
        {
            if (s_Descriptions.TryGetValue(handle, out var info)) return info;
            try
            {
                info = JsonSerializer.Deserialize<FaceJson>(Native.Call("font-face", JsonSerializer.Serialize(new { handle })), s_Json);
            }
            catch (Exception)
            {
                return null;
            }
            return s_Descriptions[handle] = info;
        }

        public static FontEngineError UnloadFontFace()
        {
            if (s_Face == null) return FontEngineError.Invalid_Face;
            s_Face = null;
            return FontEngineError.Success;
        }

        public static FontEngineError UnloadAllFontFaces()
        {
            s_Face = null;
            return FontEngineError.Success;
        }

        public static FontEngineError SetFaceSize(int pointSize) => SetFaceSize((float)pointSize);

        static FontEngineError SetFaceSize(float pointSize)
        {
            if (pointSize <= 0f) return FontEngineError.Invalid_Pixel_Size;
            s_PointSize = pointSize;
            return s_Face != null ? FontEngineError.Success : FontEngineError.Invalid_Face;
        }

        internal static int GetFaceCount() => s_Face?.Info.FaceCount ?? 0;

        /// <summary>"Family - Style" of each face in the loaded font file.</summary>
        public static string[] GetFontFaces()
        {
            if (s_Face == null) return Array.Empty<string>();
            int count = s_Face.FilePath != null || s_Face.Data != null ? Math.Max(1, s_Face.Info.FaceCount) : 1;
            var names = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var info = s_Face.Info;
                if (i != s_Face.FaceIndex)
                {
                    object request = s_Face.FilePath != null
                        ? new { file = s_Face.FilePath, faceIndex = i }
                        : new { data = Convert.ToBase64String(s_Face.Data), faceIndex = i };
                    var source = NativeGlyphSource.Open(request, quiet: true);
                    info = source != null ? Describe(source.Handle) : null;
                }
                if (info != null) names.Add(info.Family + " - " + info.Style);
            }
            return names.ToArray();
        }

        /// <summary>"Family - Style" of each installed OS font face.</summary>
        public static string[] GetSystemFontNames()
        {
            var faces = Font.OsFonts().Faces;
            var names = new string[faces.Length];
            for (int i = 0; i < faces.Length; i++) names[i] = faces[i].Family + " - " + faces[i].Style;
            return names;
        }

        internal static FontReference[] GetSystemFontReferences()
        {
            var faces = Font.OsFonts().Faces;
            var references = new FontReference[faces.Length];
            for (int i = 0; i < faces.Length; i++)
                references[i] = new FontReference { familyName = faces[i].Family, styleName = faces[i].Style, faceIndex = faces[i].Index, filePath = faces[i].Path };
            return references;
        }

        internal static bool TryGetSystemFontReference(string familyName, string styleName, out FontReference fontRef)
        {
            fontRef = default;
            if (string.IsNullOrEmpty(familyName)) return false;
            foreach (var face in Font.OsFonts().Faces)
            {
                if (!string.Equals(face.Family, familyName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(styleName) && !string.Equals(face.Style, styleName, StringComparison.OrdinalIgnoreCase)) continue;
                fontRef = new FontReference { familyName = face.Family, styleName = face.Style, faceIndex = face.Index, filePath = face.Path };
                return true;
            }
            return false;
        }

        /// <summary>Color (emoji) faces are not rendered in color yet.</summary>
        internal static bool IsColorFontFace() => false;

        /// <summary>The loaded face at the current size. Lines come from the face's ascender,
        /// descender and gap; the cap and mean lines are the tops of 'H' and 'x' as FreeType grid-fits
        /// them (rounded by a hinted font, else rounded up); the strikethrough sits at 2/5 of the mean
        /// line, as Unity places it.</summary>
        public static FaceInfo GetFaceInfo()
        {
            var face = new FaceInfo();
            if (s_Face == null) return face;
            var info = s_Face.Info;
            float size = s_PointSize;
            float scale = size / Math.Max(1, info.UnitsPerEm);
            float ascent = info.Ascender * scale;
            float descent = info.Descender * scale;
            float underlinePosition = info.Underline != null && info.Underline.Length == 2 ? info.Underline[0] : 0;
            float underlineThickness = info.Underline != null && info.Underline.Length == 2 ? info.Underline[1] : 0;
            float capLine = LineTop('H', info.CapHeight * scale, info.Hinted);
            float meanLine = LineTop('x', info.XHeight * scale, info.Hinted);
            face.faceIndex = s_Face.FaceIndex;
            face.familyName = info.Family;
            face.styleName = info.Style;
            face.pointSize = size;
            face.scale = 1f;
            face.unitsPerEM = info.UnitsPerEm;
            face.lineHeight = (info.Ascender - info.Descender + info.LineGap) * scale;
            face.ascentLine = ascent;
            face.capLine = capLine;
            face.meanLine = meanLine;
            face.baseline = 0f;
            face.descentLine = descent;
            face.superscriptOffset = ascent;
            face.superscriptSize = 0.5f;
            face.subscriptOffset = descent;
            face.subscriptSize = 0.5f;
            // FreeType's underline position is the line's center: the table's top edge minus half
            // the thickness.
            face.underlineOffset = (underlinePosition - (int)underlineThickness / 2) * scale;
            face.underlineThickness = underlineThickness * scale;
            face.strikethroughOffset = meanLine / 2.5f;
            face.strikethroughThickness = face.underlineThickness;
            face.tabWidth = (float)Math.Round(Fixed(info.SpaceAdvance * scale), MidpointRounding.AwayFromZero);
            return face;
        }

        static float LineTop(char c, float fallback, bool hinted)
        {
            float top = fallback;
            uint index = Native.FontGlyphIndex(s_Face.Handle, c);
            if (index != 0 && Native.FontRenderGlyph(s_Face.Handle, index, s_PointSize, -1, out var outline, out _) && outline.Width > 0)
                top = outline.YMax;
            return hinted ? (float)Math.Round(top, MidpointRounding.AwayFromZero) : (float)Math.Ceiling(top);
        }

        /// <summary>A value in pixels as FreeType's 26.6 fixed point holds it.</summary>
        static float Fixed(float pixels) => (float)Math.Round(pixels * 64f, MidpointRounding.AwayFromZero) / 64f;

        // ---- Glyphs ----

        internal static uint GetGlyphIndex(uint unicode) => s_Face != null ? Native.FontGlyphIndex(s_Face.Handle, unicode) : 0;

        /// <summary>Variation sequences (`cmap` format 14) are not read: there is never a variant,
        /// and TextMeshPro uses the base character's glyph.</summary>
        internal static uint GetVariantGlyphIndex(uint unicode, uint variantSelectorUnicode) => 0;

        public static bool TryGetGlyphIndex(uint unicode, out uint glyphIndex)
        {
            glyphIndex = GetGlyphIndex(unicode);
            return glyphIndex != 0;
        }

        public static bool TryGetGlyphWithUnicodeValue(uint unicode, GlyphLoadFlags flags, out Glyph glyph)
        {
            glyph = null;
            uint index = GetGlyphIndex(unicode);
            return index != 0 && TryGetGlyphWithIndexValue(index, flags, out glyph);
        }

        /// <summary>The glyph's metrics at the current size (in font units with LOAD_NO_SCALE), and
        /// a glyph rect at the origin the size of its bitmap.</summary>
        public static bool TryGetGlyphWithIndexValue(uint glyphIndex, GlyphLoadFlags flags, out Glyph glyph)
        {
            glyph = null;
            if (s_Face == null || glyphIndex >= (uint)Math.Max(0, s_Face.Info.GlyphCount)) return false;
            if (!Native.FontRenderGlyph(s_Face.Handle, glyphIndex, s_PointSize, -1, out var info, out _)) return false;
            glyph = MakeGlyph(glyphIndex, info, (flags & GlyphLoadFlags.LOAD_NO_SCALE) != 0 ? s_Face.Info.UnitsPerEm / s_PointSize : 1f);
            return true;
        }

        static Glyph MakeGlyph(uint index, GlyphOutlineInfo info, float unitScale)
        {
            bool empty = info.Width <= 0 || info.Height <= 0;
            float Scaled(float pixels) => unitScale == 1f ? Fixed(pixels) : (float)Math.Round(pixels * unitScale);
            var metrics = empty
                ? new GlyphMetrics(0f, 0f, 0f, 0f, Scaled(info.Advance))
                : new GlyphMetrics(
                    Scaled(info.XMax - info.XMin),
                    Scaled(info.YMax - info.YMin),
                    Scaled(info.XMin),
                    Scaled(info.YMax),
                    Scaled(info.Advance));
            var rect = empty ? GlyphRect.zero : new GlyphRect(0, 0, info.Width, info.Height);
            return new Glyph(index, metrics, rect, 1f, 0);
        }

        // ---- Atlas packing (MaxRects over TextMeshPro's free and used rect lists) ----

        /// <summary>The atlas space a glyph takes: its bitmap with the padding around it, plus a
        /// one-pixel gutter on the left and bottom (where Unity places glyphs in their rect).</summary>
        static int Packed(int size, int padding) => size + padding * 2 + 1;

        internal static bool TryPackGlyphInAtlas(Glyph glyph, int padding, GlyphPackingMode packingMode, GlyphRenderMode renderMode, int width, int height, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects)
        {
            if (glyph == null) return false;
            var rect = glyph.glyphRect;
            if (rect.width <= 0 || rect.height <= 0)
            {
                glyph.glyphRect = GlyphRect.zero;
                return true;
            }
            if (!GlyphPacker.TryPack(Packed(rect.width, padding), Packed(rect.height, padding), packingMode, freeGlyphRects, usedGlyphRects, out var placed))
                return false;
            glyph.glyphRect = new GlyphRect(placed.x + padding + 1, placed.y + padding + 1, rect.width, rect.height);
            return true;
        }

        internal static bool TryPackGlyphsInAtlas(List<Glyph> glyphsToAdd, List<Glyph> glyphsAdded, int padding, GlyphPackingMode packingMode, GlyphRenderMode renderMode, int width, int height, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects)
        {
            for (int i = 0; i < glyphsToAdd.Count; i++)
            {
                if (!TryPackGlyphInAtlas(glyphsToAdd[i], padding, packingMode, renderMode, width, height, freeGlyphRects, usedGlyphRects)) continue;
                glyphsAdded.Add(glyphsToAdd[i]);
                glyphsToAdd.RemoveAt(i--);
            }
            return glyphsToAdd.Count == 0;
        }

        // ---- Rendering into atlas textures ----

        internal static void SetTextureUploadMode(bool shouldUploadImmediately) { }

        /// <summary>Clears an atlas texture to transparent.</summary>
        internal static void ResetAtlasTexture(Texture2D texture)
        {
            if (texture == null) return;
            texture.m_Pixels = new Color32[texture.width * texture.height];
            if (texture.format == TextureFormat.Alpha8)
                for (int i = 0; i < texture.m_Pixels.Length; i++) texture.m_Pixels[i] = new Color32(255, 255, 255, 0);
            texture.m_PixelsDirty = true;
        }

        internal static FontEngineError RenderGlyphToTexture(Glyph glyph, int padding, GlyphRenderMode renderMode, Texture2D texture)
        {
            if (s_Face == null) return FontEngineError.Invalid_Face;
            if (texture == null) return FontEngineError.Invalid_File;
            var pixels = AtlasPixels(texture);
            // Alpha8 reads back white with the value as alpha; RGBA atlases (color modes) get white
            // glyphs, as color faces are rendered in coverage only.
            var error = Render(glyph, padding, renderMode, texture.width, texture.height, (i, v) => pixels[i] = new Color32(255, 255, 255, v));
            texture.m_PixelsDirty = true;
            return error;
        }

        internal static FontEngineError RenderGlyphsToTexture(List<Glyph> glyphs, int padding, GlyphRenderMode renderMode, Texture2D texture)
        {
            foreach (var glyph in glyphs)
            {
                var error = RenderGlyphToTexture(glyph, padding, renderMode, texture);
                if (error != FontEngineError.Success) return error;
            }
            return FontEngineError.Success;
        }

        /// <summary>Into an alpha buffer, rows bottom first.</summary>
        internal static FontEngineError RenderGlyphsToTexture(List<Glyph> glyphs, int padding, GlyphRenderMode renderMode, byte[] texBuffer, int texWidth, int texHeight)
        {
            if (s_Face == null) return FontEngineError.Invalid_Face;
            if (texBuffer == null || texBuffer.Length < texWidth * texHeight) return FontEngineError.Invalid_File;
            foreach (var glyph in glyphs)
            {
                var error = Render(glyph, padding, renderMode, texWidth, texHeight, (i, v) => texBuffer[i] = v);
                if (error != FontEngineError.Success) return error;
            }
            return FontEngineError.Success;
        }

        internal static bool TryAddGlyphToTexture(uint glyphIndex, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D texture, out Glyph glyph)
        {
            glyph = null;
            if (texture == null || !TryGetGlyphWithIndexValue(glyphIndex, GlyphLoadFlags.LOAD_NO_HINTING, out var loaded)) return false;
            if (!TryPackGlyphInAtlas(loaded, padding, packingMode, renderMode, texture.width, texture.height, freeGlyphRects, usedGlyphRects)) return false;
            if (loaded.glyphRect.width > 0 && RenderGlyphToTexture(loaded, padding, renderMode, texture) != FontEngineError.Success) return false;
            glyph = loaded;
            return true;
        }

        internal static bool TryAddGlyphsToTexture(List<Glyph> glyphsToAdd, List<Glyph> glyphsAdded, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D texture)
        {
            if (texture == null) return false;
            for (int i = 0; i < glyphsToAdd.Count; i++)
            {
                var glyph = glyphsToAdd[i];
                if (!TryPackGlyphInAtlas(glyph, padding, packingMode, renderMode, texture.width, texture.height, freeGlyphRects, usedGlyphRects)) continue;
                if (glyph.glyphRect.width > 0) RenderGlyphToTexture(glyph, padding, renderMode, texture);
                glyphsAdded.Add(glyph);
                glyphsToAdd.RemoveAt(i--);
            }
            return glyphsToAdd.Count == 0;
        }

        /// <summary>Adds the glyphs that fit; <paramref name="glyphs"/> lists them first, in order,
        /// followed by nulls for those that did not.</summary>
        internal static bool TryAddGlyphsToTexture(List<uint> glyphIndexes, int padding, GlyphPackingMode packingMode, List<GlyphRect> freeGlyphRects, List<GlyphRect> usedGlyphRects, GlyphRenderMode renderMode, Texture2D texture, out Glyph[] glyphs)
        {
            glyphs = new Glyph[glyphIndexes?.Count ?? 0];
            if (glyphIndexes == null || texture == null) return false;
            int added = 0;
            foreach (var index in glyphIndexes)
                if (TryAddGlyphToTexture(index, padding, packingMode, freeGlyphRects, usedGlyphRects, renderMode, texture, out var glyph))
                    glyphs[added++] = glyph;
            return added == glyphIndexes.Count;
        }

        static Color32[] AtlasPixels(Texture2D texture)
        {
            if (texture.m_Pixels == null || texture.m_Pixels.Length != texture.width * texture.height)
                ResetAtlasTexture(texture);
            return texture.m_Pixels;
        }

        /// <summary>Renders a glyph at its glyph rect (rows bottom first, as the rect counts them):
        /// SDF modes fill the padding around it with the distance field, the others write coverage.</summary>
        static FontEngineError Render(Glyph glyph, int padding, GlyphRenderMode renderMode, int width, int height, Action<int, byte> write)
        {
            if (glyph == null) return FontEngineError.Invalid_Glyph_Index;
            var rect = glyph.glyphRect;
            if (rect.width <= 0 || rect.height <= 0) return FontEngineError.Success;
            int mode = (int)renderMode;
            bool sdf = (mode & (k_RasterModeSdf | k_RasterModeSdfaa)) != 0;
            bool mono = (mode & k_RasterModeMono) != 0 && (mode & k_RasterModeColor) == 0;
            int grow = sdf ? Math.Max(0, padding) : 0;
            if (!Native.FontRenderGlyph(s_Face.Handle, glyph.index, s_PointSize, sdf ? grow : -1, out var info, out var pixels))
                return FontEngineError.Invalid_Glyph_Index;
            if (pixels == null) return FontEngineError.Success;
            int w = info.Width + grow * 2;
            int h = info.Height + grow * 2;
            if (pixels.Length < w * h) return FontEngineError.Invalid_Glyph_Index;
            int left = rect.x - grow;
            int bottom = rect.y - grow;
            for (int row = 0; row < h; row++)
            {
                int y = bottom + (h - 1 - row);
                if (y < 0 || y >= height) continue;
                for (int column = 0; column < w; column++)
                {
                    int x = left + column;
                    if (x < 0 || x >= width) continue;
                    byte v = pixels[row * w + column];
                    if (mono) v = v >= 128 ? (byte)255 : (byte)0;
                    write(y * width + x, v);
                }
            }
            return FontEngineError.Success;
        }

        // ---- Kerning ----

        /// <summary>Pairs from the native face: first glyph, second glyph, x advance in font units.</summary>
        static List<int[]> Kerning(IEnumerable<uint> firsts, IEnumerable<uint> seconds)
        {
            if (s_Face == null) return new List<int[]>();
            try
            {
                var request = JsonSerializer.Serialize(new { handle = s_Face.Handle, firsts, seconds });
                return JsonSerializer.Deserialize<List<int[]>>(Native.Call("font-kerning", request)) ?? new List<int[]>();
            }
            catch (Exception)
            {
                return new List<int[]>();
            }
        }

        static GlyphPairAdjustmentRecord[] Records(List<int[]> pairs, float scale)
        {
            var records = new GlyphPairAdjustmentRecord[pairs.Count];
            for (int i = 0; i < records.Length; i++)
            {
                var p = pairs[i];
                records[i] = new GlyphPairAdjustmentRecord(
                    new GlyphAdjustmentRecord((uint)p[0], new GlyphValueRecord(0f, 0f, p[2] * scale, 0f)),
                    new GlyphAdjustmentRecord((uint)p[1], default));
            }
            return records;
        }

        /// <summary>Scales font units to the current size (the older, pixel-valued queries).</summary>
        static float PixelScale => s_Face != null ? s_PointSize / Math.Max(1, s_Face.Info.UnitsPerEm) : 1f;

        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentTable(uint[] glyphIndexes) =>
            glyphIndexes == null ? Array.Empty<GlyphPairAdjustmentRecord>() : Records(Kerning(glyphIndexes, glyphIndexes), PixelScale);

        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentRecords(List<uint> glyphIndexes, out int recordCount)
        {
            var records = glyphIndexes == null ? Array.Empty<GlyphPairAdjustmentRecord>() : Records(Kerning(glyphIndexes, glyphIndexes), PixelScale);
            recordCount = records.Length;
            return records;
        }

        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentRecords(uint glyphIndex, out int recordCount)
        {
            var records = Records(Kerning(new[] { glyphIndex }, null), PixelScale);
            recordCount = records.Length;
            return records;
        }

        internal static GlyphPairAdjustmentRecord[] GetGlyphPairAdjustmentRecords(List<uint> newGlyphIndexes, List<uint> allGlyphIndexes)
        {
            if (newGlyphIndexes == null || allGlyphIndexes == null) return Array.Empty<GlyphPairAdjustmentRecord>();
            var pairs = Kerning(newGlyphIndexes, allGlyphIndexes);
            pairs.AddRange(Kerning(allGlyphIndexes, newGlyphIndexes));
            var seen = new HashSet<(int, int)>();
            pairs.RemoveAll(p => !seen.Add((p[0], p[1])));
            return Records(pairs, PixelScale);
        }

        internal static GlyphPairAdjustmentRecord GetGlyphPairAdjustmentRecord(uint firstGlyphIndex, uint secondGlyphIndex)
        {
            var records = Records(Kerning(new[] { firstGlyphIndex }, new[] { secondGlyphIndex }), PixelScale);
            return records.Length > 0 ? records[0] : default;
        }

        /// <summary>Every pair whose first glyph is <paramref name="glyphIndex"/>, in font units.</summary>
        internal static GlyphPairAdjustmentRecord[] GetPairAdjustmentRecords(uint glyphIndex) => Records(Kerning(new[] { glyphIndex }, null), 1f);

        internal static GlyphPairAdjustmentRecord GetPairAdjustmentRecord(uint firstGlyphIndex, uint secondGlyphIndex)
        {
            var records = Records(Kerning(new[] { firstGlyphIndex }, new[] { secondGlyphIndex }), 1f);
            return records.Length > 0 ? records[0] : default;
        }

        internal static GlyphPairAdjustmentRecord[] GetAllPairAdjustmentRecords() => Records(Kerning(null, null), 1f);

        /// <summary>Every pair whose first glyph is one of <paramref name="glyphIndexes"/>, in font
        /// units: as glyphs are added, the pairs with any glyph added later are already known.</summary>
        internal static GlyphPairAdjustmentRecord[] GetPairAdjustmentRecords(List<uint> glyphIndexes) =>
            glyphIndexes == null || glyphIndexes.Count == 0 ? Array.Empty<GlyphPairAdjustmentRecord>() : Records(Kerning(glyphIndexes, null), 1f);
    }

    /// <summary>MaxRects bin packing (Jylänki) on lists of free and used rects, y up.</summary>
    internal static class GlyphPacker
    {
        internal static bool TryPack(int width, int height, GlyphPackingMode mode, List<GlyphRect> free, List<GlyphRect> used, out GlyphRect placed)
        {
            placed = default;
            if (free == null || used == null) return false;
            int best = -1;
            long bestScore = long.MaxValue, bestTie = long.MaxValue;
            for (int i = 0; i < free.Count; i++)
            {
                var f = free[i];
                if (f.width < width || f.height < height) continue;
                int leftoverX = f.width - width, leftoverY = f.height - height;
                long score, tie;
                switch (mode)
                {
                    case GlyphPackingMode.BestLongSideFit:
                        score = Math.Max(leftoverX, leftoverY);
                        tie = Math.Min(leftoverX, leftoverY);
                        break;
                    case GlyphPackingMode.BestAreaFit:
                        score = (long)f.width * f.height - (long)width * height;
                        tie = Math.Min(leftoverX, leftoverY);
                        break;
                    case GlyphPackingMode.BottomLeftRule:
                        score = f.y + height;
                        tie = f.x;
                        break;
                    case GlyphPackingMode.ContactPointRule:
                        score = -Contact(f.x, f.y, width, height, free, used);
                        tie = 0;
                        break;
                    default:
                        score = Math.Min(leftoverX, leftoverY);
                        tie = Math.Max(leftoverX, leftoverY);
                        break;
                }
                if (score < bestScore || (score == bestScore && tie < bestTie))
                {
                    best = i;
                    bestScore = score;
                    bestTie = tie;
                }
            }
            if (best < 0) return false;
            placed = new GlyphRect(free[best].x, free[best].y, width, height);
            int count = free.Count;
            for (int i = 0; i < count; i++)
            {
                if (!Split(free[i], placed, free)) continue;
                free.RemoveAt(i);
                i--;
                count--;
            }
            Prune(free);
            used.Add(placed);
            return true;
        }

        static int Contact(int x, int y, int width, int height, List<GlyphRect> free, List<GlyphRect> used)
        {
            // Edges along the atlas bounds count too: the bounds are the union of the rects.
            int maxX = 0, maxY = 0;
            foreach (var r in free) { maxX = Math.Max(maxX, r.x + r.width); maxY = Math.Max(maxY, r.y + r.height); }
            foreach (var r in used) { maxX = Math.Max(maxX, r.x + r.width); maxY = Math.Max(maxY, r.y + r.height); }
            int score = 0;
            if (x == 0 || x + width == maxX) score += height;
            if (y == 0 || y + height == maxY) score += width;
            foreach (var r in used)
            {
                if (r.x == x + width || r.x + r.width == x) score += Overlap(r.y, r.y + r.height, y, y + height);
                if (r.y == y + height || r.y + r.height == y) score += Overlap(r.x, r.x + r.width, x, x + width);
            }
            return score;
        }

        static int Overlap(int a0, int a1, int b0, int b1) => a1 < b0 || b1 < a0 ? 0 : Math.Min(a1, b1) - Math.Max(a0, b0);

        /// <summary>Replaces a free rect the placed one overlaps by the free parts around it.</summary>
        static bool Split(GlyphRect f, GlyphRect p, List<GlyphRect> free)
        {
            if (p.x >= f.x + f.width || p.x + p.width <= f.x || p.y >= f.y + f.height || p.y + p.height <= f.y) return false;
            if (p.x < f.x + f.width && p.x + p.width > f.x)
            {
                if (p.y > f.y && p.y < f.y + f.height) free.Add(new GlyphRect(f.x, f.y, f.width, p.y - f.y));
                if (p.y + p.height < f.y + f.height) free.Add(new GlyphRect(f.x, p.y + p.height, f.width, f.y + f.height - (p.y + p.height)));
            }
            if (p.y < f.y + f.height && p.y + p.height > f.y)
            {
                if (p.x > f.x && p.x < f.x + f.width) free.Add(new GlyphRect(f.x, f.y, p.x - f.x, f.height));
                if (p.x + p.width < f.x + f.width) free.Add(new GlyphRect(p.x + p.width, f.y, f.x + f.width - (p.x + p.width), f.height));
            }
            return true;
        }

        /// <summary>Drops free rects contained in others.</summary>
        static void Prune(List<GlyphRect> free)
        {
            for (int i = 0; i < free.Count; i++)
            {
                for (int j = i + 1; j < free.Count; j++)
                {
                    if (Contains(free[j], free[i]))
                    {
                        free.RemoveAt(i--);
                        break;
                    }
                    if (Contains(free[i], free[j])) free.RemoveAt(j--);
                }
            }
        }

        static bool Contains(GlyphRect outer, GlyphRect inner) =>
            inner.x >= outer.x && inner.y >= outer.y && inner.x + inner.width <= outer.x + outer.width && inner.y + inner.height <= outer.y + outer.height;
    }
}
