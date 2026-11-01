using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// A/B equivalence control for the pre-existing non-sphere path. This asserts no new
// Native behavior: both captures must use the same source and the matched runtimes.
public class CrossKindProbe : MonoBehaviour
{
    static readonly StringBuilder Output = new StringBuilder();
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 x) => "[" + F(x.x) + "," + F(x.y) + "," + F(x.z) + "]";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("probe").AddComponent<CrossKindProbe>();

    void Start()
    {
        foreach (bool capsule in new[] { false, true })
        {
            var shape = new GameObject("shape");
            Collider collider = capsule ? (Collider)shape.AddComponent<CapsuleCollider>() : shape.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            Physics.SyncTransforms();
            foreach (int killAction in new[] { 0, 1, 2, 3 })
            foreach (float start in new[] { 0f, .5f, 1f })
            {
                var go = new GameObject("particles"); go.SetActive(false);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main; main.playOnAwake = false; main.startSpeed = 0f;
                var emission = ps.emission; emission.enabled = false;
                var trigger = ps.trigger; trigger.enabled = true; trigger.AddCollider(collider);
                trigger.inside = killAction == 0 ? ParticleSystemOverlapAction.Kill : ParticleSystemOverlapAction.Ignore;
                trigger.outside = killAction == 1 ? ParticleSystemOverlapAction.Kill : ParticleSystemOverlapAction.Ignore;
                trigger.enter = killAction == 2 ? ParticleSystemOverlapAction.Kill : ParticleSystemOverlapAction.Ignore;
                trigger.exit = killAction == 3 ? ParticleSystemOverlapAction.Kill : ParticleSystemOverlapAction.Ignore;
                go.SetActive(true); ps.Simulate(0f, true, true, false);
                ps.SetParticles(new[] { new ParticleSystem.Particle { position = Vector3.forward * start,
                    velocity = Vector3.forward * 2f, startSize = .4f, startLifetime = 2.013f,
                    remainingLifetime = 1.713f, randomSeed = 12345, startColor = Color.white } }, 1);
                for (int step = 0; step < 4; step++)
                {
                    if (step > 0) ps.Simulate(.05f, true, false, false);
                    var particles = new ParticleSystem.Particle[8]; int n = ps.GetParticles(particles);
                    Output.Append("{\"scenario\":\"").Append(capsule ? "capsule" : "box").Append('/').Append(killAction).Append('/').Append(F(start))
                        .Append("\",\"step\":\"").Append(step).Append("\",\"count\":").Append(ps.particleCount).Append(",\"time\":").Append(F(ps.time)).Append(",\"particles\":[");
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0) Output.Append(','); var p = particles[i];
                        Output.Append("{\"position\":").Append(V(p.position)).Append(",\"velocity\":").Append(V(p.velocity))
                            .Append(",\"totalVelocity\":").Append(V(p.totalVelocity)).Append(",\"life\":").Append(F(p.remainingLifetime))
                            .Append(",\"startLife\":").Append(F(p.startLifetime)).Append(",\"size\":").Append(V(p.GetCurrentSize3D(ps)))
                            .Append(",\"seed\":").Append(p.randomSeed).Append(",\"rotation\":").Append(V(p.rotation3D))
                            .Append(",\"angularVelocity\":").Append(V(p.angularVelocity3D)).Append('}');
                    }
                    Output.Append("]}\n");
                }
                DestroyImmediate(go);
            }
            DestroyImmediate(shape);
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_TRIGGER_BOUNDARY_OUT"), Output.ToString());
        Application.Quit();
    }
}
