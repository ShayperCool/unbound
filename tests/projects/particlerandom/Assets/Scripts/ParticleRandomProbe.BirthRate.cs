using System;
using UnityEngine;

public partial class ParticleRandomProbe
{
    // Same constant start values as the broad Birth history, with angular state isolated.
    // Parent lifetime changes the exported age rounding without changing requested deltas.
    static void BirthRateControls()
    {
        float maximum = Time.maximumParticleDeltaTime, fixedDelta = Time.fixedDeltaTime;
        Time.maximumParticleDeltaTime = 0.03f;
        Time.fixedDeltaTime = 0.02f;
        BirthRateControl("original", "rate", 1, 1.013f, 10f);
        BirthRateControl("below", "rate", 1, 1.013f, 9.999f);
        BirthRateControl("above", "rate", 1, 1.013f, 10.001f);
        BirthRateControl("lifetime04", "rate", 1, 0.4f, 10f);
        BirthRateControl("fractional", "burst", 1, 1.013f, 7f);
        BirthRateControl("one", "burst", 1, 1.013f, 10f);
        BirthRateControl("three", "burst", 3, 1.013f, 10f);
        BirthRateControl("single", "burst", 1, 1.013f, 10f, single: true);
        BirthRateControl("expiry", "burst", 1, 0.213f, 10f, childLife: 0.113f);
        BirthRateControl("stop", "rate", 1, 1.013f, 10f, stop: true);
        BirthRateControl("clear", "rate", 1, 1.013f, 10f, clear: true);
        BirthRateControl("delay", "burst", 1, 1.013f, 10f, childDelay: 0.073f);
        BirthRateControl("duration", "burst", 1, 1.013f, 10f, childDuration: 0.173f);
        Time.maximumParticleDeltaTime = maximum;
        Time.fixedDeltaTime = fixedDelta;
    }

    static void BirthRateControl(string name, string source, int count, float parentLife,
        float rate, bool single = false, float childLife = 0.413f, bool stop = false,
        bool clear = false, float childDelay = 0f, float childDuration = 5f)
    {
        string key = "birthrate/" + name;
        var parent = Make(key, 12345, ps =>
        {
            var m = ps.main; m.duration = 5f; m.startLifetime = parentLife;
            var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = 0f;
            var e = ps.emission; e.rateOverTime = source == "rate" ? 5f : 0f;
        }, source == "burst" ? count : 0);
        parent.gameObject.SetActive(false);
        var childObject = new GameObject("child");
        childObject.transform.SetParent(parent.transform, false);
        var child = childObject.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.randomSeed = 12346;
        var cm = child.main; cm.playOnAwake = false; cm.startLifetime = childLife;
        cm.startSpeed = 0.5f; cm.maxParticles = 256;
        cm.startDelay = childDelay; cm.duration = childDuration; cm.loop = false;
        var cs = child.shape; cs.enabled = false;
        var cr = child.rotationOverLifetime; cr.enabled = true; cr.z = 0f;
        var ce = child.emission; ce.rateOverTime = rate; ce.SetBursts(new ParticleSystem.Burst[0]);
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
        parent.gameObject.SetActive(true);
        int calls = single ? 1 : 18;
        for (int i = 0; i < calls; i++)
        {
            if (i == 8 && (stop || clear))
                parent.Stop(false, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
            parent.Simulate(single ? 0.9f : 0.05f, true, i == 0, false);
            RecordParticles(key + "/parent/" + i, parent);
            RecordParticles(key + "/child/" + i, child);
            Record(key + "/clock/" + i, new[] { parent.time, child.time });
        }
        parent.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        foreach (float dt in new[] { 0.05f, 0.2f, 0.5f, 0.5f })
        {
            parent.Simulate(dt, true, false, false);
            RecordParticles(key + "/parent/end/" + F(dt) + "/" + calls, parent);
            RecordParticles(key + "/child/end/" + F(dt) + "/" + calls, child);
            calls++;
        }
        DestroyImmediate(parent.gameObject);
    }
}
