using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

/// <summary>Records what Physics.ContactModifyEvent and ContactModifyEventCCD pass to scripts
/// (which pairs, how many per step, the pair and contact data) and how each ModifiableContactPair
/// edit changes the simulation.</summary>
public static class ContactModifyProbe
{
    const float Dt = .02f;
    static readonly StringBuilder records = new StringBuilder();
    static readonly List<GameObject> made = new List<GameObject>();
    static readonly Dictionary<int, string> names = new Dictionary<int, string>();

    // What the handlers do and saw in the current step.
    delegate void Edit(ref ModifiableContactPair pair, int contact);
    static Edit edit, editCcd;
    static Action<ModifiableContactPair> editPair;
    static int calls, pairs, ccdCalls, ccdPairs;
    static readonly List<string> seen = new List<string>();
    static readonly List<ModifiableContactPair> firstPairs = new List<ModifiableContactPair>();
    static bool dump, dumpCcd;
    static string dumpKey;
    static int mainThread;
    static int offThread;
    // Unity may run the handlers on several threads at once.
    static readonly object gate = new object();

    // ------------------------------------------------------------------ records

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Record(string key, params object[] values)
    {
        records.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) records.Append(',');
            switch (values[i])
            {
                case string s: records.Append('"').Append(s).Append('"'); break;
                case float f: records.Append(float.IsFinite(f) ? F(f) : "\"" + F(f) + "\""); break;
                case Vector3 v: records.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)); break;
                case Quaternion q: records.Append(F(q.x)).Append(',').Append(F(q.y)).Append(',').Append(F(q.z)).Append(',').Append(F(q.w)); break;
                case bool b: records.Append(b ? 1 : 0); break;
                default: records.Append(Convert.ToString(values[i], CultureInfo.InvariantCulture)); break;
            }
        }
        records.Append("]}\n");
    }

    static string Name(int id) => id == 0 ? "none" : names.TryGetValue(id, out var n) ? n : "unknown";

    // ------------------------------------------------------------------ handlers

    static void OnModify(PhysicsScene scene, NativeArray<ModifiableContactPair> buffer)
    {
        lock (gate) Modify(buffer);
    }

    static void Modify(NativeArray<ModifiableContactPair> buffer)
    {
        if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread) offThread++;
        calls++;
        for (int p = 0; p < buffer.Length; p++)
        {
            var pair = buffer[p];
            pairs++;
            seen.Add(Name(pair.colliderInstanceID) + "|" + Name(pair.otherColliderInstanceID) + "|" + pair.contactCount);
            if (dump) DumpPair(dumpKey, pair);
            editPair?.Invoke(pair);
            if (edit != null)
                for (int i = 0; i < pair.contactCount; i++) edit(ref pair, i);
        }
        dump = false;
    }

    static void OnModifyCcd(PhysicsScene scene, NativeArray<ModifiableContactPair> buffer)
    {
        lock (gate) ModifyCcd(buffer);
    }

    static void ModifyCcd(NativeArray<ModifiableContactPair> buffer)
    {
        ccdCalls++;
        for (int p = 0; p < buffer.Length; p++)
        {
            var pair = buffer[p];
            ccdPairs++;
            if (dumpCcd) { DumpPair(dumpKey + "/ccd", pair); dumpCcd = false; }
            if (editCcd != null)
                for (int i = 0; i < pair.contactCount; i++) editCcd(ref pair, i);
        }
    }

    static void DumpPair(string key, ModifiableContactPair pair)
    {
        string k = key + "/" + Name(pair.colliderInstanceID) + "-" + Name(pair.otherColliderInstanceID);
        Record(k + "/ids", Name(pair.colliderInstanceID), Name(pair.otherColliderInstanceID),
            Name(pair.bodyInstanceID), Name(pair.otherBodyInstanceID), pair.contactCount);
        Record(k + "/poses", pair.position, pair.rotation, pair.otherPosition, pair.otherRotation);
        Record(k + "/velocities", pair.bodyVelocity, pair.bodyAngularVelocity, pair.otherBodyVelocity, pair.otherBodyAngularVelocity);
        var m = pair.massProperties;
        Record(k + "/mass", m.inverseMassScale, m.inverseInertiaScale, m.otherInverseMassScale, m.otherInverseInertiaScale);
        for (int i = 0; i < pair.contactCount; i++)
            Record(k + "/contact" + i, pair.GetPoint(i), pair.GetNormal(i), pair.GetSeparation(i), pair.GetTargetVelocity(i),
                pair.GetMaxImpulse(i), pair.GetBounciness(i), pair.GetStaticFriction(i), pair.GetDynamicFriction(i),
                (long)pair.GetFaceIndex(i));
    }

    // ------------------------------------------------------------------ scene

    static GameObject Make(string name, PrimitiveType type, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
        go.transform.position = position;
        go.transform.localScale = scale;
        made.Add(go);
        names[go.GetComponent<Collider>().GetInstanceID()] = name;
        return go;
    }

    static Rigidbody Body(GameObject go, bool kinematic = false)
    {
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = kinematic;
        rb.linearDamping = rb.angularDamping = 0;
        names[rb.GetInstanceID()] = go.name + ".body";
        return rb;
    }

    static GameObject Ground(string name = "ground", bool mesh = false)
    {
        var go = Make(name, PrimitiveType.Cube, Vector3.zero, new Vector3(20, 1, 20));
        if (mesh)
        {
            var cube = go.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = cube;
            names[mc.GetInstanceID()] = name;
        }
        var material = new PhysicsMaterial("groundMaterial") { dynamicFriction = .4f, staticFriction = .6f, bounciness = .3f };
        go.GetComponent<Collider>().sharedMaterial = material;
        return go;
    }

    static void Clean()
    {
        foreach (var go in made)
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        made.Clear();
        edit = editCcd = null;
        editPair = null;
        dump = dumpCcd = false;
        Physics.SyncTransforms();
        Physics.Simulate(Dt);
    }

    static void ResetCounts()
    {
        calls = pairs = ccdCalls = ccdPairs = 0;
        seen.Clear();
    }

    // ------------------------------------------------------------------ cases

    /// <summary>Which pairs reach the event, per step, depending on which collider has the flag.</summary>
    static void Pairs()
    {
        string[] cases = { "none", "dynamic", "static", "both", "trigger", "kinematicStatic", "kinematicDynamic", "compound", "mesh", "unflaggedLater", "flaggedLater", "unflaggedLaterQuiet", "flaggedLaterQuiet", "unflaggedLaterWake", "flaggedLaterWake" };
        foreach (var c in cases)
        {
            var ground = Ground(mesh: c == "mesh");
            var box = Make("box", PrimitiveType.Cube, new Vector3(.3f, 1.0f, -.2f), Vector3.one);
            box.transform.rotation = Quaternion.Euler(0, 20, 0);
            var rb = Body(box, kinematic: c == "kinematicStatic");
            box.GetComponent<Collider>().sharedMaterial = new PhysicsMaterial("boxMaterial") { dynamicFriction = .2f, staticFriction = .3f, bounciness = .5f };
            GameObject other = null;
            switch (c)
            {
                case "dynamic":
                case "mesh":
                case "unflaggedLater":
                case "unflaggedLaterQuiet":
                case "unflaggedLaterWake":
                    box.GetComponent<Collider>().hasModifiableContacts = true; break;
                case "static": ground.GetComponent<Collider>().hasModifiableContacts = true; break;
                case "both":
                    box.GetComponent<Collider>().hasModifiableContacts = true;
                    ground.GetComponent<Collider>().hasModifiableContacts = true; break;
                case "trigger":
                    ground.GetComponent<Collider>().isTrigger = true;
                    ground.GetComponent<Collider>().hasModifiableContacts = true;
                    box.GetComponent<Collider>().hasModifiableContacts = true; break;
                case "kinematicStatic":
                    box.GetComponent<Collider>().hasModifiableContacts = true;
                    ground.GetComponent<Collider>().hasModifiableContacts = true; break;
                case "kinematicDynamic":
                    other = Make("pusher", PrimitiveType.Cube, new Vector3(.3f, 1.88f, -.2f), new Vector3(.8f, .8f, .8f));
                    Body(other, kinematic: true);
                    other.GetComponent<Collider>().hasModifiableContacts = true; break;
                case "compound":
                    UnityEngine.Object.DestroyImmediate(box.GetComponent<BoxCollider>());
                    var child = Make("child", PrimitiveType.Sphere, Vector3.zero, Vector3.one);
                    child.transform.SetParent(box.transform, false);
                    child.transform.localPosition = new Vector3(.2f, -.05f, 0);
                    child.GetComponent<Collider>().hasModifiableContacts = true; break;
            }
            // Changing the flag on a live contact: which collision messages follow.
            // Quiet: without a collision message receiver. Wake: the change also moves the body.
            if (c.EndsWith("Later") || c.EndsWith("Wake")) box.AddComponent<ModifyOrderLog>();
            ModifyOrderLog.events.Clear();
            Physics.SyncTransforms();
            ResetCounts();
            var perStep = new List<object>();
            for (int s = 0; s < 8; s++)
            {
                calls = pairs = 0;
                seen.Clear();
                dump = s == 2;
                dumpKey = "pair/" + c;
                if (c.StartsWith("unflaggedLater") && s == 4) box.GetComponent<Collider>().hasModifiableContacts = false;
                if (c.StartsWith("flaggedLater") && s == 4) box.GetComponent<Collider>().hasModifiableContacts = true;
                if (c.EndsWith("Wake") && s == 4) rb.position += new Vector3(0, .002f, 0);
                ModifyOrderLog.events.Add("step" + s);
                Physics.Simulate(Dt);
                perStep.Add(calls);
                perStep.Add(pairs);
                seen.Sort(string.CompareOrdinal);
                Record("pair/" + c + "/step" + s + "/seen", string.Join(";", seen));
            }
            Record("pair/" + c + "/counts", perStep.ToArray());
            if (c.EndsWith("Later") || c.EndsWith("Wake")) Record("pair/" + c + "/messages", string.Join(";", ModifyOrderLog.events));
            Record("pair/" + c + "/flags", box.GetComponent<Collider>() != null && box.GetComponent<Collider>().hasModifiableContacts,
                ground.GetComponent<Collider>().hasModifiableContacts);
            Record("pair/" + c + "/body", rb.position, rb.rotation);
            Clean();
        }
    }

    /// <summary>A flagged body resting on the ground stops being reported once it sleeps.</summary>
    static void Sleep()
    {
        Ground();
        var box = Make("box", PrimitiveType.Cube, new Vector3(0, 1.0f, 0), Vector3.one);
        var rb = Body(box);
        box.GetComponent<Collider>().hasModifiableContacts = true;
        Physics.SyncTransforms();
        var perStep = new List<object>();
        for (int s = 0; s < 120; s++)
        {
            calls = pairs = 0;
            Physics.Simulate(Dt);
            perStep.Add(pairs);
        }
        Record("sleep/pairs", perStep.ToArray());
        Record("sleep/state", rb.IsSleeping());
        Clean();
    }

    /// <summary>Many flagged pairs in one step: how they are split among event calls.</summary>
    static void Batches()
    {
        Ground();
        for (int i = 0; i < 300; i++)
        {
            var box = Make("b" + i, PrimitiveType.Cube, new Vector3((i % 20) - 9.5f, 1.0f, (i / 20) - 7.5f), new Vector3(.8f, 1, .8f));
            Body(box);
            box.GetComponent<Collider>().hasModifiableContacts = true;
        }
        Physics.SyncTransforms();
        ResetCounts();
        Physics.Simulate(Dt);
        Record("batch/step0", calls, pairs);
        ResetCounts();
        Physics.Simulate(Dt);
        Record("batch/step1", calls, pairs);
        Clean();
    }

    /// <summary>Each edit applied to every contact of a falling box, then its motion.</summary>
    static void Edits()
    {
        var kinds = new (string Name, Edit Edit)[]
        {
            ("none", null),
            ("ignore", (ref ModifiableContactPair p, int i) => p.IgnoreContact(i)),
            ("ignoreHalf", (ref ModifiableContactPair p, int i) => { if (p.GetPoint(i).x < 0) p.IgnoreContact(i); }),
            ("separation", (ref ModifiableContactPair p, int i) => p.SetSeparation(i, p.GetSeparation(i) - .2f)),
            ("normal", (ref ModifiableContactPair p, int i) => p.SetNormal(i, (p.GetNormal(i) + new Vector3(.4f, 0, 0)).normalized)),
            ("point", (ref ModifiableContactPair p, int i) => p.SetPoint(i, p.GetPoint(i) + new Vector3(.4f, 0, 0))),
            ("targetVelocity", (ref ModifiableContactPair p, int i) => p.SetTargetVelocity(i, new Vector3(2, 0, 0))),
            ("maxImpulse", (ref ModifiableContactPair p, int i) => p.SetMaxImpulse(i, .005f)),
            ("bounciness", (ref ModifiableContactPair p, int i) => p.SetBounciness(i, 1)),
            ("dynamicFriction", (ref ModifiableContactPair p, int i) => { p.SetDynamicFriction(i, 0); p.SetStaticFriction(i, 0); }),
            ("highFriction", (ref ModifiableContactPair p, int i) => { p.SetDynamicFriction(i, 1); p.SetStaticFriction(i, 1); }),
            ("staticFriction", (ref ModifiableContactPair p, int i) => p.SetStaticFriction(i, 2)),
        };
        foreach (var (name, e) in kinds)
        {
            Ground();
            var box = Make("box", PrimitiveType.Cube, new Vector3(0, 1.6f, 0), Vector3.one);
            var rb = Body(box);
            box.GetComponent<Collider>().hasModifiableContacts = true;
            rb.linearVelocity = new Vector3(3, 0, 0);
            Physics.SyncTransforms();
            edit = e;
            ResetCounts();
            var trace = new List<object>();
            for (int s = 1; s <= 60; s++)
            {
                Physics.Simulate(Dt);
                if (s % 10 == 0) { trace.Add(rb.position); trace.Add(rb.linearVelocity); }
            }
            Record("edit/" + name + "/trace", trace.ToArray());
            Record("edit/" + name + "/final", rb.position, rb.rotation, rb.linearVelocity, rb.angularVelocity);
            Record("edit/" + name + "/counts", calls, pairs);
            Clean();
        }

        // A dropped ball: restitution edits.
        foreach (var bounce in new[] { false, true })
        {
            Ground();
            var ball = Make("ball", PrimitiveType.Sphere, new Vector3(0, 3f, 0), Vector3.one);
            var rb = Body(ball);
            ball.GetComponent<Collider>().hasModifiableContacts = true;
            Physics.SyncTransforms();
            if (bounce) edit = (ref ModifiableContactPair p, int i) => p.SetBounciness(i, 1);
            float peak = float.MinValue;
            bool falling = true;
            var trace = new List<object>();
            for (int s = 1; s <= 100; s++)
            {
                Physics.Simulate(Dt);
                if (rb.linearVelocity.y > 0) falling = false;
                if (!falling) peak = Mathf.Max(peak, rb.position.y);
                if (s % 10 == 0) trace.Add(rb.position.y);
            }
            Record("ball/" + (bounce ? "bounce" : "none"), peak, rb.position, rb.linearVelocity);
            Record("ball/" + (bounce ? "bounce" : "none") + "/trace", trace.ToArray());
            Clean();
        }
    }

    /// <summary>Two boxes colliding head-on without gravity, with the pair's mass scales edited.</summary>
    static void Masses()
    {
        var cases = new (string Name, ModifiableMassProperties? Mass)[]
        {
            ("none", null),
            ("otherInfinite", new ModifiableMassProperties { inverseMassScale = 1, inverseInertiaScale = 1, otherInverseMassScale = 0, otherInverseInertiaScale = 0 }),
            ("selfInfinite", new ModifiableMassProperties { inverseMassScale = 0, inverseInertiaScale = 0, otherInverseMassScale = 1, otherInverseInertiaScale = 1 }),
            ("heavier", new ModifiableMassProperties { inverseMassScale = .25f, inverseInertiaScale = 1, otherInverseMassScale = 1, otherInverseInertiaScale = 1 }),
        };
        var gravity = Physics.gravity;
        Physics.gravity = Vector3.zero;
        foreach (var (name, mass) in cases)
        {
            // Frictionless spheres: the impulse is along the line of centres wherever PhysX puts
            // the contact point, which depends on which collider is first in the pair (Unity does
            // not specify that order), so the motion is comparable.
            var a = Make("a", PrimitiveType.Sphere, new Vector3(-1.2f, 0, 0), Vector3.one);
            var b = Make("b", PrimitiveType.Sphere, new Vector3(0.5f, 0.3f, 0), Vector3.one);
            var frictionless = new PhysicsMaterial("frictionless") { dynamicFriction = 0, staticFriction = 0, frictionCombine = PhysicsMaterialCombine.Minimum };
            a.GetComponent<Collider>().sharedMaterial = b.GetComponent<Collider>().sharedMaterial = frictionless;
            var ra = Body(a);
            var rbb = Body(b);
            a.GetComponent<Collider>().hasModifiableContacts = true;
            ra.linearVelocity = new Vector3(4, 0, 0);
            Physics.SyncTransforms();
            ResetCounts();
            bool read = false;
            editPair = pair =>
            {
                bool first = Name(pair.colliderInstanceID) == "a";
                if (!read)
                {
                    read = true;
                    var mp = pair.massProperties;
                    Record("mass/" + name + "/read", pair.contactCount, mp.inverseMassScale, mp.inverseInertiaScale,
                        mp.otherInverseMassScale, mp.otherInverseInertiaScale, first ? pair.bodyVelocity : pair.otherBodyVelocity);
                }
                if (!mass.HasValue) return;
                // Orient the edit on body "a", whichever side of the pair it is.
                var m = mass.Value;
                pair.massProperties = first ? m : new ModifiableMassProperties
                {
                    inverseMassScale = m.otherInverseMassScale, inverseInertiaScale = m.otherInverseInertiaScale,
                    otherInverseMassScale = m.inverseMassScale, otherInverseInertiaScale = m.inverseInertiaScale,
                };
            };
            for (int s = 0; s < 30; s++) Physics.Simulate(Dt);
            Record("mass/" + name, ra.position, ra.linearVelocity, rbb.position, rbb.linearVelocity, rbb.angularVelocity);
            Record("mass/" + name + "/counts", calls, pairs);
            Clean();
        }
        Physics.gravity = gravity;
    }

    /// <summary>A fast continuous ball hitting a thin static wall: the CCD event, its data and edits.</summary>
    static void Ccd()
    {
        var gravity = Physics.gravity;
        Physics.gravity = Vector3.zero;
        foreach (var mode in new[] { "none", "read", "ignore", "both", "discrete", "unflagged" })
        {
            var wall = Make("wall", PrimitiveType.Cube, new Vector3(5, 0, 0), new Vector3(.1f, 4, 4));
            var ball = Make("ball", PrimitiveType.Sphere, new Vector3(0, 0, 0), new Vector3(.4f, .4f, .4f));
            var rb = Body(ball);
            rb.collisionDetectionMode = mode == "discrete" ? CollisionDetectionMode.Discrete : CollisionDetectionMode.Continuous;
            ball.GetComponent<Collider>().hasModifiableContacts = mode != "unflagged";
            rb.linearVelocity = new Vector3(150, 0, 0);
            Physics.SyncTransforms();
            ResetCounts();
            if (mode == "ignore") editCcd = (ref ModifiableContactPair p, int i) => p.IgnoreContact(i);
            if (mode == "both")
            {
                editCcd = (ref ModifiableContactPair p, int i) => p.IgnoreContact(i);
                edit = (ref ModifiableContactPair p, int i) => p.IgnoreContact(i);
            }
            dumpKey = "ccd/" + mode;
            dumpCcd = true;
            dump = true;
            var trace = new List<object>();
            for (int s = 0; s < 6; s++)
            {
                Physics.Simulate(Dt);
                trace.Add(rb.position.x);
                trace.Add(calls);
                trace.Add(pairs);
                trace.Add(ccdCalls);
                trace.Add(ccdPairs);
            }
            Record("ccd/" + mode + "/trace", trace.ToArray());
            Record("ccd/" + mode + "/final", rb.position, rb.linearVelocity);
            Clean();
        }
        Physics.gravity = gravity;
    }

    /// <summary>When the event runs relative to the collision messages of the same step.</summary>
    static void Order()
    {
        Ground();
        var box = Make("box", PrimitiveType.Cube, new Vector3(0, 1.1f, 0), Vector3.one);
        Body(box);
        box.GetComponent<Collider>().hasModifiableContacts = true;
        box.AddComponent<ModifyOrderLog>();
        Physics.SyncTransforms();
        ModifyOrderLog.events.Clear();
        editPair = pair => ModifyOrderLog.events.Add("modify");
        for (int s = 0; s < 8; s++)
        {
            ModifyOrderLog.events.Add("simulate" + s);
            Physics.Simulate(Dt);
        }
        Record("order/events", string.Join(";", ModifyOrderLog.events));
        Clean();
    }

    /// <summary>No subscriber: the flag alone changes nothing.</summary>
    static void Unsubscribed()
    {
        Physics.ContactModifyEvent -= OnModify;
        Ground();
        var box = Make("box", PrimitiveType.Cube, new Vector3(0, 1.6f, 0), Vector3.one);
        var rb = Body(box);
        box.GetComponent<Collider>().hasModifiableContacts = true;
        rb.linearVelocity = new Vector3(3, 0, 0);
        Physics.SyncTransforms();
        ResetCounts();
        for (int s = 0; s < 60; s++) Physics.Simulate(Dt);
        Record("unsubscribed", rb.position, rb.linearVelocity, calls);
        Clean();
        Physics.ContactModifyEvent += OnModify;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("CONTACT_MODIFY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        var mode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        Physics.ContactModifyEvent += OnModify;
        Physics.ContactModifyEventCCD += OnModifyCcd;
        try
        {
            Pairs();
            Sleep();
            Batches();
            Edits();
            Masses();
            Ccd();
            Order();
            Unsubscribed();
            var probe = new GameObject("probe");
            made.Add(probe);
            Record("default", probe.AddComponent<BoxCollider>().hasModifiableContacts);
            Record("complete", 1);
        }
        finally
        {
            Physics.ContactModifyEvent -= OnModify;
            Physics.ContactModifyEventCCD -= OnModifyCcd;
            Clean();
            Physics.simulationMode = mode;
        }
        // Informational: Unity documents that the event may run on any thread.
        Debug.Log("contact modify calls off the main thread: " + offThread);
        File.WriteAllText(output, records.ToString());
        Debug.Log("contact modify complete: " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
