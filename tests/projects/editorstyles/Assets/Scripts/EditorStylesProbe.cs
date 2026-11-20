using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class EditorStylesProbe : MonoBehaviour
{
    readonly StringBuilder m_Values = new StringBuilder();
    GUIStyle m_LayoutStyle;
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

    void OnGUI()
    {
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
            Put("usesSkinFont", Flag(style.font == null));
            Put("lineHeight", Number(style.lineHeight));
            var explicitFont = new GUIStyle(style) { font = GUI.skin.font };
            Put("explicitSkinFontLineHeight", Number(explicitFont.lineHeight));
            Put("fontSize", style.fontSize.ToString(CultureInfo.InvariantCulture));
            Put("fontStyle", Quote(style.fontStyle.ToString()));
            Put("alignment", Quote(style.alignment.ToString()));
            Put("clipping", Quote(style.clipping.ToString()));
            Put("imagePosition", Quote(style.imagePosition.ToString()));
            Put("contentOffset", $"[{Number(style.contentOffset.x)},{Number(style.contentOffset.y)}]");
            Put("border", Offset(style.border));
            Put("margin", Offset(style.margin));
            Put("padding", Offset(style.padding));
            Put("overflow", Offset(style.overflow));
            Put("fixedSize", $"[{Number(style.fixedWidth)},{Number(style.fixedHeight)}]");
            Put("stretch", $"[{Flag(style.stretchWidth)},{Flag(style.stretchHeight)}]");
            Put("textFlags", $"[{Flag(style.wordWrap)},{Flag(style.richText)}]");
            State("normal", style.normal);
            State("hover", style.hover);
            State("active", style.active);
            State("focused", style.focused);
            State("onNormal", style.onNormal);
            State("onHover", style.onHover);
            State("onActive", style.onActive);
            State("onFocused", style.onFocused);

            // Callers can edit the shared style. A copy still has independent state.
            Color original = style.normal.textColor;
            try
            {
                style.normal.textColor = Color.magenta;
                Put("sharedMutation", Flag(EditorStyles.boldLabel.normal.textColor == Color.magenta));
                Put("copyUnchanged", Flag(explicitFont.normal.textColor == original));
                EditorGUI.BeginDisabledGroup(true);
                try { Put("sharedWhenDisabled", Flag(ReferenceEquals(EditorStyles.boldLabel, style))); }
                finally { EditorGUI.EndDisabledGroup(); }
            }
            finally { style.normal.textColor = original; }

            GUI.Label(new Rect(10, 10, 150, 20), "Inspector bold", style);
        }
        catch (Exception error) { Put("Repaint/exception", Quote(error.GetType().Name)); }

        Put("complete", "true");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("EDITOR_STYLES_OUT"), "values.jsonl"), m_Values.ToString());
        EditorApplication.Exit(0);
    }

    void State(string name, GUIStyleState state)
    {
        Color c = state.textColor;
        Put("state/" + name, $"[{Flag(state.background != null)},{Number(c.r)},{Number(c.g)},{Number(c.b)},{Number(c.a)}]");
    }

    void Put(string key, string value) =>
        m_Values.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    static string Flag(bool value) => value ? "true" : "false";
    static string Quote(string value) => "\"" + value + "\"";
    static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    static string Offset(RectOffset value) => $"[{value.left},{value.right},{value.top},{value.bottom}]";
}
