using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what <c>GetTriggerParticles</c> returns for each ParticleSystemTriggerEventType (with
/// and without <c>ColliderData</c>, for each collider query mode), what <c>SetTriggerParticles</c>
/// writes back (whole lists, subsets, kills, outside the callback), and what
/// <c>TriggerSubEmitter</c> emits on a system holding dead particles that carry fading trails.
/// Played frames at <c>Time.captureDeltaTime</c> 0.02. Each record is one JSON line
/// {"key", "value": [numbers]} in <c>PARTICLE_TRIGGER_API_OUT</c>.
/// </summary>
public class TriggerApiProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[256];
    static string s_Path;
    internal static int Frame;
    internal static string Current;
    internal static Component[] Colliders;
    static int s_Warnings, s_Errors;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_TRIGGER_API_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        new GameObject("TriggerApiProbe").AddComponent<TriggerApiProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    internal static void Record(string key, IList<float> values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Count; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Warning) s_Warnings++;
        else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) s_Errors++;
    }

    internal static void AddParticle(List<float> v, ParticleSystem.Particle p)
    {
        v.Add(p.position.x); v.Add(p.position.y); v.Add(p.position.z);
        v.Add(p.velocity.x); v.Add(p.velocity.y); v.Add(p.velocity.z);
        v.Add(p.remainingLifetime); v.Add(p.startSize);
        var c = p.startColor;
        v.Add(c.r); v.Add(c.g); v.Add(c.b); v.Add(c.a);
        v.Add(p.randomSeed & 0xffff); v.Add(p.randomSeed >> 16);
    }

    internal static int ColliderId(Component c)
    {
        if (c == null) return 0;
        for (int i = 0; i < Colliders.Length; i++) if (ReferenceEquals(Colliders[i], c)) return i + 1;
        return -1;
    }

    static void RecordState(string key, ParticleSystem ps)
    {
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { n, s_Warnings, s_Errors };
        for (int i = 0; i < n; i++) AddParticle(v, s_Buffer[i]);
        Record(key, v);
    }

    /// <summary>Box A (x 1, 1 m), sphere B (x 1.3, r 0.4, overlapping A), box C (x 3); trigger colliders.</summary>
    static Component[] MakeColliders()
    {
        var a = GameObject.CreatePrimitive(PrimitiveType.Cube);
        a.name = "A";
        a.transform.position = new Vector3(1f, 0f, 0f);
        var ca = a.GetComponent<BoxCollider>();
        ca.isTrigger = true;
        var b = new GameObject("B");
        b.transform.position = new Vector3(1.3f, 0f, 0f);
        var cb = b.AddComponent<SphereCollider>();
        cb.radius = 0.4f;
        cb.isTrigger = true;
        var c = new GameObject("C");
        c.transform.position = new Vector3(3f, 0f, 0f);
        var cc = c.AddComponent<BoxCollider>();
        cc.isTrigger = true;
        Destroy(a.GetComponent<MeshRenderer>());
        return new Component[] { ca, cb, cc };
    }

    /// <summary>A playing system in world space with no emission of its own; six particles emitted
    /// along +X at 1.5 m/s from x -0.5 at heights 0, 0.1, 0.45, 0.7 and 2 (two at 0, the second
    /// slower), all three colliders in its Trigger module, every event a Callback.</summary>
    static ParticleSystem Make(string name, ParticleSystemColliderQueryMode query, string mode,
        ParticleSystemOverlapAction exit = ParticleSystemOverlapAction.Callback)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 777u;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 10f;
        main.loop = false;
        main.startLifetime = 4f;
        main.startSpeed = 0f;
        main.startSize = 0.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var trigger = ps.trigger;
        trigger.enabled = true;
        for (int i = 0; i < Colliders.Length; i++) trigger.AddCollider(Colliders[i]);
        trigger.inside = ParticleSystemOverlapAction.Callback;
        trigger.outside = ParticleSystemOverlapAction.Callback;
        trigger.enter = ParticleSystemOverlapAction.Callback;
        trigger.exit = exit;
        trigger.colliderQueryMode = query;
        var callback = go.AddComponent<TriggerApiCallback>();
        callback.Mode = mode;
        go.SetActive(true);
        ps.Play();
        float[] heights = { 0f, 0f, 0.1f, 0.45f, 0.7f, 2f };
        for (int i = 0; i < heights.Length; i++)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = new Vector3(-0.5f, heights[i], 0f),
                velocity = new Vector3(i == 1 ? 0.75f : 1.5f, 0f, 0f),
                startColor = new Color32((byte)(40 * i), 255, 255, 255),
            };
            ps.Emit(e, 1);
        }
        return ps;
    }

    IEnumerator Scenario(string key, ParticleSystemColliderQueryMode query, string mode, int frames = 150,
        ParticleSystemOverlapAction exit = ParticleSystemOverlapAction.Callback)
    {
        Current = key;
        s_Warnings = s_Errors = 0;
        var ps = Make(key, query, mode, exit);
        for (int frame = 0; frame < frames; frame++)
        {
            Frame = frame;
            yield return null;
            // Outside the callback: Get returns the last collection; Set is not valid.
            if (mode == "update" && frame % 10 == 5)
            {
                var list = new List<ParticleSystem.Particle>();
                var v = new List<float>();
                foreach (ParticleSystemTriggerEventType type in Enum.GetValues(typeof(ParticleSystemTriggerEventType)))
                {
                    v.Add(ps.GetSafeTriggerParticlesSize(type));
                    v.Add(ps.GetTriggerParticles(type, list));
                    for (int i = 0; i < list.Count; i++) { var p = list[i]; p.startColor = Color.black; list[i] = p; }
                    int errors = s_Errors;
                    try { ps.SetTriggerParticles(type, list); v.Add(0); }
                    catch (Exception) { v.Add(1); }
                    v.Add(s_Errors - errors);
                }
                Record(key + "/update/" + frame, v);
            }
            if (frame % 5 == 0 || frame < 10) RecordState(key + "/state/" + frame, ps);
        }
        Destroy(ps.gameObject);
        // The next scenario starts once the object is gone.
        Current = key + "/gone";
        yield return null;
    }

    /// <summary>A parent whose particles die at 0.3 s and leave their trails to fade
    /// (dieWithParticles off, trails as long as the particles lived), with a Manual sub-emitter (one burst of 2): after the
    /// deaths, TriggerSubEmitter on every particle (carriers included), on a GetParticles list
    /// and on a single carrier copy.</summary>
    IEnumerator Carriers()
    {
        var go = new GameObject("carrier");
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 31u;
        var main = ps.main;
        main.playOnAwake = false;
        main.startLifetime = 0.3f;
        main.startSpeed = 1f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
        var shape = ps.shape;
        shape.enabled = false;
        var trails = ps.trails;
        trails.enabled = true;
        trails.dieWithParticles = false;
        // Trails as long as the particle's life, dense points: the carriers outlive the deaths
        // (at 0.3 s) by about 0.3 s.
        trails.lifetime = 1f;
        trails.minVertexDistance = 0.01f;
        go.GetComponent<ParticleSystemRenderer>().trailMaterial = new Material(Shader.Find("Sprites/Default"));
        var childGo = new GameObject("child");
        childGo.SetActive(false);
        var child = childGo.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.useAutoRandomSeed = false;
        child.randomSeed = 32u;
        var cm = child.main;
        cm.playOnAwake = false;
        cm.startLifetime = 5f;
        cm.startSpeed = 0f;
        cm.simulationSpace = ParticleSystemSimulationSpace.World;
        var ce = child.emission;
        ce.rateOverTime = 0f;
        ce.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
        var cs = child.shape;
        cs.enabled = false;
        childGo.SetActive(true);
        var sub = ps.subEmitters;
        sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritLifetime);
        go.SetActive(true);
        ps.Play();
        var list = new List<ParticleSystem.Particle>();
        for (int frame = 0; frame < 40; frame++)
        {
            yield return null;
            ps.GetParticles(s_Buffer);
            Record("carrier/parent/" + frame, new float[] { ps.particleCount, child.particleCount, ps.isPlaying ? 1 : 0 });
            if (frame == 20)
            {
                ps.TriggerSubEmitter(0);
                Record("carrier/all", new float[] { ps.particleCount, child.particleCount });
                int n = ps.GetParticles(s_Buffer);
                list.Clear();
                for (int i = 0; i < n; i++) list.Add(s_Buffer[i]);
                ps.TriggerSubEmitter(0, list);
                Record("carrier/list", new float[] { n, child.particleCount });
                if (n > 0)
                {
                    var p = s_Buffer[0];
                    ps.TriggerSubEmitter(0, ref p);
                }
                Record("carrier/single", new float[] { n, child.particleCount });
                ps.TriggerSubEmitter(1);
                Record("carrier/inherit", new float[] { n, child.particleCount });
                RecordState("carrier/child", child);
            }
        }
        Destroy(childGo);
        Destroy(go);
    }

    void Start() => StartCoroutine(Probe());

    IEnumerator Probe()
    {
        Application.logMessageReceived += OnLog;
        yield return null;
        Time.captureDeltaTime = 0.02f;
        Colliders = MakeColliders();
        // Let the colliders reach the physics scene.
        yield return new WaitForFixedUpdate();
        yield return null;
        foreach (var (name, query) in new[] { ("disabled", ParticleSystemColliderQueryMode.Disabled), ("one", ParticleSystemColliderQueryMode.One), ("all", ParticleSystemColliderQueryMode.All) })
            yield return Scenario("read/" + name, query, "read");
        yield return Scenario("write/all", ParticleSystemColliderQueryMode.All, "write");
        yield return Scenario("write/subset", ParticleSystemColliderQueryMode.Disabled, "subset");
        yield return Scenario("write/kill", ParticleSystemColliderQueryMode.Disabled, "kill");
        yield return Scenario("write/twice", ParticleSystemColliderQueryMode.Disabled, "twice");
        yield return Scenario("update", ParticleSystemColliderQueryMode.All, "update", 60);
        yield return Scenario("exitkill", ParticleSystemColliderQueryMode.All, "read", 150, ParticleSystemOverlapAction.Kill);
        yield return Carriers();
        Time.captureDeltaTime = 0f;
        Application.logMessageReceived -= OnLog;
        Record("complete", new float[] { 1 });
        File.WriteAllText(s_Path, s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
