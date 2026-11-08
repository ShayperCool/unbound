using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class FieldCoverageTests
    {
        [Fact]
        public void StoppingAReadExposesTheFieldAndThereIsNoKeyLimit()
        {
            var body = YNode.MakeMap(Enumerable.Range(0, 100).Select(i => ("key" + i, YNode.MakeScalar("0"))).ToArray());
            List<string> Read(bool last)
            {
                FieldCoverage.Track(body);
                body.Int("key0");
                if (last) body.Int("key99");
                var paths = new List<string>();
                FieldCoverage.Leaves(body, "", paths);
                return paths;
            }
            Assert.Equal(new[] { "key0", "key99" }, Read(true));
            Assert.Equal(new[] { "key0" }, Read(false));
        }

        static void DeclaredBoth(YNode body)
        {
            body.Int("a");
            body.Int("b");
            DeclaredHelper(body, "nested");
        }
        static void DeclaredStopped(YNode body) => body.Int("a");
        static void DeclaredHelper(YNode body, string key) => body.Get(key)?.Int("value");
        static void DeclaredMatrix(YNode body)
        {
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++) body.Float($"e{r}{c}");
        }
        static void DeclaredIndexed(YNode body)
        {
            for (int i = 2; i < 6; i += 2) body.Int($"k{i}");
        }

        [Fact]
        public void GeneratedFieldsFollowReadersAndHelpersWithoutInvokingThem()
        {
            var body = UnityYaml.Parse("--- !u!4 &1\nTransform:\n  a: 0\n  b: 0\n  nested:\n    value: 2\n    extra: 0\n").Single().Body;
            List<string> Read(string method)
            {
                FieldCoverage.Track(body);
                ReaderFieldSchema.Consume(body, ReaderFieldSchema.Fields(typeof(FieldCoverageTests), method, 1));
                var paths = new List<string>();
                FieldCoverage.Leaves(body, "", paths);
                return paths;
            }
            Assert.Contains("b", Read(nameof(DeclaredBoth)));
            Assert.Contains("nested.value", Read(nameof(DeclaredBoth)));
            Assert.DoesNotContain("nested.extra", Read(nameof(DeclaredBoth)));
            Assert.DoesNotContain("b", Read(nameof(DeclaredStopped)));
            var matrix = YNode.MakeMap(("e33", YNode.MakeScalar("1")), ("e44", YNode.MakeScalar("1")));
            ReaderFieldSchema.Consume(matrix, ReaderFieldSchema.Fields(typeof(FieldCoverageTests), nameof(DeclaredMatrix), 1));
            Assert.True(matrix.Get("e33").CoverageRead);
            Assert.False(matrix.Get("e44").CoverageRead);
            var indexed = YNode.MakeMap(Enumerable.Range(0, 7).Select(i => ($"k{i}", YNode.MakeScalar("0"))).ToArray());
            ReaderFieldSchema.Consume(indexed, ReaderFieldSchema.Fields(typeof(FieldCoverageTests), nameof(DeclaredIndexed), 1));
            Assert.Equal(new[] { "k2", "k4" }, indexed.Keys.Where((key, i) => indexed.Values[i].CoverageRead));
        }

        [Fact]
        public void BuiltInDeclarationsReadNestedFieldsAndTheEnabledFlag()
        {
            var documents = new List<FieldCoverage.Input>();
            foreach (var pair in new[] { (4, "Transform", "{\"m_LocalPosition\":{\"x\":\"0\",\"y\":\"1\",\"extra\":\"2\"}}"), (81, "AudioListener", "{\"m_Enabled\":\"0\"}") })
                documents.Add(new FieldCoverage.Input { ClassId = pair.Item1, FileId = pair.Item1, TypeName = pair.Item2, Body = System.Text.Json.JsonDocument.Parse(pair.Item3).RootElement });
            var read = FieldCoverage.Report(documents);
            Assert.Null(read[0].Error);
            Assert.Contains("m_LocalPosition.x", read[0].Consumed);
            Assert.DoesNotContain("m_LocalPosition.extra", read[0].Consumed);
            Assert.Null(read[1].Error);
            Assert.Contains("m_Enabled", read[1].Consumed);
        }

        [Fact]
        public void ReferenceDependentFieldsAndCallbackInputsRemainDeclared()
        {
            var input = new List<FieldCoverage.Input>();
            foreach (var pair in new[] {
                (21, "Material", "{\"m_Parent\":{\"fileID\":\"1\"},\"m_ModifiedSerializedProperties\":\"1\"}"),
                (1839735485, "Tilemap", "{\"m_TileAssetArray\":[{\"m_Data\":{\"fileID\":\"0\",\"extra\":\"0\"}}],\"m_TileColorArray\":[{\"m_Data\":{\"r\":\"1\",\"extra\":\"0\"}}]}") })
                input.Add(new FieldCoverage.Input { ClassId = pair.Item1, FileId = pair.Item1, TypeName = pair.Item2, Body = System.Text.Json.JsonDocument.Parse(pair.Item3).RootElement });
            var result = FieldCoverage.Report(input);
            Assert.Null(result[0].Error);
            Assert.Contains("m_ModifiedSerializedProperties", result[0].Consumed);
            Assert.Null(result[1].Error);
            Assert.Contains("m_TileAssetArray[].m_Data.fileID", result[1].Consumed);
            Assert.Contains("m_TileColorArray[].m_Data.r", result[1].Consumed);
            Assert.DoesNotContain("m_TileColorArray[].m_Data.extra", result[1].Consumed);
        }

        [Fact]
        public void EveryRegisteredReaderHasAGeneratedDeclaration()
        {
            var input = new List<FieldCoverage.Input>();
            foreach (var id in EngineTypes.RegisteredIds)
            {
                var reading = ClassCoverage.Classify(id, null).Reading;
                if (reading != ClassReading.Read && reading != ClassReading.Loader) continue;
                input.Add(new FieldCoverage.Input { ClassId = id, FileId = id, TypeName = "registered", Body = System.Text.Json.JsonDocument.Parse("{}").RootElement });
            }
            var issues = new List<string>();
            foreach (var result in FieldCoverage.Report(input))
                if (result.Error != null) issues.Add(result.FileId + ": " + result.Error);
            Assert.True(issues.Count == 0, string.Join("\n", issues));
        }

        [Serializable]
        class Nested { public int value; [NonSerialized] public int ignored; }
        class Script : MonoBehaviour, ISerializationCallbackReceiver
        {
            public Nested nested;
            public int[] values;
            [UnityEngine.Serialization.FormerlySerializedAs("old")] public int renamed;
            public Script() => throw new Exception("doctor constructed a script");
            public void OnBeforeSerialize() => throw new Exception("callback");
            public void OnAfterDeserialize() => throw new Exception("callback");
        }

        [Fact]
        public void ScriptCoverageUsesTheReadersMemberTableWithoutConstructorsOrCallbacks()
        {
            var body = UnityYaml.Parse("--- !u!114 &1\nMonoBehaviour:\n  old: 9\n  values: [1, 2]\n  nested:\n    value: 1\n    ignored: 2\n    extra: 3\n").Single().Body;
            FieldCoverage.Track(body);
            new YamlReader(null).ScanScriptObject(typeof(Script), body);
            var paths = new List<string>();
            FieldCoverage.Leaves(body, "", paths);
            Assert.Contains("old", paths);
            Assert.Contains("values[]", paths);
            Assert.Contains("nested.value", paths);
            Assert.DoesNotContain("nested.ignored", paths);
            Assert.DoesNotContain("nested.extra", paths);
        }
    }
}
