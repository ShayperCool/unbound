using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The contents of a GUI element: text, an image and a tooltip.</summary>
    [Serializable]
    public partial class GUIContent
    {
        [SerializeField] string m_Text = "";
        [SerializeField] Texture m_Image;
        [SerializeField] string m_Tooltip = "";

        static readonly GUIContent s_Text = new GUIContent();
        static readonly GUIContent s_Image = new GUIContent();
        static readonly GUIContent s_TextImage = new GUIContent();

        public static GUIContent none = new GUIContent("");

        public GUIContent() { }

        public GUIContent(string text) : this(text, null, "") { }

        public GUIContent(Texture image) : this("", image, "") { }

        public GUIContent(string text, Texture image) : this(text, image, "") { }

        public GUIContent(string text, string tooltip) : this(text, null, tooltip) { }

        public GUIContent(Texture image, string tooltip) : this("", image, tooltip) { }

        public GUIContent(string text, Texture image, string tooltip)
        {
            this.text = text;
            this.image = image;
            this.tooltip = tooltip;
        }

        public GUIContent(GUIContent src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            m_Text = src.m_Text;
            m_Image = src.m_Image;
            m_Tooltip = src.m_Tooltip;
        }

        public string text
        {
            get => m_Text;
            set => m_Text = value;
        }

        public Texture image
        {
            get => m_Image;
            set => m_Image = value;
        }

        public string tooltip
        {
            get => m_Tooltip;
            set => m_Tooltip = value;
        }

        internal int hash => (m_Text?.GetHashCode() ?? 0) ^ (m_Tooltip?.GetHashCode() ?? 0) ^ (m_Image != null ? m_Image.GetHashCode() : 0);

        internal static GUIContent Temp(string t)
        {
            s_Text.m_Text = t;
            s_Text.m_Tooltip = "";
            return s_Text;
        }

        internal static GUIContent Temp(string t, string tooltip)
        {
            s_Text.m_Text = t;
            s_Text.m_Tooltip = tooltip;
            return s_Text;
        }

        internal static GUIContent Temp(Texture i)
        {
            s_Image.m_Image = i;
            s_Image.m_Tooltip = "";
            return s_Image;
        }

        internal static GUIContent Temp(Texture i, string tooltip)
        {
            s_Image.m_Image = i;
            s_Image.m_Tooltip = tooltip;
            return s_Image;
        }

        internal static GUIContent Temp(string t, Texture i)
        {
            s_TextImage.m_Text = t;
            s_TextImage.m_Image = i;
            return s_TextImage;
        }

        internal static void ClearStaticCache()
        {
            s_Text.m_Text = null;
            s_Text.m_Tooltip = "";
            s_Image.m_Image = null;
            s_Image.m_Tooltip = "";
            s_TextImage.m_Text = null;
            s_TextImage.m_Image = null;
        }

        internal static GUIContent[] Temp(string[] texts)
        {
            var result = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++) result[i] = new GUIContent(texts[i]);
            return result;
        }

        internal static GUIContent[] Temp(Texture[] images)
        {
            var result = new GUIContent[images.Length];
            for (int i = 0; i < images.Length; i++) result[i] = new GUIContent(images[i]);
            return result;
        }

        public override string ToString() => m_Text ?? m_Tooltip ?? "";
    }

    /// <summary>How a GUIStyle draws in one state: its background and text color.</summary>
    [Serializable]
    public sealed partial class GUIStyleState
    {
        [SerializeField] Texture2D m_Background;
        [SerializeField] Texture2D[] m_ScaledBackgrounds = Array.Empty<Texture2D>();
        [SerializeField] Color m_TextColor = Color.black;

        public GUIStyleState() { }

        internal GUIStyleState(GUIStyleState other)
        {
            m_Background = other.m_Background;
            m_ScaledBackgrounds = (Texture2D[])other.m_ScaledBackgrounds?.Clone() ?? Array.Empty<Texture2D>();
            m_TextColor = other.m_TextColor;
        }

        public Texture2D background
        {
            get => m_Background;
            set => m_Background = value;
        }

        public Texture2D[] scaledBackgrounds
        {
            get => m_ScaledBackgrounds;
            set => m_ScaledBackgrounds = value ?? Array.Empty<Texture2D>();
        }

        public Color textColor
        {
            get => m_TextColor;
            set => m_TextColor = value;
        }
    }

    /// <summary>
    /// The look of a UnityGUI element: backgrounds per state (normal, hover, active, focused and
    /// their "on" variants) drawn nine-sliced by <see cref="border"/>, and text and image laid out
    /// inside <see cref="padding"/>. Text goes through the legacy text generator.
    /// </summary>
    [Serializable]
    public sealed partial class GUIStyle
    {
        [SerializeField] string m_Name = "";
        [SerializeField] GUIStyleState m_Normal = new GUIStyleState();
        [SerializeField] GUIStyleState m_Hover = new GUIStyleState();
        [SerializeField] GUIStyleState m_Active = new GUIStyleState();
        [SerializeField] GUIStyleState m_Focused = new GUIStyleState();
        [SerializeField] GUIStyleState m_OnNormal = new GUIStyleState();
        [SerializeField] GUIStyleState m_OnHover = new GUIStyleState();
        [SerializeField] GUIStyleState m_OnActive = new GUIStyleState();
        [SerializeField] GUIStyleState m_OnFocused = new GUIStyleState();
        [SerializeField] RectOffset m_Border = new RectOffset();
        [SerializeField] RectOffset m_Margin = new RectOffset();
        [SerializeField] RectOffset m_Padding = new RectOffset();
        [SerializeField] RectOffset m_Overflow = new RectOffset();
        [SerializeField] Font m_Font;
        [SerializeField] int m_FontSize;
        [SerializeField] FontStyle m_FontStyle;
        [SerializeField] TextAnchor m_Alignment;
        [SerializeField] bool m_WordWrap;
        [SerializeField] bool m_RichText;
        [SerializeField] TextClipping m_TextClipping;
        [SerializeField] ImagePosition m_ImagePosition;
        [SerializeField] Vector2 m_ContentOffset;
        [SerializeField] float m_FixedWidth;
        [SerializeField] float m_FixedHeight;
        [SerializeField] bool m_StretchWidth = true;
        [SerializeField] bool m_StretchHeight;

        static GUIStyle s_None;

        internal static bool showKeyboardFocus = true;

        public GUIStyle() { }

        public GUIStyle(GUIStyle other)
        {
            if (other == null)
            {
                Debug.LogError("Copied style is null. Using StyleNotFound instead.");
                other = GUISkin.error;
            }
            m_Name = other.m_Name;
            m_Normal = new GUIStyleState(other.m_Normal);
            m_Hover = new GUIStyleState(other.m_Hover);
            m_Active = new GUIStyleState(other.m_Active);
            m_Focused = new GUIStyleState(other.m_Focused);
            m_OnNormal = new GUIStyleState(other.m_OnNormal);
            m_OnHover = new GUIStyleState(other.m_OnHover);
            m_OnActive = new GUIStyleState(other.m_OnActive);
            m_OnFocused = new GUIStyleState(other.m_OnFocused);
            m_Border = Copy(other.m_Border);
            m_Margin = Copy(other.m_Margin);
            m_Padding = Copy(other.m_Padding);
            m_Overflow = Copy(other.m_Overflow);
            m_Font = other.m_Font;
            m_FontSize = other.m_FontSize;
            m_FontStyle = other.m_FontStyle;
            m_Alignment = other.m_Alignment;
            m_WordWrap = other.m_WordWrap;
            m_RichText = other.m_RichText;
            m_TextClipping = other.m_TextClipping;
            m_ImagePosition = other.m_ImagePosition;
            m_ContentOffset = other.m_ContentOffset;
            m_FixedWidth = other.m_FixedWidth;
            m_FixedHeight = other.m_FixedHeight;
            m_StretchWidth = other.m_StretchWidth;
            m_StretchHeight = other.m_StretchHeight;
        }

        static RectOffset Copy(RectOffset o) => o == null ? new RectOffset() : new RectOffset(o.left, o.right, o.top, o.bottom);

        public static GUIStyle none => s_None ??= new GUIStyle();

        public static implicit operator GUIStyle(string str)
        {
            // In Play mode the game's skin is current from the start (measured: lookups outside
            // OnGUI search "GameSkin").
            if (GUISkin.current == null && UnityEngine.Application.isPlaying) _ = GUI.skin;
            if (GUISkin.current == null)
            {
                Debug.LogError("Unable to use a named GUIStyle without a current skin. Most likely you need to move your GUIStyle initialization code to OnGUI");
                return GUISkin.error;
            }
            return GUISkin.current.GetStyle(str);
        }

        public string name { get => m_Name; set => m_Name = value; }
        public GUIStyleState normal { get => m_Normal; set => m_Normal = value ?? new GUIStyleState(); }
        public GUIStyleState hover { get => m_Hover; set => m_Hover = value ?? new GUIStyleState(); }
        public GUIStyleState active { get => m_Active; set => m_Active = value ?? new GUIStyleState(); }
        public GUIStyleState focused { get => m_Focused; set => m_Focused = value ?? new GUIStyleState(); }
        public GUIStyleState onNormal { get => m_OnNormal; set => m_OnNormal = value ?? new GUIStyleState(); }
        public GUIStyleState onHover { get => m_OnHover; set => m_OnHover = value ?? new GUIStyleState(); }
        public GUIStyleState onActive { get => m_OnActive; set => m_OnActive = value ?? new GUIStyleState(); }
        public GUIStyleState onFocused { get => m_OnFocused; set => m_OnFocused = value ?? new GUIStyleState(); }
        public RectOffset border { get => m_Border; set => m_Border = value ?? new RectOffset(); }
        public RectOffset margin { get => m_Margin; set => m_Margin = value ?? new RectOffset(); }
        public RectOffset padding { get => m_Padding; set => m_Padding = value ?? new RectOffset(); }
        public RectOffset overflow { get => m_Overflow; set => m_Overflow = value ?? new RectOffset(); }
        public Font font { get => m_Font; set => m_Font = value; }
        public int fontSize { get => m_FontSize; set => m_FontSize = value; }
        public FontStyle fontStyle { get => m_FontStyle; set => m_FontStyle = value; }
        public TextAnchor alignment { get => m_Alignment; set => m_Alignment = value; }
        public bool wordWrap { get => m_WordWrap; set => m_WordWrap = value; }
        public bool richText { get => m_RichText; set => m_RichText = value; }
        public TextClipping clipping { get => m_TextClipping; set => m_TextClipping = value; }
        public ImagePosition imagePosition { get => m_ImagePosition; set => m_ImagePosition = value; }
        public Vector2 contentOffset { get => m_ContentOffset; set => m_ContentOffset = value; }
        public float fixedWidth { get => m_FixedWidth; set => m_FixedWidth = value; }
        public float fixedHeight { get => m_FixedHeight; set => m_FixedHeight = value; }
        public bool stretchWidth { get => m_StretchWidth; set => m_StretchWidth = value; }
        public bool stretchHeight { get => m_StretchHeight; set => m_StretchHeight = value; }

        [Obsolete("Don't use clipOffset - put things inside BeginGroup instead. This functionality will be removed in a later version.", false)]
        public Vector2 clipOffset { get; set; }

        internal Vector2 Internal_clipOffset { get; set; }

        /// <summary>The font this style draws with: its own, else the current skin's, else the built-in one.</summary>
        internal Font ResolvedFont => m_Font != null ? m_Font : GUISkin.current != null && GUISkin.current.font != null ? GUISkin.current.font : BuiltinGuiSkin.Font;

        public float lineHeight => Mathf.Round(GuiText.LineHeight(ResolvedFont, m_FontSize, m_FontStyle));

        public bool isHeightDependantOnWidth => m_FixedHeight == 0f && m_WordWrap && m_ImagePosition != ImagePosition.ImageOnly;

        /// <summary>The state drawn: the first of active (pressed under the mouse), focused and hover
        /// that has a background, else normal; the "on" set when <paramref name="on"/>.</summary>
        internal GUIStyleState StateFor(bool isHover, bool isActive, bool on, bool hasKeyboardFocus)
        {
            if (on)
            {
                if (isActive && isHover && m_OnActive.background != null) return m_OnActive;
                if (hasKeyboardFocus && m_OnFocused.background != null) return m_OnFocused;
                if (isHover && m_OnHover.background != null) return m_OnHover;
                return m_OnNormal;
            }
            if (isActive && isHover && m_Active.background != null) return m_Active;
            if (hasKeyboardFocus && m_Focused.background != null) return m_Focused;
            if (isHover && m_Hover.background != null) return m_Hover;
            return m_Normal;
        }

        static bool IsRepaint()
        {
            var e = Event.current;
            if (e != null && e.type == EventType.Repaint) return true;
            Debug.LogError("Style.Draw may not be called if it is not a repaint event");
            return false;
        }

        public void Draw(Rect position, bool isHover, bool isActive, bool on, bool hasKeyboardFocus) =>
            Draw(position, GUIContent.none, isHover, isActive, on, hasKeyboardFocus);

        public void Draw(Rect position, string text, bool isHover, bool isActive, bool on, bool hasKeyboardFocus) =>
            Draw(position, GUIContent.Temp(text), isHover, isActive, on, hasKeyboardFocus);

        public void Draw(Rect position, Texture image, bool isHover, bool isActive, bool on, bool hasKeyboardFocus) =>
            Draw(position, GUIContent.Temp(image), isHover, isActive, on, hasKeyboardFocus);

        public void Draw(Rect position, GUIContent content, bool isHover, bool isActive, bool on, bool hasKeyboardFocus)
        {
            if (!IsRepaint()) return;
            DrawStyle(position, content, isHover, isActive, on, hasKeyboardFocus && showKeyboardFocus, -1, -1, -1);
        }

        public void Draw(Rect position, GUIContent content, int controlID) => Draw(position, content, controlID, false);

        public void Draw(Rect position, GUIContent content, int controlID, bool on) =>
            Draw(position, content, controlID, on, position.Contains(Event.current != null ? Event.current.mousePosition : Vector2.negativeInfinity));

        public void Draw(Rect position, GUIContent content, int controlID, bool on, bool hover)
        {
            if (!IsRepaint()) return;
            int hot = GUIUtility.hotControl;
            bool isHover = hover && (hot == 0 || hot == controlID) && GuiDispatch.MouseInView;
            bool isActive = controlID != 0 && hot == controlID;
            bool focus = controlID != 0 && GUIUtility.keyboardControl == controlID && showKeyboardFocus;
            DrawStyle(position, content, isHover, isActive, on, focus, -1, -1, -1);
        }

        public void DrawCursor(Rect position, GUIContent content, int controlID, int character)
        {
            if (!IsRepaint()) return;
            Draw(position, content, controlID, false);
            DrawCursorAt(position, content, character);
        }

        public void DrawWithTextSelection(Rect position, GUIContent content, int controlID, int firstSelectedCharacter, int lastSelectedCharacter) =>
            DrawWithTextSelection(position, content, controlID, firstSelectedCharacter, lastSelectedCharacter, false);

        internal void DrawWithTextSelection(Rect position, GUIContent content, int controlID, int firstSelectedCharacter, int lastSelectedCharacter, bool drawSelectionAsComposition)
        {
            if (!IsRepaint()) return;
            int hot = GUIUtility.hotControl;
            bool isHover = position.Contains(Event.current.mousePosition) && (hot == 0 || hot == controlID);
            bool focus = GUIUtility.keyboardControl == controlID;
            DrawStyle(position, content, isHover, hot == controlID, false, focus,
                Math.Min(firstSelectedCharacter, lastSelectedCharacter), Math.Max(firstSelectedCharacter, lastSelectedCharacter), -1);
            if (focus && firstSelectedCharacter == lastSelectedCharacter) DrawCursorAt(position, content, lastSelectedCharacter);
        }

        public void DrawPrefixLabel(Rect position, GUIContent content, int controlID)
        {
            if (!IsRepaint()) return;
            bool focus = GUIUtility.keyboardControl == controlID;
            DrawStyle(position, content, position.Contains(Event.current.mousePosition), false, false, focus, -1, -1, -1);
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

        /// <summary>Draws the background then the content; a selection range highlights characters.</summary>
        void DrawStyle(Rect position, GUIContent content, bool isHover, bool isActive, bool on, bool focus, int selectFrom, int selectTo, int unused)
        {
            var state = StateFor(isHover, isActive, on, focus);
            var enabledAlpha = GUI.enabled ? 1f : 0.5f;
            if (state.background != null)
            {
                var bg = GUI.color * GUI.backgroundColor;
                bg.a *= enabledAlpha;
                GuiRender.DrawSliced(m_Overflow.Add(position), state.background, m_Border, bg);
            }
            if (content == null) return;
            var inner = m_Padding.Remove(position);
            inner.position += m_ContentOffset + Internal_clipOffset;
            var textColor = state.textColor * GUI.color * GUI.contentColor;
            textColor.a *= enabledAlpha;
            var imageColor = GUI.color * GUI.contentColor;
            imageColor.a *= enabledAlpha;
            bool clip = m_TextClipping == TextClipping.Clip;
            if (clip) GuiRender.PushClip(m_Padding.Remove(position));
            try
            {
                LayoutContent(inner, content, out var imageRect, out var textRect);
                if (content.image != null && m_ImagePosition != ImagePosition.TextOnly)
                    GuiRender.DrawTexture(imageRect, content.image, new Rect(0, 0, 1, 1), imageColor);
                if (!string.IsNullOrEmpty(content.text) && m_ImagePosition != ImagePosition.ImageOnly)
                {
                    if (selectFrom >= 0 && selectTo > selectFrom)
                        DrawSelection(textRect, content.text, selectFrom, selectTo);
                    GuiText.Draw(textRect, content.text, this, textColor);
                }
            }
            finally
            {
                if (clip) GuiRender.PopClip();
            }
        }

        void DrawSelection(Rect textRect, string text, int from, int to)
        {
            var color = GUISkin.current != null ? GUISkin.current.settings.selectionColor : new Color(0.24f, 0.49f, 0.91f, 0.6f);
            var layout = GuiText.Layout(textRect, text, this);
            float height = lineHeight;
            to = Math.Min(to, layout.characterCount - 1);
            for (int i = from; i < to; )
            {
                var start = GuiText.CursorAt(textRect, layout, i);
                int j = i + 1;
                while (j < to && Mathf.Approximately(GuiText.CursorAt(textRect, layout, j).y, start.y)) j++;
                var end = GuiText.CursorAt(textRect, layout, j);
                float right = Mathf.Approximately(end.y, start.y) ? end.x : start.x + GuiText.CharWidth(layout, j - 1);
                GuiRender.FillRect(new Rect(start.x, start.y, Math.Max(1f, right - start.x), height), GUI.color * color);
                i = j;
            }
        }

        /// <summary>Where the image and the text go inside the content rect.</summary>
        void LayoutContent(Rect inner, GUIContent content, out Rect imageRect, out Rect textRect)
        {
            imageRect = default;
            textRect = inner;
            var image = content.image;
            if (image == null || m_ImagePosition == ImagePosition.TextOnly) return;
            float iw = image.width, ih = image.height;
            bool hasText = !string.IsNullOrEmpty(content.text) && m_ImagePosition != ImagePosition.ImageOnly;
            var textSize = hasText ? GuiText.Size(content.text, this, 0f) : Vector2.zero;
            float availW = inner.width, availH = inner.height;
            if (m_ImagePosition == ImagePosition.ImageLeft) availW -= textSize.x;
            else if (m_ImagePosition == ImagePosition.ImageAbove) availH -= textSize.y;
            float scale = Math.Min(1f, Math.Min(availW > 0 ? availW / Math.Max(1f, iw) : 0f, availH > 0 ? availH / Math.Max(1f, ih) : 0f));
            iw *= scale;
            ih *= scale;
            float h = (int)m_Alignment % 3 / 2f;
            float v = (int)m_Alignment / 3 / 2f;
            switch (m_ImagePosition)
            {
                case ImagePosition.ImageLeft:
                {
                    float total = iw + textSize.x;
                    float x = inner.x + (inner.width - total) * h;
                    imageRect = new Rect(x, inner.y + (inner.height - ih) * v, iw, ih);
                    textRect = new Rect(x + iw, inner.y, Math.Max(0f, inner.xMax - x - iw), inner.height);
                    break;
                }
                case ImagePosition.ImageAbove:
                {
                    float total = ih + textSize.y;
                    float y = inner.y + (inner.height - total) * v;
                    imageRect = new Rect(inner.x + (inner.width - iw) * h, y, iw, ih);
                    textRect = new Rect(inner.x, y + ih, inner.width, Math.Max(0f, inner.yMax - y - ih));
                    break;
                }
                default:
                    imageRect = new Rect(inner.x + (inner.width - iw) * h, inner.y + (inner.height - ih) * v, iw, ih);
                    break;
            }
        }

        public Vector2 GetCursorPixelPosition(Rect position, GUIContent content, int cursorStringIndex)
        {
            var inner = m_Padding.Remove(position);
            inner.position += m_ContentOffset;
            LayoutContent(inner, content, out _, out var textRect);
            var text = content?.text ?? "";
            var layout = GuiText.Layout(textRect, text, this);
            return GuiText.CursorAt(textRect, layout, Mathf.Clamp(cursorStringIndex, 0, text.Length));
        }

        public int GetCursorStringIndex(Rect position, GUIContent content, Vector2 cursorPixelPosition)
        {
            var inner = m_Padding.Remove(position);
            inner.position += m_ContentOffset;
            LayoutContent(inner, content, out _, out var textRect);
            var text = content?.text ?? "";
            var layout = GuiText.Layout(textRect, text, this);
            return GuiText.IndexAt(textRect, layout, text.Length, cursorPixelPosition, lineHeight);
        }

        internal int GetNumCharactersThatFitWithinWidth(string text, float width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            for (int n = 1; n <= text.Length; n++)
                if (GuiText.Size(text.Substring(0, n), this, 0f).x > width) return n - 1;
            return text.Length;
        }

        /// <summary>The size of the content with padding, or the fixed size.</summary>
        public Vector2 CalcSize(GUIContent content)
        {
            var size = ContentSize(content, 0f);
            size.x += m_Padding.horizontal;
            size.y += m_Padding.vertical;
            if (m_FixedWidth != 0f) size.x = m_FixedWidth;
            if (m_FixedHeight != 0f) size.y = m_FixedHeight;
            return new Vector2(Mathf.Ceil(size.x), Mathf.Ceil(size.y));
        }

        Vector2 ContentSize(GUIContent content, float wrapWidth)
        {
            var text = content?.text;
            var image = content?.image;
            Vector2 textSize = Vector2.zero, imageSize = Vector2.zero;
            if (m_ImagePosition != ImagePosition.ImageOnly)
                textSize = string.IsNullOrEmpty(text) ? new Vector2(0f, lineHeight) : GuiText.Size(text, this, wrapWidth);
            if (image != null && m_ImagePosition != ImagePosition.TextOnly)
                imageSize = new Vector2(image.width, image.height);
            switch (m_ImagePosition)
            {
                case ImagePosition.ImageLeft:
                    return new Vector2(textSize.x + imageSize.x, Math.Max(textSize.y, imageSize.y));
                case ImagePosition.ImageAbove:
                    return new Vector2(Math.Max(textSize.x, imageSize.x), textSize.y + imageSize.y);
                case ImagePosition.ImageOnly:
                    return imageSize;
                default:
                    return textSize;
            }
        }

        public Vector2 CalcScreenSize(Vector2 contentSize) => new Vector2(
            m_FixedWidth != 0f ? m_FixedWidth : Mathf.Ceil(contentSize.x + m_Padding.left + m_Padding.right),
            m_FixedHeight != 0f ? m_FixedHeight : Mathf.Ceil(contentSize.y + m_Padding.top + m_Padding.bottom));

        /// <summary>The height of the content laid out in <paramref name="width"/> (word wrapped
        /// when the style wraps).</summary>
        public float CalcHeight(GUIContent content, float width)
        {
            if (m_FixedHeight != 0f) return m_FixedHeight;
            float inner = width - m_Padding.horizontal;
            if (content?.image != null && m_ImagePosition == ImagePosition.ImageLeft) inner -= content.image.width;
            var size = ContentSize(content, m_WordWrap ? Math.Max(1f, inner) : 0f);
            return Mathf.Ceil(size.y + m_Padding.vertical);
        }

        /// <summary>The narrowest and widest the content can be: a wrapping style can shrink to its
        /// longest word.</summary>
        public void CalcMinMaxWidth(GUIContent content, out float minWidth, out float maxWidth)
        {
            var size = CalcSize(content);
            maxWidth = size.x;
            minWidth = size.x;
            if (m_FixedWidth != 0f || !m_WordWrap || string.IsNullOrEmpty(content?.text)) return;
            float word = 0f;
            foreach (var part in content.text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                word = Math.Max(word, GuiText.Size(part, this, 0f).x);
            float image = content.image != null && m_ImagePosition == ImagePosition.ImageLeft ? content.image.width : 0f;
            minWidth = Math.Min(maxWidth, Mathf.Ceil(word + image + m_Padding.horizontal));
        }

        public override string ToString() => $"GUIStyle '{m_Name}'";
    }

    /// <summary>Text editing settings of a skin.</summary>
    [Serializable]
    public sealed partial class GUISettings
    {
        [SerializeField] bool m_DoubleClickSelectsWord = true;
        [SerializeField] bool m_TripleClickSelectsLine = true;
        [SerializeField] Color m_CursorColor = Color.white;
        [SerializeField] float m_CursorFlashSpeed = -1f;
        [SerializeField] Color m_SelectionColor = new Color(0.5f, 0.5f, 1f, 1f);

        public bool doubleClickSelectsWord { get => m_DoubleClickSelectsWord; set => m_DoubleClickSelectsWord = value; }
        public bool tripleClickSelectsLine { get => m_TripleClickSelectsLine; set => m_TripleClickSelectsLine = value; }
        public Color cursorColor { get => m_CursorColor; set => m_CursorColor = value; }

        /// <summary>Blinks per second; negative uses the platform's rate (no blinking here).</summary>
        public float cursorFlashSpeed
        {
            get => m_CursorFlashSpeed >= 0f ? m_CursorFlashSpeed : 0f;
            set => m_CursorFlashSpeed = value;
        }

        public Color selectionColor { get => m_SelectionColor; set => m_SelectionColor = value; }
    }

    /// <summary>A set of styles for the controls (and custom ones looked up by name).</summary>
    [Serializable]
    public sealed partial class GUISkin : ScriptableObject
    {
        [SerializeField] Font m_Font;
        [SerializeField] GUIStyle m_box;
        [SerializeField] GUIStyle m_button;
        [SerializeField] GUIStyle m_toggle;
        [SerializeField] GUIStyle m_label;
        [SerializeField] GUIStyle m_textField;
        [SerializeField] GUIStyle m_textArea;
        [SerializeField] GUIStyle m_window;
        [SerializeField] GUIStyle m_horizontalSlider;
        [SerializeField] GUIStyle m_horizontalSliderThumb;
        [SerializeField] GUIStyle m_horizontalSliderThumbExtent;
        [SerializeField] GUIStyle m_verticalSlider;
        [SerializeField] GUIStyle m_verticalSliderThumb;
        [SerializeField] GUIStyle m_verticalSliderThumbExtent;
        [SerializeField] GUIStyle m_SliderMixed;
        [SerializeField] GUIStyle m_horizontalScrollbar;
        [SerializeField] GUIStyle m_horizontalScrollbarThumb;
        [SerializeField] GUIStyle m_horizontalScrollbarLeftButton;
        [SerializeField] GUIStyle m_horizontalScrollbarRightButton;
        [SerializeField] GUIStyle m_verticalScrollbar;
        [SerializeField] GUIStyle m_verticalScrollbarThumb;
        [SerializeField] GUIStyle m_verticalScrollbarUpButton;
        [SerializeField] GUIStyle m_verticalScrollbarDownButton;
        [SerializeField] GUIStyle m_ScrollView;
        [SerializeField] internal GUIStyle[] m_CustomStyles;
        [SerializeField] GUISettings m_Settings = new GUISettings();

        static GUIStyle s_Error;
        Dictionary<string, GUIStyle> m_Styles;

        internal static GUISkin current;

        public GUISkin()
        {
            m_CustomStyles = new GUIStyle[1];
        }

        internal static GUIStyle error => s_Error ??= new GUIStyle { name = "StyleNotFoundError" };

        internal void OnEnable() => Apply();

        static GUIStyle Styled(GUIStyle s) => s ?? error;

        public Font font
        {
            get => m_Font;
            set
            {
                m_Font = value;
                if (current == this) GuiText.Invalidate();
            }
        }

        public GUIStyle box { get => Styled(m_box); set { m_box = value; Apply(); } }
        public GUIStyle label { get => Styled(m_label); set { m_label = value; Apply(); } }
        public GUIStyle textField { get => Styled(m_textField); set { m_textField = value; Apply(); } }
        public GUIStyle textArea { get => Styled(m_textArea); set { m_textArea = value; Apply(); } }
        public GUIStyle button { get => Styled(m_button); set { m_button = value; Apply(); } }
        public GUIStyle toggle { get => Styled(m_toggle); set { m_toggle = value; Apply(); } }
        public GUIStyle window { get => Styled(m_window); set { m_window = value; Apply(); } }
        public GUIStyle horizontalSlider { get => Styled(m_horizontalSlider); set { m_horizontalSlider = value; Apply(); } }
        public GUIStyle horizontalSliderThumb { get => Styled(m_horizontalSliderThumb); set { m_horizontalSliderThumb = value; Apply(); } }
        internal GUIStyle horizontalSliderThumbExtent { get => Styled(m_horizontalSliderThumbExtent); set { m_horizontalSliderThumbExtent = value; Apply(); } }
        public GUIStyle verticalSlider { get => Styled(m_verticalSlider); set { m_verticalSlider = value; Apply(); } }
        public GUIStyle verticalSliderThumb { get => Styled(m_verticalSliderThumb); set { m_verticalSliderThumb = value; Apply(); } }
        internal GUIStyle verticalSliderThumbExtent { get => Styled(m_verticalSliderThumbExtent); set { m_verticalSliderThumbExtent = value; Apply(); } }
        internal GUIStyle sliderMixed { get => Styled(m_SliderMixed); set { m_SliderMixed = value; Apply(); } }
        public GUIStyle horizontalScrollbar { get => Styled(m_horizontalScrollbar); set { m_horizontalScrollbar = value; Apply(); } }
        public GUIStyle horizontalScrollbarThumb { get => Styled(m_horizontalScrollbarThumb); set { m_horizontalScrollbarThumb = value; Apply(); } }
        public GUIStyle horizontalScrollbarLeftButton { get => Styled(m_horizontalScrollbarLeftButton); set { m_horizontalScrollbarLeftButton = value; Apply(); } }
        public GUIStyle horizontalScrollbarRightButton { get => Styled(m_horizontalScrollbarRightButton); set { m_horizontalScrollbarRightButton = value; Apply(); } }
        public GUIStyle verticalScrollbar { get => Styled(m_verticalScrollbar); set { m_verticalScrollbar = value; Apply(); } }
        public GUIStyle verticalScrollbarThumb { get => Styled(m_verticalScrollbarThumb); set { m_verticalScrollbarThumb = value; Apply(); } }
        public GUIStyle verticalScrollbarUpButton { get => Styled(m_verticalScrollbarUpButton); set { m_verticalScrollbarUpButton = value; Apply(); } }
        public GUIStyle verticalScrollbarDownButton { get => Styled(m_verticalScrollbarDownButton); set { m_verticalScrollbarDownButton = value; Apply(); } }
        public GUIStyle scrollView { get => Styled(m_ScrollView); set { m_ScrollView = value; Apply(); } }

        public GUIStyle[] customStyles
        {
            get => m_CustomStyles;
            set
            {
                m_CustomStyles = value;
                Apply();
            }
        }

        public GUISettings settings => m_Settings;

        internal void Apply()
        {
            m_Styles = null;
        }

        IEnumerable<GUIStyle> Builtins()
        {
            yield return m_box; yield return m_button; yield return m_toggle; yield return m_label;
            yield return m_window; yield return m_textField; yield return m_textArea;
            yield return m_horizontalSlider; yield return m_horizontalSliderThumb; yield return m_horizontalSliderThumbExtent;
            yield return m_verticalSlider; yield return m_verticalSliderThumb; yield return m_verticalSliderThumbExtent;
            yield return m_SliderMixed;
            yield return m_horizontalScrollbar; yield return m_horizontalScrollbarThumb;
            yield return m_horizontalScrollbarLeftButton; yield return m_horizontalScrollbarRightButton;
            yield return m_verticalScrollbar; yield return m_verticalScrollbarThumb;
            yield return m_verticalScrollbarUpButton; yield return m_verticalScrollbarDownButton;
            yield return m_ScrollView;
        }

        void BuildStyleCache()
        {
            m_Styles = new Dictionary<string, GUIStyle>(StringComparer.OrdinalIgnoreCase);
            // Built-in names are fixed whatever the style calls itself.
            string[] names =
            {
                "box", "button", "toggle", "label", "window", "textfield", "textarea",
                "horizontalslider", "horizontalsliderthumb", "horizontalsliderthumbextent",
                "verticalslider", "verticalsliderthumb", "verticalsliderthumbextent", "SliderMixed",
                "horizontalscrollbar", "horizontalscrollbarthumb", "horizontalscrollbarleftbutton", "horizontalscrollbarrightbutton",
                "verticalscrollbar", "verticalscrollbarthumb", "verticalscrollbarupbutton", "verticalscrollbardownbutton",
                "scrollview",
            };
            int i = 0;
            foreach (var style in Builtins())
            {
                if (style != null) m_Styles[names[i]] = style;
                i++;
            }
            if (m_CustomStyles != null)
                foreach (var style in m_CustomStyles)
                    if (style != null && !string.IsNullOrEmpty(style.name))
                        m_Styles[style.name] = style;
        }

        public GUIStyle GetStyle(string styleName)
        {
            var style = FindStyle(styleName);
            if (style != null) return style;
            Debug.LogWarning($"Unable to find style '{styleName}' in skin '{name}' {(Event.current != null ? Event.current.type.ToString() : "<called outside OnGUI>")}");
            return error;
        }

        public GUIStyle FindStyle(string styleName)
        {
            if (this == null)
            {
                Debug.LogError("GUISkin is NULL");
                return null;
            }
            if (m_Styles == null) BuildStyleCache();
            return styleName != null && m_Styles.TryGetValue(styleName, out var style) ? style : null;
        }

        internal GUIStyle FindStyle(string styleName, bool warn) => warn ? GetStyle(styleName) : FindStyle(styleName);

        public IEnumerator GetEnumerator()
        {
            if (m_Styles == null) BuildStyleCache();
            return m_Styles.Values.GetEnumerator();
        }

        internal void MakeCurrent()
        {
            current = this;
            GuiText.Invalidate();
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>
    /// The skin GUI.skin starts as: Unity's built-in game skin (its metrics, colors and the legacy
    /// runtime font), with backgrounds drawn here since Unity's skin textures ship inside the editor.
    /// </summary>
    internal static class BuiltinGuiSkin
    {
        static GUISkin s_Skin;

        internal static Font Font => UnityEngine.Font.GetDefault();

        internal static GUISkin Skin
        {
            get
            {
                if (s_Skin == null) s_Skin = Build();
                return s_Skin;
            }
        }

        static GUISkin Build()
        {
            GUISkin skin;
            using (ObjectRegistry.AsAssets())
                skin = ScriptableObject.CreateInstance<GUISkin>();
            skin.name = "GameSkin";
            skin.hideFlags = HideFlags.HideAndDontSave;
            skin.IsPersistentAsset = true;
            skin.font = Font;
            var text = new Color(0.9f, 0.9f, 0.9f, 1f);
            var dim = new Color(0.8f, 0.8f, 0.8f, 1f);

            skin.box = Style("box", text: dim, border: (6, 6, 6, 6), margin: (4, 4, 4, 4), padding: (4, 4, 4, 4), alignment: TextAnchor.UpperCenter);
            skin.box.normal.background = Textures.Box;

            var button = Style("button", text: text, border: (6, 6, 6, 4), margin: (4, 4, 4, 4), padding: (6, 6, 3, 3), alignment: TextAnchor.MiddleCenter);
            button.normal.background = Textures.Button;
            button.hover.background = Textures.ButtonHover;
            button.hover.textColor = Color.white;
            button.active.background = Textures.ButtonActive;
            button.active.textColor = text;
            button.onNormal.background = Textures.ButtonOn;
            button.onNormal.textColor = new Color(0.98f, 0.98f, 0.98f, 1f);
            button.onHover.background = Textures.ButtonOnHover;
            button.onHover.textColor = Color.white;
            button.onActive.background = Textures.ButtonActive;
            button.onActive.textColor = text;
            skin.button = button;

            var toggle = Style("toggle", text: new Color(0.89f, 0.89f, 0.89f, 1f), border: (14, 0, 14, 0), margin: (4, 4, 4, 4), padding: (15, 0, 3, 0), overflow: (-1, 0, -4, 0));
            toggle.stretchWidth = false;
            toggle.normal.background = Textures.Toggle;
            toggle.hover.background = Textures.ToggleHover;
            toggle.hover.textColor = Color.white;
            toggle.active.background = Textures.ToggleActive;
            toggle.active.textColor = Color.white;
            toggle.onNormal.background = Textures.ToggleOn;
            toggle.onNormal.textColor = new Color(0.89f, 0.89f, 0.89f, 1f);
            toggle.onHover.background = Textures.ToggleOnHover;
            toggle.onHover.textColor = Color.white;
            toggle.onActive.background = Textures.ToggleOnActive;
            toggle.onActive.textColor = Color.white;
            skin.toggle = toggle;

            var label = Style("label", text: text, margin: (4, 4, 4, 4), padding: (2, 2, 1, 2));
            label.wordWrap = true;
            skin.label = label;

            var field = Style("textfield", text: dim, border: (3, 3, 3, 3), margin: (4, 4, 4, 4), padding: (3, 3, 3, 3));
            field.clipping = TextClipping.Clip;
            field.richText = false;
            field.normal.background = Textures.TextField;
            field.hover.background = Textures.TextFieldHover;
            field.hover.textColor = text;
            field.focused.background = Textures.TextFieldFocused;
            field.focused.textColor = Color.white;
            field.onNormal.background = Textures.TextFieldFocused;
            field.onNormal.textColor = Color.white;
            skin.textField = field;
            var area = new GUIStyle(field) { name = "textarea", wordWrap = true };
            skin.textArea = area;

            var window = Style("window", text: Color.white, border: (8, 8, 18, 8), padding: (8, 8, 18, 8), alignment: TextAnchor.UpperCenter);
            window.contentOffset = new Vector2(0f, -18f);
            window.normal.background = Textures.Window;
            window.onNormal.background = Textures.WindowOn;
            window.onNormal.textColor = Color.white;
            skin.window = window;

            var hSlider = Style("horizontalslider", border: (3, 3, 0, 0), margin: (4, 4, 4, 4), padding: (-1, -1, 0, 0), overflow: (0, 0, -4, -3));
            hSlider.fixedHeight = 12f;
            hSlider.imagePosition = ImagePosition.ImageOnly;
            hSlider.normal.background = Textures.SliderTrack;
            skin.horizontalSlider = hSlider;
            var hThumb = Style("horizontalsliderthumb", border: (4, 4, 0, 0), padding: (7, 7, 0, 0), overflow: (-1, -1, 0, 0));
            hThumb.fixedHeight = 12f;
            hThumb.stretchWidth = false;
            hThumb.imagePosition = ImagePosition.ImageOnly;
            hThumb.normal.background = Textures.Thumb;
            hThumb.hover.background = Textures.ThumbHover;
            hThumb.active.background = Textures.ThumbActive;
            skin.horizontalSliderThumb = hThumb;
            skin.horizontalSliderThumbExtent = Style("horizontalsliderthumbextent");

            var vSlider = Style("verticalslider", border: (0, 0, 3, 3), margin: (4, 4, 4, 4), padding: (0, 0, -1, -1), overflow: (-2, -3, 0, 0));
            vSlider.fixedWidth = 12f;
            vSlider.stretchWidth = false;
            vSlider.stretchHeight = true;
            vSlider.imagePosition = ImagePosition.ImageOnly;
            vSlider.normal.background = Textures.VerticalSliderTrack;
            skin.verticalSlider = vSlider;
            var vThumb = Style("verticalsliderthumb", border: (0, 0, 4, 4), padding: (0, 0, 7, 7), overflow: (0, 0, -1, -1));
            vThumb.fixedWidth = 12f;
            vThumb.stretchWidth = false;
            vThumb.stretchHeight = false;
            vThumb.imagePosition = ImagePosition.ImageOnly;
            vThumb.normal.background = Textures.Thumb;
            vThumb.hover.background = Textures.ThumbHover;
            vThumb.active.background = Textures.ThumbActive;
            skin.verticalSliderThumb = vThumb;
            skin.verticalSliderThumbExtent = Style("verticalsliderthumbextent");
            skin.sliderMixed = Style("SliderMixed");

            var hBar = Style("horizontalscrollbar", border: (9, 9, 0, 0), margin: (4, 4, 1, 4));
            hBar.fixedHeight = 15f;
            hBar.imagePosition = ImagePosition.ImageOnly;
            hBar.normal.background = Textures.ScrollTrack;
            skin.horizontalScrollbar = hBar;
            var hBarThumb = Style("horizontalscrollbarthumb", border: (6, 6, 6, 6), padding: (6, 6, 0, 0), overflow: (0, 0, -1, 1));
            hBarThumb.fixedHeight = 13f;
            hBarThumb.imagePosition = ImagePosition.ImageOnly;
            hBarThumb.normal.background = Textures.ScrollThumb;
            skin.horizontalScrollbarThumb = hBarThumb;
            skin.horizontalScrollbarLeftButton = Style("horizontalscrollbarleftbutton");
            skin.horizontalScrollbarRightButton = Style("horizontalscrollbarrightbutton");

            var vBar = Style("verticalscrollbar", border: (0, 0, 9, 9), margin: (1, 4, 4, 4));
            vBar.fixedWidth = 15f;
            vBar.stretchWidth = false;
            vBar.stretchHeight = true;
            vBar.imagePosition = ImagePosition.ImageOnly;
            vBar.normal.background = Textures.VerticalScrollTrack;
            skin.verticalScrollbar = vBar;
            var vBarThumb = Style("verticalscrollbarthumb", border: (6, 6, 6, 6), padding: (0, 0, 6, 6), overflow: (-1, -1, 0, 0));
            vBarThumb.fixedWidth = 15f;
            vBarThumb.stretchWidth = false;
            vBarThumb.imagePosition = ImagePosition.ImageOnly;
            vBarThumb.normal.background = Textures.ScrollThumb;
            skin.verticalScrollbarThumb = vBarThumb;
            skin.verticalScrollbarUpButton = Style("verticalscrollbarupbutton");
            skin.verticalScrollbarDownButton = Style("verticalscrollbardownbutton");

            skin.scrollView = Style("scrollview");
            skin.customStyles = Array.Empty<GUIStyle>();
            skin.settings.cursorColor = Color.white;
            skin.settings.selectionColor = new Color(0.24f, 0.49f, 0.91f, 0.6f);
            return skin;
        }

        static GUIStyle Style(string name, Color? text = null, (int l, int r, int t, int b) border = default,
            (int l, int r, int t, int b) margin = default, (int l, int r, int t, int b) padding = default,
            (int l, int r, int t, int b) overflow = default, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var style = new GUIStyle
            {
                name = name,
                border = new RectOffset(border.l, border.r, border.t, border.b),
                margin = new RectOffset(margin.l, margin.r, margin.t, margin.b),
                padding = new RectOffset(padding.l, padding.r, padding.t, padding.b),
                overflow = new RectOffset(overflow.l, overflow.r, overflow.t, overflow.b),
                alignment = alignment,
                richText = true,
            };
            var color = text ?? Color.black;
            foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
                state.textColor = color;
            return style;
        }

        /// <summary>The skin's backgrounds: rounded, shaded rectangles sized for their borders.</summary>
        static class Textures
        {
            internal static readonly Texture2D Box = Panel(12, 12, 3, Gray(40, 200), Gray(30, 200), Gray(85, 220));
            internal static readonly Texture2D Button = Panel(12, 12, 3, Gray(102, 255), Gray(72, 255), Gray(28, 255));
            internal static readonly Texture2D ButtonHover = Panel(12, 12, 3, Gray(122, 255), Gray(88, 255), Gray(28, 255));
            internal static readonly Texture2D ButtonActive = Panel(12, 12, 3, Gray(56, 255), Gray(66, 255), Gray(20, 255));
            internal static readonly Texture2D ButtonOn = Panel(12, 12, 3, Gray(64, 255), Gray(76, 255), Gray(20, 255));
            internal static readonly Texture2D ButtonOnHover = Panel(12, 12, 3, Gray(76, 255), Gray(90, 255), Gray(20, 255));
            internal static readonly Texture2D TextField = Panel(8, 8, 2, Gray(24, 235), Gray(30, 235), Gray(80, 255));
            internal static readonly Texture2D TextFieldHover = Panel(8, 8, 2, Gray(28, 235), Gray(34, 235), Gray(110, 255));
            internal static readonly Texture2D TextFieldFocused = Panel(8, 8, 2, Gray(24, 235), Gray(30, 235), new Color32(90, 140, 230, 255));
            internal static readonly Texture2D Toggle = Check(false, 100);
            internal static readonly Texture2D ToggleHover = Check(false, 125);
            internal static readonly Texture2D ToggleActive = Check(false, 70);
            internal static readonly Texture2D ToggleOn = Check(true, 100);
            internal static readonly Texture2D ToggleOnHover = Check(true, 125);
            internal static readonly Texture2D ToggleOnActive = Check(true, 70);
            internal static readonly Texture2D Window = TitledPanel(24, 24, 18, Gray(58, 245), Gray(46, 245));
            internal static readonly Texture2D WindowOn = TitledPanel(24, 24, 18, Gray(74, 250), Gray(50, 250));
            internal static readonly Texture2D SliderTrack = Track(8, 12, horizontal: true);
            internal static readonly Texture2D VerticalSliderTrack = Track(12, 8, horizontal: false);
            internal static readonly Texture2D Thumb = Panel(12, 12, 5, Gray(200, 255), Gray(160, 255), Gray(40, 255));
            internal static readonly Texture2D ThumbHover = Panel(12, 12, 5, Gray(225, 255), Gray(180, 255), Gray(40, 255));
            internal static readonly Texture2D ThumbActive = Panel(12, 12, 5, Gray(150, 255), Gray(130, 255), Gray(30, 255));
            internal static readonly Texture2D ScrollTrack = Panel(18, 15, 6, Gray(32, 220), Gray(38, 220), Gray(60, 220));
            internal static readonly Texture2D VerticalScrollTrack = Panel(15, 18, 6, Gray(32, 220), Gray(38, 220), Gray(60, 220));
            internal static readonly Texture2D ScrollThumb = Panel(12, 12, 5, Gray(120, 255), Gray(96, 255), Gray(40, 255));

            static Color32 Gray(byte v, byte a) => new Color32(v, v, v, a);

            static Texture2D Make(string name, int w, int h, Func<int, int, Color32> pixel)
            {
                Texture2D t;
                using (ObjectRegistry.AsAssets())
                    t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name };
                t.hideFlags = HideFlags.HideAndDontSave;
                t.IsPersistentAsset = true;
                t.filterMode = FilterMode.Bilinear;
                t.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color32[w * h];
                // Rows are stored bottom first; the pixel callback counts from the top.
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        pixels[(h - 1 - y) * w + x] = pixel(x, y);
                t.SetPixels32(pixels);
                t.Apply(false, false);
                return t;
            }

            /// <summary>Coverage of a rounded rectangle at a pixel center.</summary>
            static float Inside(int x, int y, float x0, float y0, float x1, float y1, float radius)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, x0 + radius, x1 - radius);
                float cy = Mathf.Clamp(py, y0 + radius, y1 - radius);
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return Mathf.Clamp01(radius + 0.5f - d);
            }

            static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

            static Color32 WithAlpha(Color32 c, float coverage) => new Color32(c.r, c.g, c.b, (byte)(c.a * coverage));

            static Texture2D Panel(int w, int h, float radius, Color32 top, Color32 bottom, Color32 outline) =>
                Make("gui panel", w, h, (x, y) =>
                {
                    float outer = Inside(x, y, 0, 0, w, h, radius);
                    if (outer <= 0f) return default;
                    float inner = Inside(x, y, 1, 1, w - 1, h - 1, Math.Max(0f, radius - 1f));
                    var fill = Mix(top, bottom, h <= 1 ? 0f : y / (float)(h - 1));
                    return WithAlpha(Mix(outline, fill, inner), outer);
                });

            static Texture2D TitledPanel(int w, int h, int title, Color32 titleColor, Color32 body) =>
                Make("gui window", w, h, (x, y) =>
                {
                    float outer = Inside(x, y, 0, 0, w, h, 4);
                    if (outer <= 0f) return default;
                    float inner = Inside(x, y, 1, 1, w - 1, h - 1, 3);
                    var fill = y < title ? Mix(titleColor, body, y / (float)title * 0.6f) : body;
                    return WithAlpha(Mix(Gray(20, 255), fill, inner), outer);
                });

            static Texture2D Track(int w, int h, bool horizontal) =>
                Make("gui track", w, h, (x, y) =>
                {
                    float c = horizontal
                        ? Inside(x, y, 0, h / 2f - 2f, w, h / 2f + 2f, 2f)
                        : Inside(x, y, w / 2f - 2f, 0, w / 2f + 2f, h, 2f);
                    return WithAlpha(Gray(30, 230), c);
                });

            static Texture2D Check(bool on, byte shade) =>
                Make(on ? "gui toggle on" : "gui toggle", 16, 16, (x, y) =>
                {
                    if (x >= 14 || y >= 14) return default;
                    float outer = Inside(x, y, 0, 0, 14, 14, 3);
                    if (outer <= 0f) return default;
                    float inner = Inside(x, y, 1, 1, 13, 13, 2);
                    var color = Mix(Gray(25, 255), Gray(shade, 255), inner);
                    if (on)
                    {
                        // A tick from (3.5, 7) to (6, 10) to (11, 3.5).
                        float d = Math.Min(Segment(x + 0.5f, y + 0.5f, 3.5f, 7f, 6f, 10f), Segment(x + 0.5f, y + 0.5f, 6f, 10f, 10.5f, 3.5f));
                        color = Mix(color, Gray(240, 255), Mathf.Clamp01(1.6f - d));
                    }
                    return WithAlpha(color, outer);
                });

            static float Segment(float px, float py, float ax, float ay, float bx, float by)
            {
                float vx = bx - ax, vy = by - ay;
                float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
                float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
                return Mathf.Sqrt(dx * dx + dy * dy);
            }
        }
    }
}
