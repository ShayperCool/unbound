using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Keep H as the only dynamic seed. Change contact creation order independently
// of body/proxy lifetime, then remove a middle contact without deleting a proxy.
public static class ProbeEntry
{
    static readonly List<Rigidbody2D> Bodies = new List<Rigidbody2D>();
    static float center;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero;
        int index = 0;
        foreach (string action in new[] { "staged-lr", "staged-rl", "together-lr", "together-rl",
            "dummy-delete-lr", "dummy-delete-rl", "dummy-recreate", "ignore-middle" })
        {
            center = 100 + 100 * index++;
            Case(action);
        }
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static Rigidbody2D Create(string name, float x, float y, bool dynamic = false)
    {
        var o = new GameObject(name);
        o.transform.position = new Vector3(x, y, 0);
        var b = o.AddComponent<Rigidbody2D>();
        b.bodyType = dynamic ? RigidbodyType2D.Dynamic : RigidbodyType2D.Static;
        b.gravityScale = 0;
        b.constraints = RigidbodyConstraints2D.FreezeAll;
        b.sleepMode = RigidbodySleepMode2D.NeverSleep;
        o.AddComponent<BoxCollider2D>();
        Bodies.Add(b);
        return b;
    }
    static void Snapshot(string phase)
    {
        foreach (var b in Bodies)
        {
            string k = phase + "/" + b.name;
            ProbeLog.Value(k + "/position", b.position);
            ProbeLog.Value(k + "/type", (int)b.bodyType);
            var contacts = new ContactPoint2D[16];
            int n = b.GetContacts(contacts);
            ProbeLog.Value(k + "/count", n);
            for (int i = 0; i < n; i++)
            {
                var c = contacts[i]; string q = k + "/c" + i;
                ProbeLog.Value(q + "/collider", c.collider.name);
                ProbeLog.Value(q + "/other", c.otherCollider.name);
                ProbeLog.Value(q + "/point", c.point);
                ProbeLog.Value(q + "/normal", c.normal);
                ProbeLog.Value(q + "/separation", c.separation);
                ProbeLog.Value(q + "/normalImpulse", c.normalImpulse);
                ProbeLog.Value(q + "/tangentImpulse", c.tangentImpulse);
            }
        }
    }
    static void Step(string phase)
    {
        Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); Snapshot(phase);
    }
    static void Case(string action)
    {
        ProbeLog.Begin(action);
        var h = Create("H", center, 0, true);
        var l = Create("L", center - 4, 0);
        Rigidbody2D dummy = null;
        if (action.StartsWith("dummy-")) dummy = Create("dummy", center + 20, 0);
        var r = Create("R", center + 4, 0);
        Rigidbody2D m = action == "ignore-middle" ? Create("M", center, 4) : null;
        Snapshot("created"); Step("separated");
        if (dummy)
        {
            Bodies.Remove(dummy); UnityEngine.Object.DestroyImmediate(dummy.gameObject);
            if (action == "dummy-recreate") Create("dummy2", center + 24, 0);
        }
        bool reverse = action.EndsWith("rl");
        var first = reverse ? r : l; var second = reverse ? l : r;
        if (!action.StartsWith("staged-")) Step("barrier");
        first.position = new Vector2(center + (reverse ? .9f : -.9f), 0);
        if (action.StartsWith("staged-")) Step("first");
        second.position = new Vector2(center + (reverse ? -.9f : .9f), 0);
        if (m) m.position = new Vector2(center, .9f);
        Step("both");
        if (m) Physics2D.IgnoreCollision(h.GetComponent<Collider2D>(), r.GetComponent<Collider2D>(), true);
        Step("removed");
        if (m) Physics2D.IgnoreCollision(h.GetComponent<Collider2D>(), r.GetComponent<Collider2D>(), false);
        Step("restored"); Step("final");
        foreach (var b in Bodies) UnityEngine.Object.DestroyImmediate(b.gameObject);
        Bodies.Clear();
    }
}
