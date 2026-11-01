using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;

// A clipless generator first emits silence, then a constant signal. The second case
// attaches it after the quiet DSP interval, when the mixer may already be suspended.
public class GeneratorProbe : MonoBehaviour
{
    public const int Blocks = 88;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static void Fill(NativeArray<float> values, float value)
    {
        for (int i = 0; i < values.Length; i++) values[i] = value;
    }
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
        if (Environment.GetEnvironmentVariable("SUSPEND_GENERATOR_ENGINE") == "1") { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    IEnumerator Capture()
    {
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("SUSPEND_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        string output = Environment.GetEnvironmentVariable("SUSPEND_PROBE_OUT");
        Directory.CreateDirectory(output);
        gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("Start failed");
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + config.sampleRate };
        foreach (string key in new[] { "attached", "late" }) {
            var mixer = Resources.Load<AudioMixer>(key == "attached" ? "enabled" : "enabled_late");
            var groups = mixer.FindMatchingGroups("");
            var master = Array.Find(groups, g => g.name == "Master");
            var child = Array.Find(groups, g => g.name == "Child");
            GameObject go = null;
            SilentThenTone generator = null;
            void Attach() {
                go = new GameObject(key);
                var source = go.AddComponent<AudioSource>();
                source.outputAudioMixerGroup = child;
                generator = go.AddComponent<SilentThenTone>();
                source.Play();
            }
            if (key == "attached") Attach();
            using (var sink = new NativeArray<float>(2048, Allocator.Persistent))
            using (var buffer = new NativeArray<float>(2048, Allocator.Persistent))
            using (var pcm = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32"))))
            using (var group = new BinaryWriter(File.Create(Path.Combine(output, key + ".master.f32")))) {
                var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", F);
                for (int block = 0; block < Blocks; block++) {
                    if (block == 80 && key == "late") Attach();
                    if (block == 82) generator.Amplitude = 0.125f;
                    yield return null;
                    Fill(sink, -3f);
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("Sink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                    if (block == 81) records.Add("\"" + key + ".callbackAfterSilence\":" + (generator.Calls > 0 ? "true" : "false"));
                    for (int i = 0; i < buffer.Length; i++) { pcm.Write(buffer[i]); group.Write(sink[i]); }
                }
            }
            records.Add("\"" + key + ".callbackAfterTone\":" + (generator.Calls > 0 ? "true" : "false"));
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\"" + key + suffix + "\":{\"file\":\"" + key + suffix + ".f32\",\"frames\":" + (Blocks * 1024) + ",\"channels\":2}");
            DestroyImmediate(go);
        }
        AudioRenderer.Stop();
        records.Add("\"complete\":true");
        File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Exit(0);
    }
}
public class SilentThenTone : MonoBehaviour
{
    public volatile float Amplitude;
    public volatile int Calls;
    void OnAudioFilterRead(float[] values, int channels) { Calls++; for (int i = 0; i < values.Length; i++) values[i] = Amplitude; }
}
