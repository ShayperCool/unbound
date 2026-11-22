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

        internal int hash => string.IsNullOrEmpty(m_Text) ? 0 : unchecked(m_Text.GetHashCode() * 37);

        /// <summary>The character every text is generated with at its end, so that the cursor
        /// has a place after the last character and on an empty last line.</summary>
        internal static string k_ZeroWidthSpace = "\u200b";

        internal string textWithWhitespace
        {
            get => string.IsNullOrEmpty(m_Text) ? k_ZeroWidthSpace : m_Text + k_ZeroWidthSpace;
            set => m_Text = value;
        }

        internal static GUIContent Temp(string t)
        {
            s_Text.m_Text = t;
            s_Text.m_Image = null;
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

        /// <summary>Drops the background and the first scaled background, as the Editor does
        /// for a style it draws from its style catalog.</summary>
        internal void ClearBackgrounds()
        {
            m_Background = null;
            if (m_ScaledBackgrounds.Length >= 1) m_ScaledBackgrounds[0] = null;
        }
    }

    /// <summary>
    /// The look of a UnityGUI element: backgrounds per state (normal, hover, active, focused and
    /// their "on" variants) drawn nine-sliced by <see cref="border"/>, and text and image laid out
    /// inside <see cref="padding"/> (GUIStyle.Text.cs).
    /// </summary>
    [Serializable]
    public sealed partial class GUIStyle
    {
        [NonSerialized] string m_Name = "";
        [NonSerialized] GUIStyleState m_Normal = new GUIStyleState();
        [NonSerialized] GUIStyleState m_Hover = new GUIStyleState();
        [NonSerialized] GUIStyleState m_Active = new GUIStyleState();
        [NonSerialized] GUIStyleState m_Focused = new GUIStyleState();
        [NonSerialized] GUIStyleState m_OnNormal = new GUIStyleState();
        [NonSerialized] GUIStyleState m_OnHover = new GUIStyleState();
        [NonSerialized] GUIStyleState m_OnActive = new GUIStyleState();
        [NonSerialized] GUIStyleState m_OnFocused = new GUIStyleState();
        [NonSerialized] RectOffset m_Border = new RectOffset();
        [NonSerialized] RectOffset m_Margin = new RectOffset();
        [NonSerialized] RectOffset m_Padding = new RectOffset();
        [NonSerialized] RectOffset m_Overflow = new RectOffset();
        [NonSerialized] Font m_Font;
        [NonSerialized] int m_FontSize;
        [NonSerialized] FontStyle m_FontStyle;
        [NonSerialized] TextAnchor m_Alignment;
        [NonSerialized] bool m_WordWrap;
        [NonSerialized] bool m_RichText = Compat.DefaultConstructorStateMeasured;
        [NonSerialized] TextClipping m_TextClipping;
        [NonSerialized] ImagePosition m_ImagePosition;
        [NonSerialized] Vector2 m_ContentOffset;
        [NonSerialized] float m_FixedWidth;
        [NonSerialized] float m_FixedHeight;
        [NonSerialized] bool m_StretchWidth = true;
        [NonSerialized] bool m_StretchHeight;

        [NonSerialized] bool m_IsSDF;

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
            m_IsSDF = other.m_IsSDF;
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
        internal Font ResolvedFont => m_Font != null ? m_Font : GetDefaultFont();

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
            DrawStyle(position, content, isHover, isActive, on, hasKeyboardFocus && showKeyboardFocus, -1, -1);
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
            DrawStyle(position, content, isHover, isActive, on, focus, -1, -1);
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
                Math.Min(firstSelectedCharacter, lastSelectedCharacter), Math.Max(firstSelectedCharacter, lastSelectedCharacter));
            if (focus && firstSelectedCharacter == lastSelectedCharacter) DrawCursorAt(position, content, lastSelectedCharacter);
        }

        internal void DrawPrefixLabel(Rect position, GUIContent content, int controlID)
        {
            if (content == null)
            {
                Debug.LogError("Style.DrawPrefixLabel may not be called with GUIContent that is null.");
                return;
            }
            if (!IsRepaint()) return;
            bool focus = GUIUtility.keyboardControl == controlID;
            DrawStyle(position, content, position.Contains(Event.current.mousePosition), false, false, focus, -1, -1);
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

        /// <summary>The platform's cursor flash speed, which a negative setting stands for (measured
        /// in the Linux Unity 6000.0.71f1 Editor, tests/projects/imguiskin; unmeasured on macOS).</summary>
        const float PlatformCursorFlashSpeed = 2f;

        /// <summary>Blinks per second; negative uses the platform's rate.</summary>
        public float cursorFlashSpeed
        {
            get => m_CursorFlashSpeed >= 0f ? m_CursorFlashSpeed : PlatformCursorFlashSpeed;
            set => m_CursorFlashSpeed = value;
        }

        public Color selectionColor { get => m_SelectionColor; set => m_SelectionColor = value; }
    }

    /// <summary>A set of styles for the controls (and custom ones looked up by name).</summary>
    [global::UnityEngine.@ExecuteInEditMode] [global::UnityEngine.@AssetFileNameExtensionAttribute("guiskin", new string[] {  })] [Serializable]
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
        [NonSerialized] GUIStyle m_horizontalSliderThumbExtent;
        [SerializeField] GUIStyle m_verticalSlider;
        [SerializeField] GUIStyle m_verticalSliderThumb;
        [NonSerialized] GUIStyle m_verticalSliderThumbExtent;
        [NonSerialized] GUIStyle m_SliderMixed;
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
            Apply();
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
                if (current == this) GUIStyle.SetDefaultFont(m_Font);
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
            m_box ??= new GUIStyle();
            m_button ??= new GUIStyle();
            m_toggle ??= new GUIStyle();
            m_label ??= new GUIStyle();
            m_textField ??= new GUIStyle();
            m_textArea ??= new GUIStyle();
            m_window ??= new GUIStyle();
            m_horizontalSlider ??= new GUIStyle();
            m_horizontalSliderThumb ??= new GUIStyle();
            m_horizontalSliderThumbExtent ??= new GUIStyle();
            m_verticalSlider ??= new GUIStyle();
            m_verticalSliderThumb ??= new GUIStyle();
            m_verticalSliderThumbExtent ??= new GUIStyle();
            m_SliderMixed ??= new GUIStyle();
            m_horizontalScrollbar ??= new GUIStyle();
            m_horizontalScrollbarThumb ??= new GUIStyle();
            m_horizontalScrollbarLeftButton ??= new GUIStyle();
            m_horizontalScrollbarRightButton ??= new GUIStyle();
            m_verticalScrollbar ??= new GUIStyle();
            m_verticalScrollbarThumb ??= new GUIStyle();
            m_verticalScrollbarUpButton ??= new GUIStyle();
            m_verticalScrollbarDownButton ??= new GUIStyle();
            m_ScrollView ??= new GUIStyle();
            BuildStyleCache();
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

        /// <summary>The skin's own styles, then its custom ones.</summary>
        internal IEnumerable<GUIStyle> AllStyles()
        {
            foreach (var style in Builtins()) yield return style;
            foreach (var style in m_CustomStyles ?? Array.Empty<GUIStyle>()) yield return style;
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
                if (style != null)
                {
                    // A built-in style without a name takes its own (measured: the GameSkin's
                    // slider extents and SliderMixed, tests/projects/imguiskin).
                    style.name = names[i];
                    m_Styles[names[i]] = style;
                }
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
            GUIStyle.SetDefaultFont(m_Font);
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>
    /// The Editor's own GUI skins, LightSkin and DarkSkin of `unity editor resources`: what
    /// EditorGUIUtility.GetBuiltinSkin answers for Inspector and Scene and what Editor GUI is
    /// drawn with. The skin of the Editor's current theme has the Editor's font, the other none
    /// (matches the Unity 6000.0.71f1 Editor with the dark theme, tests/projects/editorskin).
    /// </summary>
    internal static class EditorGuiSkins
    {
        const string LightPath = "Builtin Skins/Generated/Skins/LightSkin.guiskin";
        const string DarkPath = "Builtin Skins/Generated/Skins/DarkSkin.guiskin";
        static GUISkin s_Light, s_Dark;

        internal static GUISkin Light => s_Light != null ? s_Light : s_Light = Load(LightPath, "LightSkin", dark: false);

        internal static GUISkin Dark => s_Dark != null ? s_Dark : s_Dark = Load(DarkPath, "DarkSkin", dark: true);

        /// <summary>The skin of the Editor's theme.</summary>
        internal static GUISkin Current => UnityEditor.EditorGUIUtility.isProSkin ? Dark : Light;

        const string EditorFontPath = "Fonts/Inter/Inter-Regular.ttf";
        static Font s_EditorFont;

        /// <summary>The Editor's UI font: Inter Regular of the Editor's resources, at 12
        /// (tests/projects/editorskin skin/Scene/font). Null when the resources lack it.</summary>
        internal static Font EditorFont =>
            s_EditorFont != null ? s_EditorFont : s_EditorFont = Unbound.UIElements.UiResources.EditorLoad(EditorFontPath) as Font;

        /// <summary>The style sheets of the Editor's style catalog for a theme, in the order
        /// they are merged (the Editor's default font and language).</summary>
        static string[] CatalogSheets(bool dark) => new[]
        {
            "StyleSheets/Extensions/base/common.uss",
            "UIPackageResources/StyleSheets/Default/Variables/Public/common.uss",
            "StyleSheets/Northstar/common.uss",
            "StyleSheets/Extensions/fonts/inter.uss",
            dark ? "StyleSheets/Extensions/base/dark.uss" : "StyleSheets/Extensions/base/light.uss",
            dark ? "UIPackageResources/StyleSheets/Default/Northstar/Palette/dark.uss" : "UIPackageResources/StyleSheets/Default/Northstar/Palette/light.uss",
        };

        static EditorStyleCatalog s_Catalog;

        /// <summary>The style catalog of the Editor's theme.</summary>
        internal static EditorStyleCatalog Catalog
        {
            get
            {
                if (s_Catalog != null) return s_Catalog;
                var sheets = new List<UnityEngine.UIElements.StyleSheet>();
                foreach (string path in CatalogSheets(UnityEditor.EditorGUIUtility.isProSkin))
                {
                    var sheet = Unbound.UIElements.UiResources.EditorLoad(path) as UnityEngine.UIElements.StyleSheet;
                    if (sheet == null) Debug.Log("Could not load " + path);
                    else if (!sheets.Contains(sheet)) sheets.Add(sheet);
                }
                return s_Catalog = EditorStyleCatalog.Build(sheets);
            }
        }

        static GUISkin Load(string path, string name, bool dark)
        {
            var skin = Unbound.UIElements.UiResources.EditorLoad(path) as GUISkin;
            if (skin != null)
            {
                // The skin of the Editor's theme has the Editor's font and the style catalog's
                // values; the other one stays as stored.
                if (dark == UnityEditor.EditorGUIUtility.isProSkin)
                {
                    skin.font = EditorFont;
                    Catalog.ApplyTo(skin);
                }
                return skin;
            }
            Debug.LogError($"Unbound: the Editor's GUI skin {name} ({path} of unity editor resources) is not available; Editor GUI is drawn with default styles without backgrounds.");
            using (ObjectRegistry.AsAssets())
                skin = ScriptableObject.CreateInstance<GUISkin>();
            skin.name = name;
            skin.hideFlags = HideFlags.HideAndDontSave;
            skin.IsPersistentAsset = true;
            skin.Apply();
            return skin;
        }
    }

    /// <summary>
    /// The skin GUI.skin starts as in Play mode: Unity's own GameSkin, read from the install's
    /// `unity default resources` (the object the release profile names "GameSkin") with its
    /// styles, settings, font and background textures (each with its stored sampler). Matches the
    /// Unity 6000.0.71f1 Editor, tests/projects/imguiskin. Without the install (no host) the skin
    /// has default styles and no backgrounds, which is reported once.
    /// </summary>
    internal static class BuiltinGuiSkin
    {
        const int MonoBehaviourClassId = 114;
        const int Texture2DClassId = 28;
        static GUISkin s_Skin;

        internal static Font Font => UnityEngine.Font.GetDefault();

        internal static GUISkin Skin
        {
            get
            {
                // Unity's null: a destroyed skin is loaded again.
                if (s_Skin == null) s_Skin = Load();
                return s_Skin;
            }
        }

        static GUISkin Load()
        {
            GUISkin skin;
            using (ObjectRegistry.AsAssets())
                skin = ScriptableObject.CreateInstance<GUISkin>();
            skin.name = "GameSkin";
            skin.hideFlags = HideFlags.HideAndDontSave;
            skin.IsPersistentAsset = true;
            try
            {
                long fileId = SkinFileId();
                var fields = BuiltinSpriteData.RecordFields(BuiltinShaders.DefaultGuid, fileId, MonoBehaviourClassId, "GUISkin");
                using (ObjectRegistry.AsAssets())
                    new YamlReader(new Resolver()).ReadScriptObject(skin, YNode.FromJson(fields));
                skin.name = "GameSkin";
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: Unity's built-in GUI skin (GameSkin of unity default resources) is not available ({e.Message}); GUI.skin has default styles without backgrounds.");
                skin.font = Font;
            }
            skin.Apply();
            return skin;
        }

        /// <summary>The release profile's GameSkin object of `unity default resources`.</summary>
        static long SkinFileId()
        {
            if (!Native.IsReady) throw new InvalidOperationException("the host, which reads the install's resources, is not connected");
            foreach (var pair in BuiltinShaders.DefaultNames)
                if (IsSkin(pair.Key)) return pair.Key;
            throw new InvalidOperationException("the release profile names no GameSkin");
        }

        /// <summary>Whether the object `fileId` of `unity default resources` is the GameSkin the
        /// release profile names (a MonoBehaviour named "GameSkin").</summary>
        internal static bool IsSkin(long fileId) =>
            BuiltinShaders.DefaultNames.TryGetValue(fileId, out var name) && name == "GameSkin"
            && BuiltinObjects.DefaultClasses.TryGetValue(fileId, out var classId) && classId == MonoBehaviourClassId;

        /// <summary>The skin's references: its background textures read from the install with their
        /// sampler, other built-in objects (the font) as every reference resolves them.</summary>
        sealed class Resolver : IReferenceResolver
        {
            static readonly Dictionary<long, Texture2D> s_Textures = new Dictionary<long, Texture2D>();

            public Object Resolve(ObjectRef reference, Type expected)
            {
                if (reference.Guid == null || reference.FileId == 0) return null;
                bool fromDefault = string.Equals(reference.Guid, BuiltinShaders.DefaultGuid, StringComparison.OrdinalIgnoreCase);
                if (fromDefault && BuiltinObjects.DefaultClasses.TryGetValue(reference.FileId, out var classId) && classId == Texture2DClassId
                    && BuiltinShaders.DefaultNames.TryGetValue(reference.FileId, out var name))
                {
                    if (!s_Textures.TryGetValue(reference.FileId, out var texture))
                    {
                        texture = BuiltinSpriteData.LoadTexture(BuiltinShaders.DefaultGuid, reference.FileId, name);
                        if (texture != null)
                        {
                            texture.IsPersistentAsset = true;
                            texture.hideFlags = HideFlags.HideAndDontSave;
                        }
                        s_Textures[reference.FileId] = texture;
                    }
                    return texture;
                }
                return BuiltinShaders.Resolve(reference.Guid, reference.FileId);
            }
        }
    }
}
