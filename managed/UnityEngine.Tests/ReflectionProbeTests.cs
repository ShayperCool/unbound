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
    }
}
