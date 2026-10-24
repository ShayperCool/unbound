using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Cursor = UnityEngine.UIElements.Cursor;
using Object = UnityEngine.Object;

namespace Unbound.UIElements
{
    /// <summary>A value of a style sheet: the sheet and the handle into its tables.</summary>
    internal struct UssValue
    {
        public StyleSheet sheet;
        public StyleValueHandle handle;
    }

    internal static class StyleKeywords
    {
        internal static string ToUss(StyleValueKeyword k) => k switch
        {
            StyleValueKeyword.Inherit => "inherit",
            StyleValueKeyword.Initial => "initial",
            StyleValueKeyword.Auto => "auto",
            StyleValueKeyword.Unset => "unset",
            StyleValueKeyword.True => "true",
            StyleValueKeyword.False => "false",
            StyleValueKeyword.None => "none",
            StyleValueKeyword.Cover => "cover",
            StyleValueKeyword.Contain => "contain",
            _ => "",
        };
    }

    internal static partial class StyleProperties
    {
        internal static StylePropertyId IdOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return StylePropertyId.Unknown;
            if (NameToId.TryGetValue(name, out var id)) return id;
            return name.StartsWith("--", StringComparison.Ordinal) ? StylePropertyId.Custom : StylePropertyId.Unknown;
        }

        static readonly Dictionary<StyleEnumType, Dictionary<string, int>> s_Enums = BuildEnums();

