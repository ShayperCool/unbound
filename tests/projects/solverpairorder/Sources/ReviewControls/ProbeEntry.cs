using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ProbeEntry
{
    static readonly List<GameObject> Objects = new List<GameObject>();
    static readonly List<Rigidbody2D> Bodies = new List<Rigidbody2D>();
    public static int Event;
    static bool SettingsObserved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero;
        Physics2D.defaultContactOffset = .01f;
        Physics2D.queriesStartInColliders = true;
        ProbeLog.Begin("settings");
        ProbeLog.Value("gravityAssigned", Physics2D.gravity);
        ProbeLog.Value("simulationModeAssigned", Physics2D.simulationMode.ToString());
        foreach (bool horizontal in new[] { false, true })
        foreach (float angle in new[] { 0f, 23f, 90f })
        foreach (Vector2 offset in new[] { new Vector2(0, 2.5f), new Vector2(.25f, 2.5f), new Vector2(0, 3.1f), new Vector2(.9f, 0) })
            CapsuleTips(horizontal, angle, offset);
        foreach (float length in new[] { 1f, 1.004f, 1.005f, 1.006f })
        foreach (float angle in new[] { 0f, 23f })
        foreach (bool reverse in new[] { false, true })
            CollapsedCore(length, angle, reverse);
        foreach (bool trigger in new[] { false, true })
        foreach (bool callbacks in new[] { false, true })
        foreach (bool enter in new[] { false, true })
            CallbackRemoval(trigger, callbacks, enter);
        // Append boundary cases after the existing controls so their allocation
        // and callback history remains part of the original measurement.
        foreach (bool horizontal in new[] { false, true })
        foreach (float angle in new[] { 0f, 23f, 90f })
        foreach (Vector2 offset in new[] { new Vector2(0, 2), new Vector2(0, 3), Vector2.zero })
            CapsuleTips(horizontal, angle, offset);
        foreach (bool horizontal in new[] { false, true })
        foreach (float angle in new[] { 0f, 23f })
        foreach (float offset in new[] { 0f, .25f })
            DynamicTips(horizontal, angle, offset);
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static GameObject Make(string name, Vector2 position, float angle, bool body, bool movable = false)
    {
        var go = new GameObject(name); Objects.Add(go);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0, 0, angle);
        go.layer = body ? 9 : 8;
        if (body)
        {
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = movable ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.constraints = movable ? RigidbodyConstraints2D.None : RigidbodyConstraints2D.FreezeAll;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            Bodies.Add(rb);
        }
        return go;
    }

    static void CapsuleTips(bool horizontal, float angle, Vector2 offset)
    {
        ProbeLog.Begin("tips/"+horizontal+"/"+angle+"/"+offset.x+"/"+offset.y);
        var q = Quaternion.Euler(0, 0, angle);
        var delta = horizontal ? new Vector2(offset.y, offset.x) : offset;
        delta = q * delta;
        var a = Make("target", Vector2.zero, angle, false).AddComponent<CapsuleCollider2D>();
        var b = Make("moving", delta, angle, true).AddComponent<CapsuleCollider2D>();
        a.direction = b.direction = horizontal ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
        a.size = b.size = horizontal ? new Vector2(3, 1) : new Vector2(1, 3);
        Physics2D.SyncTransforms();
        if (!SettingsObserved)
        {
            SettingsObserved = true;
            ProbeLog.Value("settings.gravityAfterShapes", Physics2D.gravity);
            ProbeLog.Value("settings.simulationModeAfterShapes", Physics2D.simulationMode.ToString());
        }
        Shape("target", a); Shape("moving", b);
        var d = a.Distance(b);
        ProbeLog.Value("distance", d.distance); ProbeLog.Value("distance.normal", d.normal);
        ProbeLog.Value("distance.pointA", d.pointA); ProbeLog.Value("distance.pointB", d.pointB);
        Hit("cast", Physics2D.CapsuleCast(delta, b.size, b.direction, angle, Vector2.right, 1, 1 << 8));
        Step("first"); Step("second");
        Clear();
    }

    static void CollapsedCore(float length, float angle, bool reverse)
    {
        ProbeLog.Begin("collapsed/"+length+"/"+angle+"/"+reverse);
        Vector2 boxPosition = Quaternion.Euler(0, 0, angle) * new Vector2(-1.1f, 0);
        var capsule = Make("capsule", Vector2.zero, angle, reverse).AddComponent<CapsuleCollider2D>();
        capsule.size = new Vector2(1, length);
        var box = Make("box", boxPosition, angle + 45f, !reverse).AddComponent<BoxCollider2D>();
        box.size = Vector2.one;
        Physics2D.SyncTransforms(); Shape("capsule", capsule); Shape("box", box);
        var d = capsule.Distance(box);
        ProbeLog.Value("distance", d.distance); ProbeLog.Value("distance.normal", d.normal);
        ProbeLog.Value("distance.pointA", d.pointA); ProbeLog.Value("distance.pointB", d.pointB);
        Step("first"); Step("second");
        Clear();
    }

    static void DynamicTips(bool horizontal, float angle, float offset)
    {
        ProbeLog.Begin("dynamics/"+horizontal+"/"+angle+"/"+offset);
        var q = Quaternion.Euler(0, 0, angle);
        Vector2 delta = q * (horizontal ? new Vector2(2.5f, offset) : new Vector2(offset, 2.5f));
        var a = Make("target", Vector2.zero, angle, false).AddComponent<CapsuleCollider2D>();
        var b = Make("moving", delta, angle, true, true).AddComponent<CapsuleCollider2D>();
        a.direction = b.direction = horizontal ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
        a.size = b.size = horizontal ? new Vector2(3, 1) : new Vector2(1, 3);
        var body = b.GetComponent<Rigidbody2D>();
        body.linearDamping = body.angularDamping = 0;
        body.linearVelocity = q * (horizontal ? new Vector2(-.5f, 0) : new Vector2(0, -.5f));
        for (int i = 0; i < 8; i++)
        {
            string key = "step/"+i;
            Step(key);
            ProbeLog.Value(key+"/velocity", body.linearVelocity);
            ProbeLog.Value(key+"/angularVelocity", body.angularVelocity);
            ProbeLog.Value(key+"/rotation", body.rotation);
            var contacts = new ContactPoint2D[8]; int count = body.GetContacts(contacts);
            for (int j = 0; j < count; j++)
            {
                ProbeLog.Value(key+"/impulse/"+j+"/normal", contacts[j].normalImpulse);
                ProbeLog.Value(key+"/impulse/"+j+"/tangent", contacts[j].tangentImpulse);
            }
        }
        Clear();
    }

    static Collider2D Pair(string name, int index, bool trigger)
    {
        var peer = Make(name+"-peer", new Vector2(1000 + 20 * index, 0), 0, false).AddComponent<BoxCollider2D>();
        var own = Make(name, new Vector2(1000.9f + 20 * index, 0), 0, true).AddComponent<BoxCollider2D>();
        peer.isTrigger = trigger;
        own.gameObject.AddComponent<CapsuleContactReviewObserver>();
        return own;
    }

    static void CallbackRemoval(bool trigger, bool callbacks, bool enter)
    {
        ProbeLog.Begin("callback/"+trigger+"/"+callbacks+"/"+enter);
        Physics2D.callbacksOnDisable = callbacks; Event = 0;
        var a = Pair("A", 0, trigger);
        Step("warmA");
        var b = Pair("B", 1, trigger);
        Pair("C", 2, trigger);
        if (!enter) Step("warmAll");
        b.GetComponent<CapsuleContactReviewObserver>().Disable = a;
        Step("mutation"); Step("after");
        Clear();
    }

    public static void Record(string owner, string phase, Collider2D other)
    {
        string key = "event/"+Event++;
        ProbeLog.Value(key+"/owner", owner); ProbeLog.Value(key+"/phase", phase);
        ProbeLog.Value(key+"/other", other.name);
    }

    static void Step(string key)
    {
        Physics2D.SyncTransforms(); Physics2D.Simulate(.02f);
        foreach (var body in Bodies)
        {
            string k = key+"/"+body.name;
            var contacts = new ContactPoint2D[8]; int count = body.GetContacts(contacts);
            ProbeLog.Value(k+"/count", count);
            for (int i = 0; i < count; i++)
            {
                string p = k+"/point/"+i;
                ProbeLog.Value(p+"/position", contacts[i].point); ProbeLog.Value(p+"/normal", contacts[i].normal);
                ProbeLog.Value(p+"/separation", contacts[i].separation); ProbeLog.Value(p+"/enabled", contacts[i].enabled);
            }
            ProbeLog.Value(k+"/position", body.position);
        }
        ProbeLog.Value(key+"/events", Event);
    }

    static void Shape(string key, Collider2D collider)
    {
        var group = new PhysicsShapeGroup2D(); collider.GetShapes(group);
        var shapes = new List<PhysicsShape2D>(); var vertices = new List<Vector2>();
        group.GetShapeData(shapes, vertices);
        ProbeLog.Value(key+"/shapes", shapes.Count); ProbeLog.Value(key+"/vertices", vertices.Count);
        for (int i = 0; i < shapes.Count; i++) ProbeLog.Value(key+"/radius/"+i, shapes[i].radius);
        for (int i = 0; i < vertices.Count; i++) ProbeLog.Value(key+"/vertex/"+i, vertices[i]);
    }

    static void Hit(string key, RaycastHit2D hit)
    {
        ProbeLog.Value(key+"/collider", hit.collider ? hit.collider.name : "none");
        ProbeLog.Value(key+"/point", hit.point); ProbeLog.Value(key+"/normal", hit.normal);
        ProbeLog.Value(key+"/distance", hit.distance);
    }

    static void Clear()
    {
        foreach (var go in Objects) UnityEngine.Object.DestroyImmediate(go);
        Objects.Clear(); Bodies.Clear(); Event = 0;
    }
}

public class CapsuleContactReviewObserver : MonoBehaviour
{
    public Collider2D Disable;
    void Record(string phase, Collider2D other)
    {
        ProbeEntry.Record(name, phase, other);
        if (Disable != null) { var target = Disable; Disable = null; target.enabled = false; }
    }
    void OnCollisionEnter2D(Collision2D other) => Record("enter", other.collider);
    void OnCollisionStay2D(Collision2D other) => Record("stay", other.collider);
    void OnCollisionExit2D(Collision2D other) => Record("exit", other.collider);
    void OnTriggerEnter2D(Collider2D other) => Record("trigger-enter", other);
    void OnTriggerStay2D(Collider2D other) => Record("trigger-stay", other);
    void OnTriggerExit2D(Collider2D other) => Record("trigger-exit", other);
}
