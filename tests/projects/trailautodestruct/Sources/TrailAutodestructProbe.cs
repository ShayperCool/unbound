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
    Camera camera;
    Material material;
    RenderTexture target;
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
        instance.SetupRendering();
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

    void SetupRendering()
    {
        // Explicit rendering is needed in batchmode; the diagnostic does not rely on a Game view.
        var shader = Resources.Load<Shader>("LineColor");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("LineColor shader unavailable");
        material = new Material(shader);
        camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 2;
        camera.aspect = 1;
        camera.nearClipPlane = .3f;
        camera.farClipPlane = 50;
        camera.cullingMask = 1;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
        target.Create();
        camera.targetTexture = target;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 50;
    }

    void Add(string name, float duration, bool auto)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(-1.5f, -1.25f + objects.Count * .5f, 0);
        go.AddComponent<TrailAutodestructObserver>();
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = duration;
        trail.autodestruct = auto;
        trail.minVertexDistance = .01f;
        trail.sharedMaterial = material;
        trail.startWidth = trail.endWidth = .15f;
        trail.startColor = trail.endColor = Color.white;
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
        // Bound real elapsed time too, even when batchmode ignores targetFrameRate.
        // This is fixture pacing, not an agent wait or a fitted destruction delay.
        System.Threading.Thread.Sleep(20);
        step++;
        Phase = "Update";
        if (step < 3)
            foreach (var go in objects) if (go != null) go.transform.position = new Vector3(-1f + step * .5f, go.transform.position.y, 0);
        Snapshot("Update");
        if (step == 6) Request(objects[3]);
        if (step == 49)
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
        if (phase == "UpdateAllRenderers")
        {
            Snapshot("before.Camera.Render");
            camera.Render();
            Snapshot("after.Camera.Render");
            if (step == 2) RecordRenderedLanes();
        }
        if (step == 6 && phase == "TriggerEndOfFrameCallbacks") Request(objects[5]);
    }

    void RecordRenderedLanes()
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
        image.Apply();
        var pixels = image.GetPixels32();
        for (int lane = 0; lane < objects.Count; lane++)
        {
            int count = 0;
            // The lane centres are 24, 40, ..., 104 pixels, all inside the frustum.
            int centre = 24 + lane * 16;
            for (int y = centre - 6; y <= centre + 6; y++)
                for (int x = 0; x < 128; x++)
                {
                    var c = pixels[y * 128 + x];
                    if (c.r > 8 || c.g > 8 || c.b > 8) count++;
                }
            output.WriteLine("{\"kind\":\"rendered\",\"name\":\"" + objects[lane].name
                + "\",\"step\":" + step + ",\"frame\":" + Time.frameCount + ",\"time\":" + F(Time.time)
                + ",\"phase\":\"after.Camera.Render\",\"pixels\":" + count + "}");
        }
        RenderTexture.active = previous;
        // Avoid adding a diagnostic texture Destroy request to the measured queue.
        DestroyImmediate(image);
    }
}

public class TrailAutodestructObserver : MonoBehaviour
{
    void OnDisable() => TrailAutodestructProbe.Event(gameObject.name, "OnDisable");
    void OnDestroy() => TrailAutodestructProbe.Event(gameObject.name, "OnDestroy");
}
