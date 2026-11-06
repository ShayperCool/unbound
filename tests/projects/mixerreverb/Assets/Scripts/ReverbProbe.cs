using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class ReverbProbe : MonoBehaviour
{
    // Every case uses one SFX Reverb on the Child group unless its name says otherwise.
    // Overrides are "Parameter=value" pairs applied to the Editor defaults; "Wet" enables wet mixing.
    // Steady-state signals begin after the first DSP block; the startup cases probe that block.
    public static readonly string[][] Cases = {
        new[] { "dry_stereo" },
        new[] { "default" },
        new[] { "default_mono" },
        new[] { "dry_m600", "Dry Level=-600" },
        new[] { "dry_m10000", "Dry Level=-10000" },
        new[] { "room0", "Room=0" },
        new[] { "room0_mono", "Room=0" },
        new[] { "room_m600", "Room=-600", "Reflections=0" },
        new[] { "wet_only", "Room=0", "Dry Level=-10000", "Reflections=0" },
        new[] { "roomhf_m1200", "Room=0", "Room HF=-1200", "Reflections=0" },
        new[] { "hfref1000", "Room=0", "Room HF=-1200", "HF Reference=1000", "Reflections=0" },
        new[] { "roomlf_m1200", "Room=0", "Room LF=-1200", "LF Reference=500", "Reflections=0" },
        new[] { "decay02", "Room=0", "Decay Time=0.2" },
        new[] { "decay3", "Room=0", "Decay Time=3" },
        new[] { "decay20", "Room=0", "Decay Time=20", "Decay HF Ratio=1" },
        new[] { "decayhf01", "Room=0", "Decay HF Ratio=0.1" },
        new[] { "decayhf2", "Room=0", "Decay HF Ratio=2" },
        new[] { "refl0", "Room=0", "Reflections=0" },
        new[] { "refl1000", "Room=0", "Reflections=1000", "Reverb=-10000" },
        new[] { "reflect01", "Room=0", "Reflections=0", "Reflect Delay=0.1" },
        new[] { "reflect03", "Room=0", "Reflections=0", "Reflect Delay=0.3", "Reverb Delay=0.1" },
        new[] { "revdelay0", "Room=0", "Reflections=0", "Reverb Delay=0" },
        new[] { "zero_delays", "Room=0", "Reflections=0", "Reflect Delay=0", "Reverb Delay=0" },
        new[] { "reverb_m1000", "Room=0", "Reverb=-1000" },
        new[] { "reverb2000", "Room=0", "Reverb=2000", "Dry Level=-10000" },
        new[] { "diffusion0", "Room=0", "Diffusion=0" },
        new[] { "diffusion50", "Room=0", "Diffusion=50" },
        new[] { "density0", "Room=0", "Density=0" },
        new[] { "density50", "Room=0", "Density=50" },
        new[] { "preset", "Dry Level=-300", "Room=-1000", "Room HF=-300", "Decay Time=1.49", "Decay HF Ratio=0.59", "Reflections=-1219", "Reflect Delay=0.007", "Reverb=441", "Reverb Delay=0.011", "Diffusion=80", "Density=70", "HF Reference=4000", "Room LF=-200", "LF Reference=150" },
        new[] { "tone_stereo", "Room=0", "Reflections=-1000" },
        new[] { "silence", "Room=0" },
        new[] { "wetmix0", "Room=0", "Reflections=0", "Wet=0" },
        new[] { "wetmix_m6", "Room=0", "Reflections=0", "Wet=-6" },
        new[] { "wetmix_dry", "Wet=-6" },
        new[] { "wetmix_m68", "Room=0", "Reflections=0", "Wet=-68" },
        new[] { "wetmix_m70", "Room=0", "Reflections=0", "Wet=-70" },
        new[] { "effect_bypass", "Room=0" },
        new[] { "group_bypass", "Room=0" },
        new[] { "reverb_lp", "Room=0" },
        new[] { "volume_reverb", "Room=0" },
        new[] { "parent_reverb", "Room=0" },
        new[] { "sum_reverb", "Room=0" },
        new[] { "set_early", "Room=-10000" },
        new[] { "startup", "Room=0", "Reflections=0" },
        new[] { "startup_wet", "Room=0", "Reflections=0", "Wet=-6" },
        // Bypassed reverbs with the startup signal: does the group still fade in its first block?
        new[] { "bypass_startup_effect", "Room=0", "Reflections=0" },
        new[] { "bypass_startup_group", "Room=0", "Reflections=0" },
    };
    public static readonly string[] Parameters = {
        "Dry Level", "Room", "Room HF", "Decay Time", "Decay HF Ratio", "Reflections", "Reflect Delay",
        "Reverb", "Reverb Delay", "Diffusion", "Density", "HF Reference", "Room LF", "LF Reference"
    };
    // Exposed by set_early and set through AudioMixer.SetFloat before the first render.
    public static readonly string[] Exposed = { "Room", "Decay Time" };
    public static readonly float[] ExposedValues = { 0f, 0.5f };
    public static string[] Selected()
    {
        var only = Environment.GetEnvironmentVariable("REVERB_PROBE_CASES");
        var keys = new List<string>();
        foreach (var c in Cases) if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), c[0]) >= 0) keys.Add(c[0]);
        return keys.ToArray();
    }
    public static string[] Overrides(string key) => Array.Find(Cases, c => c[0] == key);
    public static int Blocks(string key) => 24;
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("REVERB_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine) return;
        foreach (var existing in UnityEngine.Object.FindObjectsOfType<ReverbProbe>()) DestroyImmediate(existing.gameObject);
        var probe = new GameObject("ReverbProbe").AddComponent<ReverbProbe>();
        try { var capture = probe.Capture(); while (capture.MoveNext()) throw new Exception("Engine capture yielded"); }
        catch (Exception error) { Debug.LogException(error); Exit(1); }
    }
    bool m_Captured;
    static void Exit(int code)
    {
        if (Engine) { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    void FlushEngine()
    {
        var assembly = Type.GetType("Unbound.Runtime.AudioBackend, UnityEngine.CoreModule", true).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", Flags).Invoke(null, null);
    }
    System.Collections.IEnumerator Start()
    {
        var routine = Capture();
        for (;;) {
            object next;
            try { if (!routine.MoveNext()) yield break; next = routine.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            yield return next;
        }
    }
    public const int ClipFrames = 12288;
    static readonly int[] StartupFrames = { 1, 3, 7, 15, 31, 63, 127, 255, 511, 1023 };
    static float[] Signal(string key, int channels, int rate)
    {
        var data = new float[ClipFrames * channels];
        for (int f = 0; f < ClipFrames; f++) for (int c = 0; c < channels; c++) {
            // Separate left and right impulses, then a simultaneous pair that tests the mono sum.
            float value = c == 0 ? (f == 2048 ? 0.25f : f == 5120 ? 0.2f : 0) : (f == 3072 ? -0.125f : f == 5120 ? -0.1f : 0);
            if (key.StartsWith("tone")) value = f >= 1024 && f < 9216 ? (float)(0.125 * Math.Sin(2 * Math.PI * (c == 0 ? 400 : 1700) * f / rate)) : 0;
            if (key.StartsWith("startup") || key.StartsWith("bypass_startup")) value = c == 0 && (Array.IndexOf(StartupFrames, f) >= 0 || f == 2048) ? 0.25f : 0;
            if (key == "silence") value = 0;
            data[f * channels + c] = value;
        }
        return data;
    }
    System.Collections.IEnumerator Capture()
    {
        if (m_Captured) yield break;
        m_Captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("REVERB_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("AudioSettings.Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("REVERB_PROBE_OUT"); Directory.CreateDirectory(output);
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Save();
        foreach (string key in Selected()) {
            Debug.Log("Reverb case " + key);
            var mixer = Resources.Load<AudioMixer>(key); if (!mixer) throw new Exception("Missing mixer " + key);
            if (key == "set_early") for (int p = 0; p < Exposed.Length; p++)
                if (!mixer.SetFloat(Exposed[p], ExposedValues[p])) throw new Exception("SetFloat failed");
            int channels = key.EndsWith("_mono") ? 1 : 2;
            var go = new GameObject(key); var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = mixer.FindMatchingGroups("Child")[0];
            source.spatialBlend = 0; source.volume = 1;
            var clip = AudioClip.Create(key, ClipFrames, channels, rate, false);
            if (!clip.SetData(Signal(key, channels, rate), 0)) throw new Exception("SetData failed"); source.clip = clip;
            if (key == "sum_reverb") {
                var second = go.AddComponent<AudioSource>(); second.clip = clip; second.volume = 0.5f;
                second.outputAudioMixerGroup = source.outputAudioMixerGroup; second.Play();
            }
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
            source.Play();
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                if (Engine) FlushEngine();
                for (int block = 0; block < Blocks(key); block++) {
                    if (!Engine) yield return null;
                    if (block == 0 && key == "set_early")
                        for (int p = 0; p < Exposed.Length; p++) {
                            bool ok = mixer.GetFloat(Exposed[p], out float value);
                            records.Add("\""+key+"."+Exposed[p]+"\":\""+ok+":"+BitConverter.ToInt32(BitConverter.GetBytes(value),0)+"\"");
                        }
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("AddMixerGroupSink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("AudioRenderer.Render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                    if (Engine) FlushEngine();
                }
            }
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(Blocks(key)*1024)+",\"channels\":2}");
            Save(); buffer.Dispose(); source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
