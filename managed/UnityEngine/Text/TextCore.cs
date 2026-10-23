using System;
using UnityEngine.Serialization;

// The data types of Unity's TextCore font engine. TextMeshPro font assets serialize them with these
// exact field names; the font engine's rasterizing functions (dynamic atlases) are separate.

namespace UnityEngine.TextCore
{
    [Serializable]
    public partial struct FaceInfo
    {
        [SerializeField] int m_FaceIndex;
        [SerializeField] string m_FamilyName;
        [SerializeField] string m_StyleName;
        [SerializeField] float m_PointSize;
        [SerializeField] float m_Scale;
        [SerializeField] int m_UnitsPerEM;
        [SerializeField] float m_LineHeight;
        [SerializeField] float m_AscentLine;
        [SerializeField] float m_CapLine;
        [SerializeField] float m_MeanLine;
        [SerializeField] float m_Baseline;
        [SerializeField] float m_DescentLine;
        [SerializeField] float m_SuperscriptOffset;
        [SerializeField] float m_SuperscriptSize;
        [SerializeField] float m_SubscriptOffset;
        [SerializeField] float m_SubscriptSize;
        [SerializeField] float m_UnderlineOffset;
        [SerializeField] float m_UnderlineThickness;
        [SerializeField] float m_StrikethroughOffset;
        [SerializeField] float m_StrikethroughThickness;
        [SerializeField] float m_TabWidth;

        public int faceIndex { get => m_FaceIndex; set => m_FaceIndex = value; }
        public string familyName { get => m_FamilyName; set => m_FamilyName = value; }
        public string styleName { get => m_StyleName; set => m_StyleName = value; }
        public float pointSize { get => m_PointSize; set => m_PointSize = value; }
        public float scale { get => m_Scale; set => m_Scale = value; }
        public int unitsPerEM { get => m_UnitsPerEM; set => m_UnitsPerEM = value; }
        public float lineHeight { get => m_LineHeight; set => m_LineHeight = value; }
        public float ascentLine { get => m_AscentLine; set => m_AscentLine = value; }
        public float capLine { get => m_CapLine; set => m_CapLine = value; }
        public float meanLine { get => m_MeanLine; set => m_MeanLine = value; }
        public float baseline { get => m_Baseline; set => m_Baseline = value; }
        public float descentLine { get => m_DescentLine; set => m_DescentLine = value; }
        public float superscriptOffset { get => m_SuperscriptOffset; set => m_SuperscriptOffset = value; }
        public float superscriptSize { get => m_SuperscriptSize; set => m_SuperscriptSize = value; }
        public float subscriptOffset { get => m_SubscriptOffset; set => m_SubscriptOffset = value; }
        public float subscriptSize { get => m_SubscriptSize; set => m_SubscriptSize = value; }
        public float underlineOffset { get => m_UnderlineOffset; set => m_UnderlineOffset = value; }
        public float underlineThickness { get => m_UnderlineThickness; set => m_UnderlineThickness = value; }
        public float strikethroughOffset { get => m_StrikethroughOffset; set => m_StrikethroughOffset = value; }
        public float strikethroughThickness { get => m_StrikethroughThickness; set => m_StrikethroughThickness = value; }
        public float tabWidth { get => m_TabWidth; set => m_TabWidth = value; }

        public bool Compare(FaceInfo other) =>
            m_FamilyName == other.m_FamilyName && m_StyleName == other.m_StyleName && m_FaceIndex == other.m_FaceIndex &&
            m_PointSize == other.m_PointSize && m_Scale == other.m_Scale && m_UnitsPerEM == other.m_UnitsPerEM &&
            m_LineHeight == other.m_LineHeight && m_AscentLine == other.m_AscentLine && m_CapLine == other.m_CapLine &&
            m_MeanLine == other.m_MeanLine && m_Baseline == other.m_Baseline && m_DescentLine == other.m_DescentLine &&
            m_SuperscriptOffset == other.m_SuperscriptOffset && m_SuperscriptSize == other.m_SuperscriptSize &&
            m_SubscriptOffset == other.m_SubscriptOffset && m_SubscriptSize == other.m_SubscriptSize &&
            m_UnderlineOffset == other.m_UnderlineOffset && m_UnderlineThickness == other.m_UnderlineThickness &&
            m_StrikethroughOffset == other.m_StrikethroughOffset && m_StrikethroughThickness == other.m_StrikethroughThickness &&
            m_TabWidth == other.m_TabWidth;
    }

