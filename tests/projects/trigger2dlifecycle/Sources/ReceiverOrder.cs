using System;
using System.IO;
using UnityEngine;
public static class ProbeEntry
{
    public static int Event;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var warmup = new GameObject(); warmup.AddComponent<Rigidbody2D>(); UnityEngine.Object.DestroyImmediate(warmup);
        Physics2D.gravity = Vector2.zero; Physics2D.simulationMode = SimulationMode2D.Script;
        foreach (var reverse in new[] { false, true })
        foreach (var shapeA in new[] { "circle", "box" })
        foreach (var shapeB in new[] { "circle", "box" })
        foreach (var kind in new[] { RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
        foreach (var other in new[] { RigidbodyType2D.Static, RigidbodyType2D.Dynamic, RigidbodyType2D.Kinematic })
        foreach (var sensor in new[] { false, true })
        {
            ProbeLog.Begin($"order/{reverse}/{shapeA}/{shapeB}/{kind}/{other}/{sensor}"); Event = 0;
            GameObject a, b;
            if (reverse) { a = Make("A", shapeA, kind, sensor); b = Make("B", shapeB, other, sensor); }
            else { b = Make("B", shapeB, other, sensor); a = Make("A", shapeA, kind, sensor); }
            Physics2D.Simulate(.02f); Physics2D.Simulate(.02f);
            UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(a);
        }
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static GameObject Make(string name, string shape, RigidbodyType2D kind, bool sensor)
    {
        var go = new GameObject(name);
        if (kind != RigidbodyType2D.Static) { var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = kind; rb.useFullKinematicContacts = true; rb.gravityScale = 0; }
        Collider2D c = shape == "circle" ? (Collider2D)go.AddComponent<CircleCollider2D>() : go.AddComponent<BoxCollider2D>(); c.isTrigger = sensor;
        go.AddComponent<OrderRecorder>(); return go;
    }
}
public class OrderRecorder : MonoBehaviour
{
    void Record(string kind) { var k = "event/" + ProbeEntry.Event++; ProbeLog.Value(k + "/self", name); ProbeLog.Value(k + "/kind", kind); }
    void OnTriggerEnter2D(Collider2D c) => Record("triggerEnter");
    void OnTriggerStay2D(Collider2D c) => Record("triggerStay");
    void OnTriggerExit2D(Collider2D c) => Record("triggerExit");
    void OnCollisionEnter2D(Collision2D c) => Record("contactEnter");
    void OnCollisionStay2D(Collision2D c) => Record("contactStay");
    void OnCollisionExit2D(Collision2D c) => Record("contactExit");
}
