using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine.TextCore.LowLevel
{
    public sealed partial class FontEngine
    {
        sealed class PairJson
        {
            public uint First { get; set; }
            public uint Second { get; set; }
            public int[] FirstValues { get; set; }
            public int[] SecondValues { get; set; }
        }

        static GlyphPairAdjustmentRecord[] s_PairAdjustmentRecords;
        static GlyphAdjustmentRecord PairAdjustment(uint glyph, int[] values) =>
            new GlyphAdjustmentRecord(glyph, new GlyphValueRecord(values[0], values[1], values[2], values[3]));
        static GlyphPairAdjustmentRecord PairAdjustment(PairJson pair) => new GlyphPairAdjustmentRecord(
            PairAdjustment(pair.First, pair.FirstValues), PairAdjustment(pair.Second, pair.SecondValues));

        static GlyphPairAdjustmentRecord[] PairRecords(IEnumerable<uint> glyphs, int? lookup, bool exact, uint? second = null)
        {
            if (s_Face == null) return exact ? Array.Empty<GlyphPairAdjustmentRecord>() : null;
            return FeatureRecords<PairJson, GlyphPairAdjustmentRecord>("font-pair-adjustments",
                new { handle = s_Face.Handle, glyphs, lookup, second }, exact, ref s_PairAdjustmentRecords, PairAdjustment);
        }

        static GlyphPairAdjustmentRecord[] UnindexedPairRecords(List<uint> glyphs)
        {
            FeatureGlyphList(glyphs);
            if (glyphs.Count == 0 || glyphs[0] == 0) return null;
            var records = PairRecords(glyphs, null, false);
            if (records != null) return records;
            s_LegacyPairQuery.Clear();
            PairRanks(glyphs);
            if (s_PairRanks.Count == 0 || !LoadLegacyPairs()) return null;
            // An empty GPOS selection visits KERN partners in glyph order, then the requested
            // list in its order. Incoming precedes outgoing; self pairs and repeats stay.
            foreach (uint partner in s_LegacyUnitPartners)
            {
                s_PairMatches.Clear();
                MatchPairEndpoints(s_LegacyUnitOutgoing, partner, false);
                MatchPairEndpoints(s_LegacyUnitIncoming, partner, true);
                SortPairMatches();
                foreach (var match in s_PairMatches) s_LegacyPairQuery.Add(match.Record);
            }
            return LegacyPairBuffer();
        }

        internal static GlyphPairAdjustmentRecord[] GetPairAdjustmentRecords(int lookupIndex, uint glyphIndex) =>
            IndexedPairRecords(lookupIndex, new[] { glyphIndex });
        internal static GlyphPairAdjustmentRecord[] GetPairAdjustmentRecords(int lookupIndex, List<uint> glyphIndexes) =>
            IndexedPairRecords(lookupIndex, FeatureGlyphList(glyphIndexes));
        static GlyphPairAdjustmentRecord[] IndexedPairRecords(int lookup, IEnumerable<uint> glyphs)
        {
            if (!Compat.FontPairQueriesMeasured)
            {
                Stubs.NotImplemented("UnityEngine.TextCore.LowLevel.FontEngine.GetPairAdjustmentRecords");
                return Array.Empty<GlyphPairAdjustmentRecord>();
            }
            return PairRecords(glyphs, lookup, false);
        }

        static GlyphPairAdjustmentRecord ScaledPairRecord(uint first, uint second)
        {
            foreach (var record in PairRecords(new[] { first }, null, true, second))
            {
                if (record.secondAdjustmentRecord.glyphIndex != second) continue;
                var adjustment = record.firstAdjustmentRecord;
                var value = adjustment.glyphValueRecord;
                var secondValue = record.secondAdjustmentRecord.glyphValueRecord;
                if (value.xPlacement != 0 || value.yPlacement != 0 || value.yAdvance != 0
                    || secondValue.xPlacement != 0 || secondValue.yPlacement != 0
                    || secondValue.xAdvance != 0 || secondValue.yAdvance != 0)
                    throw new NotSupportedException("FontEngine.GetPairAdjustmentRecord supports x-advance-only GPOS pairs; use the record-array queries for multi-field adjustments.");
                value.xAdvance *= PixelScale;
                adjustment.glyphValueRecord = value;
                return new GlyphPairAdjustmentRecord(adjustment, record.secondAdjustmentRecord);
            }
            return default;
        }

        // KERN queries use pixel values and the same filtered-query buffer as GPOS. The face
        // table is loaded once per handle/size; scalar lookups then need no transport or allocation.
        static ulong s_LegacyPairHandle;
        static int s_LegacyPairSize = int.MinValue;
        static readonly Dictionary<(uint, uint), GlyphPairAdjustmentRecord> s_LegacyPairLookup = new();
        static readonly Dictionary<uint, List<GlyphPairAdjustmentRecord>> s_LegacyOutgoing = new(), s_LegacyIncoming = new();
        static readonly Dictionary<uint, List<GlyphPairAdjustmentRecord>> s_LegacyUnitOutgoing = new(), s_LegacyUnitIncoming = new();
        static readonly List<uint> s_LegacyUnitPartners = new();
        static readonly HashSet<uint> s_LegacyUnitPartnerSet = new();
        static readonly List<GlyphPairAdjustmentRecord> s_LegacyPairQuery = new();
        static readonly List<(uint Glyph, int Position)> s_PairRanks = new();
        static readonly List<(int Position, int Priority, GlyphPairAdjustmentRecord Record)> s_PairMatches = new();
        static readonly HashSet<(uint, uint)> s_LegacyPairSeen = new();

        static void AddPairEndpoint(Dictionary<uint, List<GlyphPairAdjustmentRecord>> into, uint glyph, GlyphPairAdjustmentRecord pair)
        {
            if (!into.TryGetValue(glyph, out var records)) into[glyph] = records = new();
            records.Add(pair);
        }

        static bool LoadLegacyPairs()
        {
            if (s_Face == null) return false;
            if (s_LegacyPairHandle == s_Face.Handle && s_LegacyPairSize == FaceSize) return true;
            s_LegacyPairLookup.Clear(); s_LegacyOutgoing.Clear(); s_LegacyIncoming.Clear();
            foreach (var record in ReadLegacyPairs(false))
            {
                uint first = record.firstAdjustmentRecord.glyphIndex, second = record.secondAdjustmentRecord.glyphIndex;
                s_LegacyPairLookup[(first, second)] = record;
                AddPairEndpoint(s_LegacyOutgoing, first, record);
                AddPairEndpoint(s_LegacyIncoming, second, record);
            }
            s_LegacyUnitOutgoing.Clear(); s_LegacyUnitIncoming.Clear();
            s_LegacyUnitPartners.Clear(); s_LegacyUnitPartnerSet.Clear();
            foreach (var record in ReadLegacyPairs(true))
            {
                uint first = record.firstAdjustmentRecord.glyphIndex, second = record.secondAdjustmentRecord.glyphIndex;
                if (s_LegacyUnitPartnerSet.Add(first)) s_LegacyUnitPartners.Add(first);
                if (s_LegacyUnitPartnerSet.Add(second)) s_LegacyUnitPartners.Add(second);
                AddPairEndpoint(s_LegacyUnitOutgoing, first, record);
                AddPairEndpoint(s_LegacyUnitIncoming, second, record);
            }
            s_LegacyUnitPartners.Sort();
            s_LegacyPairHandle = s_Face.Handle;
            s_LegacyPairSize = FaceSize;
            return true;
        }

        static IEnumerable<GlyphPairAdjustmentRecord> ReadLegacyPairs(bool unscaled)
        {
            var source = JsonSerializer.Deserialize<float[][]>(Native.Call("font-legacy-kerning",
                JsonSerializer.Serialize(new { handle = s_Face.Handle, pixels = FaceSize, unscaled })));
            foreach (var pair in source)
                yield return new GlyphPairAdjustmentRecord(
                    new GlyphAdjustmentRecord((uint)pair[0], new GlyphValueRecord(0, 0, pair[2], 0)),
                    new GlyphAdjustmentRecord((uint)pair[1], default));
        }

        static GlyphPairAdjustmentRecord LegacyPairRecord(uint first, uint second) =>
            LoadLegacyPairs() && s_LegacyPairLookup.TryGetValue((first, second), out var record) ? record : default;

        static void PairRanks(IReadOnlyList<uint> glyphs)
        {
            s_PairRanks.Clear();
            for (int i = 0; i < glyphs.Count && glyphs[i] != 0; i++)
                if (glyphs[i] <= ushort.MaxValue) s_PairRanks.Add((glyphs[i], i));
            s_PairRanks.Sort(static (a, b) => a.Glyph != b.Glyph ? a.Glyph.CompareTo(b.Glyph) : a.Position.CompareTo(b.Position));
        }

        static void MatchPairEndpoints(Dictionary<uint, List<GlyphPairAdjustmentRecord>> source, uint glyph, bool incoming)
        {
            if (!source.TryGetValue(glyph, out var pairs)) return;
            foreach (var pair in pairs)
            {
                uint other = incoming ? pair.firstAdjustmentRecord.glyphIndex : pair.secondAdjustmentRecord.glyphIndex;
                int lo = 0, hi = s_PairRanks.Count;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (s_PairRanks[mid].Glyph < other) lo = mid + 1; else hi = mid;
                }
                for (int i = lo; i < s_PairRanks.Count && s_PairRanks[i].Glyph == other; i++)
                    s_PairMatches.Add((s_PairRanks[i].Position, incoming ? 1 : 0, pair));
            }
        }

        static void SortPairMatches() => s_PairMatches.Sort(static (a, b) =>
            a.Position != b.Position ? a.Position.CompareTo(b.Position) : a.Priority.CompareTo(b.Priority));

        static GlyphPairAdjustmentRecord[] LegacyPairBuffer()
        {
            var records = FeatureBuffer(s_LegacyPairQuery.Count, ref s_PairAdjustmentRecords, false);
            if (records != null) s_LegacyPairQuery.CopyTo(records);
            return records;
        }

        static GlyphPairAdjustmentRecord[] LegacyPairRecords(uint glyph)
        {
            s_LegacyPairQuery.Clear();
            if (glyph == 0 || glyph > ushort.MaxValue || !LoadLegacyPairs()) return null;
            s_PairMatches.Clear();
            if (s_LegacyOutgoing.TryGetValue(glyph, out var outgoing))
                foreach (var pair in outgoing) s_PairMatches.Add(((int)pair.secondAdjustmentRecord.glyphIndex, 0, pair));
            if (s_LegacyIncoming.TryGetValue(glyph, out var incoming))
                foreach (var pair in incoming) s_PairMatches.Add(((int)pair.firstAdjustmentRecord.glyphIndex, 1, pair));
            SortPairMatches();
            foreach (var match in s_PairMatches) s_LegacyPairQuery.Add(match.Record);
            return LegacyPairBuffer();
        }

        static GlyphPairAdjustmentRecord[] LegacyPairRecords(IReadOnlyList<uint> glyphs)
        {
            s_LegacyPairQuery.Clear();
            if (glyphs == null || !LoadLegacyPairs()) return null;
            PairRanks(glyphs);
            for (int i = 0; i < glyphs.Count && glyphs[i] != 0; i++)
            {
                s_PairMatches.Clear();
                MatchPairEndpoints(s_LegacyOutgoing, glyphs[i], false);
                SortPairMatches();
                foreach (var match in s_PairMatches) s_LegacyPairQuery.Add(match.Record);
            }
            return LegacyPairBuffer();
        }

        static GlyphPairAdjustmentRecord[] LegacyPairRecords(List<uint> added, List<uint> all)
        {
            FeatureGlyphList(added); FeatureGlyphList(all);
            s_LegacyPairQuery.Clear(); s_LegacyPairSeen.Clear();
            if (!LoadLegacyPairs()) return null;
            PairRanks(all);
            for (int i = 0; i < added.Count && added[i] != 0; i++)
            {
                s_PairMatches.Clear();
                MatchPairEndpoints(s_LegacyOutgoing, added[i], false);
                MatchPairEndpoints(s_LegacyIncoming, added[i], true);
                SortPairMatches();
                foreach (var match in s_PairMatches)
                {
                    var record = match.Record;
                    if (s_LegacyPairSeen.Add((record.firstAdjustmentRecord.glyphIndex, record.secondAdjustmentRecord.glyphIndex)))
                        s_LegacyPairQuery.Add(record);
                }
            }
            return LegacyPairBuffer();
        }
    }
}
