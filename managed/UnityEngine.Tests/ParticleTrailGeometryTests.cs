using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace UnityEngine.Tests
{
    public class ParticleTrailGeometryTests
    {
        static ParticleSystem Make()
        {
            var go = new GameObject("curved-trail"); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
            var m = ps.main; m.playOnAwake = false; m.startSpeed = 3; m.startLifetime = 2.013f; m.startSize = .3f;
            var e = ps.emission; e.rateOverTime = 4;
            var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle;
            s.radius = .1f; s.arcMode = ParticleSystemShapeMultiModeValue.Loop; s.arcSpeed = .7f;
            var v = ps.velocityOverLifetime; v.enabled = true; v.orbitalZ = 1.5f;
            var t = ps.trails; t.enabled = true; t.lifetime = .5f; t.minVertexDistance = .05f; t.widthOverTrail = .5f;
            go.SetActive(true);
            for (int i = 0; i < 30; i++) ps.Simulate(1f / 30f, true, i == 0, false);
            return ps;
        }

        static void Near(float expected, float actual) => Assert.True(
            Math.Abs(expected - actual) <= 2e-4f + 2e-4f * Math.Abs(expected), $"expected {expected:R}, actual {actual:R}");

        [Fact]
        public void AutomaticTrailRetainsBirthCoordinateWithoutAddingASampleOrBackdatingItsClock()
        {
            var ps = Make();
            try
            {
                Assert.Equal(new[] { 46, 31, 15 }, ps.m_TrailList.Select(t => t.Points.Count));
                var trail = ps.m_TrailList[2];
                Near(-.072075285f, trail.Points[0].x); Near(-.0114156213f, trail.Points[0].y);
                Near(.7666667f, trail.Times[0]);
                Near(-.16997236f, trail.Points[1].x); Near(-.03187909f, trail.Points[1].y);
            }
            finally { Object.DestroyImmediate(ps.gameObject); }
        }

        static string Reference([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(source), "../../tests/projects/particletrailwitness/Reference/unity-6000.0.71f1/public.jsonl"));

        [Fact]
        public void CurvedPerspectiveTrailMatchesEveryNativeVertexUvColorAndIndex()
        {
            var ps = Make(); var cameraObject = new GameObject("trail-camera"); var mesh = new Mesh();
            try
            {
                var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = new Vector3(0,0,-6);
                camera.fieldOfView = 40; camera.aspect = 1;
                ps.GetComponent<ParticleSystemRenderer>().BakeTrailsMesh(mesh, camera,
                    ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale);
                var reference = File.ReadAllLines(Reference()).Select(s => JsonDocument.Parse(s).RootElement.Clone())
                    .ToDictionary(r => r.GetProperty("k").GetString(), r => r.GetProperty("v"));
                Assert.Equal(reference["mesh.vertexCount"].GetInt32(), mesh.vertexCount);
                float Value(JsonElement value) => BitConverter.Int32BitsToSingle(Convert.ToInt32(value.GetProperty("bits").GetString(), 16));
                var positions = mesh.vertices; var uvs = mesh.uv; var colors = mesh.colors32;
                for (int i = 0; i < positions.Length; i++)
                {
                    for (int j = 0; j < 3; j++) Near(Value(reference["mesh.vertices"][i][j]), positions[i][j]);
                    Near(Value(reference["mesh.uv0"][i][0]), uvs[i].x); Near(Value(reference["mesh.uv0"][i][1]), uvs[i].y);
                    var c = reference["mesh.colors32"][i];
                    Assert.Equal(new Color32(c[0].GetByte(), c[1].GetByte(), c[2].GetByte(), c[3].GetByte()), colors[i]);
                }
                Assert.Equal(reference["mesh.submesh.0.indicesWithBase"].EnumerateArray().Select(v => v.GetInt32()), mesh.triangles);
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(ps.gameObject); }
        }
    }
}
