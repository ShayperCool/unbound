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
    static readonly List<UnityEngine.Object> madeAssets = new List<UnityEngine.Object>();
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
        foreach (var asset in madeAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
        madeAssets.Clear();
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
                Record("serialized/" + rb.name + "/contact", rb.GetComponent<Collider>().contactOffset);
                rb.gameObject.SetActive(false); made.Add(rb.gameObject);
            }
            var prefab = Resources.Load<GameObject>("Body");
            Fields("serialized/prefab", prefab.GetComponent<Rigidbody>());
            Record("serialized/prefab/contact", prefab.GetComponent<Collider>().contactOffset);
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
            Colliders(initial, prefab);
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
    static Collider NewCollider(Type type, bool active, bool geometry = false)
    {
        var go = new GameObject("ColliderProbe"); made.Add(go); go.SetActive(active);
        var c = (Collider)go.AddComponent(type);
        if (geometry && c is MeshCollider meshCollider)
        {
            var mesh = new Mesh(); madeAssets.Add(mesh);
            mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f),
                new Vector3(0,0,.5f), new Vector3(0,1,0) };
            mesh.triangles = new[] { 0,1,2, 0,3,1, 1,3,2, 2,3,0 };
            meshCollider.convex = true; meshCollider.sharedMesh = mesh;
        }
        if (geometry && c is TerrainCollider terrainCollider)
        {
            var data = new TerrainData(); madeAssets.Add(data);
            data.heightmapResolution = 33; data.size = new Vector3(4,1,4); terrainCollider.terrainData = data;
        }
        return c;
    }
    static string FloatKey(float value) => value == float.Epsilon ? "epsilon" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    static void Colliders(Globals initial, GameObject prefab)
    {
        foreach (var type in new[] { typeof(BoxCollider), typeof(SphereCollider), typeof(CapsuleCollider),
            typeof(MeshCollider), typeof(TerrainCollider), typeof(CharacterController) })
        foreach (bool geometry in type == typeof(MeshCollider) || type == typeof(TerrainCollider) ? new[] { false, true } : new[] { false })
        {
            initial.Apply();
            var active = NewCollider(type, true, geometry); var allocated = active.bounds;
            var lazy = NewCollider(type, true, geometry); var inactive = NewCollider(type, false, geometry);
            var custom = NewCollider(type, true, geometry); custom.contactOffset = .075f;
            string key = "collider/" + type.Name + (geometry ? "-geometry" : "");
            Record(key + "/initial", active.contactOffset, lazy.contactOffset, inactive.contactOffset, custom.contactOffset);
            Physics.defaultContactOffset = .05f;
            Record(key + "/changed", active.contactOffset, lazy.contactOffset, inactive.contactOffset, custom.contactOffset);
            var future = NewCollider(type, false);
            Record(key + "/future", future.contactOffset);
            inactive.gameObject.SetActive(true); custom.gameObject.SetActive(true);
            allocated = lazy.bounds; active.enabled = false; active.enabled = true;
            active.gameObject.SetActive(false); active.gameObject.SetActive(true);
            Record(key + "/activated", active.contactOffset, lazy.contactOffset, inactive.contactOffset, custom.contactOffset);
            inactive.gameObject.SetActive(false);
            foreach (var source in new[] { active, custom, inactive })
            {
                var clone = UnityEngine.Object.Instantiate(source.gameObject); made.Add(clone);
                var copied = clone.GetComponent<Collider>();
                string cloneKey = key + "/clone-" + (source == active ? "default" : source == custom ? "custom" : "inactive");
                Record(cloneKey, copied.contactOffset, source.contactOffset);
                clone.SetActive(true); Record(cloneKey + "-activated", copied.contactOffset);
            }
            custom.gameObject.SetActive(false);
            Record(key + "/deactivated", custom.contactOffset);
            custom.contactOffset = .125f;
            Record(key + "/set-inactive", custom.contactOffset);
            custom.gameObject.SetActive(true);
            Record(key + "/reactivated", custom.contactOffset);
            custom.enabled = false; custom.contactOffset = .15f;
            Record(key + "/set-disabled", custom.contactOffset);
            custom.enabled = true;
            Record(key + "/reenabled", custom.contactOffset);
            var parent = new GameObject("InactiveParent"); made.Add(parent); parent.SetActive(false);
            custom.transform.SetParent(parent.transform); custom.contactOffset = .175f;
            Record(key + "/set-inactive-parent", custom.contactOffset);
            parent.SetActive(true); Record(key + "/parent-reactivated", custom.contactOffset);
            Clean();
        }
        initial.Apply();
        Physics.defaultContactOffset = .05f;
        Record("collider/prefab-after-global", prefab.GetComponent<Collider>().contactOffset);
        var pc = UnityEngine.Object.Instantiate(prefab); made.Add(pc);
        Record("collider/prefab-clone", pc.GetComponent<Collider>().contactOffset);
        pc.SetActive(true); Record("collider/prefab-active", pc.GetComponent<Collider>().contactOffset);
        Clean();
        foreach (float value in new[] { -1f, 0, float.Epsilon, .001f, .2f, float.NaN,
            float.NegativeInfinity, float.PositiveInfinity })
        foreach (bool active in new[] { false, true })
        {
            initial.Apply();
            var c = NewCollider(typeof(BoxCollider), active);
            if (active) { var allocated = c.bounds; }
            c.contactOffset = .075f; c.contactOffset = value;
            string key = "collider/invalid/" + active + "/" + FloatKey(value);
            Record(key, c.contactOffset);
            c.contactOffset = .125f; Record(key + "/restored", c.contactOffset);
            Clean();
        }
        foreach (float value in new[] { -1f, 0, float.Epsilon, .001f, float.NaN,
            float.NegativeInfinity, float.PositiveInfinity })
        {
            initial.Apply(); Physics.defaultContactOffset = value;
            var c = NewCollider(typeof(BoxCollider), false);
            Record("collider/global-invalid/" + FloatKey(value),
                Physics.defaultContactOffset, c.contactOffset);
            Clean();
        }
        initial.Apply();
        LiveContactOffsets(initial);
    }
    static void LiveContactOffsets(Globals initial)
    {
        var ground = new GameObject("ContactSupport"); made.Add(ground); ground.AddComponent<BoxCollider>();
        var rb = Body(); rb.sleepThreshold = 0; rb.position = new Vector3(1.08f,0,0);
        rb.linearVelocity = new Vector3(-.5f,0,0);
        var collider = rb.GetComponent<Collider>();
        var events = rb.gameObject.AddComponent<DefaultsContactEvents>();
        Physics.SyncTransforms();
        foreach (string stage in new[] { "before", "increase", "decrease", "restore", "reject-zero", "reject-negative" })
        {
            events.enter = events.stay = events.exit = events.contacts = 0; events.separation = 0;
            switch (stage)
            {
                case "increase": case "restore": collider.contactOffset = .1f; break;
                case "decrease": collider.contactOffset = .001f; break;
                case "reject-zero": collider.contactOffset = 0; break;
                case "reject-negative": collider.contactOffset = -.1f; break;
            }
            Physics.Simulate(Dt);
            Record("collider/live/" + stage + "/events", events.enter,events.stay,events.exit,events.contacts);
            Record("collider/live/" + stage + "/motion", rb.position.x,rb.linearVelocity.x,events.separation,collider.contactOffset);
        }
        Clean(); initial.Apply();
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

public sealed class DefaultsContactEvents : MonoBehaviour
{
    public int enter, stay, exit, contacts;
    public float separation;
    void Read(Collision c)
    {
        contacts += c.contactCount;
        foreach (var contact in c.contacts) separation += contact.separation;
    }
    void OnCollisionEnter(Collision c) { enter++; Read(c); }
    void OnCollisionStay(Collision c) { stay++; Read(c); }
    void OnCollisionExit(Collision c) { exit++; Read(c); }
}
