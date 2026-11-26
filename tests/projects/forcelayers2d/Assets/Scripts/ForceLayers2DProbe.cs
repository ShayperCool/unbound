using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $FORCELAYERS2D_OUT (tests/value_probe.py rows): Collider2D.forceSendLayers
/// and forceReceiveLayers. A circle dropped on static ground, and a moving circle hitting one at
/// rest, with every layer left in and with one side's send or receive mask leaving the other's
/// layer out; every fifth 0.02 s step: each body's position and velocity, whether they touch,
/// their contact count and the collision messages each got so far.
/// </summary>
public class ForceLayers2DProbe : MonoBehaviour
{
    const string Variable = "FORCELAYERS2D_OUT";
    const int LayerG = 8, LayerA = 9, LayerB = 10;
    static StreamWriter output;

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Row(string key, string json) => output.Write("{\"k\":\"" + key + "\",\"v\":" + json + "}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("ForceLayers2DProbe").AddComponent<ForceLayers2DProbe>();
    }

    /// <summary>Counts the collision messages its object gets.</summary>
    class Counter : MonoBehaviour
    {
        public int enter, stay, exit;
        void OnCollisionEnter2D(Collision2D c) => enter++;
        void OnCollisionStay2D(Collision2D c) => stay++;
        void OnCollisionExit2D(Collision2D c) => exit++;
    }

    static Rigidbody2D Circle(string name, int layer, Vector2 at, float gravity)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.position = at;
        go.AddComponent<CircleCollider2D>().radius = 0.5f;
        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = gravity;
        go.AddComponent<Counter>();
        return body;
    }

    static string Body(Rigidbody2D b)
    {
        var c = b.GetComponent<Counter>();
        return F(b.position.x) + "," + F(b.position.y) + "," + F(b.linearVelocity.x) + "," + F(b.linearVelocity.y)
            + "," + c.enter + "," + c.stay + "," + c.exit;
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        Physics2D.simulationMode = SimulationMode2D.Script;
        var scenarios = new List<(string name, Rigidbody2D first, Rigidbody2D second, Collider2D ground)>();
        float x = 0f;
        // A circle dropped from 0.2 above static ground.
        foreach (var variant in new[] { "all", "circleReceivesNotG", "groundSendsNotA", "circleSendsNotG", "groundReceivesNotA" })
        {
            var ground = new GameObject("ground " + variant) { layer = LayerG };
            ground.transform.position = new Vector2(x, 0f);
            var box = ground.AddComponent<BoxCollider2D>();
            box.size = new Vector2(4f, 1f);
            ground.AddComponent<Counter>();
            var circle = Circle("drop " + variant, LayerA, new Vector2(x, 1.2f), 1f);
            var cc = circle.GetComponent<Collider2D>();
            if (variant == "circleReceivesNotG") cc.forceReceiveLayers = ~(1 << LayerG);
            if (variant == "circleSendsNotG") cc.forceSendLayers = ~(1 << LayerG);
            if (variant == "groundSendsNotA") box.forceSendLayers = ~(1 << LayerA);
            if (variant == "groundReceivesNotA") box.forceReceiveLayers = ~(1 << LayerA);
            scenarios.Add(("drop/" + variant, circle, null, box));
            x += 20f;
        }
        // A circle at 3 m/s along +x hits one at rest; no gravity.
        foreach (var variant in new[] { "all", "aSendsNotB", "bReceivesNotA", "bSendsNotA", "aReceivesNotB", "both" })
        {
            var a = Circle("push A " + variant, LayerA, new Vector2(x, 50f), 0f);
            var b = Circle("push B " + variant, LayerB, new Vector2(x + 1.5f, 50f), 0f);
            a.linearVelocity = new Vector2(3f, 0f);
            var ca = a.GetComponent<Collider2D>();
            var cb = b.GetComponent<Collider2D>();
            if (variant == "aSendsNotB" || variant == "both") ca.forceSendLayers = ~(1 << LayerB);
            if (variant == "bReceivesNotA") cb.forceReceiveLayers = ~(1 << LayerA);
            if (variant == "bSendsNotA" || variant == "both") cb.forceSendLayers = ~(1 << LayerA);
            if (variant == "aReceivesNotB") ca.forceReceiveLayers = ~(1 << LayerB);
            scenarios.Add(("push/" + variant, a, b, null));
            x += 20f;
        }
        // The circle's collider on a child of another layer (10) than its body's (9): which
        // layer the ground's send mask reads; and a continuous body the ground sends nothing to.
        foreach (var variant in new[] { "childLayerNotSent", "bodyLayerNotSent", "continuous" })
        {
            var ground = new GameObject("ground " + variant) { layer = LayerG };
            ground.transform.position = new Vector2(x, 0f);
            var box = ground.AddComponent<BoxCollider2D>();
            box.size = new Vector2(4f, 1f);
            ground.AddComponent<Counter>();
            Rigidbody2D circle;
            if (variant == "continuous")
            {
                circle = Circle("drop " + variant, LayerA, new Vector2(x, 1.2f), 1f);
                circle.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                box.forceSendLayers = ~(1 << LayerA);
            }
            else
            {
                var go = new GameObject("drop " + variant) { layer = LayerA };
                go.transform.position = new Vector2(x, 1.2f);
                circle = go.AddComponent<Rigidbody2D>();
                go.AddComponent<Counter>();
                var child = new GameObject("collider") { layer = LayerB };
                child.transform.SetParent(go.transform, false);
                child.AddComponent<CircleCollider2D>().radius = 0.5f;
                box.forceSendLayers = ~(1 << (variant == "childLayerNotSent" ? LayerB : LayerA));
            }
            scenarios.Add(("drop/" + variant, circle, null, box));
            x += 20f;
        }
        for (int step = 0; step <= 50; step++)
        {
            if (step % 5 == 0)
                foreach (var (name, first, second, ground) in scenarios)
                {
                    var other = (Collider2D)ground ?? second.GetComponent<Collider2D>();
                    var mine = first.GetComponentInChildren<Collider2D>();
                    var contacts = new List<ContactPoint2D>();
                    int count = mine.GetContacts(contacts);
                    float normal = 0f, tangent = 0f;
                    foreach (var c in contacts) { normal += c.normalImpulse; tangent += c.tangentImpulse; }
                    string v = "[" + Body(first) + (second != null ? "," + Body(second) : "")
                        + "," + (mine.IsTouching(other) ? 1 : 0) + "," + count + "," + F(normal) + "," + F(tangent) + "]";
                    Row(name + "." + step, v);
                }
            Physics2D.Simulate(0.02f);
        }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
        output = null;
        EditorApplication.Exit(0);
    }
}
