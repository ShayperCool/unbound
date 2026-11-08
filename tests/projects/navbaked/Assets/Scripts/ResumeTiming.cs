using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Where in the player loop asynchronous operations complete and the coroutines waiting for them
/// resume (<c>resume.*</c>): a marker before every player-loop system names the system that runs
/// next, and each event is recorded with it and the frame counted from the operation's start.
/// The additive load and unload of the second scene (its agent's transform, as found and as it
/// changes), the same load held by <c>allowSceneActivation</c>, <c>Resources.LoadAsync</c>,
/// <c>InstantiateAsync</c>, a coroutine yielding an operation already done, and the order of
/// <c>completed</c> handlers added before and after a coroutine began to wait.
/// </summary>
public static class ResumeTiming
{
    sealed class Marker { }

    static string s_Phase = "?";
    static int s_Start;
    static string s_Scenario;
    // The scenario's events, written to the log when it ends (the probe's own scenarios are
    // logged meanwhile).
    static readonly List<string> s_Events = new List<string>();
    static bool s_Installed;
    // The second scene's agent: last recorded state ("" before it is found).
    static string s_Agent2;

    public static void Install()
    {
        if (s_Installed) return;
        s_Installed = true;
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i];
            if (phase.subSystemList == null) continue;
            var list = new List<PlayerLoopSystem>();
            foreach (var s in phase.subSystemList)
            {
                string name = (phase.type != null ? phase.type.Name : "?") + "." + (s.type != null ? s.type.Name : "?");
                list.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Enter(name) });
                list.Add(s);
            }
            phase.subSystemList = list.ToArray();
            loop.subSystemList[i] = phase;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void Enter(string phase)
    {
        s_Phase = phase;
        if (s_Scenario == null) return;
        PollAgent2();
        PollOperation();
    }

    // The scenario's operation and its last recorded isDone.
    static AsyncOperation s_Operation;
    static string s_OperationState;

    /// <summary>isDone as each system sees it. Progress is not recorded here: below 1 it follows
    /// Unity's background work, which reaches 0.9 at no fixed point.</summary>
    static void PollOperation()
    {
        if (s_Operation == null) return;
        string state = "done=" + (s_Operation.isDone ? "true" : "false");
        if (state == s_OperationState) return;
        s_OperationState = state;
        Event("op " + state);
    }

    static void PollAgent2()
    {
        var go = GameObject.Find("Agent2");
        string state = go == null ? "" : "y=" + go.transform.position.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            + " onNavMesh=" + (go.GetComponent<NavMeshAgent>().isOnNavMesh ? "true" : "false");
        if (state == s_Agent2) return;
        s_Agent2 = state;
        Event(go == null ? "agent2.gone" : "agent2 " + state);
    }

    /// <summary>Begins a scenario whose frames count from now.</summary>
    public static void Begin(string scenario)
    {
        End();
        s_Scenario = scenario;
        s_Operation = null;
        s_Start = Time.frameCount;
        s_Events.Clear();
        s_Agent2 = GameObject.Find("Agent2") == null ? "" : null;
    }

    public static void End()
    {
        if (s_Scenario == null) return;
        ProbeLog.Begin("resume." + s_Scenario);
        for (int i = 0; i < s_Events.Count; i++) ProbeLog.Value("e" + i, s_Events[i]);
        s_Scenario = null;
    }

    public static void Event(string what)
    {
        if (s_Scenario != null) s_Events.Add("f" + (Time.frameCount - s_Start) + " " + s_Phase + " " + what);
    }

    /// <summary>The operation is started; its background work is given time to finish before the
    /// next frame, so that it completes in a fixed frame.</summary>
    static T Started<T>(T op, string label) where T : AsyncOperation
    {
        op.completed += _ => Event(label + ".completed");
        Event(label + ".started done=" + (op.isDone ? "true" : "false"));
        s_Operation = op;
        s_OperationState = null;
        PollOperation();
        Thread.Sleep(100);
        return op;
    }

    static IEnumerator Wait(AsyncOperation op, string label)
    {
        yield return op;
        Event(label + ".resumed done=" + (op.isDone ? "true" : "false"));
    }

    // ------------------------------------------------------------------ scenarios

    /// <summary>The additive load the probe's main scenarios wait for.</summary>
    public static AsyncOperation Load()
    {
        Begin("load");
        SceneManager.sceneLoaded += Loaded;
        return Started(SceneManager.LoadSceneAsync("Second", LoadSceneMode.Additive), "load");
    }

    static void Loaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= Loaded;
        Event("sceneLoaded " + scene.name);
    }

    public static void Resumed(string label) => Event(label + ".resumed");

    public static AsyncOperation Unload()
    {
        Begin("unload");
        return Started(SceneManager.UnloadSceneAsync("Second"), "unload");
    }

    /// <summary>The controls, after the probe's own scenarios.</summary>
    public static IEnumerator Controls(MonoBehaviour host)
    {
        // A load held by allowSceneActivation, released in Update two frames later.
        Begin("held");
        SceneManager.sceneLoaded += Loaded;
        var held = SceneManager.LoadSceneAsync("Second", LoadSceneMode.Additive);
        held.allowSceneActivation = false;
        Started(held, "held");
        host.StartCoroutine(Wait(held, "held"));
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            Event("held.progress=" + (held.progress >= 0.9f ? "0.9+" : "<0.9") + " done=" + (held.isDone ? "true" : "false"));
        }
        s_Release = held;
        while (!held.isDone) yield return null;
        yield return null;
        yield return null;
        End();
        var unload = SceneManager.UnloadSceneAsync("Second");
        while (!unload.isDone) yield return null;

        // Resources.LoadAsync: handlers before and after a coroutine began to wait, a second waiter.
        Begin("resources");
        var request = Resources.LoadAsync<TextAsset>("ResumeProbe");
        request.completed += _ => Event("resources.handlerBefore");
        Started(request, "resources");
        host.StartCoroutine(Wait(request, "resources.first"));
        request.completed += _ => Event("resources.handlerAfter");
        host.StartCoroutine(Wait(request, "resources.second"));
        yield return request;
        Event("resources.resumed asset=" + (request.asset != null ? request.asset.name : "null"));
        // An operation already done: where the coroutine yielding it resumes.
        Event("done.yield");
        yield return request;
        Event("done.resumed");
        yield return null;
        yield return null;
        End();

        // InstantiateAsync of a plain object.
        Begin("instantiate");
        var template = new GameObject("ResumeTemplate");
        var copies = Started(UnityEngine.Object.InstantiateAsync(template), "instantiate");
        yield return copies;
        Event("instantiate.resumed result=" + (copies.Result != null ? copies.Result.Length : -1));
        yield return null;
        yield return null;
        End();
    }

    static AsyncOperation s_Release;

    /// <summary>Called from the probe's Update: releases the held load.</summary>
    public static void Update()
    {
        if (s_Release == null) return;
        Event("held.allow");
        s_Release.allowSceneActivation = true;
        Event("held.allowed done=" + (s_Release.isDone ? "true" : "false"));
        s_Release = null;
    }
}
