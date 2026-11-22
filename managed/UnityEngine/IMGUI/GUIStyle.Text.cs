using System;
using Unbound.Runtime;
using UnityEngine.TextCore.Text;

namespace UnityEngine
{
    /// <summary>
    /// A style's content: the size of its text and image, where they go in a rect, how they are
    /// drawn and where the text cursor is. Every text question (line height, size, line breaks,
    /// glyphs, cursor positions) is answered by one engine, TextCore through
    /// <see cref="IMGUITextHandle"/>, so what is measured is what is drawn. The rules match the
    /// Unity 6000.0.71f1 Editor, tests/projects/imguiskin (calcHeight, calcText and scroll rows).
    /// </summary>
    public sealed partial class GUIStyle
    {
        /// <summary>The height of the rect a wrapping text's height is measured in; an image beside
        /// such a text is also scaled as if it had that much height.</summary>
        const float WrapMeasureHeight = 1000f;

        /// <summary>The most a wrapping text asks for as its minimum width.</summary>
        const float WrapMinimumWidth = 32f;

        /// <summary>The gap between an image and the text on its right.</summary>
        const float ImageTextGap = 1f;

        /// <summary>The font's line height at the style's size, in whole pixels.</summary>
        public float lineHeight => Mathf.Round(IMGUITextHandle.GetLineHeight(this));

        static Font s_DefaultFont;

        /// <summary>The font a style without one draws with: the current skin's (set as it becomes
        /// current), else the built-in font.</summary>
        internal static void SetDefaultFont(Font font) => s_DefaultFont = font;

        internal static Font GetDefaultFont() => s_DefaultFont != null ? s_DefaultFont : BuiltinGuiSkin.Font;

        static bool HasText(GUIContent content) => content != null && !string.IsNullOrEmpty(content.text);

        bool ShowsText(GUIContent content) => m_ImagePosition != ImagePosition.ImageOnly && HasText(content);

        /// <summary>The image the style shows for the content, null when it shows none.</summary>
        Texture ShownImage(GUIContent content) =>
            m_ImagePosition != ImagePosition.TextOnly && content != null ? content.image : null;

        static Vector2 SizeOf(Texture image) => image != null ? new Vector2(image.width, image.height) : Vector2.zero;

        /// <summary>The size EditorGUIUtility.SetIconSize gives images, when both of its components are set.</summary>
        static bool TryGetIconSize(out Vector2 size)
        {
            size = UnityEditor.EditorGUIUtility.GetIconSize();
            return size.x != 0f && size.y != 0f;
        }

        /// <summary>The text handle of the content's text laid out in a rect of the given size (the
        /// text wraps when the style does and the width is positive). Null when the text cannot be
        /// generated, which the handle reports. Nothing may ask the style for another text or its
        /// line height between this call and the queries on the handle that <paramref name="use"/>
        /// announces: see <see cref="IMGUITextHandle.Use"/>.</summary>
        IMGUITextHandle TextHandleFor(GUIContent content, Vector2 size, Color color, IMGUITextHandle.Use use) =>
            IMGUITextHandle.GetTextHandle(this, new Rect(0f, 0f, size.x, size.y), new RenderedText(content.text, GUIContent.k_ZeroWidthSpace), color, use);

        Vector2 TextSize(GUIContent content, Vector2 size)
        {
            var handle = TextHandleFor(content, size, Color.white, IMGUITextHandle.Use.Measure);
            return handle != null ? handle.GetPreferredSize() : Vector2.zero;
        }

        /// <summary>The preferred size of a text in a rect that still includes the style's padding.</summary>
        internal Vector2 GetPreferredSize(string content, Rect rect)
        {
            var inner = m_Padding.Remove(rect);
            var handle = IMGUITextHandle.GetTextHandle(this, new Rect(0f, 0f, inner.width, inner.height), new RenderedText(content ?? ""), Color.white, IMGUITextHandle.Use.Measure);
            return handle != null ? handle.GetPreferredSize() : Vector2.zero;
        }

