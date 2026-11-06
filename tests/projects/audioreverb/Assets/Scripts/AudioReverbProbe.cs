using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class AudioReverbProbe : MonoBehaviour
{
    // Each case plays one AudioSource routed to the Child group of the effect-free Probe mixer, unless
    // it has a reverb zone (then it plays to the default output). Tokens:
    //   filter         AudioReverbFilter on the source (added after the AudioSource)
    //   listener       AudioReverbFilter on the AudioListener's GameObject (removed after the case)
    //   F:prop=value   set an AudioReverbFilter property, in order (on the source's or the listener's)
    //   disabled       the filter component is disabled
    //   S:prop=value   set an AudioSource property
    //   mono           mono clip
    //   frames=n       clip length in frames (default ClipFrames)
    //   zone=x         AudioReverbZone at (x, 0, 0); the listener is at the origin (removed after the case)
    //   Z:prop=value   set a property of the last zone
    // Steady-state signals begin after the first DSP block. The clip ends at frame 12288: a source's
    // filter tail is cut after it (Native: one more block), so every wet arrival must come earlier.
    public static readonly string[][] Cases = {
        new[] { "dry" },
        new[] { "default", "filter" },
        new[] { "generic", "filter", "F:reverbPreset=Generic" },
        new[] { "paddedcell", "filter", "F:reverbPreset=PaddedCell" },
        new[] { "bathroom", "filter", "F:reverbPreset=Bathroom" },
        new[] { "cave", "filter", "F:reverbPreset=Cave" },
        new[] { "underwater", "filter", "F:reverbPreset=Underwater" },
        new[] { "psychotic", "filter", "F:reverbPreset=Psychotic" },
        new[] { "off", "filter", "F:reverbPreset=Off" },
        new[] { "user_all", "filter", "F:reverbPreset=User", "F:dryLevel=-300", "F:room=-1000", "F:roomHF=-300",
            "F:roomLF=-200", "F:decayTime=1.49", "F:decayHFRatio=0.59", "F:reflectionsLevel=-1219",
            "F:reflectionsDelay=0.007", "F:reverbLevel=441", "F:reverbDelay=0.011", "F:hfReference=4000",
            "F:lfReference=150", "F:diffusion=80", "F:density=70" },
        new[] { "user_extremes", "filter", "F:reverbPreset=User", "F:dryLevel=-10000", "F:room=0",
            "F:reflectionsLevel=1000", "F:reflectionsDelay=0.1", "F:reverbLevel=2000", "F:reverbDelay=0.1",
            "F:decayTime=20", "F:decayHFRatio=2", "F:diffusion=0", "F:density=0" },
        new[] { "set_on_preset", "filter", "F:reverbPreset=Cave", "F:room=0", "F:reverbLevel=-600" },
        new[] { "preset_then_user", "filter", "F:reverbPreset=Cave", "F:reverbPreset=User" },
        new[] { "mono", "filter", "F:reverbPreset=Generic", "mono" },
        new[] { "disabled", "filter", "F:reverbPreset=Generic", "disabled" },
        new[] { "bypass", "filter", "F:reverbPreset=Generic", "S:bypassEffects=true" },
        new[] { "volume", "filter", "F:reverbPreset=Generic", "S:volume=0.5" },
        new[] { "clip_end_mid", "filter", "F:reverbPreset=Generic", "frames=12800" },
        new[] { "listener", "listener", "F:reverbPreset=Generic" },
        new[] { "listener_bypass", "listener", "F:reverbPreset=Generic", "S:bypassListenerEffects=true" },
        new[] { "zone_in", "zone=0" },
        new[] { "zone_11", "zone=11" },
        new[] { "zone_12_5", "zone=12.5" },
        new[] { "zone_14", "zone=14" },
        new[] { "zone_out", "zone=20" },
        new[] { "zone_mix_half", "zone=0", "S:reverbZoneMix=0.5" },
        new[] { "zone_mix0", "zone=0", "S:reverbZoneMix=0" },
        new[] { "zone_user", "zone=0", "Z:reverbPreset=User", "Z:room=0", "Z:reverb=0", "Z:reflections=-10000" },
        new[] { "zone_cave", "zone=0", "Z:reverbPreset=Cave" },
        new[] { "zone_overlap", "zone=0", "zone=0", "Z:reverbPreset=Cave" },
        new[] { "zone_overlap_blend", "zone=0", "zone=12.5", "Z:reverbPreset=Cave" },
    };
    public static readonly string[] FilterProperties = {
        "reverbPreset", "dryLevel", "room", "roomHF", "roomLF", "decayTime", "decayHFRatio", "reflectionsLevel",
        "reflectionsDelay", "reverbLevel", "reverbDelay", "hfReference", "lfReference", "diffusion", "density"
    };
    public static readonly string[] ZoneProperties = {
        "reverbPreset", "minDistance", "maxDistance", "room", "roomHF", "roomLF", "decayTime", "decayHFRatio",
        "reflections", "reflectionsDelay", "reverb", "reverbDelay", "HFReference", "LFReference", "diffusion", "density"
    };
    public static string[] Selected()
    {
        var only = Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_CASES");
        var keys = new List<string>();
        foreach (var c in Cases) if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), c[0]) >= 0) keys.Add(c[0]);
        return keys.ToArray();
    }
    public const int Blocks = 24;
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine) return;
        foreach (var existing in UnityEngine.Object.FindObjectsOfType<AudioReverbProbe>()) DestroyImmediate(existing.gameObject);
        var probe = new GameObject("AudioReverbProbe").AddComponent<AudioReverbProbe>();
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
    static float[] Signal(int channels, int frames)
    {
        var data = new float[frames * channels];
        for (int f = 0; f < frames; f++) for (int c = 0; c < channels; c++) {
            // Separate left and right impulses, then a simultaneous pair that tests the mono sum.
            float value = c == 0 ? (f == 2048 ? 0.25f : f == 5120 ? 0.2f : 0) : (f == 3072 ? -0.125f : f == 5120 ? -0.1f : 0);
            if (channels == 1) value = f == 2048 ? 0.25f : f == 5120 ? -0.125f : 0;
            data[f * channels + c] = value;
        }
        return data;
    }

    // Sets "prop=value" on the target by reflection: enums by name, numbers invariant.
    static void Set(object target, string assignment)
    {
        var pair = assignment.Split('=');
        var property = target.GetType().GetProperty(pair[0], BindingFlags.Public | BindingFlags.Instance);
        if (property == null) throw new Exception("No property " + pair[0] + " on " + target.GetType().Name);
        var type = property.PropertyType;
        object value = type.IsEnum ? Enum.Parse(type, pair[1])
            : type == typeof(bool) ? bool.Parse(pair[1])
            : type == typeof(int) ? (object)(int)double.Parse(pair[1], CultureInfo.InvariantCulture)
            : float.Parse(pair[1], CultureInfo.InvariantCulture);
        property.SetValue(target, value);
    }
    // Every value exactly: enums and ints as integers, floats as their IEEE bits.
    static string Read(object target, string name)
    {
        object value = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).GetValue(target);
        if (value is float f) return BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString(CultureInfo.InvariantCulture);
        return Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture);
    }
    static string Table(object target, string[] properties)
    {
        if (target == null || (target is UnityEngine.Object o && !o)) return "missing";
        var values = new string[properties.Length];
        for (int i = 0; i < properties.Length; i++) values[i] = Read(target, properties[i]);
        return string.Join(" ", values);
    }

    // Defaults, every preset in enum order (User last, after Psychotic), a property set while a preset
    // is selected, and what each property returns after an out-of-range set on a fresh component.
    void Properties(List<string> records)
    {
        void Add(string key, string value) { records.Add("\"" + key + "\":\"" + value + "\""); }
        var host = new GameObject("properties"); host.AddComponent<AudioSource>();
        var filter = host.AddComponent<AudioReverbFilter>();
        var zone = new GameObject("zone").AddComponent<AudioReverbZone>();
        Add("filter.default", Table(filter, FilterProperties));
        Add("zone.default", Table(zone, ZoneProperties));
        foreach (AudioReverbPreset preset in Enum.GetValues(typeof(AudioReverbPreset))) {
            filter.reverbPreset = preset; Add("filter.preset." + preset, Table(filter, FilterProperties));
            zone.reverbPreset = preset; Add("zone.preset." + preset, Table(zone, ZoneProperties));
        }
        filter.reverbPreset = AudioReverbPreset.Cave; filter.room = -500;
        Add("filter.set_on_preset", Table(filter, FilterProperties));
        filter.reverbPreset = AudioReverbPreset.User;
        Add("filter.set_on_preset_then_user", Table(filter, FilterProperties));
        zone.reverbPreset = AudioReverbPreset.Cave; zone.room = -500;
        Add("zone.set_on_preset", Table(zone, ZoneProperties));
        // Custom values in every field, then a preset: which fields a preset writes.
        foreach (string assignment in Array.Find(Cases, c => c[0] == "user_all"))
            if (assignment.StartsWith("F:")) Set(filter, assignment.Substring(2));
        filter.reverbPreset = AudioReverbPreset.Generic;
        Add("filter.custom_then_preset", Table(filter, FilterProperties));
        zone.reverbPreset = AudioReverbPreset.User; zone.roomLF = -200; zone.reflectionsDelay = 0.05f; zone.HFReference = 4000;
        zone.LFReference = 150; zone.minDistance = 2; zone.maxDistance = 4;
        zone.reverbPreset = AudioReverbPreset.Generic;
        Add("zone.custom_then_preset", Table(zone, ZoneProperties));
        DestroyImmediate(host); DestroyImmediate(zone.gameObject);
        var serialized = (GameObject)Instantiate(Resources.Load<GameObject>("Serialized"));
        var user = serialized.transform.Find("User").gameObject;
        Add("serialized.filter_cave", Table(serialized.GetComponent<AudioReverbFilter>(), FilterProperties));
        Add("serialized.filter_user", Table(user.GetComponent<AudioReverbFilter>(), FilterProperties));
        Add("serialized.zone_hangar", Table(serialized.GetComponent<AudioReverbZone>(), ZoneProperties));
        Add("serialized.zone_user", Table(user.GetComponent<AudioReverbZone>(), ZoneProperties));
        Add("serialized.reverb_zone_mix", Read(user.GetComponent<AudioSource>(), "reverbZoneMix"));
        DestroyImmediate(serialized);
        foreach (var (kind, properties) in new[] { ("filter", FilterProperties), ("zone", ZoneProperties) })
            for (int i = 1; i < properties.Length; i++)
                foreach (string extreme in new[] { "1e9", "-1e9" }) {
                    var go = new GameObject("clamp"); go.AddComponent<AudioSource>();
                    object target = kind == "filter" ? (object)go.AddComponent<AudioReverbFilter>() : go.AddComponent<AudioReverbZone>();
                    Set(target, properties[i] + "=" + extreme);
                    Add(kind + ".clamp." + properties[i] + "." + extreme, Read(target, properties[i]) + " " + Read(target, "reverbPreset"));
                    DestroyImmediate(go);
                }
    }

    System.Collections.IEnumerator Capture()
    {
        if (m_Captured) yield break;
        m_Captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("AudioSettings.Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_OUT"); Directory.CreateDirectory(output);
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Properties(records);
        Save();
        var mixer = Resources.Load<AudioMixer>("Probe"); if (!mixer) throw new Exception("Missing mixer Probe");
        var child = mixer.FindMatchingGroups("Child")[0];
        var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
        foreach (string key in Selected()) {
            Debug.Log("Audio reverb case " + key);
            var tokens = Array.Find(Cases, c => c[0] == key);
            int channels = Array.IndexOf(tokens, "mono") >= 0 ? 1 : 2;
            int frames = ClipFrames;
            foreach (string token in tokens) if (token.StartsWith("frames=")) frames = int.Parse(token.Substring(7), CultureInfo.InvariantCulture);
            var go = new GameObject(key); var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0; source.volume = 1;
            var clip = AudioClip.Create(key, frames, channels, rate, false);
            if (!clip.SetData(Signal(channels, frames), 0)) throw new Exception("SetData failed"); source.clip = clip;
            AudioReverbFilter filter = null;
            var zones = new List<AudioReverbZone>();
            bool hasZone = false;
            foreach (string token in tokens) if (token.StartsWith("zone=")) hasZone = true;
            if (!hasZone) source.outputAudioMixerGroup = child;
            for (int t = 1; t < tokens.Length; t++) {
                string token = tokens[t];
                if (token == "filter") filter = go.AddComponent<AudioReverbFilter>();
                else if (token == "listener") filter = gameObject.AddComponent<AudioReverbFilter>();
                else if (token == "disabled") filter.enabled = false;
                else if (token == "mono" || token.StartsWith("frames=")) { }
                else if (token.StartsWith("F:")) Set(filter, token.Substring(2));
                else if (token.StartsWith("S:")) Set(source, token.Substring(2));
                else if (token.StartsWith("Z:")) Set(zones[zones.Count - 1], token.Substring(2));
                else if (token.StartsWith("zone=")) {
                    var z = new GameObject(key + ".zone" + zones.Count).AddComponent<AudioReverbZone>();
                    z.transform.position = new Vector3(float.Parse(token.Substring(5), CultureInfo.InvariantCulture), 0, 0);
                    zones.Add(z);
                }
                else throw new Exception("Unknown token " + token);
            }
            if (filter) records.Add("\"" + key + ".filter\":\"" + Table(filter, FilterProperties) + "\"");
            for (int z = 0; z < zones.Count; z++) records.Add("\"" + key + ".zone" + z + "\":\"" + Table(zones[z], ZoneProperties) + "\"");
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            source.Play();
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                if (Engine) FlushEngine();
                for (int block = 0; block < Blocks; block++) {
                    if (!Engine) yield return null;
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("AddMixerGroupSink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("AudioRenderer.Render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                    if (Engine) FlushEngine();
                }
            }
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(Blocks*1024)+",\"channels\":2}");
            Save(); buffer.Dispose(); source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
            if (Array.IndexOf(tokens, "listener") >= 0) DestroyImmediate(filter);
            foreach (var z in zones) DestroyImmediate(z.gameObject);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
