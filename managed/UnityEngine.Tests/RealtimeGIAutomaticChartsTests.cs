using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>What precomputed realtime GI refuses of a renderer with Optimize Realtime UVs
    /// before its charts are made (docs/REALTIME_GI.md, Automatic charts): what is not measured,
    /// and renderers whose charts the precompute's rounding of a turned instance decides.</summary>
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

        static MeshRenderer Make(string name, Mesh mesh, Vector3 euler, Vector3 scale, List<Object> made)
        {
            var go = new GameObject(name);
            made.Add(go);
            go.transform.rotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        [Fact]
        public void TurnedRenderersAndThoseOfTheirMeshAndScaleAreRefused()
        {
            var made = new List<Object>();
            try
            {
                var plate = Plate();
                var other = Plate();
                made.Add(plate);
                made.Add(other);
                var level = Make("Level", plate, Vector3.zero, Vector3.one, made);
                var turned = Make("Turned", plate, new Vector3(0f, 30f, 0f), Vector3.one, made);
                var larger = Make("Larger", plate, Vector3.zero, new Vector3(2f, 1f, 1f), made);
                var apart = Make("Apart", other, Vector3.zero, Vector3.one, made);
                // A turned renderer that keeps its mesh's charts shares none with the others.
                var kept = Make("Kept", other, new Vector3(0f, 30f, 0f), Vector3.one, made);
                kept.m_PreserveUVs = true;

                var shapes = RealtimeGI.TurnedShapes(new Renderer[] { level, turned, larger, apart, kept, null });
                Assert.Single(shapes);
                Assert.Contains((plate, Vector3.one), shapes);

                Assert.Contains("is turned", RealtimeGI.AutomaticChartsRefusal(turned, plate, shapes));
                Assert.Contains("the mesh and scale of a turned renderer", RealtimeGI.AutomaticChartsRefusal(level, plate, shapes));
                Assert.Null(RealtimeGI.AutomaticChartsRefusal(larger, plate, shapes));
                Assert.Null(RealtimeGI.AutomaticChartsRefusal(apart, other, shapes));
                // Without the turned one in the scene, the level one is made.
                Assert.Null(RealtimeGI.AutomaticChartsRefusal(level, plate, new HashSet<(Mesh, Vector3)>()));
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
            var none = new HashSet<(Mesh, Vector3)>();
            try
            {
                var plate = Plate();
                var bare = Plate(normals: false);
                made.Add(plate);
                made.Add(bare);

                var near = Make("Near", plate, Vector3.zero, Vector3.one, made);
                near.m_AutoUVMaxDistance = 0f;
                Assert.Contains("a limit of zero", RealtimeGI.AutomaticChartsRefusal(near, plate, none));
                var narrow = Make("Narrow", plate, Vector3.zero, Vector3.one, made);
                narrow.m_AutoUVMaxAngle = 0f;
                Assert.Contains("a limit of zero", RealtimeGI.AutomaticChartsRefusal(narrow, plate, none));

                var mirrored = Make("Mirrored", plate, Vector3.zero, new Vector3(-1f, 1f, 1f), made);
                Assert.Contains("a zero or mirrored scale", RealtimeGI.AutomaticChartsRefusal(mirrored, plate, none));
                var flat = Make("Flat", plate, Vector3.zero, new Vector3(1f, 0f, 1f), made);
                Assert.Contains("a zero or mirrored scale", RealtimeGI.AutomaticChartsRefusal(flat, plate, none));

                var unlit = Make("Unlit", bare, Vector3.zero, Vector3.one, made);
                Assert.Contains("no normals", RealtimeGI.AutomaticChartsRefusal(unlit, bare, none));

                var whole = Make("Whole", plate, Vector3.zero, new Vector3(2f, 3f, 0.5f), made);
                Assert.Null(RealtimeGI.AutomaticChartsRefusal(whole, plate, none));
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
