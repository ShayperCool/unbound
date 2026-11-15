using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using Unity.Collections;
using UnityEngine;

public sealed class ListenerPoseProbe : MonoBehaviour
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("LISTENER_POSE_ENGINE") == "1";
    int warnings, threadedWarnings;
    StreamWriter values, raw;
    string output;
    void Row(string key, bool value) => values.WriteLine("{\"k\":\"" + key + "\",\"v\":" + (value ? "true" : "false") + "}");
    static void FlushEngine()
    {
        var assembly = Type.GetType("Unbound.Runtime.AudioBackend, UnityEngine.CoreModule", true).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", Flags).Invoke(null, null);
    }
    static void Exit(int code)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    IEnumerator Start()
    {
        var routine = Capture();
        for (;;) {
            object next;
            try { if (!routine.MoveNext()) yield break; next = routine.Current; }
            catch (Exception error) { Debug.LogException(error); Exit(1); yield break; }
            yield return next;
        }
    }
    IEnumerator Capture()
    {
        output = Environment.GetEnvironmentVariable("LISTENER_POSE_OUT"); Directory.CreateDirectory(output);
        values = new StreamWriter(Path.Combine(output, "values.jsonl"));
        raw = new StreamWriter(Path.Combine(output, "warning-counts.jsonl"));
        var config = AudioSettings.GetConfiguration(); config.sampleRate = 48000;
        config.dspBufferSize = 1024; config.speakerMode = AudioSpeakerMode.Stereo;
        if (!AudioSettings.Reset(config)) throw new Exception("Audio reset failed");
        if (!Engine) yield return null;
        Time.captureDeltaTime = 1024f / 48000;
        AudioListener.volume = 1;
        var nearObject = new GameObject("Near listener"); var near = nearObject.AddComponent<AudioListener>();
        var farObject = new GameObject("Far listener"); farObject.transform.position = new Vector3(100, 0, 0);
        var far = farObject.AddComponent<AudioListener>();
        var sourceObject = new GameObject("Positioned source"); var source = sourceObject.AddComponent<AudioSource>();
        source.playOnAwake = false; source.loop = true; source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1; source.maxDistance = 10; source.dopplerLevel = 0;
        var clip = AudioClip.Create("Constant input", 48000, 1, 48000, false);
        var data = new float[48000]; for (int i = 0; i < data.Length; i++) data[i] = 0.125f;
        if (!clip.SetData(data, 0)) throw new Exception("SetData failed");
        source.clip = clip; source.Play();
        bool Match(string message) => message.Contains("audio listeners in the scene");
        Application.LogCallback main = (message, stack, type) => { if (Match(message)) warnings++; };
        Application.LogCallback threaded = (message, stack, type) => { if (Match(message)) Interlocked.Increment(ref threadedWarnings); };
        Application.logMessageReceived += main; Application.logMessageReceivedThreaded += threaded;
        if (!AudioRenderer.Start()) throw new Exception("Renderer start failed");
        var sink = new NativeArray<float>(2048, Allocator.Persistent);
        IEnumerator Phase(string key, Action change)
        {
            int mainBefore = warnings, threadedBefore = threadedWarnings;
            change();
            using var pcm = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32")));
            for (int block = 0; block < 6; block++) {
                if (Engine) FlushEngine(); else yield return null;
                if (!AudioRenderer.Render(sink)) throw new Exception("Render failed: " + key);
                for (int sample = 0; sample < sink.Length; sample++) pcm.Write(sink[sample]);
            }
            bool audible = false;
            for (int i = 0; i < sink.Length; i++) audible |= Math.Abs(sink[i]) > 0.000001f;
            Row(key + ".audible", audible);
            Row(key + ".warning_seen", warnings > 0);
            Row(key + ".near_enabled", near != null && near.isActiveAndEnabled);
            Row(key + ".far_enabled", far.isActiveAndEnabled);
            raw.WriteLine("{\"phase\":\"" + key + "\",\"main\":" + (warnings - mainBefore)
                + ",\"threaded\":" + (threadedWarnings - threadedBefore) + "}");
            raw.Flush(); values.Flush();
        }
        foreach (var phase in new[] {
            Phase("two_2d", () => source.spatialBlend = 0),
            Phase("two_far_last", () => source.spatialBlend = 1),
            Phase("near_reenabled", () => { near.enabled = false; near.enabled = true; }),
            Phase("near_already_enabled", () => near.enabled = true),
            Phase("far_reenabled", () => { far.enabled = false; far.enabled = true; }),
            Phase("far_disabled", () => far.enabled = false),
            Phase("none", () => near.enabled = false),
            Phase("only_far", () => far.enabled = true),
            Phase("far_moved_near", () => farObject.transform.position = Vector3.zero),
            Phase("near_enabled_far", () => { nearObject.transform.position = new Vector3(100, 0, 0); near.enabled = true; }),
            Phase("near_inactive", () => nearObject.SetActive(false)),
            Phase("near_destroyed", () => UnityEngine.Object.DestroyImmediate(nearObject)) }) yield return phase;
        source.Stop(); AudioRenderer.Stop(); sink.Dispose();
        Application.logMessageReceived -= main; Application.logMessageReceivedThreaded -= threaded;
        UnityEngine.Object.DestroyImmediate(sourceObject); UnityEngine.Object.DestroyImmediate(farObject); UnityEngine.Object.DestroyImmediate(clip);
        Row("complete", true); values.Dispose(); raw.Dispose(); Exit(0);
    }
}
