using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using Xunit;

namespace Unbound.Tests
{
    public class TextCoreFeatureTests
    {
        static void Add(FontAsset asset, string method, object records) => typeof(FontAsset).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(asset, new[] { records });

        [Fact]
        public void MarkImportsScaleOnceKeepFirstPriorityAndRebuildLookups()
        {
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            asset.faceInfo = new FaceInfo { pointSize = 32, unitsPerEM = 2048 };
            var record = new MarkToBaseAdjustmentRecord
            {
                baseGlyphID = 17, markGlyphID = 29,
                baseGlyphAnchorPoint = new GlyphAnchorPoint { xCoordinate = -320, yCoordinate = 1024 },
                markPositionAdjustment = new MarkPositionAdjustment(64, -128)
            };
            Add(asset, "AddMarkToBaseAdjustmentRecords", new[] { record });
            record.baseGlyphAnchorPoint = new GlyphAnchorPoint { xCoordinate = 999 };
            Add(asset, "AddMarkToBaseAdjustmentRecords", new[] { record });
            var table = asset.fontFeatureTable;
            Assert.Single(table.MarkToBaseAdjustmentRecords);
            var imported = table.MarkToBaseAdjustmentRecords[0];
            Assert.Equal(-5f, imported.baseGlyphAnchorPoint.xCoordinate);
            Assert.Equal(16f, imported.baseGlyphAnchorPoint.yCoordinate);
            Assert.Equal(1f, imported.markPositionAdjustment.xPositionAdjustment);
            Assert.Equal(-2f, imported.markPositionAdjustment.yPositionAdjustment);
            asset.InitializeMarkToBaseAdjustmentRecordsLookupDictionary();
            Assert.Equal(imported.baseGlyphAnchorPoint.xCoordinate, table.m_MarkToBaseAdjustmentRecordLookup[FontFeatureTable.PairKey(17, 29)].baseGlyphAnchorPoint.xCoordinate);

            var mark = new MarkToMarkAdjustmentRecord { baseMarkGlyphID = 29, combiningMarkGlyphID = 31, baseMarkGlyphAnchorPoint = new GlyphAnchorPoint { xCoordinate = -128 }, combiningMarkPositionAdjustment = new MarkPositionAdjustment(256, -64) };
            Add(asset, "AddMarkToMarkAdjustmentRecords", new[] { mark, mark });
            asset.InitializeMarkToMarkAdjustmentRecordsLookupDictionary();
            Assert.Single(table.MarkToMarkAdjustmentRecords);
            Assert.Single(table.markCandidates[31]);
            Assert.Equal(-2f, table.markCandidates[31][0].baseMarkGlyphAnchorPoint.xCoordinate);
            Assert.Equal(4f, table.markCandidates[31][0].combiningMarkPositionAdjustment.xPositionAdjustment);
            Object.DestroyImmediate(asset);
        }

        [Fact]
        public void SerializedLigaturesSkipEmptyComponentsAndKeepRecordOrder()
        {
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            var first = new LigatureSubstitutionRecord { componentGlyphIDs = new uint[] { 9, 10 }, ligatureGlyphID = 30 };
            var later = new LigatureSubstitutionRecord { componentGlyphIDs = new uint[] { 9, 10, 11 }, ligatureGlyphID = 31 };
            asset.fontFeatureTable.ligatureRecords = new() { default, new LigatureSubstitutionRecord { componentGlyphIDs = System.Array.Empty<uint>() }, first, later };
            asset.InitializeLigatureSubstitutionLookupDictionary();
            Assert.Equal(new[] { first, later }, asset.fontFeatureTable.m_LigatureSubstitutionRecordLookup[9]);
            var newGroup = new LigatureSubstitutionRecord { componentGlyphIDs = new uint[] { 12, 13 }, ligatureGlyphID = 32 };
            Add(asset, "AddLigatureSubstitutionRecords", new[] { first, newGroup });
            Assert.Equal(!Compat.FontLigatureDuplicateBatchMeasured, asset.fontFeatureTable.m_LigatureSubstitutionRecordLookup.ContainsKey(12));
            Object.DestroyImmediate(asset);
        }

        [Fact]
        public void MarkSortingPreservesEqualKeyPriorityAndDoesNotReplaceLookup()
        {
            var table = new FontFeatureTable();
            table.MarkToBaseAdjustmentRecords = new List<MarkToBaseAdjustmentRecord> {
                new() { baseGlyphID = 8, markGlyphID = 2 },
                new() { baseGlyphID = 3, markGlyphID = 7, baseGlyphAnchorPoint = new() { xCoordinate = 1 } },
                new() { baseGlyphID = 3, markGlyphID = 7, baseGlyphAnchorPoint = new() { xCoordinate = 2 } },
                new() { baseGlyphID = 3, markGlyphID = 4 }
            };
            var lookup = table.m_MarkToBaseAdjustmentRecordLookup;
            table.SortMarkToBaseAdjustmentRecords();
            Assert.Equal(new uint[] { 4, 7, 7, 2 }, table.MarkToBaseAdjustmentRecords.ConvertAll(r => r.markGlyphID));
            Assert.Equal(1, table.MarkToBaseAdjustmentRecords[1].baseGlyphAnchorPoint.xCoordinate);
            Assert.Equal(2, table.MarkToBaseAdjustmentRecords[2].baseGlyphAnchorPoint.xCoordinate);
            Assert.Same(lookup, table.m_MarkToBaseAdjustmentRecordLookup);
        }
    }
}
