using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ProbeEntry
{
    static readonly List<GameObject> Objects = new List<GameObject>();
    static int enters, stays, exits;
    public sealed class Contacts : MonoBehaviour
    {
        void OnCollisionEnter2D(Collision2D c) { enters++; }
        void OnCollisionStay2D(Collision2D c) { stays++; }
        void OnCollisionExit2D(Collision2D c) { exits++; }
        void OnTriggerEnter2D(Collider2D c) { enters++; }
        void OnTriggerStay2D(Collider2D c) { stays++; }
        void OnTriggerExit2D(Collider2D c) { exits++; }
    }

    static GameObject Make(string name, Vector2 position, int layer = 0)
    {
        var go = new GameObject(name); go.transform.position = position; go.layer = layer;
        Objects.Add(go); return go;
    }
    static Rigidbody2D Body(Vector2 position, bool circle = false, int layer = 0)
    {
        var go = Make("body", position, layer);
        var rb = go.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.freezeRotation = true;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
        if (circle) go.AddComponent<CircleCollider2D>().radius = .25f;
        else go.AddComponent<BoxCollider2D>().size = new Vector2(.5f, .5f);
        go.AddComponent<Contacts>(); return rb;
    }
    static BoxCollider2D Box(string name, Vector2 position, Vector2 size, int layer = 0, float angle = 0)
    {
        var go = Make(name, position, layer); go.transform.rotation = Quaternion.Euler(0, 0, angle);
        var box = go.AddComponent<BoxCollider2D>(); box.size = size; return box;
    }
    static void Begin(string name)
    {
        for (int i = Objects.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(Objects[i]);
        Objects.Clear(); enters = stays = exits = 0;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero; Physics2D.useSubStepping = false;
        Physics2D.useSubStepContacts = false; Physics2D.minSubStepFPS = 30; Physics2D.maxSubStepCount = 4;
        Physics2D.simulationLayers = -1; Physics2D.queriesHitTriggers = true;
        ProbeLog.Begin(name);
    }
    static void State(string key, Rigidbody2D rb)
    {
        ProbeLog.Value(key + ".position", rb.position); ProbeLog.Value(key + ".velocity", rb.linearVelocity);
        ProbeLog.Value(key + ".contacts", rb.GetContacts(new ContactPoint2D[16]));
        ProbeLog.Value(key + ".enter", enters); ProbeLog.Value(key + ".stay", stays); ProbeLog.Value(key + ".exit", exits);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Begin("api");
        ProbeLog.Value("bodySimulationLayers", typeof(Rigidbody2D).GetProperty("simulationLayers") != null);
        foreach (string p in new[] { "useSubStepping", "useSubStepContacts", "maxSubStepCount", "minSubStepFPS" })
            ProbeLog.Value(p, typeof(Physics2D).GetProperty(p) != null);
        var defaults = new Rigidbody2D.SlideMovement();
        ProbeLog.Value("maxIterations", defaults.maxIterations); ProbeLog.Value("surfaceSlideAngle", defaults.surfaceSlideAngle);
        ProbeLog.Value("gravitySlipAngle", defaults.gravitySlipAngle); ProbeLog.Value("surfaceUp", defaults.surfaceUp);
        ProbeLog.Value("surfaceAnchor", defaults.surfaceAnchor); ProbeLog.Value("gravity", defaults.gravity);
        ProbeLog.Value("startPosition", defaults.startPosition); ProbeLog.Value("layerMask", (int)defaults.layerMask);
        ProbeLog.Value("useLayerMask", defaults.useLayerMask); ProbeLog.Value("useStartPosition", defaults.useStartPosition);
        ProbeLog.Value("useNoMove", defaults.useNoMove); ProbeLog.Value("useSimulationMove", defaults.useSimulationMove);
        ProbeLog.Value("useAttachedTriggers", defaults.useAttachedTriggers);
        ProbeLog.Value("serializedMovement", JsonUtility.ToJson(defaults));
        foreach (bool circle in new[] { false, true })
        foreach (float radius in new[] { 0f, .1f })
        foreach (string layout in new[] { "single", "chain", "ghosts", "duplicates" })
            Edges(circle, radius, layout);
        foreach (SimulationMode2D mode in new[] { SimulationMode2D.FixedUpdate, SimulationMode2D.Update, SimulationMode2D.Script })
        foreach (bool sub in new[] { false, true })
        foreach (bool contacts in new[] { false, true })
        foreach (float dt in new[] { .02f, .1f, .25f })
            SubSteps(mode, sub, contacts, dt);
        foreach (string test in new[] { "mask", "floor-excluded", "floor-included", "child-layer", "force", "move", "scene" }) Layers(test);
        foreach (string test in new[] { "free", "wall", "ramp", "anchor", "gravity", "trigger", "selected", "compound" })
        foreach (string option in new[] { "plain", "iterations1", "iterations0", "angle0", "angle45", "slip0", "slip45", "upzero", "upright", "start", "mask", "nomove", "simmove", "triggers" })
            Slide(test, option);
        File.WriteAllText(output, ProbeLog.Json());
        Debug.Log("advanced 2D probe complete");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Edges(bool circle, float radius, string layout)
    {
        Begin("edge/" + circle + "/" + radius + "/" + layout);
        var material = new PhysicsMaterial2D { friction = 0, bounciness = 0 };
        if (layout == "single" || layout == "duplicates")
        {
            var edge = Make("edge", Vector2.zero).AddComponent<EdgeCollider2D>();
            edge.points = layout == "duplicates" ? new[] { new Vector2(-4, 0), Vector2.zero, Vector2.zero, new Vector2(4, 0) }
                : new[] { new Vector2(-4, 0), Vector2.zero, new Vector2(4, 0) };
            edge.edgeRadius = radius; edge.sharedMaterial = material;
        }
        else for (int i = -2; i < 2; i++)
        {
            var edge = Make("edge" + i, Vector2.zero).AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(i * 2, 0), new Vector2((i + 1) * 2, 0) };
            edge.useAdjacentStartPoint = edge.useAdjacentEndPoint = layout == "ghosts";
            edge.adjacentStartPoint = new Vector2((i - 1) * 2, 0); edge.adjacentEndPoint = new Vector2((i + 2) * 2, 0);
            edge.edgeRadius = radius; edge.sharedMaterial = material;
        }
        var rb = Body(new Vector2(-3, .27f + radius), circle);
        rb.GetComponent<Collider2D>().sharedMaterial = material; rb.gravityScale = 1;
        Physics2D.gravity = new Vector2(0, -10); rb.linearVelocity = new Vector2(5, 0);
        for (int i = 0; i < 64; i++) { Physics2D.Simulate(.02f); State(i.ToString(), rb); }
        UnityEngine.Object.DestroyImmediate(material);
    }
    static void SubSteps(SimulationMode2D mode, bool sub, bool contacts, float dt)
    {
        Begin("sub/" + mode + "/" + sub + "/" + contacts + "/" + dt);
        Physics2D.simulationMode = mode; Physics2D.useSubStepping = sub; Physics2D.useSubStepContacts = contacts;
        Box("sensor", new Vector2(.5f, 0), new Vector2(.1f, 2)).isTrigger = true;
        var rb = Body(Vector2.zero); rb.linearVelocity = new Vector2(10, 0); rb.linearDamping = .5f;
        rb.AddForce(new Vector2(2, 0));
        for (int i = 0; i < 3; i++) { ProbeLog.Value(i + ".ran", Physics2D.Simulate(dt)); State(i.ToString(), rb); }
    }
    static void Layers(string test)
    {
        Begin("layers/" + test);
        var rb = Body(new Vector2(0, 1)); var other = Body(new Vector2(5, 1), layer: 8);
        Box("floor", new Vector2(0, -.5f), new Vector2(20, 1), layer: 8);
        rb.gravityScale = other.gravityScale = 1; Physics2D.gravity = new Vector2(0, -10);
        other.linearVelocity = new Vector2(1, 0);
        if (test == "child-layer") { rb.GetComponent<Collider2D>().enabled = false; var child = Make("child", rb.position, 8); child.transform.SetParent(rb.transform, true); child.AddComponent<BoxCollider2D>(); }
        if (test == "force") other.AddForce(new Vector2(3, 0));
        if (test == "move") other.MovePosition(new Vector2(8, 1));
        Physics2D.simulationLayers = 1 << 8;
        for (int i = 0; i < 32; i++)
        {
            int mask = test == "floor-included" ? (1 << 0) | (1 << 8) : 1;
            if (i == 16 && (test == "force" || test == "move")) mask = -1;
            bool ran = test == "scene" ? Physics2D.defaultPhysicsScene.Simulate(.02f, mask) : Physics2D.Simulate(.02f, mask);
            ProbeLog.Value(i + ".ran", ran); State(i.ToString(), rb);
            ProbeLog.Value(i + ".other", other.position); ProbeLog.Value(i + ".otherVelocity", other.linearVelocity);
        }
    }
    static void Hit(string key, RaycastHit2D hit)
    {
        ProbeLog.Value(key + ".collider", hit.collider == null ? "null" : hit.collider.name);
        ProbeLog.Value(key + ".point", hit.point); ProbeLog.Value(key + ".normal", hit.normal);
        ProbeLog.Value(key + ".centroid", hit.centroid); ProbeLog.Value(key + ".distance", hit.distance);
        ProbeLog.Value(key + ".fraction", hit.fraction);
    }
    static void Slide(string test, string option)
    {
        Begin("slide/" + test + "/" + option);
        var rb = Body(new Vector2(0, 1)); rb.bodyType = RigidbodyType2D.Kinematic;
        Vector2 velocity = new Vector2(4, -1);
        if (test != "free") Box("floor", new Vector2(0, -.5f), new Vector2(20, 1), layer: 8, angle: test == "ramp" ? 30 : 0);
        if (test == "wall" || test == "selected" || test == "compound") Box("wall", new Vector2(2, 2), new Vector2(1, 6));
        if (test == "trigger") rb.GetComponent<Collider2D>().isTrigger = true;
        var m = new Rigidbody2D.SlideMovement(); m.surfaceAnchor = Vector2.zero; m.gravity = Vector2.zero;
        if (test == "anchor") { m.surfaceAnchor = new Vector2(0, -2); velocity = new Vector2(4, 0); }
        if (test == "gravity" || test == "ramp") { m.gravity = new Vector2(0, -4); velocity = Vector2.zero; }
        if (test == "selected" || test == "compound")
        {
            var child = Make("selected", rb.position + new Vector2(.5f, 0)); child.transform.SetParent(rb.transform, true);
            var col = child.AddComponent<CircleCollider2D>(); col.radius = .25f;
            if (test == "selected") m.selectedCollider = col;
        }
        switch (option)
        {
            case "iterations1": m.maxIterations = 1; break;
            case "iterations0": m.maxIterations = 0; break;
            case "angle0": m.surfaceSlideAngle = 0; break;
            case "angle45": m.surfaceSlideAngle = 45; break;
            case "slip0": m.gravitySlipAngle = 0; break;
            case "slip45": m.gravitySlipAngle = 45; break;
            case "upzero": m.surfaceUp = Vector2.zero; break;
            case "upright": m.surfaceUp = Vector2.right; break;
            case "start": m.SetStartPosition(new Vector2(-1, 1)); break;
            case "mask": m.SetLayerMask(1 << 8); break;
            case "nomove": m.useNoMove = true; break;
            case "simmove": m.useSimulationMove = true; break;
            case "triggers": m.useAttachedTriggers = true; break;
        }
        Physics2D.SyncTransforms();
        for (int i = 0; i < 2; i++)
        {
            try
            {
                var r = rb.Slide(velocity, .5f, m); string k = i.ToString();
                ProbeLog.Value(k + ".position", r.position); ProbeLog.Value(k + ".remainingVelocity", r.remainingVelocity);
                ProbeLog.Value(k + ".iterationsUsed", r.iterationsUsed); Hit(k + ".slide", r.slideHit); Hit(k + ".surface", r.surfaceHit);
                ProbeLog.Value(k + ".bodyBefore", rb.position); Physics2D.Simulate(.02f); State(k + ".after", rb);
            }
            catch (Exception e) { ProbeLog.Value(i + ".exception", e.GetType().Name); }
        }
    }
}
