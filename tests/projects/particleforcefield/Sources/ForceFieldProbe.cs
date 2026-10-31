using System;
using System.IO;
using System.Text;
using System.Globalization;
using UnityEngine;

public class ForceFieldProbe : MonoBehaviour
{
    static readonly StringBuilder output = new StringBuilder();
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static void Value(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        foreach (float speed in new[] { 0f, 2f, -2f })
        foreach (float attraction in new[] { 0f, .5f, -0.5f, 2f })
        foreach (float radius in new[] { 0f, .25f, 1f, 3f })
        foreach (float dt in new[] { .025f, .1f })
        foreach (var velocity in new[] { Vector3.zero, new Vector3(0, 0, 2), new Vector3(1, 2, 3) })
            Case("basic/" + F(speed) + "/" + F(attraction) + "/" + F(radius) + "/" + F(dt) + "/" + V(velocity),
                speed, attraction, new Vector3(radius, 0, 0), velocity, dt);
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_FIELD_OUT"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Case(string key, ParticleSystem.MinMaxCurve speed, ParticleSystem.MinMaxCurve attraction,
        Vector3 position, Vector3 velocity, float dt, Action<ParticleSystem, ParticleSystemForceField> configure = null)
    {
        var fg = new GameObject("field");
        var field = fg.AddComponent<ParticleSystemForceField>();
        field.shape = ParticleSystemForceFieldShape.Sphere;
        field.endRange = 100;
        field.gravity = 0;
        field.rotationRandomness = Vector2.zero;
        field.rotationSpeed = speed;
        field.rotationAttraction = attraction;
        var go = new GameObject("system"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        var main = ps.main; main.playOnAwake = false; main.startLifetime = 10; main.startSpeed = 0;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var ext = ps.externalForces; ext.enabled = true;
        configure?.Invoke(ps, field);
        go.SetActive(true);
        var emit = new ParticleSystem.EmitParams { position = position, velocity = velocity, startLifetime = 10, randomSeed = 6789 };
        UnityEngine.Random.InitState(7531); float expected = UnityEngine.Random.value;
        UnityEngine.Random.InitState(7531);
        ps.Emit(emit, 1);
        var particles = new ParticleSystem.Particle[1];
        for (int i = 0; i <= 4; i++)
        {
            if (i != 0) ps.Simulate(dt, false, false, false);
            int count = ps.GetParticles(particles);
            Value(key + "/" + i, "{\"count\":" + count + ",\"position\":" + V(particles[0].position) + ",\"velocity\":" + V(particles[0].velocity) + ",\"animated\":" + V(particles[0].animatedVelocity) + ",\"total\":" + V(particles[0].totalVelocity) + ",\"seed\":" + particles[0].randomSeed + ",\"life\":" + F(particles[0].remainingLifetime) + "}");
        }
        Value(key + "/rng", UnityEngine.Random.value == expected ? "true" : "false");
        DestroyImmediate(go); DestroyImmediate(fg);
    }
}
