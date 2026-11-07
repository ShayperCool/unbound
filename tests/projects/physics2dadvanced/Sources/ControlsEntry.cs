using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ControlsEntry
{
    static readonly List<GameObject> objects = new List<GameObject>();
    static GameObject Make(string name, Vector2 position)
    {
        var go = new GameObject(name); go.transform.position = position; objects.Add(go); return go;
    }
    static void Begin(string name)
    {
        foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go);
        objects.Clear(); Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
        Physics2D.useSubStepping = false; ProbeLog.Begin(name);
    }
    static Rigidbody2D Body(Vector2 position, bool circle = false)
    {
        var go = Make("body", position); var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0; rb.freezeRotation = true;
        if (circle) go.AddComponent<CircleCollider2D>().radius = .25f;
        else go.AddComponent<BoxCollider2D>().size = new Vector2(.5f, .5f);
        return rb;
    }
    static void Record(string key, Rigidbody2D rb)
    {
        ProbeLog.Value(key + ".position", rb.position); ProbeLog.Value(key + ".velocity", rb.linearVelocity);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("ADVANCED_CONTROLS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        foreach (float dy in new[] { -1f, 0f, 1f })
        foreach (float dt in new[] { .25f, .5f, 1f })
        foreach (int iterations in new[] { 1, 2, 3 })
        {
            Begin("wall/" + dy + "/" + dt + "/" + iterations);
            var wall = Make("wall", new Vector2(2, 2)).AddComponent<BoxCollider2D>(); wall.size = new Vector2(1, 20);
            var rb = Body(new Vector2(0, 1)); rb.bodyType = RigidbodyType2D.Kinematic;
            var m = new Rigidbody2D.SlideMovement { gravity = Vector2.zero, surfaceAnchor = Vector2.zero, maxIterations = iterations };
            Physics2D.SyncTransforms();
            var r = rb.Slide(new Vector2(4, dy), dt, m);
            ProbeLog.Value("position", r.position); ProbeLog.Value("remaining", r.remainingVelocity);
            ProbeLog.Value("iterations", r.iterationsUsed); ProbeLog.Value("distance", r.slideHit.distance);
        }
        foreach (float dt in new[] { 0f, .01f, .0333333351f, .04f, .06666667f, .07f, .25f })
        {
            Begin("sub/" + dt); Physics2D.useSubStepping = true; Physics2D.minSubStepFPS = 30; Physics2D.maxSubStepCount = 4;
            var rb = Body(Vector2.zero); rb.linearVelocity = Vector2.right; rb.linearDamping = 2;
            Physics2D.Simulate(dt); Record("body", rb);
            ProbeLog.Value("count", Physics2D.defaultPhysicsScene.subStepCount);
            ProbeLog.Value("lost", Physics2D.defaultPhysicsScene.subStepLostTime);
        }
        foreach (float bend in new[] { -.25f, .25f })
        foreach (bool circle in new[] { false, true })
        foreach (bool ghosts in new[] { false, true })
        foreach (float radius in new[] { 0f, .1f })
        {
            Begin("seam/" + bend + "/" + circle + "/" + ghosts + "/" + radius);
            var points = new[] { new Vector2(-4, 0), new Vector2(-1, 0), new Vector2(1, bend), new Vector2(4, bend) };
            var mat = new PhysicsMaterial2D { friction = 0, bounciness = 0 };
            for (int i = 0; i < 3; i++)
            {
                var e = Make("edge" + i, Vector2.zero).AddComponent<EdgeCollider2D>(); e.points = new[] { points[i], points[i+1] };
                e.edgeRadius = radius; e.sharedMaterial = mat;
                e.useAdjacentStartPoint = ghosts && i > 0; e.useAdjacentEndPoint = ghosts && i < 2;
                e.adjacentStartPoint = points[Math.Max(0, i-1)]; e.adjacentEndPoint = points[Math.Min(3, i+2)];
            }
            var rb = Body(new Vector2(-3, .27f + radius), circle); rb.GetComponent<Collider2D>().sharedMaterial = mat;
            rb.gravityScale = 1; Physics2D.gravity = new Vector2(0, -10); rb.linearVelocity = new Vector2(5, 0);
            for (int i = 0; i < 64; i++) { Physics2D.Simulate(.02f); Record(i.ToString(), rb); }
            UnityEngine.Object.DestroyImmediate(mat);
        }
        foreach (bool kinematic in new[] { false, true })
        foreach (bool rotation in new[] { false, true })
        {
            Begin("move/" + kinematic + "/" + rotation);
            var rb = Body(Vector2.zero); rb.freezeRotation = false; rb.gravityScale = 1; Physics2D.gravity = new Vector2(0, -10);
            if (kinematic) rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = new Vector2(1, 2); rb.angularVelocity = 3;
            if (rotation) rb.MoveRotation(90); else rb.MovePosition(new Vector2(3, 1));
            for (int i = 0; i < 3; i++) { Physics2D.Simulate(.02f); Record(i.ToString(), rb); ProbeLog.Value(i + ".angle", rb.rotation); ProbeLog.Value(i + ".angularVelocity", rb.angularVelocity); }
        }
        foreach (float damping in new[] { 0f, 2f })
        foreach (bool rotation in new[] { false, true })
        {
            Begin("move-damping/" + damping + "/" + rotation);
            var rb = Body(Vector2.zero); rb.freezeRotation = false; rb.linearDamping = rb.angularDamping = damping;
            rb.gravityScale = 1; Physics2D.gravity = new Vector2(0, -10);
            rb.linearVelocity = new Vector2(1, 2); rb.angularVelocity = 3;
            if (rotation) rb.MoveRotation(90); else rb.MovePosition(new Vector2(3, 1));
            Physics2D.Simulate(.02f); Record("body", rb); ProbeLog.Value("angle", rb.rotation); ProbeLog.Value("angularVelocity", rb.angularVelocity);
        }
        foreach (float offset in new[] { .001f, .01f, .1f })
        {
            Begin("slide-offset/" + offset); Physics2D.defaultContactOffset = offset;
            var wall = Make("wall", new Vector2(2, 2)).AddComponent<BoxCollider2D>(); wall.size = new Vector2(1, 20);
            var rb = Body(new Vector2(0, 1)); rb.bodyType = RigidbodyType2D.Kinematic;
            var m = new Rigidbody2D.SlideMovement { gravity = Vector2.zero, surfaceAnchor = Vector2.zero };
            var r = rb.Slide(Vector2.right * 4, .5f, m);
            ProbeLog.Value("position", r.position); ProbeLog.Value("distance", r.slideHit.distance);
        }
        Physics2D.defaultContactOffset = .01f;
        Begin("results-serialization");
        ProbeLog.Value("json", JsonUtility.ToJson(default(Rigidbody2D.SlideResults)));
        foreach (float dy in new[] { -1f, 1f })
        {
            Begin("anchor-direction/" + dy);
            var floor = Make("floor", new Vector2(0, -.5f)).AddComponent<BoxCollider2D>(); floor.size = new Vector2(20, 1);
            var rb = Body(new Vector2(0, 1)); rb.bodyType = RigidbodyType2D.Kinematic;
            var m = new Rigidbody2D.SlideMovement { gravity = Vector2.zero, surfaceAnchor = new Vector2(0, -2) };
            var r = rb.Slide(new Vector2(2, dy), .5f, m);
            ProbeLog.Value("position", r.position); ProbeLog.Value("surface", r.surfaceHit.collider != null);
        }
        foreach (int mask in new[] { 1, 256, 257 })
        {
            Begin("joint-layers/" + mask);
            var other = Body(Vector2.zero); other.linearVelocity = new Vector2(0, 2);
            var owner = Body(new Vector2(2, 0)); owner.gameObject.layer = 8;
            var joint = owner.gameObject.AddComponent<DistanceJoint2D>(); joint.connectedBody = other;
            joint.autoConfigureConnectedAnchor = false; joint.anchor = joint.connectedAnchor = Vector2.zero;
            joint.autoConfigureDistance = false; joint.distance = 2;
            // Initialize the joint's reaction state before querying it on omitted layers.
            other.linearVelocity = Vector2.zero; Physics2D.Simulate(.02f); other.linearVelocity = new Vector2(0, 2);
            for (int i = 0; i < 8; i++)
            {
                Physics2D.Simulate(.02f, mask); Record(i + ".owner", owner); Record(i + ".other", other);
                ProbeLog.Value(i + ".force", joint.reactionForce);
            }
        }
        foreach (int mask in new[] { 1, 256, 257 })
        {
            Begin("joint-owner-moving/" + mask);
            var other = Body(Vector2.zero);
            var owner = Body(new Vector2(2, 0)); owner.gameObject.layer = 8; owner.linearVelocity = Vector2.right;
            var joint = owner.gameObject.AddComponent<DistanceJoint2D>(); joint.connectedBody = other;
            joint.autoConfigureConnectedAnchor = false; joint.anchor = joint.connectedAnchor = Vector2.zero;
            joint.autoConfigureDistance = false; joint.distance = 2;
            owner.linearVelocity = Vector2.zero; Physics2D.Simulate(.02f); owner.linearVelocity = Vector2.right;
            for (int i = 0; i < 5; i++)
            {
                Physics2D.Simulate(.02f, mask); Record(i + ".owner", owner); Record(i + ".other", other);
                ProbeLog.Value(i + ".force", joint.reactionForce);
            }
        }
        Begin("joint-layer-transition");
        {
            var other = Body(Vector2.zero); other.linearVelocity = new Vector2(0, 2);
            var owner = Body(new Vector2(2, 0)); owner.gameObject.layer = 8;
            var joint = owner.gameObject.AddComponent<DistanceJoint2D>(); joint.connectedBody = other;
            joint.autoConfigureConnectedAnchor = false; joint.anchor = joint.connectedAnchor = Vector2.zero;
            joint.autoConfigureDistance = false; joint.distance = 2;
            // Initialize the joint's reaction state before querying it on omitted layers.
            other.linearVelocity = Vector2.zero; Physics2D.Simulate(.02f); other.linearVelocity = new Vector2(0, 2);
            for (int i = 0; i < 8; i++)
            {
                Physics2D.Simulate(.02f, i >= 3 && i < 6 ? 1 : 257); Record(i + ".owner", owner); Record(i + ".other", other);
                ProbeLog.Value(i + ".force", joint.reactionForce);
            }
        }
        foreach (bool effector in new[] { false, true })
        {
            Begin("held-contact/" + effector);
            var source = Make("volume", Vector2.zero).AddComponent<BoxCollider2D>(); source.size = new Vector2(4, 4); source.isTrigger = true;
            if (effector)
            {
                source.usedByEffector = true; var area = source.gameObject.AddComponent<AreaEffector2D>();
                area.forceAngle = 0; area.forceMagnitude = 10; area.forceVariation = 0; area.drag = area.angularDrag = 0;
            }
            var rb = Body(Vector2.zero); rb.gameObject.layer = 8; rb.GetComponent<Collider2D>().enabled = false;
            var child = Make("child", Vector2.zero); child.transform.SetParent(rb.transform, true); child.AddComponent<BoxCollider2D>();
            var receiver = rb.gameObject.AddComponent<LoopContacts>();
            for (int i = 0; i < 5; i++)
            {
                Physics2D.Simulate(.02f, i < 3 ? 1 : 257); Record(i.ToString(), rb);
                ProbeLog.Value(i + ".enter", receiver.enter); ProbeLog.Value(i + ".stay", receiver.stay);
            }
        }
        Time.captureDeltaTime = .02f; Time.fixedDeltaTime = .02f; Time.timeScale = 1f;
        new GameObject("loop-controls").AddComponent<LoopControls>().output = output;
    }
    sealed class LoopContacts : MonoBehaviour
    {
        public int enter, stay, exit;
        void OnTriggerEnter2D(Collider2D other) { enter++; }
        void OnTriggerStay2D(Collider2D other) { stay++; }
        void OnTriggerExit2D(Collider2D other) { exit++; }
    }
    sealed class LoopControls : MonoBehaviour
    {
        public string output;
        int warmup = 2, scenario = -1, frame, fixedCalls, updateCalls;
        Rigidbody2D rb;
        LoopContacts contacts;
        void FixedUpdate() { fixedCalls++; }
        void Update()
        {
            updateCalls++;
            if (scenario >= 0 && Physics2D.simulationMode == SimulationMode2D.Script)
                Physics2D.defaultPhysicsScene.Simulate(.02f);
        }
        void LateUpdate()
        {
            if (warmup-- > 0) return;
            if (scenario >= 0)
            {
                string key = frame.ToString(); Record(key, rb);
                ProbeLog.Value(key + ".fixed", fixedCalls); ProbeLog.Value(key + ".update", updateCalls);
                ProbeLog.Value(key + ".enter", contacts.enter); ProbeLog.Value(key + ".stay", contacts.stay); ProbeLog.Value(key + ".exit", contacts.exit);
                ProbeLog.Value(key + ".count", Physics2D.defaultPhysicsScene.subStepCount);
                ProbeLog.Value(key + ".lost", Physics2D.defaultPhysicsScene.subStepLostTime);
                frame++;
                if (frame < 6) { fixedCalls = updateCalls = 0; return; }
            }
            scenario++;
            if (scenario == 12)
            {
                File.WriteAllText(output, ProbeLog.Json()); Debug.Log("advanced 2D controls complete");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#else
                Application.Quit();
#endif
                return;
            }
            var mode = (SimulationMode2D)(scenario / 4); bool sub = scenario % 4 >= 2, refresh = scenario % 2 == 1;
            Begin("loop/" + mode + "/" + sub + "/" + refresh);
            Physics2D.simulationMode = mode; Physics2D.useSubStepping = sub; Physics2D.useSubStepContacts = refresh;
            Physics2D.minSubStepFPS = 200; Physics2D.maxSubStepCount = 4;
            var sensor = Make("sensor", new Vector2(.5f, 0)).AddComponent<BoxCollider2D>(); sensor.size = new Vector2(.1f, 2); sensor.isTrigger = true;
            rb = Body(Vector2.zero); rb.linearVelocity = new Vector2(10, 0); contacts = rb.gameObject.AddComponent<LoopContacts>();
            frame = fixedCalls = updateCalls = 0;
        }

    }
}
