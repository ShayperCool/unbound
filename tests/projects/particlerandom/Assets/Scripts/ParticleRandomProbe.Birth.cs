using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ParticleRandomProbe
{
    // Isolate origin, integration and expiry from shape and random selection.
    static void BirthControls()
    {
        float previousMax = Time.maximumParticleDeltaTime;
        foreach (string source in new[] { "burst", "rate", "emit" })
        foreach (int count in new[] { 1, 5 })
        foreach (float step in new[] { 0.02f, 0.05f, 0.1f })
            BirthControl(source + "/" + count + "/" + F(step), source, count, step);
        BirthControl("split", "burst", 1, 0.02f, calls: 5);
        BirthControl("single_step", "burst", 1, 0.1f, maximum: 1f);
        BirthControl("moving_child", "burst", 1, 0.05f, speed: 2f);
        BirthControl("gravity_child", "burst", 1, 0.05f, speed: 2f, gravity: 1f);
        BirthControl("without_children", "burst", 1, 0.05f, withChildren: false);
        BirthControl("restart", "burst", 1, 0.05f, restartAgain: true);
        foreach (bool parentWorld in new[] { false, true })
        foreach (bool childWorld in new[] { false, true })
        foreach (bool rotateChild in new[] { false, true })
            BirthControl("space/" + parentWorld + "/" + childWorld + "/" + rotateChild,
                "emit", 1, 0.02f, speed: 2f, transformed: true,
                parentWorld: parentWorld, childWorld: childWorld, rotateChild: rotateChild);
        BirthControl("child_rate", "burst", 1, 0.05f, childRate: true);
        BirthControl("child_bursts", "burst", 1, 0.05f, childBursts: true);
        Time.maximumParticleDeltaTime = previousMax;
    }

    static void BirthControl(string name, string source, int count, float step, int calls = 1,
        float maximum = 0.03f, float speed = 0f, float gravity = 0f, bool withChildren = true,
        bool restartAgain = false, bool transformed = false, bool parentWorld = false,
        bool childWorld = false, bool rotateChild = false, bool childRate = false, bool childBursts = false)
    {
        Time.maximumParticleDeltaTime = maximum;
        string key = "birth/" + name;
        var parent = Make(key, 12345, ps =>
        {
            var m = ps.main; m.startLifetime = 0.4f; m.startSpeed = 5f;
            var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = 0f;
            m.simulationSpace = parentWorld ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            if (transformed)
            {
                ps.transform.position = new Vector3(10f, 20f, 30f);
                ps.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            }
            if (source == "rate") { var e = ps.emission; e.rateOverTime = count / step; }
        }, source == "burst" ? count : 0);
        parent.gameObject.SetActive(false);
        var childObject = new GameObject("child");
        childObject.transform.SetParent(parent.transform, false);
        if (transformed) childObject.transform.localPosition = new Vector3(2f, 3f, 4f);
        if (rotateChild) childObject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        var child = childObject.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.randomSeed = 12346;
        var cm = child.main; cm.playOnAwake = false; cm.startLifetime = 0.18f;
        cm.startSpeed = speed; cm.gravityModifier = gravity; cm.maxParticles = 256;
        cm.simulationSpace = childWorld ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        var cs = child.shape; cs.enabled = false;
        var cr = child.rotationOverLifetime; cr.enabled = true; cr.z = 0f;
        var ce = child.emission; ce.rateOverTime = childRate ? 40f : 0f;
        ce.SetBursts(childRate ? new ParticleSystem.Burst[0] : childBursts
            ? new[] { new ParticleSystem.Burst(0f, (short)1), new ParticleSystem.Burst(0.04f, (short)1) }
            : new[] { new ParticleSystem.Burst(0f, (short)1) });
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
        parent.gameObject.SetActive(true);
        if (source == "emit")
        {
            parent.Emit(new ParticleSystem.EmitParams { position = new Vector3(1f, 2f, 3f),
                velocity = new Vector3(2f, 3f, 4f) }, count);
            RecordParticles(key + "/immediate", child);
        }
        int index = 0;
        Action<float, bool> advance = (dt, restart) =>
        {
            parent.Simulate(dt, withChildren, restart, false);
            RecordParticles(key + "/parent/" + index, parent);
            RecordParticles(key + "/child/" + index, child);
            index++;
        };
        for (int i = 0; i < calls; i++) advance(step, source != "emit" && i == 0);
        advance(0.02f, restartAgain);
        advance(0.1f, false);
        advance(0.2f, false);
        DestroyImmediate(parent.gameObject);
    }
}
