using System;
using System.Collections;
using UnityEngine;

public partial class ParticleRandomProbe
{
    // Paired constant lifetimes isolate Birth inheritance from other channels.
    static void BirthLifetimeControls()
    {
        float maximum = Time.maximumParticleDeltaTime, fixedDelta = Time.fixedDeltaTime;
        Time.maximumParticleDeltaTime = 0.03f;
        Time.fixedDeltaTime = 0.02f;
        foreach (bool inherit in new[] { false, true })
        {
            BirthLifetimeControl("short", inherit, 0.413f, 0.813f);
            BirthLifetimeControl("long", inherit, 1.613f, 0.413f);
            BirthLifetimeControl("single", inherit, 1.613f, 0.413f, single: true);
            BirthLifetimeControl("cohorts", inherit, 0.613f, 0.813f, cohorts: true);
            BirthLifetimeControl("restart", inherit, 0.413f, 0.813f, restart: true);
            BirthLifetimeControl("stop", inherit, 0.613f, 0.813f, cohorts: true, stop: true);
            BirthLifetimeControl("clear", inherit, 0.613f, 0.813f, cohorts: true, clear: true);
            BirthLifetimeControl("early_expiry", inherit, 0.213f, 0.113f);
            BirthLifetimeControl("zero", inherit, 0.413f, 0.813f, zeroOnly: true);
            BirthLifetimeControl("later", inherit, 0.413f, 0.813f, laterOnly: true);
            BirthLifetimeControl("batch", inherit, 0.613f, 0.813f, rate: 120f);
        }
        Time.maximumParticleDeltaTime = maximum;
        Time.fixedDeltaTime = fixedDelta;
    }

    static void BirthLifetimeControl(string name, bool inherit, float parentLife, float childLife,
        bool single = false, bool cohorts = false, bool restart = false, bool stop = false,
        bool clear = false, bool zeroOnly = false, bool laterOnly = false, float rate = 10f)
    {
        string key = "birthlife/" + name + "/" + (inherit ? "lifetime" : "nothing");
        var parent = Make(key, 12345, ps =>
        {
            var m = ps.main; m.duration = 5f; m.loop = false;
            m.startLifetime = parentLife; m.startSpeed = 2f;
            var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = 0f;
            if (cohorts)
            {
                var e = ps.emission;
                e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1),
                    new ParticleSystem.Burst(0.13f, (short)1), new ParticleSystem.Burst(0.27f, (short)1) });
            }
        }, 1);
        parent.gameObject.SetActive(false);
        var childObject = new GameObject("child");
        childObject.transform.SetParent(parent.transform, false);
        var child = childObject.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.useAutoRandomSeed = false; child.randomSeed = 12346;
        var cm = child.main; cm.playOnAwake = false; cm.startLifetime = childLife;
        cm.startSpeed = 0.5f; cm.maxParticles = 256; cm.loop = false; cm.duration = 5f;
        var shape = child.shape; shape.enabled = false;
        var cr = child.rotationOverLifetime; cr.enabled = true; cr.z = 0f;
        var ce = child.emission; ce.rateOverTime = zeroOnly || laterOnly ? 0f : rate;
        ce.SetBursts(zeroOnly ? new[] { new ParticleSystem.Burst(0f, (short)1) }
            : laterOnly ? new[] { new ParticleSystem.Burst(0.17f, (short)1) }
            : new[] { new ParticleSystem.Burst(0f, (short)1), new ParticleSystem.Burst(0.17f, (short)1) });
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, inherit
            ? ParticleSystemSubEmitterProperties.InheritLifetime : ParticleSystemSubEmitterProperties.InheritNothing);
        parent.gameObject.SetActive(true);
        int calls = single ? 1 : 20;
        for (int i = 0; i < calls; i++)
        {
            if (i == 6 && (stop || clear))
                parent.Stop(false, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
            parent.Simulate(single ? 1f : 0.05f, true, i == 0 || restart && i == 5, false);
            RecordParticles(key + "/parent/" + i, parent);
            RecordParticles(key + "/child/" + i, child);
        }
        parent.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        foreach (float dt in new[] { 0.05f, 0.2f, 0.5f, 1f, 2f })
        {
            parent.Simulate(dt, true, false, false);
            RecordParticles(key + "/parent/end/" + calls, parent);
            RecordParticles(key + "/child/end/" + calls, child);
            calls++;
        }
        DestroyImmediate(parent.gameObject);
    }

    static IEnumerator BirthLifetimeCallbacks()
    {
        float capture = Time.captureDeltaTime;
        Time.captureDeltaTime = 0.02f;
        foreach (bool inherit in new[] { false, true })
        {
            string key = "birthlife/callback/" + (inherit ? "lifetime" : "nothing");
            var parent = Make(key, 12345, ps =>
            {
                var m = ps.main; m.startLifetime = 0.413f;
                var r = ps.rotationOverLifetime; r.enabled = true; r.z = 0f;
            }, 1);
            parent.gameObject.SetActive(false);
            var childObject = new GameObject("child");
            childObject.transform.SetParent(parent.transform, false);
            var child = childObject.AddComponent<ParticleSystem>();
            child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var observer = childObject.AddComponent<ParticleBirthLifetimeObserver>();
            child.randomSeed = 12346;
            var cm = child.main; cm.playOnAwake = false; cm.startLifetime = 0.813f;
            cm.startSpeed = 0f; cm.duration = 0.05f; cm.loop = false;
            cm.stopAction = ParticleSystemStopAction.Callback;
            var shape = child.shape; shape.enabled = false;
            var r = child.rotationOverLifetime; r.enabled = true; r.z = 0f;
            var ce = child.emission; ce.rateOverTime = 0f;
            ce.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var sub = parent.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, inherit
                ? ParticleSystemSubEmitterProperties.InheritLifetime : ParticleSystemSubEmitterProperties.InheritNothing);
            parent.gameObject.SetActive(true);
            parent.Simulate(0.05f, true, true, false);
            // Resume only the existing child. Its parent stays paused, so no new births occur.
            child.Play(false);
            for (int frame = 0; frame < 45; frame++)
            {
                yield return null;
                RecordParticles(key + "/child/" + frame, child);
                Record(key + "/callbacks/" + frame, new float[] { observer.calls });
            }
            DestroyImmediate(parent.gameObject);
        }
        Time.captureDeltaTime = capture;
    }
}
