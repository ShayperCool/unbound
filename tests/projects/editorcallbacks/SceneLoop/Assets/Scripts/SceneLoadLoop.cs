using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

public sealed class SceneLoadLoop : MonoBehaviour
{
    sealed class Marker { }
    public static bool Finished;
    static readonly List<string> Lines = new List<string>();
    static readonly List<string> Diagnostics = new List<string>();
    static string Case;
    static string Phase = "startup";
    static int FirstFrame;
    static AsyncOperation Operation;
    static AsyncOperation Queued;
    static AsyncOperation QueuedUnload;
    static bool QueueHolding;
    static string UnloadPhase, UnloadLoadsDone;
    static int RenderUntil;
    static bool Recording;

    public static void Event(string message, GameObject target = null)
    {
        if (!Recording) return;
        Lines.Add(Case + "|" + (Time.frameCount - FirstFrame) + "|" + Phase + "|" + message
            + (target == null ? "" : "|" + target.name + "|" + target.scene.name + "|loaded=" + target.scene.isLoaded));
    }

    public static void Render(GameObject target)
    {
        if (Time.frameCount <= RenderUntil) Event("OnRenderObject", target);
    }

    static string State(AsyncOperation op)
    {
        float progress = op.progress;
        string exact = progress.ToString("0.000", CultureInfo.InvariantCulture);
        Diagnostics.Add(Case + "|" + (Time.frameCount - FirstFrame) + "|" + Phase + "|read.progress=" + exact
            + ",done=" + op.isDone + ",allow=" + op.allowSceneActivation);
        // A request's immediate progress races the background reader. Keep its exact read
        // in the sidecar, and check its documented pending range in the canonical record.
        string value = Phase == "request" && !op.isDone && progress >= 0 && progress <= .9f ? "[0,0.9]" : exact;
        return "progress=" + value + ",done=" + op.isDone + ",allow=" + op.allowSceneActivation;
    }

    static void Sample(string where)
    {
        Phase = where;
        if (Operation != null) Event(State(Operation));
        if (Queued != null) Event("queued|" + State(Queued));
        if (QueuedUnload != null)
        {
            string state = State(QueuedUnload);
            if (QueueHolding) Event("unload|" + state);
        }
    }