    [Serializable]
    public partial struct GlyphRect : IEquatable<GlyphRect>
    {
        [SerializeField] int m_X;
        [SerializeField] int m_Y;
        [SerializeField] int m_Width;
        [SerializeField] int m_Height;

        static readonly GlyphRect s_ZeroGlyphRect = new GlyphRect(0, 0, 0, 0);

        public GlyphRect(int x, int y, int width, int height)
        {
            m_X = x;
            m_Y = y;
            m_Width = width;
            m_Height = height;
        }

        public GlyphRect(Rect rect) : this((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height) { }

        public int x { get => m_X; set => m_X = value; }
        public int y { get => m_Y; set => m_Y = value; }
        public int width { get => m_Width; set => m_Width = value; }
        public int height { get => m_Height; set => m_Height = value; }
        public static GlyphRect zero => s_ZeroGlyphRect;

        public override int GetHashCode() => HashCode.Combine(m_X, m_Y, m_Width, m_Height);
        public override bool Equals(object obj) => obj is GlyphRect other && Equals(other);
        public bool Equals(GlyphRect other) => m_X == other.m_X && m_Y == other.m_Y && m_Width == other.m_Width && m_Height == other.m_Height;
        public static bool operator ==(GlyphRect lhs, GlyphRect rhs) => lhs.Equals(rhs);
        public static bool operator !=(GlyphRect lhs, GlyphRect rhs) => !lhs.Equals(rhs);
    }

    [Serializable]
    public partial struct GlyphMetrics : IEquatable<GlyphMetrics>
    {
        [SerializeField] float m_Width;
        [SerializeField] float m_Height;
        [SerializeField] float m_HorizontalBearingX;
        [SerializeField] float m_HorizontalBearingY;
        [SerializeField] float m_HorizontalAdvance;

        public GlyphMetrics(float width, float height, float bearingX, float bearingY, float advance)
        {
            m_Width = width;
            m_Height = height;
            m_HorizontalBearingX = bearingX;
            m_HorizontalBearingY = bearingY;
            m_HorizontalAdvance = advance;
        }

        public float width { get => m_Width; set => m_Width = value; }
        public float height { get => m_Height; set => m_Height = value; }
        public float horizontalBearingX { get => m_HorizontalBearingX; set => m_HorizontalBearingX = value; }
        public float horizontalBearingY { get => m_HorizontalBearingY; set => m_HorizontalBearingY = value; }
        public float horizontalAdvance { get => m_HorizontalAdvance; set => m_HorizontalAdvance = value; }

        public override int GetHashCode() => HashCode.Combine(m_Width, m_Height, m_HorizontalBearingX, m_HorizontalBearingY, m_HorizontalAdvance);
        public override bool Equals(object obj) => obj is GlyphMetrics other && Equals(other);
        public bool Equals(GlyphMetrics other) => m_Width == other.m_Width && m_Height == other.m_Height && m_HorizontalBearingX == other.m_HorizontalBearingX && m_HorizontalBearingY == other.m_HorizontalBearingY && m_HorizontalAdvance == other.m_HorizontalAdvance;
        public static bool operator ==(GlyphMetrics lhs, GlyphMetrics rhs) => lhs.Equals(rhs);
        public static bool operator !=(GlyphMetrics lhs, GlyphMetrics rhs) => !lhs.Equals(rhs);
    }

    [Serializable]
    public partial class Glyph
    {
        [SerializeField] uint m_Index;
        [SerializeField] GlyphMetrics m_Metrics;
        [SerializeField] GlyphRect m_GlyphRect;
        [SerializeField] float m_Scale = 1f;
        [SerializeField] int m_AtlasIndex;
        [SerializeField] GlyphClassDefinitionType m_ClassDefinitionType;

