using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The order a Ribbon-mode trail joins its particles, against the Unity 6000.0.71f1
    /// Editor's baked trail meshes (tests/projects/particletrails ribbon/die0/*): a burst's
    /// particles, born together with different start lifetimes, and later rate births.</summary>
    public class ParticleRibbonOrderTests
    {
        const float Step = .05f;

        static Dictionary<string, JsonElement> Reference([CallerFilePath] string source = "") =>
            File.ReadAllLines(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source),
                    "../../tests/projects/particletrails/Reference/unity-6000.0.71f1/values.jsonl")))
                .Select(s => JsonDocument.Parse(s).RootElement.Clone())
                .ToDictionary(r => r.GetProperty("key").GetString(), r => r.GetProperty("value"));

        /// <summary>The probe's ribbon system: four particles at 0 s and two at 0.3 s plus 10 a
        /// second, leaving a 0.2 circle outwards at 2 units/s, living 0.4 to 0.9 s.</summary>
        static ParticleSystem Make()
        {
            var go = new GameObject("ribbon/die0"); go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = 4242u;
            var m = ps.main; m.playOnAwake = false; m.duration = 1f; m.loop = false; m.maxParticles = 64;
            m.startLifetime = new ParticleSystem.MinMaxCurve(.4f, .9f); m.startSpeed = 2f; m.startSize = .2f;
            var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = .2f;
            var e = ps.emission; e.rateOverTime = 10f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)4), new ParticleSystem.Burst(.3f, (short)2) });
            var t = ps.trails; t.enabled = true; t.mode = ParticleSystemTrailMode.Ribbon; t.lifetime = .5f;
            t.minVertexDistance = .07f; t.dieWithParticles = false; t.widthOverTrail = .5f;
            go.SetActive(true);
            return ps;
        }

        [Fact]
        public void RibbonJoinsParticlesInTheEditorsOrderAtEveryStep()
        {
            var reference = Reference();
            var ps = Make();
            var cameraObject = new GameObject("BakeCamera");
            var mesh = new Mesh();
            try
            {
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                int compared = 0;
                for (int i = 0; i < 36; i++)
                {
                    ps.Simulate(Step, true, i == 0, false);
                    if (!reference.TryGetValue($"ribbon/die0/{i}/trailmesh", out var expected)) continue;
                    mesh.Clear();
                    renderer.BakeTrailsMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                    var values = expected.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    var vertices = mesh.vertices;
                    Assert.Equal((int)values[0], vertices.Length);
                    for (int v = 0; v < vertices.Length; v++)
                        for (int c = 0; c < 3; c++)
                        {
                            float want = values[1 + 4 * v + c], got = vertices[v][c];
                            Assert.True(Math.Abs(want - got) <= 1e-3f, $"step {i} vertex {v}.{c}: expected {want:R}, actual {got:R}");
                        }
                    compared++;
                }
                Assert.True(compared >= 20, $"{compared} steps compared");
            }
            finally
            {
                Object.DestroyImmediate(ps.gameObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
