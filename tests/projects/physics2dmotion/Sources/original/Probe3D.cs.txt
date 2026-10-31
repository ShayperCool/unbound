using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>3D physics scenarios: bodies stepped with <c>Physics.Simulate</c>, and queries against
/// a fixed layout of colliders.</summary>
public static class Probe3D
{
    public const float Dt = 0.02f;
    static readonly List<GameObject> s_Objects = new List<GameObject>();

    public static void RunAll()
    {
        Physics.simulationMode = SimulationMode.Script;
        Scenario("3d.fall", Fall);
        Scenario("3d.damping", Damping);
        Scenario("3d.forces", Forces);
        Scenario("3d.mass", Mass);
        Scenario("3d.bounce", Bounce);
        Scenario("3d.slide", Slide);
        Scenario("3d.stack", Stack);
        Scenario("3d.kinematic", Kinematic);
        Scenario("3d.triggers", Triggers);
        Scenario("3d.hinge", Hinge);
        Scenario("3d.spring", Spring);
        Scenario("3d.queries", Queries);
        Scenario("3d.penetration", Penetration);
    }

    static void Scenario(string name, Action body)
    {
        ProbeLog.Begin(name);
        ContactRecorder.Step = 0;
        var gravity = Physics.gravity;
        try
        {
            body();
        }
        catch (Exception e)
        {
            ProbeLog.Error(e);
        }
        Physics.gravity = gravity;
        foreach (var o in s_Objects) if (o) UnityEngine.Object.DestroyImmediate(o);
        s_Objects.Clear();
    }

    static GameObject Object(string name, Vector3 position, Vector3 euler = default, Vector3? scale = null)
    {
        var o = new GameObject(name);
        o.transform.position = position;
        o.transform.rotation = Quaternion.Euler(euler);
        if (scale.HasValue) o.transform.localScale = scale.Value;
        s_Objects.Add(o);
        return o;
    }

    static void Step(int count, Action<int> each = null)
    {
        for (int i = 0; i < count; i++)
        {
            ContactRecorder.Step++;
            Physics.Simulate(Dt);
            each?.Invoke(i + 1);
        }
    }

    static void State(string key, Rigidbody b)
    {
        ProbeLog.Value(key + ".position", b.position);
        ProbeLog.Value(key + ".rotation", b.rotation);
        ProbeLog.Value(key + ".velocity", b.linearVelocity);
        ProbeLog.Value(key + ".angularVelocity", b.angularVelocity);
    }

    static Rigidbody Sphere(string name, Vector3 position, float radius = 0.5f, PhysicsMaterial material = null)
    {
        var o = Object(name, position);
        var c = o.AddComponent<SphereCollider>();
        c.radius = radius;
        c.sharedMaterial = material;
        return o.AddComponent<Rigidbody>();
    }

    static void Ground(float angle = 0, PhysicsMaterial material = null)
    {
        var g = Object("ground", new Vector3(0, -0.5f, 0), new Vector3(0, 0, angle));
        var c = g.AddComponent<BoxCollider>();
        c.size = new Vector3(30, 1, 30);
        c.sharedMaterial = material;
    }

