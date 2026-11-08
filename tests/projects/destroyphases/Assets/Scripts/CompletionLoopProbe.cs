using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;

public class CompletionLoopProbe : MonoBehaviour
{
    public static bool Finished;
    static bool recording;
    static int origin, rendered = -1;
    static readonly List<string> Lines = new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("COMPLETION_LOOP_OUT") == null) return;
        Application.runInBackground = true;
        Time.captureFramerate = 50;
        new GameObject("CompletionLoopProbe").AddComponent<CompletionLoopProbe>();
    }
    public static void Record(string message)
    {
        if (recording) Lines.Add((Time.frameCount - origin) + "|" + message + "|rendered=" + (rendered == Time.frameCount));
    }
    public static void Rendered() { rendered = Time.frameCount; Record("render"); }
    void Awake()
    {
        origin = Time.frameCount;
        var camera = new GameObject("completion.camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.gameObject.AddComponent<CompletionLoopCamera>();
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        var roots = new List<PlayerLoopSystem>(loop.subSystemList);
        roots.Add(new PlayerLoopSystem { type = typeof(CompletionLoopProbe), updateDelegate = () => Record("tail") });
        loop.subSystemList = roots.ToArray(); PlayerLoop.SetPlayerLoop(loop);
        StartCoroutine(EndFrames());
    }
    IEnumerator EndFrames()
    {
        while (!Finished) { yield return new WaitForEndOfFrame(); Record("coroutine"); }
    }
    async void Start()
    {
        for (int i = 0; i < 3; i++) await Awaitable.NextFrameAsync();
        recording = true;
        foreach (int interval in new[] { 1, 2 })
        {
            OnDemandRendering.renderFrameInterval = interval;
            for (int i = 0; i < 4; i++)
            {
                Record("request.interval" + interval);
                await Awaitable.EndOfFrameAsync();
                Record("await.interval" + interval);
                await Awaitable.NextFrameAsync();
            }
        }
        recording = false; OnDemandRendering.renderFrameInterval = 1;
        Lines.Add("complete"); File.WriteAllLines(Environment.GetEnvironmentVariable("COMPLETION_LOOP_OUT"), Lines);
        Finished = true;
    }
}
public class CompletionLoopCamera : MonoBehaviour
{
    void OnPostRender() { CompletionLoopProbe.Rendered(); }
}
