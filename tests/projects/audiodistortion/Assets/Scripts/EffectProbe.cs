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
        new[] { "mix_set_low", "music=ramp", "set=-1" },
        new[] { "mix_set_high", "music=ramp", "set=2" },
        new[] { "mix_set_mid", "music=ramp", "set=0.37" },
        new[] { "mix_clear", "music=ramp", "set=0.9", "clear=1" },
        new[] { "mix_startup", "music=dc0" },
        new[] { "source_startup", "Music:Att", "filter=default", "music=dc0" },
        new[] { "plain", "Music:Att", "music=ramp" },
        new[] { "mix_default", "music=ramp" },
        new[] { "mix_0", "music=ramp", "Music.Dist.Level=0" },
        new[] { "mix_01", "music=ramp", "Music.Dist.Level=0.1" },
        new[] { "mix_025", "music=ramp", "Music.Dist.Level=0.25" },
        new[] { "mix_05", "music=ramp", "Music.Dist.Level=0.5" },
        new[] { "mix_075", "music=ramp", "Music.Dist.Level=0.75" },
        new[] { "mix_09", "music=ramp", "Music.Dist.Level=0.9" },
        new[] { "mix_1", "music=ramp", "Music.Dist.Level=1" },
        new[] { "mix_dc_000001", "music=dc:0.00001" },
        new[] { "mix_dc_05", "music=dc:0.5" },
        new[] { "mix_dc_4", "music=dc:4" },
        new[] { "mix_imp", "music=imp" },
        new[] { "mix_noise_08", "music=noise:0.8" },
        new[] { "mix_tone_08_1000", "music=tone:0.8:1000" },
        new[] { "mix_route_15", "music=ramp", "Music.bypass=1" },
        new[] { "mix_route_16", "music=ramp", "Music.Dist.bypass=1" },
        new[] { "mix_route_17", "music=ramp", "Music:Dist,Att", "Music.Volume=-12" },
        new[] { "mix_route_18", "music=ramp", "Master:Att,Dist", "Music:Att" },
        new[] { "source_default", "Music:Att", "filter=default", "music=ramp" },
        new[] { "source_0", "Music:Att", "filter=0", "music=ramp" },
        new[] { "source_025", "Music:Att", "filter=0.25", "music=ramp" },
        new[] { "source_05", "Music:Att", "filter=0.5", "music=ramp" },
        new[] { "source_075", "Music:Att", "filter=0.75", "music=ramp" },
        new[] { "source_1", "Music:Att", "filter=1", "music=ramp" },
        new[] { "source_minus1", "Music:Att", "filter=-1", "music=ramp" },
        new[] { "source_2", "Music:Att", "filter=2", "music=ramp" },
        new[] { "source_mono", "Music:Att", "filter=default", "music=ramp", "mono=1" },
        new[] { "source_disabled", "Music:Att", "filter=default", "music=ramp", "disabled=1" },
        new[] { "source_bypass", "Music:Att", "filter=default", "music=ramp", "bypass=1" },
        new[] { "source_listener_bypass", "Music:Att", "filter=default", "music=ramp", "listener_bypass=1" },
        new[] { "source_serialized", "Music:Att", "filter=default", "music=ramp", "serialized=1" },
        new[] { "source_before_script", "Music:Att", "filter=default", "music=ramp", "script=after" },
        new[] { "source_after_script", "Music:Att", "filter=default", "music=ramp", "script=before" },
        new[] { "source_imp", "Music:Att", "filter=default", "music=imp" },
        new[] { "source_dc", "Music:Att", "filter=default", "music=dc:0.5" },
        new[] { "source_small", "Music:Att", "filter=default", "music=dc:0.00001" },
        new[] { "source_large", "Music:Att", "filter=default", "music=dc:4" },
        new[] { "slot_wet_zero", "music=noise:0.25", "Music.Dist.Wet=-80" },
        new[] { "slot_wet_quarter", "music=noise:0.25", "Music.Dist.Wet=-12" },
        new[] { "slot_wet_half", "music=noise:0.25", "Music.Dist.Wet=-6" },
        new[] { "slot_wet_threequarter", "music=noise:0.25", "Music.Dist.Wet=-2.5" },
        new[] { "slot_wet_full", "music=noise:0.25", "Music.Dist.Wet=0" },
        new[] { "slot_wet_above", "music=noise:0.25", "Music.Dist.Wet=6" },
        new[] { "slot_wet_quiet", "music=noise:0.25", "Music.Dist.Wet=-70" },
        new[] { "slot_wet_audible", "music=noise:0.25", "Music.Dist.Wet=-68" },
        new[] { "slot_wet_set", "music=noise:0.25", "wetset=-6", "Music.Dist.Wet=-6" },
        new[] { "slot_wet_set_above", "music=noise:0.25", "wetset=6", "Music.Dist.Wet=-6" },
        new[] { "slot_wet_startup", "music=dc0", "Music.Dist.Wet=-6" },
        new[] { "slot_wet_before", "music=dc0", "Music:Dist,Att", "Music.Volume=-12", "Music.Dist.Wet=-6" },
        new[] { "slot_wet_bypass", "music=noise:0.25", "Music.Dist.bypass=1", "Music.Dist.Wet=-6" },
        new[] { "slot_wet_dry", "music=noise:0.25", "Music:Att" },
        new[] { "slot_wet_processed", "music=noise:0.25" },
        new[] { "slot_wet_boostlimit", "music=noise:0.25", "Music.Dist.Wet=20" },
        new[] { "slot_wet_boostclamp", "music=noise:0.25", "Music.Dist.Wet=30" },
        new[] { "slot_wet_threshold", "music=noise:0.25", "Music.Dist.Wet=-69" },
        new[] { "slot_wet_thresholdlower", "music=noise:0.25", "Music.Dist.Wet=-69.9" },
        new[] { "slot_wet_dc_dry", "music=dc0", "Music:Att" },
        new[] { "slot_wet_dc_processed", "music=dc0" },
        new[] { "slot_wet_startup_zero", "music=dc0", "Music.Dist.Wet=-80" },
        new[] { "slot_wet_before_zero", "music=dc0", "Music:Dist,Att", "Music.Volume=-12", "Music.Dist.Wet=-80" },
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
        Option(key, group + ":", group == "Music" ? "Att,Dist" : group == "Master" ? "Att" : null);
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
            string set = Option(key, "set=", "");
            if (set != "") {
                bool ok = mixer.SetFloat("Level", float.Parse(set, System.Globalization.CultureInfo.InvariantCulture));
                bool got = mixer.GetFloat("Level", out float value);
                records.Add("\""+key+".set\":"+(ok ? "true" : "false"));
                records.Add("\""+key+".get\":"+(got ? "true" : "false"));
                records.Add("\""+key+".value\":"+value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                if (Option(key, "clear=", "0") == "1") {
                    bool cleared = mixer.ClearFloat("Level"); mixer.GetFloat("Level", out value);
                    records.Add("\""+key+".clear\":"+(cleared ? "true" : "false"));
                    records.Add("\""+key+".restored\":"+value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if (key.StartsWith("slot_wet_") && key != "slot_wet_dry" && key != "slot_wet_processed" && key != "slot_wet_dc_dry" && key != "slot_wet_dc_processed") {
                string wetOverride = Option(key, "wetset=", "");
                if (wetOverride != "") records.Add("\""+key+".wetset\":"+(mixer.SetFloat("Wet", float.Parse(wetOverride, System.Globalization.CultureInfo.InvariantCulture)) ? "true" : "false"));
                bool got = mixer.GetFloat("Wet", out float value);
                records.Add("\""+key+".wetget\":"+(got ? "true" : "false"));
                records.Add("\""+key+".wetvalue\":"+value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            var go = Option(key, "serialized=", "0") == "1" ? Instantiate(Resources.Load<GameObject>("Serialized")) : new GameObject(key);
            var source = go.GetComponent<AudioSource>(); if (!source) source = go.AddComponent<AudioSource>();
            if (Option(key, "script=", "") == "before") go.AddComponent<HalfSignal>();
            string level = Option(key, "filter=", "");
            if (level != "") {
                var filter = go.GetComponent<AudioDistortionFilter>(); if (!filter) filter = go.AddComponent<AudioDistortionFilter>();
                if (level != "default") filter.distortionLevel = float.Parse(level, System.Globalization.CultureInfo.InvariantCulture);
                filter.enabled = Option(key, "disabled=", "0") != "1";
                records.Add("\""+key+".level\":"+filter.distortionLevel.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (Option(key, "script=", "") == "after") go.AddComponent<HalfSignal>();
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
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
