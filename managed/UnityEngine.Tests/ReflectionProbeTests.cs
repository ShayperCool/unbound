using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class ReflectionProbeTests
    {
        static ReflectionProbe Probe(string name, Vector3 position, Vector3 size, int importance, Vector3 center = default)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.size = size;
            probe.center = center;
            probe.importance = importance;
            probe.customBakedTexture = new Cubemap(4, TextureFormat.RGBA32, false);
            return probe;
        }

        static string Closest(Vector3 position, float scale = 1f, ReflectionProbeUsage usage = ReflectionProbeUsage.BlendProbes)
        {
            var go = new GameObject("R");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.reflectionProbeUsage = usage;
            var list = new List<ReflectionProbeBlendInfo>();
            renderer.GetClosestReflectionProbes(list);
            Object.DestroyImmediate(go);
            return string.Join("; ", list.Select(b => $"{b.probe.name}:{b.weight:F3}"));
        }

        [Fact]
        public void RenderersReflectTheProbesTheEditorPicks()
        {
            // The 6000.0 Editor's GetClosestReflectionProbes for the same layout.
            var probes = new[]
            {
                Probe("A", Vector3.zero, new Vector3(10, 10, 10), 1),
                Probe("B", new Vector3(4, 0, 0), new Vector3(4, 4, 4), 1),
                Probe("C", new Vector3(-4, 0, 0), new Vector3(4, 4, 4), 2),
                Probe("D", new Vector3(20, 0, 0), new Vector3(4, 4, 4), 0),
                Probe("E", new Vector3(0, 0, 20), new Vector3(4, 4, 4), 0, new Vector3(3, 0, 0)),
            };
            try
            {
                Assert.Equal("A:1.000", Closest(Vector3.zero));
                Assert.Equal("B:0.500; A:0.500", Closest(new Vector3(4, 0, 0)));
                Assert.Equal("B:1.000; A:0.000", Closest(new Vector3(5.5f, 0, 0)));
                Assert.Equal("C:1.000; A:0.000", Closest(new Vector3(-4, 0, 0)));
                Assert.Equal("C:0.296; A:0.704", Closest(new Vector3(-4, 0, 0), 6f));
                Assert.Equal("", Closest(new Vector3(0, 5.6f, 0)));
                Assert.Equal("D:1.000", Closest(new Vector3(21.9f, 0, 0)));
                Assert.Equal("E:1.000", Closest(new Vector3(3, 0, 20)));
                Assert.Equal("", Closest(new Vector3(0, 0, 20)));
                Assert.Equal("B:1.000; A:0.000", Closest(new Vector3(5.5f, 0, 0), 1f, ReflectionProbeUsage.Simple));
                Assert.Equal("", Closest(Vector3.zero, 1f, ReflectionProbeUsage.Off));
                probes[0].intensity = 0.9f;
                Assert.Equal(new Vector4(0.9f, 1f, 0f, 0f), probes[0].textureHDRDecodeValues);
            }
            finally
            {
                foreach (var p in probes) Object.DestroyImmediate(p.gameObject);
            }
        }

        static Dictionary<int, object> Apply(Vector3 position, ReflectionProbeUsage usage, bool srp) =>
            ReflectionProbe.Apply(new Bounds(position, Vector3.one), usage, null, srp);

        static Vector4 V(Dictionary<int, object> p, string name) => (Vector4)p[Shader.PropertyToID(name)];

        [Fact]
        public void RenderPipelineDrawsGetBlendDistancesAndTheImportanceSign()
        {
            // The URP forward path's per-object constants in the 6000.0 Editor
            // (tests/projects/urp per-object-constants.jsonl).
            var low = Probe("Low", new Vector3(-1.5f, 1, 0), new Vector3(4, 3, 5), 1);
            low.blendDistance = 1f;
            low.boxProjection = true;
            var high = Probe("High", new Vector3(1.5f, 1, 0), new Vector3(4, 3, 5), 2);
            high.blendDistance = 0.5f;
            try
            {
                // Two probes: the more important first, the sign of its importance over the second.
                var both = Apply(new Vector3(0, 0.6f, 0), ReflectionProbeUsage.BlendProbes, true);
                Assert.Equal(new Vector4(-0.5f, -0.5f, -2.5f, 1f), V(both, "unity_SpecCube0_BoxMin"));
                Assert.Equal(new Vector4(3.5f, 2.5f, 2.5f, 0.5f), V(both, "unity_SpecCube0_BoxMax"));
                Assert.Equal(new Vector4(-3.5f, -0.5f, -2.5f, 1f), V(both, "unity_SpecCube1_BoxMin"));
                Assert.Equal(new Vector4(0.5f, 2.5f, 2.5f, 1f), V(both, "unity_SpecCube1_BoxMax"));
                Assert.Equal(new Vector4(-1.5f, 1f, 0f, 1f), V(both, "unity_SpecCube1_ProbePosition"));
                // Simple samples both too: the pipeline's setting decides.
                Assert.Equal(V(both, "unity_SpecCube1_BoxMin"), V(Apply(new Vector3(0, 0.6f, 0), ReflectionProbeUsage.Simple, true), "unity_SpecCube1_BoxMin"));
                // One probe: the second slot is the default reflection over an unbounded box.
                var one = Apply(new Vector3(2.2f, 0.6f, 0), ReflectionProbeUsage.BlendProbes, true);
                Assert.Equal(new Vector4(3.5f, 2.5f, 2.5f, 0.5f), V(one, "unity_SpecCube0_BoxMax"));
                Assert.Equal(new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, 1f), V(one, "unity_SpecCube1_BoxMin"));
                Assert.Equal(new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, 0f), V(one, "unity_SpecCube1_BoxMax"));
                Assert.Equal(Vector4.zero, V(one, "unity_SpecCube1_ProbePosition"));
                // Equally important probes: no dominance (sign 0).
                high.importance = 1;
                var equal = Apply(new Vector3(0, 0.6f, 0), ReflectionProbeUsage.BlendProbes, true);
                Assert.Equal(0f, V(equal, "unity_SpecCube1_BoxMin").w);
                // No probe: nothing per object (the frame's unbounded defaults).
                Assert.Null(Apply(new Vector3(0, 0.6f, -9f), ReflectionProbeUsage.BlendProbes, true));
                Assert.Null(Apply(new Vector3(0, 0.6f, 0), ReflectionProbeUsage.Off, true));
                // Control, the built-in pipeline's layout: no blend distance; Simple keeps one probe.
                high.importance = 2;
                var builtin = Apply(new Vector3(0, 0.6f, 0), ReflectionProbeUsage.BlendProbes, false);
                Assert.Equal(0f, V(builtin, "unity_SpecCube0_BoxMax").w);
                Assert.Equal(0f, V(builtin, "unity_SpecCube1_BoxMin").w);
                var simple = Apply(new Vector3(0, 0.6f, 0), ReflectionProbeUsage.Simple, false);
                Assert.Equal(V(simple, "unity_SpecCube0_BoxMax"), V(simple, "unity_SpecCube1_BoxMax"));
            }
            finally
            {
                Object.DestroyImmediate(low.gameObject);
                Object.DestroyImmediate(high.gameObject);
            }
        }
    }
}
