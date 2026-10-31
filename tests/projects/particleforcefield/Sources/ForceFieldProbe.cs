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
        Extended();
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_FIELD_OUT"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Extended()
    {
        var p = new Vector3(1, .7f, .4f);
        var v = new Vector3(1, 2, 3);
        foreach (var point in new[] { p, new Vector3(0, 2, 0), new Vector3(-1, -2, 3), new Vector3(.001f, 2, .002f) })
            Case("position/" + V(point), 2, .5f, point, v, .025f);
        foreach (var rotation in new[] { Vector3.zero, new Vector3(0, 0, 90), new Vector3(23, 47, 81) })
        foreach (var scale in new[] { Vector3.one, new Vector3(2, .5f, 3) })
            Case("fieldTransform/" + V(rotation) + "/" + V(scale), 2, .5f, p, v, .025f,
                (ps, f) => { f.transform.position = new Vector3(.2f, -.3f, .1f); f.transform.eulerAngles = rotation; f.transform.localScale = scale; });
        foreach (var space in new[] { ParticleSystemSimulationSpace.Local, ParticleSystemSimulationSpace.World, ParticleSystemSimulationSpace.Custom })
        foreach (var scale in new[] { Vector3.one, new Vector3(2, .5f, 3) })
            Case("space/" + space + "/" + V(scale), 2, .5f, p, v, .025f, (ps, f) => {
                ps.transform.position = new Vector3(.3f, .7f, -.4f);
                ps.transform.eulerAngles = new Vector3(17, 32, 61); ps.transform.localScale = scale;
                var main = ps.main; main.simulationSpace = space;
                if (space == ParticleSystemSimulationSpace.Custom) main.customSimulationSpace = f.transform;
                f.transform.position = new Vector3(.2f, -.3f, .1f); f.transform.eulerAngles = new Vector3(23, 47, 81);
            });
        foreach (var shape in new[] { ParticleSystemForceFieldShape.Sphere, ParticleSystemForceFieldShape.Hemisphere, ParticleSystemForceFieldShape.Cylinder, ParticleSystemForceFieldShape.Box })
        foreach (var point in new[] { new Vector3(.25f, 0, .1f), p, new Vector3(1, -0.7f, .4f), new Vector3(0, 1.1f, 0), new Vector3(2.1f, 0, 0) })
        foreach (float start in new[] { 0f, .5f })
            Case("shape/" + shape + "/" + V(point) + "/" + F(start), 2, .5f, point, v, .025f,
                (ps, f) => { f.shape = shape; f.startRange = start; f.endRange = 2; f.length = 2; });
        foreach (float radius in new[] { .25f, 1f, 3f })
        foreach (float lifetime in new[] { 1f, 10f })
        foreach (int curve in new[] { 0, 1, 2 })
            Case("curve/" + F(radius) + "/" + F(lifetime) + "/" + curve, 2, .5f, new Vector3(radius, 0, 0), v, .025f,
                (ps, f) => {
                    f.endRange = 4; f.startRange = .1f;
                    var main = ps.main; main.startLifetime = lifetime;
                    if (curve != 1) f.rotationSpeed = new ParticleSystem.MinMaxCurve(3, AnimationCurve.Linear(0, .2f, 1, 1.2f));
                    if (curve != 0) f.rotationAttraction = new ParticleSystem.MinMaxCurve(2, AnimationCurve.Linear(0, .1f, 1, .6f));
                });
        foreach (var shape in new[] { ParticleSystemForceFieldShape.Sphere, ParticleSystemForceFieldShape.Hemisphere, ParticleSystemForceFieldShape.Cylinder, ParticleSystemForceFieldShape.Box })
        foreach (var point in new[] { new Vector3(.4f, .7f, .2f), new Vector3(1, .3f, 1), new Vector3(.2f, 1.5f, .1f) })
            Case("shapeCurve/" + shape + "/" + V(point),
                new ParticleSystem.MinMaxCurve(3, AnimationCurve.Linear(0, -.4f, 1, 1.2f)),
                new ParticleSystem.MinMaxCurve(2, AnimationCurve.Linear(0, .1f, 1, .6f)), point, v, .025f,
                (ps, f) => { f.shape = shape; f.startRange = .1f; f.endRange = 2; f.length = 4; });
        foreach (float dt in new[] { .001f, .01f, .02f, .05f, .1f })
            Case("dt/" + F(dt), 2, .5f, p, v, dt);
        foreach (float multiplier in new[] { 0f, .25f, 2f, -1f })
            Case("multiplier/" + F(multiplier), 2, .5f, p, v, .025f,
                (ps, f) => { var ext = ps.externalForces; ext.multiplier = multiplier; });
        for (int mode = 0; mode < 8; mode++)
        {
            int m = mode;
            Case("combined/" + mode, 2, .5f, p, v, .025f, (ps, f) => {
                if (m == 0) { f.directionX = 3; f.directionY = 4; f.drag = .7f; f.multiplyDragByParticleSize = false; f.multiplyDragByParticleVelocity = false; }
                if (m == 1) { var force = ps.forceOverLifetime; force.enabled = true; force.x = 3; force.y = 4; }
                if (m == 2) { var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = 3; vel.y = 4; }
                if (m == 3) { var main = ps.main; main.gravityModifier = 1; }
                if (m >= 4) {
                    var second = ps.gameObject.AddComponent<ParticleSystemForceField>();
                    second.endRange = 100; second.gravity = 0; second.rotationSpeed = -3; second.rotationAttraction = .2f;
                    second.rotationRandomness = Vector2.zero;
                    second.gameObject.layer = 4; f.gameObject.layer = 5;
                    var ext = ps.externalForces;
                    if (m == 5) ext.influenceMask = 1 << 5;
                    if (m == 6) { ext.influenceFilter = ParticleSystemGameObjectFilter.List; ext.AddInfluence(second); }
                    if (m == 7) { ext.influenceFilter = ParticleSystemGameObjectFilter.LayerMaskAndList; ext.influenceMask = 1 << 5; ext.AddInfluence(second); }
                }
            });
        }
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
        var emit = new ParticleSystem.EmitParams { position = position, velocity = velocity, startLifetime = ps.main.startLifetime.constant, randomSeed = 6789 };
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
