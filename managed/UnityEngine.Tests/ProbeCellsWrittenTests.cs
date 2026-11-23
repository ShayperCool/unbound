using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The cells of a bake's light probes as its lighting data keeps them, against the
    /// Editor's own bakes of tests/projects/bakedbuffers' probe scenes (their LightProbes objects,
    /// lightingData.json).</summary>
    public class ProbeCellsWrittenTests
    {
        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", ".."));

        static readonly string[] Scenes = { "Probes", "ProbesGrid", "ProbesMore", "ProbesSky", "ProbesSkyEven" };

        /// <summary>The LightProbes object's fields of a scene's Editor bake.</summary>
        static JsonElement Probes(string scene)
        {
            var path = Path.Combine(Repo(), "tests", "projects", "bakedbuffers", "Reference", "unity-6000.0.71f1", scene, "lightingData.json");
            var objects = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            return objects.EnumerateArray().First(o => o.GetProperty("classId").GetInt32() == 258).GetProperty("fields").Clone();
        }

        static float F(JsonElement e) => float.Parse(e.GetString(), System.Globalization.CultureInfo.InvariantCulture);
        static int I(JsonElement e) => int.Parse(e.GetString(), System.Globalization.CultureInfo.InvariantCulture);
        static Vector3 V(JsonElement e) => new Vector3(F(e.GetProperty("x")), F(e.GetProperty("y")), F(e.GetProperty("z")));

        static Vector3[] Positions(JsonElement probes) =>
            probes.GetProperty("m_Data").GetProperty("m_Positions").EnumerateArray().Select(V).ToArray();

        static JsonElement[] Cells(JsonElement probes) =>
            probes.GetProperty("m_Data").GetProperty("m_Tetrahedralization").GetProperty("m_Tetrahedra").EnumerateArray().ToArray();

        static int[] Ints(JsonElement cell, string name) => Enumerable.Range(0, 4).Select(k => I(cell.GetProperty($"{name}[{k}]"))).ToArray();

        static float[] Matrix(JsonElement cell) =>
            Enumerable.Range(0, 12).Select(k => F(cell.GetProperty("matrix").GetProperty($"e{k / 4}{k % 4}"))).ToArray();

        [Fact]
        public void TheEditorsCellsComeOutAsItKeepsThemBitForBit()
        {
            foreach (var scene in Scenes)
            {
                var probes = Probes(scene);
                var p = Positions(probes);
                var cells = Cells(probes);
                // The Editor's tetrahedralization as the pass takes it: its inner cells, then its
                // hull triangles with their inner cells.
                var inner = cells.Select(c => Ints(c, "indices")).Where(i => i[3] >= 0).ToList();
                var outer = cells.Where(c => I(c.GetProperty("indices[3]")) < 0).ToList();
                var written = ProbeTetrahedralization.Finish(p, inner, outer.Select(c => Ints(c, "indices").Take(3).ToArray()).ToList(),
                    outer.Select(c => I(c.GetProperty("neighbors[3]"))).ToList());
                Assert.False(written.Degenerate);
                Assert.Equal(cells.Length, written.Indices.Length);
                for (int c = 0; c < cells.Length; c++)
                {
                    Assert.Equal(Ints(cells[c], "indices"), written.Indices[c]);
                    Assert.Equal(Ints(cells[c], "neighbors"), written.Neighbors[c]);
                    Assert.Equal(cells[c].GetProperty("isValid").GetString() == "true", written.Valid[c]);
                    var want = Matrix(cells[c]);
                    for (int k = 0; k < 12; k++)
                        Assert.True(BitConverter.SingleToInt32Bits(want[k]) == BitConverter.SingleToInt32Bits(written.Matrices[c][k]),
                            $"{scene} cell {c} e{k / 4}{k % 4}: {written.Matrices[c][k]:R}, expected {want[k]:R}");
                }
                var rays = probes.GetProperty("m_Data").GetProperty("m_Tetrahedralization").GetProperty("m_HullRays").EnumerateArray().Select(V).ToArray();
                Assert.Equal(rays.Length, written.HullRays.Length);
                for (int i = 0; i < rays.Length; i++)
                    for (int a = 0; a < 3; a++)
                        Assert.True(BitConverter.SingleToInt32Bits(rays[i][a]) == BitConverter.SingleToInt32Bits(written.HullRays[i][a]),
                            $"{scene} ray {i}: {written.HullRays[i]}, expected {rays[i]}");
            }
        }

        [Fact]
        public void UnboundsCellsAreTheEditorsInItsOwnOrder()
        {
            // The same cells (by their probes) and hull; the cells' order, their corners' order
            // and so their matrices are Unbound's own.
            foreach (var scene in Scenes)
            {
                var probes = Probes(scene);
                var written = ProbeTetrahedralization.Written(Positions(probes));
                string Key(int[] indices) => string.Join(",", indices.OrderBy(v => v));
                var want = Cells(probes).Select(c => Key(Ints(c, "indices"))).OrderBy(k => k, StringComparer.Ordinal);
                Assert.Equal(want, written.Indices.Select(Key).OrderBy(k => k, StringComparer.Ordinal));
                // Each cell's neighbors are the cells across its faces, both ways.
                for (int c = 0; c < written.Indices.Length; c++)
                    foreach (var nb in written.Neighbors[c])
                        Assert.Contains(c, written.Neighbors[nb]);
                // The first cell holds the probes' mean point.
                var p = Positions(probes);
                var mean = p.Aggregate(Vector3.zero, (s, q) => s + q) / p.Length;
                var m = written.Matrices[0];
                var d = mean - p[written.Indices[0][3]];
                var w = new[] { m[0] * d.x + m[1] * d.y + m[2] * d.z, m[4] * d.x + m[5] * d.y + m[6] * d.z, m[8] * d.x + m[9] * d.y + m[10] * d.z };
                Assert.True(w.Min() >= -1e-6f && 1 - w.Sum() >= -1e-6f, $"{scene}: {string.Join(", ", w)}");
            }
        }

        [Fact]
        public void FewerThanFourProbesMakeNoCellsAndProbesInOnePlaneAreNotWritten()
        {
            var three = ProbeTetrahedralization.Written(new[] { Vector3.zero, Vector3.right, Vector3.up });
            Assert.Empty(three.Indices);
            Assert.Empty(three.HullRays);
            Assert.Null(ProbeTetrahedralization.Written(new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) }));
        }

        static BakeResult WithProbes(string probes) =>
            new BakeResult { LightProbes = JsonDocument.Parse(probes).RootElement.Clone() };

        [Fact]
        public void TheBakesLightProbesAreWrittenWithTheirCellsUnlessNotMeasured()
        {
            const string corners = "[[0,0,0],[1,0,0],[0,1,0],[0,0,1]]";
            string Reply(string positions, string unmeasured) =>
                $"{{\"positions\":{positions},\"sets\":[],\"coefficients\":[],\"occlusion\":[],\"unmeasured\":{unmeasured}}}";
            var made = RealtimeGI.LightProbesOf(null, WithProbes(Reply(corners, "null")), out var why);
            Assert.Null(why);
            var json = JsonDocument.Parse(JsonSerializer.Serialize(made)).RootElement;
            // One inner cell and its four hull triangles; a ray per probe.
            Assert.Equal(5, json.GetProperty("tetrahedra").GetArrayLength());
            Assert.Equal(4, json.GetProperty("hullRays").GetArrayLength());
            Assert.Equal(JsonValueKind.Array, json.GetProperty("positions").ValueKind);
            // What the bake says is not measured, and probes in one plane: not written.
            Assert.Null(RealtimeGI.LightProbesOf(null, WithProbes(Reply(corners, "\"probes within 5 cm\"")), out why));
            Assert.Equal("probes within 5 cm is not measured", why);
            Assert.Null(RealtimeGI.LightProbesOf(null, WithProbes(Reply("[[0,0,0],[1,0,0],[0,1,0],[1,1,0]]", "null")), out why));
            Assert.Contains("one plane", why);
            // No bake, or a bake without probes of a scene without groups: nothing, nothing said.
            Assert.Null(RealtimeGI.LightProbesOf(null, null, out why));
            Assert.Null(why);
            Assert.Null(RealtimeGI.LightProbesOf(null, new BakeResult(), out why));
            Assert.Null(why);
        }
    }
}
