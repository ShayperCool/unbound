using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    public partial class FontAsset
    {
        internal void InitializeLigatureSubstitutionLookupDictionary()
        {
            m_FontFeatureTable ??= new FontFeatureTable();
            var lookup = m_FontFeatureTable.m_LigatureSubstitutionRecordLookup;
            lookup.Clear();
            if (m_FontFeatureTable.ligatureRecords == null) return;
            foreach (var record in m_FontFeatureTable.ligatureRecords)
                if (record.componentGlyphIDs != null && record.componentGlyphIDs.Length > 0)
                    LigatureGroup(record.componentGlyphIDs[0]).Add(record);
        }

        List<LigatureSubstitutionRecord> LigatureGroup(uint first)
        {
            var lookup = m_FontFeatureTable.m_LigatureSubstitutionRecordLookup;
            if (!lookup.TryGetValue(first, out var records)) lookup.Add(first, records = new());
            return records;
        }

        internal void InitializeMarkToBaseAdjustmentRecordsLookupDictionary()
        {
            var lookup = m_FontFeatureTable.m_MarkToBaseAdjustmentRecordLookup;
            lookup.Clear();
            if (m_FontFeatureTable.MarkToBaseAdjustmentRecords == null) return;
            foreach (var record in m_FontFeatureTable.MarkToBaseAdjustmentRecords)
                lookup.TryAdd(FontFeatureTable.PairKey(record.baseGlyphID, record.markGlyphID), record);
        }

        internal void InitializeMarkToMarkAdjustmentRecordsLookupDictionary()
        {
            var lookup = m_FontFeatureTable.m_MarkToMarkAdjustmentRecordLookup;
            lookup.Clear();
            m_FontFeatureTable.markCandidates.Clear();
            if (m_FontFeatureTable.MarkToMarkAdjustmentRecords == null) return;
            foreach (var record in m_FontFeatureTable.MarkToMarkAdjustmentRecords)
                if (lookup.TryAdd(FontFeatureTable.PairKey(record.baseMarkGlyphID, record.combiningMarkGlyphID), record))
                    m_FontFeatureTable.AddMarkCandidate(record);
        }

        void UpdateGSUBFontFeaturesForNewGlyphIndex(uint index) => AddLigatureSubstitutionRecords(FontEngine.GetLigatureSubstitutionRecords(index));
        internal void UpdateLigatureSubstitutionRecords() => AddLigatureSubstitutionRecords(FontEngine.GetLigatureSubstitutionRecords(m_GlyphIndexListNewlyAdded));
        internal void UpdateDiacriticalMarkAdjustmentRecords()
        {
            AddMarkToBaseAdjustmentRecords(FontEngine.GetMarkToBaseAdjustmentRecords(m_GlyphIndexListNewlyAdded));
            AddMarkToMarkAdjustmentRecords(FontEngine.GetMarkToMarkAdjustmentRecords(m_GlyphIndexListNewlyAdded));
        }

        void AddLigatureSubstitutionRecords(LigatureSubstitutionRecord[] records)
        {
            if (records == null) return;
            foreach (var record in records)
            {
                var components = record.componentGlyphIDs;
                if (components == null || components.Length == 0 || record.ligatureGlyphID == 0) break;
                var group = LigatureGroup(components[0]);
                if (group.Contains(record))
                {
                    if (Compat.FontLigatureDuplicateBatchMeasured) return;
                    continue;
                }
                group.Add(record);
                (m_FontFeatureTable.m_LigatureSubstitutionRecords ??= new()).Add(record);
            }
        }

        void AddMarkToBaseAdjustmentRecords(MarkToBaseAdjustmentRecord[] records)
        {
            if (records == null) return;
            float scale = m_FaceInfo.pointSize / m_FaceInfo.unitsPerEM;
            foreach (var record in records)
            {
                if (record.baseGlyphID == 0 || record.markGlyphID == 0) break;
                uint key = FontFeatureTable.PairKey(record.baseGlyphID, record.markGlyphID);
                if (m_FontFeatureTable.m_MarkToBaseAdjustmentRecordLookup.ContainsKey(key)) continue;
                var scaled = record;
                scaled.baseGlyphAnchorPoint = FontEngine.Scale(record.baseGlyphAnchorPoint, scale);
                scaled.markPositionAdjustment = FontEngine.Scale(record.markPositionAdjustment, scale);
                m_FontFeatureTable.m_MarkToBaseAdjustmentRecordLookup.Add(key, scaled);
                (m_FontFeatureTable.m_MarkToBaseAdjustmentRecords ??= new()).Add(scaled);
            }
        }

        void AddMarkToMarkAdjustmentRecords(MarkToMarkAdjustmentRecord[] records)
        {
            if (records == null) return;
            float scale = m_FaceInfo.pointSize / m_FaceInfo.unitsPerEM;
            foreach (var record in records)
            {
                if (record.baseMarkGlyphID == 0 || record.combiningMarkGlyphID == 0) break;
                uint key = FontFeatureTable.PairKey(record.baseMarkGlyphID, record.combiningMarkGlyphID);
                if (m_FontFeatureTable.m_MarkToMarkAdjustmentRecordLookup.ContainsKey(key)) continue;
                var scaled = record;
                scaled.baseMarkGlyphAnchorPoint = FontEngine.Scale(record.baseMarkGlyphAnchorPoint, scale);
                scaled.combiningMarkPositionAdjustment = FontEngine.Scale(record.combiningMarkPositionAdjustment, scale);
                m_FontFeatureTable.m_MarkToMarkAdjustmentRecordLookup.Add(key, scaled);
                (m_FontFeatureTable.m_MarkToMarkAdjustmentRecords ??= new()).Add(scaled);
                m_FontFeatureTable.AddMarkCandidate(scaled);
            }
        }
    }
}