    static void Install()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i];
            if (phase.subSystemList == null) continue;
            var list = new List<PlayerLoopSystem>();
            foreach (var leaf in phase.subSystemList)
            {
                string name = phase.type.Name + "." + leaf.type.Name;
                list.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Phase = name });
                if (leaf.type == typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdatePreloading))
                    list.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Sample("preloading.before") });
                list.Add(leaf);
                if (leaf.type == typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdatePreloading))
                    list.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Sample("preloading.after") });
            }
            phase.subSystemList = list.ToArray();
            loop.subSystemList[i] = phase;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Install();
        SceneManager.sceneLoaded += (scene, mode) => {
            Event("sceneLoaded|" + scene.name + "|" + mode);
            Roots(scene, "loaded.roots");
            RenderUntil = Time.frameCount + 1;
        };
        SceneManager.activeSceneChanged += (oldScene, newScene) => Event("activeSceneChanged|" + oldScene.name + "|" + newScene.name);
    }

    static void Roots(Scene scene, string label)
    {
        var roots = scene.GetRootGameObjects();
        var names = new string[roots.Length];
        for (int i = 0; i < roots.Length; i++) names[i] = roots[i].name + ":" + roots[i].transform.GetSiblingIndex();
        Event(label + "|" + string.Join(",", names));
    }

    IEnumerator Start()
    {
        Recording = true;
        if (Environment.GetEnvironmentVariable("SCENE_LOAD_FORCE_ONLY") == "1")
        {
            yield return ForceControl(false);
            yield return ForceControl(true);
            Finish();
            yield break;
        }
        bool singleFirst = Environment.GetEnvironmentVariable("SCENE_LOAD_SINGLE_FIRST") == "1";
        if (singleFirst)
        {
            yield return Load("single.cold", "First", LoadSceneMode.Single, false);
            Finish();
            yield break;
        }
        yield return Load("additive.first", "First", LoadSceneMode.Additive, false);
        yield return Load("additive.later", "Second", LoadSceneMode.Additive, false);
        yield return Load("additive.held", "First", LoadSceneMode.Additive, true);
        yield return Load("single.first", "Second", LoadSceneMode.Single, false);
        yield return Load("single.later", "First", LoadSceneMode.Single, false);
        yield return Load("single.held", "Second", LoadSceneMode.Single, true);
        yield return Registration();
        yield return QueueControl();
        Case = "sync.control";
        FirstFrame = Time.frameCount;
        Phase = "request";
        SceneManager.LoadScene("First", LoadSceneMode.Additive);
        Event("returned");
        yield return null;
        yield return null;
        Roots(SceneManager.GetSceneByName("First"), "sync.roots");
        Finish();
    }

    static void Finish()
    {
        Recording = false;
        Lines.Add("complete");
        string path = Environment.GetEnvironmentVariable("SCENE_LOAD_LOOP_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllLines(path, Lines);
        File.WriteAllLines(path + ".raw.txt", Diagnostics);
        Finished = true;
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null) Application.Quit();
    }

    IEnumerator Load(string name, string scene, LoadSceneMode mode, bool held)
    {
        Case = name;
        FirstFrame = Time.frameCount;
        Phase = "request";
        Operation = SceneManager.LoadSceneAsync(scene, mode);
        Operation.allowSceneActivation = !held;
        Event("returned|" + State(Operation));
        Operation.completed += op => Event("completed|" + State(op));
        int holdFrame = -1;
        while (!Operation.isDone)
        {
            yield return null;
            Sample("coroutine");
            if (held && Operation.progress >= .9f)
            {
                if (holdFrame < 0) holdFrame = Time.frameCount;
                if (!Operation.allowSceneActivation && Time.frameCount - holdFrame >= 2)
                {
                    Operation.allowSceneActivation = true;
                    Event("released|" + State(Operation));
                }
            }
            if (Time.frameCount - FirstFrame > 60) throw new Exception("load timeout " + name);
        }
        Event("observed.done");
        var loaded = SceneManager.GetSceneByName(scene);
        Roots(loaded, "done.roots");
        Operation = null;
        yield return null;
        yield return null;
        if (mode == LoadSceneMode.Additive)
        {
            Phase = "unload.request";
            yield return SceneManager.UnloadSceneAsync(loaded);
        }
        yield return null;
    }

    IEnumerator ForceControl(bool prepared)
    {
        Case = prepared ? "force.prepared" : "force.sameFrame";
        FirstFrame = Time.frameCount;
        Phase = "request";
        Operation = SceneManager.LoadSceneAsync("First", LoadSceneMode.Additive);
        Operation.allowSceneActivation = false;
        Operation.completed += op => Event("completed|" + State(op));
        Event("returned|" + State(Operation));
        if (prepared) { yield return null; Sample("coroutine"); }
        Event("sync.request|" + State(Operation));
        SceneManager.LoadScene("Second", LoadSceneMode.Additive);
        Event("sync.returned|" + State(Operation));
        for (int i = 0; i < 4; i++) { yield return null; Sample("coroutine"); }
        Event("done|" + State(Operation));
        Operation = null;
        yield return null;
    }

    IEnumerator Registration()
    {
        Case = "registration.control";
        FirstFrame = Time.frameCount;
        Phase = "mutate";
        var scene = SceneManager.GetActiveScene();
        var plain = GameObject.Find("PlainA");
        plain.transform.SetSiblingIndex(2);
        Roots(scene, "reordered.roots");
        var item = plain.GetComponent<SceneLoadItem>();
        item.enabled = false;
        item.enabled = true;
        new GameObject("Runtime").AddComponent<SceneLoadItem>();
        RenderUntil = Time.frameCount + 1;
        yield return null;
        yield return null;
        Roots(scene, "runtime.roots");
    }

    IEnumerator QueueControl()
    {
        Case = "queue.control";
        FirstFrame = Time.frameCount;
        Phase = "request";
        Operation = SceneManager.LoadSceneAsync("First", LoadSceneMode.Additive);
        Operation.allowSceneActivation = false;
        Queued = SceneManager.LoadSceneAsync("Second", LoadSceneMode.Additive);
        QueuedUnload = SceneManager.UnloadSceneAsync(SceneManager.CreateScene("QueueDisposable"));
        Event("returned|" + State(Operation));
        Event("queued.returned|" + State(Queued));
        Operation.completed += op => Event("completed|" + State(op));
        Queued.completed += op => Event("queued.completed|" + State(op));
        QueueHolding = true;
        QueuedUnload.completed += op => {
            UnloadPhase = Phase;
            UnloadLoadsDone = Operation.isDone + "," + Queued.isDone;
            Diagnostics.Add(Case + "|" + (Time.frameCount - FirstFrame) + "|" + Phase + "|unload.completed|" + State(op));
        };
        for (int i = 0; i < 4; i++) { yield return null; Sample("coroutine"); }
        QueueHolding = false;
        Operation.allowSceneActivation = true;
        Event("released|" + State(Operation));
        int deadline = Time.frameCount + 60;
        while (!Queued.isDone)
        {
            yield return null;
            Sample("coroutine");
            if (Time.frameCount > deadline) throw new Exception("queue timeout");
        }
        // Unload completion can share the second load's preloading pass or use the next.
        // Its exact callback frame stays in the sidecar; the settled control checks phase,
        // causal ordering, the held queue and eventual completion without fixing its duration.
        yield return null;
        yield return null;
        yield return null;
        Phase = "settled";
        Event("unload.settled|" + State(QueuedUnload) + "|phase=" + UnloadPhase + "|loads.done=" + UnloadLoadsDone);
        QueuedUnload = null;
        Queued = null;
        Operation = null;
    }

    void LateUpdate()
    {
        // Give the background reader time to settle between the fixture's small frames.
        Thread.Sleep(50);
    }
}