        public Glyph() { }

        public Glyph(Glyph glyph)
        {
            m_Index = glyph.m_Index;
            m_Metrics = glyph.m_Metrics;
            m_GlyphRect = glyph.m_GlyphRect;
            m_Scale = glyph.m_Scale;
            m_AtlasIndex = glyph.m_AtlasIndex;
            m_ClassDefinitionType = glyph.m_ClassDefinitionType;
        }

        public Glyph(uint index, GlyphMetrics metrics, GlyphRect glyphRect) : this(index, metrics, glyphRect, 1f, 0) { }

        public Glyph(uint index, GlyphMetrics metrics, GlyphRect glyphRect, float scale, int atlasIndex)
        {
            m_Index = index;
            m_Metrics = metrics;
            m_GlyphRect = glyphRect;
            m_Scale = scale;
            m_AtlasIndex = atlasIndex;
        }

        public uint index { get => m_Index; set => m_Index = value; }
        public GlyphMetrics metrics { get => m_Metrics; set => m_Metrics = value; }
        public GlyphRect glyphRect { get => m_GlyphRect; set => m_GlyphRect = value; }
        public float scale { get => m_Scale; set => m_Scale = value; }
        public int atlasIndex { get => m_AtlasIndex; set => m_AtlasIndex = value; }
        public GlyphClassDefinitionType classDefinitionType { get => m_ClassDefinitionType; set => m_ClassDefinitionType = value; }

        public bool Compare(Glyph other) => other != null && m_Index == other.m_Index && m_Metrics == other.m_Metrics && m_GlyphRect == other.m_GlyphRect && m_Scale == other.m_Scale && m_AtlasIndex == other.m_AtlasIndex;
    }

    public enum GlyphClassDefinitionType
    {
        Undefined = 0,
        Base = 1,
        Ligature = 2,
        Mark = 3,
        Component = 4,
    }
}

namespace UnityEngine.TextCore.LowLevel
{
    [Serializable]
    public partial struct GlyphValueRecord : IEquatable<GlyphValueRecord>
    {
        [SerializeField] float m_XPlacement;
        [SerializeField] float m_YPlacement;
        [SerializeField] float m_XAdvance;
        [SerializeField] float m_YAdvance;

        public GlyphValueRecord(float xPlacement, float yPlacement, float xAdvance, float yAdvance)
        {
            m_XPlacement = xPlacement;
            m_YPlacement = yPlacement;
            m_XAdvance = xAdvance;
            m_YAdvance = yAdvance;
        }

        public float xPlacement { get => m_XPlacement; set => m_XPlacement = value; }
        public float yPlacement { get => m_YPlacement; set => m_YPlacement = value; }
        public float xAdvance { get => m_XAdvance; set => m_XAdvance = value; }
        public float yAdvance { get => m_YAdvance; set => m_YAdvance = value; }

        public static GlyphValueRecord operator +(GlyphValueRecord a, GlyphValueRecord b) =>
            new GlyphValueRecord(a.m_XPlacement + b.m_XPlacement, a.m_YPlacement + b.m_YPlacement, a.m_XAdvance + b.m_XAdvance, a.m_YAdvance + b.m_YAdvance);

        public static GlyphValueRecord operator *(GlyphValueRecord a, float emScale) =>
            new GlyphValueRecord(a.m_XPlacement * emScale, a.m_YPlacement * emScale, a.m_XAdvance * emScale, a.m_YAdvance * emScale);

        public override int GetHashCode() => HashCode.Combine(m_XPlacement, m_YPlacement, m_XAdvance, m_YAdvance);
        public override bool Equals(object obj) => obj is GlyphValueRecord other && Equals(other);
        public bool Equals(GlyphValueRecord other) => m_XPlacement == other.m_XPlacement && m_YPlacement == other.m_YPlacement && m_XAdvance == other.m_XAdvance && m_YAdvance == other.m_YAdvance;
        public static bool operator ==(GlyphValueRecord lhs, GlyphValueRecord rhs) => lhs.Equals(rhs);
        public static bool operator !=(GlyphValueRecord lhs, GlyphValueRecord rhs) => !lhs.Equals(rhs);
    }

