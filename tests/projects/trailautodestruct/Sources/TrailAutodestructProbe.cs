using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.LowLevel;
using PL = UnityEngine.PlayerLoop;

// Public API phase observations. No engine-private state or fitted frame correction.
public class TrailAutodestructProbe : MonoBehaviour
{
    public static string Phase = "boot";
    static TrailAutodestructProbe instance;
    readonly List<GameObject> objects = new List<GameObject>();
    readonly List<TrailRenderer> trails = new List<TrailRenderer>();
    StreamWriter output;
    int step = -1;
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("TRAIL_AUTODESTRUCT_OUT") == null) return;
        Time.captureFramerate = 50;
        instance = new GameObject("probe").AddComponent<TrailAutodestructProbe>();
        instance.output = new StreamWriter(Environment.GetEnvironmentVariable("TRAIL_AUTODESTRUCT_OUT")) { AutoFlush = true };
        instance.Add("auto.short", .05f, true);
        instance.Add("auto.long", .11f, true);
        instance.Add("passive", .05f, false);
        instance.Add("manual.update", 1f, false);
        instance.Add("manual.late", 1f, false);
        instance.Add("manual.eof", 1f, false);
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        Wrap(ref loop);
        PlayerLoop.SetPlayerLoop(loop);
    }

    void Add(string name, float duration, bool auto)
    {
        var go = new GameObject(name);
        go.AddComponent<TrailAutodestructObserver>();
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = duration;
        trail.autodestruct = auto;
        trail.minVertexDistance = .01f;
        objects.Add(go);
        trails.Add(trail);
    }

    static void Wrap(ref PlayerLoopSystem loop)
    {
        if (loop.subSystemList == null) return;
        var result = new List<PlayerLoopSystem>();
        foreach (var child in loop.subSystemList)
        {
            var item = child;
            Wrap(ref item);
            string name = item.type?.Name;
            bool observe = item.type == typeof(PL.PostLateUpdate.UpdateAllRenderers)
                || item.type == typeof(PL.PostLateUpdate.PlayerSendFrameStarted)
                || item.type == typeof(PL.PostLateUpdate.PlayerSendFrameComplete)
                || item.type == typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks);
            if (observe) result.Add(new PlayerLoopSystem { type = typeof(TrailAutodestructProbe), updateDelegate = () => instance.Snapshot("before." + name) });
            result.Add(item);
            if (observe) result.Add(new PlayerLoopSystem { type = typeof(TrailAutodestructProbe), updateDelegate = () => instance.After(name) });
        }
        loop.subSystemList = result.ToArray();
    }

    static void Request(GameObject go)
    {
        Event(go.name, "request");
        Destroy(go);
        Event(go.name, "returned");
    }

    public static void Event(string name, string kind)
    {
        if (instance == null || instance.output == null) return;
        instance.output.WriteLine("{\"kind\":\"" + kind + "\",\"name\":\"" + name + "\",\"step\":" + instance.step
            + ",\"frame\":" + Time.frameCount + ",\"time\":" + F(Time.time) + ",\"phase\":\"" + Phase + "\"}");
    }

    void Snapshot(string phase)
    {
        if (output == null) return;
        Phase = phase;
        for (int i = 0; i < objects.Count; i++)
        {
            var go = objects[i]; var trail = trails[i];
            output.WriteLine("{\"kind\":\"state\",\"name\":\"" + (i == 0 ? "auto.short" : i == 1 ? "auto.long" : i == 2 ? "passive" : i == 3 ? "manual.update" : i == 4 ? "manual.late" : "manual.eof")
                + "\",\"step\":" + step + ",\"frame\":" + Time.frameCount + ",\"time\":" + F(Time.time)
                + ",\"phase\":\"" + phase + "\",\"alive\":" + (go != null ? "true" : "false") + ",\"count\":" + (trail != null ? trail.positionCount : -1) + "}");
        }
    }

    void Update()
    {
        step++;
        Phase = "Update";
        if (step < 3)
            foreach (var go in objects) if (go != null) go.transform.position = new Vector3(step + 1, 0, 0);
        Snapshot("Update");
        if (step == 6) Request(objects[3]);
        if (step == 14)
        {
            output.WriteLine("{\"complete\":true}");
            output.Dispose(); output = null;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit(0);
#endif
        }
    }

    void LateUpdate()
    {
        if (output == null) return;
        Snapshot("LateUpdate");
        if (step == 6) Request(objects[4]);
    }

    void After(string phase)
    {
        if (output == null) return;
        Snapshot("after." + phase);
        if (step == 6 && phase == "TriggerEndOfFrameCallbacks") Request(objects[5]);
    }
}

public class TrailAutodestructObserver : MonoBehaviour
{
    void OnDisable() => TrailAutodestructProbe.Event(gameObject.name, "OnDisable");
    void OnDestroy() => TrailAutodestructProbe.Event(gameObject.name, "OnDestroy");
}
