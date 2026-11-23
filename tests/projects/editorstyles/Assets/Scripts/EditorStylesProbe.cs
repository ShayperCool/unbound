using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed partial class EditorStylesProbe : MonoBehaviour
{
    readonly StringBuilder m_Values = new StringBuilder();
    GUIStyle m_LayoutStyle;
    GUIStyle m_ToggleLayoutStyle;
    GUIStyle m_LabelLayoutStyle;
    bool m_SawLayout;
    bool m_SawRepaint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string output = Environment.GetEnvironmentVariable("EDITOR_STYLES_OUT");
        if (output == null) return;
        if (Environment.GetEnvironmentVariable("EDITOR_STYLES_SET_ENGINE_THEME") == "1")
            SetEngineTheme(output);
        new GameObject("EditorStylesProbe").AddComponent<EditorStylesProbe>();
    }

    static void SetEngineTheme(string output)
    {
        string oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(output, "prefs"));
            EditorPrefs.SetInt("UserSkin", Environment.GetEnvironmentVariable("EDITOR_STYLES_THEME") == "dark" ? 1 : 0);
        }
        finally { Environment.SetEnvironmentVariable("XDG_CACHE_HOME", oldCache); }
    }

    IEnumerator Start()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        while (!m_SawRepaint) yield return null;
        for (int frame = 0; !m_PointerInView && frame < 45; frame++) yield return null;
        yield return new WaitForEndOfFrame();
        RecordTogglePaint();
        RecordMixedTogglePaint();
        Put("complete", "true");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("EDITOR_STYLES_OUT"), "values.jsonl"), m_Values.ToString());
        EditorApplication.Exit(0);
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.Repaint)
            DrawTogglePaint();
        DrawMixedToggleControls();
        if (m_SawRepaint) return;
        if (Event.current.type == EventType.Layout && !m_SawLayout)
        {
            m_SawLayout = true;
            try
            {
                m_LayoutStyle = EditorStyles.boldLabel;
                Put("Layout/style", Quote(m_LayoutStyle.name));
            }
            catch (Exception error) { Put("Layout/exception", Quote(error.GetType().Name)); }
            try
            {
                m_ToggleLayoutStyle = EditorStyles.toggle;
                Put("toggle/Layout/style", Quote(m_ToggleLayoutStyle.name));
            }
            catch (Exception error) { Put("toggle/Layout/exception", Quote(error.GetType().Name)); }
            try
            {
                m_LabelLayoutStyle = EditorStyles.label;
                Put("label/Layout/style", Quote(m_LabelLayoutStyle.name));
            }
            catch (Exception error) { Put("label/Layout/exception", Quote(error.GetType().Name)); }
        }
        if (Event.current.type != EventType.Repaint || !m_SawLayout) return;
        m_SawRepaint = true;
        Put("dark", Flag(EditorGUIUtility.isProSkin));
        try
        {
            GUIStyle style = EditorStyles.boldLabel;
            Put("Repaint/style", Quote(style.name));
            Put("sharedWithLayout", Flag(ReferenceEquals(m_LayoutStyle, style)));
            Put("distinctFromGameSkinLabel", Flag(!ReferenceEquals(GUI.skin.label, style)));
            var copy = DescribeStyle("", style, textureInfo: false);

            RecordSharedStyle("", style, copy, () => EditorStyles.boldLabel);

            GUI.Label(new Rect(290, 142, 150, 20), "Inspector bold", style);
        }
        catch (Exception error) { Put("Repaint/exception", Quote(error.GetType().Name)); }

        RecordToggle();
        RecordPopup();
        RecordLabel();
    }

    void RecordLabel()
    {
        try
        {
            GUIStyle style = EditorStyles.label;
            Put("label/Repaint/style", Quote(style.name));
            Put("label/sharedWithLayout", Flag(ReferenceEquals(m_LabelLayoutStyle, style)));
            Put("label/distinctFromGameSkinLabel", Flag(!ReferenceEquals(GUI.skin.label, style)));
            Put("label/distinctFromBoldLabel", Flag(!ReferenceEquals(EditorStyles.boldLabel, style)));
            var copy = DescribeStyle("label/", style, textureInfo: true);
            RecordSharedStyle("label/", style, copy, () => EditorStyles.label);
        }
        catch (Exception error) { Put("label/Repaint/exception", Quote(error.GetType().Name)); }
    }

    void RecordSharedStyle(string prefix, GUIStyle style, GUIStyle copy, Func<GUIStyle> getStyle)
    {
        // Mutating the shared style also distinguishes it from a fresh copy.
        Color original = style.normal.textColor;
        try
        {
            style.normal.textColor = Color.magenta;
            Put(prefix + "sharedMutation", Flag(getStyle().normal.textColor == Color.magenta));
            Put(prefix + "copyUnchanged", Flag(copy.normal.textColor == original));
            EditorGUI.BeginDisabledGroup(true);
            try { Put(prefix + "sharedWhenDisabled", Flag(ReferenceEquals(getStyle(), style))); }
            finally { EditorGUI.EndDisabledGroup(); }
        }
        finally { style.normal.textColor = original; }
    }

    void RecordToggle()
    {
        try
        {
            var style = EditorStyles.toggle;
            Put("toggle/Repaint/style", Quote(style.name));
            Put("toggle/sharedWithLayout", Flag(ReferenceEquals(m_ToggleLayoutStyle, style)));
            Put("toggle/distinctFromGameSkinToggle", Flag(!ReferenceEquals(GUI.skin.toggle, style)));
            DescribeStyle("toggle/", style, textureInfo: true);
            var off = new Rect(290, 142, 150, 16);
            var on = new Rect(290, 158, 150, 16);
            bool offValue = GUI.Toggle(off, false, "Inspector unchecked", style);
            bool onValue = GUI.Toggle(on, true, "Inspector checked", style);
            Put("toggle/valueOnRepaint", $"[{Flag(offValue)},{Flag(onValue)}]");
        }
        catch (Exception error) { Put("toggle/Repaint/exception", Quote(error.GetType().Name)); }
    }

    void RecordPopup()
    {
        try
        {
            var style = EditorStyles.popup;
            Put("popup/Repaint/style", Quote(style.name));
            Put("popup/shared", Flag(ReferenceEquals(EditorStyles.popup, style)));
            DescribeStyle("popup/", style, textureInfo: true);
        }
        catch (Exception error) { Put("popup/Repaint/exception", Quote(error.GetType().Name)); }
    }

    GUIStyle DescribeStyle(string prefix, GUIStyle style, bool textureInfo)
    {
        Put(prefix + "usesSkinFont", Flag(style.font == null));
        Put(prefix + "lineHeight", Number(style.lineHeight));
        var copy = new GUIStyle(style) { font = GUI.skin.font };
        Put(prefix + "explicitSkinFontLineHeight", Number(copy.lineHeight));
        Put(prefix + "fontSize", style.fontSize.ToString(CultureInfo.InvariantCulture));
        Put(prefix + "fontStyle", Quote(style.fontStyle.ToString()));
        Put(prefix + "alignment", Quote(style.alignment.ToString()));
        Put(prefix + "clipping", Quote(style.clipping.ToString()));
        Put(prefix + "imagePosition", Quote(style.imagePosition.ToString()));
        Put(prefix + "contentOffset", $"[{Number(style.contentOffset.x)},{Number(style.contentOffset.y)}]");
        Put(prefix + "border", Offset(style.border));
        Put(prefix + "margin", Offset(style.margin));
        Put(prefix + "padding", Offset(style.padding));
        Put(prefix + "overflow", Offset(style.overflow));
        Put(prefix + "fixedSize", $"[{Number(style.fixedWidth)},{Number(style.fixedHeight)}]");
        Put(prefix + "stretch", $"[{Flag(style.stretchWidth)},{Flag(style.stretchHeight)}]");
        Put(prefix + "textFlags", $"[{Flag(style.wordWrap)},{Flag(style.richText)}]");
        State(prefix + "state/normal", style.normal, textureInfo);
        State(prefix + "state/hover", style.hover, textureInfo);
        State(prefix + "state/active", style.active, textureInfo);
        State(prefix + "state/focused", style.focused, textureInfo);
        State(prefix + "state/onNormal", style.onNormal, textureInfo);
        State(prefix + "state/onHover", style.onHover, textureInfo);
        State(prefix + "state/onActive", style.onActive, textureInfo);
        State(prefix + "state/onFocused", style.onFocused, textureInfo);
        return copy;
    }

    void State(string key, GUIStyleState state, bool textureInfo)
    {
        Color c = state.textColor;
        Texture2D image = state.background;
        Put(key, $"[{Flag(image != null)},{Number(c.r)},{Number(c.g)},{Number(c.b)},{Number(c.a)}]");
        if (textureInfo)
        {
            Put(key + "/background", image != null ? $"[{Quote(image.name)},{image.width},{image.height}]" : "null");
            Texture2D[] scaled = state.scaledBackgrounds;
            Put(key + "/scaledBackground", scaled == null || scaled.Length == 0
                ? "null" : $"[{scaled.Length},{Quote(scaled[0].name)},{scaled[0].width},{scaled[0].height}]");
        }
    }

    void Put(string key, string value) =>
        m_Values.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    static string Flag(bool value) => value ? "true" : "false";
    static string Quote(string value) => "\"" + value + "\"";
    static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    static string Offset(RectOffset value) => $"[{value.left},{value.right},{value.top},{value.bottom}]";
}