        /// <summary>An image's size scaled by the smaller of two ratios, never up, rounded to whole pixels.</summary>
        static Vector2 ScaleImage(Vector2 image, float ratioX, float ratioY, bool clampNegative)
        {
            float scale = Mathf.Min(Mathf.Min(ratioX, ratioY), 1f);
            if (clampNegative && scale < 0f) scale = 0f;
            return new Vector2(Mathf.Floor(image.x * scale + 0.5f), Mathf.Floor(image.y * scale + 0.5f));
        }

        /// <summary>How much of a width an image on the left takes from a wrapping text: the icon
        /// size's width when one is set, else the image's width scaled into the width and
        /// <paramref name="height"/>.</summary>
        static float WrapImageWidth(Vector2 image, float width, float height)
        {
            float iconWidth = UnityEditor.EditorGUIUtility.GetIconSize().x;
            if (iconWidth != 0f) return iconWidth;
            float scale = Mathf.Clamp01(Mathf.Min(width / image.x, height / image.y));
            return Mathf.Floor(scale * image.x + 0.5f);
        }

        bool WrapsBesideImage(Texture image) => m_WordWrap && image != null && m_ImagePosition == ImagePosition.ImageLeft;

        /// <summary>The size of the content with padding, or the fixed size.</summary>
        public Vector2 CalcSize(GUIContent content) => DoCalcSize(content, Vector2.zero);

        /// <summary>The size of the content within maximum sizes (zero: none): the image shrinks to
        /// keep the content inside them, and the result never exceeds them.</summary>
        internal Vector2 CalcSizeWithConstraints(GUIContent content, Vector2 constraints)
        {
            var size = DoCalcSize(content, constraints);
            if (constraints.x > 0f) size.x = Mathf.Min(size.x, constraints.x);
            if (constraints.y > 0f) size.y = Mathf.Min(size.y, constraints.y);
            return size;
        }

        Vector2 DoCalcSize(GUIContent content, Vector2 constraints)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (m_FixedWidth != 0f && m_FixedHeight != 0f) return new Vector2(m_FixedWidth, m_FixedHeight);

            bool showsText = ShowsText(content);
            var text = Vector2.zero;
            if (showsText)
            {
                var inner = m_Padding.Remove(new Rect(0f, 0f, constraints.x, constraints.y));
                text = TextSize(content, inner.size);
            }
            // The image counts whenever the content has one, even for a style that shows text only.
            var shown = ShownImage(content);
            var image = SizeOf(shown);

            Vector2 size;
            switch (m_ImagePosition)
            {
                case ImagePosition.ImageLeft:
                    if (image.x > 0f)
                    {
                        if (TryGetIconSize(out var icon)) image = icon;
                        image = ConstrainImageLeft(image, text, constraints);
                    }
                    size = new Vector2(text.x + image.x, Mathf.Max(image.y, text.y));
                    break;
                case ImagePosition.ImageAbove:
                    if (image.y > 0f) image = ConstrainImageAbove(image, text, constraints);
                    size = new Vector2(Mathf.Max(image.x, text.x), image.y + text.y);
                    break;
                case ImagePosition.ImageOnly:
                    size = image;
                    break;
                case ImagePosition.TextOnly:
                    size = text;
                    break;
                default:
                    size = Vector2.zero;
                    break;
            }
            // Content with neither an image nor text is one line high.
            if (m_ImagePosition != ImagePosition.ImageOnly && content.image == null && !HasText(content))
                size.y = lineHeight;
            return new Vector2(
                m_FixedWidth != 0f ? m_FixedWidth : size.x + m_Padding.horizontal,
                m_FixedHeight != 0f ? m_FixedHeight : size.y + m_Padding.vertical);
        }

        /// <summary>An image left of its text inside maximum sizes: it gives way in the direction
        /// that is over by more. A height over its maximum scales the image; a width over its
        /// maximum only narrows it, its height stays.</summary>
        internal static Vector2 ConstrainImageLeft(Vector2 image, Vector2 text, Vector2 max)
        {
            bool tooTall = max.y > 0f && image.y > max.y;
            bool tooWide = max.x > 0f && text.x + image.x > max.x;
            if (!tooWide && !tooTall) return image;
            if (tooWide && (!tooTall || max.x / image.x <= max.y / image.y))
                return new Vector2(max.x - text.x, image.y);
            return new Vector2(image.x * (max.y / image.y), max.y);
        }

