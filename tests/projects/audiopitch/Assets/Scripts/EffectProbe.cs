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
        new[] { "default_imp", "music=imp" },
        new[] { "default_silence", "music=none" },
        new[] { "default_noise", "music=noise:0.25" },
        new[] { "explicit_defaults", "music=noise:0.25", "Music.Shift.Pitch=1", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0" },
        new[] { "down_imp", "music=imp", "Music.Shift.Pitch=0.5" },
        new[] { "down_noise", "music=noise:0.25", "Music.Shift.Pitch=0.5" },
        new[] { "down_tone", "music=tone:0.25:997", "Music.Shift.Pitch=0.5" },
        new[] { "down_dc", "music=dc:0.25", "Music.Shift.Pitch=0.5" },
        new[] { "up_imp", "music=imp", "Music.Shift.Pitch=2" },
        new[] { "up_noise", "music=noise:0.25", "Music.Shift.Pitch=2" },
        new[] { "up_tone", "music=tone:0.25:997", "Music.Shift.Pitch=2" },
        new[] { "up_dc", "music=dc:0.25", "Music.Shift.Pitch=2" },
        new[] { "lower_imp", "music=imp", "Music.Shift.Pitch=0.75" },
        new[] { "lower_noise", "music=noise:0.25", "Music.Shift.Pitch=0.75" },
        new[] { "lower_tone", "music=tone:0.25:997", "Music.Shift.Pitch=0.75" },
        new[] { "lower_dc", "music=dc:0.25", "Music.Shift.Pitch=0.75" },
        new[] { "higher_imp", "music=imp", "Music.Shift.Pitch=1.5" },
        new[] { "higher_noise", "music=noise:0.25", "Music.Shift.Pitch=1.5" },
        new[] { "higher_tone", "music=tone:0.25:997", "Music.Shift.Pitch=1.5" },
        new[] { "higher_dc", "music=dc:0.25", "Music.Shift.Pitch=1.5" },
        new[] { "group_bypass", "music=noise:0.25", "Music.bypass=1" },
        new[] { "slot_bypass", "music=noise:0.25", "Music.Shift.bypass=1" },
        new[] { "source_bypass", "music=noise:0.25", "bypass=1" },
        new[] { "end_imp", "music=endimp", "clip=16384", "Music.Shift.Pitch=0.75" },
        new[] { "end_tone", "music=tone:0.25:997", "clip=16384", "Music.Shift.Pitch=1.5" },
        new[] { "reset_noise", "music=noise:0.25", "asset=default_noise", "reset=1" },
        new[] { "invalid_pitch_low", "music=noise:0.25", "set:Pitch=-1" },
        new[] { "invalid_pitch_high", "music=noise:0.25", "set:Pitch=3" },
        new[] { "absent_missing", "music=imp", "set:Missing=0.5" },
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
        Option(key, group + ":", group == "Music" ? "Att,Shift" : group == "Master" ? "Att" : null);
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
                case "ramp": if (f >= 1024) { l = 0.25f * (f - 1024) / (ClipFrames - 1024); r = -0.5f * l; } break;
                case "tone": if (f >= 1024) l = r = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * (f - 1024) / rate)); if (!on) { l *= 0.1f; r *= 0.1f; } break;
                case "noise":
                    seed = seed * 1664525u + 1013904223u; l = amplitude * ((seed >> 8) / 8388608f - 1f);
                    seed = seed * 1664525u + 1013904223u; r = amplitude * ((seed >> 8) / 8388608f - 1f);
                    if (f < 1024) l = r = 0;
                    break;
                case "endimp": l = f == 16383 ? 0.5f : 0; r = f == 16128 ? -0.25f : 0; break;
                case "imp": l = f == 1023 ? 0.25f : f == 2048 ? 0.5f : f == 9000 ? -0.25f : 0; r = f == 1280 ? -0.25f : f == 3072 ? -0.5f : f == 9000 ? 0.125f : 0; break;
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
            if (f >= 16384) l = r = 0;
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
            BlockCount = int.Parse(Option(key, "blocks=", "32")); ClipFrames = int.Parse(Option(key, "clip=", (BlockCount * 1024).ToString()));
            Debug.Log("Effect case " + key);
            if (Option(key, "reset=", "0") == "1") {
                AudioRenderer.Stop();
                if (!AudioSettings.Reset(config)) throw new Exception("case reset failed");
                if (!AudioRenderer.Start()) throw new Exception("case restart failed");
            }
            var mixer = Resources.Load<AudioMixer>(Option(key, "asset=", key)); if (!mixer) throw new Exception("Missing mixer " + key);
            foreach (var token in Tokens(key)) if (token.StartsWith("set:")) {
                var pair = token.Substring(4).Split('=');
                bool ok = mixer.SetFloat(pair[0], float.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                bool got = mixer.GetFloat(pair[0], out float value);
                records.Add("\""+key+".set\":"+(ok ? "true" : "false"));
                records.Add("\""+key+".get\":"+(got ? "true" : "false"));
                records.Add("\""+key+".value\":"+value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (Chain(key, "Music").Contains("Shift"))
                foreach (var name in new[] { "Pitch", "FFT size", "Overlap", "Max channels" }) {
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