        static Dictionary<StyleEnumType, Dictionary<string, int>> BuildEnums()
        {
            var d = new Dictionary<StyleEnumType, Dictionary<string, int>>();
            void Add(StyleEnumType t, string spec)
            {
                var m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in spec.Split(' '))
                {
                    int eq = pair.IndexOf('=');
                    m[pair.Substring(0, eq)] = int.Parse(pair.Substring(eq + 1), CultureInfo.InvariantCulture);
                }
                d[t] = m;
            }
            // USS identifiers to enum values, as Unity's style property utility maps them.
            Add(StyleEnumType.Align, "auto=0 flex-start=1 center=2 flex-end=3 stretch=4");
            Add(StyleEnumType.Axis, "x=0 y=1 z=2");
            Add(StyleEnumType.BackgroundPositionKeyword, "center=0 top=1 bottom=2 left=3 right=4");
            Add(StyleEnumType.BackgroundSizeType, "cover=1 contain=2");
            Add(StyleEnumType.DisplayStyle, "flex=0 none=1");
            Add(StyleEnumType.EasingMode, "ease=0 ease-in=1 ease-out=2 ease-in-out=3 linear=4 ease-in-sine=5 ease-out-sine=6 ease-in-out-sine=7 ease-in-cubic=8 ease-out-cubic=9 ease-in-out-cubic=10 ease-in-circ=11 ease-out-circ=12 ease-in-out-circ=13 ease-in-elastic=14 ease-out-elastic=15 ease-in-out-elastic=16 ease-in-back=17 ease-out-back=18 ease-in-out-back=19 ease-in-bounce=20 ease-out-bounce=21 ease-in-out-bounce=22");
            Add(StyleEnumType.EditorTextRenderingMode, "sdf=0 bitmap=1");
            Add(StyleEnumType.FlexDirection, "column=0 column-reverse=1 row=2 row-reverse=3");
            Add(StyleEnumType.FontStyle, "normal=0 bold=1 italic=2 bold-and-italic=3");
            Add(StyleEnumType.Justify, "flex-start=0 center=1 flex-end=2 space-between=3 space-around=4 space-evenly=5");
            Add(StyleEnumType.Overflow, "visible=0 hidden=1");
            Add(StyleEnumType.OverflowClipBox, "padding-box=0 content-box=1");
            Add(StyleEnumType.OverflowInternal, "visible=0 hidden=1 scroll=2");
            Add(StyleEnumType.Position, "relative=0 absolute=1");
            Add(StyleEnumType.Repeat, "no-repeat=0 repeat=3 space=1 round=2");
            Add(StyleEnumType.RepeatXY, "repeat-x=0 repeat-y=1");
            Add(StyleEnumType.ScaleMode, "stretch-to-fill=0 scale-and-crop=1 scale-to-fit=2");
            Add(StyleEnumType.SliceType, "sliced=0 tiled=1");
            Add(StyleEnumType.TextAnchor, "upper-left=0 upper-center=1 upper-right=2 middle-left=3 middle-center=4 middle-right=5 lower-left=6 lower-center=7 lower-right=8");
            Add(StyleEnumType.TextGeneratorType, "advanced=1 standard=0");
            Add(StyleEnumType.TextOverflow, "clip=0 ellipsis=1");
            Add(StyleEnumType.TextOverflowPosition, "start=1 middle=2 end=0");
            Add(StyleEnumType.TransformOriginOffset, "left=1 right=2 top=3 bottom=4 center=5");
            Add(StyleEnumType.Visibility, "visible=0 hidden=1");
            Add(StyleEnumType.WhiteSpace, "normal=0 nowrap=1 pre=2 pre-wrap=3");
            Add(StyleEnumType.Wrap, "nowrap=0 wrap=1 wrap-reverse=2");
            return d;
        }

        internal static bool TryGetEnumIntValue(StyleEnumType type, string value, out int intValue)
        {
            intValue = 0;
            return value != null && s_Enums.TryGetValue(type, out var map) && map.TryGetValue(value, out intValue);
        }

        static readonly Dictionary<string, uint> s_NamedColors = new Dictionary<string, uint>
        {
            ["aliceblue"] = 0xF0F8FFu, ["antiquewhite"] = 0xFAEBD7u, ["aqua"] = 0x00FFFFu, ["aquamarine"] = 0x7FFFD4u,
            ["azure"] = 0xF0FFFFu, ["beige"] = 0xF5F5DCu, ["bisque"] = 0xFFE4C4u, ["black"] = 0x000000u,
            ["blanchedalmond"] = 0xFFEBCDu, ["blue"] = 0x0000FFu, ["blueviolet"] = 0x8A2BE2u, ["brown"] = 0xA52A2Au,
            ["burlywood"] = 0xDEB887u, ["cadetblue"] = 0x5F9EA0u, ["chartreuse"] = 0x7FFF00u, ["chocolate"] = 0xD2691Eu,
            ["coral"] = 0xFF7F50u, ["cornflowerblue"] = 0x6495EDu, ["cornsilk"] = 0xFFF8DCu, ["crimson"] = 0xDC143Cu,
            ["cyan"] = 0x00FFFFu, ["darkblue"] = 0x00008Bu, ["darkcyan"] = 0x008B8Bu, ["darkgoldenrod"] = 0xB8860Bu,
            ["darkgray"] = 0xA9A9A9u, ["darkgreen"] = 0x006400u, ["darkgrey"] = 0xA9A9A9u, ["darkkhaki"] = 0xBDB76Bu,
            ["darkmagenta"] = 0x8B008Bu, ["darkolivegreen"] = 0x556B2Fu, ["darkorange"] = 0xFF8C00u, ["darkorchid"] = 0x9932CCu,
            ["darkred"] = 0x8B0000u, ["darksalmon"] = 0xE9967Au, ["darkseagreen"] = 0x8FBC8Fu, ["darkslateblue"] = 0x483D8Bu,
            ["darkslategray"] = 0x2F4F4Fu, ["darkslategrey"] = 0x2F4F4Fu, ["darkturquoise"] = 0x00CED1u, ["darkviolet"] = 0x9400D3u,
            ["deeppink"] = 0xFF1493u, ["deepskyblue"] = 0x00BFFFu, ["dimgray"] = 0x696969u, ["dimgrey"] = 0x696969u,
            ["dodgerblue"] = 0x1E90FFu, ["firebrick"] = 0xB22222u, ["floralwhite"] = 0xFFFAF0u, ["forestgreen"] = 0x228B22u,
            ["fuchsia"] = 0xFF00FFu, ["gainsboro"] = 0xDCDCDCu, ["ghostwhite"] = 0xF8F8FFu, ["goldenrod"] = 0xDAA520u,
            ["gold"] = 0xFFD700u, ["gray"] = 0x808080u, ["green"] = 0x008000u, ["greenyellow"] = 0xADFF2Fu,
            ["grey"] = 0x808080u, ["honeydew"] = 0xF0FFF0u, ["hotpink"] = 0xFF69B4u, ["indianred"] = 0xCD5C5Cu,
            ["indigo"] = 0x4B0082u, ["ivory"] = 0xFFFFF0u, ["khaki"] = 0xF0E68Cu, ["lavenderblush"] = 0xFFF0F5u,
            ["lavender"] = 0xE6E6FAu, ["lawngreen"] = 0x7CFC00u, ["lemonchiffon"] = 0xFFFACDu, ["lightblue"] = 0xADD8E6u,
            ["lightcoral"] = 0xF08080u, ["lightcyan"] = 0xE0FFFFu, ["lightgoldenrodyellow"] = 0xFAFAD2u, ["lightgray"] = 0xD3D3D3u,
            ["lightgreen"] = 0x90EE90u, ["lightgrey"] = 0xD3D3D3u, ["lightpink"] = 0xFFB6C1u, ["lightsalmon"] = 0xFFA07Au,
            ["lightseagreen"] = 0x20B2AAu, ["lightskyblue"] = 0x87CEFAu, ["lightslategray"] = 0x778899u, ["lightslategrey"] = 0x778899u,
            ["lightsteelblue"] = 0xB0C4DEu, ["lightyellow"] = 0xFFFFE0u, ["lime"] = 0x00FF00u, ["limegreen"] = 0x32CD32u,
            ["linen"] = 0xFAF0E6u, ["magenta"] = 0xFF00FFu, ["maroon"] = 0x800000u, ["mediumaquamarine"] = 0x66CDAAu,
            ["mediumblue"] = 0x0000CDu, ["mediumorchid"] = 0xBA55D3u, ["mediumpurple"] = 0x9370DBu, ["mediumseagreen"] = 0x3CB371u,
            ["mediumslateblue"] = 0x7B68EEu, ["mediumspringgreen"] = 0x00FA9Au, ["mediumturquoise"] = 0x48D1CCu, ["mediumvioletred"] = 0xC71585u,
            ["midnightblue"] = 0x191970u, ["mintcream"] = 0xF5FFFAu, ["mistyrose"] = 0xFFE4E1u, ["moccasin"] = 0xFFE4B5u,
            ["navajowhite"] = 0xFFDEADu, ["navy"] = 0x000080u, ["oldlace"] = 0xFDF5E6u, ["olive"] = 0x808000u,
            ["olivedrab"] = 0x6B8E23u, ["orange"] = 0xFFA500u, ["orangered"] = 0xFF4500u, ["orchid"] = 0xDA70D6u,
            ["palegoldenrod"] = 0xEEE8AAu, ["palegreen"] = 0x98FB98u, ["paleturquoise"] = 0xAFEEEEu, ["palevioletred"] = 0xDB7093u,
            ["papayawhip"] = 0xFFEFD5u, ["peachpuff"] = 0xFFDAB9u, ["peru"] = 0xCD853Fu, ["pink"] = 0xFFC0CBu,
            ["plum"] = 0xDDA0DDu, ["powderblue"] = 0xB0E0E6u, ["purple"] = 0x800080u, ["rebeccapurple"] = 0x663399u,
            ["red"] = 0xFF0000u, ["rosybrown"] = 0xBC8F8Fu, ["royalblue"] = 0x4169E1u, ["saddlebrown"] = 0x8B4513u,
            ["salmon"] = 0xFA8072u, ["sandybrown"] = 0xF4A460u, ["seagreen"] = 0x2E8B57u, ["seashell"] = 0xFFF5EEu,
            ["sienna"] = 0xA0522Du, ["silver"] = 0xC0C0C0u, ["skyblue"] = 0x87CEEBu, ["slateblue"] = 0x6A5ACDu,
            ["slategray"] = 0x708090u, ["slategrey"] = 0x708090u, ["snow"] = 0xFFFAFAu, ["springgreen"] = 0x00FF7Fu,
            ["steelblue"] = 0x4682B4u, ["tan"] = 0xD2B48Cu, ["teal"] = 0x008080u, ["thistle"] = 0xD8BFD8u,
            ["tomato"] = 0xFF6347u, ["turquoise"] = 0x40E0D0u, ["violet"] = 0xEE82EEu, ["wheat"] = 0xF5DEB3u,
            ["white"] = 0xFFFFFFu, ["whitesmoke"] = 0xF5F5F5u, ["yellow"] = 0xFFFF00u, ["yellowgreen"] = 0x9ACD32u,
        };

        internal static bool TryGetColor(string name, out Color color)
        {
            if (name == "transparent")
            {
                color = new Color(0, 0, 0, 0);
                return true;
            }
            if (s_NamedColors.TryGetValue(name, out var v))
            {
                color = new Color32((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF), 255);
                return true;
            }
            color = Color.clear;
            return false;
        }
    }

    /// <summary>The variables in effect for an element: its ancestors' and its own matched custom
    /// properties, the latest of a name winning.</summary>
    internal sealed class VariableContext
    {
        internal static readonly VariableContext none = new VariableContext();
        readonly List<(string Name, StyleSheet Sheet, StyleValueHandle[] Handles)> m_Variables = new List<(string, StyleSheet, StyleValueHandle[])>();

        internal int Count => m_Variables.Count;

        internal VariableContext() { }
        internal VariableContext(VariableContext other) { m_Variables.AddRange(other.m_Variables); }

        internal void Add(string name, StyleSheet sheet, StyleValueHandle[] handles)
        {
            for (int i = m_Variables.Count - 1; i >= 0; i--)
                if (m_Variables[i].Name == name) { m_Variables.RemoveAt(i); break; }
            m_Variables.Add((name, sheet, handles));
        }

        internal bool TryFind(string name, out StyleSheet sheet, out StyleValueHandle[] handles)
        {
            for (int i = m_Variables.Count - 1; i >= 0; i--)
            {
                if (m_Variables[i].Name == name)
                {
                    sheet = m_Variables[i].Sheet;
                    handles = m_Variables[i].Handles;
                    return true;
                }
            }
            sheet = null;
            handles = null;
            return false;
        }
    }

    /// <summary>Reads the properties of a rule (or inline rule) one by one, its var() references
    /// resolved against the element's variables (an unresolvable one makes the value `unset`).</summary>
    internal sealed class UssReader
    {
        readonly List<UssValue> m_Values = new List<UssValue>();
        readonly List<int> m_ValueCount = new List<int>();
        StyleSheet m_Sheet;
        StyleProperty[] m_Properties;
        int m_CurrentValueIndex;
        int m_CurrentPropertyIndex;
        VariableContext m_Variables;

        public StyleProperty property { get; private set; }
        public StylePropertyId propertyId { get; private set; }
        public int valueCount { get; private set; }
        public float dpiScaling { get; private set; }

        public void SetContext(StyleSheet sheet, StyleProperty[] properties, VariableContext variables, float dpiScaling)
        {
            m_Sheet = sheet;
            m_Properties = properties ?? Array.Empty<StyleProperty>();
            m_Variables = variables ?? VariableContext.none;
            this.dpiScaling = dpiScaling;
            Load();
        }

        void Load()
        {
            m_CurrentPropertyIndex = 0;
            m_CurrentValueIndex = 0;
            m_Values.Clear();
            m_ValueCount.Clear();
            foreach (var p in m_Properties)
            {
                int count = 0;
                if (p.requireVariableResolve)
                {
                    var resolved = new List<UssValue>();
                    bool ok = true;
                    var values = p.values;
                    for (int j = 0; j < values.Length && ok; j++)
                    {
                        if (values[j].IsVarFunction()) ok = ResolveVar(m_Sheet, values, ref j, resolved, new Stack<string>(), true) == Result.Valid;
                        else resolved.Add(new UssValue { sheet = m_Sheet, handle = values[j] });
                    }
                    if (ok && resolved.Count > 0)
                    {
                        m_Values.AddRange(resolved);
                        count = resolved.Count;
                    }
                    else
                    {
                        m_Values.Add(new UssValue { sheet = m_Sheet, handle = new StyleValueHandle((int)StyleValueKeyword.Unset, StyleValueType.Keyword) });
                        count = 1;
                    }
                }
                else
                {
                    foreach (var h in p.values) m_Values.Add(new UssValue { sheet = m_Sheet, handle = h });
                    count = p.values.Length;
                }
                m_ValueCount.Add(count);
            }
            SetCurrent();
        }

        enum Result { Valid, Invalid, NotFound }

        Result ResolveVar(StyleSheet sheet, StyleValueHandle[] handles, ref int index, List<UssValue> into, Stack<string> stack, bool append)
        {
            int argc = (int)sheet.ReadFloat(handles[++index]);
            string name = sheet.ReadVariable(handles[++index]);
            var result = append ? ResolveVariable(name, into, stack) : Result.Valid;
            if (argc > 1)
            {
                var comma = handles[++index];
                if (comma.valueType == StyleValueType.CommaSeparator && index + 1 < handles.Length)
                {
                    index++;
                    var fallback = ResolveFallback(sheet, handles, ref index, into, stack, append && result == Result.NotFound);
                    if (append && result == Result.NotFound) result = fallback;
                }
            }
            return result;
        }

        Result ResolveVariable(string name, List<UssValue> into, Stack<string> stack)
        {
            if (!m_Variables.TryFind(name, out var sheet, out var handles)) return Result.NotFound;
            if (stack.Contains(name)) return Result.NotFound;
            stack.Push(name);
            var result = Result.Valid;
            for (int i = 0; i < handles.Length && result == Result.Valid; i++)
            {
                if (into.Count + 1 > 100) return Result.Invalid;
                if (handles[i].IsVarFunction()) result = ResolveVar(sheet, handles, ref i, into, stack, true);
                else into.Add(new UssValue { sheet = sheet, handle = handles[i] });
            }
            stack.Pop();
            return result;
        }

        Result ResolveFallback(StyleSheet sheet, StyleValueHandle[] handles, ref int index, List<UssValue> into, Stack<string> stack, bool append)
        {
            var result = Result.Valid;
            while (index < handles.Length && result == Result.Valid)
            {
                var h = handles[index];
                if (h.IsVarFunction())
                {
                    var r = ResolveVar(sheet, handles, ref index, into, stack, append);
                    if (append && r != Result.Valid) result = r;
                }
                else if (append)
                {
                    into.Add(new UssValue { sheet = sheet, handle = h });
                }
                index++;
            }
            return result;
        }

        void SetCurrent()
        {
            if (m_CurrentPropertyIndex < m_Properties.Length)
            {
                property = m_Properties[m_CurrentPropertyIndex];
                propertyId = StyleProperties.IdOf(property.name);
                valueCount = m_ValueCount[m_CurrentPropertyIndex];
            }
            else
            {
                property = null;
                propertyId = StylePropertyId.Unknown;
                valueCount = 0;
            }
        }

        public StylePropertyId MoveNextProperty()
        {
            m_CurrentPropertyIndex++;
            m_CurrentValueIndex += valueCount;
            SetCurrent();
            return propertyId;
        }

        public UssValue GetValue(int index) => m_Values[m_CurrentValueIndex + index];
        public StyleValueType GetValueType(int index) => m_Values[m_CurrentValueIndex + index].handle.valueType;
        public bool IsValueType(int index, StyleValueType type) => GetValueType(index) == type;

        public bool IsKeyword(int index, StyleValueKeyword keyword)
        {
            var v = GetValue(index);
            return v.handle.valueType == StyleValueType.Keyword && v.handle.valueIndex == (int)keyword;
        }

        public string ReadAsString(int index)
        {
            var v = GetValue(index);
            return v.sheet.ReadAsString(v.handle);
        }

        public Length ReadLength(int index)
        {
            var v = GetValue(index);
            if (v.handle.valueType == StyleValueType.Keyword)
            {
                switch ((StyleValueKeyword)v.handle.valueIndex)
                {
                    case StyleValueKeyword.Auto: return Length.Auto();
                    case StyleValueKeyword.None: return Length.None();
                    default: return default;
                }
            }
            return v.sheet.ReadDimension(v.handle).ToLength();
        }

        public float ReadFloat(int index)
        {
            var v = GetValue(index);
            return v.sheet.ReadFloat(v.handle);
        }

        public int ReadInt(int index) => (int)ReadFloat(index);

        public Color ReadColor(int index)
        {
            var v = GetValue(index);
            if (v.handle.valueType == StyleValueType.Enum)
            {
                StyleProperties.TryGetColor(v.sheet.ReadAsString(v.handle).ToLowerInvariant(), out var named);
                return named;
            }
            return v.sheet.ReadColor(v.handle);
        }

        public int ReadEnum(StyleEnumType type, int index) => ReadEnum(type, GetValue(index));

        internal static int ReadEnum(StyleEnumType type, UssValue v)
        {
            string text = v.handle.valueType == StyleValueType.Keyword
                ? StyleKeywords.ToUss(v.sheet.ReadKeyword(v.handle))
                : v.sheet.ReadEnum(v.handle);
            StyleProperties.TryGetEnumIntValue(type, text, out int value);
            return value;
        }

        static bool TryReadEnum(StyleEnumType type, UssValue v, out int value)
        {
            string text = v.handle.valueType == StyleValueType.Keyword
                ? StyleKeywords.ToUss(v.sheet.ReadKeyword(v.handle))
                : v.sheet.ReadEnum(v.handle);
            return StyleProperties.TryGetEnumIntValue(type, text, out value);
        }

        public FontDefinition ReadFontDefinition(int index)
        {
            var v = GetValue(index);
            Font font = null;
            FontAsset fontAsset = null;
            switch (v.handle.valueType)
            {
                case StyleValueType.ResourcePath:
                {
                    var path = v.sheet.ReadResourcePath(v.handle);
                    if (!string.IsNullOrEmpty(path))
                    {
                        font = UiResources.Load(path, typeof(Font), dpiScaling) as Font;
                        if (font == null) fontAsset = UiResources.Load(path, typeof(FontAsset), dpiScaling) as FontAsset;
                    }
                    if (font == null && fontAsset == null) Debug.LogWarning($"Font not found for path: {path}");
                    break;
                }
                case StyleValueType.AssetReference:
                {
                    var obj = v.sheet.ReadAssetReference(v.handle);
                    font = obj as Font;
                    if (font == null) fontAsset = obj as FontAsset;
                    break;
                }
                case StyleValueType.Keyword:
                    if (v.handle.valueIndex != (int)StyleValueKeyword.None) Debug.LogWarning("Invalid keyword for font " + (StyleValueKeyword)v.handle.valueIndex);
                    break;
                default:
                    Debug.LogWarning("Invalid value for font " + v.handle.valueType);
                    break;
            }
            if (font != null) return FontDefinition.FromFont(font);
            if (fontAsset != null) return FontDefinition.FromSDFFont(fontAsset);
            return default;
        }

        public Font ReadFont(int index)
        {
            var v = GetValue(index);
            Font font = null;
            switch (v.handle.valueType)
            {
                case StyleValueType.ResourcePath:
                {
                    var path = v.sheet.ReadResourcePath(v.handle);
                    if (!string.IsNullOrEmpty(path)) font = UiResources.Load(path, typeof(Font), dpiScaling) as Font;
                    if (font == null) Debug.LogWarning($"Font not found for path: {path}");
                    break;
                }
                case StyleValueType.AssetReference:
                    font = v.sheet.ReadAssetReference(v.handle) as Font;
                    break;
                case StyleValueType.Keyword:
                    if (v.handle.valueIndex != (int)StyleValueKeyword.None) Debug.LogWarning("Invalid keyword for font " + (StyleValueKeyword)v.handle.valueIndex);
                    break;
                default:
                    Debug.LogWarning("Invalid value for font " + v.handle.valueType);
                    break;
            }
            return font;
        }

        internal static bool TryGetImage(UssValue v, float dpiScaling, out Background image)
        {
            image = default;
            switch (v.handle.valueType)
            {
                case StyleValueType.ResourcePath:
                {
                    var path = v.sheet.ReadResourcePath(v.handle);
                    Object obj = null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        obj = UiResources.Load(path, typeof(Sprite), dpiScaling)
                              ?? UiResources.Load(path, typeof(Texture2D), dpiScaling)
                              ?? UiResources.Load(path, typeof(VectorImage), dpiScaling)
                              ?? UiResources.Load(path, typeof(RenderTexture), dpiScaling);
                    }
                    image = Background.FromObject(obj);
                    if (image.IsEmpty())
                    {
                        Debug.LogWarning($"Image not found for path: {path}");
                        return false;
                    }
                    return true;
                }
                case StyleValueType.AssetReference:
                    image = Background.FromObject(v.sheet.ReadAssetReference(v.handle));
                    if (image.IsEmpty())
                    {
                        Debug.LogWarning("Invalid image specified");
                        return false;
                    }
                    return true;
                case StyleValueType.MissingAssetReference:
                    return false;
                case StyleValueType.ScalableImage:
                {
                    var scalable = v.sheet.ReadScalableImage(v.handle);
                    if (scalable.normalImage == null && scalable.highResolutionImage == null)
                    {
                        Debug.LogWarning("Invalid scalable image specified");
                        return false;
                    }
                    if (dpiScaling > 1f && scalable.highResolutionImage != null)
                    {
                        image = Background.FromTexture2D(scalable.highResolutionImage);
                        scalable.highResolutionImage.pixelsPerPoint = 2f;
                    }
                    else
                    {
                        image = Background.FromTexture2D(scalable.normalImage);
                    }
                    if (!Mathf.Approximately(dpiScaling % 1f, 0f) && image.texture != null) image.texture.filterMode = FilterMode.Bilinear;
                    return true;
                }
                default:
                    Debug.LogWarning("Invalid value for image texture " + v.handle.valueType);
                    return false;
            }
        }

        public Background ReadBackground(int index)
        {
            var v = GetValue(index);
            if (v.handle.valueType == StyleValueType.Keyword)
            {
                if (v.handle.valueIndex != (int)StyleValueKeyword.None) Debug.LogWarning("Invalid keyword for image source " + (StyleValueKeyword)v.handle.valueIndex);
                return default;
            }
            if (!TryGetImage(v, dpiScaling, out var image)) return default;
            return image;
        }

        public Cursor ReadCursor(int index)
        {
            var type = GetValueType(index);
            var cursor = new Cursor();
            if (type == StyleValueType.ResourcePath || type == StyleValueType.AssetReference || type == StyleValueType.ScalableImage || type == StyleValueType.MissingAssetReference)
            {
                if (TryGetImage(GetValue(index), dpiScaling, out var image))
                {
                    cursor.texture = image.texture;
                    if (valueCount >= 3)
                    {
                        var x = GetValue(index + 1);
                        var y = GetValue(index + 2);
                        if (x.handle.valueType == StyleValueType.Float && y.handle.valueType == StyleValueType.Float)
                            cursor.hotspot = new Vector2(x.sheet.ReadFloat(x.handle), y.sheet.ReadFloat(y.handle));
                    }
                }
            }
            return cursor;
        }

        public TextShadow ReadTextShadow(int index)
        {
            float x = 0, y = 0, blur = 0;
            Color color = Color.clear;
            if (valueCount >= 2)
            {
                int i = index;
                var type = GetValueType(i);
                bool hasColor = false;
                if (type == StyleValueType.Color || type == StyleValueType.Enum)
                {
                    color = ReadColor(i++);
                    hasColor = true;
                }
                if (i + 1 < valueCount)
                {
                    var t1 = GetValueType(i);
                    var t2 = GetValueType(i + 1);
                    if ((t1 == StyleValueType.Dimension || t1 == StyleValueType.Float) && (t2 == StyleValueType.Dimension || t2 == StyleValueType.Float))
                    {
                        var v1 = GetValue(i++);
                        var v2 = GetValue(i++);
                        x = v1.sheet.ReadDimension(v1.handle).value;
                        y = v2.sheet.ReadDimension(v2.handle).value;
                    }
                }
                if (i < valueCount)
                {
                    type = GetValueType(i);
                    if (type == StyleValueType.Dimension || type == StyleValueType.Float)
                    {
                        var v3 = GetValue(i++);
                        blur = v3.sheet.ReadDimension(v3.handle).value;
                    }
                    else if ((type == StyleValueType.Color || type == StyleValueType.Enum) && !hasColor)
                    {
                        color = ReadColor(i);
                    }
                }
                if (i < valueCount)
                {
                    type = GetValueType(i);
                    if ((type == StyleValueType.Color || type == StyleValueType.Enum) && !hasColor) color = ReadColor(i);
                }
            }
            return new TextShadow { offset = new Vector2(x, y), blurRadius = blur, color = color };
        }

        UssValue Opt(int index, int offset) => valueCount > offset ? GetValue(index + offset) : default;

        public Translate ReadTranslate(int index)
        {
            var v1 = GetValue(index);
            if (v1.handle.valueType == StyleValueType.Keyword && v1.handle.valueIndex == (int)StyleValueKeyword.None) return Translate.None();
            var v2 = Opt(index, 1);
            var v3 = Opt(index, 2);
            Length x = 0f, y = 0f;
            float z = 0f;
            bool IsNum(UssValue v) => v.handle.valueType == StyleValueType.Dimension || v.handle.valueType == StyleValueType.Float;
            switch (valueCount)
            {
                case 1:
                    if (IsNum(v1))
                    {
                        x = v1.sheet.ReadDimension(v1.handle).ToLength();
                        y = v1.sheet.ReadDimension(v1.handle).ToLength();
                    }
                    break;
                case 2:
                case 3:
                    if (valueCount == 3 && IsNum(v3)) z = v3.sheet.ReadDimension(v3.handle).value;
                    if (IsNum(v1)) x = v1.sheet.ReadDimension(v1.handle).ToLength();
                    if (IsNum(v2)) y = v2.sheet.ReadDimension(v2.handle).ToLength();
                    break;
            }
            return new Translate(x, y, z);
        }

        public TransformOrigin ReadTransformOrigin(int index)
        {
            var v1 = GetValue(index);
            var v2 = Opt(index, 1);
            var v3 = Opt(index, 2);
            Length x = Length.Percent(50f), y = Length.Percent(50f);
            float z = 0f;
            switch (valueCount)
            {
                case 1:
                {
                    var l = OriginEnum(v1, out _, out bool horizontal);
                    if (horizontal) x = l;
                    else y = l;
                    break;
                }
                case 2:
                case 3:
                {
                    if (valueCount == 3 && (v3.handle.valueType == StyleValueType.Dimension || v3.handle.valueType == StyleValueType.Float))
                        z = v3.sheet.ReadDimension(v3.handle).value;
                    var l1 = OriginEnum(v1, out bool vert1, out bool hor1);
                    var l2 = OriginEnum(v2, out bool vert2, out bool hor2);
                    if (hor1 && vert2) { x = l1; y = l2; }
                    else if (hor2 && vert1) { x = l2; y = l1; }
                    break;
                }
            }
            return new TransformOrigin(x, y, z);
        }

        static Length OriginEnum(UssValue v, out bool isVertical, out bool isHorizontal)
        {
            if (v.handle.valueType == StyleValueType.Enum)
            {
                switch ((TransformOriginOffset)ReadEnum(StyleEnumType.TransformOriginOffset, v))
                {
                    case TransformOriginOffset.Left: isVertical = false; isHorizontal = true; return Length.Percent(0f);
                    case TransformOriginOffset.Top: isVertical = true; isHorizontal = false; return Length.Percent(0f);
                    case TransformOriginOffset.Center: isVertical = true; isHorizontal = true; return Length.Percent(50f);
                    case TransformOriginOffset.Right: isVertical = false; isHorizontal = true; return Length.Percent(100f);
                    case TransformOriginOffset.Bottom: isVertical = true; isHorizontal = false; return Length.Percent(100f);
                }
            }
            else if (v.handle.valueType == StyleValueType.Dimension || v.handle.valueType == StyleValueType.Float)
            {
                isVertical = true;
                isHorizontal = true;
                return v.sheet.ReadDimension(v.handle).ToLength();
            }
            isVertical = false;
            isHorizontal = false;
            return Length.Percent(50f);
        }

        public Rotate ReadRotate(int index)
        {
            var v1 = GetValue(index);
            if (v1.handle.valueType == StyleValueType.Keyword && v1.handle.valueIndex == (int)StyleValueKeyword.None) return Rotate.None();
            var r = Rotate.Initial();
            switch (valueCount)
            {
                case 1:
                    if (v1.handle.valueType == StyleValueType.Dimension) r.angle = ReadAngle(v1);
                    break;
                case 2:
                    r.angle = ReadAngle(GetValue(index + 1));
                    switch ((Axis)ReadEnum(StyleEnumType.Axis, v1))
                    {
                        case Axis.X: r.axis = new Vector3(1, 0, 0); break;
                        case Axis.Y: r.axis = new Vector3(0, 1, 0); break;
                        case Axis.Z: r.axis = new Vector3(0, 0, 1); break;
                    }
                    break;
                case 4:
                {
                    var a = GetValue(index + 1);
                    var b = GetValue(index + 2);
                    r.angle = ReadAngle(GetValue(index + 3));
                    r.axis = new Vector3(v1.sheet.ReadFloat(v1.handle), a.sheet.ReadFloat(a.handle), b.sheet.ReadFloat(b.handle));
                    break;
                }
            }
            return r;
        }

        static Angle ReadAngle(UssValue v)
        {
            if (v.handle.valueType == StyleValueType.Keyword)
                return (StyleValueKeyword)v.handle.valueIndex == StyleValueKeyword.None ? Angle.None() : default;
            return v.sheet.ReadDimension(v.handle).ToAngle();
        }

        public Scale ReadScale(int index)
        {
            var v1 = GetValue(index);
            if (v1.handle.valueType == StyleValueType.Keyword && v1.handle.valueIndex == (int)StyleValueKeyword.None) return Scale.None();
            var v2 = Opt(index, 1);
            var v3 = Opt(index, 2);
            var s = Vector3.one;
            bool IsNum(UssValue v) => v.handle.valueType == StyleValueType.Dimension || v.handle.valueType == StyleValueType.Float;
            switch (valueCount)
            {
                case 1:
                    if (IsNum(v1)) { s.x = v1.sheet.ReadFloat(v1.handle); s.y = s.x; }
                    break;
                case 2:
                case 3:
                    if (valueCount == 3 && IsNum(v3)) s.z = v3.sheet.ReadFloat(v3.handle);
                    if (IsNum(v1)) s.x = v1.sheet.ReadFloat(v1.handle);
                    if (IsNum(v2)) s.y = v2.sheet.ReadFloat(v2.handle);
                    break;
            }
            return new Scale(s);
        }

        public BackgroundPosition ReadBackgroundPositionX(int index) => ReadBackgroundPosition(index, BackgroundPositionKeyword.Left);
        public BackgroundPosition ReadBackgroundPositionY(int index) => ReadBackgroundPosition(index, BackgroundPositionKeyword.Top);

        BackgroundPosition ReadBackgroundPosition(int index, BackgroundPositionKeyword keyword)
        {
            var v1 = GetValue(index);
            var v2 = Opt(index, 1);
            switch (valueCount)
            {
                case 1:
                    if (v1.handle.valueType == StyleValueType.Enum) return new BackgroundPosition((BackgroundPositionKeyword)ReadEnum(StyleEnumType.BackgroundPositionKeyword, v1));
                    if (v1.handle.valueType == StyleValueType.Dimension || v1.handle.valueType == StyleValueType.Float)
                        return new BackgroundPosition(keyword, v1.sheet.ReadDimension(v1.handle).ToLength());
                    break;
                case 2:
                    if (v1.handle.valueType == StyleValueType.Enum && (v2.handle.valueType == StyleValueType.Dimension || v2.handle.valueType == StyleValueType.Float))
                        return new BackgroundPosition((BackgroundPositionKeyword)ReadEnum(StyleEnumType.BackgroundPositionKeyword, v1), v1.sheet.ReadDimension(v2.handle).ToLength());
                    break;
            }
            return default;
        }

        public BackgroundRepeat ReadBackgroundRepeat(int index)
        {
            var v1 = GetValue(index);
            var r = default(BackgroundRepeat);
            if (valueCount == 1)
            {
                if (TryReadEnum(StyleEnumType.RepeatXY, v1, out int xy))
                {
                    if (xy == 0) { r.x = Repeat.Repeat; r.y = Repeat.NoRepeat; }
                    else if (xy == 1) { r.x = Repeat.NoRepeat; r.y = Repeat.Repeat; }
                }
                else
                {
                    r.x = (Repeat)ReadEnum(StyleEnumType.Repeat, v1);
                    r.y = r.x;
                }
            }
            else
            {
                r.x = (Repeat)ReadEnum(StyleEnumType.Repeat, v1);
                r.y = (Repeat)ReadEnum(StyleEnumType.Repeat, GetValue(index + 1));
            }
            return r;
        }

        public BackgroundSize ReadBackgroundSize(int index)
        {
            var v1 = GetValue(index);
            var v2 = Opt(index, 1);
            var s = default(BackgroundSize);
            switch (valueCount)
            {
                case 1:
                    if (v1.handle.valueType == StyleValueType.Keyword)
                    {
                        if (v1.handle.valueIndex == (int)StyleValueKeyword.Auto) { s.x = Length.Auto(); s.y = Length.Auto(); }
                        else if (v1.handle.valueIndex == (int)StyleValueKeyword.Cover) s.sizeType = BackgroundSizeType.Cover;
                        else if (v1.handle.valueIndex == (int)StyleValueKeyword.Contain) s.sizeType = BackgroundSizeType.Contain;
                    }
                    else if (v1.handle.valueType == StyleValueType.Enum)
                    {
                        s.sizeType = (BackgroundSizeType)ReadEnum(StyleEnumType.BackgroundSizeType, v1);
                    }
                    else if (v1.handle.valueType == StyleValueType.Dimension)
                    {
                        s.x = v1.sheet.ReadDimension(v1.handle).ToLength();
                        s.y = Length.Auto();
                    }
                    break;
                case 2:
                    if (v1.handle.valueType == StyleValueType.Keyword) { if (v1.handle.valueIndex == (int)StyleValueKeyword.Auto) s.x = Length.Auto(); }
                    else if (v1.handle.valueType == StyleValueType.Dimension) s.x = v1.sheet.ReadDimension(v1.handle).ToLength();
                    if (v2.handle.valueType == StyleValueType.Keyword) { if (v2.handle.valueIndex == (int)StyleValueKeyword.Auto) s.y = Length.Auto(); }
                    else if (v2.handle.valueType == StyleValueType.Dimension) s.y = v2.sheet.ReadDimension(v2.handle).ToLength();
                    break;
            }
            return s;
        }

        public List<EasingFunction> ReadListEasingFunction(int index)
        {
            var list = new List<EasingFunction>();
            do
            {
                var v = GetValue(index);
                if (v.handle.valueType == StyleValueType.Enum)
                {
                    StyleProperties.TryGetEnumIntValue(StyleEnumType.EasingMode, v.sheet.ReadEnum(v.handle), out int mode);
                    list.Add(new EasingFunction((EasingMode)mode));
                    index++;
                }
                if (index < valueCount && GetValueType(index) == StyleValueType.CommaSeparator) index++;
                else if (index < valueCount && GetValueType(index) != StyleValueType.Enum) index++;
            }
            while (index < valueCount);
            return list;
        }

        public List<TimeValue> ReadListTimeValue(int index)
        {
            var list = new List<TimeValue>();
            do
            {
                var v = GetValue(index);
                list.Add(v.sheet.ReadDimension(v.handle).ToTime());
                index++;
                if (index < valueCount && GetValueType(index) == StyleValueType.CommaSeparator) index++;
            }
            while (index < valueCount);
            return list;
        }

        public List<StylePropertyName> ReadListStylePropertyName(int index)
        {
            var list = new List<StylePropertyName>();
            do
            {
                var v = GetValue(index);
                list.Add(new StylePropertyName(v.sheet.ReadAsString(v.handle)));
                index++;
                if (index < valueCount && GetValueType(index) == StyleValueType.CommaSeparator) index++;
            }
            while (index < valueCount);
            return list;
        }
    }
}
