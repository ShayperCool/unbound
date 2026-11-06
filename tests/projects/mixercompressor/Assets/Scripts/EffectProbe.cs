using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class EffectProbe : MonoBehaviour
{
    // Each case is a private mixer: Master with the child Music. Tokens after the key:
    //   music=<signal>          the source signal (default dc:0.5)
    //   <Group>:<chain>         effect units in order; Att is the group's Attenuation unit
    //   <Group>.<Unit>.<Param>=v  overrides: effect parameters, Volume, bypass
    // Units: Comp (Compressor), EQ (ParamEQ), LPS (Lowpass Simple), HPS (Highpass Simple).
    public static readonly string[][] Cases = {
        new[] { "plain", "Music:Att" },
        // Compressor static curve: a 0..2 ramp with the fastest Attack and Release.
        new[] { "comp_ramp_default", "music=ramp", "Music.Comp.Attack=10", "Music.Comp.Release=20" },
        new[] { "comp_ramp_thr20", "music=ramp", "Music.Comp.Threshold=-20", "Music.Comp.Attack=10", "Music.Comp.Release=20" },
        new[] { "comp_ramp_thr40", "music=ramp", "Music.Comp.Threshold=-40", "Music.Comp.Attack=10", "Music.Comp.Release=20" },
        new[] { "comp_ramp_makeup", "music=ramp", "Music.Comp.Threshold=-20", "Music.Comp.Attack=10", "Music.Comp.Release=20", "Music.Comp.Make up gain=10" },
        // Attack and release: a step from 0.05 to the level at 4096..16384.
        new[] { "comp_default", "music=step:1" },
        new[] { "comp_default_half", "music=step:0.5" },
        new[] { "comp_step_thr20", "music=step:0.5", "Music.Comp.Threshold=-20" },
        new[] { "comp_attack10", "music=step:0.5", "Music.Comp.Threshold=-20", "Music.Comp.Attack=10" },
        new[] { "comp_attack200", "music=step:0.5", "Music.Comp.Threshold=-20", "Music.Comp.Attack=200" },
        new[] { "comp_release20", "music=step:0.5", "Music.Comp.Threshold=-20", "Music.Comp.Release=20" },
        new[] { "comp_release1000", "music=step:0.5", "Music.Comp.Threshold=-20", "Music.Comp.Release=1000" },
        new[] { "comp_makeup30", "music=step:0.5", "Music.Comp.Threshold=-20", "Music.Comp.Make up gain=30" },
        // Detector: tones, one channel, noise.
        new[] { "comp_tone1k", "music=tone:0.5:1000", "Music.Comp.Threshold=-20" },
        new[] { "comp_tone100", "music=tone:0.5:100", "Music.Comp.Threshold=-20" },
        new[] { "comp_left", "music=stepL:0.5", "Music.Comp.Threshold=-20" },
        new[] { "comp_right", "music=stepR:0.5", "Music.Comp.Threshold=-20" },
        new[] { "comp_noise", "music=noise:0.5", "Music.Comp.Threshold=-30" },
        new[] { "comp_prefader", "music=step:0.5", "Music:Comp,Att", "Music.Volume=-12", "Music.Comp.Threshold=-20" },
        new[] { "comp_unset_times", "music=step:0.5", "Music.Comp.Threshold=-30", "Music.Comp.Make up gain=6" },
        new[] { "comp_bypass", "music=step:0.5", "Music.Comp.Threshold=-20", "Music.Comp.bypass=1" },
        new[] { "comp_group_bypass", "music=step:0.5", "Music.Comp.Threshold=-20", "Music.bypass=1" },
        new[] { "comp_startup", "music=dc0", "Music.Comp.Threshold=-20" },
        new[] { "comp_startup_bypass", "music=dc0", "Music.Comp.Threshold=-20", "Music.Comp.bypass=1" },
        new[] { "comp_thr60", "music=step:0.5", "Music.Comp.Threshold=-60" },
        // ParamEQ.
        new[] { "eq_default", "music=imp", "Music:Att,EQ" },
        new[] { "eq_gain_only", "music=imp", "Music:Att,EQ", "Music.EQ.Frequency gain=2" },
        new[] { "eq_boost", "music=imp", "Music:Att,EQ", "Music.EQ.Center freq=1000", "Music.EQ.Frequency gain=3" },
        new[] { "eq_cut", "music=imp", "Music:Att,EQ", "Music.EQ.Center freq=1000", "Music.EQ.Frequency gain=0.05" },
        new[] { "eq_narrow", "music=imp", "Music:Att,EQ", "Music.EQ.Center freq=3000", "Music.EQ.Octave range=0.2", "Music.EQ.Frequency gain=2" },
        new[] { "eq_wide", "music=imp", "Music:Att,EQ", "Music.EQ.Center freq=300", "Music.EQ.Octave range=5", "Music.EQ.Frequency gain=0.5" },
        new[] { "eq_low", "music=imp", "Music:Att,EQ", "Music.EQ.Center freq=20", "Music.EQ.Frequency gain=2" },
        new[] { "eq_high", "music=imp", "Music:Att,EQ", "Music.EQ.Center freq=22000", "Music.EQ.Frequency gain=2" },
        new[] { "eq_sweep", "music=sweep", "Music:Att,EQ", "Music.EQ.Center freq=1000", "Music.EQ.Frequency gain=3" },
        new[] { "eq_bypass", "music=imp", "Music:Att,EQ", "Music.EQ.Frequency gain=3", "Music.EQ.bypass=1" },
        new[] { "eq_startup", "music=dc0", "Music:Att,EQ", "Music.EQ.Center freq=100", "Music.EQ.Frequency gain=3" },
        new[] { "eq_startup_group_bypass", "music=dc0", "Music:Att,EQ", "Music.EQ.Center freq=100", "Music.EQ.Frequency gain=3", "Music.bypass=1" },
        // Lowpass Simple and Highpass Simple.
        new[] { "lps_default", "music=imp", "Music:Att,LPS" },
        new[] { "lps_500", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=500" },
        new[] { "lps_10", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=10" },
        new[] { "lps_22000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=22000" },
        new[] { "lps_noise", "music=noise:0.5", "Music:Att,LPS", "Music.LPS.Cutoff freq=2000" },
        new[] { "lps_bypass", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=500", "Music.LPS.bypass=1" },
        new[] { "lps_startup", "music=dc0", "Music:Att,LPS", "Music.LPS.Cutoff freq=500" },
        new[] { "lps_startup_bypass", "music=dc0", "Music:Att,LPS", "Music.LPS.Cutoff freq=500", "Music.LPS.bypass=1" },
        new[] { "lps_8000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=8000" },
        new[] { "lps_11000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=11000" },
        new[] { "lps_14000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=14000" },
        new[] { "lps_17000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=17000" },
        new[] { "lps_19000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=19000" },
        new[] { "lps_21000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=21000" },
        new[] { "lps_21900", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=21900" },
        new[] { "lps_20000", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=20000" },
        new[] { "lps_21999", "music=imp", "Music:Att,LPS", "Music.LPS.Cutoff freq=21999" },
        new[] { "hps_default", "music=imp", "Music:Att,HPS" },
        new[] { "hps_500", "music=imp", "Music:Att,HPS", "Music.HPS.Cutoff freq=500" },
        new[] { "hps_10", "music=imp", "Music:Att,HPS", "Music.HPS.Cutoff freq=10" },
        new[] { "hps_22000", "music=imp", "Music:Att,HPS", "Music.HPS.Cutoff freq=22000" },
        new[] { "hps_noise", "music=noise:0.5", "Music:Att,HPS", "Music.HPS.Cutoff freq=2000" },
        new[] { "hps_bypass", "music=imp", "Music:Att,HPS", "Music.HPS.Cutoff freq=500", "Music.bypass=1" },
        new[] { "hps_startup", "music=dc0", "Music:Att,HPS", "Music.HPS.Cutoff freq=500" },
        new[] { "hps_11000", "music=imp", "Music:Att,HPS", "Music.HPS.Cutoff freq=11000" },
        new[] { "hps_20000", "music=imp", "Music:Att,HPS", "Music.HPS.Cutoff freq=20000" },
        new[] { "hps_21999", "music=imp", "Music:Att,HPS", "Music.HPS.Cutoff freq=21999" },
    };
    public const int BlockCount = 32;
    public const int ClipFrames = BlockCount * 1024;
    public static string[] Selected()
    {
        var only = Environment.GetEnvironmentVariable("EFFECT_PROBE_CASES");
        var keys = new List<string>();
        foreach (var c in Cases) if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), c[0]) >= 0) keys.Add(c[0]);
        return keys.ToArray();
    }
    public static string[] Tokens(string key) => Array.Find(Cases, c => c[0] == key);
    public static string Option(string key, string name, string fallback)
    {
        foreach (var token in Tokens(key)) if (token.StartsWith(name)) return token.Substring(name.Length);
        return fallback;
    }
    public static string Chain(string key, string group) =>
        Option(key, group + ":", group == "Music" ? "Att,Comp" : group == "Master" ? "Att" : null);
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("EFFECT_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine) return;
        foreach (var existing in UnityEngine.Object.FindObjectsOfType<EffectProbe>()) DestroyImmediate(existing.gameObject);
        var probe = new GameObject("EffectProbe").AddComponent<EffectProbe>();
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
    // Signals start after the first DSP block unless named *0; steps run 4096..16384.
    static float[] Signal(string spec, int rate)
    {
        var data = new float[ClipFrames * 2];
        var parts = spec.Split(':');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float amplitude = parts.Length > 1 ? float.Parse(parts[1], inv) : 0f;
        double frequency = parts.Length > 2 ? double.Parse(parts[2], inv) : 1000;
        uint seed = 12345;
        for (int f = 0; f < ClipFrames; f++) {
            float l = 0, r = 0;
            bool on = f >= 4096 && f < 16384;
            switch (parts[0]) {
                case "dc": if (f >= 1024) { l = amplitude; r = -0.5f * amplitude; } break;
                case "dc0": l = 0.5f; r = -0.25f; break;
                case "step": if (f >= 1024) { l = on ? amplitude : 0.05f; r = -l; } break;
                case "stepL": if (f >= 1024) { l = on ? amplitude : 0.05f; r = 0.05f; } break;
                case "stepR": if (f >= 1024) { r = on ? amplitude : 0.05f; l = 0.05f; } break;
                case "ramp": if (f >= 1024) { l = 2f * (f - 1024) / (ClipFrames - 1024); r = -0.5f * l; } break;
                case "tone": if (f >= 1024) l = r = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * (f - 1024) / rate)); if (!on) { l *= 0.1f; r *= 0.1f; } break;
                case "noise":
                    seed = seed * 1664525u + 1013904223u; l = amplitude * ((seed >> 8) / 8388608f - 1f);
                    seed = seed * 1664525u + 1013904223u; r = amplitude * ((seed >> 8) / 8388608f - 1f);
                    if (f < 1024) l = r = 0;
                    break;
                case "imp": l = f == 2048 ? 0.5f : f == 9000 ? -0.25f : 0; r = f == 3072 ? -0.5f : f == 9000 ? 0.125f : 0; break;
                case "sweep":
                    if (f >= 1024) {
                        double t = (double)(f - 1024) / rate, T = (double)(ClipFrames - 1024) / rate, k = Math.Log(20000.0 / 20.0);
                        l = r = (float)(0.25 * Math.Sin(2 * Math.PI * 20 * T / k * (Math.Exp(t / T * k) - 1)));
                        r *= -1;
                    }
                    break;
                case "none": break;
                default: throw new Exception("Unknown signal " + spec);
            }
            data[2 * f] = l; data[2 * f + 1] = r;
        }
        return data;
    }
    System.Collections.IEnumerator Capture()
    {
        if (m_Captured) yield break;
        m_Captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("EFFECT_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("AudioSettings.Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("EFFECT_PROBE_OUT"); Directory.CreateDirectory(output);
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Save();
        foreach (string key in Selected()) {
            Debug.Log("Effect case " + key);
            var mixer = Resources.Load<AudioMixer>(key); if (!mixer) throw new Exception("Missing mixer " + key);
            var go = new GameObject(key);
            var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Music");
            if (!source.outputAudioMixerGroup) throw new Exception("Missing group Music");
            source.spatialBlend = 0; source.volume = 1;
            var clip = AudioClip.Create(key, ClipFrames, 2, rate, false);
            if (!clip.SetData(Signal(Option(key, "music=", "dc:0.5"), rate), 0)) throw new Exception("SetData failed");
            source.clip = clip; source.Play();
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                if (Engine) FlushEngine();
                for (int block = 0; block < BlockCount; block++) {
                    if (!Engine) yield return null;
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("AddMixerGroupSink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("AudioRenderer.Render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                    if (Engine) FlushEngine();
                }
            }
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(BlockCount*1024)+",\"channels\":2}");
            Save(); buffer.Dispose();
            source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
