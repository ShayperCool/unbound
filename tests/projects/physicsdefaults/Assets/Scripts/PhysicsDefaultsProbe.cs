using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public static class PhysicsDefaultsProbe
{
    const float Dt = .02f;
    static readonly StringBuilder records = new StringBuilder();
    static readonly List<GameObject> made = new List<GameObject>();
    struct Globals
    {
        public int solver, velocity;
        public float sleep, angular, depen, contact;
        public Globals(bool capture)
        {
            solver = Physics.defaultSolverIterations; velocity = Physics.defaultSolverVelocityIterations;
            sleep = Physics.sleepThreshold; angular = Physics.defaultMaxAngularSpeed;
            depen = Physics.defaultMaxDepenetrationVelocity; contact = Physics.defaultContactOffset;
        }
        public void Apply()
        {
            Physics.defaultSolverIterations = solver; Physics.defaultSolverVelocityIterations = velocity;
            Physics.sleepThreshold = sleep; Physics.defaultMaxAngularSpeed = angular;
            Physics.defaultMaxDepenetrationVelocity = depen; Physics.defaultContactOffset = contact;
        }
    }
    static void Record(string key, params float[] values)
    {
        records.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) records.Append(',');
            string s = values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) records.Append('"').Append(s).Append('"');
            else records.Append(s);
        }
        records.Append("]}\n");
    }
    static void StaticFields(string key) => Record(key, Physics.defaultSolverIterations, Physics.defaultSolverVelocityIterations,
        Physics.sleepThreshold, Physics.defaultMaxAngularSpeed, Physics.defaultMaxDepenetrationVelocity, Physics.defaultContactOffset);
    static void Fields(string key, Rigidbody rb) => Record(key, rb.solverIterations, rb.solverVelocityIterations,
        rb.sleepThreshold, rb.maxAngularVelocity, rb.maxDepenetrationVelocity, rb.maxLinearVelocity);
    static Rigidbody Body(bool active = true, bool collider = true)
    {
        var go = new GameObject("ProbeBody"); made.Add(go); go.SetActive(active);
        if (collider) go.AddComponent<BoxCollider>();
        var rb = go.AddComponent<Rigidbody>(); rb.useGravity = false; rb.linearDamping = rb.angularDamping = 0;
        return rb;
    }
    static void Clean()
    {
        foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        made.Clear();
    }
    static void Override(Rigidbody rb)
    {
        rb.solverIterations = 21; rb.solverVelocityIterations = 7;
        rb.sleepThreshold = .0625f; rb.maxAngularVelocity = 6; rb.maxDepenetrationVelocity = 2;
        rb.maxLinearVelocity = 12;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_DEFAULTS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var initial = new Globals(true);
        var mode = Physics.simulationMode; var gravity = Physics.gravity;
        Physics.simulationMode = SimulationMode.Script; Physics.gravity = Vector3.zero;
        try
        {
            StaticFields("project/globals");
            foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Fields("serialized/" + rb.name, rb);
                rb.gameObject.SetActive(false); made.Add(rb.gameObject);
            }
            var prefab = Resources.Load<GameObject>("Body");
            Fields("serialized/prefab", prefab.GetComponent<Rigidbody>());
            var pc = UnityEngine.Object.Instantiate(prefab); made.Add(pc);
            Fields("serialized/prefab-clone", pc.GetComponent<Rigidbody>());
            pc.SetActive(true); Fields("serialized/prefab-active", pc.GetComponent<Rigidbody>());
            Clean();

            // A getter forces Unbound's lazy native allocation. Inactive bodies cannot allocate.
            var allocated = Body(); var ignoredPosition = allocated.position;
            var lazy = Body(); var inactive = Body(false); var overridden = Body(); Override(overridden);
            Fields("lifetime/allocated-initial", allocated); Fields("lifetime/lazy-initial", lazy);
            Fields("lifetime/inactive-initial", inactive); Fields("lifetime/override-initial", overridden);
            var changed = new Globals(true) { solver = 15, velocity = 5, sleep = .015625f, angular = 4, depen = 1.5f, contact = .04f };
            changed.Apply(); StaticFields("lifetime/changed-globals");
            Fields("lifetime/allocated-after", allocated); Fields("lifetime/lazy-after", lazy);
            Fields("lifetime/inactive-after", inactive); Fields("lifetime/override-after", overridden);
            inactive.gameObject.SetActive(true); ignoredPosition = lazy.position;
            Fields("lifetime/lazy-flushed", lazy); Fields("lifetime/activated", inactive);
            var future = Body(); Fields("lifetime/future", future);
            allocated.gameObject.SetActive(false); allocated.gameObject.SetActive(true);
            Fields("lifetime/reactivated", allocated);
            foreach (var source in new[] { allocated, overridden, inactive })
            {
                var clone = UnityEngine.Object.Instantiate(source.gameObject); made.Add(clone);
                Fields("clone/" + (source == allocated ? "defaults" : source == overridden ? "override" : "inactive"), clone.GetComponent<Rigidbody>());
            }
            var latePrefab = UnityEngine.Object.Instantiate(prefab); made.Add(latePrefab);
            Fields("clone/prefab-late", latePrefab.GetComponent<Rigidbody>());
            Clean(); initial.Apply();

            // Change one global at a time to expose existing-body updates without cross-property effects.
            for (int property = 0; property < 6; property++)
            {
                initial.Apply(); var old = Body(); ignoredPosition = old.position;
                var custom = Body(false); Override(custom);
                switch (property)
                {
                    case 0: Physics.defaultSolverIterations = 16; break;
                    case 1: Physics.defaultSolverVelocityIterations = 6; break;
                    case 2: Physics.sleepThreshold = .125f; break;
                    case 3: Physics.defaultMaxAngularSpeed = 8; break;
                    case 4: Physics.defaultMaxDepenetrationVelocity = 4; break;
                    case 5: Physics.defaultContactOffset = .05f; break;
                }
                Fields("global-" + property + "/old", old); Fields("global-" + property + "/override", custom);
                Fields("global-" + property + "/future", Body());
                Record("global-" + property + "/contact-old", old.GetComponent<BoxCollider>().contactOffset);
                Record("global-" + property + "/contact-new", Body().GetComponent<BoxCollider>().contactOffset);
                Clean();
            }
            initial.Apply();
            foreach (int value in new[] { -1000, -256, -255, -2, -1, 0, 1, 255, 256, 1000, int.MinValue, int.MaxValue })
            {
                for (int property = 0; property < 2; property++)
                foreach (bool active in new[] { false, true })
                {
                    initial.Apply(); var old = Body(active); if (active) ignoredPosition = old.position;
                    if (property == 0) { Physics.defaultSolverIterations = value; old.solverIterations = value; }
                    else { Physics.defaultSolverVelocityIterations = value; old.solverVelocityIterations = value; }
                    string key = "invalid-int/" + active + "/" + property + "/" + value;
                    StaticFields(key + "/globals"); Fields(key + "/body", old); Fields(key + "/future", Body(false)); Clean();
                }
            }
            foreach (float value in new[] { -1f, 0, .001f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            {
                for (int property = 0; property < 4; property++)
                foreach (bool active in new[] { false, true })
                {
                    initial.Apply(); var old = Body(active); if (active) ignoredPosition = old.position;
                    switch (property)
                    {
                        case 0: Physics.sleepThreshold = value; old.sleepThreshold = value; break;
                        case 1: Physics.defaultMaxAngularSpeed = value; old.maxAngularVelocity = value; break;
                        case 2: Physics.defaultMaxDepenetrationVelocity = value; old.maxDepenetrationVelocity = value; break;
                        case 3: Physics.defaultContactOffset = value; old.maxLinearVelocity = value; break;
                    }
                    string key = "invalid-float/" + active + "/" + property + "/" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    StaticFields(key + "/globals"); Fields(key + "/body", old); Fields(key + "/future", Body(false)); Clean();
                }
            }
            initial.Apply();
            Dynamics(initial);
        }
        finally { Clean(); initial.Apply(); Physics.gravity = gravity; Physics.simulationMode = mode; }
        File.WriteAllText(output, records.ToString());
        Debug.Log("physics defaults complete: " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Dynamics(Globals initial)
    {
        foreach (string kind in new[] { "angular", "sleep", "depenetration", "solver" })
        foreach (string timing in new[] { "project", "global-before", "global-after", "override", "reactivate", "clone" })
        {
            initial.Apply();
            if (timing == "global-before") { Physics.defaultMaxAngularSpeed = 2; Physics.sleepThreshold = 0; Physics.defaultMaxDepenetrationVelocity = .5f; Physics.defaultSolverIterations = 1; Physics.defaultSolverVelocityIterations = 0; }
            var rb = Body(); var ignoredPosition = rb.position;
            if (timing == "global-after") { Physics.defaultMaxAngularSpeed = 2; Physics.sleepThreshold = 0; Physics.defaultMaxDepenetrationVelocity = .5f; Physics.defaultSolverIterations = 1; Physics.defaultSolverVelocityIterations = 0; }
            if (timing == "override" || timing == "clone") { rb.maxAngularVelocity = 2; rb.sleepThreshold = 0; rb.maxDepenetrationVelocity = .5f; rb.solverIterations = 1; rb.solverVelocityIterations = 0; }
            if (timing == "reactivate") { rb.gameObject.SetActive(false); rb.gameObject.SetActive(true); }
            if (timing == "clone") { var clone = UnityEngine.Object.Instantiate(rb.gameObject); rb.gameObject.SetActive(false); made.Add(clone); rb = clone.GetComponent<Rigidbody>(); }
            Fields("dynamics/" + kind + "/" + timing + "/fields", rb);
            if (kind == "angular") rb.angularVelocity = new Vector3(0, 30, 0);
            if (kind == "sleep")
            {
                var support = new GameObject("Support"); made.Add(support);
                support.transform.position = new Vector3(0, -.5f, 0); support.AddComponent<BoxCollider>().size = new Vector3(20, 1, 20);
                rb.position = new Vector3(0, .5f, 0); rb.useGravity = true; Physics.gravity = new Vector3(0, -9.81f, 0);
                rb.linearVelocity = new Vector3(.1f, 0, 0);
            }
            if (kind == "depenetration" || kind == "solver")
            {
                var ground = new GameObject("Ground"); made.Add(ground);
                ground.transform.position = new Vector3(0, -.75f, 0); ground.AddComponent<BoxCollider>().size = new Vector3(20, 1, 20);
                rb.position = new Vector3(0, -.2f, 0);
                if (kind == "solver") { rb.rotation = Quaternion.Euler(5, 0, 7); rb.useGravity = true; Physics.gravity = new Vector3(0, -9.81f, 0); }
            }
            Physics.SyncTransforms();
            int steps = kind == "sleep" ? 60 : kind == "solver" ? 5 : 1;
            for (int step = 0; step < steps; step++) Physics.Simulate(Dt);
            var p = rb.position; var v = rb.linearVelocity; var a = rb.angularVelocity;
            Record("dynamics/" + kind + "/" + timing + "/motion", p.x,p.y,p.z,v.x,v.y,v.z,a.x,a.y,a.z,rb.IsSleeping()?1:0);
            Clean(); Physics.gravity = Vector3.zero;
        }
        initial.Apply();
    }
}
