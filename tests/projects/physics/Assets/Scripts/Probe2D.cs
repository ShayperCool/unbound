using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>2D physics scenarios: bodies stepped with <c>Physics2D.Simulate</c>, and queries
/// against a fixed layout of every collider kind.</summary>
public static class Probe2D
{
    public const float Dt = 0.02f;
    static readonly List<GameObject> s_Objects = new List<GameObject>();

    public static void RunAll()
    {
        Physics2D.simulationMode = SimulationMode2D.Script;
        Scenario("2d.fall", Fall);
        Scenario("2d.damping", Damping);
        Scenario("2d.gravity", Gravity);
        Scenario("2d.forces", Forces);
        Scenario("2d.mass", Mass);
        Scenario("2d.bounce", Bounce);
        Scenario("2d.slide", Slide);
        Scenario("2d.stack", Stack);
        Scenario("2d.kinematic", Kinematic);
        Scenario("2d.triggers", Triggers);
        Scenario("2d.hinge", Hinge);
        Scenario("2d.spring", Spring);
        Scenario("2d.distance", DistanceJoint);
        Scenario("2d.queries", Queries);
        Scenario("2d.queries.options", QueryOptions);
        Scenario("2d.casts.inside", CastsFromInside);
    }

    static void Scenario(string name, Action body)
    {
        ProbeLog.Begin(name);
        ContactRecorder.Step = 0;
        var gravity = Physics2D.gravity;
        try
        {
            body();
        }
        catch (Exception e)
        {
            ProbeLog.Error(e);
        }
        Physics2D.gravity = gravity;
        Physics2D.queriesHitTriggers = true;
        Physics2D.queriesStartInColliders = true;
        foreach (var o in s_Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        s_Objects.Clear();
    }

    static GameObject Object(string name, Vector2 position, float angle = 0f, Vector2? scale = null)
    {
        var o = new GameObject(name);
        o.transform.position = position;
        o.transform.rotation = Quaternion.Euler(0, 0, angle);
        if (scale.HasValue) o.transform.localScale = new Vector3(scale.Value.x, scale.Value.y, 1);
        s_Objects.Add(o);
        return o;
    }

    static Rigidbody2D Body(GameObject o, RigidbodyType2D type = RigidbodyType2D.Dynamic)
    {
        var b = o.AddComponent<Rigidbody2D>();
        b.bodyType = type;
        return b;
    }

    static void Step(int count, Action<int> each = null)
    {
        for (int i = 0; i < count; i++)
        {
            ContactRecorder.Step++;
            Physics2D.Simulate(Dt);
            each?.Invoke(i + 1);
        }
    }

    static void State(string key, Rigidbody2D b)
    {
        ProbeLog.Value(key + ".position", b.position);
        ProbeLog.Value(key + ".rotation", b.rotation);
        ProbeLog.Value(key + ".velocity", b.linearVelocity);
        ProbeLog.Value(key + ".angularVelocity", b.angularVelocity);
    }

    static void Fall()
    {
        var o = Object("ball", new Vector2(0, 10));
        o.AddComponent<CircleCollider2D>().radius = 0.5f;
        var b = Body(o);
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
        ProbeLog.Value("transform", o.transform.position);
    }

    static void Damping()
    {
        var o = Object("box", Vector2.zero);
        o.AddComponent<BoxCollider2D>();
        var b = Body(o);
        b.gravityScale = 0;
        b.linearDamping = 0.5f;
        b.angularDamping = 0.8f;
        b.linearVelocity = new Vector2(5, 3);
        b.angularVelocity = 180;
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void Gravity()
    {
        Physics2D.gravity = new Vector2(1, -20);
        var o = Object("ball", Vector2.zero);
        o.AddComponent<CircleCollider2D>();
        var b = Body(o);
        b.gravityScale = 2.5f;
        b.mass = 3;
        Step(50, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void Forces()
    {
        var a = Body(Object("force", new Vector2(0, 0)));
        a.gameObject.AddComponent<BoxCollider2D>();
        var c = Body(Object("impulse", new Vector2(0, 5)));
        c.gameObject.AddComponent<BoxCollider2D>();
        var d = Body(Object("atPosition", new Vector2(0, 10)));
        d.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(2, 1);
        var e = Body(Object("relative", new Vector2(0, 15), 30));
        e.gameObject.AddComponent<BoxCollider2D>();
        foreach (var b in new[] { a, c, d, e }) b.gravityScale = 0;
        c.AddForce(new Vector2(3, 1), ForceMode2D.Impulse);
        c.AddTorque(2, ForceMode2D.Impulse);
        Step(25, i =>
        {
            a.AddForce(new Vector2(10, 0));
            a.AddTorque(5);
            d.AddForceAtPosition(new Vector2(0, 4), d.position + new Vector2(1, 0));
            e.AddRelativeForce(new Vector2(6, 0));
            if (i % 5 == 0)
                foreach (var b in new[] { a, c, d, e }) State(b.name + ".s" + i, b);
        });
    }

    static void Mass()
    {
        var box = Object("box", Vector2.zero, 0, new Vector2(2, 1));
        var collider = box.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(1, 2);
        collider.density = 3;
        var b = Body(box);
        b.useAutoMass = true;
        var circle = Object("circle", new Vector2(5, 0));
        var cc = circle.AddComponent<CircleCollider2D>();
        cc.radius = 0.7f;
        cc.offset = new Vector2(0.5f, 0.25f);
        var c = Body(circle);
        var capsule = Object("capsule", new Vector2(10, 0));
        var cap = capsule.AddComponent<CapsuleCollider2D>();
        cap.size = new Vector2(1, 3);
        var p = Body(capsule);
        p.useAutoMass = true;
        Physics2D.SyncTransforms();
        foreach (var r in new[] { b, c, p })
        {
            ProbeLog.Value(r.name + ".mass", r.mass);
            ProbeLog.Value(r.name + ".inertia", r.inertia);
            ProbeLog.Value(r.name + ".centerOfMass", r.centerOfMass);
            ProbeLog.Value(r.name + ".worldCenterOfMass", r.worldCenterOfMass);
        }
    }

    static GameObject Ground(float width = 20, float angle = 0, PhysicsMaterial2D material = null)
    {
        var g = Object("ground", new Vector2(0, -0.5f), angle);
        var c = g.AddComponent<BoxCollider2D>();
        c.size = new Vector2(width, 1);
        c.sharedMaterial = material;
        return g;
    }

    static void Bounce()
    {
        Ground();
        var material = new PhysicsMaterial2D("bouncy") { bounciness = 0.8f, friction = 0.4f };
        var o = Object("ball", new Vector2(0, 5));
        var c = o.AddComponent<CircleCollider2D>();
        c.radius = 0.5f;
        c.sharedMaterial = material;
        var b = Body(o);
        b.linearVelocity = new Vector2(1, 0);
        o.AddComponent<ContactRecorder>();
        Step(200, i => { if (i % 5 == 0) State("s" + i, b); });
    }

    static void Slide()
    {
        Ground(30, -20, new PhysicsMaterial2D { friction = 0.2f });
        var o = Object("box", new Vector2(-3, 1.5f), -20);
        o.AddComponent<BoxCollider2D>().sharedMaterial = new PhysicsMaterial2D { friction = 0.6f };
        var b = Body(o);
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
        ProbeLog.Value("sleeping", b.IsSleeping());
    }

    static void Stack()
    {
        Ground();
        var bodies = new List<Rigidbody2D>();
        for (int i = 0; i < 4; i++)
        {
            var o = Object("box" + i, new Vector2(i * 0.1f, 0.5f + i * 1.01f));
            o.AddComponent<BoxCollider2D>();
            bodies.Add(Body(o));
        }
        Step(300, i => { if (i % 50 == 0) foreach (var b in bodies) State(b.name + ".s" + i, b); });
        foreach (var b in bodies) ProbeLog.Value(b.name + ".sleeping", b.IsSleeping());
    }

    static void Kinematic()
    {
        Ground();
        var k = Body(Object("paddle", new Vector2(-4, 0.5f)), RigidbodyType2D.Kinematic);
        k.gameObject.AddComponent<BoxCollider2D>().size = new Vector2(1, 1);
        var d = Body(Object("ball", new Vector2(0, 0.5f)));
        d.gameObject.AddComponent<CircleCollider2D>().radius = 0.5f;
        d.gameObject.AddComponent<ContactRecorder>();
        Step(100, i =>
        {
            k.MovePosition(k.position + new Vector2(0.1f, 0));
            k.MoveRotation(k.rotation + 1);
            if (i % 10 == 0)
            {
                State("paddle.s" + i, k);
                State("ball.s" + i, d);
            }
        });
    }

    static void Triggers()
    {
        var zone = Object("zone", new Vector2(0, 0));
        var t = zone.AddComponent<BoxCollider2D>();
        t.size = new Vector2(2, 2);
        t.isTrigger = true;
        var o = Object("ball", new Vector2(0, 5));
        o.AddComponent<CircleCollider2D>().radius = 0.25f;
        Body(o);
        o.AddComponent<ContactRecorder>();
        Step(120);
    }

    static void Hinge()
    {
        var anchor = Object("anchor", new Vector2(0, 5));
        var o = Object("bob", new Vector2(3, 5));
        o.AddComponent<CircleCollider2D>().radius = 0.25f;
        var b = Body(o);
        var h = o.AddComponent<HingeJoint2D>();
        h.connectedBody = null;
        h.autoConfigureConnectedAnchor = false;
        h.anchor = new Vector2(-3, 0);
        h.connectedAnchor = anchor.transform.position;
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void Spring()
    {
        var o = Object("bob", new Vector2(0, 3));
        o.AddComponent<CircleCollider2D>().radius = 0.25f;
        var b = Body(o);
        var s = o.AddComponent<SpringJoint2D>();
        s.autoConfigureConnectedAnchor = false;
        s.autoConfigureDistance = false;
        s.connectedAnchor = new Vector2(0, 5);
        s.distance = 1;
        s.frequency = 2;
        s.dampingRatio = 0.1f;
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void DistanceJoint()
    {
        var o = Object("bob", new Vector2(2, 5));
        o.AddComponent<CircleCollider2D>().radius = 0.25f;
        var b = Body(o);
        b.linearVelocity = new Vector2(0, 3);
        var s = o.AddComponent<DistanceJoint2D>();
        s.autoConfigureConnectedAnchor = false;
        s.autoConfigureDistance = false;
        s.connectedAnchor = new Vector2(0, 5);
        s.distance = 2;
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    // ------------------------------------------------------------------ queries

    static void Layout()
    {
        var box = Object("box", new Vector2(0, 0), 20);
        box.AddComponent<BoxCollider2D>().size = new Vector2(2, 1);
        var circle = Object("circle", new Vector2(4, 0));
        var c = circle.AddComponent<CircleCollider2D>();
        c.radius = 0.75f;
        c.offset = new Vector2(0.2f, 0.1f);
        var capV = Object("capsuleV", new Vector2(-4, 0));
        capV.AddComponent<CapsuleCollider2D>().size = new Vector2(1, 3);
        var capH = Object("capsuleH", new Vector2(0, 4), -15);
        var ch = capH.AddComponent<CapsuleCollider2D>();
        ch.size = new Vector2(3, 1);
        ch.direction = CapsuleDirection2D.Horizontal;
        var poly = Object("polygon", new Vector2(4, 4), 10, new Vector2(1.5f, 0.75f));
        poly.AddComponent<PolygonCollider2D>().points = new[]
        {
            new Vector2(0, 1), new Vector2(0.95f, 0.31f), new Vector2(0.59f, -0.81f), new Vector2(-0.59f, -0.81f), new Vector2(-0.95f, 0.31f),
        };
        var edge = Object("edge", new Vector2(0, -4));
        edge.AddComponent<EdgeCollider2D>().points = new[] { new Vector2(-5, 0), new Vector2(-1, 0.5f), new Vector2(2, -0.5f), new Vector2(5, 0) };
        var trigger = Object("trigger", new Vector2(-4, 4));
        var t = trigger.AddComponent<BoxCollider2D>();
        t.size = new Vector2(1.5f, 1.5f);
        t.isTrigger = true;
        var scaled = Object("scaledBox", new Vector2(8, 0), 45, new Vector2(1.5f, 0.5f));
        scaled.AddComponent<BoxCollider2D>().edgeRadius = 0.1f;
        var bodyBox = Object("bodyBox", new Vector2(8, 4));
        bodyBox.AddComponent<BoxCollider2D>();
        Body(bodyBox, RigidbodyType2D.Kinematic);
        Physics2D.SyncTransforms();
    }

    static void Hit(string key, RaycastHit2D h)
    {
        ProbeLog.Value(key + ".collider", h.collider ? h.collider.name : "none");
        if (!h.collider) return;
        ProbeLog.Value(key + ".point", h.point);
        ProbeLog.Value(key + ".normal", h.normal);
        ProbeLog.Value(key + ".centroid", h.centroid);
        ProbeLog.Value(key + ".distance", h.distance);
        ProbeLog.Value(key + ".fraction", h.fraction);
    }

    static void Hits(string key, RaycastHit2D[] hits, int count)
    {
        ProbeLog.Value(key + ".count", count);
        for (int i = 0; i < count; i++) Hit(key + "." + i, hits[i]);
    }

    static readonly RaycastHit2D[] s_Hits = new RaycastHit2D[16];
    static readonly Collider2D[] s_Colliders = new Collider2D[16];

    static IEnumerable<(string, Vector2, Vector2)> Rays()
    {
        for (float y = -4.5f; y <= 4.5f; y += 0.5f) yield return ("h" + y, new Vector2(-10, y), Vector2.right);
        for (float x = -5f; x <= 9f; x += 1f) yield return ("v" + x, new Vector2(x, 10), Vector2.down);
        for (int a = 0; a < 360; a += 30)
        {
            var d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
            yield return ("d" + a, new Vector2(2, 2) - d * 8, d);
        }
    }

    static void Queries()
    {
        Layout();
        foreach (var name in new[] { "box", "circle", "capsuleV", "capsuleH", "polygon", "edge", "trigger", "scaledBox" })
        {
            var c = GameObject.Find(name).GetComponent<Collider2D>();
            ProbeLog.Value(name + ".bounds.center", c.bounds.center);
            ProbeLog.Value(name + ".bounds.size", c.bounds.size);
            ProbeLog.Value(name + ".closest", c.ClosestPoint(new Vector2(1, 1)));
        }
        foreach (var (key, origin, dir) in Rays())
        {
            Hit("ray." + key, Physics2D.Raycast(origin, dir, 30));
            Hits("rayAll." + key, s_Hits, Physics2D.RaycastNonAlloc(origin, dir, s_Hits, 30));
            Hit("circle." + key, Physics2D.CircleCast(origin, 0.3f, dir, 30));
            Hit("box." + key, Physics2D.BoxCast(origin, new Vector2(0.5f, 0.8f), 10, dir, 30));
            Hits("capsuleV." + key, s_Hits, Physics2D.CapsuleCastNonAlloc(origin, new Vector2(0.5f, 1.2f), CapsuleDirection2D.Vertical, 0, dir, s_Hits, 30));
            Hits("capsuleH." + key, s_Hits, Physics2D.CapsuleCastNonAlloc(origin, new Vector2(1.6f, 0.6f), CapsuleDirection2D.Horizontal, 30, dir, s_Hits, 30));
            Hit("line." + key, Physics2D.Linecast(origin, origin + dir * 12));
        }
        for (float x = -6; x <= 9; x += 0.75f)
            for (float y = -5; y <= 5; y += 0.75f)
            {
                var p = new Vector2(x, y);
                var hit = Physics2D.OverlapPoint(p);
                if (hit) ProbeLog.Value("point." + x + "," + y, hit.name);
            }
        for (float x = -6; x <= 9; x += 1.5f)
            for (float y = -5; y <= 5; y += 1.5f)
            {
                var p = new Vector2(x, y);
                int n = Physics2D.OverlapCircleNonAlloc(p, 0.8f, s_Colliders);
                ProbeLog.Value("overlapCircle." + x + "," + y, Names(n));
                n = Physics2D.OverlapBoxNonAlloc(p, new Vector2(1.2f, 0.6f), 25, s_Colliders);
                ProbeLog.Value("overlapBox." + x + "," + y, Names(n));
                n = Physics2D.OverlapCapsuleNonAlloc(p, new Vector2(0.6f, 1.4f), CapsuleDirection2D.Vertical, 0, s_Colliders);
                ProbeLog.Value("overlapCapsule." + x + "," + y, Names(n));
            }
        var all = new[] { "box", "circle", "capsuleV", "capsuleH", "polygon", "edge", "scaledBox" };
        for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
            {
                var d = Physics2D.Distance(GameObject.Find(all[i]).GetComponent<Collider2D>(), GameObject.Find(all[j]).GetComponent<Collider2D>());
                string k = "distance." + all[i] + "." + all[j];
                ProbeLog.Value(k + ".distance", d.distance);
                ProbeLog.Value(k + ".pointA", d.pointA);
                ProbeLog.Value(k + ".pointB", d.pointB);
                ProbeLog.Value(k + ".normal", d.normal);
            }
    }

    static string Names(int count)
    {
        var names = new List<string>();
        for (int i = 0; i < count; i++) names.Add(s_Colliders[i].name);
        names.Sort(StringComparer.Ordinal);
        return string.Join(",", names);
    }

    static void QueryOptions()
    {
        Layout();
        Physics2D.queriesHitTriggers = false;
        foreach (var (key, origin, dir) in Rays()) Hit("noTriggers." + key, Physics2D.Raycast(origin, dir, 30));
        Physics2D.queriesHitTriggers = true;
        var poly = GameObject.Find("polygon");
        poly.layer = 8;
        foreach (var (key, origin, dir) in Rays())
            Hit("mask." + key, Physics2D.CircleCast(origin, 0.3f, dir, 30, ~(1 << 8)));
        foreach (var (key, origin, dir) in Rays())
            Hit("depth." + key, Physics2D.Raycast(origin, dir, 30, -1, -1, 1));
    }

    static void CastsFromInside()
    {
        Layout();
        var origins = new[] { new Vector2(0, 0), new Vector2(0.9f, 0.2f), new Vector2(4, 0), new Vector2(-4, 1.2f), new Vector2(4, 4) };
        var dirs = new[] { Vector2.right, Vector2.up, new Vector2(-0.6f, -0.8f) };
        foreach (var start in new[] { true, false })
        {
            Physics2D.queriesStartInColliders = start;
            for (int i = 0; i < origins.Length; i++)
                for (int j = 0; j < dirs.Length; j++)
                {
                    string k = (start ? "in." : "out.") + i + "." + j;
                    Hits(k + ".ray", s_Hits, Physics2D.RaycastNonAlloc(origins[i], dirs[j], s_Hits, 10));
                    Hits(k + ".circle", s_Hits, Physics2D.CircleCastNonAlloc(origins[i], 0.4f, dirs[j], s_Hits, 10));
                    Hits(k + ".capsule", s_Hits, Physics2D.CapsuleCastNonAlloc(origins[i], new Vector2(0.5f, 1.2f), CapsuleDirection2D.Vertical, 15, dirs[j], s_Hits, 10));
                    Hits(k + ".box", s_Hits, Physics2D.BoxCastNonAlloc(origins[i], new Vector2(0.6f, 0.6f), 0, dirs[j], s_Hits, 10));
                }
        }
    }
}