    [Serializable]
    public partial struct GlyphAdjustmentRecord : IEquatable<GlyphAdjustmentRecord>
    {
        [SerializeField] uint m_GlyphIndex;
        [SerializeField] GlyphValueRecord m_GlyphValueRecord;

        public GlyphAdjustmentRecord(uint glyphIndex, GlyphValueRecord glyphValueRecord)
        {
            m_GlyphIndex = glyphIndex;
            m_GlyphValueRecord = glyphValueRecord;
        }

        public uint glyphIndex { get => m_GlyphIndex; set => m_GlyphIndex = value; }
        public GlyphValueRecord glyphValueRecord { get => m_GlyphValueRecord; set => m_GlyphValueRecord = value; }

        public override int GetHashCode() => HashCode.Combine(m_GlyphIndex, m_GlyphValueRecord);
        public override bool Equals(object obj) => obj is GlyphAdjustmentRecord other && Equals(other);
        public bool Equals(GlyphAdjustmentRecord other) => m_GlyphIndex == other.m_GlyphIndex && m_GlyphValueRecord == other.m_GlyphValueRecord;
        public static bool operator ==(GlyphAdjustmentRecord lhs, GlyphAdjustmentRecord rhs) => lhs.Equals(rhs);
        public static bool operator !=(GlyphAdjustmentRecord lhs, GlyphAdjustmentRecord rhs) => !lhs.Equals(rhs);
    }

    [Serializable]
    public partial struct GlyphPairAdjustmentRecord : IEquatable<GlyphPairAdjustmentRecord>
    {
        [SerializeField] GlyphAdjustmentRecord m_FirstAdjustmentRecord;
        [SerializeField] GlyphAdjustmentRecord m_SecondAdjustmentRecord;
        [SerializeField] FontFeatureLookupFlags m_FeatureLookupFlags;

        public GlyphPairAdjustmentRecord(GlyphAdjustmentRecord firstAdjustmentRecord, GlyphAdjustmentRecord secondAdjustmentRecord)
        {
            m_FirstAdjustmentRecord = firstAdjustmentRecord;
            m_SecondAdjustmentRecord = secondAdjustmentRecord;
            m_FeatureLookupFlags = FontFeatureLookupFlags.None;
        }

        public GlyphAdjustmentRecord firstAdjustmentRecord { get => m_FirstAdjustmentRecord; set => m_FirstAdjustmentRecord = value; }
        public GlyphAdjustmentRecord secondAdjustmentRecord { get => m_SecondAdjustmentRecord; set => m_SecondAdjustmentRecord = value; }
        public FontFeatureLookupFlags featureLookupFlags { get => m_FeatureLookupFlags; set => m_FeatureLookupFlags = value; }

        public override int GetHashCode() => HashCode.Combine(m_FirstAdjustmentRecord, m_SecondAdjustmentRecord);
        public override bool Equals(object obj) => obj is GlyphPairAdjustmentRecord other && Equals(other);
        public bool Equals(GlyphPairAdjustmentRecord other) => m_FirstAdjustmentRecord == other.m_FirstAdjustmentRecord && m_SecondAdjustmentRecord == other.m_SecondAdjustmentRecord;
        public static bool operator ==(GlyphPairAdjustmentRecord lhs, GlyphPairAdjustmentRecord rhs) => lhs.Equals(rhs);
        public static bool operator !=(GlyphPairAdjustmentRecord lhs, GlyphPairAdjustmentRecord rhs) => !lhs.Equals(rhs);
    }

    [Flags]
    public enum FontFeatureLookupFlags
    {
        None = 0,
        IgnoreLigatures = 4,
        IgnoreSpacingAdjustments = 256,
    }
}
