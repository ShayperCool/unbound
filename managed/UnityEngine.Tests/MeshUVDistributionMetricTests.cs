using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected values match the Unity 6000.0.71f1 Editor (tests/projects/uvmetrics/Reference/unity-6000.0.71f1/values.jsonl).
    public class MeshUVDistributionMetricTests
    {
        static Mesh MakeRightTriangles(params (float World, float Uv)[] triangles)
        {
            var positions = new Vector3[triangles.Length * 3];
            var uvs = new Vector2[triangles.Length * 3];
            var indices = new int[triangles.Length * 3];
            for (int i = 0; i < triangles.Length; i++)
            {
                var (w, u) = triangles[i];
                var o = new Vector3(i * 10f, 0f, 0f);
                positions[3 * i] = o;
                positions[3 * i + 1] = o + new Vector3(0f, w, 0f);
                positions[3 * i + 2] = o + new Vector3(w, 0f, 0f);
                uvs[3 * i] = Vector2.zero;
                uvs[3 * i + 1] = new Vector2(0f, u);
                uvs[3 * i + 2] = new Vector2(u, 0f);
                indices[3 * i] = 3 * i;
                indices[3 * i + 1] = 3 * i + 1;
                indices[3 * i + 2] = 3 * i + 2;
            }
            var mesh = new Mesh();
            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.triangles = indices;
            return mesh;
        }

        [Fact]
        public void DefaultsAndOutOfRangeIndicesReturnOne()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = MakeRightTriangles((2f, 1f));
            try
            {
                Assert.Equal(1f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(1));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(-1));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(2));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(8));

                mesh.RecalculateUVDistributionMetric(0);
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(1));

                mesh.RecalculateUVDistributionMetric(-1);
                mesh.RecalculateUVDistributionMetric(2);
                mesh.RecalculateUVDistributionMetric(8);
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void RecalculatesEachUvSetIndependently()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = MakeRightTriangles((2f, 1f));
            try
            {
                mesh.uv2 = new[] { Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0.5f, 0f) };
                mesh.RecalculateUVDistributionMetric(1);
                Assert.Equal(1f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(16f, mesh.GetUVDistributionMetric(1));

                mesh.uv2 = new[] { Vector2.zero, new Vector2(0f, 1f), new Vector2(1f, 0f) };
                mesh.RecalculateUVDistributionMetric(0);
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(16f, mesh.GetUVDistributionMetric(1));

                mesh.RecalculateUVDistributionMetrics();
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(4f, mesh.GetUVDistributionMetric(1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void MissingUvsReturnOneEvenWithZeroThreshold()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = MakeRightTriangles((2f, 1f));
            try
            {
                mesh.RecalculateUVDistributionMetrics();
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));

                // Removing UVs must reset a previously non-1 metric back to 1 even with threshold 0.
                mesh.uv = null;
                mesh.RecalculateUVDistributionMetrics(0f);
                Assert.Equal(1f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(1));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void AggregatesMatchesCommittedEditorReferenceValues()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var disposable = new MeshScope();
            // Reference: tests/projects/uvmetrics/Reference/unity-6000.0.71f1/values.jsonl
            var mixed = disposable.Add(MakeRightTriangles((1f, 1f), (2f, 1f)));
            mixed.RecalculateUVDistributionMetrics();
            Assert.Equal(4f, mixed.GetUVDistributionMetric(0));

            var oneToFive = disposable.Add(MakeRightTriangles((1f, 1f), (1.41421356f, 1f), (1.73205081f, 1f), (2f, 1f), (2.23606798f, 1f)));
            oneToFive.RecalculateUVDistributionMetrics();
            Assert.Equal(4.61803532f, oneToFive.GetUVDistributionMetric(0));

            var sameWorldFour = disposable.Add(MakeRightTriangles(
                (1f, 1f),
                (1f, 1f / Mathf.Sqrt(2f)),
                (1f, 1f / Mathf.Sqrt(3f)),
                (1f, 0.5f)));
            sameWorldFour.RecalculateUVDistributionMetrics();
            Assert.Equal(3.618034f, sameWorldFour.GetUVDistributionMetric(0));

            var outlier = new (float, float)[21];
            for (int i = 0; i < 20; i++) outlier[i] = (1f, 1f);
            outlier[20] = (10f, 1f);
            var outlierHigh = disposable.Add(MakeRightTriangles(outlier));
            outlierHigh.RecalculateUVDistributionMetrics();
            Assert.Equal(100f, outlierHigh.GetUVDistributionMetric(0));

            // Threshold skipping: UV area of (1, 0.5) right triangle is 0.125.
            var threshold = disposable.Add(MakeRightTriangles((1f, 0.5f)));
            threshold.RecalculateUVDistributionMetrics(0.1f);
            Assert.Equal(4f, threshold.GetUVDistributionMetric(0));
            threshold.RecalculateUVDistributionMetrics(0.2f);
            Assert.Equal(1f, threshold.GetUVDistributionMetric(0));
        }

        [Fact]
        public void SkipsEmptyNonTriangleShortEdgeAndNarrowAngleTriangles()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var disposable = new MeshScope();
            var empty = disposable.Add(new Mesh());
            empty.RecalculateUVDistributionMetrics();
            Assert.Equal(1f, empty.GetUVDistributionMetric(0));

            var lines = disposable.Add(new Mesh());
            lines.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            lines.uv = new[] { Vector2.zero, Vector2.up, Vector2.right };
            lines.SetIndices(new[] { 0, 1, 1, 2 }, MeshTopology.Lines, 0);
            lines.RecalculateUVDistributionMetrics();
            Assert.Equal(1f, lines.GetUVDistributionMetric(0));

            // Edge under 0.001 skipped; valid triangle of ratio 9 counted.
            var tinyEdge = disposable.Add(MakeRightTriangles((0.0005f, 0.0001f), (3f, 1f)));
            tinyEdge.RecalculateUVDistributionMetrics();
            Assert.Equal(9f, tinyEdge.GetUVDistributionMetric(0));

            // Sliver triangle with angle under 5 degrees skipped; valid triangle of ratio 4 counted.
            var sliver = disposable.Add(new Mesh());
            sliver.vertices = new[]
            {
                Vector3.zero, new Vector3(100f, 0f, 0f), new Vector3(100f, 0.5f, 0f),
                new Vector3(0f, 10f, 0f), new Vector3(0f, 12f, 0f), new Vector3(2f, 10f, 0f),
            };
            sliver.uv = new[]
            {
                Vector2.zero, new Vector2(1f, 0f), new Vector2(1f, 0.01f),
                Vector2.zero, new Vector2(0f, 1f), new Vector2(1f, 0f),
            };
            sliver.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            sliver.RecalculateUVDistributionMetrics();
            Assert.Equal(4f, sliver.GetUVDistributionMetric(0));
        }

        [Fact]
        public void BaseVertexAndOutOfBoundsIndicesAreHandledSafely()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[]
                {
                    Vector3.zero, Vector3.zero,
                    Vector3.zero, new Vector3(0f, 2f, 0f), new Vector3(2f, 0f, 0f),
                };
                mesh.uv = new[]
                {
                    Vector2.zero, Vector2.zero,
                    Vector2.zero, new Vector2(0f, 1f), new Vector2(1f, 0f),
                };
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, calculateBounds: true, baseVertex: 2);
                mesh.RecalculateUVDistributionMetrics();
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));

                // Out-of-bounds / negative indices do not throw.
                float metric = Mesh.UVDistributionMetric(mesh.vertices, new[] { -1, 0, 99 }, mesh.m_UVs[0], 1e-9f);
                Assert.Equal(1f, metric);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void CopyAndSerializedAssetRoundTripPreserveMetrics()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = MakeRightTriangles((2f, 1f));
            Mesh clone = null;
            Mesh reloaded = null;
            try
            {
                mesh.name = "MetricsRoundTrip";
                mesh.uv2 = new[] { Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0.5f, 0f) };
                mesh.RecalculateUVDistributionMetrics();
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(16f, mesh.GetUVDistributionMetric(1));

                clone = (Mesh)mesh.CloneObject();
                Assert.Equal(4f, clone.GetUVDistributionMetric(0));
                Assert.Equal(16f, clone.GetUVDistributionMetric(1));

                var yaml = UnityYamlWriter.Write(new Object[] { mesh });
                Assert.Contains("'m_MeshMetrics[0]': 4", yaml);
                Assert.Contains("'m_MeshMetrics[1]': 16", yaml);

                reloaded = new Mesh();
                reloaded.ReadSerialized(TestYaml.DocumentBody(yaml), new NullResolver());
                Assert.Equal(4f, reloaded.GetUVDistributionMetric(0));
                Assert.Equal(16f, reloaded.GetUVDistributionMetric(1));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                if (clone != null) Object.DestroyImmediate(clone);
                if (reloaded != null) Object.DestroyImmediate(reloaded);
            }
        }

        [Fact]
        public void NonReadableMeshRefusesRecalculateInPlayModeAndLogsOnce()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = MakeRightTriangles((2f, 1f));
            mesh.name = "LockedMesh";
            mesh.UploadMeshData(true);
            var logs = new List<(LogType Type, string Message)>();
            var prevHandler = Debug.unityLogger.logHandler;
            bool wasPlaying = EditorSession.IsPlaying;
            try
            {
                Debug.unityLogger.logHandler = new LogCapture(logs);
                EditorSession.IsPlaying = true;

                mesh.RecalculateUVDistributionMetrics();
                Assert.Equal(1f, mesh.GetUVDistributionMetric(0));
                Assert.Equal(1f, mesh.GetUVDistributionMetric(1));
                Assert.Single(logs);
                Assert.Equal(LogType.Error, logs[0].Type);
                Assert.Equal("Not allowed to call RecalculateUVDistributionMetrics() on mesh 'LockedMesh'", logs[0].Message);

                logs.Clear();
                mesh.RecalculateUVDistributionMetric(0);
                Assert.Equal(1f, mesh.GetUVDistributionMetric(0));
                Assert.Single(logs);
                Assert.Equal("Not allowed to call RecalculateUVDistributionMetric() on mesh 'LockedMesh'", logs[0].Message);

                // Even an out-of-range uvSetIndex logs before range checking (matches Unity 6000.0).
                logs.Clear();
                mesh.RecalculateUVDistributionMetric(8);
                Assert.Single(logs);
                Assert.Equal("Not allowed to call RecalculateUVDistributionMetric() on mesh 'LockedMesh'", logs[0].Message);

                // Control: outside Play mode, access is allowed and computes 4f without logging.
                logs.Clear();
                EditorSession.IsPlaying = false;
                mesh.RecalculateUVDistributionMetrics();
                Assert.Empty(logs);
                Assert.Equal(4f, mesh.GetUVDistributionMetric(0));
            }
            finally
            {
                EditorSession.IsPlaying = wasPlaying;
                Debug.unityLogger.logHandler = prevHandler;
                Object.DestroyImmediate(mesh);
            }
        }

        [Fact]
        public void ExistingMeshAssetSavePersistsUpdatedMetrics()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = MakeRightTriangles((2f, 1f));
            Mesh reloaded = null;
            try
            {
                mesh.name = "SavedExistingMesh";
                mesh.uv2 = new[] { Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0.5f, 0f) };
                var initialYaml = UnityYamlWriter.Write(new Object[] { mesh });
                var docs = UnityYamlText.Parse(initialYaml);
                Assert.Single(docs);
                Assert.Equal("1", docs[0].Body.Get("'m_MeshMetrics[0]'")?.Raw);
                Assert.Equal("1", docs[0].Body.Get("'m_MeshMetrics[1]'")?.Raw);

                mesh.RecalculateUVDistributionMetrics();
                UnityEditor.EditorUtility.SetDirty(mesh);
                var ids = new Dictionary<Object, long>(System.Collections.Generic.ReferenceEqualityComparer.Instance) { [mesh] = docs[0].FileId };
                int unlocated = 0;
                Assert.True(NativeDocuments.Write(docs[0], mesh, ids, null, ref unlocated));
                Assert.Equal(0, unlocated);

                var updatedYaml = UnityYamlText.Write(docs);
                Assert.Contains("'m_MeshMetrics[0]': 4", updatedYaml);
                Assert.Contains("'m_MeshMetrics[1]': 16", updatedYaml);

                reloaded = new Mesh();
                reloaded.ReadSerialized(TestYaml.DocumentBody(updatedYaml), new NullResolver());
                Assert.Equal(4f, reloaded.GetUVDistributionMetric(0));
                Assert.Equal(16f, reloaded.GetUVDistributionMetric(1));

                // YText.ToNode() must unquote single-quoted keys so ReadSerialized finds m_MeshMetrics[0..1].
                var fromNode = new Mesh();
                try
                {
                    fromNode.ReadSerialized(docs[0].Body.ToNode(), new NullResolver());
                    Assert.Equal(4f, fromNode.GetUVDistributionMetric(0));
                    Assert.Equal(16f, fromNode.GetUVDistributionMetric(1));
                }
                finally { Object.DestroyImmediate(fromNode); }
            }
            finally
            {
                UnityEditor.EditorUtility.ClearDirty(mesh);
                Object.DestroyImmediate(mesh);
                if (reloaded != null) Object.DestroyImmediate(reloaded);
            }
        }

        [Fact]
        public void SingleQuotedYamlKeysRoundTripInSequencesAndMapsWithoutBreakingQuotedScalars()
        {
            const string yaml =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!114 &11400000\n" +
                "MonoBehaviour:\n" +
                "  weights:\n" +
                "  - 'weight[0]': 1\n" +
                "    'weight[1]': 0\n" +
                "  harmonics:\n" +
                "  - 'sh[ 0]': 17\n" +
                "    'sh[ 1]': 17.5\n" +
                "  scalars:\n" +
                "  - 'a: b'\n" +
                "  - 'end:'\n" +
                "  after: 12\n";

            var docs = UnityYamlText.Parse(yaml);
            Assert.Single(docs);
            Assert.Equal(yaml, UnityYamlText.Write(docs));

            var body = docs[0].Body;
            Assert.Equal("12", body.Get("after")?.Raw);
            var weights = body.Get("weights");
            Assert.Equal(YKind.Seq, weights.Kind);
            Assert.Single(weights.Items);
            Assert.Equal(YKind.Map, weights.Items[0].Kind);
            Assert.Equal("1", weights.Items[0].Get("'weight[0]'")?.Raw);
            Assert.Equal("0", weights.Items[0].Get("'weight[1]'")?.Raw);

            var scalars = body.Get("scalars");
            Assert.Equal(2, scalars.Items.Count);
            Assert.Equal(YKind.Scalar, scalars.Items[0].Kind);
            Assert.Equal("a: b", scalars.Items[0].Text);
            Assert.Equal(YKind.Scalar, scalars.Items[1].Kind);
            Assert.Equal("end:", scalars.Items[1].Text);

            var node = body.ToNode();
            Assert.Equal(12, node.Int("after"));
            var wNode = node.Get("weights").Items[0];
            Assert.Equal(1f, wNode.Float("weight[0]"));
            Assert.Equal(0f, wNode.Float("weight[1]"));
            var hNode = node.Get("harmonics").Items[0];
            Assert.Equal(17f, hNode.Float("sh[ 0]"));
            Assert.Equal(17.5f, hNode.Float("sh[ 1]"));

            const string meta =
                "TextureImporter:\n" +
                "  weights:\n" +
                "  - 'weight[0]': 1\n" +
                "    'weight[1]': 0\n" +
                "  userData: keep\n";
            var top = UnityYamlText.ParseTopLevel(meta);
            Assert.Equal(meta, UnityYamlText.WriteTopLevel(top));
            Assert.Equal("keep", top.Get("TextureImporter").Get("userData")?.Raw);
            Assert.Equal(1f, top.ToNode().Get("TextureImporter").Get("weights").Items[0].Float("weight[0]"));
        }

        [Fact]
        public void MultiSubMeshTruncatesTrailingIndicesToTriangleMultiple()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[]
                {
                    Vector3.zero, new Vector3(0f, 2f, 0f), new Vector3(2f, 0f, 0f),
                    new Vector3(10f, 0f, 0f), new Vector3(10f, 2f, 0f), new Vector3(12f, 0f, 0f),
                };
                mesh.uv = new[]
                {
                    Vector2.zero, new Vector2(0f, 1f), new Vector2(1f, 0f),
                    Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0.5f, 0f),
                };
                mesh.subMeshCount = 2;
                // Submesh 0 has 4 indices (ratio 4 + 1 trailing index); submesh 1 has 3 indices (ratio 16).
                // Mean of {4, 16} + upper deviation = 10 + 6 = 16; without % 3 truncation submesh 1 is
                // shifted into a degenerate triangle and the metric stays 4.
                mesh.m_SubMeshes[0].Indices = new[] { 0, 1, 2, 0 };
                mesh.m_SubMeshes[1].Indices = new[] { 3, 4, 5 };
                mesh.RecalculateUVDistributionMetrics();
                Assert.Equal(16f, mesh.GetUVDistributionMetric(0));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void UnmeasuredReleaseKeepsDefaultMetricsAndReportsIgnored()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var mesh = MakeRightTriangles((1f, 1f), (1.41421356f, 1f), (1.73205081f, 1f), (2f, 1f), (2.23606798f, 1f));
            var logs = new List<(LogType Type, string Message)>();
            var prevHandler = Debug.unityLogger.logHandler;
            try
            {
                Stubs.Forget("Mesh.RecalculateUVDistributionMetrics");
                Stubs.Forget("Mesh.RecalculateUVDistributionMetric");
                Stubs.Forget("Mesh.GetUVDistributionMetric");
                Debug.unityLogger.logHandler = new LogCapture(logs);

                mesh.RecalculateUVDistributionMetrics();
                mesh.RecalculateUVDistributionMetric(0);
                Assert.Equal(1f, mesh.GetUVDistributionMetric(0));
                Assert.Contains(logs, l => l.Type == LogType.Warning && l.Message.Contains("Mesh.RecalculateUVDistributionMetrics"));
                Assert.Contains(logs, l => l.Type == LogType.Warning && l.Message.Contains("Mesh.RecalculateUVDistributionMetric "));
                Assert.Contains(logs, l => l.Type == LogType.Warning && l.Message.Contains("Mesh.GetUVDistributionMetric"));
            }
            finally
            {
                Debug.unityLogger.logHandler = prevHandler;
                Object.DestroyImmediate(mesh);
            }
        }

        [Fact]
        public void QuadsSubMeshSkipsWithNotice()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var mesh = new Mesh();
            var logs = new List<(LogType Type, string Message)>();
            var prevHandler = Debug.unityLogger.logHandler;
            try
            {
                Stubs.Forget("Mesh.RecalculateUVDistributionMetric(Quads)");
                Debug.unityLogger.logHandler = new LogCapture(logs);

                mesh.vertices = new[]
                {
                    Vector3.zero, new Vector3(0f, 2f, 0f), new Vector3(2f, 2f, 0f), new Vector3(2f, 0f, 0f),
                };
                mesh.uv = new[]
                {
                    Vector2.zero, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                };
                mesh.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);
                mesh.RecalculateUVDistributionMetrics();
                Assert.Equal(1f, mesh.GetUVDistributionMetric(0));
                Assert.Single(logs);
                Assert.Equal(LogType.Warning, logs[0].Type);
                Assert.Contains("MeshTopology.Quads", logs[0].Message);
            }
            finally
            {
                Debug.unityLogger.logHandler = prevHandler;
                Object.DestroyImmediate(mesh);
            }
        }

        [Fact]
        public void ImporterMetaRewrittenPreservesAndPatchesSingleQuotedKeysInSequences()
        {
            const string meta =
                "fileFormatVersion: 2\n" +
                "guid: 11111111111111111111111111111111\n" +
                "TextureImporter:\n" +
                "  serializedVersion: 13\n" +
                "  spriteSheet:\n" +
                "    weights:\n" +
                "    - 'weight[0]': 1\n" +
                "      'weight[1]': 0\n" +
                "      'boneIndex[0]': 0\n" +
                "  userData: old\n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";

            var parsed = UnityYamlText.ParseTopLevel(meta).Get("TextureImporter").ToNode();
            var unwritten = new List<string>();
            // Unchanged settings must report 0 unwritten keys and return null (no write).
            var unchanged = ImporterMeta.Rewritten(meta, "TextureImporter", parsed, null, ("old", "", ""), "", unwritten);
            Assert.Null(unchanged);
            Assert.Empty(unwritten);

            // Editing userData alone writes cleanly with 0 unwritten keys and preserves single-quoted keys.
            var userDataOnly = ImporterMeta.Rewritten(meta, "TextureImporter", parsed, null, ("updated", "", ""), "", unwritten);
            Assert.NotNull(userDataOnly);
            Assert.Empty(unwritten);
            Assert.Contains("- 'weight[0]': 1", userDataOnly);
            Assert.Contains("userData: updated", userDataOnly);

            // Editing a single-quoted weight value patches under the original raw quoted key.
            var weightMap = parsed.Get("spriteSheet").Get("weights").Items[0];
            for (int i = 0; i < weightMap.Keys.Length; i++)
                if (weightMap.Keys[i] == "weight[0]") weightMap.Values[i] = YNode.MakeScalar("0.75");
            var editedWeight = ImporterMeta.Rewritten(meta, "TextureImporter", parsed, null, ("old", "", ""), "", unwritten);
            Assert.NotNull(editedWeight);
            Assert.Empty(unwritten);
            Assert.Contains("- 'weight[0]': 0.75", editedWeight);
            Assert.Contains("  'weight[1]': 0", editedWeight);
        }

        [Fact]
        public void PrefabValuesFlattenAndSetPathMatchSingleQuotedKeys()
        {
            const string yaml =
                "%YAML 1.1\n" +
                "%TAG !u! tag:unity3d.com,2011:\n" +
                "--- !u!114 &11400000\n" +
                "MonoBehaviour:\n" +
                "  m_AmbientProbe:\n" +
                "    'sh[ 0]': 1\n" +
                "    'sh[ 1]': 2\n";
            var docs = UnityYamlText.Parse(yaml);
            var body = docs[0].Body;
            var flat = PrefabValues.ByPath(PrefabValues.Flatten(body));
            Assert.True(flat.ContainsKey("m_AmbientProbe.sh[ 0]"));
            Assert.Equal("1", flat["m_AmbientProbe.sh[ 0]"].Raw);

            PrefabValues.SetPath(body, "m_AmbientProbe.sh[ 0]", YText.Scalar("3.5"));
            var written = UnityYamlText.Write(docs);
            Assert.Contains("'sh[ 0]': 3.5", written);
            Assert.Contains("'sh[ 1]': 2", written);
        }

        sealed class LogCapture : ILogHandler
        {
            readonly List<(LogType Type, string Message)> m_Logs;
            public LogCapture(List<(LogType Type, string Message)> logs) => m_Logs = logs;
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Logs.Add((logType, args == null || args.Length == 0 ? format : string.Format(format, args)));
            public void LogException(System.Exception exception, Object context) { }
        }

        sealed class MeshScope : System.IDisposable
        {
            readonly List<Mesh> m_Meshes = new();
            public Mesh Add(Mesh m) { m_Meshes.Add(m); return m; }
            public void Dispose() { foreach (var m in m_Meshes) Object.DestroyImmediate(m); }
        }
    }
}
