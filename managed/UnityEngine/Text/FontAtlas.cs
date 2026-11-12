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

        /// <summary>The glyph at a pixel size (em), FreeType-style: false when the face lacks it.
        /// <paramref name="renderingMode"/> is the requesting font's FontRenderingMode, which a
        /// fallback face renders in too.</summary>
        internal abstract bool Render(int codepoint, int size, FontStyle style, int renderingMode, out RenderedGlyph glyph);
    }

    /// <summary>A rasterized glyph: pixels relative to the pen on the baseline, coverage top row first.</summary>
    internal struct RenderedGlyph
    {
        internal float Advance;
        internal int Left;
        internal float Top;
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
        /// <summary>False for a face that provides metrics only: the Editor's built-in
        /// LegacyRuntime font holds no font data, its glyphs come from OS fonts.</summary>
        internal bool HasGlyphData = true;
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

        /// <summary>The OS families a legacy dynamic font falls back to, per character and in this
        /// order, after its own face, fallback fonts and font names (Unity 6000.0.71f1 Editor on
        /// Linux, tests/projects/glyphraster): common families, the platform's, then LastResort.</summary>
        internal static readonly string[] LegacyOsFallbackNames = {
            "Arial", "Arial Unicode MS", "Helvetica Neue", "Microsoft Sans Serif", "Microsoft YaHei",
            "Malgun Gothic", "Gulim", "MS Gothic", "Nirmala UI",
            "FreeSans", "Droid Sans", "DejaVu Sans", "Liberation Sans", "WenQuanYi Micro Hei", "HYSong",
            "NanumGothic", "TakaoGothic", "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans CJK SC",
            "Noto Sans CJK TC", "Droid Sans Fallback", "Noto Color Emoji",
            "LastResort",
        };

        static readonly Dictionary<string, NativeGlyphSource> s_OsFaces = new Dictionary<string, NativeGlyphSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The installed OS face of a family, opened once; null when it is not installed.</summary>
        internal static NativeGlyphSource OsFace(string family)
        {
            if (string.IsNullOrEmpty(family) || !Native.IsReady) return null;
            if (s_OsFaces.TryGetValue(family, out var face)) return face;
            face = OpenOsFont(new[] { family });
            s_OsFaces[family] = face;
            return face;
        }

        static GlyphSource s_LegacyOsFallback;
        static bool s_LegacyOsFallbackOpened;

        /// <summary>The installed faces of <see cref="LegacyOsFallbackNames"/>, first one having a
        /// character first; null when none is installed.</summary>
        internal static GlyphSource LegacyOsFallback
        {
            get
            {
                if (s_LegacyOsFallbackOpened || !Native.IsReady) return s_LegacyOsFallback;
                s_LegacyOsFallbackOpened = true;
                var sources = new List<GlyphSource>();
                foreach (var name in LegacyOsFallbackNames)
                {
                    var face = OsFace(name);
                    if (face != null && !sources.Contains(face)) sources.Add(face);
                }
                s_LegacyOsFallback = sources.Count > 0 ? new ChainGlyphSource(sources) : null;
                return s_LegacyOsFallback;
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
            if (!HasGlyphData) return false;
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

        internal override bool Render(int codepoint, int size, FontStyle style, int renderingMode, out RenderedGlyph glyph)
        {
            glyph = default;
            if (!HasGlyphData) return false;
            if (!Native.FontGlyph(m_Handle, (uint)codepoint, size, (int)style | ((renderingMode & 3) << 8), out var info, out var coverage)) return false;
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

        internal override bool Render(int codepoint, int size, FontStyle style, int renderingMode, out RenderedGlyph glyph)
        {
            foreach (var s in m_Sources)
                if (s.Render(codepoint, size, style, renderingMode, out glyph)) return true;
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
        internal float Top;
        internal int Width;
        internal int Height;
        internal byte[] Coverage;
        /// <summary>In the texture: its cell (bitmap and padding) at <see cref="X"/>, <see cref="Y"/>,
        /// the bitmap's top row at the cell's lowest v.</summary>
        internal bool Placed;
        internal int X;
        internal int Y;
        /// <summary>Stored a quarter turn round in the texture (CharacterInfo.flipped): a glyph wider
        /// than tall, as Unity stores them.</summary>
        internal bool Flipped;
        /// <summary>The last frame (Time.frameCount) a text asked for it.</summary>
        internal int Frame;

        internal bool HasPixels => Width > 0 && Height > 0 && Coverage != null;
        /// <summary>The size the glyph's bitmap takes in the texture.</summary>
        internal int StoredWidth => Flipped ? Height : Width;
        internal int StoredHeight => Flipped ? Width : Height;
    }

    /// <summary>
    /// The texture of a dynamic font: an Alpha8 atlas glyphs are rendered into on request, packed as the
    /// Unity 6000.0.71f1 Editor packs them (tests/projects/textmesh atlas controls).
    ///
    /// The texture is laid out in square blocks, row by row. Each glyph takes a cell (its bitmap, the
    /// character padding on every side and the character spacing) in the current block: at the first
    /// free corner from where the last search stopped, the corners ordered by x + y, then x; the cell
    /// then slides left and then down while it stays free, and its right and top corners become
    /// candidates. When no corner takes it, the next block starts empty. When the last block is full the
    /// texture is reset: it keeps only the characters asked for in this frame, grows when its area at
    /// the current density would not hold them, and they are packed again (in character order) with
    /// <see cref="Font.textureRebuilt"/> raised; a second miss doubles the smaller side.
    /// </summary>
    internal sealed class FontAtlas
    {
        internal const int InitialSize = 256;
        internal const int MaxSize = 4096;
        /// <summary>Glyph bitmaps kept for measuring beyond those in the texture, before the cache is trimmed.</summary>
        const int MaxCachedGlyphs = 16384;
        /// <summary>Characters at or below U+001F (control characters) never take a cell.</summary>
        const int FirstCachedCodepoint = 0x20;
        /// <summary>A reset's block side, at least: 8 times the font size, a power of two.</summary>
        const int ResetBlockScale = 8;
        /// <summary>A grown texture's block side, at least: 4 times the largest size asked for.</summary>
        const int GrowBlockScale = 4;
        /// <summary>The squared sizes per texel assumed before any glyph was placed, and the slack the
        /// estimate of the area needed allows.</summary>
        const float DefaultDensity = 2f;
        const float AreaSlack = 1.1f;

        readonly Font m_Font;
        internal readonly Texture2D Texture;
        readonly Dictionary<GlyphKey, AtlasGlyph> m_Glyphs = new Dictionary<GlyphKey, AtlasGlyph>();
        /// <summary>The glyphs in the texture, in the order they were placed.</summary>
        readonly List<AtlasGlyph> m_Placed = new List<AtlasGlyph>();
        /// <summary>The current block: its candidate corners, where the search goes on from (null: past
        /// the last corner) and the cells taken, in block coordinates.</summary>
        readonly SortedSet<(int X, int Y)> m_Corners = new SortedSet<(int X, int Y)>(CornerOrder.Instance);
        (int X, int Y)? m_Search;
        readonly List<RectInt> m_Cells = new List<RectInt>();
        /// <summary>The cells overlapping each square of <see cref="GridSide"/> texels of the block, so
        /// that free space is looked up near a cell instead of among all of them.</summary>
        List<int>[] m_Grid = Array.Empty<List<int>>();
        int m_GridColumns;
        readonly List<int> m_UsedSquares = new List<int>();
        const int GridSide = 32;
        int m_Block = 1;
        int m_BlockIndex;
        bool m_Started;
        readonly List<GlyphKey> m_Asked = new List<GlyphKey>();

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
                if (!source.Render(key.Codepoint, key.Size, key.Style, m_Font.m_RenderingMode, out var rendered)) continue;
                glyph.Found = true;
                glyph.Advance = m_Font.m_ShouldRoundAdvanceValue
                    ? (float)Math.Round(rendered.Advance, MidpointRounding.AwayFromZero)
                    : rendered.Advance;
                glyph.Left = rendered.Left;
                glyph.Top = rendered.Top;
                glyph.Width = rendered.Width;
                glyph.Height = rendered.Height;
                glyph.Coverage = rendered.Coverage;
                glyph.Flipped = rendered.Height < rendered.Width;
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

        /// <summary>Puts the characters of a text in the texture: every one above U+001F, with or
        /// without pixels (a space or a character the font lacks takes a cell of padding). Returns true
        /// when the texture was rebuilt (and <see cref="Font.textureRebuilt"/> raised).</summary>
        internal bool Request(IReadOnlyList<GlyphKey> keys)
        {
            int frame = Time.frameCount;
            // Nothing to place (the usual case once a text's glyphs are in): no work lists.
            if (AllPlaced(keys, frame)) return false;
            // Engine glyph writes take back the pixels before changing them or the atlas size.
            Texture.EnsurePixels();
            if (!m_Started) Reset();
            var asked = m_Asked;
            asked.Clear();
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].Codepoint >= FirstCachedCodepoint) asked.Add(keys[i]);
            asked.Sort(KeyOrder);
            Distinct(asked);
            long askedArea = 0;
            int largest = 0;
            foreach (var key in asked)
            {
                askedArea += (long)key.Size * key.Size;
                largest = Math.Max(largest, key.Size);
            }
            bool reset = false;
            while (!PlaceAll(asked, frame))
            {
                // A texture at its largest size that a reset cannot free (every glyph in it was asked
                // for in this frame, or it was just reset) stays as it is: what did not fit is left
                // out, with Unity's error, and no rebuild is announced (matches the Unity 6000.0.71f1
                // Editor, tests/projects/textcolor legacy/full). Glyphs of older frames are still
                // evicted by the reset below (unmeasured at the largest size).
                if (Texture.m_Width >= MaxSize && Texture.m_Height >= MaxSize && (reset || OnlyAskedIn(frame)))
                {
                    Debug.LogError($"Failed to update dynamic font ({m_Font.m_Name}) texture; all the needed characters do not fit onto a single texture (max size {MaxSize}). Try using less text or a smaller font size.");
                    break;
                }
                if (reset)
                {
                    if (Texture.m_Width < Texture.m_Height) Texture.m_Width *= 2;
                    else Texture.m_Height *= 2;
                    m_Block = Math.Min(Math.Max(NextPowerOfTwo(GrowBlockScale * largest), m_Block), Texture.m_Width);
                }
                else
                {
                    // Keep what this frame asked for; grow when the area at the current density would
                    // not hold it.
                    long placedArea = 0;
                    foreach (var g in m_Placed)
                    {
                        long area = (long)g.Key.Size * g.Key.Size;
                        placedArea += area;
                        if (g.Frame != frame || asked.BinarySearch(g.Key, KeyOrder) >= 0) continue;
                        asked.Insert(~asked.BinarySearch(g.Key, KeyOrder), g.Key);
                        askedArea += area;
                        largest = Math.Max(largest, g.Key.Size);
                    }
                    long texels = (long)Texture.m_Width * Texture.m_Height;
                    float density = placedArea == 0 ? DefaultDensity : (float)placedArea / texels;
                    long needed = (long)((float)askedArea / density * AreaSlack + 0.5f);
                    if (texels < needed)
                    {
                        int block = NextPowerOfTwo(GrowBlockScale * largest);
                        bool wider = Texture.m_Width < Texture.m_Height;
                        while (Texture.m_Width < MaxSize || Texture.m_Height < MaxSize)
                        {
                            if (wider) Texture.m_Width *= 2;
                            else Texture.m_Height *= 2;
                            m_Block = Math.Min(Math.Max(block, m_Block), Texture.m_Width);
                            if (needed <= (long)Texture.m_Width * Texture.m_Height) break;
                            wider = Texture.m_Width < Texture.m_Height;
                        }
                    }
                }
                Reset();
                reset = true;
            }
            Texture.m_PixelsDirty = true;
            if (!reset) return false;
            RebuildCount++;
            Font.InvokeTextureRebuilt_Internal(m_Font);
            return true;
        }

        /// <summary>Whether every glyph in the texture was asked for in <paramref name="frame"/>.</summary>
        bool OnlyAskedIn(int frame)
        {
            foreach (var g in m_Placed)
                if (g.Frame != frame) return false;
            return true;
        }

        /// <summary>Whether every character of <paramref name="keys"/> is in the texture, marking them
        /// asked for in this frame (a glyph not looked up yet is left to the full request).</summary>
        bool AllPlaced(IReadOnlyList<GlyphKey> keys, int frame)
        {
            bool all = true;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Codepoint < FirstCachedCodepoint) continue;
                if (m_Glyphs.TryGetValue(keys[i], out var glyph) && glyph.Placed) glyph.Frame = frame;
                else all = false;
            }
            return all;
        }

        /// <summary>Places the characters not yet in the texture, in order; false when one does not fit.</summary>
        bool PlaceAll(List<GlyphKey> asked, int frame)
        {
            foreach (var key in asked)
            {
                var glyph = Get(key);
                if (!glyph.Placed && !TryPlace(glyph)) return false;
                glyph.Frame = frame;
            }
            return true;
        }

        /// <summary>Character order: code point, size, style.</summary>
        static readonly Comparison<GlyphKey> s_KeyOrder = (a, b) =>
            a.Codepoint != b.Codepoint ? ((uint)a.Codepoint).CompareTo((uint)b.Codepoint)
            : a.Size != b.Size ? a.Size.CompareTo(b.Size)
            : ((uint)a.Style).CompareTo((uint)b.Style);
        static readonly IComparer<GlyphKey> KeyOrder = Comparer<GlyphKey>.Create(s_KeyOrder);

        static void Distinct(List<GlyphKey> sorted)
        {
            int kept = 0;
            for (int i = 0; i < sorted.Count; i++)
                if (kept == 0 || !sorted[i].Equals(sorted[kept - 1])) sorted[kept++] = sorted[i];
            sorted.RemoveRange(kept, sorted.Count - kept);
        }

        sealed class CornerOrder : IComparer<(int X, int Y)>
        {
            internal static readonly CornerOrder Instance = new CornerOrder();

            public int Compare((int X, int Y) a, (int X, int Y) b)
            {
                int sum = (a.X + a.Y).CompareTo(b.X + b.Y);
                return sum != 0 ? sum : a.X.CompareTo(b.X);
            }
        }

        static int NextPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value) power <<= 1;
            return power;
        }

        /// <summary>Clears the texture (at most <see cref="MaxSize"/> a side) and the glyphs in it, and
        /// starts the first block, its side at least 8 times the font size.</summary>
        void Reset()
        {
            Texture.m_Width = Math.Min(Texture.m_Width, MaxSize);
            Texture.m_Height = Math.Min(Texture.m_Height, MaxSize);
            foreach (var g in m_Placed) g.Placed = false;
            m_Placed.Clear();
            Texture.m_Pixels = new Color32[Texture.m_Width * Texture.m_Height];
            int block = NextPowerOfTwo(ResetBlockScale * Math.Max(1, (int)m_Font.m_FontSize));
            m_Block = Math.Min(Math.Max(block, m_Block), Texture.m_Width);
            m_BlockIndex = 0;
            StartBlock();
            m_Started = true;
        }

        void StartBlock()
        {
            m_Corners.Clear();
            m_Cells.Clear();
            foreach (var square in m_UsedSquares) m_Grid[square].Clear();
            m_UsedSquares.Clear();
            m_GridColumns = (m_Block + GridSide - 1) / GridSide;
            if (m_Grid.Length < m_GridColumns * m_GridColumns)
            {
                var grid = new List<int>[m_GridColumns * m_GridColumns];
                Array.Copy(m_Grid, grid, m_Grid.Length);
                m_Grid = grid;
            }
            m_Corners.Add((0, 0));
            m_Search = (0, 0);
        }

        /// <summary>The grid squares a rect of the block overlaps (clamped to the block).</summary>
        (int Left, int Bottom, int Right, int Top) Squares(int x, int y, int width, int height)
        {
            int last = m_GridColumns - 1;
            int left = Math.Clamp(x / GridSide, 0, last), bottom = Math.Clamp(y / GridSide, 0, last);
            // A cell of no width or height (a negative character spacing) still covers its square.
            return (left, bottom, Math.Clamp((x + Math.Max(1, width) - 1) / GridSide, left, last),
                Math.Clamp((y + Math.Max(1, height) - 1) / GridSide, bottom, last));
        }

        bool Free(int x, int y, int width, int height)
        {
            var (left, bottom, right, top) = Squares(x, y, width, height);
            for (int row = bottom; row <= top; row++)
                for (int column = left; column <= right; column++)
                {
                    var cells = m_Grid[row * m_GridColumns + column];
                    if (cells == null) continue;
                    foreach (int i in cells)
                    {
                        var c = m_Cells[i];
                        if (c.x < x + width && x < c.x + c.width && c.y < y + height && y < c.y + c.height) return false;
                    }
                }
            return true;
        }

        /// <summary>Where a free cell at (x, y) stops sliding left one texel at a time: at the nearest
        /// right edge of a cell beside it in its rows, or at 0.</summary>
        int SlideLeft(int x, int y, int width, int height)
        {
            int stop = 0;
            var (_, bottom, right, top) = Squares(0, y, x, height);
            for (int row = bottom; row <= top; row++)
                for (int column = 0; column <= right; column++)
                {
                    var cells = m_Grid[row * m_GridColumns + column];
                    if (cells == null) continue;
                    foreach (int i in cells)
                    {
                        var c = m_Cells[i];
                        if (c.y < y + height && y < c.y + c.height && c.x + c.width <= x) stop = Math.Max(stop, c.x + c.width);
                    }
                }
            return x <= 0 ? x : stop;
        }

        /// <summary>Where a free cell at (x, y) stops sliding down: at the nearest top edge of a cell
        /// below it in its columns, or at 0.</summary>
        int SlideDown(int x, int y, int width, int height)
        {
            int stop = 0;
            var (left, _, right, top) = Squares(x, 0, width, y);
            for (int row = 0; row <= top; row++)
                for (int column = left; column <= right; column++)
                {
                    var cells = m_Grid[row * m_GridColumns + column];
                    if (cells == null) continue;
                    foreach (int i in cells)
                    {
                        var c = m_Cells[i];
                        if (c.x < x + width && x < c.x + c.width && c.y + c.height <= y) stop = Math.Max(stop, c.y + c.height);
                    }
                }
            return y <= 0 ? y : stop;
        }

        void AddCell(RectInt cell)
        {
            int index = m_Cells.Count;
            m_Cells.Add(cell);
            var (left, bottom, right, top) = Squares(cell.x, cell.y, cell.width, cell.height);
            for (int row = bottom; row <= top; row++)
                for (int column = left; column <= right; column++)
                {
                    int square = row * m_GridColumns + column;
                    var cells = m_Grid[square] ??= new List<int>();
                    if (cells.Count == 0) m_UsedSquares.Add(square);
                    cells.Add(index);
                }
        }

        bool TryPlace(AtlasGlyph g)
        {
            int pad = Padding, spacing = m_Font.m_CharacterSpacing;
            int w = (g.HasPixels ? g.StoredWidth : 0) + 2 * pad + spacing;
            int h = (g.HasPixels ? g.StoredHeight : 0) + 2 * pad + spacing;
            while (true)
            {
                // From where the last search stopped to the last corner, then once more from the
                // first; a block whose corners all miss is left for the next.
                var first = m_Corners.Min;
                if (m_Search is { } from && TryCorners(g, from, w, h)) return true;
                if (!(m_Search is { } started && CornerOrder.Instance.Compare(started, first) == 0))
                {
                    m_Search = first;
                    if (TryCorners(g, first, w, h)) return true;
                }
                if ((long)(Texture.m_Width / m_Block) * (Texture.m_Height / m_Block) <= m_BlockIndex + 1) return false;
                m_BlockIndex++;
                StartBlock();
            }
        }

        /// <summary>The first corner from <paramref name="from"/> on where the cell fits, slid left and
        /// then down while it stays free.</summary>
        bool TryCorners(AtlasGlyph g, (int X, int Y) from, int w, int h)
        {
            foreach (var corner in m_Corners.GetViewBetween(from, LastCorner))
            {
                var (x, y) = corner;
                if (x < 0 || y < 0 || x + w > m_Block || y + h > m_Block || !Free(x, y, w, h)) continue;
                x = SlideLeft(x, y, w, h);
                y = SlideDown(x, y, w, h);
                Take(corner, x, y, w, h);
                Store(g, x, y);
                return true;
            }
            return false;
        }

        /// <summary>Beyond every corner a block can have.</summary>
        static readonly (int X, int Y) LastCorner = (int.MaxValue / 2, int.MaxValue / 2);

        /// <summary>Takes a cell found at <paramref name="corner"/> (slid to x, y): the search goes on
        /// after that corner, and the cell's right and top corners in the block become candidates.</summary>
        void Take((int X, int Y) corner, int x, int y, int w, int h)
        {
            m_Search = null;
            foreach (var next in m_Corners.GetViewBetween(corner, LastCorner))
            {
                if (CornerOrder.Instance.Compare(next, corner) == 0) continue;
                m_Search = next;
                break;
            }
            m_Corners.Remove(corner);
            AddCell(new RectInt(x, y, w, h));
            if (x + w < m_Block) m_Corners.Add((x + w, y));
            if (y + h < m_Block) m_Corners.Add((x, y + h));
        }

        /// <summary>Puts the glyph's cell at (x, y) of the current block and copies its bitmap in.</summary>
        void Store(AtlasGlyph g, int x, int y)
        {
            int origin = m_BlockIndex * m_Block;
            g.X = origin % Texture.m_Width + x;
            g.Y = origin / Texture.m_Width * m_Block + y;
            g.Placed = true;
            m_Placed.Add(g);
            if (g.HasPixels) Blit(g);
        }

        /// <summary>Copies a glyph's coverage (top row first) into its cell, the top row at the lowest
        /// v; a flipped glyph's column c of row r goes to (Height - 1 - r, Width - 1 - c) of the bitmap
        /// area.</summary>
        void Blit(AtlasGlyph g)
        {
            var pixels = Texture.m_Pixels;
            int width = Texture.m_Width, pad = Padding;
            int left = g.X + pad, bottom = g.Y + pad;
            for (int row = 0; row < g.Height; row++)
            {
                int src = row * g.Width;
                for (int column = 0; column < g.Width; column++)
                {
                    int u = g.Flipped ? left + g.Height - 1 - row : left + column;
                    int v = g.Flipped ? bottom + g.Width - 1 - column : bottom + row;
                    pixels[v * width + u] = new Color32(255, 255, 255, g.Coverage[src + column]);
                }
            }
        }

        /// <summary>The glyph's bitmap and padding in the texture as CharacterInfo.uv: from its cell's
        /// left and highest v, the height negative (the bitmap's top row is at the lowest v).</summary>
        internal Rect UV(AtlasGlyph g)
        {
            float w = Texture.m_Width, h = Texture.m_Height;
            int pad = Padding, cellWidth = (g.HasPixels ? g.StoredWidth : 0) + 2 * pad, cellHeight = (g.HasPixels ? g.StoredHeight : 0) + 2 * pad;
            return new Rect(g.X / w, (g.Y + cellHeight) / h, cellWidth / w, -cellHeight / h);
        }

        /// <summary>A glyph as CharacterInfo: its cell with the padding on every side (uvs and
        /// minX/maxX/minY/maxY, as the quads text layouts draw), its advance, and the size it was
        /// asked for (0 for the font's own size). Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/textmesh characterInfo.</summary>
        internal CharacterInfo ToCharacterInfo(AtlasGlyph g, int requestedSize)
        {
            int pad = Padding;
#pragma warning disable 618
            return new CharacterInfo
            {
                index = g.Key.Codepoint,
                size = requestedSize,
                style = g.Key.Style,
                uv = g.Placed ? UV(g) : default,
                vert = new Rect(g.Left - pad, g.Top + pad, g.Width + 2 * pad, -(g.Height + 2 * pad)),
                width = g.Advance,
                flipped = g.Flipped,
            };
#pragma warning restore 618
        }

        /// <summary>A character's CharacterInfo while it is in the texture (a character the font lacks
        /// too, with its cell of padding).</summary>
        internal bool TryGetCharacterInfo(int codepoint, int size, int requestedSize, FontStyle style, out CharacterInfo info)
        {
            if (m_Glyphs.TryGetValue(new GlyphKey(codepoint, size, style), out var g) && g.Placed)
            {
                info = ToCharacterInfo(g, requestedSize);
                return true;
            }
            info = default;
            return false;
        }

        /// <summary>The characters in the texture, in the order they were placed.</summary>
        internal CharacterInfo[] CharacterInfos()
        {
            var list = new CharacterInfo[m_Placed.Count];
            for (int i = 0; i < list.Length; i++) list[i] = ToCharacterInfo(m_Placed[i], m_Placed[i].Key.Size);
            return list;
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
                    // The Editor's resource has no font data: its metrics are serialized and its
                    // glyphs come from the OS faces of its font names and the fallback families.
                    // Without any of those installed, the player's face draws them.
                    if (source != null && Compat.LegacyGlyphRasterMeasured
                        && (NativeGlyphSource.OsFace("Arial") != null || NativeGlyphSource.LegacyOsFallback != null))
                        source.HasGlyphData = false;
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
                font.m_CharacterSpacing = settings.CharacterSpacing;
                font.m_RenderingMode = settings.RenderingMode;
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
