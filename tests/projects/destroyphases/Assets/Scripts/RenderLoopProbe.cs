using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RenderLoopProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly List<string> Lines = new List<string>();
    static string position = "boot", invocation = "automatic";
    static int frame, origin;
    Camera manual;
    GameObject subject;
    static bool Recording => frame >= 2 && frame <= 7;
    public static void Log(string message)
    {
        if (Recording) Lines.Add((frame - 2) + "|" + position + "|" + invocation + "|" + message);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("RENDER_LOOP_OUT") == null) return;
        Time.captureFramerate = 50;
        Application.runInBackground = true;
        new GameObject("RenderLoopProbe").AddComponent<RenderLoopProbe>();
    }
    void Awake()
    {
        origin = Time.frameCount;
        var automatic = new GameObject("automatic").AddComponent<Camera>();
        automatic.transform.position = new Vector3(0, 0, -10);
        automatic.clearFlags = CameraClearFlags.SolidColor;
        automatic.allowHDR = false;
        automatic.gameObject.AddComponent<RenderLoopCamera>();
        manual = new GameObject("manual").AddComponent<Camera>();
        manual.transform.position = automatic.transform.position;
        manual.enabled = false;
        manual.allowHDR = false;
        manual.targetTexture = new RenderTexture(16, 16, 16);
        manual.gameObject.AddComponent<RenderLoopCamera>();
        subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        subject.name = "subject";
        subject.AddComponent<RenderLoopObserver>();
        Camera.onPreCull += c => Log(c.name + ".event.preCull");
        Camera.onPreRender += c => Log(c.name + ".event.preRender");
        Camera.onPostRender += c => Log(c.name + ".event.postRender");
        RenderPipelineManager.beginCameraRendering += (context, c) => Log("srp.beginCamera");
        RenderPipelineManager.endCameraRendering += (context, c) => Log("srp.endCamera");
        RenderPipelineManager.beginFrameRendering += (context, cameras) => Log("srp.beginFrame");
        RenderPipelineManager.endFrameRendering += (context, cameras) => Log("srp.endFrame");
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        Wrap(ref loop);
        var roots = new List<PlayerLoopSystem>(loop.subSystemList);
        roots.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => { position = "custom.tail"; Log("tail"); } });
        loop.subSystemList = roots.ToArray();
        PlayerLoop.SetPlayerLoop(loop);
        StartCoroutine(EndFrames());
    }
    struct Marker { }
    static void Wrap(ref PlayerLoopSystem parent)
    {
        if (parent.subSystemList == null) return;
        var children = new List<PlayerLoopSystem>();
        foreach (var original in parent.subSystemList)
        {
            var node = original;
            if (node.subSystemList != null && node.subSystemList.Length > 0) Wrap(ref node);
            else
            {
                string name = node.type.DeclaringType.Name + "." + node.type.Name;
                children.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => { position = name; if (name.StartsWith("PostLateUpdate.")) Log("leaf.before"); } });
                children.Add(node);
                children.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => { if (name.StartsWith("PostLateUpdate.")) Log("leaf.after"); position = "after:" + name; } });
                continue;
            }
            children.Add(node);
        }
        parent.subSystemList = children.ToArray();
    }
    void Update()
    {
        frame = Time.frameCount - origin;
        if (frame == 8)
        {
            Finished = true;
            Lines.Add("complete");
            File.WriteAllLines(Environment.GetEnvironmentVariable("RENDER_LOOP_OUT"), Lines);
            return;
        }
        Log("Update");
        if (frame == 3 || frame == 5) Manual();
        if (frame == 4) subject.GetComponent<Renderer>().enabled = false;
        if (frame == 5) subject.GetComponent<Renderer>().enabled = true;
        if (frame == 5)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveRenderingLeaf(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }
        if (frame == 6)
        {
            var victim = new GameObject("update.victim");
            victim.AddComponent<RenderLoopVictim>();
            Object.Destroy(victim);
            Log("update.destroy.return");
        }
    }
    static void RemoveRenderingLeaf(ref PlayerLoopSystem parent)
    {
        if (parent.subSystemList == null) return;
        var children = new List<PlayerLoopSystem>();
        foreach (var original in parent.subSystemList)
        {
            var node = original;
            if (node.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.FinishFrameRendering)) continue;
            RemoveRenderingLeaf(ref node);
            children.Add(node);
        }
        parent.subSystemList = children.ToArray();
    }
    void Manual()
    {
        invocation = "manual";
        Log("Camera.Render.before");
        manual.Render();
        Log("Camera.Render.after");
        invocation = "automatic";
    }
    IEnumerator EndFrames()
    {
        while (!Finished)
        {
            yield return new WaitForEndOfFrame();
            Log("WaitForEndOfFrame");
            if (frame == 3) Manual();
            if (frame == 6)
            {
                var victim = new GameObject("end.victim");
                victim.AddComponent<RenderLoopVictim>();
                Object.Destroy(victim);
                Log("end.destroy.return");
            }
        }
    }
}
public class RenderLoopCamera : MonoBehaviour
{
    bool added;
    void OnPreCull()
    {
        RenderLoopProbe.Log(name + ".preCull");
        if (name == "manual" && !added)
        {
            added = true;
            gameObject.AddComponent<RenderLoopAdded>();
            RenderLoopProbe.Log("manual.added");
        }
    }
    void OnPreRender() { RenderLoopProbe.Log(name + ".preRender"); }
    void OnPostRender() { RenderLoopProbe.Log(name + ".postRender"); }
    void OnRenderObject() { if (Camera.current == GetComponent<Camera>()) RenderLoopProbe.Log(name + ".renderObject"); }
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    { RenderLoopProbe.Log(name + ".renderImage"); Graphics.Blit(source, destination); }
}
public class RenderLoopObserver : MonoBehaviour
{
    void OnWillRenderObject() { RenderLoopProbe.Log("subject.willRender:" + Camera.current.name); }
    void OnBecameVisible() { RenderLoopProbe.Log("subject.visible"); }
    void OnBecameInvisible() { RenderLoopProbe.Log("subject.invisible"); }
}
public class RenderLoopVictim : MonoBehaviour
{
    void OnDisable() { RenderLoopProbe.Log(name + ".disable"); }
    void OnDestroy() { RenderLoopProbe.Log(name + ".destroy"); }
}

public class RenderLoopAdded : MonoBehaviour
{
    void OnPreCull() { RenderLoopProbe.Log("added.preCull"); }
    void OnPreRender() { RenderLoopProbe.Log("added.preRender"); }
}
