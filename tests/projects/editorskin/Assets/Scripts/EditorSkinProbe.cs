using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

/// <summary>Records the Editor's built-in GUI skins in Play mode: each skin's name, font and
/// settings, every built-in and custom style's layout and text fields, state colours and
/// background names, line height and the size of a short text; which of them GUI.skin is in a
/// game OnGUI; and the fonts of the Editor's resources with the font assets their text is made with.</summary>
public sealed class EditorSkinProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable("EDITOR_SKIN_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        new GameObject("Editor skin probe").AddComponent<EditorSkinProbe>().m_Dir = dir;
    }

    void OnGUI()
    {
        if (m_Done || Event.current.type != EventType.Repaint) return;
        m_Done = true;
        Put("gui/skin", Json(GUI.skin != null ? GUI.skin.name : "null"));
        Put("gui/isProSkin", EditorGUIUtility.isProSkin ? "true" : "false");
        foreach (EditorSkin which in new[] { EditorSkin.Game, EditorSkin.Inspector, EditorSkin.Scene })
        {
            GUISkin skin = null;
            string error = null;
            try { skin = EditorGUIUtility.GetBuiltinSkin(which); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            string prefix = "skin/" + which + "/";
            if (skin == null)
            {
                Put(prefix + "name", Json(error ?? "null"));
                continue;
            }
            Put(prefix + "name", Json(skin.name));
            Put(prefix + "sameAsGuiSkin", ReferenceEquals(skin, GUI.skin) ? "true" : "false");
            Put(prefix + "font", Json(skin.font ? skin.font.name + " " + skin.font.fontSize.ToString(Inv) : "null"));
            var s = skin.settings;
            Put(prefix + "settings", $"[{(s.doubleClickSelectsWord ? "true" : "false")},{(s.tripleClickSelectsLine ? "true" : "false")},{Col(s.cursorColor)},{F(s.cursorFlashSpeed)},{Col(s.selectionColor)}]");
            if (which == EditorSkin.Game) continue;
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
            foreach (var (name, style) in builtins) Style(prefix + "builtin/" + name, style);
            var custom = skin.customStyles ?? Array.Empty<GUIStyle>();
            Put(prefix + "customCount", custom.Length.ToString(Inv));
            var seen = new HashSet<string>();
            for (int i = 0; i < custom.Length; i++)
            {
                if (custom[i] == null) continue;
                // A name used twice keeps its place in the list apart.
                string key = seen.Add(custom[i].name) ? custom[i].name : custom[i].name + "#" + i.ToString(Inv);
                Style(prefix + "custom/" + key, custom[i]);
            }
        }
        Fonts();
        OsFontAssets();
        LoadOrder();
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.Exit(0);
    }

    static readonly GUIContent s_Sample = new GUIContent("Sample Text");

    void Style(string key, GUIStyle style)
    {
        var states = new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused };
        string State(GUIStyleState state) =>
            "[" + Json(state.background != null ? state.background.name : "") + "," + Col(state.textColor) + "," + state.scaledBackgrounds.Length.ToString(Inv) + "]";
        var size = style.CalcSize(s_Sample);
        Put(key, "{" +
            "\"name\":" + Json(style.name) +
            ",\"margin\":" + Offset(style.margin) + ",\"padding\":" + Offset(style.padding) +
            ",\"border\":" + Offset(style.border) + ",\"overflow\":" + Offset(style.overflow) +
            ",\"fixed\":[" + F(style.fixedWidth) + "," + F(style.fixedHeight) + "]" +
            ",\"stretch\":[" + (style.stretchWidth ? "true" : "false") + "," + (style.stretchHeight ? "true" : "false") + "]" +
            ",\"font\":" + Json(style.font ? style.font.name : "") +
            ",\"text\":[" + style.fontSize.ToString(Inv) + "," + Json(style.fontStyle.ToString()) + "," + Json(style.alignment.ToString()) + "," +
                (style.wordWrap ? "true" : "false") + "," + (style.richText ? "true" : "false") + "," + Json(style.clipping.ToString()) + "," + Json(style.imagePosition.ToString()) + "]" +
            ",\"offset\":[" + F(style.contentOffset.x) + "," + F(style.contentOffset.y) + "]" +
            ",\"lineHeight\":" + F(style.lineHeight) +
            ",\"sample\":[" + F(size.x) + "," + F(size.y) + "]" +
            ",\"states\":[" + string.Join(",", states.Select(State)) + "]}");
    }

    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly string[] s_Fonts =
    {
        "Fonts/System/System Normal.ttf", "Fonts/System/System Big.ttf", "Fonts/System/System Small.ttf",
        "Fonts/System/System Warning.ttf", "Fonts/System/System Normal Bold.ttf", "Fonts/System/System Small Bold.ttf",
        "Fonts/Inter/Inter-Regular.ttf", "Fonts/Inter/Inter-SemiBold.ttf", "Fonts/RobotoMono/RobotoMono-Regular.ttf",
        "Fonts/RobotoMono/RobotoMono-Bold.ttf",
    };

    static readonly string[] s_Texts = { "Te", "T", "e", "AV", "A", "V", "Sample Text" };

    /// <summary>The fonts of the Editor's resources that its skins' styles name: the font, the
    /// font asset IMGUI text of it is made with (its face, atlas, which earlier font shares it,
    /// the styles of its weight table), the widths of texts with and without kerning pairs in
    /// plain and bold styles of the font, and the feature records the asset has afterwards.</summary>
    void Fonts()
    {
        var assets = new List<FontAsset>();
        foreach (string path in s_Fonts)
        {
            string prefix = "font/" + Path.GetFileNameWithoutExtension(path) + "/";
            try
            {
                var font = EditorGUIUtility.Load(path) as Font;
                if (font == null)
                {
                    Put(prefix + "font", "null");
                    continue;
                }
                Put(prefix + "font", $"[{Json(font.name)},{font.fontSize},{(font.dynamic ? "true" : "false")},{font.lineHeight},[{string.Join(",", font.fontNames.Select(Json))}]]");
                foreach (var fontStyle in new[] { FontStyle.Normal, FontStyle.Bold })
                {
                    var style = new GUIStyle { font = font, fontStyle = fontStyle };
                    Put(prefix + "lineHeight/" + fontStyle, F(style.lineHeight));
                    Put(prefix + "widths/" + fontStyle, "[" + string.Join(",", s_Texts.Select(t => F(style.CalcSize(new GUIContent(t)).x))) + "]");
                }
                var handleType = typeof(GUIStyle).Assembly.GetType("UnityEngine.IMGUITextHandle");
                var settings = (TextSettings)handleType.GetField("s_EditorTextSettings", Hidden).GetValue(null);
                var asset = (FontAsset)typeof(TextSettings).GetMethod("GetCachedFontAsset", Hidden).Invoke(settings, new object[] { font });
                if (asset == null)
                {
                    Put(prefix + "asset", "null");
                    continue;
                }
                int shared = assets.IndexOf(asset);
                assets.Add(asset);
                Put(prefix + "sharesAssetOfFont", shared.ToString(Inv));
                var face = asset.faceInfo;
                Put(prefix + "face", $"[{Json(face.familyName)},{Json(face.styleName)},{F(face.pointSize)},{F(face.ascentLine)},{F(face.descentLine)},{F(face.lineHeight)}]");
                Put(prefix + "atlas", $"[{Json(asset.atlasRenderMode.ToString())},{asset.atlasPadding},{Json(asset.atlasPopulationMode.ToString())},{(asset.isMultiAtlasTexturesEnabled ? "true" : "false")}]");
                string Typeface(FontAsset typeface) => typeface == null ? "null" : Json(typeface.faceInfo.familyName + " " + typeface.faceInfo.styleName);
                var weights = asset.fontWeightTable;
                Put(prefix + "weights", "[" + string.Join(",", Enumerable.Range(0, weights.Length).Select(i => "[" + Typeface(weights[i].regularTypeface) + "," + Typeface(weights[i].italicTypeface) + "]")) + "]");
                // The Editor draws its own windows with the Inter fonts: the records of their
                // assets count the glyphs of that text too, which is no property of the fonts.
                if (font.name.StartsWith("Inter", StringComparison.Ordinal)) continue;
                var table = typeof(FontAsset).GetProperty("fontFeatureTable", Hidden)?.GetValue(asset);
                string Records(string property) =>
                    table?.GetType().GetProperty(property, Hidden)?.GetValue(table) is System.Collections.ICollection records ? records.Count.ToString(Inv) : "null";
                Put(prefix + "featureRecords", $"[{Records("glyphPairAdjustmentRecords")},{Records("ligatureRecords")},{Records("MarkToBaseAdjustmentRecords")},{Records("MarkToMarkAdjustmentRecords")}]");
            }
            catch (Exception error)
            {
                Put(prefix + "error", Json(error.GetType().Name + ": " + (error.InnerException ?? error).Message));
            }
        }
    }

    static readonly string[] s_OsFamilies = { "Liberation Sans", "FreeSans", "DejaVu Sans", "Liberation Serif", "FreeSerif", "Cantarell" };

    /// <summary>Font assets of OS fonts made by family name: whether the family is found, and
    /// the feature records the asset has once the characters of a text with kerning pairs are
    /// added (which fonts' pair adjustments TextCore takes differs by face).</summary>
    void OsFontAssets()
    {
        foreach (string family in s_OsFamilies)
        {
            string prefix = "osAsset/" + family + "/";
            try
            {
                var asset = FontAsset.CreateFontAsset(family, "Regular", 90);
                if (asset == null)
                {
                    Put(prefix + "face", "null");
                    continue;
                }
                var face = asset.faceInfo;
                Put(prefix + "face", $"[{Json(face.familyName)},{Json(face.styleName)},{Json(asset.atlasPopulationMode.ToString())}]");
                bool added = asset.TryAddCharacters("TeAVWaoy.,Sample xt", out string missing, true);
                typeof(FontAsset).GetMethod("UpdateFontFeaturesForFontAssetsInQueue", Hidden)?.Invoke(null, null);
                var table = typeof(FontAsset).GetProperty("fontFeatureTable", Hidden)?.GetValue(asset);
                string Records(string property) =>
                    table?.GetType().GetProperty(property, Hidden)?.GetValue(table) is System.Collections.ICollection records ? records.Count.ToString(Inv) : "null";
                Put(prefix + "added", $"[{(added ? "true" : "false")},{Json(missing ?? "")}]");
                Put(prefix + "featureRecords", $"[{Records("glyphPairAdjustmentRecords")},{Records("ligatureRecords")},{Records("MarkToBaseAdjustmentRecords")},{Records("MarkToMarkAdjustmentRecords")}]");
            }
            catch (Exception error)
            {
                Put(prefix + "error", Json(error.GetType().Name + ": " + (error.InnerException ?? error).Message));
            }
        }
    }

    /// <summary>EditorGUIUtility.Load looks under "Assets/Editor Default Resources" before the
    /// Editor's own resources: the project has a font at the path of one of the Editor's fonts,
    /// and none at the path of the one beside it.</summary>
    void LoadOrder()
    {
        foreach (var (key, path) in new[]
        {
            ("load/projectBeforeEditor", "Fonts/RobotoMono/RobotoMono-Italic.ttf"),
            ("load/editorOnly", "Fonts/RobotoMono/RobotoMono-BoldItalic.ttf"),
            ("load/neither", "Fonts/RobotoMono/NoSuchFont.ttf"),
        })
        {
            var font = EditorGUIUtility.Load(path) as Font;
            Put(key, font == null ? "null" : $"[{Json(font.name)},[{string.Join(",", font.fontNames.Select(Json))}]]");
        }
    }

    void Put(string key, string json) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(json).Append("}\n");

    static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? Json(v.ToString(Inv)) : v.ToString("R", Inv);
    static string Col(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string Offset(RectOffset o) => o == null ? "null" : $"[{o.left},{o.right},{o.top},{o.bottom}]";

    static string Json(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", Inv));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
