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
        foreach (var wallFirst in new[] { false, true })
            foreach (var direction in new[] { -1f, 1f })
                foreach (var overlap in new[] { false, true })
                    Case($"order/{wallFirst}/{direction}/{overlap}", () =>
                    {
                        GameObject wall = null;
                        if (wallFirst) wall = Obj("wall", new Vector2(direction * 1.6f, 0));
                        Pair(true, false, overlap ? direction * 1.4f : -direction * 1.4f,
                            0.25f, 0.01f, RigidbodyType2D.Dynamic);
                        if (!wallFirst) wall = Obj("wall", new Vector2(direction * 1.6f, 0));
                        wall.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 2);
                        Mover.linearVelocity = new Vector2(direction * 10, 0);
                        for (int i = 0; i < 25; i++) Sim(0.02f);
                    });
        foreach (var reverse in new[] { false, true })
            Case($"initial/{reverse}", () =>
            {
                GameObject wall = null;
                if (reverse) wall = Obj("wall", new Vector2(-1.6f, 0));
                Pair(true, false, -1.2f, 0.25f, 0.01f, RigidbodyType2D.Dynamic);
                if (!reverse) wall = Obj("wall", new Vector2(-1.6f, 0));
                wall.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 2);
                for (int i = 0; i < 8; i++) Sim(0.02f);
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
