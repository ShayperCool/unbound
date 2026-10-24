using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.TextCore.Text;

// The value types of UI Toolkit styles: lengths, angles, transforms, backgrounds and their Style*
// wrappers (a value or a keyword), with Unity's equality, keyword rules and string forms.

namespace UnityEngine.UIElements
{
    public partial interface IStyleValue<T>
    {
        T value { get; set; }
        StyleKeyword keyword { get; set; }
    }

    internal static class StyleValueStrings
    {
        internal static string Num(float f) => Unbound.MonoNumberFormat.Format(f, null, CultureInfo.InvariantCulture.NumberFormat);
        internal static string Of<T>(IStyleValue<T> s) => s.keyword != StyleKeyword.Undefined ? s.keyword.ToString() : Unbound.MonoNumberFormat.ToString((object)s.value);
    }

    [Serializable]
    public partial struct Length : IEquatable<Length>
    {
        enum Unit { Pixel, Percent, Auto, None }

        internal const float k_MaxValue = 8388608f;

        [SerializeField] float m_Value;
        [SerializeField] Unit m_Unit;

        public float value { get => m_Value; set => m_Value = Mathf.Clamp(value, -k_MaxValue, k_MaxValue); }
        public LengthUnit unit { get => (LengthUnit)m_Unit; set => m_Unit = (Unit)value; }

        public static Length Percent(float value) => new Length(value, LengthUnit.Percent);
        public static Length Auto() => new Length(0f, Unit.Auto);
        public static Length None() => new Length(0f, Unit.None);
        public bool IsAuto() => m_Unit == Unit.Auto;
        public bool IsNone() => m_Unit == Unit.None;

        public Length(float value) : this(value, Unit.Pixel) { }
        public Length(float value, LengthUnit unit) : this(value, (Unit)unit) { }
        Length(float value, Unit unit)
        {
            m_Value = 0;
            m_Unit = unit;
            this.value = value;
        }

        public static implicit operator Length(float value) => new Length(value, LengthUnit.Pixel);
        public static bool operator ==(Length lhs, Length rhs) => lhs.m_Value == rhs.m_Value && lhs.m_Unit == rhs.m_Unit;
        public static bool operator !=(Length lhs, Length rhs) => !(lhs == rhs);
        public bool Equals(Length other) => other == this;
        public override bool Equals(object obj) => obj is Length other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Unit;

        public override string ToString()
        {
            var number = StyleValueStrings.Num(value);
            switch (m_Unit)
            {
                case Unit.Pixel: return Mathf.Approximately(0f, value) ? number : number + "px";
                case Unit.Percent: return number + "%";
                case Unit.Auto: return "auto";
                case Unit.None: return "none";
            }
            return number;
        }
    }

    public partial struct StyleLength : IStyleValue<Length>, IEquatable<StyleLength>
    {
        Length m_Value;
        StyleKeyword m_Keyword;

        public Length value
        {
            get => m_Keyword == StyleKeyword.Auto || m_Keyword == StyleKeyword.None || m_Keyword == StyleKeyword.Undefined ? m_Value : default;
            set
            {
                m_Keyword = value.IsAuto() ? StyleKeyword.Auto : value.IsNone() ? StyleKeyword.None : StyleKeyword.Undefined;
                m_Value = value;
            }
        }

        public StyleKeyword keyword
        {
            get => m_Keyword;
            set
            {
                m_Keyword = value;
                if (value == StyleKeyword.Auto) m_Value = Length.Auto();
                else if (value == StyleKeyword.None) m_Value = Length.None();
                else if (value != StyleKeyword.Undefined) m_Value = default;
            }
        }

