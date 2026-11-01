using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Builds particle systems in code, each exercising one module with fixed (non-random) values,
/// steps them with <see cref="ParticleSystem.Simulate(float, bool, bool, bool)"/> and records the
/// system's state and every particle after each step, as JSON lines in <c>PARTICLE_TRIGGER_OUT</c>.
/// Trigger controls record the Editor physics scene before and after explicit transform sync.
/// </summary>
public class TriggerProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Events = new List<string>();
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[4096];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("TriggerProbe").AddComponent<TriggerProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string C(Color32 c) => "[" + c.r + "," + c.g + "," + c.b + "," + c.a + "]";
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    /// <summary>A system on an inactive object, stopped and cleared, with a fixed seed, no
    /// shape and no play on awake; <paramref name="configure"/> sets the rest.</summary>
    static ParticleSystem Make(string name, Action<ParticleSystem> configure, Vector3 position = default, Transform parent = null)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 12345;
        var main = ps.main;
        main.playOnAwake = false;
        main.startLifetime = 1.013f;
        main.startSpeed = 2f;
        main.startSize = 1f;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 10f;
        configure(ps);
        go.SetActive(true);
        return ps;
    }

    static void RecordState(string scenario, string step, ParticleSystem ps)
    {
        FlushEvents(scenario, step + " before");
        int count = ps.particleCount;
        if (s_Buffer.Length < count) s_Buffer = new ParticleSystem.Particle[count * 2];
        // Unity leaves the fields a system does not use as they were in the buffer.
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        s_Out.Append("{\"scenario\":").Append(Q(scenario)).Append(",\"step\":").Append(Q(step));
        s_Out.Append(",\"count\":").Append(count);
        s_Out.Append(",\"time\":").Append(F(ps.time));
        s_Out.Append(",\"playing\":").Append(ps.isPlaying ? "true" : "false");
        s_Out.Append(",\"emitting\":").Append(ps.isEmitting ? "true" : "false");
        s_Out.Append(",\"stopped\":").Append(ps.isStopped ? "true" : "false");
        s_Out.Append(",\"paused\":").Append(ps.isPaused ? "true" : "false");
        s_Out.Append(",\"alive\":").Append(ps.IsAlive(true) ? "true" : "false");
        s_Out.Append(",\"particles\":[");
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            if (i > 0) s_Out.Append(',');
            s_Out.Append("{\"pos\":").Append(V(p.position));
            s_Out.Append(",\"vel\":").Append(V(p.velocity));
            s_Out.Append(",\"totalVel\":").Append(V(p.totalVelocity));
            s_Out.Append(",\"life\":").Append(F(p.remainingLifetime));
            s_Out.Append(",\"startLife\":").Append(F(p.startLifetime));
            s_Out.Append(",\"startSize\":").Append(V(p.startSize3D));
            s_Out.Append(",\"size\":").Append(V(p.GetCurrentSize3D(ps)));
            s_Out.Append(",\"startColor\":").Append(C(p.startColor));
            s_Out.Append(",\"color\":").Append(C(p.GetCurrentColor(ps)));
            s_Out.Append(",\"rot\":").Append(V(p.rotation3D));
            s_Out.Append(",\"angVel\":").Append(V(p.angularVelocity3D));
            s_Out.Append('}');
        }
        s_Out.Append("]}\n");
    }

    static void RecordValue(string scenario, string step, string json)
    {
        s_Out.Append("{\"scenario\":").Append(Q(scenario)).Append(",\"step\":").Append(Q(step)).Append(",\"value\":").Append(json).Append("}\n");
    }

    /// <summary>Simulates <paramref name="steps"/> steps of <paramref name="dt"/> (the first one
    /// restarting the system), recording after each; <paramref name="before"/> runs before a step.</summary>
    static void Step(string scenario, ParticleSystem ps, float dt, int steps, Action<int> before = null, bool fixedTimeStep = false, bool restart = true)
    {
        for (int i = 0; i < steps; i++)
        {
            before?.Invoke(i);
            ps.Simulate(dt, true, restart && i == 0, fixedTimeStep);
            RecordState(scenario, i.ToString(CultureInfo.InvariantCulture), ps);
            FlushEvents(scenario, i.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Records what was logged since the last flush, under the scenario that logged it.</summary>
    static void FlushEvents(string scenario, string step)
    {
        if (s_Events.Count == 0) return;
        var sb = new StringBuilder("[");
        for (int i = 0; i < s_Events.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(s_Events[i]));
        RecordValue(scenario + " log", step, sb.Append(']').ToString());
        s_Events.Clear();
    }

    static void Done(ParticleSystem ps) => DestroyImmediate(ps.transform.root.gameObject);

    void Start()
    {
        foreach (bool changeProperty in new[] { false, true })
        {
            string name = changeProperty ? "query_only_trigger" : "query_only_move";
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var collider = sphere.GetComponent<Collider>();
            sphere.transform.position = new Vector3(0f, 0f, 2.5f);
            if (changeProperty) collider.isTrigger = true;
            RecordQuery(name, "beforeSync", collider, 0.2f);
            Physics.SyncTransforms();
            RecordQuery(name, "afterSync", collider, 0.2f);
            DestroyImmediate(sphere);
        }
        ShapeRebuildControl(false);
        ShapeRebuildControl(true);
        foreach (string order in new[] { "original", "triggerFirst", "synced" })
        {
            RunCase("trigger_kill_inside", 1.5f, 1f, 30, ParticleSystemOverlapAction.Kill, ParticleSystemOverlapAction.Ignore, order);
            RunCase("trigger_kill_far", 2.5f, 0.4f, 20, ParticleSystemOverlapAction.Kill, ParticleSystemOverlapAction.Ignore, order);
            RunCase("trigger_kill_outside", 2.5f, 0.4f, 20, ParticleSystemOverlapAction.Ignore, ParticleSystemOverlapAction.Kill, order);
        }
        RunCase("trigger_callback_inside", 2.5f, 0.4f, 20, ParticleSystemOverlapAction.Callback, ParticleSystemOverlapAction.Ignore, "original");
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_TRIGGER_OUT"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void RunCase(string name, float z, float size, int steps, ParticleSystemOverlapAction inside,
        ParticleSystemOverlapAction outside, string order)
    {
        string scenario = name + (order == "original" ? "" : "/" + order);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = scenario + "_sphere";
        var collider = sphere.GetComponent<Collider>();
        if (order == "triggerFirst") collider.isTrigger = true;
        sphere.transform.position = new Vector3(0f, 0f, z);
        collider.isTrigger = true;
        if (order == "synced") Physics.SyncTransforms();
        var ps = Make(scenario, s =>
        {
            var m = s.main; m.startSpeed = 2f; m.startLifetime = 2.013f; m.startSize = size;
            var t = s.trigger; t.enabled = true; t.AddCollider(collider);
            t.inside = inside; t.outside = outside;
            t.enter = ParticleSystemOverlapAction.Ignore; t.exit = ParticleSystemOverlapAction.Ignore;
        });
        var inner = new List<ParticleSystem.Particle>();
        for (int i = 0; i < steps; i++)
        {
            ps.Simulate(0.05f, true, i == 0, false);
            RecordState(scenario, i.ToString(CultureInfo.InvariantCulture), ps);
            if (name != "trigger_kill_inside")
                RecordValue(scenario + " inside", i.ToString(CultureInfo.InvariantCulture),
                    ps.GetTriggerParticles(ParticleSystemTriggerEventType.Inside, inner).ToString(CultureInfo.InvariantCulture));
        }
        // These public queries run after the original histories, so they cannot flush the
        // physics scene before those controls have completed.
        RecordQuery(scenario, "beforeSync", collider, size * 0.5f);
        Physics.SyncTransforms();
        RecordQuery(scenario, "afterSync", collider, size * 0.5f);
        Done(ps);
        DestroyImmediate(sphere);
    }

    // Keep geometry edits distinct from pending transform changes, both on a static actor
    // and in a child collider's cached frame on a Rigidbody.
    static void ShapeRebuildControl(bool attached)
    {
        var parent = new GameObject("shape-parent");
        if (attached) parent.AddComponent<Rigidbody>().isKinematic = true;
        var child = new GameObject("shape-child");
        child.transform.SetParent(parent.transform, false);
        child.transform.localPosition = new Vector3(1f, 0f, 0f);
        var sphere = child.AddComponent<SphereCollider>();
        Physics.SyncTransforms();
        string scenario = attached ? "shape_rebuild_attached" : "shape_rebuild_static";
        RecordShape("initial");
        parent.transform.position = new Vector3(0f, 0f, 2.5f);
        child.transform.localPosition = new Vector3(2f, 0f, 0f);
        child.transform.localScale = new Vector3(2f, 2f, 2f);
        sphere.isTrigger = true;
        RecordShape("propertyBeforeSync");
        sphere.radius = 0.75f;
        sphere.center = new Vector3(0.25f, 0f, 0f);
        RecordShape("beforeSync");
        Physics.SyncTransforms();
        RecordShape("afterSync");
        DestroyImmediate(parent);

        void RecordShape(string step)
        {
            var bounds = sphere.bounds;
            RecordValue(scenario, step, "{\"boundsCenter\":" + V(bounds.center)
                + ",\"boundsSize\":" + V(bounds.size) + "}");
        }
    }

    static void RecordQuery(string scenario, string step, Collider collider, float radius)
    {
        var overlaps = new Collider[8];
        int origin = Physics.OverlapSphereNonAlloc(Vector3.zero, radius, overlaps, Physics.AllLayers, QueryTriggerInteraction.Collide);
        bool atOrigin = false;
        for (int i = 0; i < origin; i++) atOrigin |= overlaps[i] == collider;
        int moved = Physics.OverlapSphereNonAlloc(collider.transform.position, radius, overlaps, Physics.AllLayers, QueryTriggerInteraction.Collide);
        bool atMoved = false;
        for (int i = 0; i < moved; i++) atMoved |= overlaps[i] == collider;
        RecordValue(scenario + " query", step,
            "{\"autoSync\":" + (Physics.autoSyncTransforms ? "true" : "false")
            + ",\"transform\":" + V(collider.transform.position)
            + ",\"boundsCenter\":" + V(collider.bounds.center)
            + ",\"atOrigin\":" + (atOrigin ? "true" : "false")
            + ",\"atMoved\":" + (atMoved ? "true" : "false") + "}");
    }
}
