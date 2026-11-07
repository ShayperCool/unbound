using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class StateProbe : MonoBehaviour
{
    public static readonly string[] Cases = {
        "echo_hold", "echo_disabled", "echo_fresh", "lp_hold", "lp_disabled", "lp_fresh",
        "hp_hold", "hp_disabled", "hp_fresh", "reverb_hold", "reverb_disabled", "reverb_fresh",
        "duck_hold", "duck_disabled", "duck_fresh", "snap_instant", "snap_timed", "snap_frozen",
        "snap_unscaled_instant", "gain_live", "snap_live", "clear_live", "snap_half", "snap_override", "snap_clear", "snap_early", "snap_selected"
    };
    public const int Blocks = 512;
    static bool Engine => Environment.GetEnvironmentVariable("SUSPEND_STATE_ENGINE") == "1";
    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    BinaryWriter listener;
    public void Listen(float[] data) { if (listener != null) foreach (float x in data) listener.Write(x); }
    System.Collections.IEnumerator Start()
    {
        var run = Capture();
        for (;;) {
            object next;
            try { if (!run.MoveNext()) yield break; next = run.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            yield return next;
        }
    }
    static void Exit(int code) {
        if (Engine) { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    System.Collections.IEnumerator Capture()
    {
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("SUSPEND_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("SUSPEND_PROBE_OUT"); Directory.CreateDirectory(output);
        gameObject.AddComponent<AudioListener>(); gameObject.AddComponent<StateListener>().Probe = this;
        if (!AudioRenderer.Start()) throw new Exception("Renderer start failed");
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        var arrays = new List<NativeArray<float>>();
        foreach (string key in Cases) {
            Debug.Log("State case " + key);
            Time.timeScale = key == "snap_frozen" || key == "snap_unscaled_instant" ? 0 : key == "snap_half" ? 0.5f : 1;
            var mixer = Resources.Load<AudioMixer>(key);
            var groups = mixer.FindMatchingGroups("");
            var child = Array.Find(groups, g => g.name == "Child");
            var master = Array.Find(groups, g => g.name == "Master");
            void Value(string stage) {
                if (!mixer.GetFloat("Gain", out float gain)) throw new Exception("Missing Gain");
                string name = key + "." + stage;
                using (var writer = new BinaryWriter(File.Create(Path.Combine(output, name + ".f32")))) writer.Write(gain);
                records.Add("\"" + name + "\":{\"file\":\"" + name + ".f32\",\"frames\":1,\"channels\":1}");
            }
            Value("loaded");
            // Explicitly establish the same snapshot on both runtimes for the state experiment.
            // snap_selected separately observes the saved Editor selection at startup.
            if (key != "snap_selected") mixer.FindSnapshot("Base").TransitionTo(0);
            mixer.updateMode = key == "snap_unscaled_instant" ? AudioMixerUpdateMode.UnscaledTime : AudioMixerUpdateMode.Normal;
            var go = new GameObject(key); var source = go.AddComponent<AudioSource>();
            var clip = AudioClip.Create(key, 4096, 2, rate, false);
            var signal = new float[8192];
            for (int i = 1024; i < 4096; i++) {
                signal[i * 2] = key.StartsWith("duck") ? 0.5f : i == 2048 ? 0.25f : 0;
                signal[i * 2 + 1] = key.StartsWith("duck") ? -0.25f : i == 3072 ? -0.125f : 0;
            }
            clip.SetData(signal, 0); source.clip = clip; source.outputAudioMixerGroup = child;
            var wake = go.AddComponent<AudioSource>(); var wakeClip = AudioClip.Create("wake", 1024, 2, rate, false);
            var wakeData = new float[2048];
            if (key.StartsWith("snap") || key.StartsWith("duck") || key.EndsWith("live")) Array.Fill(wakeData, 0.001f);
            wakeClip.SetData(wakeData, 0); wake.clip = wakeClip; wake.loop = true; wake.outputAudioMixerGroup = child;
            var sink = new NativeArray<float>(2048, Allocator.Persistent); arrays.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent); arrays.Add(buffer);
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", F);
            if (!key.EndsWith("fresh")) source.Play();
            if (key.EndsWith("live")) wake.Play();
            using (var lw = new BinaryWriter(File.Create(Path.Combine(output, key + ".listener.f32"))))
            using (var mw = new BinaryWriter(File.Create(Path.Combine(output, key + ".master.f32"))))
            using (var pw = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32")))) {
                listener = lw;
                for (int block = 0; block < Blocks; block++) {
                    yield return null;
                    if (block == 8) {
                        Value("initial");
                        if (key == "snap_early") mixer.FindSnapshot("Other").TransitionTo(10);
                    }
                    if (block == 160 && ((key.StartsWith("snap") && key != "snap_selected" && key != "snap_early") || key == "gain_live" || key == "clear_live")) {
                        if (key == "snap_override" || key == "snap_clear" || key == "clear_live") mixer.SetFloat("Gain", -3);
                        if (key == "gain_live") mixer.SetFloat("Gain", -12);
                        else mixer.FindSnapshot("Other").TransitionTo(key == "snap_instant" || key == "snap_unscaled_instant" || key == "snap_live" ? 0 : 4);
                        Value("requested");
                    }
                    if (block == 161 || block == 192 || block == 255 || block == 257 || block == 320 || block == 511) Value("block" + block);
                    if (block == 256) {
                        Value("beforewake");
                        if (key == "snap_clear" || key == "clear_live") { mixer.ClearFloat("Gain"); Value("clear"); }
                        wake.Play(); Value("afterplay");
                    }
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("Sink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                    for (int i = 0; i < buffer.Length; i++) { pw.Write(buffer[i]); mw.Write(sink[i]); }
                }
                listener = null;
            }
            foreach (string suffix in new[] { "", ".master", ".listener" })
                records.Add("\"" + key + suffix + "\":{\"file\":\"" + key + suffix + ".f32\",\"frames\":" + new FileInfo(Path.Combine(output, key + suffix + ".f32")).Length / 8 + ",\"channels\":2}");
            source.Stop(); wake.Stop(); mixer.SetFloat("Gain", -80);
            DestroyImmediate(go); DestroyImmediate(clip); DestroyImmediate(wakeClip); Save();
        }
        Time.timeScale = 1; AudioRenderer.Stop(); foreach (var array in arrays) array.Dispose(); Save(); Exit(0);
    }
}
public class StateListener : MonoBehaviour
{
    public StateProbe Probe;
    void OnAudioFilterRead(float[] data, int channels) => Probe.Listen(data);
}