        /// <summary>An image above its text inside maximum sizes, by the same rule.</summary>
        internal static Vector2 ConstrainImageAbove(Vector2 image, Vector2 text, Vector2 max)
        {
            bool tooTall = max.y > 0f && text.y + image.y > max.y;
            if (!tooTall)
                return max.x > 0f && max.x < text.x ? new Vector2(max.x, image.y) : image;
            if (max.x <= 0f || image.x <= max.x || max.y / image.y < max.x / image.x)
            {
                float height = max.y - text.y;
                return new Vector2(image.x * (height / image.y), height);
            }
            return new Vector2(max.x, image.y);
        }

        public Vector2 CalcScreenSize(Vector2 contentSize) => new Vector2(
            m_FixedWidth != 0f ? m_FixedWidth : Mathf.Ceil(contentSize.x + m_Padding.left + m_Padding.right),
            m_FixedHeight != 0f ? m_FixedHeight : Mathf.Ceil(contentSize.y + m_Padding.top + m_Padding.bottom));

        /// <summary>The height of the content laid out in <paramref name="width"/> (word wrapped
        /// when the style wraps). An image keeps its own height here, whatever the icon size.</summary>
        public float CalcHeight(GUIContent content, float width)
        {
            if (m_FixedHeight != 0f) return m_FixedHeight;
            if (content == null) throw new ArgumentNullException(nameof(content));
            var shown = content.image;
            var image = SizeOf(shown);
            if (WrapsBesideImage(shown)) width -= WrapImageWidth(image, width, WrapMeasureHeight);

            float text = 0f;
            if (HasText(content))
            {
                var inner = m_Padding.Remove(new Rect(0f, 0f, width, WrapMeasureHeight));
                text = TextSize(content, inner.size).y;
            }
            float height = m_ImagePosition switch
            {
                ImagePosition.ImageLeft => Mathf.Max(text, image.y),
                ImagePosition.ImageAbove => text + image.y,
                ImagePosition.ImageOnly => image.y,
                ImagePosition.TextOnly => text,
                _ => 0f,
            };
            if (m_ImagePosition != ImagePosition.ImageOnly && shown == null && !HasText(content))
                height = lineHeight;
            return height + m_Padding.top + m_Padding.bottom;
        }

        /// <summary>The narrowest and widest the content can be: a wrapping text can shrink to
        /// <see cref="WrapMinimumWidth"/>, an image keeps its width (scaled to a fixed height).</summary>
        public void CalcMinMaxWidth(GUIContent content, out float minWidth, out float maxWidth)
        {
            if (m_FixedWidth != 0f)
            {
                minWidth = maxWidth = m_FixedWidth;
                return;
            }
            if (content == null) throw new ArgumentNullException(nameof(content));
            maxWidth = HasText(content) ? TextSize(content, Vector2.zero).x : 0f;
            minWidth = m_WordWrap ? Mathf.Min(maxWidth, WrapMinimumWidth) : maxWidth;
            var image = SizeOf(content.image);
            if (content.image != null)
            {
                float width = image.x;
                if (image.y > 0f && m_FixedHeight != 0f) width = width * m_FixedHeight / image.y;
                switch (m_ImagePosition)
                {
                    case ImagePosition.ImageLeft:
                        minWidth += width;
                        maxWidth += width;
                        break;
                    case ImagePosition.ImageAbove:
                        minWidth = Mathf.Max(minWidth, width);
                        maxWidth = Mathf.Max(maxWidth, width);
                        break;
                    case ImagePosition.ImageOnly:
                        minWidth = maxWidth = width;
                        break;
                }
            }
            minWidth += m_Padding.horizontal;
            maxWidth += m_Padding.horizontal;
        }

