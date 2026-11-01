using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// End a touching pair and begin another inside the same simulation barrier.
// Read getters both inside each notification and after the full dispatch.
public static class LifetimeProbe
{
    static readonly List<GameObject> Objects = new List<GameObject>();
    public static Rigidbody2D Seed;
    public static int Step, Event;
    static BoxCollider2D Box(string name, Vector2 position, bool dynamic = false)
    {
        var o = new GameObject(name); Objects.Add(o); o.transform.position = position;
        var b = o.AddComponent<Rigidbody2D>();
        b.bodyType = dynamic ? RigidbodyType2D.Dynamic : RigidbodyType2D.Static;
        b.constraints = RigidbodyConstraints2D.FreezeAll;
        b.sleepMode = RigidbodySleepMode2D.NeverSleep;
        return o.AddComponent<BoxCollider2D>();
    }
    public static void Snapshot(string key)
    {
        var array = new Collider2D[8]; int n = Seed.GetContacts(array);
        ProbeLog.Value(key + "/count", n);
        for (int i = 0; i < n; i++) ProbeLog.Value(key + "/c" + i, array[i].name);
        var points = new List<ContactPoint2D>(); Seed.GetContacts(points);
        ProbeLog.Value(key + "/points", points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            ProbeLog.Value(key + "/p" + i + "/collider", points[i].collider.name);
            ProbeLog.Value(key + "/p" + i + "/point", points[i].point);
            ProbeLog.Value(key + "/p" + i + "/normal", points[i].normal);
        }
    }
    static void Simulate()
    {
        Step++;
        Physics2D.Simulate(.02f);
        Snapshot("s" + Step);
    }
    public static void Run(string output)
    {
        Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
        foreach (string kind in new[] { "solid-loss-enter", "sensor-loss-enter", "solid-loss-two-enter", "two-loss-enter" })
        {
            ProbeLog.Begin(kind); Step = Event = 0;
            var h = Box("H", Vector2.zero, true); Seed = h.GetComponent<Rigidbody2D>();
            h.gameObject.AddComponent<LifetimeObserver>();
            var l = Box("L", new Vector2(-.9f, 0));
            if (kind == "sensor-loss-enter") l.isTrigger = true;
            Simulate(); Box("R", new Vector2(.9f, 0)); Simulate();
            var m = Box("M", new Vector2(0, .9f)); Simulate();
            Physics2D.IgnoreCollision(h, l, true);
            if (kind == "two-loss-enter") Physics2D.IgnoreCollision(h, m, true);
            Snapshot("filtered");
            Box("N", new Vector2(0, -.9f));
            if (kind == "solid-loss-two-enter") Box("K", new Vector2(.7f, .7f));
            Simulate(); Simulate();
            Physics2D.IgnoreCollision(h, l, false); Simulate();
            foreach (var o in Objects) UnityEngine.Object.DestroyImmediate(o);
            Objects.Clear(); Seed = null;
        }
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
public class LifetimeObserver : MonoBehaviour
{
    void Record(string kind, Collider2D other)
    {
        string key = "e" + LifetimeProbe.Event++;
        ProbeLog.Value(key + "/step", LifetimeProbe.Step);
        ProbeLog.Value(key + "/kind", kind); ProbeLog.Value(key + "/other", other.name);
        LifetimeProbe.Snapshot(key + "/view");
    }
    void OnCollisionEnter2D(Collision2D c) => Record("enter", c.collider);
    void OnCollisionStay2D(Collision2D c) => Record("stay", c.collider);
    void OnCollisionExit2D(Collision2D c) => Record("exit", c.collider);
    void OnTriggerEnter2D(Collider2D c) => Record("trigger-enter", c);
    void OnTriggerStay2D(Collider2D c) => Record("trigger-stay", c);
    void OnTriggerExit2D(Collider2D c) => Record("trigger-exit", c);
}
