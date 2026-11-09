using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Forward draws take a reflection probe's changes from the next frame on, while
    /// Renderer.GetClosestReflectionProbes answers with them at once (tests/projects/reflectionprobes
    /// lag.*, forward_lag_same, forward_lag_next).</summary>
    public class ReflectionProbeFrameTests
    {
        static ReflectionProbe Probe(string name, Vector3 position, List<GameObject> made)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            made.Add(go);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(4f, 4f, 4f);
            return probe;
        }

        [Fact]
        public void ForwardDrawsSeeAProbeChangeFromTheNextFrame()
        {
            var made = new List<GameObject>();
            try
            {
                var a = Probe("A", Vector3.zero, made);
                var b = Probe("B", new Vector3(2f, 0f, 0f), made);
                // A holds more of the bounds: first while the importances are equal.
                var bounds = new Bounds(new Vector3(0.9f, 0f, 0f), Vector3.one);
                var live = new List<ReflectionProbeBlendInfo>();
                var drawn = new List<ReflectionProbeBlendInfo>();
                ReflectionProbe.BeginFrame();
                b.importance = 2;
                ReflectionProbe.Closest(bounds, ReflectionProbeUsage.BlendProbes, live, drawn: false);
                ReflectionProbe.Closest(bounds, ReflectionProbeUsage.BlendProbes, drawn, drawn: true);
                Assert.Same(b, live[0].probe);
                Assert.Same(a, drawn[0].probe);
                ReflectionProbe.BeginFrame();
                ReflectionProbe.Closest(bounds, ReflectionProbeUsage.BlendProbes, drawn, drawn: true);
                Assert.Same(b, drawn[0].probe);
                // A probe enabled during the frame counts with its live placement.
                var c = Probe("C", new Vector3(1f, 0f, 0f), made);
                c.importance = 3;
                ReflectionProbe.Closest(bounds, ReflectionProbeUsage.BlendProbes, drawn, drawn: true);
                Assert.Same(c, drawn[0].probe);
                // Moved during the frame: rendering keeps the box it began with.
                var start = b.Drawn.Bounds;
                b.transform.position += new Vector3(5f, 0f, 0f);
                Assert.Equal(start, b.Drawn.Bounds);
                Assert.NotEqual(start, b.bounds);
                // Disabled and enabled again during the frame: its live placement.
                b.enabled = false;
                b.enabled = true;
                Assert.Equal(b.bounds, b.Drawn.Bounds);
            }
            finally
            {
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        }
    }
}
