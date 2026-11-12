using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>The instance matrix precomputed realtime GI makes the automatic charts of a
    /// renderer with Optimize Realtime UVs from, and what it refuses before they are made
    /// (docs/REALTIME_GI.md, Automatic charts).</summary>
    public class RealtimeGIAutomaticChartsTests
    {
        static Mesh Plate(bool normals = true)
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 1f), new Vector3(0f, 0f, 1f) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            if (normals) mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            return mesh;
        }

        static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);

        static Quaternion Q(uint x, uint y, uint z, uint w) => new Quaternion(F(x), F(y), F(z), F(w));

        static Vector3 V(uint x, uint y, uint z) => new Vector3(F(x), F(y), F(z));

        static GameObject Node(string name, Quaternion rotation, Vector3 scale, List<Object> made, Transform parent = null)
        {
            var go = new GameObject(name);
            made.Add(go);
            if (parent != null) go.transform.SetParent(parent, false);
            // As a scene file gives them: the serialized values, not normalized again.
            go.transform.m_LocalRotation = rotation;
            go.transform.localScale = scale;
            return go;
        }

        static MeshRenderer Make(string name, Mesh mesh, Quaternion rotation, Vector3 scale, List<Object> made)
        {
            var go = Node(name, rotation, scale, made);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        static void AssertBits(uint[] expected, float[] actual)
        {
            Assert.Equal(9, actual.Length);
            var bits = Array.ConvertAll(actual, BitConverter.SingleToUInt32Bits);
            Assert.Equal(expected, bits);
        }

        /// <summary>The transforms of tests/projects/realtimegi RoomTurned and the matrices whose
        /// positions and normals are the Editor's cached precompute input to the last bit. (A
        /// cube of 0.35 turned 25 degrees about Y has the matrix of an unturned one, and the
        /// two share their charts.)</summary>
        [Fact]
        public void TheInstanceMatrixIsTheEditorsToTheLastBit()
        {
            var made = new List<Object>();
            try
            {
                // A slab turned 30 degrees about Y: the rotation is taken out exactly.
                var slab = Node("SlabTurned", Q(0, 0x3e8483ef, 0, 0x3f7746eb), V(0x3fa66666, 0x3e4ccccd, 0x3f666666), made);
                AssertBits(new uint[] { 0x3fa66666, 0, 0, 0, 0x3e4ccccd, 0, 0, 0, 0x3f666666 }, RealtimeGI.WorldScale(slab.transform));
                // A box turned (20, 40, 10): rounding in the last digits off the diagonal.
                var box = Node("BoxTurned", Q(0x3e44845a, 0x3ea48423, 0x3cb00c30, 0x3f6d548d), V(0x3f333333, 0x3f000000, 0x3f19999a), made);
                AssertBits(new uint[] { 0x3f333333, 0x32400000, 0xb3000000, 0, 0x3f000000, 0x31c00000, 0xb3000000, 0xb1800000, 0x3f19999a },
                    RealtimeGI.WorldScale(box.transform));
                // A box turned 10 degrees about X under a parent turned 45 about Y and scaled 1.5.
                var pivot = Node("Pivot", Q(0, 0x3ec3ef16, 0, 0x3f6c835e), V(0x3fc00000, 0x3fc00000, 0x3fc00000), made);
                var child = Node("Child", Q(0x3db27eb5, 0, 0, 0x3f7f069e), V(0x3f000000, 0x3e99999a, 0x3ecccccd), made, pivot.transform);
                AssertBits(new uint[] { 0x3f400000, 0x32000000, 0, 0xb2000000, 0x3ee66667, 0x31800000, 0, 0, 0x3f19999a },
                    RealtimeGI.WorldScale(child.transform));
                // Not turned: the scale alone.
                var level = Node("Level", Quaternion.identity, new Vector3(2f, 3f, 0.5f), made);
                AssertBits(new uint[] { 0x40000000, 0, 0, 0, 0x40400000, 0, 0, 0, 0x3f000000 }, RealtimeGI.WorldScale(level.transform));
                var shared = Node("SharedTurned", Q(0, 0x3e5da259, 0, 0x3f79ee89), V(0x3eb33333, 0x3eb33333, 0x3eb33333), made);
                AssertBits(new uint[] { 0x3eb33333, 0, 0, 0, 0x3eb33333, 0, 0, 0, 0x3eb33333 }, RealtimeGI.WorldScale(shared.transform));
                // The same rotation with a scale of 0.45 leaves rounding off the diagonal.
                var unshared = Node("Turned", Q(0, 0x3e5da259, 0, 0x3f79ee89), V(0x3ee66666, 0x3ee66666, 0x3ee66666), made);
                AssertBits(new uint[] { 0x3ee66665, 0, 0x32800000, 0, 0x3ee66666, 0, 0xb2800000, 0, 0x3ee66665 }, RealtimeGI.WorldScale(unshared.transform));
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }

        /// <summary>Renderers of one mesh whose instance matrices round to the same thousandths
        /// share one geometry in the precompute, from one of their matrices: they are refused
        /// unless the matrices are the same to the last bit.</summary>
        [Fact]
        public void RenderersSharingARoundedMatrixWithAnotherAreRefused()
        {
            var made = new List<Object>();
            var none = new HashSet<Renderer>();
            try
            {
                var plate = Plate();
                var other = Plate();
                made.Add(plate);
                made.Add(other);
                var boxScale = V(0x3f333333, 0x3f000000, 0x3f19999a);
                var boxTurned = Make("BoxTurned", plate, Q(0x3e44845a, 0x3ea48423, 0x3cb00c30, 0x3f6d548d), boxScale, made);
                var boxFlat = Make("BoxFlat", plate, Quaternion.identity, boxScale, made);
                // Turned 25 degrees with a scale of 0.35: the very matrix of an unturned one.
                var small = V(0x3eb33333, 0x3eb33333, 0x3eb33333);
                var sharedTurned = Make("SharedTurned", plate, Q(0, 0x3e5da259, 0, 0x3f79ee89), small, made);
                var sharedFlat = Make("SharedFlat", plate, Quaternion.identity, small, made);
                // Another mesh, and a renderer that keeps its mesh's charts.
                var apart = Make("Apart", other, Quaternion.identity, boxScale, made);
                var kept = Make("Kept", plate, Quaternion.identity, boxScale, made);
                kept.m_PreserveUVs = true;

                var unshared = RealtimeGI.UnsharedRenderers(new Renderer[] { boxTurned, boxFlat, sharedTurned, sharedFlat, apart, kept, null });
                Assert.Equal(2, unshared.Count);
                Assert.Contains(boxTurned, unshared);
                Assert.Contains(boxFlat, unshared);
                Assert.Contains("turned otherwise", RealtimeGI.AutomaticChartsRefusal(boxFlat, plate, unshared));
                Assert.Null(RealtimeGI.AutomaticChartsRefusal(sharedTurned, plate, unshared));
                Assert.Null(RealtimeGI.AutomaticChartsRefusal(apart, other, unshared));
                // Alone in its scene the turned box is made.
                Assert.Empty(RealtimeGI.UnsharedRenderers(new Renderer[] { boxTurned, apart }));
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }

        [Fact]
        public void WhatIsNotMeasuredIsRefused()
        {
            var made = new List<Object>();
            var none = new HashSet<Renderer>();
            try
            {
                var plate = Plate();
                var bare = Plate(normals: false);
                made.Add(plate);
                made.Add(bare);

                var mirrored = Make("Mirrored", plate, Quaternion.identity, new Vector3(-1f, 1f, 1f), made);
                Assert.Contains("a zero or mirrored scale", RealtimeGI.AutomaticChartsRefusal(mirrored, plate, none));
                var flat = Make("Flat", plate, Quaternion.identity, new Vector3(1f, 0f, 1f), made);
                Assert.Contains("a zero or mirrored scale", RealtimeGI.AutomaticChartsRefusal(flat, plate, none));

                // Mirrored twice up the hierarchy: a positive lossy scale, and not measured.
                var mirror = Node("Mirror", Quaternion.identity, new Vector3(-1f, 1f, 1f), made);
                var twice = Make("Twice", plate, Quaternion.identity, new Vector3(-1f, 1f, 1f), made);
                twice.transform.SetParent(mirror.transform, false);
                Assert.True(twice.transform.lossyScale.x > 0f);
                Assert.Contains("a zero or mirrored scale", RealtimeGI.AutomaticChartsRefusal(twice, plate, none));

                var unlit = Make("Unlit", bare, Quaternion.identity, Vector3.one, made);
                Assert.Contains("no normals", RealtimeGI.AutomaticChartsRefusal(unlit, bare, none));

                // A turned renderer is made.
                var turned = Make("Turned", plate, Quaternion.Euler(20f, 40f, 10f), new Vector3(2f, 3f, 0.5f), made);
                Assert.Null(RealtimeGI.AutomaticChartsRefusal(turned, plate, none));
                Assert.True(RealtimeGI.MakesAutomaticCharts(turned));
                // With a limit of zero the precompute keeps the mesh's charts, as with Preserve
                // UVs (RoomTurned, StepNoDistance).
                turned.m_AutoUVMaxDistance = 0f;
                Assert.False(RealtimeGI.MakesAutomaticCharts(turned));
                turned.m_AutoUVMaxDistance = 0.5f;
                turned.m_AutoUVMaxAngle = 0f;
                Assert.False(RealtimeGI.MakesAutomaticCharts(turned));
                turned.m_AutoUVMaxAngle = 89f;
                turned.m_PreserveUVs = true;
                Assert.False(RealtimeGI.MakesAutomaticCharts(turned));
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
