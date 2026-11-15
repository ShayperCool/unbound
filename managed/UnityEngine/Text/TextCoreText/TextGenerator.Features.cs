using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    internal partial class TextGenerator
    {
        int MatchLigature(TextProcessingElement[] elements, int start, uint first, bool canAdd, out Glyph glyph, out bool success)
        {
            glyph = null;
            success = true;
            if (!m_CurrentFontAsset.fontFeatureTable.m_LigatureSubstitutionRecordLookup.TryGetValue(first, out var records) || records == null) return 0;
            foreach (var record in records)
            {
                var ids = record.componentGlyphIDs;
                if (ids == null || ids.Length == 0 || ids.Length > elements.Length - start || record.ligatureGlyphID == 0) continue;
                bool matches = true;
                for (int component = 1; component < ids.Length; component++)
                {
                    uint unicode = elements[start + component].unicode;
                    if (unicode == 0 || unicode == k_Consumed) { matches = false; break; }
                    uint id = m_CurrentFontAsset.GetGlyphIndex(unicode, out success);
                    if (!success) return 0;
                    if (id != ids[component]) { matches = false; break; }
                }
                if (!matches) continue;
                if (m_CurrentFontAsset.glyphLookupTable.TryGetValue(record.ligatureGlyphID, out glyph)) return ids.Length;
                if (!canAdd) { success = false; return 0; }
                if (m_CurrentFontAsset.TryAddGlyphInternal(record.ligatureGlyphID, out glyph)) return ids.Length;
            }
            return 0;
        }

        static void ConsumeLigature(TextProcessingElement[] elements, ref int index, int count)
        {
            elements[index].length = count;
            for (int component = 1; component < count; component++) elements[index + component].unicode = k_Consumed;
            index += count - 1;
        }

        struct OccurrenceRange { internal int offset, count, written; }
        readonly Dictionary<uint, OccurrenceRange> m_MarkOccurrenceRanges = new();
        uint[] m_MarkGlyphs = Array.Empty<uint>();
        int[] m_MarkOccurrences = Array.Empty<int>();

        // Group nominal glyph occurrences once per pass. Searching before the current character
        // remains valid when wrapping rewinds the pass, and does not scan long mark chains.
        void PrepareMarkOccurrences(TextInfo textInfo)
        {
            m_MarkOccurrenceRanges.Clear();
            if (m_MarkOccurrences.Length < m_TotalCharacterCount)
            {
                int capacity = Mathf.NextPowerOfTwo(m_TotalCharacterCount);
                Array.Resize(ref m_MarkOccurrences, capacity);
                Array.Resize(ref m_MarkGlyphs, capacity);
            }
            int unique = 0;
            for (int i = 0; i < m_TotalCharacterCount; i++)
            {
                uint glyph = textInfo.textElementInfo[i].textElement?.glyphIndex ?? 0;
                if (!m_MarkOccurrenceRanges.TryGetValue(glyph, out var range)) m_MarkGlyphs[unique++] = glyph;
                range.count++;
                m_MarkOccurrenceRanges[glyph] = range;
            }
            int offset = 0;
            for (int i = 0; i < unique; i++)
            {
                uint glyph = m_MarkGlyphs[i];
                var range = m_MarkOccurrenceRanges[glyph];
                range.offset = offset;
                offset += range.count;
                m_MarkOccurrenceRanges[glyph] = range;
            }
            for (int i = 0; i < m_TotalCharacterCount; i++)
            {
                uint glyph = textInfo.textElementInfo[i].textElement?.glyphIndex ?? 0;
                var range = m_MarkOccurrenceRanges[glyph];
                m_MarkOccurrences[range.offset + range.written++] = i;
                m_MarkOccurrenceRanges[glyph] = range;
            }
        }

        int PreviousGlyphOccurrence(uint glyph)
        {
            if (!m_MarkOccurrenceRanges.TryGetValue(glyph, out var range)) return -1;
            int low = range.offset, high = low + range.count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (m_MarkOccurrences[middle] < m_CharacterCount) low = middle + 1;
                else high = middle;
            }
            return low == range.offset ? -1 : m_MarkOccurrences[low - 1];
        }

        void PositionMark(TextInfo textInfo, TextElementInfo[] placed, bool isBase, float scale, float baseline,
            bool mark, bool markToMark, bool preferred, ref GlyphValueRecord adjustment, ref float spacing)
        {
            if (isBase) { m_LastBaseGlyphIndex = m_CharacterCount; return; }
            if (m_CharacterCount == 0) return;
            var table = m_CurrentFontAsset.fontFeatureTable;
            uint combining = m_CachedTextElement.glyphIndex;
            bool immediateBase = mark && m_LastBaseGlyphIndex == m_CharacterCount - 1;
            if (!immediateBase && markToMark && table.markCandidates.TryGetValue(combining, out var candidates))
            {
                int nearest = -1;
                MarkToMarkAdjustmentRecord selected = default;
                foreach (var candidate in candidates)
                {
                    int index = PreviousGlyphOccurrence(candidate.baseMarkGlyphID);
                    if (index > m_LastBaseGlyphIndex && index > nearest) { nearest = index; selected = candidate; }
                }
                if (nearest >= 0)
                {
                    // Preferred measurement retains the generated origin for this attachment in
                    // the measured release. Its baselines come from the measuring pass itself.
                    float origin = preferred && Compat.FontPreferredMarksMeasured ? textInfo.textElementInfo[nearest].origin : placed[nearest].origin;
                    adjustment.xPlacement = (origin - m_XAdvance) / scale + selected.baseMarkGlyphAnchorPoint.xCoordinate - selected.combiningMarkPositionAdjustment.xPositionAdjustment;
                    adjustment.yPlacement = (placed[nearest].baseLine - baseline) / scale + selected.baseMarkGlyphAnchorPoint.yCoordinate - selected.combiningMarkPositionAdjustment.yPositionAdjustment;
                    spacing = 0f;
                    return;
                }
            }
            if (!mark || m_LastBaseGlyphIndex < 0) return;
            uint first = textInfo.textElementInfo[m_LastBaseGlyphIndex].textElement.glyphIndex;
            if (table.m_MarkToBaseAdjustmentRecordLookup.TryGetValue(FontFeatureTable.PairKey(first, combining), out var record))
            {
                adjustment.xPlacement = (placed[m_LastBaseGlyphIndex].origin - m_XAdvance) / scale + record.baseGlyphAnchorPoint.xCoordinate - record.markPositionAdjustment.xPositionAdjustment;
                adjustment.yPlacement = record.baseGlyphAnchorPoint.yCoordinate - record.markPositionAdjustment.yPositionAdjustment;
                spacing = 0f;
            }
        }
    }
}
