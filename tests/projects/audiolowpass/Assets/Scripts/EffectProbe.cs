using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

/// <summary>
/// AudioLowPassFilter and AudioHighPassFilter on an AudioSource: each case plays a clip through
/// one source whose filter components the case's tokens set up, renders it offline and writes the
/// device PCM and the mixer's Master float sink, with the filters' getters after the setup.
/// Tokens: music= signal; blocks= length in DSP blocks; mono=1; chain= the filter components in
/// order (low, high, dist); low:/high: a property set on that filter; curve= the low-pass filter's
/// customCutoffCurve as t,v,in,out keys separated by ';' (empty: a curve without keys; a null
/// curve crashes the 6000.0.71f1 Editor, so none is set); cutoff= cutoffFrequency set
/// after the curve; distance=, maxdist=, spatial=, rolloff=: the source's place on the x axis (the
/// listener at the origin) and its 3D settings; move=from:to over the case; toggle=off:on blocks
/// (the first component disabled and enabled again); live=block:cutoff (the low-pass cutoff set
/// while playing); listener=1 puts the chain on the AudioListener; serialized= a prefab of
/// Resources to play. Errors logged during each case's setup are counted.
/// </summary>
public class EffectProbe : MonoBehaviour
{
    public static readonly string[][] Cases = {
        new[] { "low_default", "music=sweep", "chain=low" },
        new[] { "low_c300", "music=sweep", "chain=low", "low:cutoffFrequency=300" },
        new[] { "low_c1000_q3", "music=sweep", "chain=low", "low:cutoffFrequency=1000", "low:lowpassResonanceQ=3" },
        new[] { "low_c5000_q10", "music=sweep", "chain=low", "low:cutoffFrequency=5000", "low:lowpassResonanceQ=10" },
        new[] { "low_c22000", "music=sweep", "chain=low", "low:cutoffFrequency=22000" },
        new[] { "low_c10", "music=sweep", "chain=low", "low:cutoffFrequency=10" },
        new[] { "low_c_below", "music=sweep", "chain=low", "low:cutoffFrequency=5" },
        new[] { "low_c_above", "music=sweep", "chain=low", "low:cutoffFrequency=30000" },
        new[] { "low_c_round", "music=sweep", "chain=low", "low:cutoffFrequency=1234.5" },
        new[] { "low_q_below", "music=sweep", "chain=low", "low:cutoffFrequency=1000", "low:lowpassResonanceQ=0.5" },
        new[] { "low_q_above", "music=sweep", "chain=low", "low:cutoffFrequency=1000", "low:lowpassResonanceQ=20" },
        new[] { "low_imp", "music=imp", "chain=low", "low:cutoffFrequency=1000", "low:lowpassResonanceQ=3" },
        new[] { "low_mono", "music=sweep", "mono=1", "chain=low", "low:cutoffFrequency=1000" },
        new[] { "high_default", "music=sweep", "chain=high" },
        new[] { "high_c100", "music=sweep", "chain=high", "high:cutoffFrequency=100" },
        new[] { "high_c1000_q3", "music=sweep", "chain=high", "high:cutoffFrequency=1000", "high:highpassResonanceQ=3" },
        new[] { "high_c10000_q10", "music=sweep", "chain=high", "high:cutoffFrequency=10000", "high:highpassResonanceQ=10" },
        new[] { "high_c_below", "music=sweep", "chain=high", "high:cutoffFrequency=5" },
        new[] { "high_c_above", "music=sweep", "chain=high", "high:cutoffFrequency=30000" },
        new[] { "high_q_below", "music=sweep", "chain=high", "high:cutoffFrequency=1000", "high:highpassResonanceQ=0.5" },
        new[] { "high_q_above", "music=sweep", "chain=high", "high:cutoffFrequency=1000", "high:highpassResonanceQ=20" },
        new[] { "high_imp", "music=imp", "chain=high", "high:cutoffFrequency=1000", "high:highpassResonanceQ=3" },
        // Order with a nonlinear filter, and a band of both kinds.
        new[] { "chain_low_dist", "music=sweep", "chain=low,dist", "low:cutoffFrequency=1000", "low:lowpassResonanceQ=3" },
        new[] { "chain_dist_low", "music=sweep", "chain=dist,low", "low:cutoffFrequency=1000", "low:lowpassResonanceQ=3" },
        new[] { "chain_low_high", "music=sweep", "chain=low,high", "low:cutoffFrequency=5000", "high:cutoffFrequency=500" },
        // While playing: the filter disabled and enabled again; the cutoff set.
        new[] { "low_toggle", "music=sweep", "chain=low", "low:cutoffFrequency=1000", "toggle=8:16" },
        new[] { "low_live", "music=sweep", "chain=low", "live=12:500" },
        // The custom cutoff curve: a cutoff over distance / maxDistance (both normalized). 3D
        // sources play a mono clip (the sweep's channels are opposite, and a 3D source mixes them).
        new[] { "curve_2d", "music=sweep", "chain=low", "curve=0,0.5,-0.5,-0.5;1,0,-0.5,-0.5", "distance=5", "maxdist=10" },
        new[] { "curve_3d_log", "music=sweep", "mono=1", "chain=low", "curve=0,0.5,-0.5,-0.5;1,0,-0.5,-0.5", "distance=5", "maxdist=10", "spatial=1", "rolloff=log" },
        new[] { "curve_3d_linear", "music=sweep", "mono=1", "chain=low", "curve=0,0.5,-0.5,-0.5;1,0,-0.5,-0.5", "distance=5", "maxdist=10", "spatial=1", "rolloff=linear" },
        new[] { "curve_far", "music=sweep", "chain=low", "curve=0,0.5,-0.45,-0.45;1,0.05,-0.45,-0.45", "distance=15", "maxdist=10" },
        new[] { "curve_near", "music=sweep", "chain=low", "curve=0,0.5,-0.45,-0.45;1,0.05,-0.45,-0.45", "distance=0", "maxdist=10" },
        new[] { "curve_long", "music=sweep", "chain=low", "curve=0,0.5,-0.225,-0.225;2,0.05,-0.225,-0.225", "distance=5", "maxdist=10" },
        new[] { "curve_three", "music=sweep", "chain=low", "curve=0,0.6,0,0;0.4,0.1,0,0;1,0.3,0,0", "distance=3", "maxdist=10" },
        new[] { "curve_moving", "music=sweep", "blocks=64", "chain=low", "curve=0,0.5,-0.45,-0.45;1,0.05,-0.45,-0.45", "move=1:9", "maxdist=10" },
        new[] { "curve_cutoff", "music=sweep", "chain=low", "curve=0,0.5,-0.5,-0.5;1,0,-0.5,-0.5", "cutoff=2000", "distance=5", "maxdist=10" },
        new[] { "curve_one", "music=sweep", "chain=low", "curve=0.5,0.1,0,0", "distance=5", "maxdist=10" },
        new[] { "curve_listener", "music=sweep", "listener=1", "chain=low", "curve=0,0.5,-0.45,-0.45;1,0.05,-0.45,-0.45", "distance=5", "maxdist=10" },
        new[] { "curve_empty", "music=sweep", "chain=low", "low:cutoffFrequency=1000", "curve=empty" },
        // Serialized components: a curve, its pre-normalization form (keys in world units, an
        // older serialized version) and a high-pass filter.
        new[] { "serialized_low", "music=sweep", "serialized=SerializedLow", "distance=5" },
        new[] { "serialized_legacy", "music=sweep", "serialized=SerializedLegacy", "distance=5" },
        new[] { "serialized_high", "music=sweep", "serialized=SerializedHigh" },
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
    static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
    static string R(float value) => value.ToString("R", CultureInfo.InvariantCulture);
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
    // Signals start after the first DSP block.
    static float[] Signal(string spec, int rate)
    {
        var data = new float[ClipFrames * 2];
        for (int f = 0; f < ClipFrames; f++) {
            float l = 0, r = 0;
            switch (spec) {
                case "imp": l = f == 2048 ? 0.5f : f == 9000 ? -0.25f : 0; r = f == 3072 ? -0.5f : f == 9000 ? 0.125f : 0; break;
                case "sweep":
                    // A logarithmic sweep from 20 Hz to 20 kHz over the clip.
                    if (f >= 1024) {
                        double t = (double)(f - 1024) / rate, T = (double)(ClipFrames - 1024) / rate, k = Math.Log(20000.0 / 20.0);
                        l = r = (float)(0.25 * Math.Sin(2 * Math.PI * 20 * T / k * (Math.Exp(t / T * k) - 1)));
                        r *= -1;
                    }
                    break;
                default: throw new Exception("Unknown signal " + spec);
            }
            data[2 * f] = l; data[2 * f + 1] = r;
        }
        return data;
    }
    static AnimationCurve Curve(string spec)
    {
        if (spec == "empty") return new AnimationCurve();
        var keys = new List<Keyframe>();
        foreach (var key in spec.Split(';')) {
            var v = key.Split(',');
            keys.Add(new Keyframe(Number(v[0]), Number(v[1]), Number(v[2]), Number(v[3])));
        }
        return new AnimationCurve(keys.ToArray());
    }
    static void Set(Component filter, string token)
    {
        var pair = token.Substring(token.IndexOf(':') + 1).Split('=');
        filter.GetType().GetProperty(pair[0]).SetValue(filter, Number(pair[1]));
    }
    // The filters' getters and the low-pass curve's keys.
    static void Read(string key, Component filter, List<string> records)
    {
        if (filter is AudioLowPassFilter low) {
            records.Add("\"" + key + ".low.cutoffFrequency\":" + R(low.cutoffFrequency));
            records.Add("\"" + key + ".low.lowpassResonanceQ\":" + R(low.lowpassResonanceQ));
            var curve = low.customCutoffCurve;
            records.Add("\"" + key + ".low.curve\":\"" + (curve == null ? "null" : Keys(curve)) + "\"");
        }
        else if (filter is AudioHighPassFilter high) {
            records.Add("\"" + key + ".high.cutoffFrequency\":" + R(high.cutoffFrequency));
            records.Add("\"" + key + ".high.highpassResonanceQ\":" + R(high.highpassResonanceQ));
        }
    }
    static string Keys(AnimationCurve curve)
    {
        var parts = new List<string>();
        foreach (var k in curve.keys) parts.Add(R(k.time) + "," + R(k.value) + "," + R(k.inTangent) + "," + R(k.outTangent));
        return string.Join(";", parts) + "|" + curve.preWrapMode + "," + curve.postWrapMode;
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
        transform.position = Vector3.zero;
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Save();
        var mixer = Resources.Load<AudioMixer>("Probe"); if (!mixer) throw new Exception("Missing mixer Probe");
        var music = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Music");
        var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
        if (!music || !master) throw new Exception("Missing groups");
        foreach (string key in Selected()) {
            BlockCount = int.Parse(Option(key, "blocks=", "32")); ClipFrames = BlockCount * 1024;
            Debug.Log("Effect case " + key);
            // Errors logged while the case is set up (a curve without keys, say).
            int errors = 0;
            Application.LogCallback count = (condition, trace, type) => { if (type == LogType.Error || type == LogType.Exception) errors++; };
            Application.logMessageReceived += count;
            string serialized = Option(key, "serialized=", "");
            var go = serialized != "" ? Instantiate(Resources.Load<GameObject>(serialized)) : new GameObject(key);
            var source = go.GetComponent<AudioSource>(); if (!source) source = go.AddComponent<AudioSource>();
            // The filters live on the source's object, or on the listener's.
            var host = Option(key, "listener=", "0") == "1" ? gameObject : go;
            var filters = new List<Component>(host.GetComponents<AudioLowPassFilter>());
            filters.AddRange(host.GetComponents<AudioHighPassFilter>());
            foreach (var kind in Option(key, "chain=", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
                if (kind == "low") filters.Add(host.AddComponent<AudioLowPassFilter>());
                else if (kind == "high") filters.Add(host.AddComponent<AudioHighPassFilter>());
                else if (kind == "dist") host.AddComponent<AudioDistortionFilter>().distortionLevel = 0.8f;
                else throw new Exception("Unknown filter " + kind);
            }
            if (Option(key, "maxdist=", "") != "") source.maxDistance = Number(Option(key, "maxdist=", ""));
            foreach (var filter in filters)
                foreach (var token in Tokens(key))
                    if ((token.StartsWith("low:") && filter is AudioLowPassFilter) || (token.StartsWith("high:") && filter is AudioHighPassFilter)) Set(filter, token);
            var lowPass = host.GetComponent<AudioLowPassFilter>();
            string curve = Option(key, "curve=", "");
            if (curve != "") {
                string failure = "none";
                try { lowPass.customCutoffCurve = Curve(curve); }
                catch (Exception e) { failure = e.GetType().Name; }
                records.Add("\"" + key + ".curve_set\":\"" + failure + "\"");
            }
            if (Option(key, "cutoff=", "") != "") lowPass.cutoffFrequency = Number(Option(key, "cutoff=", ""));
            foreach (var filter in filters) Read(key, filter, records);
            Application.logMessageReceived -= count;
            records.Add("\"" + key + ".errors\":" + errors);
            source.outputAudioMixerGroup = music;
            source.spatialBlend = Number(Option(key, "spatial=", "0"));
            string rolloff = Option(key, "rolloff=", "");
            if (rolloff != "") source.rolloffMode = rolloff == "linear" ? AudioRolloffMode.Linear : AudioRolloffMode.Logarithmic;
            // Placed, not moving: no Doppler shift from the place it is put at.
            source.velocityUpdateMode = AudioVelocityUpdateMode.Dynamic; source.dopplerLevel = 0;
            source.volume = 1;
            string move = Option(key, "move=", "");
            float from = move != "" ? Number(move.Split(':')[0]) : Number(Option(key, "distance=", "0"));
            float to = move != "" ? Number(move.Split(':')[1]) : from;
            go.transform.position = new Vector3(from, 0f, 0f);
            int channels = Option(key, "mono=", "0") == "1" ? 1 : 2;
            var clip = AudioClip.Create(key, ClipFrames, channels, rate, false);
            var data = Signal(Option(key, "music=", "sweep"), rate);
            if (channels == 1) { var mono = new float[ClipFrames]; for (int f = 0; f < ClipFrames; f++) mono[f] = data[2*f]; data = mono; }
            if (!clip.SetData(data, 0)) throw new Exception("SetData failed");
            source.clip = clip; source.Play();
            string[] toggle = Option(key, "toggle=", "").Split(':');
            string[] live = Option(key, "live=", "").Split(':');
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                for (int block = 0; block < BlockCount; block++) {
                    if (toggle.Length == 2 && block == int.Parse(toggle[0])) ((Behaviour)filters[0]).enabled = false;
                    if (toggle.Length == 2 && block == int.Parse(toggle[1])) ((Behaviour)filters[0]).enabled = true;
                    if (live.Length == 2 && block == int.Parse(live[0])) lowPass.cutoffFrequency = Number(live[1]);
                    if (move != "") go.transform.position = new Vector3(from + (to - from) * block / (BlockCount - 1), 0f, 0f);
                    // The frame before each block: what the block's changes made reaches the
                    // audio before it renders.
                    if (!Engine) yield return null; else FlushEngine();
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("AddMixerGroupSink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("AudioRenderer.Render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                }
            }
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(BlockCount*1024)+",\"channels\":2}");
            Save(); buffer.Dispose();
            source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
            if (host == gameObject)
                foreach (var filter in host.GetComponents<Behaviour>())
                    if (filter is AudioLowPassFilter || filter is AudioHighPassFilter || filter is AudioDistortionFilter) DestroyImmediate(filter);
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