        /// <summary>Where a style's content goes: the image's rect and the origin of the rect the
        /// text was generated for (the text is already aligned inside that rect).</summary>
        internal struct ContentPlacement
        {
            internal Rect Image;
            internal Vector2 TextOrigin;
            /// <summary>The size of image and text together.</summary>
            internal Vector2 Total;
        }

        /// <summary>The rect a style's content is placed in: the position without the padding,
        /// on the device's pixels.</summary>
        Rect ContentRect(Rect position) => GUIClip.SnapToPixels(m_Padding.Remove(position));

        /// <summary>Places an image and a text of <paramref name="text"/> size in the content rect
        /// (<see cref="ContentRect"/>). The image is scaled down into the room the text leaves,
        /// or takes the icon size; image and text together are aligned as the style says, and
        /// each then goes on the device's pixels.</summary>
        ContentPlacement PlaceContent(Rect inner, Vector2 image, Vector2 text)
        {
            var placement = PlaceContentUnsnapped(inner, image, text);
            if (placement.Image.width > 0f) placement.Image = GUIClip.SnapToPixels(placement.Image);
            placement.TextOrigin = GUIClip.SnapToPixels(placement.TextOrigin);
            return placement;
        }

        /// <summary>The placement before it goes on the device's pixels.</summary>
        internal ContentPlacement PlaceContentUnsnapped(Rect inner, Vector2 image, Vector2 text)
        {
            var placement = new ContentPlacement();
            bool hasImage = image.x > 0f;
            bool icon = TryGetIconSize(out var iconSize);
            switch (m_ImagePosition)
            {
                case ImagePosition.ImageLeft:
                    if (hasImage)
                        image = icon ? iconSize : ScaleImage(image, (inner.width - text.x) / image.x, inner.height / image.y, true);
                    placement.Total = new Vector2(image.x + text.x, Mathf.Max(text.y, image.y));
                    break;
                case ImagePosition.ImageAbove:
                    if (hasImage)
                        image = icon ? iconSize : ScaleImage(image, inner.width / image.x, (inner.height - text.y) / image.y, true);
                    placement.Total = new Vector2(Mathf.Max(text.x, image.x), image.y + text.y);
                    break;
                case ImagePosition.ImageOnly:
                    if (hasImage)
                        image = icon ? iconSize : ScaleImage(image, inner.width / image.x, inner.height / image.y, false);
                    placement.Total = image;
                    break;
                default:
                    placement.Total = text;
                    break;
            }
            float alignX = (int)m_Alignment % 3 * 0.5f;
            float alignY = (int)m_Alignment / 3 * 0.5f;
            float left = inner.x + m_ContentOffset.x;
            float top = inner.y + m_ContentOffset.y;
            float x = left + alignX * (inner.width - placement.Total.x);
            float y = top + alignY * (inner.height - placement.Total.y);
            placement.TextOrigin = new Vector2(left, top);
            switch (m_ImagePosition)
            {
                case ImagePosition.ImageLeft:
                    if (hasImage)
                    {
                        placement.Image = new Rect(x, y + (placement.Total.y - image.y) * 0.5f, image.x, image.y);
                        placement.TextOrigin.x = left + image.x + ImageTextGap;
                    }
                    break;
                case ImagePosition.ImageAbove:
                    if (hasImage)
                        placement.Image = new Rect(Mathf.Floor(x + (placement.Total.x - image.x) * 0.5f + 0.5f), y, image.x, image.y);
                    placement.TextOrigin.y = y + image.y;
                    break;
                case ImagePosition.ImageOnly:
                    if (hasImage)
                        placement.Image = new Rect(Mathf.Floor(x + (placement.Total.x - image.x) * 0.5f + 0.5f), y, image.x, image.y);
                    break;
            }
            return placement;
        }

        /// <summary>The size of the rect the content's text is generated for when it is drawn in
        /// <paramref name="inner"/>: a wrapping text leaves an image on its left its width.</summary>
        Vector2 TextRectSize(Rect inner, Texture image)
        {
            var size = inner.size;
            if (WrapsBesideImage(image)) size.x -= WrapImageWidth(SizeOf(image), size.x, size.y);
            return size;
        }

