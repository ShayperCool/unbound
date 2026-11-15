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
            var request = JsonSerializer.Serialize(new { handle = s_Face.Handle, glyphs, lookup = lookup == -1 ? null : lookup });
            var source = JsonSerializer.Deserialize<LigatureJson[]>(Native.Call("font-ligatures", request), s_Json);
            int count = source.Length;
            if (!all && count == 0) return null;
            LigatureSubstitutionRecord[] result;
            if (all) result = new LigatureSubstitutionRecord[count];
            else
            {
                if (s_LigatureRecords == null || s_LigatureRecords.Length <= count)
                {
                    int capacity = 1;
                    while (capacity <= count) capacity *= 2;
                    s_LigatureRecords = new LigatureSubstitutionRecord[capacity];
                }
                result = s_LigatureRecords;
            }
            for (int i = 0; i < count; i++) result[i] = new LigatureSubstitutionRecord { componentGlyphIDs = source[i].Components, ligatureGlyphID = source[i].Glyph };
            if (!all) result[count] = default;
            return result;
        }

        internal static LigatureSubstitutionRecord[] GetAllLigatureSubstitutionRecords() => Ligatures(null, null, true);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(uint glyphIndex) => Ligatures(new[] { glyphIndex }, null, false);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(List<uint> glyphIndexes) => Ligatures(FeatureGlyphList(glyphIndexes), null, false);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(int lookupIndex, uint glyphIndex) => Ligatures(new[] { glyphIndex }, lookupIndex, false);
        internal static LigatureSubstitutionRecord[] GetLigatureSubstitutionRecords(int lookupIndex, List<uint> glyphIndexes) => Ligatures(FeatureGlyphList(glyphIndexes), lookupIndex, false);

        static List<uint> FeatureGlyphList(List<uint> glyphs)
        {
            if (glyphs == null) throw new NullReferenceException();
            return glyphs;
        }
    }
}
