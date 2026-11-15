using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine.TextCore.LowLevel
{
    [Serializable]
    internal partial struct GlyphAnchorPoint
    {
        [SerializeField] float m_XCoordinate, m_YCoordinate;
        public float xCoordinate { get => m_XCoordinate; set => m_XCoordinate = value; }
        public float yCoordinate { get => m_YCoordinate; set => m_YCoordinate = value; }
    }
    [Serializable]
    internal partial struct MarkPositionAdjustment
    {
        [SerializeField] float m_XPositionAdjustment, m_YPositionAdjustment;
        public MarkPositionAdjustment(float x, float y) { m_XPositionAdjustment = x; m_YPositionAdjustment = y; }
        public float xPositionAdjustment { get => m_XPositionAdjustment; set => m_XPositionAdjustment = value; }
        public float yPositionAdjustment { get => m_YPositionAdjustment; set => m_YPositionAdjustment = value; }
    }
    [Serializable]
    internal partial struct MarkToBaseAdjustmentRecord
    {
        [SerializeField] uint m_BaseGlyphID;
        [SerializeField] GlyphAnchorPoint m_BaseGlyphAnchorPoint;
        [SerializeField] uint m_MarkGlyphID;
        [SerializeField] MarkPositionAdjustment m_MarkPositionAdjustment;
        public uint baseGlyphID { get => m_BaseGlyphID; set => m_BaseGlyphID = value; }
        public GlyphAnchorPoint baseGlyphAnchorPoint { get => m_BaseGlyphAnchorPoint; set => m_BaseGlyphAnchorPoint = value; }
        public uint markGlyphID { get => m_MarkGlyphID; set => m_MarkGlyphID = value; }
        public MarkPositionAdjustment markPositionAdjustment { get => m_MarkPositionAdjustment; set => m_MarkPositionAdjustment = value; }
    }
    [Serializable]
    internal partial struct MarkToMarkAdjustmentRecord
    {
        [SerializeField] uint m_BaseMarkGlyphID;
        [SerializeField] GlyphAnchorPoint m_BaseMarkGlyphAnchorPoint;
        [SerializeField] uint m_CombiningMarkGlyphID;
        [SerializeField] MarkPositionAdjustment m_CombiningMarkPositionAdjustment;
        public uint baseMarkGlyphID { get => m_BaseMarkGlyphID; set => m_BaseMarkGlyphID = value; }
        public GlyphAnchorPoint baseMarkGlyphAnchorPoint { get => m_BaseMarkGlyphAnchorPoint; set => m_BaseMarkGlyphAnchorPoint = value; }
        public uint combiningMarkGlyphID { get => m_CombiningMarkGlyphID; set => m_CombiningMarkGlyphID = value; }
        public MarkPositionAdjustment combiningMarkPositionAdjustment { get => m_CombiningMarkPositionAdjustment; set => m_CombiningMarkPositionAdjustment = value; }
    }

    public sealed partial class FontEngine
    {
        sealed class MarkJson
        {
            public uint Base { get; set; }
            public int[] BaseAnchor { get; set; }
            public uint Mark { get; set; }
            public int[] MarkAnchor { get; set; }
        }
        static MarkToBaseAdjustmentRecord[] s_MarkToBaseRecords;
        static MarkToMarkAdjustmentRecord[] s_MarkToMarkRecords;
        static T[] Marks<T>(IEnumerable<uint> glyphs, int? lookup, bool markToMark, bool zeroTerminated,
            bool exact, ref T[] cache, Func<MarkJson, T> convert)
        {
            if (s_Face == null) return exact ? Array.Empty<T>() : null;
            var request = JsonSerializer.Serialize(new { handle = s_Face.Handle, glyphs, lookup, markToMark, zeroTerminated, preserveMissing = Compat.FontMarkNullAnchorMeasured });
            var source = JsonSerializer.Deserialize<MarkJson[]>(Native.Call("font-marks", request), s_Json);
            var result = FeatureBuffer(source.Length, ref cache, exact);
            if (result == null) return null;
            for (int i = 0; i < source.Length; i++) result[i] = convert(source[i]);
            return result;
        }
        static MarkToBaseAdjustmentRecord BaseRecord(MarkJson r) => new MarkToBaseAdjustmentRecord {
            baseGlyphID = r.Base, markGlyphID = r.Mark,
            baseGlyphAnchorPoint = new GlyphAnchorPoint { xCoordinate = r.BaseAnchor[0], yCoordinate = r.BaseAnchor[1] },
            markPositionAdjustment = new MarkPositionAdjustment(r.MarkAnchor[0], r.MarkAnchor[1]) };
        static MarkToMarkAdjustmentRecord MarkRecord(MarkJson r) => new MarkToMarkAdjustmentRecord {
            baseMarkGlyphID = r.Base, combiningMarkGlyphID = r.Mark,
            baseMarkGlyphAnchorPoint = new GlyphAnchorPoint { xCoordinate = r.BaseAnchor[0], yCoordinate = r.BaseAnchor[1] },
            combiningMarkPositionAdjustment = new MarkPositionAdjustment(r.MarkAnchor[0], r.MarkAnchor[1]) };
        internal static MarkToBaseAdjustmentRecord[] GetAllMarkToBaseAdjustmentRecords() => Marks(null, null, false, false, true, ref s_MarkToBaseRecords, BaseRecord);
        internal static MarkToBaseAdjustmentRecord[] GetMarkToBaseAdjustmentRecords(uint baseGlyphIndex) => Marks(new[] { baseGlyphIndex }, null, false, false, true, ref s_MarkToBaseRecords, BaseRecord);
        internal static MarkToBaseAdjustmentRecord[] GetMarkToBaseAdjustmentRecords(List<uint> glyphIndexes) => Marks(FeatureGlyphList(glyphIndexes), null, false, true, false, ref s_MarkToBaseRecords, BaseRecord);
        internal static MarkToBaseAdjustmentRecord[] GetMarkToBaseAdjustmentRecords(int lookupIndex, List<uint> glyphIndexes) => Marks(FeatureGlyphList(glyphIndexes), lookupIndex, false, true, false, ref s_MarkToBaseRecords, BaseRecord);
        internal static MarkToMarkAdjustmentRecord[] GetAllMarkToMarkAdjustmentRecords() => Marks(null, null, true, false, true, ref s_MarkToMarkRecords, MarkRecord);
        internal static MarkToMarkAdjustmentRecord[] GetMarkToMarkAdjustmentRecords(uint baseMarkGlyphIndex) => Marks(new[] { baseMarkGlyphIndex }, null, true, false, true, ref s_MarkToMarkRecords, MarkRecord);
        internal static MarkToMarkAdjustmentRecord[] GetMarkToMarkAdjustmentRecords(List<uint> glyphIndexes) => Marks(FeatureGlyphList(glyphIndexes), null, true, true, false, ref s_MarkToMarkRecords, MarkRecord);
        internal static MarkToMarkAdjustmentRecord[] GetMarkToMarkAdjustmentRecords(int lookupIndex, List<uint> glyphIndexes) => Marks(FeatureGlyphList(glyphIndexes), lookupIndex, true, true, false, ref s_MarkToMarkRecords, MarkRecord);
        static GlyphAnchorPoint Scale(GlyphAnchorPoint a, float scale) => new GlyphAnchorPoint { xCoordinate = a.xCoordinate * scale, yCoordinate = a.yCoordinate * scale };
        static MarkPositionAdjustment Scale(MarkPositionAdjustment a, float scale) => new MarkPositionAdjustment(a.xPositionAdjustment * scale, a.yPositionAdjustment * scale);
        internal static MarkToBaseAdjustmentRecord GetMarkToBaseAdjustmentRecord(uint baseGlyphIndex, uint markGlyphIndex)
        {
            foreach (var r in GetMarkToBaseAdjustmentRecords(baseGlyphIndex)) if (r.markGlyphID == markGlyphIndex)
                return new MarkToBaseAdjustmentRecord { baseGlyphID = r.baseGlyphID, markGlyphID = r.markGlyphID, baseGlyphAnchorPoint = Scale(r.baseGlyphAnchorPoint, PixelScale), markPositionAdjustment = Scale(r.markPositionAdjustment, PixelScale) };
            return default;
        }
        internal static MarkToMarkAdjustmentRecord GetMarkToMarkAdjustmentRecord(uint baseMarkGlyphIndex, uint combiningMarkGlyphIndex)
        {
            foreach (var r in GetMarkToMarkAdjustmentRecords(baseMarkGlyphIndex)) if (r.combiningMarkGlyphID == combiningMarkGlyphIndex)
                return new MarkToMarkAdjustmentRecord { baseMarkGlyphID = r.baseMarkGlyphID, combiningMarkGlyphID = r.combiningMarkGlyphID, baseMarkGlyphAnchorPoint = Scale(r.baseMarkGlyphAnchorPoint, PixelScale), combiningMarkPositionAdjustment = Scale(r.combiningMarkPositionAdjustment, PixelScale) };
            return default;
        }
    }
}