        /// <summary>The content as it is drawn in a content rect: its text handle (null without
        /// text) and the placement of image and text.</summary>
        ContentPlacement LayoutContent(Rect inner, GUIContent content, Color textColor, IMGUITextHandle.Use use, out IMGUITextHandle handle, out Vector2 textRectSize)
        {
            var image = ShownImage(content);
            textRectSize = TextRectSize(inner, image);
            handle = ShowsText(content) ? TextHandleFor(content, textRectSize, textColor, use) : null;
            var text = handle != null ? handle.GetPreferredSize() : Vector2.zero;
            return PlaceContent(inner, SizeOf(image), text);
        }

        /// <summary>Draws the background then the content; a selection range highlights characters.</summary>
        void DrawStyle(Rect position, GUIContent content, bool isHover, bool isActive, bool on, bool focus, int selectFrom, int selectTo)
        {
            var state = StateFor(isHover, isActive, on, focus);
            var enabledAlpha = GUI.enabled ? 1f : 0.5f;
            bool mixedCheckbox = m_Name == UnityEditor.InspectorTogglePaint.MixedStyleName;
            bool inspectorCheckbox = (mixedCheckbox || m_Name == "toggle" || m_Name == "Toggle") && m_Normal.background == null
                && Compat.InspectorGuiStylesMeasured && Native.IsReady;
            if (inspectorCheckbox)
                UnityEditor.InspectorTogglePaint.Draw(position, isHover, isActive, on, focus, mixedCheckbox, enabledAlpha);
            else if (state.background != null)
            {
                var bg = GUI.color * GUI.backgroundColor;
                bg.a *= enabledAlpha;
                GuiRender.DrawSliced(m_Overflow.Add(position), state.background, m_Border, bg);
            }
            if (content == null) return;
            var textColor = state.textColor * GUI.color * GUI.contentColor;
            textColor.a *= enabledAlpha;
            var imageColor = GUI.color * GUI.contentColor;
            imageColor.a *= enabledAlpha;

            var inner = ContentRect(position);
            // The line height is asked for before the handle: it overwrites the generation settings.
            bool selects = selectFrom >= 0 && selectTo > selectFrom;
            float selectionHeight = selects ? lineHeight : 0f;
            var placement = LayoutContent(inner, content, textColor, selects ? IMGUITextHandle.Use.Query : IMGUITextHandle.Use.Draw, out var handle, out var textRectSize);
            var clipOffset = Internal_clipOffset;
            // Content that does not fit is cut at the content rect unless the style lets it overflow.
            bool clip = m_TextClipping != TextClipping.Overflow && (placement.Total.x > inner.width || placement.Total.y > inner.height);
            if (clip) GuiRender.PushClip(new Rect(inner.position + m_ContentOffset, inner.size));
            try
            {
                var image = ShownImage(content);
                if (image != null && placement.Image.width > 0f && placement.Image.height > 0f)
                {
                    var imageRect = placement.Image;
                    imageRect.position += clipOffset;
                    GuiRender.DrawTexture(imageRect, image, new Rect(0, 0, 1, 1), imageColor);
                }
                if (handle != null)
                {
                    var origin = placement.TextOrigin + clipOffset;
                    if (selects) DrawSelection(handle, origin, selectFrom, selectTo, selectionHeight);
                    GuiRender.DrawText(origin, handle.textInfo);
                }
            }
            finally
            {
                if (clip) GuiRender.PopClip();
            }
        }

