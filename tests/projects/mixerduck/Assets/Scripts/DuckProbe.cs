using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class DuckProbe : MonoBehaviour
{
    // Each case is a private mixer: Master with the children Music, Voice and (when a chain names
    // it) Fx. Tokens after the key:
    //   <signal source>=<signal>  music (default dc), voice (default none), fx (default none)
    //   <Group>:<chain>           effect units in order; Att is the group's Attenuation unit,
    //                             Send>G targets the first Duck Volume or Receive of group G
    //   <Group>.<Unit>.<Param>=v  overrides: Duck parameters, Send.Level, Volume, bypass
    //   order=G,G,...             group creation order (default Music,Voice,Fx)
    // Defaults: Music "Att,Duck", Voice "Send>Music,Att" with Voice.Volume=-80 and
    // Voice.Send.Level=0, so the voice is a silent pre-fader sidechain and Master carries Music.
    public static readonly string[][] Cases = {
        new[] { "music_only" },
        new[] { "nosend", "voice=step:0.5", "Voice:Att" },
        new[] { "default", "voice=step:0.5" },
        new[] { "level005", "voice=step:0.05" },
        new[] { "level025", "voice=step:0.25" },
        new[] { "level1", "voice=step:1" },
        new[] { "ramp_fast", "voice=ramp", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0" },
        new[] { "ramp_default", "voice=ramp" },
        new[] { "ramp_knee0", "voice=ramp", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0", "Music.Duck.Knee=0" },
        new[] { "ramp_knee50", "voice=ramp", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0", "Music.Duck.Knee=50" },
        new[] { "ramp_ratio10", "voice=ramp", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0", "Music.Duck.Ratio=10" },
        new[] { "ramp_ratio02", "voice=ramp", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0", "Music.Duck.Ratio=0.2" },
        new[] { "ramp_thr30", "voice=ramp", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0", "Music.Duck.Threshold=-30" },
        new[] { "ramp_makeup", "voice=ramp", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0", "Music.Duck.Make-up Gain=6" },
        new[] { "makeup_m20", "voice=step:0.5", "Music.Duck.Make-up Gain=-20" },
        new[] { "attack001", "voice=step:0.5", "Music.Duck.Attack Time=0.01" },
        new[] { "attack1", "voice=step:0.5", "Music.Duck.Attack Time=1" },
        new[] { "release001", "voice=step:0.5", "Music.Duck.Release Time=0.01" },
        new[] { "release1", "voice=step:0.5", "Music.Duck.Release Time=1" },
        new[] { "instant", "voice=step:0.5", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0" },
        new[] { "mix0", "voice=step:0.5", "Music.Duck.Sidechain Mix=0", "Music.Duck.Threshold=-20" },
        new[] { "mix05", "voice=step:0.5", "Music.Duck.Sidechain Mix=0.5", "Music.Duck.Threshold=-20" },
        new[] { "tone", "voice=tone:0.5" },
        new[] { "tone_fast", "voice=tone:0.5", "Music.Duck.Attack Time=0", "Music.Duck.Release Time=0" },
        new[] { "step_left", "voice=stepL:0.5" },
        new[] { "step_right", "voice=stepR:0.5" },
        new[] { "release01", "voice=step:0.5", "Music.Duck.Release Time=0.1" },
        new[] { "send_m6", "voice=step:0.5", "Voice.Send.Level=-6" },
        new[] { "send_default_level", "voice=step:1", "Voice.Send.Level=" },
        new[] { "send_post", "voice=step:0.5", "Voice:Att,Send>Music", "Voice.Volume=-6" },
        new[] { "send_pre_audible", "voice=step:0.5", "Voice.Volume=-6" },
        new[] { "send_bypass", "voice=step:0.5", "Voice.Send.bypass=1" },
        new[] { "duck_bypass", "voice=step:0.5", "Music.Duck.bypass=1" },
        new[] { "group_bypass", "voice=step:0.5", "Music.bypass=1" },
        new[] { "duck_prefader", "voice=step:0.5", "Music:Duck,Att", "Music.Volume=-6" },
        new[] { "duck_parent", "voice=step:0.5", "Music:Att", "Master:Att,Duck", "Voice:Send>Master,Att" },
        new[] { "duck_order_first", "voice=step:0.5", "order=Voice,Music" },
        new[] { "two_sends", "voice=step:0.25", "fx=step2:0.25", "Fx:Send>Music,Att", "Fx.Volume=-80", "Fx.Send.Level=0" },
        new[] { "receive_sibling", "music=none", "voice=imp", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att" },
        new[] { "receive_sibling_first", "music=none", "voice=imp", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att", "order=Fx,Music,Voice" },
        new[] { "receive_level", "music=none", "voice=imp", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att", "Voice.Send.Level=-6", "Fx.Volume=-3" },
        new[] { "receive_parent", "music=none", "voice=imp", "Music:Att", "Voice:Send>Master,Att", "Master:Att,Receive" },
        new[] { "receive_bypass", "music=none", "voice=imp", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att", "Fx.Receive.bypass=1" },
        new[] { "receive_group_bypass", "music=none", "voice=imp", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att", "Fx.bypass=1" },
        new[] { "receive_in_music", "voice=imp", "Music:Att,Receive", "Voice:Send>Music,Att" },
        new[] { "startup", "music=dc0", "voice=step0:0.5" },
        new[] { "startup_duck_bypass", "music=dc0", "voice=step0:0.5", "Music.Duck.bypass=1" },
        new[] { "startup_group_bypass", "music=dc0", "voice=step0:0.5", "Music.bypass=1" },
        // Receive in the first block, and the sending group's own output (audible, Fx silent).
        new[] { "startup_receive", "music=none", "voice=imp0", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att" },
        new[] { "startup_receive_source", "music=none", "voice=imp0", "fx=dc0", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att" },
        new[] { "startup_send_audible", "music=none", "voice=imp0", "Music:Att", "Voice:Send>Fx,Att", "Fx:Receive,Att", "Voice.Volume=0", "Fx.Volume=-80" },
    };
    public const int BlockCount = 32;
    public const int ClipFrames = BlockCount * 1024;
    public static string[] Selected()
    {
        var only = Environment.GetEnvironmentVariable("DUCK_PROBE_CASES");
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
        Option(key, group + ":", group == "Music" ? "Att,Duck" : group == "Voice" ? "Send>Music,Att" : group == "Master" ? "Att" : null);
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("DUCK_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine) return;
        foreach (var existing in UnityEngine.Object.FindObjectsOfType<DuckProbe>()) DestroyImmediate(existing.gameObject);
        var probe = new GameObject("DuckProbe").AddComponent<DuckProbe>();
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
    // Steady signals start after the first DSP block; the sidechain steps run 4096..16384.
    static float[] Signal(string spec, int rate)
    {
        var data = new float[ClipFrames * 2];
        var parts = spec.Split(':');
        float amplitude = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
        for (int f = 0; f < ClipFrames; f++) {
            float l = 0, r = 0;
            switch (parts[0]) {
                case "dc": if (f >= 1024) { l = 0.25f; r = -0.125f; } break;
                case "dc0": l = 0.25f; r = -0.125f; break;
                case "step": if (f >= 4096 && f < 16384) l = r = amplitude; break;
                case "step0": if (f < 12288) l = r = amplitude; break;
                case "step2": if (f >= 8192 && f < 20480) l = r = amplitude; break;
                case "stepL": if (f >= 4096 && f < 16384) l = amplitude; break;
                case "stepR": if (f >= 4096 && f < 16384) r = amplitude; break;
                case "imp0": l = f == 1 || f == 100 || f == 700 || f == 1030 || f == 2050 ? 0.5f : 0; r = f == 33 || f == 1100 ? -0.25f : 0; break;
                case "ramp": if (f >= 1024) l = r = (float)(f - 1024) / (ClipFrames - 1024); break;
                case "tone": if (f >= 4096 && f < 16384) l = r = (float)(amplitude * Math.Sin(2 * Math.PI * 1000 * (f - 4096) / rate)); break;
                case "imp": l = f == 2048 ? 0.5f : f == 5000 ? 0.25f : 0; r = f == 3072 ? -0.25f : f == 5000 ? 0.125f : 0; break;
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
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("DUCK_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("AudioSettings.Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("DUCK_PROBE_OUT"); Directory.CreateDirectory(output);
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Save();
        foreach (string key in Selected()) {
            Debug.Log("Duck case " + key);
            var mixer = Resources.Load<AudioMixer>(key); if (!mixer) throw new Exception("Missing mixer " + key);
            var go = new GameObject(key);
            var clips = new List<AudioClip>();
            foreach (var (name, fallback) in new[] { ("Music", "dc"), ("Voice", "none"), ("Fx", "none") }) {
                string spec = Option(key, name.ToLowerInvariant() + "=", fallback);
                if (spec == "none") continue;
                var source = go.AddComponent<AudioSource>();
                source.outputAudioMixerGroup = Array.Find(mixer.FindMatchingGroups(""), g => g.name == name);
                if (!source.outputAudioMixerGroup) throw new Exception("Missing group " + name);
                source.spatialBlend = 0; source.volume = 1;
                var clip = AudioClip.Create(key + name, ClipFrames, 2, rate, false); clips.Add(clip);
                if (!clip.SetData(Signal(spec, rate), 0)) throw new Exception("SetData failed");
                source.clip = clip; source.Play();
            }
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
            foreach (var source in go.GetComponents<AudioSource>()) source.Stop();
            DestroyImmediate(go); foreach (var clip in clips) DestroyImmediate(clip);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
