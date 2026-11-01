using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Positive-life control for the original outside-kill witness. No player-loop callbacks.
public class BoundaryProbe : MonoBehaviour
{
    static readonly StringBuilder output = new StringBuilder();
    const float Dt = .05f;
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string B(bool b) => b ? "true" : "false";
    static string Bits(float x) => "\"" + BitConverter.ToInt32(BitConverter.GetBytes(x), 0).ToString("x8") + "\"";
    static float FromBits(int bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
    static void Row(string scenario, string step, string value) => output.Append("{\"scenario\":\"").Append(scenario)
        .Append("\",\"step\":\"").Append(step).Append("\",\"value\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("BoundaryProbe").AddComponent<BoundaryProbe>();

    static ParticleSystem Make(SphereCollider collider, bool kill, float startSize = .4f)
    {
        var go = new GameObject("boundary"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        var m = ps.main; m.playOnAwake = false; m.startLifetime = 2.013f;
        m.startSpeed = 2f; m.startSize = startSize;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.rateOverTime = 10f;
        var t = ps.trigger; t.enabled = true; t.AddCollider(collider);
        t.inside = ParticleSystemOverlapAction.Ignore;
        t.outside = kill ? ParticleSystemOverlapAction.Kill : ParticleSystemOverlapAction.Ignore;
        t.enter = ParticleSystemOverlapAction.Ignore; t.exit = ParticleSystemOverlapAction.Ignore;
        go.SetActive(true);
        ps.Simulate(0f, true, true, false);
        return ps;
    }

    static string Query(Vector3 position, float radius, SphereCollider collider)
    {
        var hits = new Collider[8];
        int n = Physics.OverlapSphereNonAlloc(position, radius, hits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        bool found = false;
        for (int i = 0; i < n; i++) found |= hits[i] == collider;
        var all = Physics.OverlapSphere(position, radius, Physics.AllLayers, QueryTriggerInteraction.Collide);
        bool allocating = false;
        foreach (var hit in all) allocating |= hit == collider;
        return "{\"position\":" + V(position) + ",\"zBits\":" + Bits(position.z) + ",\"radius\":" + F(radius)
            + ",\"radiusBits\":" + Bits(radius) + ",\"nonAlloc\":" + B(found) + ",\"allocating\":" + B(allocating)
            + ",\"check\":" + B(Physics.CheckSphere(position, radius, Physics.AllLayers, QueryTriggerInteraction.Collide)) + "}";
    }

    static string Particle(ParticleSystem.Particle p, ParticleSystem ps, SphereCollider collider)
    {
        float scalar = p.GetCurrentSize(ps);
        var size = p.GetCurrentSize3D(ps);
        float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * .5f * ps.trigger.radiusScale;
        return "{\"position\":" + V(p.position) + ",\"zBits\":" + Bits(p.position.z)
            + ",\"velocity\":" + V(p.velocity) + ",\"totalVelocity\":" + V(p.totalVelocity)
            + ",\"life\":" + F(p.remainingLifetime) + ",\"startLife\":" + F(p.startLifetime)
            + ",\"startSize\":" + F(p.startSize) + ",\"startSize3D\":" + V(p.startSize3D)
            + ",\"currentSize\":" + F(scalar) + ",\"currentSize3D\":" + V(size)
            + ",\"seed\":" + p.randomSeed + ",\"rotation\":" + V(p.rotation3D)
            + ",\"angularVelocity\":" + V(p.angularVelocity3D)
            + ",\"startColor\":[" + p.startColor.r + "," + p.startColor.g + "," + p.startColor.b + "," + p.startColor.a + "]"
            + ",\"query\":" + Query(p.position, radius, collider)
            + ",\"scalarRadiusQuery\":" + Query(p.position, scalar * .5f * ps.trigger.radiusScale, collider) + "}";
    }

    static void State(string key, string phase, ParticleSystem ps, SphereCollider collider)
    {
        var buffer = new ParticleSystem.Particle[16];
        int n = ps.GetParticles(buffer);
        var s = new StringBuilder("{\"count\":").Append(ps.particleCount).Append(",\"read\":").Append(n)
            .Append(",\"time\":").Append(F(ps.time)).Append(",\"playing\":").Append(B(ps.isPlaying))
            .Append(",\"paused\":").Append(B(ps.isPaused)).Append(",\"particles\":[");
        for (int i = 0; i < n; i++) {
            if (i > 0) s.Append(',');
            s.Append(Particle(buffer[i], ps, collider));
        }
        Row(key, phase, s.Append("]}").ToString());
    }

    // One bounded discriminator. Scale the whole pair, then change the radius split
    // at fixed total radius. The diagonal distinguishes spherical distance from AABB.
    static void Discriminator()
    {
        var go = new GameObject("sphere arithmetic");
        var collider = go.AddComponent<SphereCollider>(); collider.isTrigger = true;
        float[] colliderRadii = { .5f, .25f, 1f, .2f, .6f, .5f };
        float[] particleSizes = { .4f, .2f, .8f, 1f, .2f, 0f };
        var directions = new[] { Vector3.forward, new Vector3(.6f, 0f, .8f) };
        // Relative separations, chosen before capture. No boundary search or fitted predicate.
        float[] offsets = { -.001f, 0f, .0000002384185791015625f, .00001f, .0001f, .001f, .05f };
        for (int geometry = 0; geometry < colliderRadii.Length; geometry++) {
            collider.radius = colliderRadii[geometry];
            Physics.SyncTransforms();
            float cr = collider.radius, size = particleSizes[geometry], pr = size * .5f;
            var bounds = collider.bounds;
            Row("arithmetic/" + geometry, "geometry", "{\"colliderRadius\":" + F(cr)
                + ",\"particleSize\":" + F(size) + ",\"particleRadius\":" + F(pr)
                + ",\"transform\":" + V(go.transform.position) + ",\"scale\":" + V(go.transform.lossyScale)
                + ",\"colliderCenter\":" + V(collider.center) + ",\"boundsCenter\":" + V(bounds.center)
                + ",\"boundsSize\":" + V(bounds.size) + ",\"contactOffset\":" + F(collider.contactOffset) + "}");
            for (int direction = 0; direction < directions.Length; direction++)
            for (int separation = 0; separation < offsets.Length; separation++)
            foreach (bool kill in new[] { false, true }) {
                float sum = cr + pr;
                var position = directions[direction] * (sum * (1f + offsets[separation]));
                var delta = position - bounds.center;
                float distanceSquared = delta.sqrMagnitude, distance = delta.magnitude;
                var radial = delta - delta / distance * cr;
                var normalized = delta / cr;
                float normalizedRadius = 1f + pr / cr;
                var closest = collider.ClosestPoint(position);
                var key = "arithmetic/" + geometry + "/" + direction + "/" + separation
                    + (kill ? "/killOutside" : "/ignoreOutside");
                var ps = Make(collider, kill, size);
                var p = new ParticleSystem.Particle {
                    position = position, velocity = Vector3.zero,
                    startLifetime = 2.013f, remainingLifetime = 1.713f, startSize = size,
                    startColor = Color.white, randomSeed = 12345
                };
                Row(key, "input", "{\"relativeOffset\":" + F(offsets[separation])
                    + ",\"direction\":" + V(directions[direction]) + ",\"sumRadius\":" + F(sum)
                    + ",\"distanceSquared\":" + F(distanceSquared) + ",\"distance\":" + F(distance)
                    + ",\"sumSquared\":" + F(sum * sum) + ",\"radialSquared\":" + F(radial.sqrMagnitude)
                    + ",\"closest\":" + V(closest) + ",\"closestSquared\":" + F((position - closest).sqrMagnitude)
                    + ",\"predicates\":{\"sumSquared\":" + B(distanceSquared <= sum * sum)
                    + ",\"distance\":" + B(distance <= sum) + ",\"radialSquared\":" + B(radial.sqrMagnitude <= pr * pr)
                    + ",\"normalizedSquared\":" + B(normalized.sqrMagnitude <= normalizedRadius * normalizedRadius)
                    + ",\"publicClosestSquared\":" + B((position - closest).sqrMagnitude <= pr * pr)
                    + ",\"expandedAabb\":" + B(Mathf.Max(Mathf.Abs(delta.x), Mathf.Max(Mathf.Abs(delta.y), Mathf.Abs(delta.z))) <= sum)
                    + "},\"particle\":" + Particle(p, ps, collider) + "}");
                ps.SetParticles(new[] { p }, 1);
                State(key, "before", ps, collider);
                ps.Simulate(Dt, true, false, false);
                State(key, "after", ps, collider);
                DestroyImmediate(ps.gameObject);
            }
        }
        DestroyImmediate(go);
    }

    void Start()
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var collider = sphere.GetComponent<SphereCollider>(); collider.isTrigger = true;
        // Match the measured cached pose, not the original unsynchronized Transform at z=2.5.
        sphere.transform.position = Vector3.zero;
        Physics.SyncTransforms();
        Row("setup", "synced", "{\"autoSync\":" + B(Physics.autoSyncTransforms)
            + ",\"transform\":" + V(sphere.transform.position) + ",\"scale\":" + V(sphere.transform.lossyScale)
            + ",\"colliderRadius\":" + F(collider.radius) + ",\"colliderCenter\":" + V(collider.center)
            + ",\"boundsCenter\":" + V(collider.bounds.center) + ",\"boundsSize\":" + V(collider.bounds.size)
            + ",\"contactOffset\":" + F(collider.contactOffset) + ",\"isTrigger\":" + B(collider.isTrigger)
            + ",\"dt\":" + F(Dt) + ",\"targetBits\":" + Bits(.700000167f) + "}");
        int boundary = BitConverter.ToInt32(BitConverter.GetBytes(.700000167f), 0);
        foreach (int offset in new[] { -1, 0, 1 })
        foreach (string motion in new[] { "stationary", "movingTo", "movingFrom" })
        foreach (bool kill in new[] { false, true }) {
            float target = FromBits(boundary + offset);
            float velocity = motion == "stationary" ? 0f : 2f;
            float start = motion == "movingTo" ? target - velocity * Dt : target;
            string key = "boundary/" + offset + "/" + motion + (kill ? "/killOutside" : "/ignoreOutside");
            var ps = Make(collider, kill);
            var p = new ParticleSystem.Particle {
                position = new Vector3(0f, 0f, start), velocity = new Vector3(0f, 0f, velocity),
                startLifetime = 2.013f, remainingLifetime = 1.713f, startSize = .4f,
                startColor = Color.white, randomSeed = 12345
            };
            Row(key, "input", "{\"target\":" + F(target) + ",\"targetBits\":" + Bits(target)
                + ",\"nominalEndQuery\":" + Query(new Vector3(0f, 0f, start + velocity * Dt), .2f * ps.trigger.radiusScale, collider)
                + ",\"radiusScale\":" + F(ps.trigger.radiusScale)
                + ",\"emissionEnabled\":" + B(ps.emission.enabled) + ",\"rateOverTime\":" + F(ps.emission.rateOverTime.constant)
                + ",\"particle\":" + Particle(p, ps, collider) + "}");
            ps.SetParticles(new[] { p }, 1);
            State(key, "before", ps, collider);
            ps.Simulate(Dt, true, false, false);
            State(key, "after", ps, collider);
            DestroyImmediate(ps.gameObject);
        }
        DestroyImmediate(sphere);
        Discriminator();
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_TRIGGER_BOUNDARY_OUT"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
