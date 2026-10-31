using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lifetime operations precede a connected contact chain. Body labels, input poses,
// contact query order and solved poses remain public; debugger diagnostics are separate.
public static class ProbeEntry
{
    static readonly List<GameObject> Objects = new List<GameObject>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero;
        foreach (string action in new[] { "none", "delete-first", "delete-middle", "delete-last",
            "recreate", "dynamic-static", "static-dynamic", "dynamic-kinematic", "kinematic-dynamic",
            "kinematic-static", "static-kinematic", "simulated", "active", "scene" }) Case(action);
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static Rigidbody2D Create(string name, RigidbodyType2D type = RigidbodyType2D.Dynamic)
    {
        var o = new GameObject(name); Objects.Add(o);
        o.transform.position = new Vector3(10 + Objects.Count * 3, 0, 0);
        o.AddComponent<BoxCollider2D>();
        var b = o.AddComponent<Rigidbody2D>(); b.bodyType = type;
        b.sleepMode = RigidbodySleepMode2D.NeverSleep;
        return b;
    }
    static void Snapshot(string prefix, List<Rigidbody2D> bodies)
    {
        foreach (var b in bodies)
        {
            string k = prefix + "/" + b.name;
            ProbeLog.Value(k + "/type", (int)b.bodyType);
            ProbeLog.Value(k + "/position", b.position);
            ProbeLog.Value(k + "/rotation", b.rotation);
            ProbeLog.Value(k + "/velocity", b.linearVelocity);
            ProbeLog.Value(k + "/angularVelocity", b.angularVelocity);
            var contacts = new ContactPoint2D[8]; int n = b.GetContacts(contacts);
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
    static void Case(string action)
    {
        ProbeLog.Begin(action);
        var bodies = new List<Rigidbody2D>();
        bool startStatic = action.StartsWith("static-");
        bool startKinematic = action.StartsWith("kinematic-");
        bodies.Add(Create("A", startStatic ? RigidbodyType2D.Static : startKinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic));
        bodies.Add(Create("B")); bodies.Add(Create("C")); bodies.Add(Create("D"));
        Snapshot("created", bodies);
        Physics2D.Simulate(.02f);
        if (action.StartsWith("delete-") || action == "recreate")
        {
            int index = action == "delete-middle" ? 1 : action == "delete-last" ? 3 : 0;
            UnityEngine.Object.DestroyImmediate(bodies[index].gameObject); bodies.RemoveAt(index);
            bodies.Add(Create(action == "recreate" ? "A2" : "E"));
        }
        else if (action.Contains("-"))
        {
            bodies[0].bodyType = action.EndsWith("static") ? RigidbodyType2D.Static :
                action.EndsWith("kinematic") ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
        }
        else if (action == "simulated")
        {
            bodies[0].simulated = false; Physics2D.Simulate(.02f); bodies[0].simulated = true;
        }
        else if (action == "active")
        {
            bodies[0].gameObject.SetActive(false); Physics2D.Simulate(.02f); bodies[0].gameObject.SetActive(true);
        }
        else if (action == "scene")
        {
            var scene = SceneManager.CreateScene("order-witness");
            SceneManager.MoveGameObjectToScene(bodies[0].gameObject, scene);
        }
        for (int i = 0; i < bodies.Count; i++)
        {
            bodies[i].position = new Vector2(10 + i * .9f, 0);
            bodies[i].linearVelocity = new Vector2(.1f * (i + 1), 0);
        }
        Physics2D.SyncTransforms();
        Snapshot("input", bodies);
        for (int step = 1; step <= 3; step++)
        {
            Physics2D.Simulate(.02f); Snapshot("s" + step, bodies);
        }
        foreach (var o in Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        Objects.Clear();
    }
}
