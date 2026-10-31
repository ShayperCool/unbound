using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ProbeEntry
{
    public static int Step, Event;
    public static string Phase;
    public static Action Reaction;
    static readonly List<GameObject> Objects = new List<GameObject>();
    static Rigidbody2D Body;
    static Collider2D A, B;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var warmup = new GameObject("warmup"); warmup.AddComponent<Rigidbody2D>();
        UnityEngine.Object.DestroyImmediate(warmup);
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.autoSyncTransforms = false;
        Physics2D.gravity = Vector2.zero;
        foreach (var sensor in new[] { false, true })
        foreach (var callbacks in new[] { false, true })
        foreach (var kind in new[] { RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
        foreach (var target in new[] { RigidbodyType2D.Static, RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
        foreach (var sleep in new[] { false, true })
        foreach (var action in new[] { "collider-off", "simulated-off", "inactive", "destroy-collider", "destroy-body", "destroy-object" })
            Case($"lifetime/{sensor}/{callbacks}/{kind}/{target}/{sleep}/{action}", () =>
            {
                Physics2D.callbacksOnDisable = callbacks;
                Pair(sensor, kind, target);
                Sim(); Sim();
                if (sleep) { Body.Sleep(); var b = B.attachedRigidbody; if (b) b.Sleep(); }
                Phase = "action"; Act(action); Snapshot("after-action");
                Sim(); Sim();
                Phase = "reactivate";
                if (action == "collider-off") A.enabled = true;
                if (action == "simulated-off") Body.simulated = true;
                if (action == "inactive") Body.gameObject.SetActive(true);
                Snapshot("reactivated"); Sim(); Sim();
            });
        foreach (var sensor in new[] { false, true })
        foreach (var callbacks in new[] { false, true })
        foreach (var action in new[] { "collider-off", "simulated-off", "inactive", "destroy-collider", "destroy-body", "destroy-object" })
            Case($"callback/{sensor}/{callbacks}/{action}", () =>
            {
                Physics2D.callbacksOnDisable = callbacks;
                Pair(sensor, RigidbodyType2D.Dynamic, RigidbodyType2D.Static);
                Reaction = () => Act(action);
                Sim(); Sim(); Snapshot("after-callback");
            });
        foreach (var sensor in new[] { false, true })
        foreach (var kind in new[] { RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
        foreach (var target in new[] { "implicit", "static", "kinematic" })
            Case($"static-move/{sensor}/{kind}/{target}", () =>
            {
                Physics2D.callbacksOnDisable = true;
                Pair(sensor, kind, RigidbodyType2D.Static);
                if (target == "static") B.gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                if (target == "kinematic") B.gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                Body.position = new Vector2(-3, 0); Sim(); Body.Sleep();
                Phase = "move-in"; B.transform.position = new Vector3(-2.5f, 0, 0);
                Physics2D.SyncTransforms(); Snapshot("sync-in"); Sim(); Sim();
                Body.Sleep(); Phase = "move-away"; B.transform.position = new Vector3(4, 0, 0);
                Physics2D.SyncTransforms(); Snapshot("sync-out"); Sim(); Sim();
            });
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Case(string name, Action action)
    {
        ProbeLog.Begin(name); Step = Event = 0; Phase = "setup"; Reaction = null;
        try { action(); } catch (Exception e) { ProbeLog.Error(e); }
        Reaction = null; Phase = "teardown";
        foreach (var o in Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        Objects.Clear();
    }
    static GameObject Obj(string name, Vector2 p)
    {
        var o = new GameObject(name); o.transform.position = p; Objects.Add(o); return o;
    }
    static void Pair(bool sensor, RigidbodyType2D kind, RigidbodyType2D target)
    {
        var b = Obj("B", Vector2.zero);
        B = b.AddComponent<BoxCollider2D>(); ((BoxCollider2D)B).size = new Vector2(2, 2); B.isTrigger = sensor;
        if (target != RigidbodyType2D.Static)
        {
            var rb = b.AddComponent<Rigidbody2D>(); rb.bodyType = target; rb.gravityScale = 0; rb.useFullKinematicContacts = true;
        }
        b.AddComponent<LifecycleRecorder>();
        var a = Obj("A", new Vector2(-.5f, 0));
        Body = a.AddComponent<Rigidbody2D>(); Body.bodyType = kind; Body.gravityScale = 0; Body.useFullKinematicContacts = true;
        A = a.AddComponent<CircleCollider2D>(); ((CircleCollider2D)A).radius = .25f;
        a.AddComponent<LifecycleRecorder>();
    }
    static void Act(string action)
    {
        if (action == "collider-off") A.enabled = false;
        if (action == "simulated-off") Body.simulated = false;
        if (action == "inactive") Body.gameObject.SetActive(false);
        if (action == "destroy-collider") UnityEngine.Object.DestroyImmediate(A);
        if (action == "destroy-body") UnityEngine.Object.DestroyImmediate(Body);
        if (action == "destroy-object") UnityEngine.Object.DestroyImmediate(Body.gameObject);
    }
    static void Snapshot(string key)
    {
        ProbeLog.Value(key + "/body", (bool)Body);
        if (Body) { ProbeLog.Value(key + "/sleep", Body.IsSleeping()); ProbeLog.Value(key + "/position", Body.position); }
        ProbeLog.Value(key + "/collider", (bool)A);
        if (A && B) ProbeLog.Value(key + "/touching", A.IsTouching(B));
    }
    static void Sim()
    {
        Phase = "simulate"; Step++; Physics2D.Simulate(.02f); Snapshot(Step + "/after");
    }
}
public class LifecycleRecorder : MonoBehaviour
{
    void Record(string kind, Collider2D other)
    {
        var key = "event/" + ProbeEntry.Event++;
        ProbeLog.Value(key + "/self", name);
        ProbeLog.Value(key + "/other", ReferenceEquals(other, null) ? "null" : other.name);
        ProbeLog.Value(key + "/phase", ProbeEntry.Phase);
        ProbeLog.Value(key + "/kind", kind);
        ProbeLog.Value(key + "/step", ProbeEntry.Step);
        if (name == "A" && kind.EndsWith("Enter"))
        {
            var reaction = ProbeEntry.Reaction; ProbeEntry.Reaction = null;
            if (reaction != null) { ProbeEntry.Phase = "callback-action"; reaction(); ProbeEntry.Phase = "simulate"; }
        }
    }
    void OnTriggerEnter2D(Collider2D c) => Record("triggerEnter", c);
    void OnTriggerStay2D(Collider2D c) => Record("triggerStay", c);
    void OnTriggerExit2D(Collider2D c) => Record("triggerExit", c);
    void OnCollisionEnter2D(Collision2D c) => Record("contactEnter", c.collider);
    void OnCollisionStay2D(Collision2D c) => Record("contactStay", c.collider);
    void OnCollisionExit2D(Collision2D c) => Record("contactExit", c.collider);
}
