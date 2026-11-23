using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Records the skin GUI.skin answers in Play mode (inside OnGUI): its name and font, its settings,
/// every built-in and custom style's properties and every background texture's size, sampler and
/// pixels (copied through a RenderTexture: the skin's textures need not be readable).
/// Writes values.jsonl to $IMGUI_SKIN_OUT.
/// </summary>
public sealed partial class ImguiSkinProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly Dictionary<Texture, string> m_Textures = new Dictionary<Texture, string>();
    string m_Dir;
    GUISkin m_Skin;
    bool m_Done;
    bool m_RecordedLayout;
    bool m_RecordedRepaint;
    bool m_RecordedPass2Layout;
    bool m_RecordedPass2Repaint;
    ImguiTextPaint m_TextPaint;
    ReorderableList m_ListA;
    ReorderableList m_ListB;
    int m_ListAId;
    int m_ListBId;
    int m_LastTemporaryId;
    [SerializeField] int m_First = 11;
    [SerializeField] int m_Second = 22;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable("IMGUI_SKIN_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        var probe = new GameObject("IMGUI skin probe").AddComponent<ImguiSkinProbe>();
        probe.m_Dir = dir;
        probe.m_ListA = new ReorderableList(Array.Empty<int>(), typeof(int));
        probe.m_ListB = new ReorderableList(Array.Empty<int>(), typeof(int));
        probe.m_TextPaint = ImguiTextPaint.Create(dir);
    }

    void OnGUI()
    {
        if (m_Skin == null) m_Skin = GUI.skin;
        if (Event.current.type == EventType.Layout && !m_RecordedLayout)
        {
            m_RecordedLayout = true;
            RecordPropertyHooks("Layout");
            RecordListFocus("Layout");
            RecordPropertyScopes("Layout");
            RecordEditorLayoutGroups("Layout");
            RecordLayoutAndCalc("Layout");
            RecordFocusIdPass1("Layout");
            RecordPopups("Layout");
        }
        else if (Event.current.type == EventType.Repaint && m_RecordedLayout && !m_RecordedRepaint)
        {
            m_RecordedRepaint = true;
            RecordPropertyHooks("Repaint");
            RecordListFocus("Repaint");
            RecordPropertyScopes("Repaint");
            RecordEditorLayoutGroups("Repaint");
            RecordLayoutAndCalc("Repaint");
            RecordFocusIdPass1("Repaint");
            RecordPopups("Repaint");
        }
        else if (Event.current.type == EventType.Layout && m_RecordedRepaint && !m_RecordedPass2Layout)
        {
            m_RecordedPass2Layout = true;
            RecordFocusIdPass2("Layout");
        }
        else if (Event.current.type == EventType.Repaint && m_RecordedPass2Layout && !m_RecordedPass2Repaint)
        {
            m_RecordedPass2Repaint = true;
            RecordFocusIdPass2("Repaint");
        }
    }

    void Update()
    {
        if (m_Done || ((!m_RecordedLayout || !m_RecordedRepaint || !m_RecordedPass2Layout || !m_RecordedPass2Repaint || !m_TextPaint.Done) && Time.frameCount < 600)) return;
        m_Done = true;
        RecordListFocus("Update");
        Put("onGUI", Json(m_Skin != null ? "called" : "not called"));
        if (m_Skin != null) Record(m_Skin);
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void RecordPropertyHooks(string phase)
    {
        using var so = new SerializedObject(this);
        var first = so.FindProperty("m_First");
        var second = so.FindProperty("m_Second");
        var hook = typeof(EditorGUIUtility).GetEvent("beginProperty", BindingFlags.NonPublic | BindingFlags.Static);
        var add = hook.GetAddMethod(true);
        var remove = hook.GetRemoveMethod(true);
        var calls = new List<string>();
        string entry = "";
        void Note(string listener, Rect area, SerializedProperty property)
        {
            if (property == null || property.serializedObject.targetObject == this)
                calls.Add("[" + Json(entry) + "," + Json(listener) + "," + Json(property?.propertyPath ?? "") +
                    $",[{F(area.x)},{F(area.y)},{F(area.width)},{F(area.height)}]]");
        }
        Action<Rect, SerializedProperty> a = (area, property) => Note("a", area, property);
        Action<Rect, SerializedProperty> b = (area, property) => Note("b", area, property);
        var outer = new Rect(4, 8, 110, 18);
        var inner = new Rect(12, 28, 100, 18);
        var label = new GUIContent("Probe");
        add.Invoke(null, new object[] { a });
        add.Invoke(null, new object[] { b });
        try
        {
            entry = "outer";
            EditorGUI.BeginProperty(outer, label, first);
            try
            {
                entry = "inner";
                EditorGUI.BeginProperty(inner, label, second);
                EditorGUI.EndProperty();
            }
            finally { EditorGUI.EndProperty(); }

            entry = "field";
            EditorGUI.PropertyField(inner, first, label);
            remove.Invoke(null, new object[] { a });

            entry = "afterRemove";
            EditorGUI.BeginProperty(outer, label, second);
            EditorGUI.EndProperty();
            entry = "null";
            try
            {
                EditorGUI.BeginProperty(outer, label, null);
                EditorGUI.EndProperty();
                calls.Add(Json("null returned"));
            }
            catch (Exception error) { calls.Add(Json(error.GetType().Name + ": " + error.Message)); }
        }
        finally
        {
            remove.Invoke(null, new object[] { a });
            remove.Invoke(null, new object[] { b });
        }
        Put("propertyHook/" + phase, "[" + string.Join(",", calls) + "]");
    }

    void RecordListFocus(string phase)
    {
        var observations = new List<string>();
        void Flag(bool value) => observations.Add(value ? "true" : "false");
        void CheckFocus(ReorderableList list)
        {
            try { Flag(list.HasKeyboardControl()); }
            catch (Exception error) { observations.Add(Json(error.GetType().Name + ": " + error.Message)); }
        }

        int previous = GUIUtility.keyboardControl;
        try
        {
            GUIUtility.keyboardControl = 0;
            CheckFocus(m_ListA);

            m_ListA.GrabKeyboardFocus();
            int idA = GUIUtility.keyboardControl;
            Flag(idA != 0);
            Flag(m_ListAId == 0 || idA == m_ListAId);
            m_ListAId = idA;
            CheckFocus(m_ListA);

            m_ListB.ReleaseKeyboardFocus();
            Flag(GUIUtility.keyboardControl == idA);

            m_ListB.GrabKeyboardFocus();
            int idB = GUIUtility.keyboardControl;
            Flag(idB != 0 && idB != idA);
            Flag(m_ListBId == 0 || idB == m_ListBId);
            m_ListBId = idB;
            CheckFocus(m_ListA);
            CheckFocus(m_ListB);

            var created = new ReorderableList(Array.Empty<int>(), typeof(int));
            created.GrabKeyboardFocus();
            int temporaryId = GUIUtility.keyboardControl;
            Flag(temporaryId != 0 && temporaryId != idA && temporaryId != idB && temporaryId != m_LastTemporaryId);
            m_LastTemporaryId = temporaryId;
            if (Event.current != null)
            {
                int controlId = GUIUtility.GetControlID(FocusType.Keyboard);
                Flag(controlId != idA && controlId != idB && controlId != temporaryId);
            }

            created.ReleaseKeyboardFocus();
            Flag(GUIUtility.keyboardControl == 0);
            CheckFocus(created);
        }
        finally { GUIUtility.keyboardControl = previous; }
        Put("listFocus/" + phase, "[" + string.Join(",", observations) + "]");
    }

    void Put(string key, string json) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(json).Append("}\n");

    static string F(float v) => v.ToString("R", Inv);
    static string Col(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string Offset(RectOffset o) => o == null ? "null" : $"[{o.left},{o.right},{o.top},{o.bottom}]";

    void Record(GUISkin skin)
    {
        Put("skin/name", Json(skin.name));
        Put("skin/hideFlags", Json(skin.hideFlags.ToString()));
        Put("skin/font", Json(skin.font ? skin.font.name : "null"));
        var s = skin.settings;
        Put("skin/settings", $"[{(s.doubleClickSelectsWord ? "true" : "false")},{(s.tripleClickSelectsLine ? "true" : "false")},{Col(s.cursorColor)},{F(s.cursorFlashSpeed)},{Col(s.selectionColor)}]");
        var builtins = new (string, GUIStyle)[]
        {
            ("box", skin.box), ("button", skin.button), ("toggle", skin.toggle), ("label", skin.label),
            ("textField", skin.textField), ("textArea", skin.textArea), ("window", skin.window),
            ("horizontalSlider", skin.horizontalSlider), ("horizontalSliderThumb", skin.horizontalSliderThumb),
            ("verticalSlider", skin.verticalSlider), ("verticalSliderThumb", skin.verticalSliderThumb),
            ("horizontalScrollbar", skin.horizontalScrollbar), ("horizontalScrollbarThumb", skin.horizontalScrollbarThumb),
            ("horizontalScrollbarLeftButton", skin.horizontalScrollbarLeftButton), ("horizontalScrollbarRightButton", skin.horizontalScrollbarRightButton),
            ("verticalScrollbar", skin.verticalScrollbar), ("verticalScrollbarThumb", skin.verticalScrollbarThumb),
            ("verticalScrollbarUpButton", skin.verticalScrollbarUpButton), ("verticalScrollbarDownButton", skin.verticalScrollbarDownButton),
            ("scrollView", skin.scrollView),
        };
        foreach (var (name, style) in builtins) Style("style/" + name + "/", style);
        var custom = skin.customStyles ?? Array.Empty<GUIStyle>();
        Put("skin/customStyles", "[" + string.Join(",", custom.Select(c => Json(c == null ? "null" : c.name))) + "]");
        for (int i = 0; i < custom.Length; i++)
            if (custom[i] != null) Style("custom/" + i + "/", custom[i]);
        // Every enumerated style (built-in names and custom styles) by name, as FindStyle sees them.
        var names = new List<string>();
        foreach (GUIStyle style in skin) names.Add(style.name);
        names.Sort(StringComparer.Ordinal);
        Put("skin/enumerated", "[" + string.Join(",", names.Select(Json)) + "]");
        Put("skin/findStyle", "[" + string.Join(",", new[] { "button", "BUTTON", "textfield", "horizontalsliderthumbextent", "verticalsliderthumbextent", "SliderMixed", "nothing" }
            .Select(n => Json(n + "=" + (skin.FindStyle(n)?.name ?? "null")))) + "]");
        foreach (var pair in m_Textures.OrderBy(p => p.Value, StringComparer.Ordinal))
            Texture(pair.Value, pair.Key);
    }

    void Style(string key, GUIStyle style)
    {
        Put(key + "name", Json(style.name));
        var states = new (string, GUIStyleState)[]
        {
            ("normal", style.normal), ("hover", style.hover), ("active", style.active), ("focused", style.focused),
            ("onNormal", style.onNormal), ("onHover", style.onHover), ("onActive", style.onActive), ("onFocused", style.onFocused),
        };
        foreach (var (name, state) in states)
        {
            var background = state.background;
            string id = "null";
            if (background != null)
            {
                if (!m_Textures.TryGetValue(background, out id))
                {
                    id = background.name;
                    while (m_Textures.ContainsValue(id)) id += "+";
                    m_Textures[background] = id;
                }
            }
            Put(key + name, "[" + Json(id) + "," + Col(state.textColor) + "," + state.scaledBackgrounds.Length + "]");
        }
        Put(key + "border", Offset(style.border));
        Put(key + "margin", Offset(style.margin));
        Put(key + "padding", Offset(style.padding));
        Put(key + "overflow", Offset(style.overflow));
        Put(key + "font", Json(style.font ? style.font.name : "null"));
        Put(key + "text", $"[{style.fontSize},{Json(style.fontStyle.ToString())},{Json(style.alignment.ToString())},{(style.wordWrap ? "true" : "false")},{(style.richText ? "true" : "false")},{Json(style.clipping.ToString())},{Json(style.imagePosition.ToString())}]");
        Put(key + "layout", $"[{F(style.contentOffset.x)},{F(style.contentOffset.y)},{F(style.fixedWidth)},{F(style.fixedHeight)},{(style.stretchWidth ? "true" : "false")},{(style.stretchHeight ? "true" : "false")}]");
        Put(key + "lineHeight", F(style.lineHeight));
    }

    void Texture(string id, Texture texture)
    {
        string key = "texture/" + id + "/";
        Put(key + "info", $"[{Json(texture.GetType().Name)},{texture.width},{texture.height},{Json(texture.filterMode.ToString())},{Json(texture.wrapModeU.ToString())},{Json(texture.wrapModeV.ToString())},{texture.anisoLevel},{F(texture.mipMapBias)},{texture.mipmapCount}]");
        if (texture is Texture2D t2) Put(key + "format", Json(t2.format.ToString()));
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Point;
        var filter = texture.filterMode;
        texture.filterMode = FilterMode.Point;
        Graphics.Blit(texture, rt);
        texture.filterMode = filter;
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = copy.GetPixels32();
        Destroy(copy);
        // Rows bottom-up, RGBA bytes as hex: small enough for the skin's textures.
        var hex = new StringBuilder(pixels.Length * 8);
        foreach (var p in pixels) hex.Append(p.r.ToString("x2")).Append(p.g.ToString("x2")).Append(p.b.ToString("x2")).Append(p.a.ToString("x2"));
        Put(key + "pixels", Json(hex.ToString()));
    }

    static string Json(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 0x20 || c > 0x7e) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