        /// <summary>Highlights the characters from..to of a drawn text, one rect per line.</summary>
        static void DrawSelection(IMGUITextHandle handle, Vector2 origin, int from, int to, float height)
        {
            var color = GUI.color * (GUISkin.current != null ? GUISkin.current.settings.selectionColor : new Color(0.24f, 0.49f, 0.91f, 0.6f));
            // The text ends with the zero width space every content gets: the selection stops before it.
            to = Math.Min(to, handle.characterCount - 1);
            for (int i = from; i < to;)
            {
                var start = handle.GetCursorPositionFromStringIndexUsingLineHeight(i);
                int line = handle.GetLineNumber(i);
                int j = i + 1;
                while (j < to && handle.GetLineNumber(j) == line) j++;
                // The end of this line's part: the cursor before the next selected character when
                // it is on the same line, else after the line's last selected character.
                var end = j < to || handle.GetLineNumber(j) != line
                    ? handle.GetCursorPositionFromStringIndexUsingLineHeight(j - 1, useXAdvance: true)
                    : handle.GetCursorPositionFromStringIndexUsingLineHeight(j);
                GuiRender.FillRect(new Rect(origin.x + start.x, origin.y + start.y - height, Math.Max(1f, end.x - start.x), height), color);
                i = j;
            }
        }

        void DrawCursorAt(Rect position, GUIContent content, int index)
        {
            if (GUISkin.current != null)
            {
                float speed = GUISkin.current.settings.cursorFlashSpeed;
                if (speed > 0f && (Time.realtimeSinceStartup * speed) % 1f > 0.5f) return;
            }
            var at = GetCursorPixelPosition(position, content, index);
            var color = GUISkin.current != null ? GUISkin.current.settings.cursorColor : Color.white;
            GuiRender.FillRect(new Rect(at.x, at.y, 1f, lineHeight), GUI.color * color);
        }

        /// <summary>The top left of the cursor before character <paramref name="cursorStringIndex"/>
        /// of the content drawn in <paramref name="position"/>: the cursor's place in the text,
        /// which is generated for the whole content rect, moved to where the text is drawn.
        /// (The Unity 6000.0.71f1 Editor answers NaN in the capture of tests/projects/imguiskin,
        /// calcText/cursor/public; the text handle's own positions are its calcText/cursor/handle
        /// rows.)</summary>
        public Vector2 GetCursorPixelPosition(Rect position, GUIContent content, int cursorStringIndex)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var inner = ContentRect(position);
            float line = lineHeight;
            var cursor = new Vector2(0f, line);
            var text = new Vector2(0f, line);
            // The cursor is asked for wherever the style shows text, also in an empty field.
            var handle = m_ImagePosition != ImagePosition.ImageOnly ? TextHandleFor(content, inner.size, Color.white, IMGUITextHandle.Use.Query) : null;
            if (handle != null)
            {
                cursor = handle.GetCursorPositionFromStringIndexUsingLineHeight(cursorStringIndex);
                cursor.x = Mathf.Max(0f, cursor.x);
                var preferred = handle.GetPreferredSize();
                text = new Vector2(preferred.x, preferred.y > 0f ? preferred.y : line);
            }
            var placement = PlaceContent(inner, SizeOf(ShownImage(content)), text);
            return cursor + placement.TextOrigin + Internal_clipOffset - new Vector2(0f, line);
        }

        /// <summary>The cursor index nearest a point of the content drawn in <paramref name="position"/>.</summary>
        public int GetCursorStringIndex(Rect position, GUIContent content, Vector2 cursorPixelPosition)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (m_ImagePosition == ImagePosition.ImageOnly) return 0;
            var inner = ContentRect(position);
            float line = lineHeight;
            var handle = TextHandleFor(content, inner.size, Color.white, IMGUITextHandle.Use.Query);
            if (handle == null) return 0;
            var preferred = handle.GetPreferredSize();
            var placement = PlaceContent(inner, SizeOf(ShownImage(content)), new Vector2(preferred.x, preferred.y > 0f ? preferred.y : line));
            int index = handle.GetCursorIndexFromPosition(cursorPixelPosition - placement.TextOrigin - Internal_clipOffset);
            return Mathf.Clamp(index, 0, content.text?.Length ?? 0);
        }

        /// <summary>How many characters of a single line fit in a width.</summary>
        internal int GetNumCharactersThatFitWithinWidth(string text, float width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var handle = IMGUITextHandle.GetTextHandle(this, new Rect(0f, 0f, width, 1f), new RenderedText(text), Color.white, IMGUITextHandle.Use.Query);
            return handle != null ? handle.GetNumCharactersThatFitWithinWidth(width) : 0;
        }
    }
}
