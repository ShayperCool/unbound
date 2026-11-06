using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Records which 3D collider pairs contact, trigger, sweep and block with every
/// combination of the layer collision matrix, Collider/Rigidbody include/exclude layers and
/// Collider.layerOverridePriority, and which scene queries see overridden colliders.</summary>
public static class LayerOverrideProbe
{
    const int LA = 8, LB = 9, LU = 10;
    const float Dt = .02f;
    static readonly StringBuilder records = new StringBuilder();
    static readonly List<GameObject> made = new List<GameObject>();
    // Collider mask kinds: what includeLayers/excludeLayers name, relative to the other side's layer.
    static readonly string[] Kinds = { "none", "incOther", "excOther", "incUnrelated", "excUnrelated", "incExcOther" };
    static readonly int[] BodyKinds = { 0, 1, 2, 3 };
    static readonly int[] Priorities = { -1, 0, 1 };

    static void Record(string key, params int[] values)
    {
        records.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) records.Append(',');
            records.Append(values[i]);
        }
        records.Append("]}\n");
    }

    static void Masks(int kind, int other, out int include, out int exclude)
    {
        include = exclude = 0;
        switch (kind)
        {
            case 1: include = 1 << other; break;
            case 2: exclude = 1 << other; break;
            case 3: include = 1 << LU; break;
            case 4: exclude = 1 << LU; break;
            case 5: include = exclude = 1 << other; break;
        }
    }

    static void Apply(Collider c, int kind, int other, int priority)
    {
        Masks(kind, other, out int include, out int exclude);
        c.includeLayers = include;
        c.excludeLayers = exclude;
        c.layerOverridePriority = priority;
    }

    static void Apply(Rigidbody rb, int kind, int other)
    {
        Masks(kind, other, out int include, out int exclude);
        rb.includeLayers = include;
        rb.excludeLayers = exclude;
    }

    static GameObject Box(string name, int layer, Vector3 position, bool body, bool gravity = false)
    {
        var go = new GameObject(name);
        made.Add(go);
        go.layer = layer;
        go.transform.position = position;
        go.AddComponent<BoxCollider>();
        if (body)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = gravity;
            rb.linearDamping = rb.angularDamping = 0;
        }
        return go;
    }

    static Vector3 Slot(int i) => new Vector3((i % 64) * 4f, 0, (i / 64) * 4f);

    static void Step(int steps)
    {
        for (int i = 0; i < steps; i++) Physics.Simulate(Dt);
    }

    static void Clean()
    {
        foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        made.Clear();
    }

    static string Matrix(bool ignore) => ignore ? "ignored" : "colliding";

    /// <summary>A dynamic box A (layer 8) overlapping a static box B (layer 9).</summary>
    static void Pairs(bool ignore, bool trigger)
    {
        Physics.IgnoreLayerCollision(LA, LB, ignore);
        var cases = new List<(string key, OverrideCounter counter, Vector3 start)>();
        int n = 0;
        for (int am = 0; am < Kinds.Length; am++)
        foreach (int ab in BodyKinds)
        foreach (int ap in Priorities)
        for (int bm = 0; bm < Kinds.Length; bm++)
        foreach (int bp in Priorities)
        {
            var at = Slot(n++);
            var a = Box("A", LA, at, true);
            var ac = a.GetComponent<BoxCollider>();
            ac.isTrigger = trigger;
            Apply(ac, am, LB, ap);
            Apply(a.GetComponent<Rigidbody>(), ab, LB);
            var b = Box("B", LB, at + new Vector3(0, .6f, 0), false);
            Apply(b.GetComponent<BoxCollider>(), bm, LA, bp);
            string key = $"pair/{Matrix(ignore)}/{(trigger ? "trigger" : "solid")}/A-{Kinds[am]}-body-{Kinds[ab]}-p{ap}/B-{Kinds[bm]}-p{bp}";
            cases.Add((key, a.AddComponent<OverrideCounter>(), at));
        }
        Step(3);
        foreach (var (key, c, start) in cases)
        {
            bool moved = (c.transform.position - start).sqrMagnitude > 1e-8f;
            Record(key, c.enter, c.stay > 0 ? 1 : 0, c.triggerEnter, c.triggerStay > 0 ? 1 : 0, moved ? 1 : 0);
        }
        Clean();
    }

    /// <summary>Two dynamic boxes whose Rigidbodies carry the masks.</summary>
    static void Bodies(bool ignore)
    {
        Physics.IgnoreLayerCollision(LA, LB, ignore);
        var cases = new List<(string key, OverrideCounter counter, Vector3 start)>();
        int n = 0;
        foreach (int ab in BodyKinds)
        foreach (int ap in Priorities)
        foreach (int bb in BodyKinds)
        foreach (int bp in Priorities)
        {
            var at = Slot(n++);
            var a = Box("A", LA, at, true);
            a.GetComponent<BoxCollider>().layerOverridePriority = ap;
            Apply(a.GetComponent<Rigidbody>(), ab, LB);
            var b = Box("B", LB, at + new Vector3(0, .6f, 0), true);
            b.GetComponent<BoxCollider>().layerOverridePriority = bp;
            Apply(b.GetComponent<Rigidbody>(), bb, LA);
            cases.Add(($"bodies/{Matrix(ignore)}/A-body-{Kinds[ab]}-p{ap}/B-body-{Kinds[bb]}-p{bp}", a.AddComponent<OverrideCounter>(), at));
        }
        Step(3);
        foreach (var (key, c, start) in cases)
        {
            bool moved = (c.transform.position - start).sqrMagnitude > 1e-8f;
            Record(key, c.enter, c.stay > 0 ? 1 : 0, moved ? 1 : 0);
        }
        Clean();
    }

    /// <summary>A box resting on a static box under gravity while the masks and priorities
    /// change between steps; the same sequence with A as a trigger.</summary>
    static void Runtime(bool trigger)
    {
        Physics.IgnoreLayerCollision(LA, LB, false);
        string prefix = "runtime/" + (trigger ? "trigger" : "solid") + "/";
        var a = Box("A", LA, new Vector3(0, 1, 0), true, gravity: !trigger);
        var ac = a.GetComponent<BoxCollider>();
        ac.isTrigger = trigger;
        var arb = a.GetComponent<Rigidbody>();
        var b = Box("B", LB, Vector3.zero, false);
        b.transform.localScale = new Vector3(4, 1, 4);
        var bc = b.GetComponent<BoxCollider>();
        var counter = a.AddComponent<OverrideCounter>();
        var stages = new (string name, Action change)[]
        {
            ("initial", () => { }),
            ("B-excludes-A", () => bc.excludeLayers = 1 << LA),
            ("A-includes-B", () => ac.includeLayers = 1 << LB),
            ("A-priority-1", () => ac.layerOverridePriority = 1),
            ("B-priority-2", () => bc.layerOverridePriority = 2),
            ("A-body-excludes-B", () => arb.excludeLayers = 1 << LB),
            ("B-excludes-none", () => bc.excludeLayers = 0),
            ("A-body-excludes-none", () => arb.excludeLayers = 0),
            ("matrix-ignored-A-includes-none", () => { ac.includeLayers = 0; Physics.IgnoreLayerCollision(LA, LB, true); }),
            ("A-includes-B-again", () => ac.includeLayers = 1 << LB),
            ("B-priority-1-tie", () => bc.layerOverridePriority = 1),
            ("B-includes-A", () => bc.includeLayers = 1 << LA),
            ("A-body-includes-unrelated-A-includes-none", () => { ac.includeLayers = 0; arb.includeLayers = 1 << LU; }),
        };
        foreach (var (name, change) in stages)
        {
            change();
            // Keep the box on the ground: falls during contact-free stages would move it away.
            arb.position = new Vector3(0, .98f, 0);
            arb.linearVelocity = Vector3.zero;
            int enter = counter.enter + counter.triggerEnter, exit = counter.exit + counter.triggerExit;
            counter.ResetStay();
            Step(2);
            Record(prefix + name, counter.enter + counter.triggerEnter - enter, counter.exit + counter.triggerExit - exit,
                counter.stay + counter.triggerStay > 0 ? 1 : 0);
        }
        Physics.IgnoreLayerCollision(LA, LB, false);
        Clean();
    }

    /// <summary>Scene queries against a layer-9 box whose collider or body overrides layers.</summary>
    static void Queries()
    {
        var targets = new (string name, Action<GameObject> setup)[]
        {
            ("none", go => { }),
            ("excludeAll", go => go.GetComponent<Collider>().excludeLayers = ~0),
            ("includeAll", go => go.GetComponent<Collider>().includeLayers = ~0),
            ("excludeOwn", go => go.GetComponent<Collider>().excludeLayers = 1 << LB),
            ("bodyExcludeAll", go => { var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.excludeLayers = ~0; }),
        };
        var masks = new (string name, int mask)[] { ("all", ~0), ("own", 1 << LB), ("other", 1 << LA) };
        foreach (bool ignore in new[] { false, true })
        {
            Physics.IgnoreLayerCollision(LA, LB, ignore);
            for (int t = 0; t < targets.Length; t++)
            {
                var at = new Vector3(t * 4f, 0, 0);
                var go = Box("T", LB, at, false);
                targets[t].setup(go);
                Physics.SyncTransforms();
                var above = at + new Vector3(0, 5, 0);
                foreach (var (mname, mask) in masks)
                {
                    string key = $"query/{Matrix(ignore)}/{targets[t].name}/{mname}";
                    Record(key,
                        Physics.Raycast(above, Vector3.down, 10, mask) ? 1 : 0,
                        Physics.CheckSphere(at, .1f, mask) ? 1 : 0,
                        Physics.OverlapSphere(at, .1f, mask).Length,
                        Physics.SphereCast(above, .2f, Vector3.down, out _, 10, mask) ? 1 : 0,
                        Physics.BoxCast(above, Vector3.one * .2f, Vector3.down, out _, Quaternion.identity, 10, mask) ? 1 : 0,
                        Physics.CapsuleCast(above, above + Vector3.up * .3f, .2f, Vector3.down, out _, 10, mask) ? 1 : 0,
                        Physics.RaycastAll(above, Vector3.down, 10, mask).Length);
                }
                Clean();
            }
        }
        Physics.IgnoreLayerCollision(LA, LB, false);
    }

    /// <summary>Rigidbody.SweepTest of a layer-8 body towards a layer-9 box.</summary>
    static void Sweeps()
    {
        int[] kinds = { 0, 1, 2 };
        foreach (bool ignore in new[] { false, true })
        {
            Physics.IgnoreLayerCollision(LA, LB, ignore);
            var cases = new List<(string key, Rigidbody body)>();
            int n = 0;
            foreach (int sm in kinds)
            foreach (int sb in kinds)
            foreach (int tm in kinds)
            foreach (int tp in new[] { 0, 1 })
            {
                var at = Slot(n++);
                var s = Box("S", LA, at + new Vector3(0, 5, 0), true);
                Apply(s.GetComponent<BoxCollider>(), sm, LB, 0);
                var rb = s.GetComponent<Rigidbody>();
                rb.isKinematic = true;
                Apply(rb, sb, LB);
                var t = Box("T", LB, at, false);
                Apply(t.GetComponent<BoxCollider>(), tm, LA, tp);
                cases.Add(($"sweep/{Matrix(ignore)}/S-{Kinds[sm]}-body-{Kinds[sb]}/T-{Kinds[tm]}-p{tp}", rb));
            }
            Physics.SyncTransforms();
            foreach (var (key, rb) in cases)
                Record(key, rb.SweepTest(Vector3.down, out _, 10) ? 1 : 0, rb.SweepTestAll(Vector3.down, 10).Length);
            Clean();
        }
        Physics.IgnoreLayerCollision(LA, LB, false);
    }

    static CharacterController Controller(string name, int layer, Vector3 position)
    {
        var go = new GameObject(name);
        made.Add(go);
        go.layer = layer;
        go.transform.position = position;
        var cc = go.AddComponent<CharacterController>();
        cc.radius = .5f;
        cc.height = 2;
        return cc;
    }

    /// <summary>CharacterController.Move of a layer-8 controller into a layer-9 wall or controller.</summary>
    static void Controllers()
    {
        int[] kinds = { 0, 1, 2, 3 };
        foreach (bool ignore in new[] { false, true })
        {
            Physics.IgnoreLayerCollision(LA, LB, ignore);
            var cases = new List<(string key, CharacterController cc, Vector3 start)>();
            int n = 0;
            foreach (int cm in kinds)
            foreach (int cp in new[] { 0, 1 })
            foreach (int wm in kinds)
            foreach (int wp in new[] { 0, 1 })
            {
                var at = Slot(n++) * 2 + new Vector3(0, 100, 0);
                var cc = Controller("C", LA, at);
                Apply(cc, cm, LB, cp);
                var wall = Box("W", LB, at + new Vector3(2, 0, 0), false);
                wall.transform.localScale = new Vector3(1, 4, 1);
                Apply(wall.GetComponent<BoxCollider>(), wm, LA, wp);
                cases.Add(($"controller/{Matrix(ignore)}/C-{Kinds[cm]}-p{cp}/W-{Kinds[wm]}-p{wp}", cc, at));
            }
            foreach (int cm in kinds)
            foreach (int cp in new[] { 0, 1 })
            foreach (int om in kinds)
            foreach (int op in new[] { 0, 1 })
            {
                var at = Slot(n++) * 2 + new Vector3(0, 100, 0);
                var cc = Controller("C", LA, at);
                Apply(cc, cm, LB, cp);
                var other = Controller("O", LB, at + new Vector3(2, 0, 0));
                Apply(other, om, LA, op);
                cases.Add(($"controllers/{Matrix(ignore)}/C-{Kinds[cm]}-p{cp}/O-{Kinds[om]}-p{op}", cc, at));
            }
            Physics.SyncTransforms();
            foreach (var (key, cc, start) in cases)
            {
                var flags = cc.Move(new Vector3(3, 0, 0));
                bool blocked = cc.transform.position.x - start.x < 2;
                Record(key, (int)flags, blocked ? 1 : 0);
            }
            Clean();
            // A dynamic box overlapping a controller: simulation contacts, not Move.
            var bodies = new List<(string key, OverrideCounter counter, Vector3 start)>();
            n = 0;
            foreach (int cm in kinds)
            foreach (int cp in new[] { 0, 1 })
            foreach (int bm in kinds)
            foreach (int bp in new[] { 0, 1 })
            {
                var at = Slot(n++) * 2 + new Vector3(0, 200, 0);
                var cc = Controller("C", LA, at);
                Apply(cc, cm, LB, cp);
                var box = Box("B", LB, at + new Vector3(.8f, 0, 0), true);
                Apply(box.GetComponent<BoxCollider>(), bm, LA, bp);
                bodies.Add(($"controller-body/{Matrix(ignore)}/C-{Kinds[cm]}-p{cp}/B-{Kinds[bm]}-p{bp}", box.AddComponent<OverrideCounter>(), box.transform.position));
            }
            Step(3);
            foreach (var (key, c, start) in bodies)
            {
                bool moved = (c.transform.position - start).sqrMagnitude > 1e-8f;
                Record(key, c.enter, c.stay > 0 ? 1 : 0, moved ? 1 : 0);
            }
            Clean();
        }
        Physics.IgnoreLayerCollision(LA, LB, false);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("LAYER_OVERRIDES_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var mode = Physics.simulationMode;
        var gravity = Physics.gravity;
        bool ignored = Physics.GetIgnoreLayerCollision(LA, LB);
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            Physics.gravity = Vector3.zero;
            foreach (bool ignore in new[] { false, true })
            foreach (bool trigger in new[] { false, true })
                Pairs(ignore, trigger);
            Bodies(false);
            Bodies(true);
            Physics.gravity = new Vector3(0, -9.81f, 0);
            Runtime(false);
            Runtime(true);
            Physics.gravity = Vector3.zero;
            Queries();
            Sweeps();
            Controllers();
            Record("complete", 1);
        }
        finally
        {
            Clean();
            Physics.IgnoreLayerCollision(LA, LB, ignored);
            Physics.gravity = gravity;
            Physics.simulationMode = mode;
        }
        File.WriteAllText(output, records.ToString());
        Debug.Log("layer overrides complete: " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
