using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>A face glyphs are rendered from: font-unit metrics and a rasterizer.</summary>
    internal abstract class GlyphSource
    {
        internal int UnitsPerEm = 2048;
        internal int Ascender;
        internal int Descender;
        internal int LineGap;
        internal int YMax;
        internal int YMin;
        internal string Family = "";
        internal string Style = "";

        internal abstract bool HasCharacter(int codepoint);

        /// <summary>The glyph at a pixel size (em), FreeType-style: false when the face lacks it.</summary>
        internal abstract bool Render(int codepoint, int size, FontStyle style, out RenderedGlyph glyph);
    }

    /// <summary>A rasterized glyph: pixels relative to the pen on the baseline, coverage top row first.</summary>
    internal struct RenderedGlyph
    {
        internal float Advance;
        internal int Left;
        internal int Top;
        internal int Width;
        internal int Height;
        internal byte[] Coverage;
    }

    /// <summary>A face opened on the native side (`font-open`), rendered through `font_glyph`.</summary>
    internal sealed class NativeGlyphSource : GlyphSource
    {
        internal sealed class FontSettingsJson
        {
            public int FontSize { get; set; } = 16;
            public int TextureCase { get; set; } = -2;
            public int CharacterSpacing { get; set; }
            public int CharacterPadding { get; set; } = 1;
            public string[] FontNames { get; set; } = Array.Empty<string>();
            public string[] FallbackFonts { get; set; } = Array.Empty<string>();
            public int RenderingMode { get; set; }
            public int AscentCalculationMode { get; set; } = 1;
            public bool ShouldRoundAdvanceValue { get; set; } = true;
        }

        sealed class OpenJson
        {
            public ulong Handle { get; set; }
            public int UnitsPerEm { get; set; }
            public int Ascender { get; set; }
            public int Descender { get; set; }
            public int LineGap { get; set; }
            public int YMax { get; set; }
            public int YMin { get; set; }
            public string Family { get; set; }
            public string Style { get; set; }
            public bool Found { get; set; } = true;
            public FontSettingsJson Settings { get; set; }
        }

        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        readonly ulong m_Handle;
        readonly Dictionary<int, bool> m_Has = new Dictionary<int, bool>();
        /// <summary>The TrueTypeFontImporter settings of a project font.</summary>
        internal FontSettingsJson Settings;

        NativeGlyphSource(ulong handle) { m_Handle = handle; }

        /// <summary>The native face (`font_glyph`, `font-face`, ...).</summary>
        internal ulong Handle => m_Handle;

        /// <summary>Opens a face (`{guid}`, `{file}`, `{data}`, `{builtin}` or `{names}`, with an
        /// optional `faceIndex`); null when it fails, logged unless <paramref name="quiet"/>.</summary>
        internal static NativeGlyphSource Open(object request, bool quiet = false)
        {
            if (!Native.IsReady) return null;
            OpenJson info;
            try
            {
                info = JsonSerializer.Deserialize<OpenJson>(Native.Call("font-open", JsonSerializer.Serialize(request, s_Json)), s_Json);
            }
            catch (Exception e)
            {
                if (!quiet) Debug.LogWarning($"Unbound: {e.Message}");
                return null;
            }
            return new NativeGlyphSource(info.Handle)
            {
                UnitsPerEm = info.UnitsPerEm,
                Ascender = info.Ascender,
                Descender = info.Descender,
                LineGap = info.LineGap,
                YMax = info.YMax,
                YMin = info.YMin,
                Family = info.Family ?? "",
                Style = info.Style ?? "",
                Found = info.Found,
                Settings = info.Settings,
            };
        }

        /// <summary>An OS font by family names, or null when none of them is installed.</summary>
        internal static NativeGlyphSource OpenOsFont(string[] names)
        {
            var source = Open(new { names });
            return source != null && source.Found ? source : null;
        }

        /// <summary>Whether the face is what was asked for (an OS font request may get the built-in font).</summary>
        internal bool Found = true;

        /// <summary>The OS fonts dynamic fonts fall back to for characters they lack: per group, the
        /// first installed one (wide coverage, then CJK).</summary>
        static readonly string[][] s_OsFallbackGroups =
        {
            new[] { "Noto Sans", "DejaVu Sans", "Arial Unicode MS", "Segoe UI", "Helvetica Neue" },
            new[] { "Noto Sans CJK SC", "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans CJK TC", "Source Han Sans SC",
                    "WenQuanYi Zen Hei", "Droid Sans Fallback", "Microsoft YaHei", "PingFang SC", "Hiragino Sans" },
        };

        internal static string[] OsFallbackNames
        {
            get
            {
                var names = new List<string>();
                foreach (var group in s_OsFallbackGroups) names.AddRange(group);
                return names.ToArray();
            }
        }

        static GlyphSource s_OsFallback;
        static bool s_OsFallbackOpened;

        internal static GlyphSource OsFallback
        {
            get
            {
                if (s_OsFallbackOpened || !Native.IsReady) return s_OsFallback;
                s_OsFallbackOpened = true;
                var sources = new List<GlyphSource>();
                foreach (var group in s_OsFallbackGroups)
                {
                    var source = OpenOsFont(group);
                    if (source != null) sources.Add(source);
                }
                s_OsFallback = sources.Count > 0 ? new ChainGlyphSource(sources) : null;
                return s_OsFallback;
            }
        }

        internal override bool HasCharacter(int codepoint)
        {
            if (m_Has.TryGetValue(codepoint, out var has)) return has;
            try
            {
                has = Native.Call("font-has-character", JsonSerializer.Serialize(new { handle = m_Handle, codepoint })) == "true";
            }
            catch (Exception)
            {
                has = false;
            }
            return m_Has[codepoint] = has;
        }

        internal override bool Render(int codepoint, int size, FontStyle style, out RenderedGlyph glyph)
        {
            glyph = default;
            if (!Native.FontGlyph(m_Handle, (uint)codepoint, size, (int)style, out var info, out var coverage)) return false;
            m_Has[codepoint] = true;
            glyph = new RenderedGlyph
            {
                Advance = info.Advance,
                Left = info.Left,
                Top = info.Top,
                Width = coverage != null ? info.Width : 0,
                Height = coverage != null ? info.Height : 0,
                Coverage = coverage,
            };
            return true;
        }
    }

    /// <summary>The first of several faces that has a character.</summary>
    internal sealed class ChainGlyphSource : GlyphSource
    {
        readonly List<GlyphSource> m_Sources;

        internal ChainGlyphSource(List<GlyphSource> sources)
        {
            m_Sources = sources;
            var first = sources[0];
            UnitsPerEm = first.UnitsPerEm;
            Ascender = first.Ascender;
            Descender = first.Descender;
            LineGap = first.LineGap;
            YMax = first.YMax;
            YMin = first.YMin;
            Family = first.Family;
        }

        internal GlyphSource First => m_Sources[0];

        internal override bool HasCharacter(int codepoint)
        {
            foreach (var s in m_Sources)
                if (s.HasCharacter(codepoint)) return true;
            return false;
        }

        internal override bool Render(int codepoint, int size, FontStyle style, out RenderedGlyph glyph)
        {
            foreach (var s in m_Sources)
                if (s.Render(codepoint, size, style, out glyph)) return true;
            glyph = default;
            return false;
        }
    }

    internal readonly struct GlyphKey : IEquatable<GlyphKey>
    {
        internal readonly int Codepoint;
        internal readonly int Size;
        internal readonly FontStyle Style;

        internal GlyphKey(int codepoint, int size, FontStyle style)
        {
            Codepoint = codepoint;
            Size = size;
            Style = style;
        }

        public bool Equals(GlyphKey other) => Codepoint == other.Codepoint && Size == other.Size && Style == other.Style;
        public override bool Equals(object obj) => obj is GlyphKey other && Equals(other);
        public override int GetHashCode() => (Codepoint * 397 ^ Size) * 31 + (int)Style;
    }

    /// <summary>A glyph of a dynamic font: metrics (always) and its place in the font texture (once requested).</summary>
    internal sealed class AtlasGlyph
    {
        internal GlyphKey Key;
        internal bool Found;
        /// <summary>The advance layout uses: rounded when the font rounds advances.</summary>
        internal float Advance;
        internal int Left;
        internal int Top;
        internal int Width;
        internal int Height;
        internal byte[] Coverage;
        internal bool Placed;
        internal int X;
        internal int Y;

        internal bool HasPixels => Width > 0 && Height > 0 && Coverage != null;
    }

    /// <summary>
    /// The texture of a dynamic font: an Alpha8 atlas glyphs are rendered into on request. When a request
    /// does not fit, the atlas is rebuilt (grown, or at its largest size cleared down to the glyphs being
    /// requested) and <see cref="Font.textureRebuilt"/> is raised, as Unity's dynamic fonts do.
    /// </summary>
    internal sealed class FontAtlas
    {
        internal const int InitialSize = 256;
        internal const int MaxSize = 4096;
        /// <summary>Glyph bitmaps kept for measuring beyond those in the texture, before the cache is trimmed.</summary>
        const int MaxCachedGlyphs = 16384;

        readonly Font m_Font;
        internal readonly Texture2D Texture;
        readonly Dictionary<GlyphKey, AtlasGlyph> m_Glyphs = new Dictionary<GlyphKey, AtlasGlyph>();
        readonly List<AtlasGlyph> m_Placed = new List<AtlasGlyph>();
        int m_ShelfX;
        int m_ShelfY;
        int m_ShelfHeight;
        bool m_WarnedFull;

        /// <summary>How many times the texture was rebuilt (tests).</summary>
        internal int RebuildCount;

        internal FontAtlas(Font font)
        {
            m_Font = font;
            // Created in the caller's id scope: an asset font's texture is an asset too.
            Texture = new Texture2D(InitialSize, InitialSize, TextureFormat.Alpha8, false)
            {
                name = "Font Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Texture.IsPersistentAsset = font.IsPersistentAsset;
            Texture.m_Pixels = new Color32[InitialSize * InitialSize];
            Texture.m_PixelsDirty = true;
        }

        internal int Padding => Math.Max(1, m_Font.m_CharacterPadding);

        /// <summary>A glyph's metrics (rendering it on first use); it is not put in the texture.</summary>
        internal AtlasGlyph Get(GlyphKey key)
        {
            if (m_Glyphs.TryGetValue(key, out var glyph)) return glyph;
            if (m_Glyphs.Count >= MaxCachedGlyphs) Trim();
            glyph = new AtlasGlyph { Key = key };
            foreach (var source in m_Font.Sources())
            {
                if (!source.Render(key.Codepoint, key.Size, key.Style, out var rendered)) continue;
                glyph.Found = true;
                glyph.Advance = m_Font.m_ShouldRoundAdvanceValue
                    ? (float)Math.Round(rendered.Advance, MidpointRounding.AwayFromZero)
                    : rendered.Advance;
                glyph.Left = rendered.Left;
                glyph.Top = rendered.Top;
                glyph.Width = rendered.Width;
                glyph.Height = rendered.Height;
                glyph.Coverage = rendered.Coverage;
                break;
            }
            m_Glyphs[key] = glyph;
            return glyph;
        }

        void Trim()
        {
            var drop = new List<GlyphKey>();
            foreach (var pair in m_Glyphs)
                if (!pair.Value.Placed) drop.Add(pair.Key);
            foreach (var key in drop) m_Glyphs.Remove(key);
        }

        /// <summary>Puts the glyphs in the texture. Returns true when the texture was rebuilt (and
        /// <see cref="Font.textureRebuilt"/> raised).</summary>
        internal bool Request(IReadOnlyList<GlyphKey> keys)
        {
            var pending = new List<AtlasGlyph>();
            var seen = new HashSet<GlyphKey>();
            foreach (var key in keys)
            {
                if (!seen.Add(key)) continue;
                var glyph = Get(key);
                if (glyph.HasPixels && !glyph.Placed) pending.Add(glyph);
            }
            if (pending.Count == 0) return false;
            pending.Sort(ByHeight);
            int placed = 0;
            for (; placed < pending.Count; placed++)
                if (!TryPlace(pending[placed])) break;
            if (placed == pending.Count)
            {
                foreach (var g in pending) Blit(g);
                Texture.m_PixelsDirty = true;
                return false;
            }
            // Undo the partial placement and rebuild with everything.
            for (int i = 0; i < placed; i++)
            {
                pending[i].Placed = false;
                m_Placed.Remove(pending[i]);
            }
            Rebuild(pending, seen);
            return true;
        }

        static int ByHeight(AtlasGlyph a, AtlasGlyph b) => b.Height != a.Height ? b.Height.CompareTo(a.Height) : b.Width.CompareTo(a.Width);

        void Rebuild(List<AtlasGlyph> pending, HashSet<GlyphKey> requested)
        {
            var all = new List<AtlasGlyph>(m_Placed);
            all.AddRange(pending);
            all.Sort(ByHeight);
            int width = Texture.m_Width, height = Texture.m_Height;
            while (!Fits(all, width, height))
            {
                if (width >= MaxSize && height >= MaxSize)
                {
                    // Full at the largest size: keep only what is being requested now.
                    all.RemoveAll(g => !requested.Contains(g.Key));
                    if (!m_WarnedFull)
                    {
                        m_WarnedFull = true;
                        Debug.LogWarning($"Unbound: the texture of font '{m_Font.m_Name}' is full at {MaxSize}x{MaxSize}; it was cleared.");
                    }
                    break;
                }
                if (width <= height) width = Math.Min(MaxSize, width * 2);
                else height = Math.Min(MaxSize, height * 2);
            }
            foreach (var g in m_Placed) g.Placed = false;
            m_Placed.Clear();
            Texture.m_Width = width;
            Texture.m_Height = height;
            Texture.m_Pixels = new Color32[width * height];
            m_ShelfX = m_ShelfY = m_ShelfHeight = 0;
            foreach (var g in all)
                if (TryPlace(g)) Blit(g);
            Texture.m_PixelsDirty = true;
            RebuildCount++;
            Font.InvokeTextureRebuilt_Internal(m_Font);
        }

        bool Fits(List<AtlasGlyph> glyphs, int width, int height)
        {
            int pad = Padding, x = pad, y = pad, shelf = 0;
            foreach (var g in glyphs)
            {
                int w = g.Width + pad, h = g.Height + pad;
                if (w + pad > width || h + pad > height) return false;
                if (x + w > width)
                {
                    y += shelf;
                    x = pad;
                    shelf = 0;
                }
                if (y + h > height) return false;
                x += w;
                shelf = Math.Max(shelf, h);
            }
            return true;
        }

        bool TryPlace(AtlasGlyph g)
        {
            // Every glyph keeps `pad` empty pixels on each side (the first row and column too):
            // its quad samples them.
            int pad = Padding, w = g.Width + pad, h = g.Height + pad;
            int width = Texture.m_Width, height = Texture.m_Height;
            if (w + pad > width || h + pad > height) return false;
            m_ShelfX = Math.Max(m_ShelfX, pad);
            m_ShelfY = Math.Max(m_ShelfY, pad);
            if (m_ShelfX + w > width)
            {
                m_ShelfY += m_ShelfHeight;
                m_ShelfX = pad;
                m_ShelfHeight = 0;
            }
            if (m_ShelfY + h > height) return false;
            g.X = m_ShelfX;
            g.Y = m_ShelfY;
            g.Placed = true;
            m_ShelfX += w;
            m_ShelfHeight = Math.Max(m_ShelfHeight, h);
            m_Placed.Add(g);
            return true;
        }

        /// <summary>Copies a glyph's coverage (top row first) into the texture (bottom row first).</summary>
        void Blit(AtlasGlyph g)
        {
            var pixels = Texture.m_Pixels;
            int width = Texture.m_Width;
            for (int row = 0; row < g.Height; row++)
            {
                int y = g.Y + g.Height - 1 - row;
                int src = row * g.Width;
                int dst = y * width + g.X;
                for (int x = 0; x < g.Width; x++)
                    pixels[dst + x] = new Color32(255, 255, 255, g.Coverage[src + x]);
            }
        }

        /// <summary>The glyph's rectangle in the texture, in uv.</summary>
        internal Rect UV(AtlasGlyph g, int pad = 0)
        {
            float w = Texture.m_Width, h = Texture.m_Height;
            return new Rect((g.X - pad) / w, (g.Y - pad) / h, (g.Width + 2 * pad) / w, (g.Height + 2 * pad) / h);
        }

        internal CharacterInfo ToCharacterInfo(AtlasGlyph g)
        {
#pragma warning disable 618
            return new CharacterInfo
            {
                index = g.Key.Codepoint,
                size = g.Key.Size,
                style = g.Key.Style,
                uv = g.Placed ? UV(g) : default,
                vert = new Rect(g.Left, g.Top, g.Width, -g.Height),
                width = g.Advance,
                flipped = false,
            };
#pragma warning restore 618
        }

        internal bool TryGetCharacterInfo(int codepoint, int size, FontStyle style, out CharacterInfo info)
        {
            if (m_Glyphs.TryGetValue(new GlyphKey(codepoint, size, style), out var g) && g.Found && (g.Placed || !g.HasPixels))
            {
                info = ToCharacterInfo(g);
                return true;
            }
            info = default;
            return false;
        }

        internal CharacterInfo[] CharacterInfos()
        {
            var list = new List<CharacterInfo>();
            foreach (var g in m_Glyphs.Values)
                if (g.Found && (g.Placed || !g.HasPixels)) list.Add(ToCharacterInfo(g));
            return list.ToArray();
        }
    }

    /// <summary>Unity's built-in fonts: LegacyRuntime.ttf, its material and texture.</summary>
    internal static class BuiltinFonts
    {
        static Font s_LegacyRuntime;
        static bool s_Failed;

        /// <summary>The default font of UI Text (`{fileID: 10102, guid: 0000000000000000e000000000000000}`).</summary>
        internal static Font LegacyRuntime
        {
            get
            {
                if (s_LegacyRuntime != null) return s_LegacyRuntime;
                var source = NativeGlyphSource.Open(new { builtin = "LegacyRuntime" });
                if (source == null && !s_Failed)
                {
                    s_Failed = true;
                    Debug.LogError("Unbound: the built-in font LegacyRuntime.ttf is not available; text using it is not drawn.");
                }
                using (ObjectRegistry.AsAssets())
                {
                    var font = new Font { m_Name = "LegacyRuntime", IsPersistentAsset = true };
                    // As Unity 6000.0's resources serialize it: an Arial-compatible face at size 13.
                    font.m_FontNames = new[] { "Arial" };
                    font.InitFromSource(source, 13, "LegacyRuntime");
                    s_LegacyRuntime = font;
                }
                return s_LegacyRuntime;
            }
        }

        /// <summary>A built-in object of `unity default resources` by name; null when it is not a font's.</summary>
        internal static Object Get(string name)
        {
            switch (name)
            {
                case "LegacyRuntime": return LegacyRuntime;
                case "Font Material": return LegacyRuntime.material;
                case "Font Texture": return LegacyRuntime.m_Texture;
                default: return null;
            }
        }
    }

    /// <summary>Font files (.ttf, .otf): a dynamic Font with its material and texture sub-assets.</summary>
    internal static class FontImporter
    {
        internal const long FontFileId = 12800000;
        internal const long MaterialFileId = 2100000;
        internal const long TextureFileId = 2800000;

        internal static void Register()
        {
            AssetDatabaseRuntime.Importers["font"] = Import;
            BuiltinResources.Factories["Font:LegacyRuntime.ttf"] = () => BuiltinFonts.LegacyRuntime;
        }

        static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(info.Path);
            var source = NativeGlyphSource.Open(new { guid = info.Guid });
            if (source == null) throw new InvalidOperationException("the font could not be opened");
            var settings = source.Settings ?? new NativeGlyphSource.FontSettingsJson();
            Font font;
            using (ObjectRegistry.AsAssets())
            {
                font = new Font { m_Name = name, IsPersistentAsset = true };
                if (settings.FontNames != null && settings.FontNames.Length > 0) font.m_FontNames = settings.FontNames;
                font.m_FallbackGuids = settings.FallbackFonts ?? Array.Empty<string>();
                font.m_ShouldRoundAdvanceValue = settings.ShouldRoundAdvanceValue;
                font.m_CharacterPadding = settings.CharacterPadding;
                font.InitFromSource(source, settings.FontSize, name, settings.AscentCalculationMode);
                // A font with a fixed character set is not dynamic: its glyphs have the import size.
                font.m_Dynamic = settings.TextureCase == -2;
            }
            var file = new LoadedFile { MainObject = font };
            file.Objects[FontFileId] = font;
            if (font.material != null) file.Objects[MaterialFileId] = font.material;
            if (font.m_Texture != null) file.Objects[TextureFileId] = font.m_Texture;
            return file;
        }
    }
}
