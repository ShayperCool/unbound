using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The scroll position and extents of a scroll view between events.</summary>
    internal partial class ScrollViewState
    {
        public Rect position;
        public Rect visibleRect;
        public Rect viewRect;
        public Vector2 scrollPosition;
        public bool apply;
        public bool isDuringTouchScroll;

        public void ScrollTo(Rect pos) => ScrollTowards(pos, float.PositiveInfinity);

        public bool ScrollTowards(Rect pos, float maxDelta)
        {
            var delta = ScrollNeeded(pos);
            if (delta.sqrMagnitude < 0.0001f) return false;
            if (delta.sqrMagnitude > maxDelta * maxDelta) delta = delta.normalized * maxDelta;
            scrollPosition += delta;
            apply = true;
            return true;
        }

        Vector2 ScrollNeeded(Rect pos)
        {
            var rect = visibleRect;
            rect.x += scrollPosition.x;
            rect.y += scrollPosition.y;
            float extraX = pos.width - visibleRect.width;
            if (extraX > 0f)
            {
                pos.width -= extraX;
                pos.x += extraX * 0.5f;
            }
            float extraY = pos.height - visibleRect.height;
            if (extraY > 0f)
            {
                pos.height -= extraY;
                pos.y += extraY * 0.5f;
            }
            var needed = Vector2.zero;
            if (pos.xMax > rect.xMax) needed.x += pos.xMax - rect.xMax;
            else if (pos.xMin < rect.xMin) needed.x -= rect.xMin - pos.xMin;
            if (pos.yMax > rect.yMax) needed.y += pos.yMax - rect.yMax;
            else if (pos.yMin < rect.yMin) needed.y -= rect.yMin - pos.yMin;
            var max = new Vector2(Math.Max(0f, viewRect.width - visibleRect.width), Math.Max(0f, viewRect.height - visibleRect.height));
            needed.x = Mathf.Clamp(needed.x + scrollPosition.x, 0f, max.x) - scrollPosition.x;
            needed.y = Mathf.Clamp(needed.y + scrollPosition.y, 0f, max.y) - scrollPosition.y;
            return needed;
        }
    }

    /// <summary>
    /// UnityGUI controls placed by explicit rects: they draw on Repaint and react to the input
    /// events OnGUI gets. Windows run after the rest of an OnGUI call when painting and before it
    /// for input, so they sit on top of the controls under them.
    /// </summary>
    public partial class GUI
    {
        public delegate void WindowFunction(int id);

        public enum ToolbarButtonSize
        {
            Fixed = 0,
            FitToContents = 1,
        }

        static Color s_Color = Color.white;
        static Color s_BackgroundColor = Color.white;
        static Color s_ContentColor = Color.white;
        static bool s_Changed;
        static bool s_Enabled = true;
        static int s_Depth;
        static GUISkin s_Skin;
        static string s_Tooltip = "";
        static string s_MouseTooltip = "";
        static string s_NextControlName;
        static readonly Dictionary<string, int> s_ControlNames = new Dictionary<string, int>();
        static readonly Stack<ScrollViewState> s_ScrollViews = new Stack<ScrollViewState>();

        static readonly int s_ButtonHint = "Button".GetHashCode();
        static readonly int s_RepeatButtonHint = "repeatButton".GetHashCode();
        static readonly int s_ToggleHint = "Toggle".GetHashCode();
        static readonly int s_ButtonGridHint = "ButtonGrid".GetHashCode();
        static readonly int s_SliderHint = "Slider".GetHashCode();
        static readonly int s_BeginGroupHint = "BeginGroup".GetHashCode();
        static readonly int s_ScrollviewHint = "scrollView".GetHashCode();
        static readonly int s_TextFieldHint = "TextField".GetHashCode();
        static readonly int s_PasswordFieldHint = "PasswordField".GetHashCode();
        static readonly int s_DragWindowHint = "DragWindow".GetHashCode();
        static float s_RepeatButtonTime;

        public static Color color { get => s_Color; set => s_Color = value; }
        public static Color backgroundColor { get => s_BackgroundColor; set => s_BackgroundColor = value; }
        public static Color contentColor { get => s_ContentColor; set => s_ContentColor = value; }
        public static bool changed { get => s_Changed; set => s_Changed = value; }
        public static bool enabled { get => s_Enabled; set => s_Enabled = value; }
        public static int depth { get => s_Depth; set => s_Depth = value; }

        public static GUISkin skin
        {
            get
            {
                if (s_Skin == null) DoSetSkin(null);
                return s_Skin;
            }
            set => DoSetSkin(value);
        }

        internal static void DoSetSkin(GUISkin newSkin)
        {
            if (newSkin == null) newSkin = BuiltinGuiSkin.Skin;
            s_Skin = newSkin;
            newSkin.MakeCurrent();
        }

        public static Matrix4x4 matrix
        {
            get => GUIClip.GetMatrix();
            set => GUIClip.SetMatrix(value);
        }

        /// <summary>The tooltip of the control under the mouse.</summary>
        public static string tooltip
        {
            get => s_Tooltip;
            set => s_Tooltip = value ?? "";
        }

        internal static string mouseTooltip => s_MouseTooltip;

        internal static void ResetState()
        {
            s_Color = Color.white;
            s_BackgroundColor = Color.white;
            s_ContentColor = Color.white;
            s_Changed = false;
            s_Enabled = true;
            s_ScrollViews.Clear();
            s_NextControlName = null;
            if (Event.current != null && Event.current.type == EventType.Repaint) s_MouseTooltip = "";
            s_Tooltip = s_MouseTooltip;
        }

        internal static void ResetScrollViews() => s_ScrollViews.Clear();

        static Event Current => Event.current ?? throw new ArgumentException("You can only call GUI functions from inside OnGUI.");

        /// <summary>Notes the tooltip of a hovered control.</summary>
        static void Hover(Rect position, GUIContent content)
        {
            if (content != null && !string.IsNullOrEmpty(content.tooltip) && Event.current?.type == EventType.Repaint &&
                position.Contains(Event.current.mousePosition))
                s_MouseTooltip = content.tooltip;
        }

        // ------------------------------------------------------------------ names and focus

        public static void SetNextControlName(string name) => s_NextControlName = name;

        static int NamedId(int id)
        {
            if (s_NextControlName != null)
            {
                s_ControlNames[s_NextControlName] = id;
                s_NextControlName = null;
            }
            return id;
        }

        public static string GetNameOfFocusedControl()
        {
            foreach (var pair in s_ControlNames)
                if (pair.Value == GUIUtility.keyboardControl && pair.Value != 0) return pair.Key;
            return "";
        }

        public static void FocusControl(string name)
        {
            if (name != null && s_ControlNames.TryGetValue(name, out int id)) GUIUtility.keyboardControl = id;
            else GUIUtility.keyboardControl = 0;
        }

        internal static void GrabMouseControl(int id) => GUIUtility.hotControl = id;

        internal static bool HasMouseControl(int id) => GUIUtility.hotControl == id;

        internal static void ReleaseMouseControl() => GUIUtility.hotControl = 0;

        // ------------------------------------------------------------------ labels, boxes, textures

        public static void Label(Rect position, string text) => Label(position, GUIContent.Temp(text), s_Skin != null ? s_Skin.label : skin.label);
        public static void Label(Rect position, Texture image) => Label(position, GUIContent.Temp(image), skin.label);
        public static void Label(Rect position, GUIContent content) => Label(position, content, skin.label);
        public static void Label(Rect position, string text, GUIStyle style) => Label(position, GUIContent.Temp(text), style);
        public static void Label(Rect position, Texture image, GUIStyle style) => Label(position, GUIContent.Temp(image), style);

        public static void Label(Rect position, GUIContent content, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            if (Current.type != EventType.Repaint) return;
            style.Draw(position, content, false, false, false, false);
            Hover(position, content);
        }

        public static void Box(Rect position, string text) => Box(position, GUIContent.Temp(text), skin.box);
        public static void Box(Rect position, Texture image) => Box(position, GUIContent.Temp(image), skin.box);
        public static void Box(Rect position, GUIContent content) => Box(position, content, skin.box);
        public static void Box(Rect position, string text, GUIStyle style) => Box(position, GUIContent.Temp(text), style);
        public static void Box(Rect position, Texture image, GUIStyle style) => Box(position, GUIContent.Temp(image), style);

        public static void Box(Rect position, GUIContent content, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            int id = GUIUtility.GetControlID(s_BeginGroupHint, FocusType.Passive);
            if (Current.type != EventType.Repaint) return;
            style.Draw(position, content, id);
            Hover(position, content);
        }

        public static void DrawTexture(Rect position, Texture image) => DrawTexture(position, image, ScaleMode.StretchToFill);

        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode) => DrawTexture(position, image, scaleMode, true);

        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend) => DrawTexture(position, image, scaleMode, alphaBlend, 0f);

        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend, float imageAspect) =>
            DrawTexture(position, image, scaleMode, alphaBlend, imageAspect, color, 0f, 0f);

        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend, float imageAspect, Color color, float borderWidth, float borderRadius) =>
            DrawTexture(position, image, scaleMode, alphaBlend, imageAspect, color, Vector4.one * borderWidth, borderRadius);

        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend, float imageAspect, Color color, Vector4 borderWidths, float borderRadius) =>
            DrawTexture(position, image, scaleMode, alphaBlend, imageAspect, color, borderWidths, Vector4.one * borderRadius);

        /// <summary>A texture scaled into a rect. Border widths draw only the outline of that width
        /// (rounded corners are drawn square).</summary>
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend, float imageAspect, Color color, Vector4 borderWidths, Vector4 borderRadiuses)
        {
            GUIUtility.CheckOnGUI();
            if (Current.type != EventType.Repaint) return;
            if (image == null)
            {
                Debug.LogWarning("null texture passed to GUI.DrawTexture");
                return;
            }
            if (imageAspect == 0f) imageAspect = image.width / (float)Math.Max(1, image.height);
            var screen = default(Rect);
            var source = default(Rect);
            if (!CalculateScaledTextureRects(position, scaleMode, imageAspect, ref screen, ref source)) return;
            var tint = color;
            if (!enabled) tint.a *= 0.5f;
            if (borderWidths == Vector4.zero)
            {
                GuiRender.DrawTexture(screen, image, source, tint);
                return;
            }
            // Left, top, right, bottom strips.
            float l = borderWidths.x, t = borderWidths.y, r = borderWidths.z, b = borderWidths.w;
            GuiRender.DrawTexture(new Rect(screen.x, screen.y, l, screen.height), image, source, tint);
            GuiRender.DrawTexture(new Rect(screen.xMax - r, screen.y, r, screen.height), image, source, tint);
            GuiRender.DrawTexture(new Rect(screen.x + l, screen.y, Math.Max(0f, screen.width - l - r), t), image, source, tint);
            GuiRender.DrawTexture(new Rect(screen.x + l, screen.yMax - b, Math.Max(0f, screen.width - l - r), b), image, source, tint);
        }

        internal static bool CalculateScaledTextureRects(Rect position, ScaleMode scaleMode, float imageAspect, ref Rect outScreenRect, ref Rect outSourceRect)
        {
            float destAspect = position.width / Math.Max(0.0001f, position.height);
            switch (scaleMode)
            {
                case ScaleMode.StretchToFill:
                    outScreenRect = position;
                    outSourceRect = new Rect(0, 0, 1, 1);
                    return true;
                case ScaleMode.ScaleAndCrop:
                    outScreenRect = position;
                    if (destAspect > imageAspect)
                    {
                        float h = imageAspect / destAspect;
                        outSourceRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
                    }
                    else
                    {
                        float w = destAspect / imageAspect;
                        outSourceRect = new Rect(0.5f - w * 0.5f, 0f, w, 1f);
                    }
                    return true;
                case ScaleMode.ScaleToFit:
                    if (destAspect > imageAspect)
                    {
                        float w = imageAspect / destAspect;
                        outScreenRect = new Rect(position.xMin + position.width * (1f - w) * 0.5f, position.yMin, w * position.width, position.height);
                    }
                    else
                    {
                        float h = destAspect / imageAspect;
                        outScreenRect = new Rect(position.xMin, position.yMin + position.height * (1f - h) * 0.5f, position.width, h * position.height);
                    }
                    outSourceRect = new Rect(0, 0, 1, 1);
                    return true;
            }
            return false;
        }

        public static void DrawTextureWithTexCoords(Rect position, Texture image, Rect texCoords) => DrawTextureWithTexCoords(position, image, texCoords, true);

        public static void DrawTextureWithTexCoords(Rect position, Texture image, Rect texCoords, bool alphaBlend)
        {
            GUIUtility.CheckOnGUI();
            if (Current.type != EventType.Repaint || image == null) return;
            var tint = color;
            if (!enabled) tint.a *= 0.5f;
            GuiRender.DrawTexture(position, image, texCoords, tint);
        }

        // ------------------------------------------------------------------ buttons and toggles

        public static bool Button(Rect position, string text) => Button(position, GUIContent.Temp(text), skin.button);
        public static bool Button(Rect position, Texture image) => Button(position, GUIContent.Temp(image), skin.button);
        public static bool Button(Rect position, GUIContent content) => Button(position, content, skin.button);
        public static bool Button(Rect position, string text, GUIStyle style) => Button(position, GUIContent.Temp(text), style);
        public static bool Button(Rect position, Texture image, GUIStyle style) => Button(position, GUIContent.Temp(image), style);

        public static bool Button(Rect position, GUIContent content, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            int id = NamedId(GUIUtility.GetControlID(s_ButtonHint, FocusType.Passive, position));
            return Button(position, id, content, style);
        }

        internal static bool Button(Rect position, int id, GUIContent content, GUIStyle style) => DoButton(position, id, content, style);

        /// <summary>A click: pressed and released over the control (or Space or Return with focus).</summary>
        internal static bool DoButton(Rect position, int id, GUIContent content, GUIStyle style) =>
            DoControl(position, id, false, position.Contains(Current.mousePosition), content, style);

        internal static bool DoControl(Rect position, int id, bool on, bool hover, GUIContent content, GUIStyle style)
        {
            var e = Current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUIUtility.HitTest(position, e))
                    {
                        GrabMouseControl(id);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (HasMouseControl(id)) e.Use();
                    break;
                case EventType.MouseUp:
                    if (HasMouseControl(id))
                    {
                        ReleaseMouseControl();
                        e.Use();
                        if (GUIUtility.HitTest(position, e))
                        {
                            changed = true;
                            return true;
                        }
                    }
                    break;
                case EventType.KeyDown:
                    bool submit = e.character == ' ' || e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
                    if (submit && GUIUtility.keyboardControl == id)
                    {
                        e.Use();
                        changed = true;
                        return true;
                    }
                    break;
                case EventType.Repaint:
                    style.Draw(position, content, id, on, hover);
                    Hover(position, content);
                    break;
            }
            return false;
        }

        public static bool RepeatButton(Rect position, string text) => DoRepeatButton(position, GUIContent.Temp(text), skin.button, FocusType.Passive);
        public static bool RepeatButton(Rect position, Texture image) => DoRepeatButton(position, GUIContent.Temp(image), skin.button, FocusType.Passive);
        public static bool RepeatButton(Rect position, GUIContent content) => DoRepeatButton(position, content, skin.button, FocusType.Passive);
        public static bool RepeatButton(Rect position, string text, GUIStyle style) => DoRepeatButton(position, GUIContent.Temp(text), style, FocusType.Passive);
        public static bool RepeatButton(Rect position, Texture image, GUIStyle style) => DoRepeatButton(position, GUIContent.Temp(image), style, FocusType.Passive);
        public static bool RepeatButton(Rect position, GUIContent content, GUIStyle style) => DoRepeatButton(position, content, style, FocusType.Passive);

        /// <summary>True on every Repaint while held down over the control.</summary>
        static bool DoRepeatButton(Rect position, GUIContent content, GUIStyle style, FocusType focusType)
        {
            GUIUtility.CheckOnGUI();
            int id = NamedId(GUIUtility.GetControlID(s_RepeatButtonHint, focusType, position));
            var e = Current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUIUtility.HitTest(position, e))
                    {
                        GrabMouseControl(id);
                        e.Use();
                    }
                    return false;
                case EventType.MouseUp:
                    if (HasMouseControl(id))
                    {
                        ReleaseMouseControl();
                        e.Use();
                        return GUIUtility.HitTest(position, e);
                    }
                    return false;
                case EventType.Repaint:
                    style.Draw(position, content, id, false, position.Contains(e.mousePosition));
                    return HasMouseControl(id) && position.Contains(e.mousePosition);
            }
            return false;
        }

        public static bool Toggle(Rect position, bool value, string text) => Toggle(position, value, GUIContent.Temp(text), skin.toggle);
        public static bool Toggle(Rect position, bool value, Texture image) => Toggle(position, value, GUIContent.Temp(image), skin.toggle);
        public static bool Toggle(Rect position, bool value, GUIContent content) => Toggle(position, value, content, skin.toggle);
        public static bool Toggle(Rect position, bool value, string text, GUIStyle style) => Toggle(position, value, GUIContent.Temp(text), style);
        public static bool Toggle(Rect position, bool value, Texture image, GUIStyle style) => Toggle(position, value, GUIContent.Temp(image), style);

        public static bool Toggle(Rect position, bool value, GUIContent content, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            return DoToggle(position, NamedId(GUIUtility.GetControlID(s_ToggleHint, FocusType.Passive, position)), value, content, style);
        }

        public static bool Toggle(Rect position, int id, bool value, GUIContent content, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            return DoToggle(position, id, value, content, style);
        }

        internal static bool DoToggle(Rect position, int id, bool value, GUIContent content, GUIStyle style) =>
            DoControl(position, id, value, position.Contains(Current.mousePosition), content, style) ? !value : value;

        // ------------------------------------------------------------------ toolbars and grids

        public static int Toolbar(Rect position, int selected, string[] texts) => Toolbar(position, selected, GUIContent.Temp(texts), skin.button);
        public static int Toolbar(Rect position, int selected, Texture[] images) => Toolbar(position, selected, GUIContent.Temp(images), skin.button);
        public static int Toolbar(Rect position, int selected, GUIContent[] contents) => Toolbar(position, selected, contents, skin.button);
        public static int Toolbar(Rect position, int selected, string[] texts, GUIStyle style) => Toolbar(position, selected, GUIContent.Temp(texts), style);
        public static int Toolbar(Rect position, int selected, Texture[] images, GUIStyle style) => Toolbar(position, selected, GUIContent.Temp(images), style);
        public static int Toolbar(Rect position, int selected, GUIContent[] contents, GUIStyle style) => Toolbar(position, selected, contents, style, ToolbarButtonSize.Fixed);
        public static int Toolbar(Rect position, int selected, GUIContent[] contents, GUIStyle style, ToolbarButtonSize buttonSize) =>
            DoButtonGrid(position, selected, contents, contents?.Length ?? 0, style, buttonSize);

        public static int SelectionGrid(Rect position, int selected, string[] texts, int xCount) => SelectionGrid(position, selected, GUIContent.Temp(texts), xCount, skin.button);
        public static int SelectionGrid(Rect position, int selected, Texture[] images, int xCount) => SelectionGrid(position, selected, GUIContent.Temp(images), xCount, skin.button);
        public static int SelectionGrid(Rect position, int selected, GUIContent[] content, int xCount) => SelectionGrid(position, selected, content, xCount, skin.button);
        public static int SelectionGrid(Rect position, int selected, string[] texts, int xCount, GUIStyle style) => SelectionGrid(position, selected, GUIContent.Temp(texts), xCount, style);
        public static int SelectionGrid(Rect position, int selected, Texture[] images, int xCount, GUIStyle style) => SelectionGrid(position, selected, GUIContent.Temp(images), xCount, style);
        public static int SelectionGrid(Rect position, int selected, GUIContent[] contents, int xCount, GUIStyle style) =>
            DoButtonGrid(position, selected, contents, xCount, style, ToolbarButtonSize.Fixed);

        /// <summary>The cells of a button grid: <paramref name="xCount"/> per row, sharing the
        /// rect less the margins between them.</summary>
        internal static Rect[] GridRects(Rect position, int count, int xCount, GUIStyle style, GUIContent[] contents, ToolbarButtonSize buttonSize)
        {
            xCount = Math.Max(1, xCount);
            int rows = (count + xCount - 1) / xCount;
            float spacingX = Math.Max(style.margin.left, style.margin.right);
            float spacingY = Math.Max(style.margin.top, style.margin.bottom);
            float cellW = (position.width - spacingX * (xCount - 1)) / xCount;
            float cellH = rows > 0 ? (position.height - spacingY * (rows - 1)) / rows : 0f;
            var rects = new Rect[count];
            float[] widths = null;
            if (buttonSize == ToolbarButtonSize.FitToContents && rows == 1)
            {
                widths = new float[count];
                float total = 0f;
                for (int i = 0; i < count; i++) total += widths[i] = style.CalcSize(contents[i]).x;
                float scale = total > 0f ? (position.width - spacingX * (count - 1)) / total : 1f;
                for (int i = 0; i < count; i++) widths[i] *= scale;
            }
            float x = position.x;
            for (int i = 0; i < count; i++)
            {
                int row = i / xCount, col = i % xCount;
                if (col == 0) x = position.x;
                float w = widths != null ? widths[i] : cellW;
                rects[i] = new Rect(x, position.y + row * (cellH + spacingY), w, cellH);
                x += w + spacingX;
            }
            return rects;
        }

        static int DoButtonGrid(Rect position, int selected, GUIContent[] contents, int xCount, GUIStyle style, ToolbarButtonSize buttonSize)
        {
            GUIUtility.CheckOnGUI();
            int count = contents?.Length ?? 0;
            if (count == 0) return selected;
            if (xCount <= 0)
            {
                Debug.LogWarning("You are trying to create a SelectionGrid with zero or less elements to be displayed in the horizontal direction. Set xCount to a positive value.");
                return selected;
            }
            int id = NamedId(GUIUtility.GetControlID(s_ButtonGridHint, FocusType.Passive, position));
            var rects = GridRects(position, count, xCount, style, contents, buttonSize);
            var e = Current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUIUtility.HitTest(position, e) && Hit(rects, e.mousePosition) >= 0)
                    {
                        GrabMouseControl(id);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (HasMouseControl(id)) e.Use();
                    break;
                case EventType.MouseUp:
                    if (HasMouseControl(id))
                    {
                        ReleaseMouseControl();
                        e.Use();
                        int hit = Hit(rects, e.mousePosition);
                        if (hit >= 0)
                        {
                            changed = true;
                            return hit;
                        }
                    }
                    break;
                case EventType.Repaint:
                {
                    int hover = Hit(rects, e.mousePosition);
                    bool pressed = HasMouseControl(id);
                    for (int i = 0; i < count; i++)
                    {
                        bool isHover = i == hover && (GUIUtility.hotControl == 0 || pressed);
                        style.Draw(rects[i], contents[i], isHover, pressed && isHover, i == selected, false);
                        Hover(rects[i], contents[i]);
                    }
                    break;
                }
            }
            return selected;
        }

        static int Hit(Rect[] rects, Vector2 point)
        {
            for (int i = 0; i < rects.Length; i++)
                if (rects[i].Contains(point)) return i;
            return -1;
        }

        // ------------------------------------------------------------------ text fields

        public static string TextField(Rect position, string text) => TextField(position, text, -1, skin.textField);
        public static string TextField(Rect position, string text, int maxLength) => TextField(position, text, maxLength, skin.textField);
        public static string TextField(Rect position, string text, GUIStyle style) => TextField(position, text, -1, style);

        public static string TextField(Rect position, string text, int maxLength, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            var content = new GUIContent(text);
            DoTextField(position, NamedId(GUIUtility.GetControlID(s_TextFieldHint, FocusType.Keyboard, position)), content, false, maxLength, style);
            return content.text;
        }

        public static string TextArea(Rect position, string text) => TextArea(position, text, -1, skin.textArea);
        public static string TextArea(Rect position, string text, int maxLength) => TextArea(position, text, maxLength, skin.textArea);
        public static string TextArea(Rect position, string text, GUIStyle style) => TextArea(position, text, -1, style);

        public static string TextArea(Rect position, string text, int maxLength, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            var content = new GUIContent(text);
            DoTextField(position, NamedId(GUIUtility.GetControlID(s_TextFieldHint, FocusType.Keyboard, position)), content, true, maxLength, style);
            return content.text;
        }

        public static string PasswordField(Rect position, string password, char maskChar) => PasswordField(position, password, maskChar, -1, skin.textField);
        public static string PasswordField(Rect position, string password, char maskChar, int maxLength) => PasswordField(position, password, maskChar, maxLength, skin.textField);
        public static string PasswordField(Rect position, string password, char maskChar, GUIStyle style) => PasswordField(position, password, maskChar, -1, style);

        public static string PasswordField(Rect position, string password, char maskChar, int maxLength, GUIStyle style)
        {
            GUIUtility.CheckOnGUI();
            var content = new GUIContent(password);
            DoTextField(position, NamedId(GUIUtility.GetControlID(s_PasswordFieldHint, FocusType.Keyboard, position)), content, false, maxLength, style, null, maskChar);
            return content.text;
        }

        internal static string PasswordFieldGetStrToShow(string password, char maskChar) =>
            Event.current != null && (Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseDown)
                ? new string(maskChar, password?.Length ?? 0)
                : password;

        internal static void DoTextField(Rect position, int id, GUIContent content, bool multiline, int maxLength, GUIStyle style) =>
            DoTextField(position, id, content, multiline, maxLength, style, null, '\0');

        internal static void DoTextField(Rect position, int id, GUIContent content, bool multiline, int maxLength, GUIStyle style, string secureText) =>
            DoTextField(position, id, content, multiline, maxLength, style, secureText, '\0');

        /// <summary>
        /// A text field: a click focuses it and places the cursor, a drag selects, typed characters
        /// and editing keys change the text (written back into <paramref name="content"/>). With a
        /// mask character the text shows masked.
        /// </summary>
        internal static void DoTextField(Rect position, int id, GUIContent content, bool multiline, int maxLength, GUIStyle style, string secureText, char maskChar)
        {
            if (maxLength >= 0 && content.text != null && content.text.Length > maxLength)
                content.text = content.text.Substring(0, maxLength);
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), id);
            editor.text = content.text ?? "";
            editor.position = position;
            editor.style = style;
            editor.multiline = multiline;
            editor.controlID = id;
            editor.isPasswordField = maskChar != '\0';
            editor.DetectFocusChange();
            var e = Current;
            bool focused = GUIUtility.keyboardControl == id;
            bool edited = false;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUIUtility.HitTest(position, e))
                    {
                        GrabMouseControl(id);
                        GUIUtility.keyboardControl = id;
                        editor.OnFocus();
                        editor.MoveCursorToPosition(e.mousePosition);
                        if (e.clickCount == 2 && skin.settings.doubleClickSelectsWord)
                        {
                            editor.SelectCurrentWord();
                            editor.DblClickSnap(TextEditor.DblClickSnapping.WORDS);
                            editor.MouseDragSelectsWholeWords(true);
                        }
                        else if (e.clickCount == 3 && skin.settings.tripleClickSelectsLine)
                        {
                            editor.SelectCurrentParagraph();
                            editor.MouseDragSelectsWholeWords(true);
                            editor.DblClickSnap(TextEditor.DblClickSnapping.PARAGRAPHS);
                        }
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (HasMouseControl(id))
                    {
                        if (e.shift) editor.MoveCursorToPosition(e.mousePosition);
                        else editor.SelectToPosition(e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (HasMouseControl(id))
                    {
                        editor.MouseDragSelectsWholeWords(false);
                        ReleaseMouseControl();
                        e.Use();
                    }
                    break;
                case EventType.KeyDown:
                    if (!focused) break;
                    if (!((e.control || e.command) && e.keyCode == KeyCode.Z)) editor.SaveBackup();
                    if (editor.HandleKeyEvent(e))
                    {
                        e.Use();
                        edited = true;
                        break;
                    }
                    if (e.keyCode == KeyCode.Tab || e.character == '\t') break;
                    char c = e.character;
                    if (c == '\n' && !multiline && !e.alt) break;
                    var font = style.ResolvedFont;
                    if (c != '\0' && (c == '\n' || !char.IsControl(c)) && (font == null || font.HasCharacter(c) || c == '\n'))
                    {
                        editor.Insert(c);
                        edited = true;
                        e.Use();
                    }
                    else if (c == '\0' && e.keyCode != KeyCode.None && !e.functionKey && !e.control && !e.command)
                    {
                        // The character arrives in its own event: keep the key from reaching others.
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                {
                    var shown = editor.isPasswordField ? new GUIContent(new string(maskChar, editor.text.Length)) : new GUIContent(editor.text);
                    if (focused)
                    {
                        editor.UpdateScrollOffsetIfNeeded(e);
                        var saved = style.Internal_clipOffset;
                        style.Internal_clipOffset = -editor.scrollOffset;
                        style.DrawWithTextSelection(position, shown, id, editor.cursorIndex, editor.selectIndex);
                        style.Internal_clipOffset = saved;
                    }
                    else
                    {
                        style.Draw(position, shown, id, false, position.Contains(e.mousePosition));
                    }
                    break;
                }
            }
            if (edited)
            {
                var text = editor.text;
                if (maxLength >= 0 && text.Length > maxLength)
                {
                    text = text.Substring(0, maxLength);
                    editor.text = text;
                }
                if (content.text != text)
                {
                    content.text = text;
                    changed = true;
                }
            }
            editor.UpdateScrollOffsetIfNeeded(e);
        }

        // ------------------------------------------------------------------ sliders and scrollbars

        public static float HorizontalSlider(Rect position, float value, float leftValue, float rightValue) =>
            Slider(position, value, 0f, leftValue, rightValue, skin.horizontalSlider, skin.horizontalSliderThumb, true, 0);

        public static float HorizontalSlider(Rect position, float value, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb) =>
            Slider(position, value, 0f, leftValue, rightValue, slider, thumb, true, 0);

        public static float HorizontalSlider(Rect position, float value, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb, GUIStyle thumbExtent) =>
            Slider(position, value, 0f, leftValue, rightValue, slider, thumb, true, 0, thumbExtent);

        public static float VerticalSlider(Rect position, float value, float topValue, float bottomValue) =>
            Slider(position, value, 0f, topValue, bottomValue, skin.verticalSlider, skin.verticalSliderThumb, false, 0);

        public static float VerticalSlider(Rect position, float value, float topValue, float bottomValue, GUIStyle slider, GUIStyle thumb) =>
            Slider(position, value, 0f, topValue, bottomValue, slider, thumb, false, 0);

        public static float VerticalSlider(Rect position, float value, float topValue, float bottomValue, GUIStyle slider, GUIStyle thumb, GUIStyle thumbExtent) =>
            Slider(position, value, 0f, topValue, bottomValue, slider, thumb, false, 0, thumbExtent);

        /// <summary>
        /// A slider (<paramref name="size"/> 0: the thumb has the thumb style's size and a click
        /// jumps to the value) or a scrollbar (the thumb covers <paramref name="size"/> of the range
        /// and a click on the trough moves a page).
        /// </summary>
        public static float Slider(Rect position, float value, float size, float start, float end, GUIStyle slider, GUIStyle thumb, bool horiz, int id, GUIStyle thumbExtent = null)
        {
            GUIUtility.CheckOnGUI();
            if (id == 0) id = NamedId(GUIUtility.GetControlID(s_SliderHint, FocusType.Passive, position));
            var e = Current;
            var track = slider.padding.Remove(position);
            float trackLength = horiz ? track.width : track.height;
            float range = end - start;
            float thumbBase = horiz ? (thumb.fixedWidth != 0f ? thumb.fixedWidth : thumb.padding.horizontal) : (thumb.fixedHeight != 0f ? thumb.fixedHeight : thumb.padding.vertical);
            float thumbLength = size > 0f && Math.Abs(range) > 0f
                ? Math.Max(thumbBase, trackLength * size / Math.Abs(range))
                : thumbBase;
            thumbLength = Math.Min(thumbLength, trackLength);
            float travel = Math.Max(0f, trackLength - thumbLength);
            float span = range - (size > 0f ? Math.Sign(range) * size : 0f);
            float lo = Math.Min(start, end), hi = Math.Max(start, end);
            float clampHi = size > 0f ? hi - size : hi;
            float Clamp(float v) => Mathf.Clamp(v, lo, Math.Max(lo, clampHi));
            value = Clamp(value);
            float Fraction(float v) => Math.Abs(span) > 0f ? (v - start) / span : 0f;
            float pos = Mathf.Clamp01(Fraction(value)) * travel;
            Rect ThumbRect() => horiz
                ? new Rect(track.x + pos, position.y, thumbLength, position.height)
                : new Rect(position.x, track.y + pos, position.width, thumbLength);
            float Along(Vector2 p) => horiz ? p.x - track.x : p.y - track.y;
            float ValueAt(float along) => travel > 0f ? start + along / travel * span : start;
            var state = (SliderDragState)GUIUtility.GetStateObject(typeof(SliderDragState), id);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!GUIUtility.HitTest(position, e) || travel <= 0f && size <= 0f) break;
                    GrabMouseControl(id);
                    e.Use();
                    var thumbRect = ThumbRect();
                    if (thumbRect.Contains(e.mousePosition))
                    {
                        state.grabOffset = Along(e.mousePosition) - pos;
                    }
                    else if (size <= 0f)
                    {
                        // A slider jumps to the click.
                        state.grabOffset = thumbLength / 2f;
                        float v = Clamp(ValueAt(Along(e.mousePosition) - state.grabOffset));
                        if (v != value)
                        {
                            value = v;
                            changed = true;
                        }
                    }
                    else
                    {
                        // A scrollbar moves a page towards the click.
                        state.grabOffset = float.NaN;
                        bool before = Along(e.mousePosition) < pos;
                        float v = Clamp(value + (before ? -1f : 1f) * Math.Sign(range) * size * 0.9f);
                        if (v != value)
                        {
                            value = v;
                            changed = true;
                        }
                    }
                    break;
                case EventType.MouseDrag:
                    if (!HasMouseControl(id)) break;
                    e.Use();
                    if (float.IsNaN(state.grabOffset)) break;
                    {
                        float v = Clamp(ValueAt(Along(e.mousePosition) - state.grabOffset));
                        if (v != value)
                        {
                            value = v;
                            changed = true;
                        }
                    }
                    break;
                case EventType.MouseUp:
                    if (HasMouseControl(id))
                    {
                        ReleaseMouseControl();
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    slider.Draw(position, GUIContent.none, id);
                    var rect = ThumbRect();
                    thumb.Draw(rect, GUIContent.none, id, false, rect.Contains(e.mousePosition));
                    break;
            }
            return value;
        }

        /// <summary>Where a dragged thumb was grabbed.</summary>
        internal sealed class SliderDragState
        {
            internal float grabOffset;
        }

        public static float HorizontalScrollbar(Rect position, float value, float size, float leftValue, float rightValue) =>
            HorizontalScrollbar(position, value, size, leftValue, rightValue, skin.horizontalScrollbar);

        public static float HorizontalScrollbar(Rect position, float value, float size, float leftValue, float rightValue, GUIStyle style) =>
            Scroller(position, value, size, leftValue, rightValue, style, ScrollbarPart(style, "thumb", skin.horizontalScrollbarThumb), null, null, true);

        public static float VerticalScrollbar(Rect position, float value, float size, float topValue, float bottomValue) =>
            VerticalScrollbar(position, value, size, topValue, bottomValue, skin.verticalScrollbar);

        public static float VerticalScrollbar(Rect position, float value, float size, float topValue, float bottomValue, GUIStyle style) =>
            Scroller(position, value, size, topValue, bottomValue, style, ScrollbarPart(style, "thumb", skin.verticalScrollbarThumb), null, null, false);

        static GUIStyle ScrollbarPart(GUIStyle bar, string part, GUIStyle fallback) =>
            (bar != null && skin.FindStyle(bar.name + part) is GUIStyle found) ? found : fallback;

        internal static float Scroller(Rect position, float value, float size, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb, GUIStyle leftButton, GUIStyle rightButton, bool horiz)
        {
            GUIUtility.CheckOnGUI();
            int id = GUIUtility.GetControlID(s_SliderHint, FocusType.Passive, position);
            return Slider(position, value, size, leftValue, rightValue, slider, thumb, horiz, id);
        }

        // ------------------------------------------------------------------ groups and clips

        public static void BeginGroup(Rect position) => BeginGroup(position, GUIContent.none, GUIStyle.none);
        public static void BeginGroup(Rect position, string text) => BeginGroup(position, GUIContent.Temp(text), GUIStyle.none);
        public static void BeginGroup(Rect position, Texture image) => BeginGroup(position, GUIContent.Temp(image), GUIStyle.none);
        public static void BeginGroup(Rect position, GUIContent content) => BeginGroup(position, content, GUIStyle.none);
        public static void BeginGroup(Rect position, GUIStyle style) => BeginGroup(position, GUIContent.none, style);
        public static void BeginGroup(Rect position, string text, GUIStyle style) => BeginGroup(position, GUIContent.Temp(text), style);
        public static void BeginGroup(Rect position, Texture image, GUIStyle style) => BeginGroup(position, GUIContent.Temp(image), style);
        public static void BeginGroup(Rect position, GUIContent content, GUIStyle style) => BeginGroup(position, content, style, Vector2.zero);

        /// <summary>Clips to the rect and moves the origin to its corner.</summary>
        internal static void BeginGroup(Rect position, GUIContent content, GUIStyle style, Vector2 scrollOffset)
        {
            GUIUtility.CheckOnGUI();
            int id = GUIUtility.GetControlID(s_BeginGroupHint, FocusType.Passive);
            if (content != GUIContent.none || style != GUIStyle.none)
            {
                if (Current.type == EventType.Repaint) style.Draw(position, content, id);
                else if (GUIUtility.HitTest(position, Current)) GUIUtility.mouseUsed = true;
            }
            GUIClip.Push(position, scrollOffset, Vector2.zero, false);
        }

        public static void EndGroup()
        {
            GUIUtility.CheckOnGUI();
            GUIClip.Pop();
        }

        public static void BeginClip(Rect position, Vector2 scrollOffset, Vector2 renderOffset, bool resetOffset)
        {
            GUIUtility.CheckOnGUI();
            GUIClip.Push(position, scrollOffset, renderOffset, resetOffset);
        }

        public static void BeginClip(Rect position)
        {
            GUIUtility.CheckOnGUI();
            GUIClip.Push(position, Vector2.zero, Vector2.zero, false);
        }

        public static void EndClip()
        {
            GUIUtility.CheckOnGUI();
            GUIClip.Pop();
        }

        // ------------------------------------------------------------------ scroll views

        public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect) =>
            BeginScrollView(position, scrollPosition, viewRect, false, false, skin.horizontalScrollbar, skin.verticalScrollbar, skin.scrollView);

        public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical) =>
            BeginScrollView(position, scrollPosition, viewRect, alwaysShowHorizontal, alwaysShowVertical, skin.horizontalScrollbar, skin.verticalScrollbar, skin.scrollView);

        public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar) =>
            BeginScrollView(position, scrollPosition, viewRect, false, false, horizontalScrollbar, verticalScrollbar, skin.scrollView);

        public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar) =>
            BeginScrollView(position, scrollPosition, viewRect, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, skin.scrollView);

        /// <summary>A clipped view over <paramref name="viewRect"/> with scrollbars where the view is
        /// larger than the rect; the wheel scrolls it (in EndScrollView).</summary>
        internal static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical,
            GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background)
        {
            GUIUtility.CheckOnGUI();
            int id = GUIUtility.GetControlID(s_ScrollviewHint, FocusType.Passive);
            var state = (ScrollViewState)GUIUtility.GetStateObject(typeof(ScrollViewState), id);
            if (state.apply)
            {
                scrollPosition = state.scrollPosition;
                state.apply = false;
            }
            state.position = position;
            state.scrollPosition = scrollPosition;
            state.visibleRect = new Rect(0, 0, position.width, position.height);
            state.viewRect = viewRect;
            s_ScrollViews.Push(state);

            var clip = new Rect(position.x, position.y, position.width, position.height);
            bool needH = alwaysShowHorizontal, needV = alwaysShowVertical;
            float hHeight = horizontalScrollbar.fixedHeight + horizontalScrollbar.margin.top;
            float vWidth = verticalScrollbar.fixedWidth + verticalScrollbar.margin.left;
            if (needH || viewRect.width > clip.width)
            {
                needH = true;
                clip.height = position.height - hHeight;
            }
            if (needV || viewRect.height > clip.height)
            {
                needV = true;
                clip.width = position.width - vWidth;
                if (!needH && viewRect.width > clip.width)
                {
                    needH = true;
                    clip.height = position.height - hHeight;
                }
            }
            state.visibleRect = new Rect(0, 0, clip.width, clip.height);

            if (Current.type == EventType.Repaint && background != GUIStyle.none) background.Draw(position, position.Contains(Current.mousePosition), false, needH && needV, false);
            if (needH && horizontalScrollbar != GUIStyle.none)
            {
                scrollPosition.x = HorizontalScrollbar(new Rect(position.x, position.yMax - horizontalScrollbar.fixedHeight, clip.width, horizontalScrollbar.fixedHeight),
                    scrollPosition.x, Math.Min(clip.width, viewRect.width), 0f, viewRect.width, horizontalScrollbar);
            }
            else
            {
                GUIUtility.GetControlID(s_SliderHint, FocusType.Passive);
                GUIUtility.GetControlID(s_RepeatButtonHint, FocusType.Passive);
                GUIUtility.GetControlID(s_RepeatButtonHint, FocusType.Passive);
                scrollPosition.x = horizontalScrollbar != GUIStyle.none ? 0f : Mathf.Clamp(scrollPosition.x, 0f, Math.Max(viewRect.width - position.width, 0f));
            }
            if (needV && verticalScrollbar != GUIStyle.none)
            {
                scrollPosition.y = VerticalScrollbar(new Rect(clip.xMax + verticalScrollbar.margin.left, clip.y, verticalScrollbar.fixedWidth, clip.height),
                    scrollPosition.y, Math.Min(clip.height, viewRect.height), 0f, viewRect.height, verticalScrollbar);
            }
            else
            {
                GUIUtility.GetControlID(s_SliderHint, FocusType.Passive);
                GUIUtility.GetControlID(s_RepeatButtonHint, FocusType.Passive);
                GUIUtility.GetControlID(s_RepeatButtonHint, FocusType.Passive);
                scrollPosition.y = verticalScrollbar != GUIStyle.none ? 0f : Mathf.Clamp(scrollPosition.y, 0f, Math.Max(viewRect.height - position.height, 0f));
            }
            state.scrollPosition = scrollPosition;
            GUIClip.Push(clip, new Vector2(Mathf.Round(-scrollPosition.x - viewRect.x), Mathf.Round(-scrollPosition.y - viewRect.y)), Vector2.zero, false);
            return scrollPosition;
        }

        public static void EndScrollView() => EndScrollView(true);

        public static void EndScrollView(bool handleScrollWheel)
        {
            GUIUtility.CheckOnGUI();
            if (s_ScrollViews.Count == 0)
            {
                Debug.LogError("Unmatched EndScrollView");
                return;
            }
            var state = s_ScrollViews.Pop();
            GUIClip.Pop();
            var e = Current;
            if (handleScrollWheel && e.type == EventType.ScrollWheel && state.position.Contains(e.mousePosition))
            {
                var max = new Vector2(Math.Max(0f, state.viewRect.width - state.visibleRect.width), Math.Max(0f, state.viewRect.height - state.visibleRect.height));
                var target = state.scrollPosition + e.delta * 20f;
                target.x = Mathf.Clamp(target.x, 0f, max.x);
                target.y = Mathf.Clamp(target.y, 0f, max.y);
                if (target != state.scrollPosition)
                {
                    state.scrollPosition = target;
                    state.apply = true;
                    e.Use();
                }
            }
        }

        internal static ScrollViewState GetTopScrollView() => s_ScrollViews.Count > 0 ? s_ScrollViews.Peek() : null;

        public static void ScrollTo(Rect position) => GetTopScrollView()?.ScrollTo(position);

        public static bool ScrollTowards(Rect position, float maxDelta) => GetTopScrollView()?.ScrollTowards(position, maxDelta) ?? false;

        // ------------------------------------------------------------------ windows

        /// <summary>A window of one OnGUI owner, kept between frames.</summary>
        internal sealed class GuiWindow
        {
            internal int Id;
            internal Rect Rect;
            internal Rect? Moved;
            internal WindowFunction Func;
            internal GUIContent Title;
            internal GUIStyle Style;
            internal GUISkin Skin;
            internal bool Modal;
            internal bool Layout;
            internal GUILayoutOption[] Options;
            internal int UsedFrame;
            internal Color Color, BackgroundColor, ContentColor;
            internal bool Enabled;
            internal Matrix4x4 Matrix;
        }

        sealed class WindowSet
        {
            internal readonly Dictionary<int, GuiWindow> ById = new Dictionary<int, GuiWindow>();
            /// <summary>Back to front.</summary>
            internal readonly List<int> Order = new List<int>();
            internal int Focused = -1;
        }

        static readonly Dictionary<int, WindowSet> s_Windows = new Dictionary<int, WindowSet>();
        static WindowSet s_CurrentWindows;
        static int s_CurrentOwner;
        static GuiWindow s_RunningWindow;
        static int s_WindowPass;

        internal static bool HasModalWindow
        {
            get
            {
                foreach (var set in s_Windows.Values)
                    foreach (var w in set.ById.Values)
                        if (w.Modal && w.UsedFrame >= Time.frameCount - 1) return true;
                return false;
            }
        }

        public static Rect Window(int id, Rect clientRect, WindowFunction func, string text) => DoWindow(id, clientRect, func, GUIContent.Temp(text), skin.window, skin, false, false, null);
        public static Rect Window(int id, Rect clientRect, WindowFunction func, Texture image) => DoWindow(id, clientRect, func, GUIContent.Temp(image), skin.window, skin, false, false, null);
        public static Rect Window(int id, Rect clientRect, WindowFunction func, GUIContent content) => DoWindow(id, clientRect, func, content, skin.window, skin, false, false, null);
        public static Rect Window(int id, Rect clientRect, WindowFunction func, string text, GUIStyle style) => DoWindow(id, clientRect, func, GUIContent.Temp(text), style, skin, false, false, null);
        public static Rect Window(int id, Rect clientRect, WindowFunction func, Texture image, GUIStyle style) => DoWindow(id, clientRect, func, GUIContent.Temp(image), style, skin, false, false, null);
        public static Rect Window(int id, Rect clientRect, WindowFunction func, GUIContent title, GUIStyle style) => DoWindow(id, clientRect, func, title, style, skin, false, false, null);

        public static Rect ModalWindow(int id, Rect clientRect, WindowFunction func, string text) => DoWindow(id, clientRect, func, GUIContent.Temp(text), skin.window, skin, true, false, null);
        public static Rect ModalWindow(int id, Rect clientRect, WindowFunction func, Texture image) => DoWindow(id, clientRect, func, GUIContent.Temp(image), skin.window, skin, true, false, null);
        public static Rect ModalWindow(int id, Rect clientRect, WindowFunction func, GUIContent content) => DoWindow(id, clientRect, func, content, skin.window, skin, true, false, null);
        public static Rect ModalWindow(int id, Rect clientRect, WindowFunction func, string text, GUIStyle style) => DoWindow(id, clientRect, func, GUIContent.Temp(text), style, skin, true, false, null);
        public static Rect ModalWindow(int id, Rect clientRect, WindowFunction func, Texture image, GUIStyle style) => DoWindow(id, clientRect, func, GUIContent.Temp(image), style, skin, true, false, null);
        public static Rect ModalWindow(int id, Rect clientRect, WindowFunction func, GUIContent content, GUIStyle style) => DoWindow(id, clientRect, func, content, style, skin, true, false, null);

        /// <summary>Registers a window for this OnGUI call; it runs at the end of the call (or at
        /// the start, for input). Returns where the window is, moved if it was dragged.</summary>
        internal static Rect DoWindow(int id, Rect clientRect, WindowFunction func, GUIContent title, GUIStyle style, GUISkin skin, bool modal, bool layout, GUILayoutOption[] options)
        {
            GUIUtility.CheckOnGUI();
            if (func == null)
            {
                Debug.LogError("GUI.Window: the window function is null");
                return clientRect;
            }
            var set = s_CurrentWindows;
            if (set == null) return clientRect;
            if (!set.ById.TryGetValue(id, out var w))
            {
                w = new GuiWindow { Id = id };
                set.ById[id] = w;
                set.Order.Add(id);
                if (set.Focused < 0) set.Focused = id;
            }
            if (w.Moved.HasValue)
            {
                clientRect.position = w.Moved.Value.position;
                if (!layout) clientRect.size = w.Moved.Value.size;
                w.Moved = null;
            }
            if (layout && w.Layout && w.UsedFrame >= Time.frameCount - 1)
            {
                // A layout window keeps the size its layout gave it.
                clientRect.size = w.Rect.size;
            }
            w.Rect = clientRect;
            w.Func = func;
            w.Title = new GUIContent(title);
            w.Style = style;
            w.Skin = skin;
            w.Modal = modal;
            w.Layout = layout;
            w.Options = options;
            w.UsedFrame = Time.frameCount;
            w.Color = color;
            w.BackgroundColor = backgroundColor;
            w.ContentColor = contentColor;
            w.Enabled = enabled;
            w.Matrix = matrix;
            if (modal) set.Focused = id;
            return clientRect;
        }

        /// <summary>Input reaches windows before the controls under them: front to back.</summary>
        internal static void BeginWindows(int owner)
        {
            s_CurrentOwner = owner;
            if (!s_Windows.TryGetValue(owner, out var set)) s_Windows[owner] = set = new WindowSet();
            s_CurrentWindows = set;
            var e = Event.current;
            if (e == null || e.type == EventType.Layout || e.type == EventType.Repaint || e.type == EventType.Used) return;
            var order = FrontToBack(set);
            bool modal = false;
            foreach (var w in order)
                if (w.Modal) modal = true;
            foreach (var w in order)
            {
                if (e.type == EventType.Used) break;
                if (modal && !w.Modal) continue;
                RunWindow(w, e);
                if (e.type == EventType.MouseDown && w.Rect.Contains(e.mousePosition))
                {
                    // A click on a window brings it forward and stays with it.
                    FocusWindow(set, w.Id);
                    e.Use();
                }
            }
            // Under a modal window nothing else gets the mouse.
            if (modal && e.isMouse && e.type != EventType.Used) e.Use();
        }

        /// <summary>Layout and Repaint run the windows after the rest of the call: back to front.</summary>
        internal static void EndWindows(int owner)
        {
            var set = s_CurrentWindows;
            var e = Event.current;
            if (set == null || e == null) return;
            if (e.type == EventType.Layout || e.type == EventType.Repaint)
            {
                var order = FrontToBack(set);
                order.Reverse();
                foreach (var w in order) RunWindow(w, e);
            }
            // Windows not registered this frame or the last are gone.
            foreach (var id in new List<int>(set.Order))
            {
                if (set.ById[id].UsedFrame >= Time.frameCount - 1) continue;
                set.ById.Remove(id);
                set.Order.Remove(id);
                if (set.Focused == id) set.Focused = set.Order.Count > 0 ? set.Order[set.Order.Count - 1] : -1;
            }
            s_CurrentWindows = null;
        }

        static List<GuiWindow> FrontToBack(WindowSet set)
        {
            var list = new List<GuiWindow>();
            for (int i = set.Order.Count - 1; i >= 0; i--)
            {
                var w = set.ById[set.Order[i]];
                if (w.UsedFrame >= Time.frameCount - 1) list.Add(w);
            }
            return list;
        }

        static void FocusWindow(WindowSet set, int id)
        {
            if (!set.ById.ContainsKey(id)) return;
            set.Order.Remove(id);
            set.Order.Add(id);
            set.Focused = id;
        }

        /// <summary>Runs one window's function for an event, inside its rect.</summary>
        static void RunWindow(GuiWindow w, Event e)
        {
            var savedSkin = s_Skin;
            var savedColor = s_Color;
            var savedBackground = s_BackgroundColor;
            var savedContent = s_ContentColor;
            var savedEnabled = s_Enabled;
            var savedMatrix = matrix;
            var savedRunning = s_RunningWindow;
            int clipDepth = GUIClip.Depth;
            s_RunningWindow = w;
            try
            {
                DoSetSkin(w.Skin);
                s_Color = w.Color;
                s_BackgroundColor = w.BackgroundColor;
                s_ContentColor = w.ContentColor;
                s_Enabled = w.Enabled;
                matrix = w.Matrix;
                GUIUtility.WithScope(s_CurrentOwner * 31 + w.Id + 1, () =>
                {
                    if (w.Layout || e.type == EventType.Layout || GUILayoutUtility.HasWindow(w.Id))
                        GUILayoutUtility.BeginWindow(w.Id, w.Style, w.Options, w.Rect, w.Layout);
                    if (e.type == EventType.Repaint)
                    {
                        var set = s_CurrentWindows;
                        bool focused = set != null && set.Focused == w.Id;
                        w.Style.Draw(w.Rect, w.Title, w.Rect.Contains(e.mousePosition), false, focused, false);
                    }
                    GUIClip.Push(w.Rect, Vector2.zero, Vector2.zero, false);
                    try
                    {
                        w.Func(w.Id);
                    }
                    finally
                    {
                        while (GUIClip.Depth > clipDepth) GUIClip.Pop();
                        GUILayoutUtility.EndWindow(w, e);
                    }
                });
            }
            finally
            {
                s_RunningWindow = savedRunning;
                DoSetSkin(savedSkin);
                s_Color = savedColor;
                s_BackgroundColor = savedBackground;
                s_ContentColor = savedContent;
                s_Enabled = savedEnabled;
                matrix = savedMatrix;
            }
        }

        public static void DragWindow() => DragWindow(new Rect(0, 0, 10000, 10000));

        /// <summary>Lets the mouse drag the running window by the given part of it.</summary>
        public static void DragWindow(Rect position)
        {
            GUIUtility.CheckOnGUI();
            var w = s_RunningWindow;
            if (w == null)
            {
                Debug.LogError("Dragwindow can only be called within a window callback");
                return;
            }
            int id = GUIUtility.GetControlID(s_DragWindowHint, FocusType.Passive);
            var e = Current;
            var state = (SliderDragState)GUIUtility.GetStateObject(typeof(SliderDragState), id);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (position.Contains(e.mousePosition))
                    {
                        GrabMouseControl(id);
                        if (s_CurrentWindows != null) FocusWindow(s_CurrentWindows, w.Id);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (HasMouseControl(id))
                    {
                        var moved = w.Moved ?? w.Rect;
                        moved.position += e.delta;
                        w.Moved = moved;
                        w.Rect = moved;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (HasMouseControl(id))
                    {
                        ReleaseMouseControl();
                        e.Use();
                    }
                    break;
            }
        }

        public static void BringWindowToFront(int windowID)
        {
            if (s_CurrentWindows != null && s_CurrentWindows.ById.ContainsKey(windowID))
            {
                s_CurrentWindows.Order.Remove(windowID);
                s_CurrentWindows.Order.Add(windowID);
            }
        }

        public static void BringWindowToBack(int windowID)
        {
            if (s_CurrentWindows != null && s_CurrentWindows.ById.ContainsKey(windowID))
            {
                s_CurrentWindows.Order.Remove(windowID);
                s_CurrentWindows.Order.Insert(0, windowID);
            }
        }

        public static void FocusWindow(int windowID)
        {
            if (s_CurrentWindows != null) FocusWindow(s_CurrentWindows, windowID);
        }

        public static void UnfocusWindow()
        {
            if (s_CurrentWindows != null) s_CurrentWindows.Focused = -1;
        }

        // ------------------------------------------------------------------ scopes

        public abstract partial class Scope : IDisposable
        {
            bool m_Disposed;

            protected abstract void CloseScope();

            ~Scope()
            {
                if (!m_Disposed) Debug.LogError("Scope was not disposed! You should use the 'using' keyword or manually call Dispose.");
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                GC.SuppressFinalize(this);
                if (!GUIUtility.guiIsExiting) CloseScope();
            }
        }

        public partial class GroupScope : Scope
        {
            public GroupScope(Rect position) => BeginGroup(position);
            public GroupScope(Rect position, string text) => BeginGroup(position, text);
            public GroupScope(Rect position, Texture image) => BeginGroup(position, image);
            public GroupScope(Rect position, GUIContent content) => BeginGroup(position, content);
            public GroupScope(Rect position, GUIStyle style) => BeginGroup(position, style);
            public GroupScope(Rect position, string text, GUIStyle style) => BeginGroup(position, text, style);
            public GroupScope(Rect position, Texture image, GUIStyle style) => BeginGroup(position, image, style);
            protected override void CloseScope() => EndGroup();
        }

        public partial class ScrollViewScope : Scope
        {
            public Vector2 scrollPosition { get; private set; }
            public bool handleScrollWheel { get; set; } = true;

            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect) =>
                this.scrollPosition = BeginScrollView(position, scrollPosition, viewRect);

            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical) =>
                this.scrollPosition = BeginScrollView(position, scrollPosition, viewRect, alwaysShowHorizontal, alwaysShowVertical);

            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar) =>
                this.scrollPosition = BeginScrollView(position, scrollPosition, viewRect, horizontalScrollbar, verticalScrollbar);

            public ScrollViewScope(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar) =>
                this.scrollPosition = BeginScrollView(position, scrollPosition, viewRect, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar);

            protected override void CloseScope() => EndScrollView(handleScrollWheel);
        }

        public partial class ClipScope : Scope
        {
            public ClipScope(Rect position) => BeginClip(position);
            internal ClipScope(Rect position, Vector2 scrollOffset) => BeginClip(position, scrollOffset, Vector2.zero, false);
            protected override void CloseScope() => EndClip();
        }
    }
}
