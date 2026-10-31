using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Each case starts in a fresh set of objects. Step numbers surround an actual Simulate call;
// event poses and post-step poses distinguish scheduling from a geometric threshold.
public static class ProbeEntry
{
    public static int Step, Event;
    public static string Phase;
    static readonly List<GameObject> Objects = new List<GameObject>();
    static Rigidbody2D Mover;
    static Collider2D Moving, Fixed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        // Realize an empty world before overriding project settings. This also makes the
        // pre-existing first-world settings reset visible only in the retained v1 controls.
        var warmup = new GameObject("warmup"); warmup.AddComponent<Rigidbody2D>();
        UnityEngine.Object.DestroyImmediate(warmup);
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.autoSyncTransforms = false;
        Physics2D.gravity = Vector2.zero;
        foreach (var dt in new[] { 0.01f, 0.02f, 0.04f })
            foreach (var speed in new[] { 2f, 10f })
                foreach (var sensor in new[] { false, true })
                    Case($"approach/{dt:R}/{speed:R}/{sensor}", () =>
                    {
                        Pair(sensor, false, -1.4f, 0.25f, 0.01f, RigidbodyType2D.Dynamic);
                        Mover.linearVelocity = new Vector2(speed, 0);
                        for (int i = 0; i < 32; i++) Sim(dt);
                    });
        foreach (var kind in new[] { RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
            foreach (var sensor in new[] { false, true })
                foreach (var both in new[] { false, true })
                    Case($"overlap/{kind}/{sensor}/{both}", () =>
                    {
                        Pair(sensor, both, -0.5f, 0.25f, 0.01f, kind);
                        Mover.useFullKinematicContacts = true;
                        Sim(0.02f); Sim(0.02f);
                        Mover.linearVelocity = new Vector2(-10, 0);
                        for (int i = 0; i < 8; i++) Sim(0.02f);
                    });
        foreach (var size in new[] { 0.1f, 0.25f, 0.7f })
            foreach (var margin in new[] { 0.001f, 0.01f, 0.1f })
                Case($"margin/{size:R}/{margin:R}", () =>
                {
                    Pair(true, false, -1-size-0.045f, size, margin, RigidbodyType2D.Dynamic);
                    Mover.linearVelocity = new Vector2(1, 0);
                    for (int i = 0; i < 6; i++) Sim(0.02f);
                    Mover.linearVelocity = new Vector2(-1, 0);
                    for (int i = 0; i < 8; i++) Sim(0.02f);
                });
        foreach (var sensor in new[] { false, true })
            foreach (var sync in new[] { false, true })
                Case($"lifetime/{sensor}/{sync}", () =>
                {
                    Pair(sensor, false, -2, 0.25f, 0.01f, RigidbodyType2D.Dynamic);
                    Sim(0.02f);
                    Mover.transform.position = new Vector3(-0.5f, 0, 0);
                    if (sync) Physics2D.SyncTransforms();
                    Snapshot("transformed"); Sim(0.02f); Sim(0.02f);
                    Mover.Sleep(); Snapshot("sleep"); Sim(0.02f); Sim(0.02f);
                    Mover.WakeUp(); Sim(0.02f);
                    Moving.enabled = false; Snapshot("disabled"); Sim(0.02f);
                    Moving.enabled = true; Sim(0.02f);
                    Mover.simulated = false; Sim(0.02f);
                    Mover.simulated = true; Sim(0.02f);
                    Mover.gameObject.SetActive(false); Sim(0.02f);
                    Mover.gameObject.SetActive(true); Sim(0.02f);
                    Mover.position = new Vector2(-2, 0); Sim(0.02f);
                });
        foreach (var kind in new[] { RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
            foreach (var target in new[] { RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic, RigidbodyType2D.Static })
                foreach (var sleep in new[] { false, true })
                    Case($"bodies/{kind}/{target}/{sleep}", () =>
                    {
                        Pair(true, false, -1.4f, 0.25f, 0.01f, kind);
                        var other = Fixed.gameObject.AddComponent<Rigidbody2D>();
                        other.bodyType = target; other.gravityScale = 0;
                        if (sleep) { other.Sleep(); Mover.Sleep(); }
                        Mover.linearVelocity = new Vector2(10, 0);
                        for (int i = 0; i < 16; i++) Sim(0.02f);
                    });
        foreach (var kind in new[] { RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
            Case($"sleep/static-transform/{kind}", () =>
            {
                Pair(true, false, -2, 0.25f, 0.01f, kind);
                Sim(0.02f); Mover.Sleep();
                Fixed.transform.position = new Vector3(-1.5f, 0, 0);
                Physics2D.SyncTransforms(); Sim(0.02f); Sim(0.02f);
                Mover.WakeUp(); Sim(0.02f);
                Fixed.transform.position = new Vector3(4, 0, 0);
                Sim(0.02f); Sim(0.02f);
            });
        Case("fall/original", () =>
        {
            Pair(true, false, 0, 0.25f, 0.01f, RigidbodyType2D.Dynamic);
            Mover.position = new Vector2(0, 5);
            Physics2D.gravity = new Vector2(0, -9.81f);
            for (int i = 0; i < 120; i++) Sim(0.02f);
        });
        // Native records both enter and exit when the step starts inside and ends outside.
        Case("cross/initial-overlap", () =>
        {
            Pair(true, false, -0.5f, 0.25f, 0.01f, RigidbodyType2D.Dynamic);
            Mover.linearVelocity = new Vector2(100, 0);
            Sim(0.02f); Sim(0.02f);
        });
        Case("cross/pass-through", () =>
        {
            Pair(true, false, -1.4f, 0.25f, 0.01f, RigidbodyType2D.Dynamic);
            Mover.linearVelocity = new Vector2(100, 0);
            Sim(0.04f); Sim(0.04f);
        });
        Case("mixed/solid-and-sensor", () =>
        {
            Pair(true, false, -1.4f, 0.25f, 0.01f, RigidbodyType2D.Dynamic);
            var solid = Obj("wall", new Vector2(1.6f, 0));
            solid.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 2);
            Mover.linearVelocity = new Vector2(10, 0);
            for (int i = 0; i < 25; i++) Sim(0.02f);
        });
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Case(string name, Action run)
    {
        ProbeLog.Begin(name); Step = Event = 0; Phase = "setup";
        Physics2D.gravity = Vector2.zero;
        try { run(); } catch (Exception e) { ProbeLog.Error(e); }
        Phase = "teardown";
        foreach (var o in Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        Objects.Clear();
    }

    static GameObject Obj(string name, Vector2 position)
    {
        var o = new GameObject(name); o.transform.position = position; Objects.Add(o); return o;
    }

    static void Pair(bool sensor, bool both, float x, float radius, float offset, RigidbodyType2D type)
    {
        Physics2D.defaultContactOffset = offset;
        var zone = Obj("zone", Vector2.zero);
        var box = zone.AddComponent<BoxCollider2D>(); box.size = new Vector2(2, 2); box.isTrigger = sensor;
        Fixed = box;
        var ball = Obj("ball", new Vector2(x, 0));
        Mover = ball.AddComponent<Rigidbody2D>(); Mover.bodyType = type; Mover.gravityScale = 1;
        var circle = ball.AddComponent<CircleCollider2D>(); circle.radius = radius; circle.isTrigger = both;
        Moving = circle;
        ball.AddComponent<TimingRecorder>();
    }

    static void Sim(float dt)
    {
        Phase = "simulate"; Step++;
        ProbeLog.Value(Step + "/before", Mover.position);
        ProbeLog.Value(Step + "/beforeVelocity", Mover.linearVelocity);
        Physics2D.Simulate(dt);
        Snapshot(Step + "/after");
    }

    static void Snapshot(string key)
    {
        ProbeLog.Value(key + "/position", Mover.position);
        ProbeLog.Value(key + "/transform", Mover.transform.position);
        ProbeLog.Value(key + "/velocity", Mover.linearVelocity);
        ProbeLog.Value(key + "/sleep", Mover.IsSleeping());
        ProbeLog.Value(key + "/touching", Moving.IsTouching(Fixed));
    }
}

public class TimingRecorder : MonoBehaviour
{
    void Record(string kind, Collider2D other)
    {
        var key = "event/" + ProbeEntry.Event++;
        ProbeLog.Value(key + "/phase", ProbeEntry.Phase);
        ProbeLog.Value(key + "/kind", kind);
        ProbeLog.Value(key + "/other", other.name);
        ProbeLog.Value(key + "/step", ProbeEntry.Step);
        ProbeLog.Value(key + "/position", GetComponent<Rigidbody2D>().position);
        ProbeLog.Value(key + "/velocity", GetComponent<Rigidbody2D>().linearVelocity);
    }
    void OnTriggerEnter2D(Collider2D c) => Record("triggerEnter", c);
    void OnTriggerStay2D(Collider2D c) => Record("triggerStay", c);
    void OnTriggerExit2D(Collider2D c) => Record("triggerExit", c);
    void OnCollisionEnter2D(Collision2D c) => Record("contactEnter", c.collider);
    void OnCollisionStay2D(Collision2D c) => Record("contactStay", c.collider);
    void OnCollisionExit2D(Collision2D c) => Record("contactExit", c.collider);
}
