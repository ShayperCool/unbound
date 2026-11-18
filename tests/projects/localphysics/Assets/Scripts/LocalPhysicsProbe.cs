using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Scenes with their own (local) 3D and 2D physics scenes in Play mode: what is simulated by the
// automatic simulation and by PhysicsScene(2D).Simulate, what the static and per-scene queries see,
// contact callbacks during a local step, objects made in or moved between scenes, and unloading.
// Each case has a control where the default physics scene would give another answer.
public class LocalPhysicsProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("LocalPhysicsProbe").AddComponent<LocalPhysicsProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    readonly List<string> logs = new List<string>();
    internal static readonly List<string> Events = new List<string>();
    internal static string Phase = "";
    Scene main;

    static string F(float f) { string s = f.ToString("F4", CultureInfo.InvariantCulture); return s == "-0.0000" ? "0.0000" : s; }
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    static string V(Vector2 v) => F(v.x) + "," + F(v.y);

    void Put(string key, object value)
    {
        string text = value == null ? "<null>"
            : value is float f ? F(f)
            : value is Vector3 v3 ? V(v3)
            : value is Vector2 v2 ? V(v2)
            : value is bool b ? (b ? "true" : "false")
            : Convert.ToString(value, CultureInfo.InvariantCulture);
        output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = text }));
    }

    void OnLog(string message, string stack, LogType type)
    {
        int end = message.IndexOf('\n');
        logs.Add(type + ": " + (end < 0 ? message : message.Substring(0, end)));
    }

    // The messages logged since the previous call, one row each, and their count.
    void Logs(string key)
    {
        Put(key + ".logs", logs.Count);
        for (int i = 0; i < logs.Count; i++) Put(key + ".log" + i, logs[i]);
        logs.Clear();
    }

    void EventsRow(string key)
    {
        Put(key, Events.Count == 0 ? "none" : string.Join(";", Events));
        Events.Clear();
    }

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("LOCAL_PHYSICS_OUT");
        if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        Application.logMessageReceived += OnLog;
        bool ok = true;
        var run = Run();
        while (true)
        {
            object current;
            try
            {
                if (!run.MoveNext()) break;
                current = run.Current;
            }
            catch (Exception e)
            {
                Application.logMessageReceived -= OnLog;
                Debug.LogException(e);
                ok = false;
                break;
            }
            yield return current;
        }
        Application.logMessageReceived -= OnLog;
        if (ok) Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    IEnumerator Run()
    {
        yield return null;
        main = SceneManager.GetActiveScene();
        Put("settings.mode3d", Physics.simulationMode);
        Put("settings.mode2d", Physics2D.simulationMode);
        Logs("settings");
        foreach (var step in Each(Run3D())) yield return step;
        foreach (var step in Each(Run2D())) yield return step;
        foreach (var step in Each(RunBoth())) yield return step;
        foreach (var step in Each(RunLoaded())) yield return step;
    }

    static IEnumerable<object> Each(IEnumerator steps)
    {
        while (steps.MoveNext()) yield return steps.Current;
    }

    static IEnumerator Fixed(int count)
    {
        for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
    }

    // ------------------------------------------------------------------ 3D

    static Rigidbody Body3(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.AddComponent<BoxCollider>();
        go.AddComponent<Hits>();
        return go.AddComponent<Rigidbody>();
    }

    static GameObject Static3(string name, Vector3 position, Vector3 size, bool trigger = false)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        box.isTrigger = trigger;
        return go;
    }

    static string Hit(bool hit, RaycastHit info) => hit ? info.collider.name + "@" + F(info.distance) : "none";

    IEnumerator Run3D()
    {
        var scene = SceneManager.CreateScene("Local3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var ps = scene.GetPhysicsScene();
        Put("p3.create.distinct", ps != Physics.defaultPhysicsScene);
        Put("p3.create.valid", ps.IsValid());
        Put("p3.create.empty", ps.IsEmpty());
        Put("p3.create.same", scene.GetPhysicsScene() == ps);
        Put("p3.create.default2d", scene.GetPhysicsScene2D() == Physics2D.defaultPhysicsScene);
        Put("p3.create.mainScene", main.GetPhysicsScene() == Physics.defaultPhysicsScene);
        Logs("p3.create");

        // A static collider alone in a local scene.
        var lone = SceneManager.CreateScene("Lone3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var loneScene = lone.GetPhysicsScene();
        var loneStatic = Static3("loneStatic", new Vector3(0, -50, 0), Vector3.one);
        SceneManager.MoveGameObjectToScene(loneStatic, lone);
        Put("p3.lone.empty", loneScene.IsEmpty());
        Logs("p3.lone");

        // A local and a main body overlap at the same place: in one world they would push apart.
        var local = Body3("local", new Vector3(0, 5, 0));
        SceneManager.MoveGameObjectToScene(local.gameObject, scene);
        var mainBody = Body3("main", new Vector3(0, 5, 0));
        Put("p3.move.scene", local.gameObject.scene.name);
        Put("p3.move.empty", ps.IsEmpty());
        Logs("p3.move");

        Phase = "p3.auto";
        foreach (var step in Each(Fixed(10))) yield return step;
        Put("p3.auto.local", local.position);
        Put("p3.auto.localVelocity", local.linearVelocity);
        Put("p3.auto.mainFell", mainBody.position.y < 4.99f);
        Put("p3.auto.mainX", mainBody.position.x);
        Put("p3.auto.mainZ", mainBody.position.z);
        EventsRow("p3.auto.events");
        Logs("p3.auto");

        Phase = "p3.sim";
        float mainY = mainBody.position.y;
        for (int i = 0; i < 5; i++) ps.Simulate(0.02f);
        Put("p3.sim.local", local.position);
        Put("p3.sim.localTransform", local.transform.position);
        Put("p3.sim.localVelocity", local.linearVelocity);
        Put("p3.sim.mainUnchanged", mainBody.position.y == mainY);
        EventsRow("p3.sim.events");
        Logs("p3.sim");

        Phase = "p3.defaultSim";
        var before = local.position;
        mainY = mainBody.position.y;
        Physics.defaultPhysicsScene.Simulate(0.02f);
        Put("p3.defaultSim.localUnchanged", local.position == before);
        Put("p3.defaultSim.mainMoved", mainBody.position.y != mainY);
        Logs("p3.defaultSim");

        // Queries: a local floor at x = 20, a main floor at x = -20; walls for the sweep.
        var floorLocal = Static3("floorLocal", new Vector3(20, 0, 0), new Vector3(4, 1, 4));
        SceneManager.MoveGameObjectToScene(floorLocal, scene);
        Static3("floorMain", new Vector3(-20, 0, 0), new Vector3(4, 1, 4));
        var wallLocal = Static3("wallLocal", new Vector3(10, local.position.y, 0), new Vector3(1, 4, 4));
        SceneManager.MoveGameObjectToScene(wallLocal, scene);
        Static3("wallMain", new Vector3(5, local.position.y, 0), new Vector3(1, 4, 4));
        Physics.SyncTransforms();
        Put("p3.query.staticOverLocal", Hit(Physics.Raycast(new Vector3(20, 10, 0), Vector3.down, out var a, 100), a));
        Put("p3.query.staticOverMain", Hit(Physics.Raycast(new Vector3(-20, 10, 0), Vector3.down, out var b, 100), b));
        Put("p3.query.sceneOverLocal", Hit(ps.Raycast(new Vector3(20, 10, 0), Vector3.down, out var c, 100), c));
        Put("p3.query.sceneOverMain", Hit(ps.Raycast(new Vector3(-20, 10, 0), Vector3.down, out var d, 100), d));
        Put("p3.query.defaultOverLocal", Hit(Physics.defaultPhysicsScene.Raycast(new Vector3(20, 10, 0), Vector3.down, out var e, 100), e));
        Put("p3.query.staticOverlap", Physics.OverlapSphere(new Vector3(20, 0, 0), 1).Length);
        var found = new Collider[8];
        Put("p3.query.sceneOverlap", ps.OverlapSphere(new Vector3(20, 0, 0), 1, found, ~0, QueryTriggerInteraction.UseGlobal));
        Put("p3.query.sceneOverlapName", found[0] == null ? "none" : found[0].name);
        Put("p3.query.sweep", Hit(local.SweepTest(Vector3.right, out var f, 100), f));
        Put("p3.query.mainSweep", Hit(mainBody.SweepTest(Vector3.right, out var g, 100), g));
        Logs("p3.query");

        // Contacts in a local step: a body dropped through a trigger onto the local floor.
        var dropper = Body3("dropper", new Vector3(20, 1.6f, 0));
        SceneManager.MoveGameObjectToScene(dropper.gameObject, scene);
        var zone = Static3("zone", new Vector3(20, 0.8f, 0), new Vector3(2, 0.2f, 2), trigger: true);
        SceneManager.MoveGameObjectToScene(zone, scene);
        for (int i = 0; i < 30; i++)
        {
            Phase = "p3.contact." + i;
            ps.Simulate(0.02f);
        }
        Put("p3.contact.dropper", dropper.position);
        EventsRow("p3.contact.events");
        Logs("p3.contact");

        // A GameObject made while the local scene is active.
        SceneManager.SetActiveScene(scene);
        var made = Body3("made", new Vector3(40, 5, 0));
        var copy = Instantiate(mainBody.gameObject, new Vector3(45, 5, 0), Quaternion.identity);
        copy.name = "copy";
        SceneManager.SetActiveScene(main);
        var copyBody = copy.GetComponent<Rigidbody>();
        copyBody.linearVelocity = Vector3.zero;
        Put("p3.active.scene", made.gameObject.scene.name);
        Put("p3.active.copyScene", copy.scene.name);
        var madeAt = made.position;
        var copyAt = copyBody.position;
        Phase = "p3.active";
        foreach (var step in Each(Fixed(5))) yield return step;
        Put("p3.active.autoUnchanged", made.position == madeAt);
        Put("p3.active.copyAutoUnchanged", copyBody.position == copyAt);
        ps.Simulate(0.02f);
        Put("p3.active.simMoved", made.position != madeAt);
        Put("p3.active.copySimMoved", copyBody.position != copyAt);
        Logs("p3.active");

        // Moved back into the main scene: the automatic simulation takes it.
        Put("p3.back.velocityBefore", local.linearVelocity);
        SceneManager.MoveGameObjectToScene(local.gameObject, main);
        Put("p3.back.velocity", local.linearVelocity);
        var back = local.position;
        Phase = "p3.back";
        foreach (var step in Each(Fixed(5))) yield return step;
        Put("p3.back.autoMoved", local.position != back);
        Logs("p3.back");

        // Gravity: Physics.gravity is the local scene's too.
        var heavy = Body3("heavy", new Vector3(60, 5, 0));
        SceneManager.MoveGameObjectToScene(heavy.gameObject, scene);
        var gravity = Physics.gravity;
        Physics.gravity = new Vector3(0, -20, 0);
        ps.Simulate(0.02f);
        Physics.gravity = gravity;
        Put("p3.gravity.velocity", heavy.linearVelocity);
        Logs("p3.gravity");

        // A moving main body moved into the local scene.
        var mover = Body3("mover", new Vector3(70, 5, 0));
        mover.useGravity = false;
        mover.linearVelocity = new Vector3(3, 0, 0);
        mover.angularVelocity = new Vector3(0, 2, 0);
        SceneManager.MoveGameObjectToScene(mover.gameObject, scene);
        Put("p3.into.velocity", mover.linearVelocity);
        Put("p3.into.angularVelocity", mover.angularVelocity);
        ps.Simulate(0.02f);
        Put("p3.into.position", mover.position);
        Logs("p3.into");

        // A joint between a local body and a main body.
        var jointLocal = Body3("jointLocal", new Vector3(80, 5, 0));
        SceneManager.MoveGameObjectToScene(jointLocal.gameObject, scene);
        var jointMain = Body3("jointMain", new Vector3(82, 5, 0));
        jointMain.isKinematic = true;
        var joint = jointLocal.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = jointMain;
        Phase = "p3.joint";
        foreach (var step in Each(Fixed(1))) yield return step;
        for (int i = 0; i < 5; i++) ps.Simulate(0.02f);
        Put("p3.joint.local", jointLocal.position);
        Put("p3.joint.main", jointMain.position);
        Put("p3.joint.connected", joint.connectedBody == null ? "<null>" : joint.connectedBody.name);
        Logs("p3.joint");

        // Unloading the scene ends its physics scene.
        var unload = SceneManager.UnloadSceneAsync(scene);
        while (!unload.isDone) yield return null;
        Put("p3.unload.valid", ps.IsValid());
        Put("p3.unload.empty", Try(() => ps.IsEmpty()));
        Put("p3.unload.simulate", Try(() => { ps.Simulate(0.02f); return "ran"; }));
        Put("p3.unload.raycast", Try(() => ps.Raycast(new Vector3(20, 10, 0), Vector3.down, 100)));
        Put("p3.unload.equalsDefault", ps == Physics.defaultPhysicsScene);
        Put("p3.unload.sceneValid", scene.IsValid());
        Logs("p3.unload");
        var unloadLone = SceneManager.UnloadSceneAsync(lone);
        while (!unloadLone.isDone) yield return null;
        foreach (var name in new[] { "main", "floorMain", "wallMain", "local", "jointMain" })
            Destroy(GameObject.Find(name));
        yield return null;
        Logs("p3.end");
    }

    static string Try<T>(Func<T> read)
    {
        try
        {
            object value = read();
            return value is bool b ? (b ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) { return "threw " + e.GetType().Name; }
    }

    // ------------------------------------------------------------------ 2D

    static Rigidbody2D Body2(string name, Vector2 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.AddComponent<BoxCollider2D>();
        go.AddComponent<Hits>();
        return go.AddComponent<Rigidbody2D>();
    }

    static GameObject Static2(string name, Vector2 position, Vector2 size, bool trigger = false)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var box = go.AddComponent<BoxCollider2D>();
        box.size = size;
        box.isTrigger = trigger;
        return go;
    }

    static string Hit(RaycastHit2D hit) => hit.collider == null ? "none" : hit.collider.name + "@" + F(hit.distance);

    IEnumerator Run2D()
    {
        var scene = SceneManager.CreateScene("Local2D", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var ps = scene.GetPhysicsScene2D();
        Put("p2.create.distinct", ps != Physics2D.defaultPhysicsScene);
        Put("p2.create.valid", ps.IsValid());
        Put("p2.create.empty", ps.IsEmpty());
        Put("p2.create.same", scene.GetPhysicsScene2D() == ps);
        Put("p2.create.default3d", scene.GetPhysicsScene() == Physics.defaultPhysicsScene);
        Put("p2.create.mainScene", main.GetPhysicsScene2D() == Physics2D.defaultPhysicsScene);
        Put("p2.create.defaultValid", Physics2D.defaultPhysicsScene.IsValid());
        Logs("p2.create");

        var lone = SceneManager.CreateScene("Lone2D", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var loneScene = lone.GetPhysicsScene2D();
        var loneStatic = Static2("loneStatic2", new Vector2(0, -50), Vector2.one);
        SceneManager.MoveGameObjectToScene(loneStatic, lone);
        Put("p2.lone.empty", loneScene.IsEmpty());
        Logs("p2.lone");

        var local = Body2("local2", new Vector2(0, 5));
        SceneManager.MoveGameObjectToScene(local.gameObject, scene);
        var mainBody = Body2("main2", new Vector2(0, 5));
        Put("p2.move.scene", local.gameObject.scene.name);
        Put("p2.move.empty", ps.IsEmpty());
        Put("p2.move.touching", local.GetComponent<Collider2D>().IsTouching(mainBody.GetComponent<Collider2D>()));
        Logs("p2.move");

        Phase = "p2.auto";
        foreach (var step in Each(Fixed(10))) yield return step;
        Put("p2.auto.local", local.position);
        Put("p2.auto.localTransform", (Vector2)local.transform.position);
        Put("p2.auto.localVelocity", local.linearVelocity);
        Put("p2.auto.mainFell", mainBody.position.y < 4.99f);
        Put("p2.auto.mainX", mainBody.position.x);
        Put("p2.auto.touching", local.GetComponent<Collider2D>().IsTouching(mainBody.GetComponent<Collider2D>()));
        EventsRow("p2.auto.events");
        Logs("p2.auto");

        Phase = "p2.sim";
        float mainY = mainBody.position.y;
        bool all = true;
        for (int i = 0; i < 5; i++) all &= ps.Simulate(0.02f);
        Put("p2.sim.result", all);
        Put("p2.sim.local", local.position);
        Put("p2.sim.localTransform", (Vector2)local.transform.position);
        Put("p2.sim.localVelocity", local.linearVelocity);
        Put("p2.sim.mainUnchanged", mainBody.position.y == mainY);
        EventsRow("p2.sim.events");
        Logs("p2.sim");

        Phase = "p2.defaultSim";
        var before = local.position;
        mainY = mainBody.position.y;
        Put("p2.defaultSim.result", Physics2D.Simulate(0.02f));
        Put("p2.defaultSim.localUnchanged", local.position == before);
        Put("p2.defaultSim.mainMoved", mainBody.position.y != mainY);
        Logs("p2.defaultSim");

        var floorLocal = Static2("floorLocal2", new Vector2(20, 0), new Vector2(4, 1));
        SceneManager.MoveGameObjectToScene(floorLocal, scene);
        Static2("floorMain2", new Vector2(-20, 0), new Vector2(4, 1));
        var wallLocal = Static2("wallLocal2", new Vector2(10, local.position.y), new Vector2(1, 4));
        SceneManager.MoveGameObjectToScene(wallLocal, scene);
        Static2("wallMain2", new Vector2(5, local.position.y), new Vector2(1, 4));
        Physics2D.SyncTransforms();
        Put("p2.query.staticOverLocal", Hit(Physics2D.Raycast(new Vector2(20, 10), Vector2.down, 100)));
        Put("p2.query.staticOverMain", Hit(Physics2D.Raycast(new Vector2(-20, 10), Vector2.down, 100)));
        Put("p2.query.sceneOverLocal", Hit(ps.Raycast(new Vector2(20, 10), Vector2.down, 100)));
        Put("p2.query.sceneOverMain", Hit(ps.Raycast(new Vector2(-20, 10), Vector2.down, 100)));
        Put("p2.query.defaultOverLocal", Hit(Physics2D.defaultPhysicsScene.Raycast(new Vector2(20, 10), Vector2.down, 100)));
        var point = Physics2D.OverlapPoint(new Vector2(20, 0));
        Put("p2.query.staticPoint", point == null ? "none" : point.name);
        var scenePoint = ps.OverlapPoint(new Vector2(20, 0));
        Put("p2.query.scenePoint", scenePoint == null ? "none" : scenePoint.name);
        var hits = new RaycastHit2D[4];
        Put("p2.query.bodyCast", local.Cast(Vector2.right, hits, 100) > 0 ? Hit(hits[0]) : "none");
        hits = new RaycastHit2D[4];
        Put("p2.query.colliderCast", local.GetComponent<Collider2D>().Cast(Vector2.right, hits, 100) > 0 ? Hit(hits[0]) : "none");
        hits = new RaycastHit2D[4];
        Put("p2.query.mainCast", mainBody.Cast(Vector2.right, hits, 100) > 0 ? Hit(hits[0]) : "none");
        var overlaps = new List<Collider2D>();
        Put("p2.query.overlapCollider", floorLocal.GetComponent<Collider2D>().Overlap(new ContactFilter2D().NoFilter(), overlaps));
        Logs("p2.query");

        var dropper = Body2("dropper2", new Vector2(20, 1.6f));
        SceneManager.MoveGameObjectToScene(dropper.gameObject, scene);
        var zone = Static2("zone2", new Vector2(20, 0.8f), new Vector2(2, 0.2f), trigger: true);
        SceneManager.MoveGameObjectToScene(zone, scene);
        for (int i = 0; i < 30; i++)
        {
            Phase = "p2.contact." + i;
            ps.Simulate(0.02f);
        }
        Put("p2.contact.dropper", dropper.position);
        Put("p2.contact.touching", dropper.IsTouching(floorLocal.GetComponent<Collider2D>()));
        var contacts = new List<ContactPoint2D>();
        Put("p2.contact.contacts", dropper.GetContacts(contacts));
        EventsRow("p2.contact.events");
        Logs("p2.contact");

        SceneManager.SetActiveScene(scene);
        var made = Body2("made2", new Vector2(40, 5));
        var copy = Instantiate(mainBody.gameObject, new Vector3(45, 5, 0), Quaternion.identity);
        copy.name = "copy2";
        SceneManager.SetActiveScene(main);
        var copyBody = copy.GetComponent<Rigidbody2D>();
        copyBody.linearVelocity = Vector2.zero;
        Put("p2.active.scene", made.gameObject.scene.name);
        Put("p2.active.copyScene", copy.scene.name);
        var madeAt = made.position;
        var copyAt = copyBody.position;
        Phase = "p2.active";
        foreach (var step in Each(Fixed(5))) yield return step;
        Put("p2.active.autoUnchanged", made.position == madeAt);
        Put("p2.active.copyAutoUnchanged", copyBody.position == copyAt);
        ps.Simulate(0.02f);
        Put("p2.active.simMoved", made.position != madeAt);
        Put("p2.active.copySimMoved", copyBody.position != copyAt);
        Logs("p2.active");

        Put("p2.back.velocityBefore", local.linearVelocity);
        SceneManager.MoveGameObjectToScene(local.gameObject, main);
        Put("p2.back.velocity", local.linearVelocity);
        var back = local.position;
        Phase = "p2.back";
        foreach (var step in Each(Fixed(5))) yield return step;
        Put("p2.back.autoMoved", local.position != back);
        Logs("p2.back");

        var heavy = Body2("heavy2", new Vector2(60, 5));
        SceneManager.MoveGameObjectToScene(heavy.gameObject, scene);
        var gravity = Physics2D.gravity;
        Physics2D.gravity = new Vector2(0, -20);
        ps.Simulate(0.02f);
        Physics2D.gravity = gravity;
        Put("p2.gravity.velocity", heavy.linearVelocity);
        Logs("p2.gravity");

        var mover = Body2("mover2", new Vector2(70, 5));
        mover.gravityScale = 0;
        mover.linearVelocity = new Vector2(3, 0);
        mover.angularVelocity = 90;
        SceneManager.MoveGameObjectToScene(mover.gameObject, scene);
        Put("p2.into.velocity", mover.linearVelocity);
        Put("p2.into.angularVelocity", mover.angularVelocity);
        ps.Simulate(0.02f);
        Put("p2.into.position", mover.position);
        Logs("p2.into");

        var jointLocal = Body2("jointLocal2", new Vector2(80, 5));
        SceneManager.MoveGameObjectToScene(jointLocal.gameObject, scene);
        var jointMain = Body2("jointMain2", new Vector2(82, 5));
        jointMain.bodyType = RigidbodyType2D.Kinematic;
        var joint = jointLocal.gameObject.AddComponent<FixedJoint2D>();
        joint.connectedBody = jointMain;
        Phase = "p2.joint";
        foreach (var step in Each(Fixed(1))) yield return step;
        for (int i = 0; i < 5; i++) ps.Simulate(0.02f);
        Put("p2.joint.local", jointLocal.position);
        Put("p2.joint.main", jointMain.position);
        Put("p2.joint.connected", joint.connectedBody == null ? "<null>" : joint.connectedBody.name);
        Logs("p2.joint");

        var unload = SceneManager.UnloadSceneAsync(scene);
        while (!unload.isDone) yield return null;
        Put("p2.unload.valid", ps.IsValid());
        Put("p2.unload.empty", Try(() => ps.IsEmpty()));
        Put("p2.unload.simulate", Try(() => ps.Simulate(0.02f)));
        Put("p2.unload.raycast", Try(() => Hit(ps.Raycast(new Vector2(20, 10), Vector2.down, 100))));
        Put("p2.unload.equalsDefault", ps == Physics2D.defaultPhysicsScene);
        Logs("p2.unload");
        var unloadLone = SceneManager.UnloadSceneAsync(lone);
        while (!unloadLone.isDone) yield return null;
        foreach (var name in new[] { "main2", "floorMain2", "wallMain2", "local2", "jointMain2" })
            Destroy(GameObject.Find(name));
        yield return null;
        Logs("p2.end");
    }

    // ------------------------------------------------------------------ both, loaded

    IEnumerator RunBoth()
    {
        var scene = SceneManager.CreateScene("Both", new CreateSceneParameters(LocalPhysicsMode.Physics2D | LocalPhysicsMode.Physics3D));
        var both3 = scene.GetPhysicsScene();
        var both2 = scene.GetPhysicsScene2D();
        Put("both.distinct3d", both3 != Physics.defaultPhysicsScene);
        Put("both.distinct2d", both2 != Physics2D.defaultPhysicsScene);
        var none = SceneManager.CreateScene("None", new CreateSceneParameters(LocalPhysicsMode.None));
        Put("both.none3d", none.GetPhysicsScene() == Physics.defaultPhysicsScene);
        Put("both.none2d", none.GetPhysicsScene2D() == Physics2D.defaultPhysicsScene);
        var first = SceneManager.UnloadSceneAsync(scene);
        while (!first.isDone) yield return null;
        Put("both.unloaded3d", both3.IsValid());
        Put("both.unloaded2d", both2.IsValid());
        var second = SceneManager.UnloadSceneAsync(none);
        while (!second.isDone) yield return null;
        Logs("both");
    }

    // The Sim scene holds a 3D floor and a 2D floor at the origin.
    IEnumerator RunLoaded()
    {
        foreach (var step in Each(RunLoadedOnly("loaded3", LocalPhysicsMode.Physics3D))) yield return step;
        foreach (var step in Each(RunLoadedOnly("loaded2", LocalPhysicsMode.Physics2D))) yield return step;
        var load = SceneManager.LoadSceneAsync("Sim", new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D | LocalPhysicsMode.Physics3D));
        while (!load.isDone) yield return null;
        var scene = SceneManager.GetSceneByName("Sim");
        var ps = scene.GetPhysicsScene();
        var ps2 = scene.GetPhysicsScene2D();
        Put("loaded.distinct3d", ps != Physics.defaultPhysicsScene);
        Put("loaded.distinct2d", ps2 != Physics2D.defaultPhysicsScene);
        Put("loaded.empty3d", ps.IsEmpty());
        Put("loaded.empty2d", ps2.IsEmpty());
        Put("loaded.static3d", Hit(Physics.Raycast(new Vector3(0, 10, 0), Vector3.down, out var a, 100), a));
        Put("loaded.scene3d", Hit(ps.Raycast(new Vector3(0, 10, 0), Vector3.down, out var b, 100), b));
        Put("loaded.static2d", Hit(Physics2D.Raycast(new Vector2(0, 10), Vector2.down, 100)));
        Put("loaded.scene2d", Hit(ps2.Raycast(new Vector2(0, 10), Vector2.down, 100)));
        var unload = SceneManager.UnloadSceneAsync(scene);
        while (!unload.isDone) yield return null;
        Put("loaded.unloaded3d", ps.IsValid());
        Put("loaded.unloaded2d", ps2.IsValid());
        Put("loaded.unloadedEmpty3d", Try(() => ps.IsEmpty()));
        Put("loaded.unloadedEmpty2d", Try(() => ps2.IsEmpty()));
        Put("loaded.unloadedScene", scene.IsValid());
        for (int i = 0; i < 3; i++) yield return null;
        Put("loaded.later3d", ps.IsValid());
        Put("loaded.later2d", ps2.IsValid());
        Logs("loaded");
    }

    // A load with one local physics scene: the other stays the default one.
    IEnumerator RunLoadedOnly(string key, LocalPhysicsMode mode)
    {
        var load = SceneManager.LoadSceneAsync("Sim", new LoadSceneParameters(LoadSceneMode.Additive, mode));
        while (!load.isDone) yield return null;
        var scene = SceneManager.GetSceneByName("Sim");
        var ps = scene.GetPhysicsScene();
        var ps2 = scene.GetPhysicsScene2D();
        Put(key + ".distinct3d", ps != Physics.defaultPhysicsScene);
        Put(key + ".distinct2d", ps2 != Physics2D.defaultPhysicsScene);
        Put(key + ".static3d", Hit(Physics.Raycast(new Vector3(0, 10, 0), Vector3.down, out var a, 100), a));
        Put(key + ".static2d", Hit(Physics2D.Raycast(new Vector2(0, 10), Vector2.down, 100)));
        var unload = SceneManager.UnloadSceneAsync(scene);
        while (!unload.isDone) yield return null;
        Put(key + ".unloaded3d", ps.IsValid());
        Put(key + ".unloaded2d", ps2.IsValid());
        Put(key + ".unloadedDefault3d", ps == Physics.defaultPhysicsScene);
        Put(key + ".unloadedDefault2d", ps2 == Physics2D.defaultPhysicsScene);
        Logs(key);
    }
}
