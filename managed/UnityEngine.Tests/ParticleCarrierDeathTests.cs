using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>When the Death sub-emitters of a particle whose trail outlives it fire, against the
    /// Unity 6000.0.71f1 Editor (tests/projects/particletrails pp/deathsub/die0): as its carrier
    /// leaves the buffer, its trail spent, not when it dies.</summary>
    public class ParticleCarrierDeathTests
    {
        const float Step = .05f;

        static Dictionary<string, JsonElement> Reference([CallerFilePath] string source = "") =>
            File.ReadAllLines(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source),
                    "../../tests/projects/particletrails/Reference/unity-6000.0.71f1/values.jsonl")))
                .Select(s => JsonDocument.Parse(s).RootElement.Clone())
                .ToDictionary(r => r.GetProperty("key").GetString(), r => r.GetProperty("value"));

        /// <summary>The probe's system: four particles at 0 s and two at 0.3 s leaving a 0.2 circle
        /// outwards at 2 units/s, living 0.4 to 0.9 s, with trails of half their start lifetime.</summary>
        static ParticleSystem Make(string name, Transform parent, bool dieWithParticles)
        {
            var go = new GameObject(name); go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = 4242u;
            var m = ps.main; m.playOnAwake = false; m.duration = 1f; m.loop = false; m.maxParticles = 64;
            m.startLifetime = new ParticleSystem.MinMaxCurve(.4f, .9f); m.startSpeed = 2f; m.startSize = .2f;
            var s = ps.shape; s.enabled = true; s.shapeType = ParticleSystemShapeType.Circle; s.radius = .2f;
            var e = ps.emission; e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)4), new ParticleSystem.Burst(.3f, (short)2) });
            var t = ps.trails; t.enabled = true; t.mode = ParticleSystemTrailMode.PerParticle; t.lifetime = .5f;
            t.minVertexDistance = .07f; t.dieWithParticles = dieWithParticles; t.widthOverTrail = .5f;
            return ps;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeathChildrenComeAsTheParticlesLeaveTheBuffer(bool dieWithParticles)
        {
            var reference = Reference();
            string key = "pp/deathsub/" + (dieWithParticles ? "die1" : "die0");
            var root = new GameObject(key + "/root");
            try
            {
                var parent = Make(key, root.transform, dieWithParticles);
                var child = Make(key + "/child", parent.transform, false);
                var cm = child.main; cm.startLifetime = 5f; cm.startSpeed = 0f;
                var cs = child.shape; cs.enabled = false;
                var ce = child.emission; ce.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                var ct = child.trails; ct.enabled = false;
                var sub = parent.subEmitters; sub.enabled = true;
                sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
                child.gameObject.SetActive(true);
                parent.gameObject.SetActive(true);
                var buffer = new ParticleSystem.Particle[64];
                for (int i = 0; i < 36; i++)
                {
                    parent.Simulate(Step, true, i == 0, false);
                    var expected = reference[$"{key}/{i}/child/particles"].EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    int n = child.GetParticles(buffer);
                    Assert.True((int)expected[0] == n, $"step {i}: {expected[0]} children expected, {n} found");
                    for (int p = 0; p < n; p++)
                    {
                        Assert.True(Mathf.Abs(expected[1 + 12 * p] - buffer[p].position.x) <= 1e-3f, $"step {i} child {p} x");
                        Assert.True(Mathf.Abs(expected[2 + 12 * p] - buffer[p].position.y) <= 1e-3f, $"step {i} child {p} y");
                        Assert.True(Mathf.Abs(expected[7 + 12 * p] - buffer[p].remainingLifetime) <= 1e-3f, $"step {i} child {p} life");
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
