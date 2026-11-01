using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class GeneratorLifecycleProbe : MonoBehaviour
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    IEnumerator Start()
    {
        var run = Capture();
        for (;;) {
            object next;
            try { if (!run.MoveNext()) yield break; next = run.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            yield return next;
        }
    }
    static void Exit(int code)
    {
        if (Environment.GetEnvironmentVariable("GENERATOR_LIFECYCLE_ENGINE") == "1") { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    static void Fill(NativeArray<float> buffer, float value)
    {
        for (int i = 0; i < buffer.Length; i++) buffer[i] = value;
    }
    IEnumerator Capture()
    {
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = 48000; config.dspBufferSize = 1024; config.speakerMode = AudioSpeakerMode.Stereo;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        string output = Environment.GetEnvironmentVariable("GENERATOR_LIFECYCLE_OUT");
        Directory.CreateDirectory(output);
        if (!AudioRenderer.Start()) throw new Exception("Start failed");
        var mixer = Resources.Load<AudioMixer>("lifecycle");
        var groups = mixer.FindMatchingGroups("");
        var master = Array.Find(groups, g => g.name == "Master");
        var child = Array.Find(groups, g => g.name == "Child");
        var go = new GameObject("Generator");
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false; source.outputAudioMixerGroup = child;
        var generator = go.AddComponent<GeneratorTrace>();
        var listener = gameObject.AddComponent<ListenerTrace>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":48000" };
        var labels = new List<string>();
        int renders = 0;
        using (var buffer = new NativeArray<float>(2048, Allocator.Persistent))
        using (var sink = new NativeArray<float>(2048, Allocator.Persistent))
        using (var pcm = new BinaryWriter(File.Create(Path.Combine(output, "output.f32"))))
        using (var group = new BinaryWriter(File.Create(Path.Combine(output, "master.f32"))))
        using (var trace = new BinaryWriter(File.Create(Path.Combine(output, "trace.f32")))) {
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", F);
            void Render(string label) {
                int before = generator.Calls, heard = listener.Calls;
                Fill(sink, -3f);
                if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("Sink failed");
                if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                labels.Add("\"" + label + "\""); renders++;
                trace.Write((float)(generator.Calls - before)); trace.Write((float)(listener.Calls - heard));
                trace.Write(source.isPlaying ? 1f : 0f); trace.Write(source.isVirtual ? 1f : 0f);
                trace.Write((float)source.timeSamples); trace.Write((float)generator.LastLength);
                trace.Write(generator.LastAmplitude); trace.Write(listener.First); trace.Write(listener.Last);
                for (int i = 0; i < buffer.Length; i++) { pcm.Write(buffer[i]); group.Write(sink[i]); }
            }
            // Render bursts distinguish audio submission from main-thread updates. Amplitude
            // changes distinguish source callback input from listener/capture delivery.
            yield return null;
            for (int i = 0; i < 3; i++) { Render("noPlay." + i); yield return null; }
            source.Play();
            Render("play.immediate.0"); Render("play.immediate.1");
            yield return null;
            for (int i = 0; i < 3; i++) Render("play.updated." + i);
            generator.Amplitude = 0.125f;
            for (int i = 0; i < 3; i++) Render("amplitude." + i);
            source.Pause(); Render("pause.immediate"); yield return null;
            Render("pause.updated.0"); Render("pause.updated.1");
            source.UnPause(); Render("unpause.immediate"); yield return null;
            Render("unpause.updated.0"); Render("unpause.updated.1");
            source.Stop(); Render("stop.immediate"); yield return null;
            Render("stop.updated.0"); Render("stop.updated.1");
            generator.Amplitude = 0.25f;
            source.Play(); Render("replay.immediate"); yield return null;
            Render("replay.updated.0"); Render("replay.updated.1");
        }
        AudioRenderer.Stop();
        records.Add("\"steps\":[" + string.Join(",", labels) + "]");
        foreach (string key in new[] { "output", "master", "trace" })
            records.Add("\"" + key + "\":{\"file\":\"" + key + ".f32\",\"frames\":" + (key == "trace" ? renders : renders * 1024) + ",\"channels\":" + (key == "trace" ? 9 : 2) + "}");
        records.Add("\"complete\":true");
        File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Exit(0);
    }
}
public class GeneratorTrace : MonoBehaviour
{
    public volatile float Amplitude = 0.0625f;
    public volatile int Calls, LastLength;
    public volatile float LastAmplitude;
    void OnAudioFilterRead(float[] data, int channels)
    {
        Calls++; LastLength = data.Length; LastAmplitude = Amplitude;
        for (int i = 0; i < data.Length; i++) data[i] = Amplitude;
    }
}
public class ListenerTrace : MonoBehaviour
{
    public volatile int Calls;
    public volatile float First, Last;
    void OnAudioFilterRead(float[] data, int channels)
    {
        Calls++; First = data[0]; Last = data[data.Length - 1];
    }
}