        public StyleLength(float v) : this(new Length(v, LengthUnit.Pixel), StyleKeyword.Undefined) { }
        public StyleLength(Length v) : this(v, StyleKeyword.Undefined) { }
        public StyleLength(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleLength(Length v, StyleKeyword keyword)
        {
            m_Keyword = keyword;
            m_Value = v;
            if (v.IsAuto()) m_Keyword = StyleKeyword.Auto;
            else if (v.IsNone()) m_Keyword = StyleKeyword.None;
        }

        public static bool operator ==(StyleLength lhs, StyleLength rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleLength lhs, StyleLength rhs) => !(lhs == rhs);
        public static implicit operator StyleLength(StyleKeyword keyword) => new StyleLength(keyword);
        public static implicit operator StyleLength(float v) => new StyleLength(v);
        public static implicit operator StyleLength(Length v) => new StyleLength(v);
        public bool Equals(StyleLength other) => other == this;
        public override bool Equals(object obj) => obj is StyleLength other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct StyleFloat : IStyleValue<float>, IEquatable<StyleFloat>
    {
        float m_Value;
        StyleKeyword m_Keyword;

        public float value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : 0f; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleFloat(float v) : this(v, StyleKeyword.Undefined) { }
        public StyleFloat(StyleKeyword keyword) : this(0f, keyword) { }
        internal StyleFloat(float v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleFloat lhs, StyleFloat rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleFloat lhs, StyleFloat rhs) => !(lhs == rhs);
        public static implicit operator StyleFloat(StyleKeyword keyword) => new StyleFloat(keyword);
        public static implicit operator StyleFloat(float v) => new StyleFloat(v);
        public bool Equals(StyleFloat other) => other == this;
        public override bool Equals(object obj) => obj is StyleFloat other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct StyleInt : IStyleValue<int>, IEquatable<StyleInt>
    {
        int m_Value;
        StyleKeyword m_Keyword;

        public int value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : 0; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleInt(int v) : this(v, StyleKeyword.Undefined) { }
        public StyleInt(StyleKeyword keyword) : this(0, keyword) { }
        internal StyleInt(int v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleInt lhs, StyleInt rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleInt lhs, StyleInt rhs) => !(lhs == rhs);
        public static implicit operator StyleInt(StyleKeyword keyword) => new StyleInt(keyword);
        public static implicit operator StyleInt(int v) => new StyleInt(v);
        public bool Equals(StyleInt other) => other == this;
        public override bool Equals(object obj) => obj is StyleInt other && Equals(other);
        public override int GetHashCode() => (m_Value * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct StyleColor : IStyleValue<Color>, IEquatable<StyleColor>
    {
        Color m_Value;
        StyleKeyword m_Keyword;

        public Color value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : Color.clear; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleColor(Color v) : this(v, StyleKeyword.Undefined) { }
        public StyleColor(StyleKeyword keyword) : this(Color.clear, keyword) { }
        internal StyleColor(Color v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleColor lhs, StyleColor rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleColor lhs, StyleColor rhs) => !(lhs == rhs);
        public static bool operator ==(StyleColor lhs, Color rhs) => lhs == new StyleColor(rhs);
        public static bool operator !=(StyleColor lhs, Color rhs) => !(lhs == rhs);
        public static implicit operator StyleColor(StyleKeyword keyword) => new StyleColor(keyword);
        public static implicit operator StyleColor(Color v) => new StyleColor(v);
        public bool Equals(StyleColor other) => other == this;
        public override bool Equals(object obj) => obj is StyleColor other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct StyleEnum<T> : IStyleValue<T>, IEquatable<StyleEnum<T>> where T : struct, IConvertible
    {
        T m_Value;
        StyleKeyword m_Keyword;

        public T value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleEnum(T v) : this(v, StyleKeyword.Undefined) { }
        public StyleEnum(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleEnum(T v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleEnum<T> lhs, StyleEnum<T> rhs) => lhs.m_Keyword == rhs.m_Keyword && EqualityComparer<T>.Default.Equals(lhs.m_Value, rhs.m_Value);
        public static bool operator !=(StyleEnum<T> lhs, StyleEnum<T> rhs) => !(lhs == rhs);
        public static implicit operator StyleEnum<T>(StyleKeyword keyword) => new StyleEnum<T>(keyword);
        public static implicit operator StyleEnum<T>(T v) => new StyleEnum<T>(v);
        public bool Equals(StyleEnum<T> other) => other == this;
        public override bool Equals(object obj) => obj is StyleEnum<T> other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct Background : IEquatable<Background>
    {
        Texture2D m_Texture;
        Sprite m_Sprite;
        RenderTexture m_RenderTexture;
        VectorImage m_VectorImage;

        public Texture2D texture { get => m_Texture; set { if (m_Texture == value) return; Clear(); m_Texture = value; } }
        public Sprite sprite { get => m_Sprite; set { if (m_Sprite == value) return; Clear(); m_Sprite = value; } }
        public RenderTexture renderTexture { get => m_RenderTexture; set { if (m_RenderTexture == value) return; Clear(); m_RenderTexture = value; } }
        public VectorImage vectorImage { get => m_VectorImage; set { if (m_VectorImage == value) return; Clear(); m_VectorImage = value; } }

        void Clear() { m_Texture = null; m_Sprite = null; m_RenderTexture = null; m_VectorImage = null; }

        [Obsolete("Use Background.FromTexture2D instead")]
        public Background(Texture2D t) { m_Texture = t; m_Sprite = null; m_RenderTexture = null; m_VectorImage = null; }

        public static Background FromTexture2D(Texture2D t) => new Background { texture = t };
        public static Background FromRenderTexture(RenderTexture rt) => new Background { renderTexture = rt };
        public static Background FromSprite(Sprite s) => new Background { sprite = s };
        public static Background FromVectorImage(VectorImage vi) => new Background { vectorImage = vi };

        internal static Background FromObject(Object obj) => obj switch
        {
            Texture2D t => FromTexture2D(t),
            RenderTexture rt => FromRenderTexture(rt),
            Sprite s => FromSprite(s),
            VectorImage v => FromVectorImage(v),
            _ => default,
        };

        public Object GetSelectedImage()
        {
            if (texture != null) return texture;
            if (sprite != null) return sprite;
            if (renderTexture != null) return renderTexture;
            if (vectorImage != null) return vectorImage;
            return null;
        }

        public bool IsEmpty() => GetSelectedImage() == null;

        public static bool operator ==(Background lhs, Background rhs) =>
            lhs.texture == rhs.texture && lhs.sprite == rhs.sprite && lhs.renderTexture == rhs.renderTexture && lhs.vectorImage == rhs.vectorImage;
        public static bool operator !=(Background lhs, Background rhs) => !(lhs == rhs);
        public static implicit operator Background(Texture2D v) => FromTexture2D(v);
        public bool Equals(Background other) => other == this;
        public override bool Equals(object obj) => obj is Background other && Equals(other);
        public override int GetHashCode()
        {
            int h = 851985039;
            if (texture != null) h = h * -1521134295 + texture.GetHashCode();
            if (sprite != null) h = h * -1521134295 + sprite.GetHashCode();
            if (renderTexture != null) h = h * -1521134295 + renderTexture.GetHashCode();
            if (vectorImage != null) h = h * -1521134295 + vectorImage.GetHashCode();
            return h;
        }
        public override string ToString() => GetSelectedImage()?.ToString() ?? "";
    }

    public partial struct StyleBackground : IStyleValue<Background>, IEquatable<StyleBackground>
    {
        Background m_Value;
        StyleKeyword m_Keyword;

        public Background value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleBackground(Background v) : this(v, StyleKeyword.Undefined) { }
        public StyleBackground(Texture2D v) : this(v, StyleKeyword.Undefined) { }
        public StyleBackground(Sprite v) : this(v, StyleKeyword.Undefined) { }
        public StyleBackground(VectorImage v) : this(v, StyleKeyword.Undefined) { }
        public StyleBackground(StyleKeyword keyword) : this(default(Background), keyword) { }
        internal StyleBackground(Texture2D v, StyleKeyword keyword) : this(Background.FromTexture2D(v), keyword) { }
        internal StyleBackground(Sprite v, StyleKeyword keyword) : this(Background.FromSprite(v), keyword) { }
        internal StyleBackground(VectorImage v, StyleKeyword keyword) : this(Background.FromVectorImage(v), keyword) { }
        internal StyleBackground(Background v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleBackground lhs, StyleBackground rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleBackground lhs, StyleBackground rhs) => !(lhs == rhs);
        public static implicit operator StyleBackground(StyleKeyword keyword) => new StyleBackground(keyword);
        public static implicit operator StyleBackground(Background v) => new StyleBackground(v);
        public static implicit operator StyleBackground(Texture2D v) => new StyleBackground(v);
        public bool Equals(StyleBackground other) => other == this;
        public override bool Equals(object obj) => obj is StyleBackground other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct FontDefinition : IEquatable<FontDefinition>
    {
        Font m_Font;
        FontAsset m_FontAsset;

        public Font font { get => m_Font; set { if (value != null && fontAsset != null) throw new InvalidOperationException("Cannot set both Font and FontAsset on FontDefinition"); m_Font = value; } }
        public FontAsset fontAsset { get => m_FontAsset; set { if (value != null && font != null) throw new InvalidOperationException("Cannot set both Font and FontAsset on FontDefinition"); m_FontAsset = value; } }

        public static FontDefinition FromFont(Font f) => new FontDefinition { m_Font = f };
        public static FontDefinition FromSDFFont(FontAsset f) => new FontDefinition { m_FontAsset = f };
        internal static FontDefinition FromObject(object obj) => obj is Font f ? FromFont(f) : obj is FontAsset fa ? FromSDFFont(fa) : default;
        internal bool IsEmpty() => m_Font == null && m_FontAsset == null;

        public override string ToString() => font != null ? $"{font}" : $"{fontAsset}";
        public bool Equals(FontDefinition other) => Equals(m_Font, other.m_Font) && Equals(m_FontAsset, other.m_FontAsset);
        public override bool Equals(object obj) => obj is FontDefinition other && Equals(other);
        public override int GetHashCode() => ((m_Font != null ? m_Font.GetHashCode() : 0) * 397) ^ (m_FontAsset != null ? m_FontAsset.GetHashCode() : 0);
        public static bool operator ==(FontDefinition left, FontDefinition right) => left.Equals(right);
        public static bool operator !=(FontDefinition left, FontDefinition right) => !left.Equals(right);
    }

    public partial struct StyleFontDefinition : IStyleValue<FontDefinition>, IEquatable<StyleFontDefinition>
    {
        FontDefinition m_Value;
        StyleKeyword m_Keyword;

        public FontDefinition value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleFontDefinition(FontDefinition f) : this(f, StyleKeyword.Undefined) { }
        public StyleFontDefinition(FontAsset f) : this(FontDefinition.FromSDFFont(f), StyleKeyword.Undefined) { }
        public StyleFontDefinition(Font f) : this(FontDefinition.FromFont(f), StyleKeyword.Undefined) { }
        public StyleFontDefinition(StyleKeyword keyword) : this(default(FontDefinition), keyword) { }
        internal StyleFontDefinition(object obj, StyleKeyword keyword) : this(FontDefinition.FromObject(obj), keyword) { }
        internal StyleFontDefinition(FontDefinition f, StyleKeyword keyword) { m_Keyword = keyword; m_Value = f; }

        public static implicit operator StyleFontDefinition(StyleKeyword keyword) => new StyleFontDefinition(keyword);
        public static implicit operator StyleFontDefinition(FontDefinition f) => new StyleFontDefinition(f);
        public bool Equals(StyleFontDefinition other) => m_Keyword == other.m_Keyword && m_Value.Equals(other.m_Value);
        public override bool Equals(object obj) => obj is StyleFontDefinition other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public static bool operator ==(StyleFontDefinition left, StyleFontDefinition right) => left.Equals(right);
        public static bool operator !=(StyleFontDefinition left, StyleFontDefinition right) => !left.Equals(right);
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct StyleFont : IStyleValue<Font>, IEquatable<StyleFont>
    {
        Font m_Value;
        StyleKeyword m_Keyword;

        public Font value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : null; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleFont(Font v) : this(v, StyleKeyword.Undefined) { }
        public StyleFont(StyleKeyword keyword) : this(null, keyword) { }
        internal StyleFont(Font v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleFont lhs, StyleFont rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleFont lhs, StyleFont rhs) => !(lhs == rhs);
        public static implicit operator StyleFont(StyleKeyword keyword) => new StyleFont(keyword);
        public static implicit operator StyleFont(Font v) => new StyleFont(v);
        public bool Equals(StyleFont other) => other == this;
        public override bool Equals(object obj) => obj is StyleFont other && Equals(other);
        public override int GetHashCode() => ((m_Value != null ? m_Value.GetHashCode() : 0) * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct Cursor : IEquatable<Cursor>
    {
        public Texture2D texture { get; set; }
        public Vector2 hotspot { get; set; }
        internal int defaultCursorId { get; set; }

        public override bool Equals(object obj) => obj is Cursor other && Equals(other);
        public bool Equals(Cursor other) => texture == other.texture && hotspot == other.hotspot && defaultCursorId == other.defaultCursorId;
        public override int GetHashCode() => HashCode.Combine(texture, hotspot, defaultCursorId);
        public static bool operator ==(Cursor style1, Cursor style2) => style1.Equals(style2);
        public static bool operator !=(Cursor style1, Cursor style2) => !style1.Equals(style2);
        public override string ToString() => $"texture={texture}, hotspot={hotspot}";
    }

    public partial struct StyleCursor : IStyleValue<Cursor>, IEquatable<StyleCursor>
    {
        Cursor m_Value;
        StyleKeyword m_Keyword;

        public Cursor value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleCursor(Cursor v) : this(v, StyleKeyword.Undefined) { }
        public StyleCursor(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleCursor(Cursor v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleCursor lhs, StyleCursor rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleCursor lhs, StyleCursor rhs) => !(lhs == rhs);
        public static implicit operator StyleCursor(StyleKeyword keyword) => new StyleCursor(keyword);
        public static implicit operator StyleCursor(Cursor v) => new StyleCursor(v);
        public bool Equals(StyleCursor other) => other == this;
        public override bool Equals(object obj) => obj is StyleCursor other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct TextShadow : IEquatable<TextShadow>
    {
        public Vector2 offset;
        public float blurRadius;
        public Color color;

        public override bool Equals(object obj) => obj is TextShadow other && Equals(other);
        public bool Equals(TextShadow other) => other.offset == offset && other.blurRadius == blurRadius && other.color == color;
        public override int GetHashCode() => HashCode.Combine(offset, blurRadius, color);
        public static bool operator ==(TextShadow style1, TextShadow style2) => style1.Equals(style2);
        public static bool operator !=(TextShadow style1, TextShadow style2) => !style1.Equals(style2);
        public override string ToString() => $"offset={offset}, blurRadius={StyleValueStrings.Num(blurRadius)}, color={color}";
    }

    public partial struct StyleTextShadow : IStyleValue<TextShadow>, IEquatable<StyleTextShadow>
    {
        TextShadow m_Value;
        StyleKeyword m_Keyword;

        public TextShadow value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleTextShadow(TextShadow v) : this(v, StyleKeyword.Undefined) { }
        public StyleTextShadow(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleTextShadow(TextShadow v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleTextShadow lhs, StyleTextShadow rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleTextShadow lhs, StyleTextShadow rhs) => !(lhs == rhs);
        public static implicit operator StyleTextShadow(StyleKeyword keyword) => new StyleTextShadow(keyword);
        public static implicit operator StyleTextShadow(TextShadow v) => new StyleTextShadow(v);
        public bool Equals(StyleTextShadow other) => other == this;
        public override bool Equals(object obj) => obj is StyleTextShadow other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct Angle : IEquatable<Angle>
    {
        enum Unit { Degree, Gradian, Radian, Turn, None }

        [SerializeField] float m_Value;
        [SerializeField] Unit m_Unit;

        public float value { get => m_Value; set => m_Value = value; }
        public AngleUnit unit { get => (AngleUnit)m_Unit; set => m_Unit = (Unit)value; }

        public static Angle Degrees(float value) => new Angle(value, AngleUnit.Degree);
        public static Angle Gradians(float value) => new Angle(value, AngleUnit.Gradian);
        public static Angle Radians(float value) => new Angle(value, AngleUnit.Radian);
        public static Angle Turns(float value) => new Angle(value, AngleUnit.Turn);
        internal static Angle None() => new Angle(0f, Unit.None);
        internal bool IsNone() => m_Unit == Unit.None;

        public Angle(float value) : this(value, Unit.Degree) { }
        public Angle(float value, AngleUnit unit) : this(value, (Unit)unit) { }
        Angle(float value, Unit unit) { m_Value = value; m_Unit = unit; }

        public float ToDegrees() => m_Unit switch
        {
            Unit.Gradian => m_Value * 360f / 400f,
            Unit.Radian => m_Value * 180f / Mathf.PI,
            Unit.Turn => m_Value * 360f,
            Unit.None => 0f,
            _ => m_Value,
        };
        public float ToGradians() => m_Unit switch
        {
            Unit.Degree => m_Value * 400f / 360f,
            Unit.Radian => m_Value * 200f / Mathf.PI,
            Unit.Turn => m_Value * 400f,
            Unit.None => 0f,
            _ => m_Value,
        };
        public float ToRadians() => m_Unit switch
        {
            Unit.Degree => m_Value * Mathf.PI / 180f,
            Unit.Gradian => m_Value * Mathf.PI / 200f,
            Unit.Turn => m_Value * Mathf.PI * 2f,
            Unit.None => 0f,
            _ => m_Value,
        };
        public float ToTurns() => m_Unit switch
        {
            Unit.Degree => m_Value / 360f,
            Unit.Gradian => m_Value / 400f,
            Unit.Radian => m_Value / (Mathf.PI * 2f),
            Unit.None => 0f,
            _ => m_Value,
        };

        public static implicit operator Angle(float value) => new Angle(value, AngleUnit.Degree);
        public static bool operator ==(Angle lhs, Angle rhs) => lhs.m_Value == rhs.m_Value && lhs.m_Unit == rhs.m_Unit;
        public static bool operator !=(Angle lhs, Angle rhs) => !(lhs == rhs);
        public bool Equals(Angle other) => other == this;
        public override bool Equals(object obj) => obj is Angle other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Unit;
        public override string ToString()
        {
            var number = StyleValueStrings.Num(value);
            switch (m_Unit)
            {
                case Unit.Degree: return Mathf.Approximately(0f, value) ? number : number + "deg";
                case Unit.Gradian: return number + "grad";
                case Unit.Radian: return number + "rad";
                case Unit.Turn: return number + "turn";
                case Unit.None: return "none";
            }
            return number;
        }
    }

    public partial struct Rotate : IEquatable<Rotate>
    {
        Angle m_Angle;
        Vector3 m_Axis;
        bool m_IsNone;

        internal Rotate(Angle angle, Vector3 axis) { m_Angle = angle; m_Axis = axis; m_IsNone = false; }
        public Rotate(Angle angle) : this(angle, Vector3.forward) { }
        internal Rotate(Vector3 axis) : this(Angle.None(), axis) { }

        internal static Rotate Initial() => new Rotate(0f);
        public static Rotate None() { var r = Initial(); r.m_IsNone = true; return r; }

        public Angle angle { get => m_Angle; set => m_Angle = value; }
        internal Vector3 axis { get => m_Axis; set => m_Axis = value; }
        internal bool IsNone() => m_IsNone;
        internal Quaternion ToQuaternion() => Quaternion.AngleAxis(m_Angle.ToDegrees(), m_Axis);

        public static bool operator ==(Rotate lhs, Rotate rhs) => lhs.m_Angle == rhs.m_Angle && lhs.m_Axis == rhs.m_Axis && lhs.m_IsNone == rhs.m_IsNone;
        public static bool operator !=(Rotate lhs, Rotate rhs) => !(lhs == rhs);
        public bool Equals(Rotate other) => other == this;
        public override bool Equals(object obj) => obj is Rotate other && Equals(other);
        public override int GetHashCode() => (m_Angle.GetHashCode() * 397) ^ m_Axis.GetHashCode();
        public override string ToString() => m_Angle.ToString() + " " + m_Axis.ToString();
    }

    public partial struct StyleRotate : IStyleValue<Rotate>, IEquatable<StyleRotate>
    {
        Rotate m_Value;
        StyleKeyword m_Keyword;

        public Rotate value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : m_Keyword == StyleKeyword.None ? Rotate.None() : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleRotate(Rotate v) : this(v, StyleKeyword.Undefined) { }
        public StyleRotate(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleRotate(Rotate v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleRotate lhs, StyleRotate rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleRotate lhs, StyleRotate rhs) => !(lhs == rhs);
        public static implicit operator StyleRotate(StyleKeyword keyword) => new StyleRotate(keyword);
        public static implicit operator StyleRotate(Rotate v) => new StyleRotate(v);
        public bool Equals(StyleRotate other) => other == this;
        public override bool Equals(object obj) => obj is StyleRotate other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct Scale : IEquatable<Scale>
    {
        Vector3 m_Scale;
        bool m_IsNone;

        public Scale(Vector2 scale) { m_Scale = new Vector3(scale.x, scale.y, 1f); m_IsNone = false; }
        public Scale(Vector3 scale) { m_Scale = new Vector3(scale.x, scale.y, 1f); m_IsNone = false; }

        internal static Scale Initial() => new Scale(Vector3.one);
        public static Scale None() { var s = Initial(); s.m_IsNone = true; return s; }
        internal bool IsNone() => m_IsNone;

        public Vector3 value { get => m_Scale; set => m_Scale = value; }

        public static implicit operator Scale(Vector2 scale) => new Scale(scale);
        public static bool operator ==(Scale lhs, Scale rhs) => lhs.m_Scale == rhs.m_Scale;
        public static bool operator !=(Scale lhs, Scale rhs) => !(lhs == rhs);
        public bool Equals(Scale other) => other == this;
        public override bool Equals(object obj) => obj is Scale other && Equals(other);
        public override int GetHashCode() => m_Scale.GetHashCode() * 793;
        public override string ToString() => m_Scale.ToString();
    }

    public partial struct StyleScale : IStyleValue<Scale>, IEquatable<StyleScale>
    {
        Scale m_Value;
        StyleKeyword m_Keyword;

        public Scale value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : m_Keyword == StyleKeyword.None ? Scale.None() : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleScale(Scale v) : this(v, StyleKeyword.Undefined) { }
        public StyleScale(StyleKeyword keyword) : this(default, keyword) { }
        public StyleScale(Vector2 scale) : this(new Scale(scale)) { }
        internal StyleScale(Scale v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static implicit operator StyleScale(Vector2 scale) => new Scale(scale);
        public static bool operator ==(StyleScale lhs, StyleScale rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleScale lhs, StyleScale rhs) => !(lhs == rhs);
        public static implicit operator StyleScale(StyleKeyword keyword) => new StyleScale(keyword);
        public static implicit operator StyleScale(Scale v) => new StyleScale(v);
        public bool Equals(StyleScale other) => other == this;
        public override bool Equals(object obj) => obj is StyleScale other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct Translate : IEquatable<Translate>
    {
        Length m_X;
        Length m_Y;
        float m_Z;
        bool m_IsNone;

        public Translate(Length x, Length y, float z) { m_X = x; m_Y = y; m_Z = z; m_IsNone = false; }
        public Translate(Length x, Length y) : this(x, y, 0f) { }
        public static Translate None() { var t = new Translate(0f, 0f, 0f); t.m_IsNone = true; return t; }
        internal bool IsNone() => m_IsNone;

        public Length x { get => m_X; set => m_X = value; }
        public Length y { get => m_Y; set => m_Y = value; }
        public float z { get => m_Z; set => m_Z = value; }

        public static bool operator ==(Translate lhs, Translate rhs) => lhs.m_X == rhs.m_X && lhs.m_Y == rhs.m_Y && lhs.m_Z == rhs.m_Z && lhs.m_IsNone == rhs.m_IsNone;
        public static bool operator !=(Translate lhs, Translate rhs) => !(lhs == rhs);
        public bool Equals(Translate other) => other == this;
        public override bool Equals(object obj) => obj is Translate other && Equals(other);
        public override int GetHashCode() => (((m_X.GetHashCode() * 793) ^ (m_Y.GetHashCode() * 791)) ^ (m_Z.GetHashCode() * 571));
        public override string ToString() => m_X.ToString() + " " + m_Y.ToString() + " " + StyleValueStrings.Num(m_Z);
    }

    public partial struct StyleTranslate : IStyleValue<Translate>, IEquatable<StyleTranslate>
    {
        Translate m_Value;
        StyleKeyword m_Keyword;

        public Translate value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : m_Keyword == StyleKeyword.None ? Translate.None() : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleTranslate(Translate v) : this(v, StyleKeyword.Undefined) { }
        public StyleTranslate(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleTranslate(Translate v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleTranslate lhs, StyleTranslate rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleTranslate lhs, StyleTranslate rhs) => !(lhs == rhs);
        public static implicit operator StyleTranslate(StyleKeyword keyword) => new StyleTranslate(keyword);
        public static implicit operator StyleTranslate(Translate v) => new StyleTranslate(v);
        public bool Equals(StyleTranslate other) => other == this;
        public override bool Equals(object obj) => obj is StyleTranslate other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct TransformOrigin : IEquatable<TransformOrigin>
    {
        Length m_X;
        Length m_Y;
        float m_Z;

        public TransformOrigin(Length x, Length y, float z) { m_X = x; m_Y = y; m_Z = z; }
        public TransformOrigin(Length x, Length y) : this(x, y, 0f) { }
        internal TransformOrigin(Vector3 vector) : this(vector.x, vector.y, vector.z) { }

        public static TransformOrigin Initial() => new TransformOrigin(Length.Percent(50f), Length.Percent(50f), 0f);

        public Length x { get => m_X; set => m_X = value; }
        public Length y { get => m_Y; set => m_Y = value; }
        public float z { get => m_Z; set => m_Z = value; }

        public static bool operator ==(TransformOrigin lhs, TransformOrigin rhs) => lhs.m_X == rhs.m_X && lhs.m_Y == rhs.m_Y && lhs.m_Z == rhs.m_Z;
        public static bool operator !=(TransformOrigin lhs, TransformOrigin rhs) => !(lhs == rhs);
        public bool Equals(TransformOrigin other) => other == this;
        public override bool Equals(object obj) => obj is TransformOrigin other && Equals(other);
        public override int GetHashCode() => (((m_X.GetHashCode() * 793) ^ (m_Y.GetHashCode() * 791)) ^ (m_Z.GetHashCode() * 571));
        public override string ToString() => m_X.ToString() + " " + m_Y.ToString() + " " + StyleValueStrings.Num(m_Z);
    }

    public partial struct StyleTransformOrigin : IStyleValue<TransformOrigin>, IEquatable<StyleTransformOrigin>
    {
        TransformOrigin m_Value;
        StyleKeyword m_Keyword;

        public TransformOrigin value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : m_Keyword == StyleKeyword.Initial ? TransformOrigin.Initial() : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleTransformOrigin(TransformOrigin v) : this(v, StyleKeyword.Undefined) { }
        public StyleTransformOrigin(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleTransformOrigin(TransformOrigin v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleTransformOrigin lhs, StyleTransformOrigin rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleTransformOrigin lhs, StyleTransformOrigin rhs) => !(lhs == rhs);
        public static implicit operator StyleTransformOrigin(StyleKeyword keyword) => new StyleTransformOrigin(keyword);
        public static implicit operator StyleTransformOrigin(TransformOrigin v) => new StyleTransformOrigin(v);
        public bool Equals(StyleTransformOrigin other) => other == this;
        public override bool Equals(object obj) => obj is StyleTransformOrigin other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct BackgroundPosition : IEquatable<BackgroundPosition>
    {
        public BackgroundPositionKeyword keyword;
        public Length offset;

        public BackgroundPosition(BackgroundPositionKeyword keyword) { this.keyword = keyword; offset = new Length(0f); }
        public BackgroundPosition(BackgroundPositionKeyword keyword, Length offset) { this.keyword = keyword; this.offset = offset; }
        internal static BackgroundPosition Initial() => BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition();

        public static bool operator ==(BackgroundPosition style1, BackgroundPosition style2) => style1.keyword == style2.keyword && style1.offset == style2.offset;
        public static bool operator !=(BackgroundPosition style1, BackgroundPosition style2) => !(style1 == style2);
        public bool Equals(BackgroundPosition other) => other == this;
        public override bool Equals(object obj) => obj is BackgroundPosition other && Equals(other);
        public override int GetHashCode() => (keyword.GetHashCode() * 397) ^ offset.GetHashCode();
        public override string ToString() => $"(type:{keyword} x:{offset})";
    }

    public partial struct StyleBackgroundPosition : IStyleValue<BackgroundPosition>, IEquatable<StyleBackgroundPosition>
    {
        BackgroundPosition m_Value;
        StyleKeyword m_Keyword;

        public BackgroundPosition value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleBackgroundPosition(BackgroundPosition v) : this(v, StyleKeyword.Undefined) { }
        public StyleBackgroundPosition(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleBackgroundPosition(BackgroundPosition v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleBackgroundPosition lhs, StyleBackgroundPosition rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleBackgroundPosition lhs, StyleBackgroundPosition rhs) => !(lhs == rhs);
        public static implicit operator StyleBackgroundPosition(StyleKeyword keyword) => new StyleBackgroundPosition(keyword);
        public static implicit operator StyleBackgroundPosition(BackgroundPosition v) => new StyleBackgroundPosition(v);
        public bool Equals(StyleBackgroundPosition other) => other == this;
        public override bool Equals(object obj) => obj is StyleBackgroundPosition other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct BackgroundRepeat : IEquatable<BackgroundRepeat>
    {
        public Repeat x;
        public Repeat y;

        public BackgroundRepeat(Repeat repeatX, Repeat repeatY) { x = repeatX; y = repeatY; }
        internal static BackgroundRepeat Initial() => BackgroundPropertyHelper.ConvertScaleModeToBackgroundRepeat();

        public static bool operator ==(BackgroundRepeat style1, BackgroundRepeat style2) => style1.x == style2.x && style1.y == style2.y;
        public static bool operator !=(BackgroundRepeat style1, BackgroundRepeat style2) => !(style1 == style2);
        public bool Equals(BackgroundRepeat other) => other == this;
        public override bool Equals(object obj) => obj is BackgroundRepeat other && Equals(other);
        public override int GetHashCode() => (x.GetHashCode() * 397) ^ y.GetHashCode();
        public override string ToString() => $"(x:{x}, y:{y})";
    }

    public partial struct StyleBackgroundRepeat : IStyleValue<BackgroundRepeat>, IEquatable<StyleBackgroundRepeat>
    {
        BackgroundRepeat m_Value;
        StyleKeyword m_Keyword;

        public BackgroundRepeat value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleBackgroundRepeat(BackgroundRepeat v) : this(v, StyleKeyword.Undefined) { }
        public StyleBackgroundRepeat(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleBackgroundRepeat(BackgroundRepeat v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleBackgroundRepeat lhs, StyleBackgroundRepeat rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleBackgroundRepeat lhs, StyleBackgroundRepeat rhs) => !(lhs == rhs);
        public static implicit operator StyleBackgroundRepeat(StyleKeyword keyword) => new StyleBackgroundRepeat(keyword);
        public static implicit operator StyleBackgroundRepeat(BackgroundRepeat v) => new StyleBackgroundRepeat(v);
        public bool Equals(StyleBackgroundRepeat other) => other == this;
        public override bool Equals(object obj) => obj is StyleBackgroundRepeat other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct BackgroundSize : IEquatable<BackgroundSize>
    {
        BackgroundSizeType m_SizeType;
        Length m_X;
        Length m_Y;

        public BackgroundSize(Length sizeX, Length sizeY) { m_SizeType = BackgroundSizeType.Length; m_X = sizeX; m_Y = sizeY; }
        public BackgroundSize(BackgroundSizeType sizeType) { m_SizeType = sizeType; m_X = Length.Auto(); m_Y = Length.Auto(); }
        internal static BackgroundSize Initial() => BackgroundPropertyHelper.ConvertScaleModeToBackgroundSize();

        public BackgroundSizeType sizeType { get => m_SizeType; set { m_SizeType = value; m_X = new Length(0f); m_Y = new Length(0f); } }
        public Length x { get => m_X; set { m_X = value; m_SizeType = BackgroundSizeType.Length; } }
        public Length y { get => m_Y; set { m_Y = value; m_SizeType = BackgroundSizeType.Length; } }

        public static bool operator ==(BackgroundSize style1, BackgroundSize style2) => style1.m_SizeType == style2.m_SizeType && style1.m_X == style2.m_X && style1.m_Y == style2.m_Y;
        public static bool operator !=(BackgroundSize style1, BackgroundSize style2) => !(style1 == style2);
        public bool Equals(BackgroundSize other) => other == this;
        public override bool Equals(object obj) => obj is BackgroundSize other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_SizeType, m_X, m_Y);
        public override string ToString() => $"(sizeType:{sizeType} x:{x}, y:{y})";
    }

    public partial struct StyleBackgroundSize : IStyleValue<BackgroundSize>, IEquatable<StyleBackgroundSize>
    {
        BackgroundSize m_Value;
        StyleKeyword m_Keyword;

        public BackgroundSize value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : default; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleBackgroundSize(BackgroundSize v) : this(v, StyleKeyword.Undefined) { }
        public StyleBackgroundSize(StyleKeyword keyword) : this(default, keyword) { }
        internal StyleBackgroundSize(BackgroundSize v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleBackgroundSize lhs, StyleBackgroundSize rhs) => lhs.m_Keyword == rhs.m_Keyword && lhs.m_Value == rhs.m_Value;
        public static bool operator !=(StyleBackgroundSize lhs, StyleBackgroundSize rhs) => !(lhs == rhs);
        public static implicit operator StyleBackgroundSize(StyleKeyword keyword) => new StyleBackgroundSize(keyword);
        public static implicit operator StyleBackgroundSize(BackgroundSize v) => new StyleBackgroundSize(v);
        public bool Equals(StyleBackgroundSize other) => other == this;
        public override bool Equals(object obj) => obj is StyleBackgroundSize other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Keyword;
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct TimeValue : IEquatable<TimeValue>
    {
        float m_Value;
        TimeUnit m_Unit;

        public float value { get => m_Value; set => m_Value = value; }
        public TimeUnit unit { get => m_Unit; set => m_Unit = value; }

        public TimeValue(float value) : this(value, TimeUnit.Second) { }
        public TimeValue(float value, TimeUnit unit) { m_Value = value; m_Unit = unit; }

        public static implicit operator TimeValue(float value) => new TimeValue(value, TimeUnit.Second);
        public static bool operator ==(TimeValue lhs, TimeValue rhs) => lhs.m_Value == rhs.m_Value && lhs.m_Unit == rhs.m_Unit;
        public static bool operator !=(TimeValue lhs, TimeValue rhs) => !(lhs == rhs);
        public bool Equals(TimeValue other) => other == this;
        public override bool Equals(object obj) => obj is TimeValue other && Equals(other);
        public override int GetHashCode() => (m_Value.GetHashCode() * 397) ^ (int)m_Unit;
        public override string ToString() => StyleValueStrings.Num(value) + (unit == TimeUnit.Second ? "s" : "ms");
    }

    public partial struct StyleList<T> : IStyleValue<List<T>>, IEquatable<StyleList<T>>
    {
        List<T> m_Value;
        StyleKeyword m_Keyword;

        public List<T> value { get => m_Keyword == StyleKeyword.Undefined ? m_Value : null; set { m_Value = value; m_Keyword = StyleKeyword.Undefined; } }
        public StyleKeyword keyword { get => m_Keyword; set => m_Keyword = value; }

        public StyleList(List<T> v) : this(v, StyleKeyword.Undefined) { }
        public StyleList(StyleKeyword keyword) : this(null, keyword) { }
        internal StyleList(List<T> v, StyleKeyword keyword) { m_Keyword = keyword; m_Value = v; }

        public static bool operator ==(StyleList<T> lhs, StyleList<T> rhs)
        {
            if (lhs.m_Keyword != rhs.m_Keyword) return false;
            var a = lhs.m_Value;
            var b = rhs.m_Value;
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
            return true;
        }
        public static bool operator !=(StyleList<T> lhs, StyleList<T> rhs) => !(lhs == rhs);
        public static implicit operator StyleList<T>(StyleKeyword keyword) => new StyleList<T>(keyword);
        public static implicit operator StyleList<T>(List<T> v) => new StyleList<T>(v);
        public bool Equals(StyleList<T> other) => other == this;
        public override bool Equals(object obj) => obj is StyleList<T> other && Equals(other);
        public override int GetHashCode()
        {
            int h = 0;
            if (m_Value != null && m_Value.Count > 0)
            {
                h = EqualityComparer<T>.Default.GetHashCode(m_Value[0]);
                for (int i = 1; i < m_Value.Count; i++) h = (h * 397) ^ EqualityComparer<T>.Default.GetHashCode(m_Value[i]);
            }
            return (h * 397) ^ (int)m_Keyword;
        }
        public override string ToString() => StyleValueStrings.Of(this);
    }

    public partial struct StylePropertyName : IEquatable<StylePropertyName>
    {
        internal StyleSheets.StylePropertyId id { get; }
        internal string name { get; }

        public StylePropertyName(string name)
        {
            id = Unbound.UIElements.StyleProperties.IdOf(name);
            this.name = id != StyleSheets.StylePropertyId.Unknown || !string.IsNullOrEmpty(name) ? name : null;
        }

        public static bool IsNullOrEmpty(StylePropertyName propertyName) => propertyName.id == StyleSheets.StylePropertyId.Unknown;
        public static bool operator ==(StylePropertyName lhs, StylePropertyName rhs) => lhs.id == rhs.id;
        public static bool operator !=(StylePropertyName lhs, StylePropertyName rhs) => lhs.id != rhs.id;
        public static implicit operator StylePropertyName(string name) => new StylePropertyName(name);
        public override int GetHashCode() => (int)id;
        public override bool Equals(object other) => other is StylePropertyName n && Equals(n);
        public bool Equals(StylePropertyName other) => this == other;
        public override string ToString() => name;
    }

    public partial struct EasingFunction : IEquatable<EasingFunction>
    {
        EasingMode m_Mode;

        public EasingMode mode { get => m_Mode; set => m_Mode = value; }
        public EasingFunction(EasingMode mode) { m_Mode = mode; }

        public static implicit operator EasingFunction(EasingMode easingMode) => new EasingFunction(easingMode);
        public static bool operator ==(EasingFunction lhs, EasingFunction rhs) => lhs.m_Mode == rhs.m_Mode;
        public static bool operator !=(EasingFunction lhs, EasingFunction rhs) => !(lhs == rhs);
        public bool Equals(EasingFunction other) => other == this;
        public override bool Equals(object obj) => obj is EasingFunction other && Equals(other);
        public override string ToString() => m_Mode.ToString();
        public override int GetHashCode() => (int)m_Mode;
    }
}
