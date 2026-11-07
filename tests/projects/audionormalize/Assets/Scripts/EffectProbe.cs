using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class EffectProbe : MonoBehaviour
{
    public static readonly string[][] Cases = {
        new[] { "plain", "music=imp", "Music:Att" },
        new[] { "default_imp", "music=imp", "blocks=64" },
        new[] { "default_silence", "music=none", "blocks=64" },
        new[] { "default_dc0", "music=dc0", "blocks=512" },
        new[] { "default_dc_tiny", "music=dc:0.00001", "blocks=64" },
        new[] { "default_dc_small", "music=dc:0.01", "blocks=64" },
        new[] { "default_dc_quarter", "music=dc:0.25", "blocks=512" },
        new[] { "default_dc_4", "music=dc:4", "blocks=64" },
        new[] { "default_dc_neg4", "music=dc:-4", "blocks=64" },
        new[] { "default_ramp", "music=ramp", "blocks=512" },
        new[] { "default_tone", "music=tone:0.25:997", "blocks=512" },
        new[] { "default_noise", "music=noise:0.25", "blocks=512" },
        new[] { "default_step", "music=step:0.5", "blocks=512" },
        new[] { "default_step_left", "music=stepL:0.5", "blocks=512" },
        new[] { "default_step_right", "music=stepR:0.5", "blocks=512" },
        new[] { "explicit_defaults", "music=noise:0.25", "blocks=512", "Music.Norm.Fade in time=5000", "Music.Norm.Lowest volume=0.1", "Music.Norm.Maximum amp=20" },
        new[] { "fade_0", "music=step:0.5", "blocks=128", "Music.Norm.Fade in time=0" },
        new[] { "fade_10", "music=step:0.5", "blocks=128", "Music.Norm.Fade in time=10" },
        new[] { "fade_100", "music=step:0.5", "blocks=128", "Music.Norm.Fade in time=100" },
        new[] { "fade_1000", "music=step:0.5", "blocks=128", "Music.Norm.Fade in time=1000" },
        new[] { "fade_20000", "music=step:0.5", "blocks=128", "Music.Norm.Fade in time=20000" },
        new[] { "invalid_fade_low", "music=step:0.5", "blocks=128", "set:Fade in time=-1" },
        new[] { "invalid_fade_high", "music=step:0.5", "blocks=128", "set:Fade in time=40000" },
        new[] { "lowest_0", "music=step:0.5", "blocks=128", "Music.Norm.Lowest volume=0" },
        new[] { "lowest_0p01", "music=step:0.5", "blocks=128", "Music.Norm.Lowest volume=0.01" },
        new[] { "lowest_0p5", "music=step:0.5", "blocks=128", "Music.Norm.Lowest volume=0.5" },
        new[] { "lowest_1", "music=step:0.5", "blocks=128", "Music.Norm.Lowest volume=1" },
        new[] { "invalid_lowest_low", "music=step:0.5", "blocks=128", "set:Lowest volume=-1" },
        new[] { "invalid_lowest_high", "music=step:0.5", "blocks=128", "set:Lowest volume=2" },
        new[] { "maximum_0", "music=step:0.5", "blocks=128", "Music.Norm.Maximum amp=0" },
        new[] { "maximum_1", "music=step:0.5", "blocks=128", "Music.Norm.Maximum amp=1" },
        new[] { "maximum_2", "music=step:0.5", "blocks=128", "Music.Norm.Maximum amp=2" },
        new[] { "maximum_100000", "music=step:0.5", "blocks=128", "Music.Norm.Maximum amp=100000" },
        new[] { "invalid_maximum_low", "music=step:0.5", "blocks=128", "set:Maximum amp=-1" },
        new[] { "invalid_maximum_high", "music=step:0.5", "blocks=128", "set:Maximum amp=200000" },
        new[] { "fast_imp_0", "music=imp", "mono=0", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_imp_1", "music=imp", "mono=1", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_noise_0", "music=noise:0.25", "mono=0", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_noise_1", "music=noise:0.25", "mono=1", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_tone_0", "music=tone:0.25:997", "mono=0", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_tone_1", "music=tone:0.25:997", "mono=1", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_dc_0", "music=dc:0.25", "mono=0", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_dc_1", "music=dc:0.25", "mono=1", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_stepl_0", "music=stepL:0.5", "mono=0", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "fast_stepl_1", "music=stepL:0.5", "mono=1", "blocks=128", "Music.Norm.Fade in time=100", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10" },
        new[] { "absent_missing", "music=imp", "set:Missing=0.5" },
        new[] { "group_bypass", "music=noise:0.25", "Music.bypass=1" },
        new[] { "slot_bypass", "music=noise:0.25", "Music.Norm.bypass=1" },
        new[] { "source_bypass", "music=noise:0.25", "bypass=1" },
        new[] { "master_route", "music=noise:0.25", "blocks=128", "Music:Att", "Master:Att,Norm", "Master.Norm.Fade in time=100" },
    };
    static int BlockCount, ClipFrames;
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
        Option(key, group + ":", group == "Music" ? "Att,Norm" : group == "Master" ? "Att" : null);
    const BindingFlags Normgs = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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
        typeof(Time).GetField("s_RealDeltaTime", Normgs).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", Normgs).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", Normgs).Invoke(null, null);
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
                case "ramp": if (f >= 1024) { l = 0.25f * (f - 1024) / (ClipFrames - 1024); r = -0.5f * l; } break;
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
            BlockCount = int.Parse(Option(key, "blocks=", "32")); ClipFrames = BlockCount * 1024;
            Debug.Log("Effect case " + key);
            var mixer = Resources.Load<AudioMixer>(key); if (!mixer) throw new Exception("Missing mixer " + key);
            foreach (var token in Tokens(key)) if (token.StartsWith("set:")) {
                var pair = token.Substring(4).Split('=');
                bool ok = mixer.SetFloat(pair[0], float.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                bool got = mixer.GetFloat(pair[0], out float value);
                records.Add("\""+key+".set\":"+(ok ? "true" : "false"));
                records.Add("\""+key+".get\":"+(got ? "true" : "false"));
                records.Add("\""+key+".value\":"+value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (Chain(key, "Music").Contains("Norm"))
                foreach (var name in new[] { "Fade in time", "Lowest volume", "Maximum amp" }) {
                    mixer.GetFloat(name, out float v);
                    records.Add("\""+key+".mixer_"+name+"\":"+v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            var go = new GameObject(key);
            var source = go.AddComponent<AudioSource>();
            source.bypassEffects = Option(key, "bypass=", "0") == "1";
            source.bypassListenerEffects = Option(key, "listener_bypass=", "0") == "1";
            source.outputAudioMixerGroup = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Music");
            if (!source.outputAudioMixerGroup) throw new Exception("Missing group Music");
            source.spatialBlend = 0; source.volume = 1;
            int channels = Option(key, "mono=", "0") == "1" ? 1 : 2;
            var clip = AudioClip.Create(key, ClipFrames, channels, rate, false);
            var data = Signal(Option(key, "music=", "dc:0.5"), rate);
            if (channels == 1) { var mono = new float[ClipFrames]; for (int f = 0; f < ClipFrames; f++) mono[f] = data[2*f]; data = mono; }
            if (!clip.SetData(data, 0)) throw new Exception("SetData failed");
            source.clip = clip; source.Play();
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Normgs);
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
            var silence = new NativeArray<float>(2048, Allocator.Persistent);
            for (int drain = 0; drain < 8; drain++) {
                if (!Engine) yield return null; else FlushEngine();
                if (!AudioRenderer.Render(silence)) throw new Exception("drain render failed");
            }
            silence.Dispose();
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
