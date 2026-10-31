using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The original scenarios run first so contact allocation and world history are real.
public static class ProbeEntry
{
    public static int Step, Event;
    static readonly List<GameObject> Objects = new List<GameObject>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Probe2D.RunAll(); Probe3D.RunAll();
        foreach (bool box in new[] { false, true })
        foreach (float angle in new[] { 0f, 13f })
        foreach (float speed in new[] { 0f, -3f, -12f })
        foreach (bool continuous in new[] { false, true })
            Case(box, angle, speed, continuous);
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static GameObject Obj(string name, Vector2 position, float angle = 0)
    {
        var o = new GameObject(name); Objects.Add(o);
        o.transform.position = position; o.transform.rotation = Quaternion.Euler(0, 0, angle);
        o.AddComponent<DynamicsRecorder>();
        return o;
    }
    static void Case(bool box, float angle, float speed, bool continuous)
    {
        ProbeLog.Begin($"phase/{box}/{angle}/{speed}/{continuous}"); Step = Event = 0;
        try
        {
            Physics2D.gravity = new Vector2(0, -9.81f);
            var ground = Obj("ground", new Vector2(0, -.5f));
            ground.AddComponent<BoxCollider2D>().size = new Vector2(20, 1);
            var o = Obj("moving", new Vector2(0, .6f), angle);
            Collider2D c = box ? (Collider2D)o.AddComponent<BoxCollider2D>() : o.AddComponent<CircleCollider2D>();
            c.sharedMaterial = new PhysicsMaterial2D { bounciness = .8f, friction = .4f };
            var body = o.AddComponent<Rigidbody2D>();
            body.collisionDetectionMode = continuous ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
            body.linearVelocity = new Vector2(1, speed); body.angularVelocity = 17;
            for (Step = 1; Step <= 20; Step++)
            {
                Snapshot("s" + Step + "/before", body);
                Physics2D.Simulate(.02f);
                Snapshot("s" + Step + "/after", body);
            }
        }
        catch (Exception e) { ProbeLog.Error(e); }
        foreach (var o in Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        Objects.Clear();
    }
    public static void Snapshot(string key, Rigidbody2D b)
    {
        ProbeLog.Value(key + "/position", b.position); ProbeLog.Value(key + "/rotation", b.rotation);
        ProbeLog.Value(key + "/velocity", b.linearVelocity); ProbeLog.Value(key + "/angularVelocity", b.angularVelocity);
        var contacts = new ContactPoint2D[8]; int n = b.GetContacts(contacts);
        ProbeLog.Value(key + "/count", n);
        for (int i = 0; i < n; i++) Contact(key + "/c" + i, contacts[i]);
    }
    public static void Contact(string key, ContactPoint2D c)
    {
        ProbeLog.Value(key + "/point", c.point); ProbeLog.Value(key + "/normal", c.normal);
        ProbeLog.Value(key + "/separation", c.separation); ProbeLog.Value(key + "/relativeVelocity", c.relativeVelocity);
        ProbeLog.Value(key + "/normalImpulse", c.normalImpulse); ProbeLog.Value(key + "/tangentImpulse", c.tangentImpulse);
    }
}
public class DynamicsRecorder : MonoBehaviour
{
    void Record(string kind, Collision2D c)
    {
        string key = "e" + ProbeEntry.Event++;
        ProbeLog.Value(key + "/kind", kind); ProbeLog.Value(key + "/step", ProbeEntry.Step);
        ProbeLog.Value(key + "/self", name); ProbeLog.Value(key + "/other", c.collider.name);
        ProbeLog.Value(key + "/relativeVelocity", c.relativeVelocity); ProbeLog.Value(key + "/count", c.contactCount);
        for (int i = 0; i < c.contactCount; i++) ProbeEntry.Contact(key + "/c" + i, c.GetContact(i));
        var b = GetComponent<Rigidbody2D>();
        if (b) ProbeEntry.Snapshot(key + "/query", b);
    }
    void OnCollisionEnter2D(Collision2D c) => Record("enter", c);
    void OnCollisionStay2D(Collision2D c) => Record("stay", c);
    void OnCollisionExit2D(Collision2D c) => Record("exit", c);
}
