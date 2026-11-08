using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Records the skin GUI.skin answers in Play mode (inside OnGUI): its name and font, its settings,
/// every built-in and custom style's properties and every background texture's size, sampler and
/// pixels (copied through a RenderTexture: the skin's textures need not be readable).
/// Writes values.jsonl to $IMGUI_SKIN_OUT.
/// </summary>
public sealed class ImguiSkinProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly Dictionary<Texture, string> m_Textures = new Dictionary<Texture, string>();
    string m_Dir;
    GUISkin m_Skin;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable("IMGUI_SKIN_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        new GameObject("IMGUI skin probe").AddComponent<ImguiSkinProbe>().m_Dir = dir;
    }

    void OnGUI()
    {
        if (m_Skin != null) return;
        m_Skin = GUI.skin;
    }

    void Update()
    {
        if (m_Done || (m_Skin == null && Time.frameCount < 600)) return;
        m_Done = true;
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
