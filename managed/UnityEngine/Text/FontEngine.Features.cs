using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine.TextCore.LowLevel
{
    [Serializable]
    internal partial struct LigatureSubstitutionRecord : IEquatable<LigatureSubstitutionRecord>
    {
        [SerializeField] uint[] m_ComponentGlyphIDs;
        [SerializeField] uint m_LigatureGlyphID;
        public uint[] componentGlyphIDs { get => m_ComponentGlyphIDs; set => m_ComponentGlyphIDs = value; }
        public uint ligatureGlyphID { get => m_LigatureGlyphID; set => m_LigatureGlyphID = value; }
        public bool Equals(LigatureSubstitutionRecord other)
        {
            if (m_LigatureGlyphID != other.m_LigatureGlyphID) return false;
            if (m_ComponentGlyphIDs == null || other.m_ComponentGlyphIDs == null) return m_ComponentGlyphIDs == other.m_ComponentGlyphIDs;
            if (m_ComponentGlyphIDs.Length != other.m_ComponentGlyphIDs.Length) return false;
            for (int i = 0; i < m_ComponentGlyphIDs.Length; i++)
                if (m_ComponentGlyphIDs[i] != other.m_ComponentGlyphIDs[i]) return false;
            return true;
        }
        public override bool Equals(object obj) => obj is LigatureSubstitutionRecord record && Equals(record);
        public override int GetHashCode() => m_ComponentGlyphIDs.GetHashCode();
        public static bool operator ==(LigatureSubstitutionRecord lhs, LigatureSubstitutionRecord rhs) => lhs.Equals(rhs);
        public static bool operator !=(LigatureSubstitutionRecord lhs, LigatureSubstitutionRecord rhs) => !lhs.Equals(rhs);
    }

    public sealed partial class FontEngine
    {
        sealed class LigatureJson
        {
            public uint[] Components { get; set; }
            public uint Glyph { get; set; }
        }
        static LigatureSubstitutionRecord[] s_LigatureRecords;

        static LigatureSubstitutionRecord[] Ligatures(IEnumerable<uint> glyphs, int? lookup, bool all)
        {
            if (lookup.HasValue && lookup.Value != -1 && Compat.FontFeatureLookupIndexQueriesMeasured) return null;
            if (s_Face == null) return all ? Array.Empty<LigatureSubstitutionRecord>() : null;
            var request = new { handle = s_Face.Handle, glyphs, lookup = lookup == -1 ? null : lookup };
            return FeatureRecords<LigatureJson, LigatureSubstitutionRecord>("font-ligatures", request, all, ref s_LigatureRecords, LigatureRecord);
        }
        static LigatureSubstitutionRecord LigatureRecord(LigatureJson r) => new LigatureSubstitutionRecord { componentGlyphIDs = r.Components, ligatureGlyphID = r.Glyph };

        static T[] FeatureRecords<TJson, T>(string operation, object request, bool exact, ref T[] cache, Func<TJson, T> convert)
        {
            var source = JsonSerializer.Deserialize<TJson[]>(Native.Call(operation, JsonSerializer.Serialize(request)), s_Json);
            var result = FeatureBuffer(source.Length, ref cache, exact);
            if (result == null) return null;
            for (int i = 0; i < source.Length; i++) result[i] = convert(source[i]);
            return result;
        }

        internal static LigatureSubstitutionRecord[] GetAllLigatureSubstitutionRecords() => Ligatures(null, null, true);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(uint glyphIndex) => Ligatures(new[] { glyphIndex }, null, false);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(List<uint> glyphIndexes) => Ligatures(FeatureGlyphList(glyphIndexes), null, false);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(int lookupIndex, uint glyphIndex) => Ligatures(new[] { glyphIndex }, lookupIndex, false);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(int lookupIndex, List<uint> glyphIndexes) => Ligatures(FeatureGlyphList(glyphIndexes), lookupIndex, false);

        sealed class AdjustmentJson
        {
            public uint Glyph { get; set; }
            public int[] Values { get; set; }
        }
        static GlyphAdjustmentRecord[] s_SingleAdjustmentRecords;
        static GlyphAdjustmentRecord Adjustment(AdjustmentJson r) => new GlyphAdjustmentRecord(r.Glyph,
            new GlyphValueRecord(r.Values[0], r.Values[1], r.Values[2], r.Values[3]));
        static GlyphAdjustmentRecord[] SingleAdjustments(int lookup, IEnumerable<uint> glyphs)
        {
            if (s_Face == null) return null;
            return FeatureRecords<AdjustmentJson, GlyphAdjustmentRecord>("font-single-adjustments", new { handle = s_Face.Handle, glyphs, lookup }, false, ref s_SingleAdjustmentRecords, Adjustment);
        }
        internal static GlyphAdjustmentRecord[] GetSingleAdjustmentRecords(int lookupIndex, uint glyphIndex) => SingleAdjustments(lookupIndex, new[] { glyphIndex });
        internal static GlyphAdjustmentRecord[] GetSingleAdjustmentRecords(int lookupIndex, List<uint> glyphIndexes) => SingleAdjustments(lookupIndex, FeatureGlyphList(glyphIndexes));

        static T[] FeatureBuffer<T>(int count, ref T[] cache, bool exact)
        {
            if (exact) return new T[count];
            if (count == 0) return null;
            if (cache == null || cache.Length <= count)
            {
                int capacity = 1;
                while (capacity <= count) capacity *= 2;
                cache = new T[capacity];
            }
            cache[count] = default;
            return cache;
        }

        static List<uint> FeatureGlyphList(List<uint> glyphs)
        {
            if (glyphs == null) throw new NullReferenceException();
            return glyphs;
        }
    }
}
