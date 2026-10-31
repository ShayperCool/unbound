using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class ProbeEntry
{
    static readonly List<GameObject> Objects = new List<GameObject>();
    static readonly List<Rigidbody2D> Bodies = new List<Rigidbody2D>();
    public static int Step, Event;
    public static string Phase;
    static float Dt;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        // Run the exact original methods, including their own stepping, logging and teardown.
        foreach (var name in new[] { "Bounce", "Stack", "Kinematic" })
        {
            var action = (Action)Delegate.CreateDelegate(typeof(Action), typeof(Probe2D).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
            typeof(Probe2D).GetMethod("Scenario", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "2d." + name.ToLowerInvariant(), action });
        }
        foreach (var kind in new[] { "bounce", "stack", "kinematic" })
            Case("original/" + kind, kind, .02f, 8, 3, 0, .4f, .8f, false, false);
        foreach (var dt in new[] { .01f, .02f, .04f })
        foreach (var iterations in new[] { 2, 8, 16 })
        foreach (var angle in new[] { 0f, 7f })
            Case($"solver/{dt}/{iterations}/{angle}", "stack", dt, iterations, 3, angle, .4f, 0, false, false);
        foreach (var friction in new[] { 0f, .2f, .6f })
        foreach (var bounce in new[] { 0f, .5f, 1f })
        foreach (var reverse in new[] { false, true })
            Case($"material/{friction}/{bounce}/{reverse}", "bounce", .02f, 8, 3, 0, friction, bounce, reverse, false);
        foreach (var pos in new[] { 1, 8 })
        foreach (var neverSleep in new[] { false, true })
            Case($"ordering/{pos}/{neverSleep}", "stack", .02f, 8, pos, 0, .4f, 0, true, neverSleep);
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static GameObject Obj(string name, Vector2 p, float a = 0)
    {
        var o = new GameObject(name); Objects.Add(o);
        o.transform.position = p; o.transform.rotation = Quaternion.Euler(0, 0, a);
        o.AddComponent<MotionRecorder>(); return o;
    }
    static Rigidbody2D Body(GameObject o, bool neverSleep = false)
    {
        var b = o.AddComponent<Rigidbody2D>(); Bodies.Add(b);
        if (neverSleep) b.sleepMode = RigidbodySleepMode2D.NeverSleep;
        return b;
    }
    static void Case(string name, string kind, float dt, int vi, int pi, float angle, float friction, float restitution, bool reverse, bool neverSleep)
    {
        ProbeLog.Begin(name); Step = Event = 0; Phase = "setup"; Dt = dt;
        try
        {
            Physics2D.gravity = new Vector2(0, -9.81f);
            Physics2D.velocityIterations = vi; Physics2D.positionIterations = pi;
            ProbeLog.Value("dt", dt); ProbeLog.Value("velocityIterations", vi); ProbeLog.Value("positionIterations", pi);
            ProbeLog.Value("gravity", Physics2D.gravity); ProbeLog.Value("contactOffset", Physics2D.defaultContactOffset);
            ProbeLog.Value("bounceThreshold", Physics2D.bounceThreshold); ProbeLog.Value("reverse", reverse);
            Action ground = () => {
                var g = Obj("ground", new Vector2(0, -.5f));
                var c = g.AddComponent<BoxCollider2D>(); c.size = new Vector2(30, 1);
                if (name.StartsWith("material/")) c.sharedMaterial = new PhysicsMaterial2D { friction = .3f, bounciness = .2f };
            };
            if (!reverse) ground();
            Rigidbody2D paddle = null;
            if (kind == "bounce")
            {
                var o = Obj("ball", new Vector2(0, 5), angle);
                var c = o.AddComponent<CircleCollider2D>(); c.radius = .5f;
                c.sharedMaterial = new PhysicsMaterial2D { friction = friction, bounciness = restitution };
                Body(o).linearVelocity = new Vector2(1, 0);
            }
            else if (kind == "stack")
            {
                for (int j = 0; j < 4; j++)
                {
                    int i = reverse ? 3 - j : j;
                    var o = Obj("box" + i, new Vector2(i * .1f, .5f + i * 1.01f), angle);
                    o.AddComponent<BoxCollider2D>(); Body(o, neverSleep);
                }
            }
            else
            {
                paddle = Body(Obj("paddle", new Vector2(-4, .5f)));
                paddle.bodyType = RigidbodyType2D.Kinematic;
                paddle.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one;
                Body(Obj("ball", new Vector2(0, .5f))).gameObject.AddComponent<CircleCollider2D>().radius = .5f;
            }
            if (reverse) ground();
            foreach (var o in Objects)
            {
                var c = o.GetComponent<Collider2D>(); var m = c.sharedMaterial;
                ProbeLog.Value(o.name + ".friction", m ? m.friction : .4f);
                ProbeLog.Value(o.name + ".restitution", m ? m.bounciness : 0);
            }
            int count = kind == "stack" ? 300 : kind == "bounce" ? 200 : 100;
            for (Step = 1; Step <= count; Step++)
            {
                Phase = "before"; Snapshot("s" + Step + "/before");
                Phase = "simulate"; Physics2D.Simulate(dt);
                Phase = "after"; Snapshot("s" + Step + "/after");
                if (paddle)
                {
                    paddle.MovePosition(paddle.position + new Vector2(.1f, 0));
                    paddle.MoveRotation(paddle.rotation + 1);
                }
            }
        }
        catch (Exception e) { ProbeLog.Error(e); }
        Phase = "teardown";
        foreach (var o in Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        Objects.Clear(); Bodies.Clear();
        Physics2D.velocityIterations = 8; Physics2D.positionIterations = 3;
    }
    public static void Snapshot(string key)
    {
        foreach (var b in Bodies)
        {
            var k = key + "/" + b.name;
            ProbeLog.Value(k + "/position", b.position); ProbeLog.Value(k + "/rotation", b.rotation);
            ProbeLog.Value(k + "/velocity", b.linearVelocity); ProbeLog.Value(k + "/angularVelocity", b.angularVelocity);
            ProbeLog.Value(k + "/sleep", b.IsSleeping());
            ProbeLog.Value(k + "/transform", b.transform.position);
            var contacts = new ContactPoint2D[8]; int n = b.GetContacts(contacts);
            ProbeLog.Value(k + "/contacts", n);
            for (int i = 0; i < n; i++) Contact(k + "/c" + i, contacts[i]);
        }
    }
    public static void Contact(string k, ContactPoint2D p)
    {
        ProbeLog.Value(k + "/point", p.point); ProbeLog.Value(k + "/normal", p.normal);
        ProbeLog.Value(k + "/separation", p.separation); ProbeLog.Value(k + "/relativeVelocity", p.relativeVelocity);
        ProbeLog.Value(k + "/normalImpulse", p.normalImpulse); ProbeLog.Value(k + "/tangentImpulse", p.tangentImpulse);
    }
}
public class MotionRecorder : MonoBehaviour
{
    void Record(string kind, Collision2D c)
    {
        string k = "e" + ProbeEntry.Event++;
        ProbeLog.Value(k + "/kind", kind); ProbeLog.Value(k + "/self", name); ProbeLog.Value(k + "/other", c.collider.name);
        ProbeLog.Value(k + "/step", ProbeEntry.Step); ProbeLog.Value(k + "/phase", ProbeEntry.Phase);
        ProbeLog.Value(k + "/relativeVelocity", c.relativeVelocity); ProbeLog.Value(k + "/count", c.contactCount);
        for (int i = 0; i < c.contactCount; i++) ProbeEntry.Contact(k + "/c" + i, c.GetContact(i));
        var b = GetComponent<Rigidbody2D>();
        if (b) { ProbeLog.Value(k + "/position", b.position); ProbeLog.Value(k + "/rotation", b.rotation); ProbeLog.Value(k + "/velocity", b.linearVelocity); ProbeLog.Value(k + "/angularVelocity", b.angularVelocity); }
    }
    void OnCollisionEnter2D(Collision2D c) => Record("enter", c);
    void OnCollisionStay2D(Collision2D c) => Record("stay", c);
    void OnCollisionExit2D(Collision2D c) => Record("exit", c);
}