    static void Fall()
    {
        var b = Sphere("ball", new Vector3(0, 10, 0));
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void Damping()
    {
        var o = Object("box", Vector3.zero);
        o.AddComponent<BoxCollider>();
        var b = o.AddComponent<Rigidbody>();
        b.useGravity = false;
        b.linearDamping = 0.5f;
        b.angularDamping = 0.8f;
        b.linearVelocity = new Vector3(5, 3, -1);
        b.angularVelocity = new Vector3(1, 2, 3);
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void Forces()
    {
        var modes = new[] { ForceMode.Force, ForceMode.Acceleration, ForceMode.Impulse, ForceMode.VelocityChange };
        var bodies = new List<Rigidbody>();
        for (int m = 0; m < modes.Length; m++)
        {
            var o = Object(modes[m].ToString(), new Vector3(m * 3, 0, 0));
            o.AddComponent<BoxCollider>();
            var b = o.AddComponent<Rigidbody>();
            b.useGravity = false;
            b.mass = 2;
            bodies.Add(b);
        }
        var rel = Object("relative", new Vector3(0, 5, 0), new Vector3(0, 0, 30)).AddComponent<Rigidbody>();
        rel.gameObject.AddComponent<BoxCollider>();
        rel.useGravity = false;
        var atPos = Object("atPosition", new Vector3(0, 10, 0)).AddComponent<Rigidbody>();
        atPos.gameObject.AddComponent<BoxCollider>().size = new Vector3(2, 1, 1);
        atPos.useGravity = false;
        Step(25, i =>
        {
            for (int m = 0; m < modes.Length; m++)
            {
                bodies[m].AddForce(new Vector3(1, 0.5f, 0), modes[m]);
                bodies[m].AddTorque(new Vector3(0, 0.3f, 0.1f), modes[m]);
            }
            rel.AddRelativeForce(new Vector3(4, 0, 0));
            atPos.AddForceAtPosition(new Vector3(0, 3, 0), atPos.position + new Vector3(1, 0, 0));
            if (i % 5 == 0)
            {
                foreach (var b in bodies) State(b.name + ".s" + i, b);
                State("relative.s" + i, rel);
                State("atPosition.s" + i, atPos);
            }
        });
    }

    static void Mass()
    {
        var box = Object("box", Vector3.zero, Vector3.zero, new Vector3(2, 1, 1));
        box.AddComponent<BoxCollider>().size = new Vector3(1, 2, 3);
        var b = box.AddComponent<Rigidbody>();
        b.mass = 4;
        var compound = Object("compound", new Vector3(5, 0, 0));
        compound.AddComponent<BoxCollider>();
        var s = compound.AddComponent<SphereCollider>();
        s.center = new Vector3(1, 0, 0);
        s.radius = 0.6f;
        var c = compound.AddComponent<Rigidbody>();
        var capsule = Object("capsule", new Vector3(10, 0, 0));
        var cc = capsule.AddComponent<CapsuleCollider>();
        cc.height = 3;
        cc.radius = 0.5f;
        cc.direction = 0;
        var p = capsule.AddComponent<Rigidbody>();
        Physics.SyncTransforms();
        foreach (var r in new[] { b, c, p })
        {
            ProbeLog.Value(r.name + ".mass", r.mass);
            ProbeLog.Value(r.name + ".inertiaTensor", r.inertiaTensor);
            ProbeLog.Value(r.name + ".inertiaTensorRotation", r.inertiaTensorRotation);
            ProbeLog.Value(r.name + ".centerOfMass", r.centerOfMass);
            ProbeLog.Value(r.name + ".worldCenterOfMass", r.worldCenterOfMass);
        }
    }

    static void Bounce()
    {
        Ground();
        var material = new PhysicsMaterial("bouncy")
        {
            bounciness = 0.8f,
            dynamicFriction = 0.4f,
            staticFriction = 0.5f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
        };
        var b = Sphere("ball", new Vector3(0, 5, 0), 0.5f, material);
        b.linearVelocity = new Vector3(1, 0, 0.5f);
        b.gameObject.AddComponent<ContactRecorder>();
        Step(200, i => { if (i % 5 == 0) State("s" + i, b); });
    }

    static void Slide()
    {
        Ground(-20, new PhysicsMaterial { dynamicFriction = 0.2f, staticFriction = 0.2f });
        var o = Object("box", new Vector3(-3, 1.5f, 0), new Vector3(0, 0, -20));
        o.AddComponent<BoxCollider>().sharedMaterial = new PhysicsMaterial { dynamicFriction = 0.6f, staticFriction = 0.6f };
        var b = o.AddComponent<Rigidbody>();
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
        ProbeLog.Value("sleeping", b.IsSleeping());
    }

    static void Stack()
    {
        Ground();
        var bodies = new List<Rigidbody>();
        for (int i = 0; i < 4; i++)
        {
            var o = Object("box" + i, new Vector3(i * 0.1f, 0.5f + i * 1.01f, 0));
            o.AddComponent<BoxCollider>();
            bodies.Add(o.AddComponent<Rigidbody>());
        }
        Step(300, i => { if (i % 50 == 0) foreach (var b in bodies) State(b.name + ".s" + i, b); });
        foreach (var b in bodies) ProbeLog.Value(b.name + ".sleeping", b.IsSleeping());
    }

    static void Kinematic()
    {
        Ground();
        var k = Object("paddle", new Vector3(-4, 0.5f, 0)).AddComponent<Rigidbody>();
        k.gameObject.AddComponent<BoxCollider>();
        k.isKinematic = true;
        var d = Sphere("ball", new Vector3(0, 0.5f, 0));
        d.gameObject.AddComponent<ContactRecorder>();
        Step(100, i =>
        {
            k.MovePosition(k.position + new Vector3(0.1f, 0, 0));
            k.MoveRotation(k.rotation * Quaternion.Euler(0, 1, 0));
            if (i % 10 == 0)
            {
                State("paddle.s" + i, k);
                State("ball.s" + i, d);
            }
        });
    }

    static void Triggers()
    {
        var zone = Object("zone", Vector3.zero);
        var t = zone.AddComponent<BoxCollider>();
        t.size = new Vector3(2, 2, 2);
        t.isTrigger = true;
        var b = Sphere("ball", new Vector3(0, 5, 0), 0.25f);
        b.gameObject.AddComponent<ContactRecorder>();
        Step(120);
    }

    static void Hinge()
    {
        var b = Sphere("bob", new Vector3(3, 5, 0), 0.25f);
        var h = b.gameObject.AddComponent<HingeJoint>();
        h.autoConfigureConnectedAnchor = false;
        h.anchor = new Vector3(-3, 0, 0);
        h.connectedAnchor = new Vector3(0, 5, 0);
        h.axis = Vector3.forward;
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void Spring()
    {
        var b = Sphere("bob", new Vector3(0, 3, 0), 0.25f);
        var s = b.gameObject.AddComponent<SpringJoint>();
        s.autoConfigureConnectedAnchor = false;
        s.connectedAnchor = new Vector3(0, 5, 0);
        s.spring = 20;
        s.damper = 0.5f;
        s.minDistance = 0;
        s.maxDistance = 1;
        Step(100, i => { if (i % 10 == 0) State("s" + i, b); });
    }

    static void Layout()
    {
        Object("box", new Vector3(0, 0, 0), new Vector3(0, 20, 10)).AddComponent<BoxCollider>().size = new Vector3(2, 1, 1.5f);
        var sphere = Object("sphere", new Vector3(4, 0, 0)).AddComponent<SphereCollider>();
        sphere.radius = 0.75f;
        sphere.center = new Vector3(0.2f, 0.1f, 0);
        var cap = Object("capsule", new Vector3(-4, 0, 0)).AddComponent<CapsuleCollider>();
        cap.height = 3;
        cap.radius = 0.5f;
        var capX = Object("capsuleX", new Vector3(0, 4, 0), new Vector3(0, 0, -15)).AddComponent<CapsuleCollider>();
        capX.height = 3;
        capX.radius = 0.5f;
        capX.direction = 0;
        var trig = Object("trigger", new Vector3(-4, 4, 0)).AddComponent<BoxCollider>();
        trig.isTrigger = true;
        Object("scaledBox", new Vector3(8, 0, 0), new Vector3(0, 0, 45), new Vector3(1.5f, 0.5f, 1)).AddComponent<BoxCollider>();
        Physics.SyncTransforms();
    }

    static void Hit(string key, bool hit, RaycastHit h)
    {
        ProbeLog.Value(key + ".collider", hit && h.collider ? h.collider.name : "none");
        if (!hit) return;
        ProbeLog.Value(key + ".point", h.point);
        ProbeLog.Value(key + ".normal", h.normal);
        ProbeLog.Value(key + ".distance", h.distance);
    }

    static readonly RaycastHit[] s_Hits = new RaycastHit[16];
    static readonly Collider[] s_Colliders = new Collider[16];

    static void Queries()
    {
        Layout();
        foreach (var name in new[] { "box", "sphere", "capsule", "capsuleX", "trigger", "scaledBox" })
        {
            var c = GameObject.Find(name).GetComponent<Collider>();
            ProbeLog.Value(name + ".bounds.center", c.bounds.center);
            ProbeLog.Value(name + ".bounds.size", c.bounds.size);
            ProbeLog.Value(name + ".closest", c.ClosestPoint(new Vector3(1, 1, 0.5f)));
        }
        var rays = new List<(string, Vector3, Vector3)>();
        for (float y = -4.5f; y <= 4.5f; y += 0.5f) rays.Add(("h" + y, new Vector3(-10, y, 0.2f), Vector3.right));
        for (int a = 0; a < 360; a += 30)
        {
            var d = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad), 0.1f).normalized;
            rays.Add(("d" + a, new Vector3(2, 2, 0) - d * 8, d));
        }
        foreach (var (key, origin, dir) in rays)
        {
            Hit("ray." + key, Physics.Raycast(origin, dir, out var h, 30), h);
            Hit("sphere." + key, Physics.SphereCast(origin, 0.3f, dir, out h, 30), h);
            Hit("capsule." + key, Physics.CapsuleCast(origin, origin + Vector3.up * 0.8f, 0.25f, dir, out h, 30), h);
            Hit("box." + key, Physics.BoxCast(origin, new Vector3(0.25f, 0.4f, 0.25f), dir, out h, Quaternion.Euler(0, 0, 10), 30), h);
            int n = Physics.RaycastNonAlloc(origin, dir, s_Hits, 30);
            var names = new List<string>();
            for (int i = 0; i < n; i++) names.Add(s_Hits[i].collider.name + "@" + s_Hits[i].distance.ToString("F4"));
            names.Sort(StringComparer.Ordinal);
            ProbeLog.Value("rayAll." + key, string.Join(",", names));
        }
        for (float x = -6; x <= 9; x += 1.5f)
            for (float y = -5; y <= 5; y += 1.5f)
            {
                var p = new Vector3(x, y, 0);
                ProbeLog.Value("overlapSphere." + x + "," + y, Names(Physics.OverlapSphereNonAlloc(p, 0.8f, s_Colliders)));
                ProbeLog.Value("overlapBox." + x + "," + y, Names(Physics.OverlapBoxNonAlloc(p, new Vector3(0.6f, 0.3f, 0.5f), s_Colliders, Quaternion.Euler(0, 0, 25))));
            }
    }

    static string Names(int count)
    {
        var names = new List<string>();
        for (int i = 0; i < count; i++) names.Add(s_Colliders[i].name);
        names.Sort(StringComparer.Ordinal);
        return string.Join(",", names);
    }

    static void Penetration()
    {
        Layout();
        var probe = Object("probe", Vector3.zero).AddComponent<BoxCollider>();
        foreach (var name in new[] { "box", "sphere", "capsule", "scaledBox" })
        {
            var other = GameObject.Find(name).GetComponent<Collider>();
            var at = other.transform.position + new Vector3(0.4f, 0.3f, 0);
            bool overlap = Physics.ComputePenetration(probe, at, Quaternion.identity, other, other.transform.position, other.transform.rotation, out var dir, out var dist);
            ProbeLog.Value(name + ".overlap", overlap);
            if (overlap)
            {
                ProbeLog.Value(name + ".direction", dir);
                ProbeLog.Value(name + ".distance", dist);
            }
        }
    }
}
