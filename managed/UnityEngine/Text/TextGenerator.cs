using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    [Flags]
    internal enum TextGenerationError
    {
        None = 0,
        CustomSizeOnNonDynamicFont = 1,
        CustomStyleOnNonDynamicFont = 2,
        NoFont = 4,
    }

    /// <summary>What a <see cref="TextGenerator"/> lays text out with.</summary>
    public partial struct TextGenerationSettings
    {
        public Font font;
        public Color color;
        public int fontSize;
        public float lineSpacing;
        public bool richText;
        public float scaleFactor;
        public FontStyle fontStyle;
        public TextAnchor textAnchor;
        public bool alignByGeometry;
        public bool resizeTextForBestFit;
        public int resizeTextMinSize;
        public int resizeTextMaxSize;
        public bool updateBounds;
        public VerticalWrapMode verticalOverflow;
        public HorizontalWrapMode horizontalOverflow;
        public Vector2 generationExtents;
        public Vector2 pivot;
        public bool generateOutOfBounds;

        bool CompareColors(Color left, Color right) =>
            Mathf.Approximately(left.r, right.r) && Mathf.Approximately(left.g, right.g) &&
            Mathf.Approximately(left.b, right.b) && Mathf.Approximately(left.a, right.a);

        bool CompareVector2(Vector2 left, Vector2 right) =>
            Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);

        public bool Equals(TextGenerationSettings other) =>
            CompareColors(color, other.color) && fontSize == other.fontSize && Mathf.Approximately(scaleFactor, other.scaleFactor) &&
            resizeTextMinSize == other.resizeTextMinSize && resizeTextMaxSize == other.resizeTextMaxSize &&
            Mathf.Approximately(lineSpacing, other.lineSpacing) && fontStyle == other.fontStyle && richText == other.richText &&
            textAnchor == other.textAnchor && alignByGeometry == other.alignByGeometry &&
            resizeTextForBestFit == other.resizeTextForBestFit && updateBounds == other.updateBounds &&
            horizontalOverflow == other.horizontalOverflow && verticalOverflow == other.verticalOverflow &&
            CompareVector2(generationExtents, other.generationExtents) && CompareVector2(pivot, other.pivot) && font == other.font;
    }

    /// <summary>A character of generated text: where the cursor before it is, and its advance.</summary>
    public partial struct UICharInfo
    {
        public Vector2 cursorPos;
        public float charWidth;
    }

    /// <summary>A line of generated text.</summary>
    public partial struct UILineInfo
    {
        public int startCharIdx;
        public int height;
        public float topY;
        public float leading;
    }

    /// <summary>
    /// Lays text out with a font as Unity's legacy text generator does (UI Text, TextMesh): rich text,
    /// wrapping, alignment, best fit; it gives 4 vertices per visible character (top left, top right,
    /// bottom right, bottom left), one <see cref="UICharInfo"/> per character plus the end, and the lines.
    /// Positions are in pixels (settings' units times <c>scaleFactor</c>) relative to the pivot.
    /// </summary>
    public sealed partial class TextGenerator : IDisposable
    {
        internal IntPtr m_Ptr;
        string m_LastString;
        TextGenerationSettings m_LastSettings;
        bool m_HasGenerated;
        TextGenerationError m_LastValid;
        int m_Generation;
        readonly List<UIVertex> m_Verts;
        readonly List<UICharInfo> m_Characters;
        readonly List<UILineInfo> m_Lines;
        Rect m_RectExtents;
        int m_FontSizeUsedForBestFit;

        static int s_Generation;

        public TextGenerator() : this(50) { }

        public TextGenerator(int initialCapacity)
        {
            m_Verts = new List<UIVertex>((initialCapacity + 1) * 4);
            m_Characters = new List<UICharInfo>(initialCapacity + 1);
            m_Lines = new List<UILineInfo>(20);
        }

        void IDisposable.Dispose() { }

        /// <summary>Invalidates every generator (fonts changed).</summary>
        internal static void InvalidateAll() => s_Generation++;

        public void Invalidate() => m_HasGenerated = false;

        public int characterCountVisible => characterCount - 1;

        public IList<UIVertex> verts => m_Verts;

        public IList<UICharInfo> characters => m_Characters;

        public IList<UILineInfo> lines => m_Lines;

        public Rect rectExtents => m_RectExtents;

        public int vertexCount => m_Verts.Count;

        public int characterCount => m_Characters.Count;

        public int lineCount => m_Lines.Count;

        public int fontSizeUsedForBestFit => m_FontSizeUsedForBestFit;

        public void GetCharacters(List<UICharInfo> characters)
        {
            characters.Clear();
            characters.AddRange(m_Characters);
        }

        public void GetLines(List<UILineInfo> lines)
        {
            lines.Clear();
            lines.AddRange(m_Lines);
        }

        public void GetVertices(List<UIVertex> vertices)
        {
            vertices.Clear();
            vertices.AddRange(m_Verts);
        }

        public UIVertex[] GetVerticesArray() => m_Verts.ToArray();

        public UICharInfo[] GetCharactersArray() => m_Characters.ToArray();

        public UILineInfo[] GetLinesArray() => m_Lines.ToArray();

        public float GetPreferredWidth(string str, TextGenerationSettings settings)
        {
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.updateBounds = true;
            Populate(str, settings);
            return rectExtents.width;
        }

        public float GetPreferredHeight(string str, TextGenerationSettings settings)
        {
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.updateBounds = true;
            Populate(str, settings);
            return rectExtents.height;
        }

        public bool PopulateWithErrors(string str, TextGenerationSettings settings, GameObject context)
        {
            var error = PopulateWithError(str, settings);
            if (error == TextGenerationError.None) return true;
            if ((error & TextGenerationError.CustomSizeOnNonDynamicFont) != 0)
                Debug.LogErrorFormat(context, "Font '{0}' is not dynamic, which is required to override its size", settings.font);
            if ((error & TextGenerationError.CustomStyleOnNonDynamicFont) != 0)
                Debug.LogErrorFormat(context, "Font '{0}' is not dynamic, which is required to override its style", settings.font);
            return false;
        }

        public bool Populate(string str, TextGenerationSettings settings) => PopulateWithError(str, settings) == TextGenerationError.None;

        TextGenerationError PopulateWithError(string str, TextGenerationSettings settings)
        {
            if (m_HasGenerated && m_Generation == s_Generation && str == m_LastString && settings.Equals(m_LastSettings))
                return m_LastValid;
            m_LastValid = PopulateAlways(str, settings);
            return m_LastValid;
        }

        TextGenerationError PopulateAlways(string str, TextGenerationSettings settings)
        {
            m_LastString = str;
            m_HasGenerated = true;
            m_Generation = s_Generation;
            m_LastSettings = settings;
            var valid = ValidatedSettings(settings);
            Populate_Internal(str, valid.font, valid.color, valid.fontSize, valid.scaleFactor, valid.lineSpacing, valid.fontStyle,
                valid.richText, valid.resizeTextForBestFit, valid.resizeTextMinSize, valid.resizeTextMaxSize, valid.verticalOverflow,
                valid.horizontalOverflow, valid.updateBounds, valid.textAnchor, valid.generationExtents, valid.pivot,
                valid.generateOutOfBounds, valid.alignByGeometry, out var error);
            m_LastValid = error;
            return error;
        }

        TextGenerationSettings ValidatedSettings(TextGenerationSettings settings)
        {
            if (settings.font != null && settings.font.dynamic) return settings;
            if (settings.fontSize != 0 || settings.fontStyle != FontStyle.Normal)
            {
                if (settings.font != null)
                    Debug.LogWarningFormat(settings.font, "Font size and style overrides are only supported for dynamic fonts. Font '{0}' is not dynamic.", settings.font.name);
                settings.fontSize = 0;
                settings.fontStyle = FontStyle.Normal;
            }
            if (settings.resizeTextForBestFit)
            {
                if (settings.font != null)
                    Debug.LogWarningFormat(settings.font, "BestFit is only supported for dynamic fonts. Font '{0}' is not dynamic.", settings.font.name);
                settings.resizeTextForBestFit = false;
            }
            return settings;
        }

        internal bool Populate_Internal(string str, Font font, Color color, int fontSize, float scaleFactor, float lineSpacing, FontStyle style,
            bool richText, bool resizeTextForBestFit, int resizeTextMinSize, int resizeTextMaxSize, int verticalOverFlow, int horizontalOverflow,
            bool updateBounds, TextAnchor anchor, float extentsX, float extentsY, float pivotX, float pivotY, bool generateOutOfBounds,
            bool alignByGeometry, out uint error)
        {
            bool result = Populate_Internal(str, font, color, fontSize, scaleFactor, lineSpacing, style, richText, resizeTextForBestFit,
                resizeTextMinSize, resizeTextMaxSize, (VerticalWrapMode)verticalOverFlow, (HorizontalWrapMode)horizontalOverflow, updateBounds,
                anchor, new Vector2(extentsX, extentsY), new Vector2(pivotX, pivotY), generateOutOfBounds, alignByGeometry, out var e);
            error = (uint)e;
            return result;
        }

        internal bool Populate_Internal(string str, Font font, Color color, int fontSize, float scaleFactor, float lineSpacing, FontStyle style,
            bool richText, bool resizeTextForBestFit, int resizeTextMinSize, int resizeTextMaxSize, VerticalWrapMode verticalOverFlow,
            HorizontalWrapMode horizontalOverflow, bool updateBounds, TextAnchor anchor, Vector2 extents, Vector2 pivot, bool generateOutOfBounds,
            bool alignByGeometry, out TextGenerationError error)
        {
            m_Verts.Clear();
            m_Characters.Clear();
            m_Lines.Clear();
            m_RectExtents = default;
            m_FontSizeUsedForBestFit = 0;
            if (font == null)
            {
                error = TextGenerationError.NoFont;
                return false;
            }
            error = TextGenerationError.None;
            var request = new TextLayout.Request
            {
                Text = str ?? "",
                Font = font,
                Color = color,
                FontSize = fontSize,
                ScaleFactor = scaleFactor,
                LineSpacing = lineSpacing,
                Style = style,
                RichText = richText,
                BestFit = resizeTextForBestFit,
                MinSize = resizeTextMinSize,
                MaxSize = resizeTextMaxSize,
                VerticalOverflow = verticalOverFlow,
                HorizontalOverflow = horizontalOverflow,
                UpdateBounds = updateBounds,
                Anchor = anchor,
                Extents = extents,
                Pivot = pivot,
                AlignByGeometry = alignByGeometry,
            };
            TextLayout.Generate(request, m_Verts, m_Characters, m_Lines, out m_RectExtents, out m_FontSizeUsedForBestFit);
            return true;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>The legacy text layout behind <see cref="TextGenerator"/>.</summary>
    internal static class TextLayout
    {
        internal struct Request
        {
            internal string Text;
            internal Font Font;
            internal Color Color;
            internal int FontSize;
            internal float ScaleFactor;
            internal float LineSpacing;
            internal FontStyle Style;
            internal bool RichText;
            internal bool BestFit;
            internal int MinSize;
            internal int MaxSize;
            internal VerticalWrapMode VerticalOverflow;
            internal HorizontalWrapMode HorizontalOverflow;
            internal bool UpdateBounds;
            internal TextAnchor Anchor;
            internal Vector2 Extents;
            internal Vector2 Pivot;
            internal bool AlignByGeometry;
        }

        internal enum Kind : byte
        {
            /// <summary>A character drawn with a glyph (whitespace too: it advances without vertices).</summary>
            Glyph,
            /// <summary>Part of a rich text tag, or the second half of a surrogate pair: nothing.</summary>
            Hidden,
            Newline,
            Quad,
        }

        /// <summary>One UTF-16 unit of the text.</summary>
        internal struct Item
        {
            internal Kind Kind;
            internal int Codepoint;
            /// <summary>The size a size tag sets (in settings units), 0 for the base size.</summary>
            internal float TagSize;
            internal FontStyle Style;
            internal Color32 Color;
            internal float QuadSize;
            internal Rect QuadUV;
            // Measured at a base size:
            internal int PixelSize;
            internal float Advance;
            internal bool Visible;
            internal float MinX, MaxX, MinY, MaxY;
            internal AtlasGlyph Glyph;
            internal int CustomIndex;
        }

        struct Line
        {
            internal int Start;
            /// <summary>One past the last item (a newline belongs to its line).</summary>
            internal int End;
            internal float Width;
            internal float GeometryMin;
            internal float GeometryMax;
            internal bool HasGeometry;
            internal int Size;
            /// <summary>The font's line spacing at the size, unrounded (`UILineInfo.height` truncates it).</summary>
            internal float Height;
            /// <summary>Ascent and descent rounded to pixels: the baseline is `Ascent` below the
            /// line's top, and the last line of a block takes `Ascent + Descent`.</summary>
            internal int Ascent;
            internal int Descent;

            /// <summary>The line spacing rounded to pixels, before the settings' line spacing.</summary>
            internal int Advance => (int)Round(Height);

            /// <summary>From this line's top to the next one's.</summary>
            internal int Step(float lineSpacing) => (int)Round(Advance * lineSpacing);

            internal int Body => Ascent + Descent;
        }

        const float Epsilon = 0.001f;

        static float Round(float v) => (float)Math.Floor(v + 0.5f);

        internal static void Generate(Request r, List<UIVertex> verts, List<UICharInfo> chars, List<UILineInfo> lines, out Rect extents, out int bestFitSize)
        {
            var font = r.Font;
            float scale = r.ScaleFactor > 0f ? r.ScaleFactor : 1f;
            var ext = r.Extents * scale;
            var items = RichText.Parse(r.Text, r.RichText, r.Style, (Color32)r.Color);

            int baseSize = font.dynamic && r.FontSize > 0 ? r.FontSize : font.fontSize;
            int pixelSize = PixelSizeOf(font, baseSize, scale);
            bestFitSize = 0;
            List<Line> layout;
            // Best fit: the largest size that fits, words whole; along an axis that overflows
            // everything fits, and text that overflows both ways keeps its size (measured:
            // preferred heights of best-fit text are its maximum size's, preferred widths its own).
            if (r.BestFit && font.dynamic && !(r.HorizontalOverflow == HorizontalWrapMode.Overflow && r.VerticalOverflow == VerticalWrapMode.Overflow))
            {
                int lo = Math.Max(1, PixelSizeOf(font, r.MinSize, scale)), hi = Math.Max(lo, PixelSizeOf(font, r.MaxSize, scale));
                int best = lo;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    Measure(items, font, mid, scale);
                    var trial = BreakLines(items, font, r, ext.x, mid);
                    if (Fits(trial, r, ext))
                    {
                        best = mid;
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }
                pixelSize = best;
            }
            bestFitSize = pixelSize;
            Measure(items, font, pixelSize, scale);
            // Put the glyphs in the font texture; this may rebuild it (and raise textureRebuilt).
            RequestGlyphs(items, font);
            layout = BreakLines(items, font, r, ext.x, pixelSize);

            int kept = layout.Count;
            if (r.VerticalOverflow == VerticalWrapMode.Truncate)
            {
                int used = 0;
                for (int i = 0; i < layout.Count; i++)
                {
                    if (used + layout[i].Body > ext.y + Epsilon)
                    {
                        kept = i;
                        break;
                    }
                    used += layout[i].Step(r.LineSpacing);
                }
            }

            int blockHeight = 0;
            float maxWidth = 0f;
            for (int i = 0; i < kept; i++)
            {
                blockHeight += i < kept - 1 ? layout[i].Step(r.LineSpacing) : layout[i].Body;
                maxWidth = Math.Max(maxWidth, layout[i].Width);
            }

            // The rectangle grows out to whole pixels; lines are placed in it on whole pixels
            // (measured: a 301 by 101 rectangle centred on the origin spans -151..151, -51..51).
            float xMinRaw = -r.Pivot.x * ext.x, yMaxRaw = (1f - r.Pivot.y) * ext.y;
            float xMin = Floor(xMinRaw), xMax = Ceil(xMinRaw + ext.x);
            float yMax = Ceil(yMaxRaw), yMin = Floor(yMaxRaw - ext.y);
            float align = Horizontal(r.Anchor);
            float vertical = Vertical(r.Anchor);
            float top0 = Floor(yMax - (yMax - yMin - blockHeight) * vertical);

            float lastTop = top0, lastX = Floor(xMin + (xMax - xMin) * align);
            int advanceSum = 0;
            for (int li = 0; li < kept; li++)
            {
                var line = layout[li];
                float top = top0 - advanceSum;
                advanceSum += line.Step(r.LineSpacing);
                float baseline = top - line.Ascent;
                float width = r.AlignByGeometry && line.HasGeometry ? line.GeometryMax - line.GeometryMin : line.Width;
                float start = r.AlignByGeometry && line.HasGeometry ? line.GeometryMin : 0f;
                float x0 = Floor(xMin + (xMax - xMin - width) * align) - start;
                lines.Add(new UILineInfo
                {
                    startCharIdx = line.Start,
                    height = (int)line.Height,
                    topY = top,
                    leading = line.Advance - line.Body,
                });
                float pen = 0f;
                for (int i = line.Start; i < line.End; i++)
                {
                    var item = items[i];
                    chars.Add(new UICharInfo { cursorPos = new Vector2(x0 + pen, top), charWidth = item.Advance });
                    if (item.Visible) EmitQuad(verts, item, font, x0 + pen, baseline);
                    pen += item.Advance;
                }
                lastTop = top;
                lastX = x0 + pen;
            }
            chars.Add(new UICharInfo { cursorPos = new Vector2(lastX, lastTop), charWidth = 0f });

            // Measured: the bounds start at the origin; along an axis the text overflows they are
            // the text's size, otherwise the generation extents.
            extents = r.UpdateBounds
                ? new Rect(0f, 0f, r.HorizontalOverflow == HorizontalWrapMode.Overflow ? maxWidth : ext.x,
                    r.VerticalOverflow == VerticalWrapMode.Overflow ? blockHeight : ext.y)
                : new Rect(0f, 0f, ext.x, ext.y);
        }

        static float Floor(float v) => (float)Math.Floor(v + Epsilon);

        static float Ceil(float v) => (float)Math.Ceiling(v - Epsilon);

        /// <summary>A size in pixels: the scaled size truncated (measured: 32 at 0.703125 is 22).</summary>
        static int PixelSizeOf(Font font, int size, float scale) =>
            font.dynamic ? Math.Max(1, (int)(size * scale)) : font.PixelSize(0);

        static float Horizontal(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    return 0.5f;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return 1f;
                default:
                    return 0f;
            }
        }

        static float Vertical(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    return 0.5f;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    return 1f;
                default:
                    return 0f;
            }
        }

        static bool Fits(List<Line> lines, Request r, Vector2 ext)
        {
            if (s_BrokeWord) return false;
            int height = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                height += i < lines.Count - 1 ? lines[i].Step(r.LineSpacing) : lines[i].Body;
                if (r.HorizontalOverflow == HorizontalWrapMode.Wrap && lines[i].Width > ext.x + Epsilon) return false;
            }
            return r.VerticalOverflow == VerticalWrapMode.Overflow || height <= ext.y + Epsilon;
        }

        static bool IsSpace(int c) => c < 0x10000 && char.IsWhiteSpace((char)c);

        /// <summary>Advances and glyph boxes of the items at a base pixel size.</summary>
        static void Measure(Item[] items, Font font, int basePixelSize, float scale)
        {
            float spaceAdvance = -1f;
            for (int i = 0; i < items.Length; i++)
            {
                ref var item = ref items[i];
                item.Visible = false;
                item.Advance = 0f;
                item.Glyph = null;
                item.CustomIndex = -1;
                item.PixelSize = item.TagSize > 0f && font.dynamic ? Math.Max(1, (int)(item.TagSize * scale)) : basePixelSize;
                if (item.Kind == Kind.Quad)
                {
                    float size = item.QuadSize > 0f ? item.QuadSize * scale : item.PixelSize;
                    item.Advance = size;
                    item.Visible = size > 0f;
                    item.MinX = 0f;
                    item.MaxX = size;
                    item.MinY = 0f;
                    item.MaxY = size;
                    continue;
                }
                if (item.Kind != Kind.Glyph) continue;
                var style = font.dynamic ? item.Style : FontStyle.Normal;
                if (font.m_Source != null)
                {
                    var glyph = font.Atlas.Get(new GlyphKey(item.Codepoint, item.PixelSize, style));
                    item.Glyph = glyph;
                    item.Advance = glyph.Found ? glyph.Advance : 0f;
                    if (item.Codepoint == '\t' && (!glyph.Found || glyph.Advance <= 0f))
                    {
                        if (spaceAdvance < 0f) spaceAdvance = font.Atlas.Get(new GlyphKey(' ', item.PixelSize, style)).Advance;
                        item.Advance = spaceAdvance * 4f;
                    }
                    item.Visible = glyph.Found && glyph.HasPixels && !IsSpace(item.Codepoint);
                    item.MinX = glyph.Left;
                    item.MaxX = glyph.Left + glyph.Width;
                    item.MaxY = glyph.Top;
                    item.MinY = glyph.Top - glyph.Height;
                }
                else
                {
                    var rects = font.m_CharacterRects;
                    for (int k = 0; k < rects.Count; k++)
                    {
                        if (rects[k].index != item.Codepoint) continue;
                        var info = rects[k];
#pragma warning disable 618
                        item.CustomIndex = k;
                        item.Advance = info.width;
                        item.MinX = info.vert.x;
                        item.MaxX = info.vert.x + info.vert.width;
                        item.MaxY = info.vert.y;
                        item.MinY = info.vert.y + info.vert.height;
#pragma warning restore 618
                        item.Visible = !IsSpace(item.Codepoint) && item.MaxX > item.MinX;
                        break;
                    }
                }
                if (font.m_Tracking != 1f && font.m_Source == null) item.Advance *= font.m_Tracking;
            }
        }

        static void RequestGlyphs(Item[] items, Font font)
        {
            if (font.m_Source == null) return;
            var keys = new List<GlyphKey>();
            foreach (var item in items)
                if (item.Visible && item.Glyph != null) keys.Add(item.Glyph.Key);
            if (keys.Count > 0) font.Atlas.Request(keys);
        }

        /// <summary>Breaks the items into lines: at newlines and, when wrapping, after the last space
        /// that keeps the line within the width (a word longer than the width breaks between characters).</summary>
        /// <summary>Whether the last <see cref="BreakLines"/> had to break a word between characters.</summary>
        [ThreadStatic] static bool s_BrokeWord;

        static List<Line> BreakLines(Item[] items, Font font, Request r, float width, int basePixelSize)
        {
            s_BrokeWord = false;
            var lines = new List<Line>();
            bool wrap = r.HorizontalOverflow == HorizontalWrapMode.Wrap;
            int start = 0;
            float pen = 0f;
            int lastSpace = -1;
            bool hasContent = false;
            int i = 0;
            while (i < items.Length)
            {
                var item = items[i];
                if (item.Kind == Kind.Newline)
                {
                    lines.Add(MakeLine(items, font, start, i + 1, false, basePixelSize));
                    start = i + 1;
                    pen = 0f;
                    lastSpace = -1;
                    hasContent = false;
                    i++;
                    continue;
                }
                if (item.Kind == Kind.Hidden)
                {
                    i++;
                    continue;
                }
                bool space = item.Kind == Kind.Glyph && IsSpace(item.Codepoint);
                if (wrap && !space && hasContent && pen + item.Advance > width + Epsilon)
                {
                    int next = lastSpace >= start ? lastSpace + 1 : i;
                    if (lastSpace < start) s_BrokeWord = true;
                    lines.Add(MakeLine(items, font, start, next, true, basePixelSize));
                    start = next;
                    pen = 0f;
                    lastSpace = -1;
                    hasContent = false;
                    i = next;
                    continue;
                }
                pen += item.Advance;
                if (space) lastSpace = i;
                else hasContent = true;
                i++;
            }
            lines.Add(MakeLine(items, font, start, items.Length, false, basePixelSize));
            return lines;
        }

        static Line MakeLine(Item[] items, Font font, int start, int end, bool wrapped, int basePixelSize)
        {
            var line = new Line { Start = start, End = end, Size = 0 };
            float pen = 0f, width = 0f;
            for (int i = start; i < end; i++)
            {
                var item = items[i];
                if (item.Kind == Kind.Glyph || item.Kind == Kind.Quad)
                {
                    line.Size = Math.Max(line.Size, item.PixelSize);
                    if (item.Visible)
                    {
                        float min = pen + item.MinX, max = pen + item.MaxX;
                        if (!line.HasGeometry)
                        {
                            line.GeometryMin = min;
                            line.GeometryMax = max;
                            line.HasGeometry = true;
                        }
                        else
                        {
                            line.GeometryMin = Math.Min(line.GeometryMin, min);
                            line.GeometryMax = Math.Max(line.GeometryMax, max);
                        }
                    }
                    pen += item.Advance;
                    // A wrapped line does not count the spaces it was broken at.
                    if (!wrapped || item.Kind == Kind.Quad || !IsSpace(item.Codepoint)) width = pen;
                }
            }
            if (line.Size == 0)
            {
                // An empty line has the size in effect where it starts (or where the text ends).
                int at = Math.Min(start, items.Length - 1);
                line.Size = at >= 0 && items[at].PixelSize > 0 ? items[at].PixelSize : basePixelSize;
            }
            line.Width = width;
            line.Height = font.LineSpacingAt(line.Size);
            line.Ascent = (int)Round(font.AscentAt(line.Size));
            line.Descent = (int)Round(-font.DescentAt(line.Size));
            return line;
        }

        static void EmitQuad(List<UIVertex> verts, Item item, Font font, float x, float baseline)
        {
            // A dynamic font's glyph quad takes its character padding on every side (measured).
            int pad = item.Kind == Kind.Glyph && item.Glyph != null ? font.Atlas.Padding : 0;
            float left = x + item.MinX - pad, right = x + item.MaxX + pad;
            float top = baseline + item.MaxY + pad, bottom = baseline + item.MinY - pad;
            Vector2 tl, tr, br, bl;
            if (item.Kind == Kind.Quad)
            {
                var uv = item.QuadUV;
                bl = new Vector2(uv.xMin, uv.yMin);
                br = new Vector2(uv.xMax, uv.yMin);
                tr = new Vector2(uv.xMax, uv.yMax);
                tl = new Vector2(uv.xMin, uv.yMax);
            }
            else if (item.Glyph != null)
            {
                var uv = item.Glyph.Placed ? font.Atlas.UV(item.Glyph, pad) : default;
                bl = new Vector2(uv.xMin, uv.yMin);
                br = new Vector2(uv.xMax, uv.yMin);
                tr = new Vector2(uv.xMax, uv.yMax);
                tl = new Vector2(uv.xMin, uv.yMax);
            }
            else
            {
                var info = font.m_CharacterRects[item.CustomIndex];
                bl = info.uvBottomLeft;
                br = info.uvBottomRight;
                tr = info.uvTopRight;
                tl = info.uvTopLeft;
            }
            var v = UIVertex.simpleVert;
            v.color = item.Color;
            v.position = new Vector3(left, top, 0f);
            v.uv0 = new Vector4(tl.x, tl.y, 0f, 0f);
            verts.Add(v);
            v.position = new Vector3(right, top, 0f);
            v.uv0 = new Vector4(tr.x, tr.y, 0f, 0f);
            verts.Add(v);
            v.position = new Vector3(right, bottom, 0f);
            v.uv0 = new Vector4(br.x, br.y, 0f, 0f);
            verts.Add(v);
            v.position = new Vector3(left, bottom, 0f);
            v.uv0 = new Vector4(bl.x, bl.y, 0f, 0f);
            verts.Add(v);
        }
    }

    /// <summary>
    /// The legacy rich text tags: &lt;b&gt;, &lt;i&gt;, &lt;size=N&gt;, &lt;color=name|#rrggbb[aa]&gt;,
    /// &lt;material=N&gt; and &lt;quad ...&gt;. A tag only acts when it is well formed and, except quad,
    /// closed in the right nesting; otherwise it is ordinary text.
    /// </summary>
    internal static class RichText
    {
        struct Tag
        {
            internal int Start;
            internal int End;
            internal string Name;
            internal bool Closing;
            internal string Value;
            internal bool Valid;
        }

        internal static TextLayout.Item[] Parse(string text, bool rich, FontStyle style, Color32 color)
        {
            var items = new TextLayout.Item[text.Length];
            var tags = rich ? FindTags(text) : new List<Tag>();
            int tagIndex = 0;
            int bold = 0, italic = 0;
            var sizes = new Stack<float>();
            var colors = new Stack<Color32>();
            for (int i = 0; i < text.Length; i++)
            {
                while (tagIndex < tags.Count && (!tags[tagIndex].Valid || tags[tagIndex].End < i)) tagIndex++;
                if (tagIndex < tags.Count && tags[tagIndex].Start == i)
                {
                    var tag = tags[tagIndex];
                    for (int k = tag.Start; k <= tag.End; k++) items[k].Kind = TextLayout.Kind.Hidden;
                    var current = CurrentStyle(style, bold, italic);
                    var currentColor = colors.Count > 0 ? colors.Peek() : color;
                    float currentSize = sizes.Count > 0 ? sizes.Peek() : 0f;
                    switch (tag.Name)
                    {
                        case "b": bold += tag.Closing ? -1 : 1; break;
                        case "i": italic += tag.Closing ? -1 : 1; break;
                        case "size":
                            if (tag.Closing) sizes.Pop();
                            else sizes.Push(ParseSize(tag.Value));
                            break;
                        case "color":
                            if (tag.Closing) colors.Pop();
                            else colors.Push(ParseColor(tag.Value).Value);
                            break;
                        case "quad":
                        {
                            // The quad takes the tag's first character; the rest of the tag is hidden.
                            var attributes = Attributes(tag.Value);
                            ref var q = ref items[tag.Start];
                            q.Kind = TextLayout.Kind.Quad;
                            q.Style = current;
                            q.Color = currentColor;
                            q.TagSize = currentSize;
                            q.QuadSize = Attr(attributes, "size", currentSize);
                            q.QuadUV = new Rect(Attr(attributes, "x", 0f), Attr(attributes, "y", 0f), Attr(attributes, "width", 1f), Attr(attributes, "height", 1f));
                            break;
                        }
                    }
                    for (int k = tag.Start; k <= tag.End; k++)
                    {
                        if (items[k].Kind == TextLayout.Kind.Quad) continue;
                        items[k].Style = CurrentStyle(style, bold, italic);
                        items[k].Color = colors.Count > 0 ? colors.Peek() : color;
                        items[k].TagSize = sizes.Count > 0 ? sizes.Peek() : 0f;
                    }
                    i = tag.End;
                    tagIndex++;
                    continue;
                }
                ref var item = ref items[i];
                item.Style = CurrentStyle(style, bold, italic);
                item.Color = colors.Count > 0 ? colors.Peek() : color;
                item.TagSize = sizes.Count > 0 ? sizes.Peek() : 0f;
                char c = text[i];
                if (c == '\n')
                {
                    item.Kind = TextLayout.Kind.Newline;
                }
                else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    item.Kind = TextLayout.Kind.Glyph;
                    item.Codepoint = char.ConvertToUtf32(c, text[i + 1]);
                    items[i + 1] = item;
                    items[i + 1].Kind = TextLayout.Kind.Hidden;
                    i++;
                }
                else if (c == '\r' || char.IsSurrogate(c))
                {
                    item.Kind = TextLayout.Kind.Hidden;
                }
                else
                {
                    item.Kind = TextLayout.Kind.Glyph;
                    item.Codepoint = c;
                }
            }
            return items;
        }

        static FontStyle CurrentStyle(FontStyle style, int bold, int italic)
        {
            bool b = bold > 0 || style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
            bool i = italic > 0 || style == FontStyle.Italic || style == FontStyle.BoldAndItalic;
            return b && i ? FontStyle.BoldAndItalic : b ? FontStyle.Bold : i ? FontStyle.Italic : FontStyle.Normal;
        }

        /// <summary>The tags of a text, with matched pairs (and quads) marked valid.</summary>
        static List<Tag> FindTags(string text)
        {
            var tags = new List<Tag>();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '<') continue;
                int end = text.IndexOf('>', i + 1);
                if (end < 0) break;
                int nested = text.IndexOf('<', i + 1, end - i - 1);
                if (nested >= 0) continue;
                var content = text.Substring(i + 1, end - i - 1);
                var tag = new Tag { Start = i, End = end };
                if (content.StartsWith("/"))
                {
                    tag.Closing = true;
                    tag.Name = content.Substring(1).ToLowerInvariant();
                }
                else
                {
                    int eq = content.IndexOf('=');
                    int sp = content.IndexOf(' ');
                    int cut = eq >= 0 && (sp < 0 || eq < sp) ? eq : sp;
                    tag.Name = (cut >= 0 ? content.Substring(0, cut) : content).ToLowerInvariant();
                    tag.Value = cut >= 0 ? content.Substring(cut + (cut == eq ? 1 : 0)) : null;
                }
                if (!IsKnown(tag))
                    continue;
                tags.Add(tag);
                i = end;
            }
            var open = new List<int>();
            for (int t = 0; t < tags.Count; t++)
            {
                var tag = tags[t];
                if (tag.Name == "quad")
                {
                    if (!tag.Closing) MarkValid(tags, t);
                    continue;
                }
                if (!tag.Closing)
                {
                    open.Add(t);
                    continue;
                }
                if (open.Count > 0 && tags[open[open.Count - 1]].Name == tag.Name)
                {
                    MarkValid(tags, open[open.Count - 1]);
                    MarkValid(tags, t);
                    open.RemoveAt(open.Count - 1);
                }
            }
            return tags;
        }

        static void MarkValid(List<Tag> tags, int index)
        {
            var tag = tags[index];
            tag.Valid = true;
            tags[index] = tag;
        }

        static bool IsKnown(Tag tag)
        {
            switch (tag.Name)
            {
                case "b":
                case "i":
                    return tag.Closing || tag.Value == null;
                case "size":
                    return tag.Closing || ParseSize(tag.Value) > 0f;
                case "color":
                    return tag.Closing || ParseColor(tag.Value).HasValue;
                case "material":
                    return tag.Closing || int.TryParse(Unquote(tag.Value), out _);
                case "quad":
                    return !tag.Closing;
                default:
                    return false;
            }
        }

        static string Unquote(string value)
        {
            if (value == null) return null;
            value = value.Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                value = value.Substring(1, value.Length - 2);
            return value;
        }

        static float ParseSize(string value)
        {
            var v = Unquote(value);
            return float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size) ? size : -1f;
        }

        static readonly Dictionary<string, Color32> s_Colors = new Dictionary<string, Color32>
        {
            ["aqua"] = new Color32(0, 255, 255, 255),
            ["black"] = new Color32(0, 0, 0, 255),
            ["blue"] = new Color32(0, 0, 255, 255),
            ["brown"] = new Color32(165, 42, 42, 255),
            ["cyan"] = new Color32(0, 255, 255, 255),
            ["darkblue"] = new Color32(0, 0, 160, 255),
            ["fuchsia"] = new Color32(255, 0, 255, 255),
            ["green"] = new Color32(0, 128, 0, 255),
            ["grey"] = new Color32(128, 128, 128, 255),
            ["lightblue"] = new Color32(173, 216, 230, 255),
            ["lime"] = new Color32(0, 255, 0, 255),
            ["magenta"] = new Color32(255, 0, 255, 255),
            ["maroon"] = new Color32(128, 0, 0, 255),
            ["navy"] = new Color32(0, 0, 128, 255),
            ["olive"] = new Color32(128, 128, 0, 255),
            ["orange"] = new Color32(255, 165, 0, 255),
            ["purple"] = new Color32(128, 0, 128, 255),
            ["red"] = new Color32(255, 0, 0, 255),
            ["silver"] = new Color32(192, 192, 192, 255),
            ["teal"] = new Color32(0, 128, 128, 255),
            ["white"] = new Color32(255, 255, 255, 255),
            ["yellow"] = new Color32(255, 255, 0, 255),
        };

        internal static Color32? ParseColor(string value)
        {
            var v = Unquote(value);
            if (string.IsNullOrEmpty(v)) return null;
            if (v[0] != '#') return s_Colors.TryGetValue(v.ToLowerInvariant(), out var named) ? named : (Color32?)null;
            if (v.Length != 7 && v.Length != 9) return null;
            var bytes = new byte[4];
            bytes[3] = 255;
            for (int k = 0; k < (v.Length - 1) / 2; k++)
            {
                if (!byte.TryParse(v.Substring(1 + k * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[k])) return null;
            }
            return new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
        }

        static Dictionary<string, string> Attributes(string value)
        {
            var result = new Dictionary<string, string>();
            if (value == null) return result;
            foreach (var part in value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                result[part.Substring(0, eq).ToLowerInvariant()] = Unquote(part.Substring(eq + 1).TrimEnd('/'));
            }
            return result;
        }

        static float Attr(Dictionary<string, string> attributes, string name, float fallback) =>
            attributes.TryGetValue(name, out var v) &&
            float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;
    }
}
