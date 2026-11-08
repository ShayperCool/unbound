using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public sealed class LiveCase
{
    public readonly string Key, Kind, Parameter, Mode;
    public readonly float Initial, Changed;
    public LiveCase(string key, string kind, string parameter, float initial, float changed, string mode)
    { Key = key; Kind = kind; Parameter = parameter; Initial = initial; Changed = changed; Mode = mode; }
    public bool Component => Mode == "source" || Mode == "mono" || Mode == "listener" || Mode == "delaydepth" || Mode == "recreate";
}

public class LiveEffectProbe : MonoBehaviour
{
    public const int Blocks = 24;
    public static readonly LiveCase[] Cases = {
        new LiveCase("dist_level_set", "Distortion", "Level", 0.25f, 0.85f, "set"),
        new LiveCase("dist_level_timed", "Distortion", "Level", 0.25f, 0.85f, "timed"),
        new LiveCase("dist_level_source", "Distortion", "Level", 0.25f, 0.85f, "source"),
        new LiveCase("dist_instant", "Distortion", "Level", 0.25f, 0.85f, "instant"),
        new LiveCase("dist_wet_set", "Distortion", "Wet", -12f, 0f, "set"),
        new LiveCase("dist_wet_timed", "Distortion", "Wet", -12f, 0f, "timed"),
        new LiveCase("chorus_dry_mix_set", "Chorus", "Dry mix", 0.25f, 0.8f, "set"),
        new LiveCase("chorus_dry_mix_timed", "Chorus", "Dry mix", 0.25f, 0.8f, "timed"),
        new LiveCase("chorus_dry_mix_source", "Chorus", "Dry mix", 0.25f, 0.8f, "source"),
        new LiveCase("chorus_wet_mix_tap_1_set", "Chorus", "Wet mix tap 1", 0.75f, 0.1f, "set"),
        new LiveCase("chorus_wet_mix_tap_1_timed", "Chorus", "Wet mix tap 1", 0.75f, 0.1f, "timed"),
        new LiveCase("chorus_wet_mix_tap_1_source", "Chorus", "Wet mix tap 1", 0.75f, 0.1f, "source"),
        new LiveCase("chorus_wet_mix_tap_2_set", "Chorus", "Wet mix tap 2", 0.375f, 0.8f, "set"),
        new LiveCase("chorus_wet_mix_tap_2_timed", "Chorus", "Wet mix tap 2", 0.375f, 0.8f, "timed"),
        new LiveCase("chorus_wet_mix_tap_2_source", "Chorus", "Wet mix tap 2", 0.375f, 0.8f, "source"),
        new LiveCase("chorus_delay_set", "Chorus", "Delay", 17.5f, 2f, "set"),
        new LiveCase("chorus_delay_timed", "Chorus", "Delay", 17.5f, 2f, "timed"),
        new LiveCase("chorus_delay_source", "Chorus", "Delay", 17.5f, 2f, "source"),
        new LiveCase("chorus_rate_set", "Chorus", "Rate", 5f, 13f, "set"),
        new LiveCase("chorus_rate_timed", "Chorus", "Rate", 5f, 13f, "timed"),
        new LiveCase("chorus_rate_source", "Chorus", "Rate", 5f, 13f, "source"),
        new LiveCase("chorus_depth_set", "Chorus", "Depth", 0.5f, 0.9f, "set"),
        new LiveCase("chorus_depth_timed", "Chorus", "Depth", 0.5f, 0.9f, "timed"),
        new LiveCase("chorus_depth_source", "Chorus", "Depth", 0.5f, 0.9f, "source"),
        new LiveCase("chorus_instant", "Chorus", "Depth", 0.5f, 0.9f, "instant"),
        new LiveCase("chorus_wet_set", "Chorus", "Wet", -12f, 0f, "set"),
        new LiveCase("chorus_wet_timed", "Chorus", "Wet", -12f, 0f, "timed"),
        new LiveCase("flange_drymix_set", "Flange", "Drymix", 0.45f, 0.8f, "set"),
        new LiveCase("flange_drymix_timed", "Flange", "Drymix", 0.45f, 0.8f, "timed"),
        new LiveCase("flange_wetmix_set", "Flange", "Wetmix", 0.55f, 0.1f, "set"),
        new LiveCase("flange_wetmix_timed", "Flange", "Wetmix", 0.55f, 0.1f, "timed"),
        new LiveCase("flange_depth_set", "Flange", "Depth", 1f, 0.2f, "set"),
        new LiveCase("flange_depth_timed", "Flange", "Depth", 1f, 0.2f, "timed"),
        new LiveCase("flange_rate_set", "Flange", "Rate", 0.1f, 13f, "set"),
        new LiveCase("flange_rate_timed", "Flange", "Rate", 0.1f, 13f, "timed"),
        new LiveCase("flange_instant", "Flange", "Rate", 0.1f, 13f, "instant"),
        new LiveCase("flange_wet_set", "Flange", "Wet", -12f, 0f, "set"),
        new LiveCase("flange_wet_timed", "Flange", "Wet", -12f, 0f, "timed"),
        new LiveCase("normalize_fade_in_time_set", "Normalize", "Fade in time", 5000f, 50f, "set"),
        new LiveCase("normalize_fade_in_time_timed", "Normalize", "Fade in time", 5000f, 50f, "timed"),
        new LiveCase("normalize_lowest_volume_set", "Normalize", "Lowest volume", 0.1f, 0.6f, "set"),
        new LiveCase("normalize_lowest_volume_timed", "Normalize", "Lowest volume", 0.1f, 0.6f, "timed"),
        new LiveCase("normalize_maximum_amp_set", "Normalize", "Maximum amp", 20f, 0.5f, "set"),
        new LiveCase("normalize_maximum_amp_timed", "Normalize", "Maximum amp", 20f, 0.5f, "timed"),
        new LiveCase("normalize_instant", "Normalize", "Maximum amp", 20f, 0.5f, "instant"),
        new LiveCase("normalize_wet_set", "Normalize", "Wet", -12f, 0f, "set"),
        new LiveCase("normalize_wet_timed", "Normalize", "Wet", -12f, 0f, "timed"),
        new LiveCase("pitch_pitch_set", "Pitch Shifter", "Pitch", 0.75f, 1.5f, "set"),
        new LiveCase("pitch_pitch_timed", "Pitch Shifter", "Pitch", 0.75f, 1.5f, "timed"),
        new LiveCase("pitch_instant", "Pitch Shifter", "Pitch", 0.75f, 1.5f, "instant"),
        new LiveCase("pitch_wet_set", "Pitch Shifter", "Wet", -12f, 0f, "set"),
        new LiveCase("pitch_wet_timed", "Pitch Shifter", "Wet", -12f, 0f, "timed"),
        new LiveCase("dist_mono", "Distortion", "Level", 0.25f, 0.85f, "mono"),
        new LiveCase("dist_listener", "Distortion", "Level", 0.25f, 0.85f, "listener"),
        new LiveCase("chorus_mono", "Chorus", "Rate", 5f, 13f, "mono"),
        new LiveCase("chorus_listener", "Chorus", "Rate", 5f, 13f, "listener"),
        new LiveCase("dist_wet_zero", "Distortion", "Wet", -80f, -6f, "set"),
        new LiveCase("dist_wet_boost", "Distortion", "Wet", 6f, 30f, "set"),
        new LiveCase("dist_wet_boundary", "Distortion", "Wet", -70f, -68f, "set"),
        new LiveCase("chorus_wet_zero", "Chorus", "Wet", -80f, -6f, "set"),
        new LiveCase("chorus_wet_boost", "Chorus", "Wet", 6f, 30f, "set"),
        new LiveCase("chorus_wet_boundary", "Chorus", "Wet", -70f, -68f, "set"),
        new LiveCase("flange_wet_zero", "Flange", "Wet", -80f, -6f, "set"),
        new LiveCase("flange_wet_boost", "Flange", "Wet", 6f, 30f, "set"),
        new LiveCase("flange_wet_boundary", "Flange", "Wet", -70f, -68f, "set"),
        new LiveCase("normalize_wet_zero", "Normalize", "Wet", -80f, -6f, "set"),
        new LiveCase("normalize_wet_boost", "Normalize", "Wet", 6f, 30f, "set"),
        new LiveCase("normalize_wet_boundary", "Normalize", "Wet", -70f, -68f, "set"),
        new LiveCase("pitch_wet_zero", "Pitch Shifter", "Wet", -80f, -6f, "set"),
        new LiveCase("pitch_wet_boost", "Pitch Shifter", "Wet", 6f, 30f, "set"),
        new LiveCase("pitch_wet_boundary", "Pitch Shifter", "Wet", -70f, -68f, "set"),
        new LiveCase("dist_wet_delta_small", "Distortion", "Wet", -40f, -39.9f, "set"),
        new LiveCase("dist_wet_delta_near", "Distortion", "Wet", -40f, -39.75f, "set"),
        new LiveCase("dist_wet_delta_over", "Distortion", "Wet", -40f, -39.7f, "set"),
        new LiveCase("dist_wet_delta_halfsmall", "Distortion", "Wet", -6f, -5.999f, "set"),
        new LiveCase("dist_wet_delta_halfover", "Distortion", "Wet", -6f, -5.99f, "set"),
        new LiveCase("dist_wet_delta_loudsmall", "Distortion", "Wet", 6f, 6.001f, "set"),
        new LiveCase("dist_wet_delta_loudover", "Distortion", "Wet", 6f, 6.002f, "set"),
        new LiveCase("dist_parameter_wet", "Distortion", "Level", 0.25f, 0.85f, "setwet"),
        new LiveCase("chorus_parameter_wet", "Chorus", "Rate", 5f, 13f, "setwet"),
        new LiveCase("flange_parameter_wet", "Flange", "Rate", 0.1f, 13f, "setwet"),
        new LiveCase("normalize_parameter_wet", "Normalize", "Fade in time", 5000f, 50f, "setwet"),
        new LiveCase("pitch_parameter_wet", "Pitch Shifter", "Pitch", 0.75f, 1.5f, "setwet"),
        new LiveCase("dist_wet_delta_bound_low", "Distortion", "Wet", -40f, -39.7349052f, "set"),
        new LiveCase("dist_wet_delta_bound_high", "Distortion", "Wet", -40f, -39.7332573f, "set"),
        new LiveCase("normalize_floor_set", "Normalize", "Lowest volume", .1f, 1f, "set"),
        new LiveCase("normalize_floor_timed", "Normalize", "Lowest volume", .1f, 1f, "timed"),
        new LiveCase("chorus_depth_down_set", "Chorus", "Depth", .5f, .1f, "set"),
        new LiveCase("chorus_depth_down_timed", "Chorus", "Depth", .5f, .1f, "timed"),
        new LiveCase("chorus_depth_down_source", "Chorus", "Depth", .5f, .1f, "source"),
        new LiveCase("dist_lifecycle_control", "Distortion", "Level", .25f, .85f, "prime"),
        new LiveCase("chorus_delay_depth", "Chorus", "Delay", 17.5f, 2f, "delaydepth"),
        new LiveCase("chorus_delay_recreate", "Chorus", "Delay", 17.5f, 2f, "recreate"),
        new LiveCase("pitch_zero_hold", "Pitch Shifter", "Pitch", .75f, 1.5f, "dryhold"),
        new LiveCase("pitch_zero_edit", "Pitch Shifter", "Pitch", .75f, 1.5f, "dryedit"),
        new LiveCase("chorus_zero_hold", "Chorus", "Depth", .5f, .3f, "dryhold"),
        new LiveCase("chorus_zero_edit", "Chorus", "Depth", .5f, .3f, "dryedit"),
    };
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("EFFECT_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine || Environment.GetEnvironmentVariable("EFFECT_PROBE_PROFILE") != "live") return;
        foreach (var old in UnityEngine.Object.FindObjectsOfType<LiveEffectProbe>()) DestroyImmediate(old.gameObject);
        var probe = new GameObject("LiveEffectProbe").AddComponent<LiveEffectProbe>();
        try { var capture = probe.Capture(); while (capture.MoveNext()) throw new Exception("Engine capture yielded"); }
        catch (Exception error) { Debug.LogException(error); Exit(1); }
    }
    static void Exit(int code)
    {
        if (Engine) { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    System.Collections.IEnumerator Start()
    {
        var routine = Capture();
        for (;;) {
            object next;
            try { if (!routine.MoveNext()) yield break; next = routine.Current; }
            catch (Exception error) { Debug.LogException(error); Exit(1); yield break; }
            yield return next;
        }
    }
    static void FlushEngine(float delta)
    {
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, (double)delta);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, delta * Time.timeScale);
        Type.GetType("Unbound.Runtime.AudioSystem, UnityEngine.CoreModule", true).GetMethod("Update", Flags).Invoke(null, null);
    }
    static AudioChorusFilter AddChorus(GameObject host, float delay)
    {
        var chorus = host.AddComponent<AudioChorusFilter>();
        chorus.dryMix = .25f; chorus.wetMix1 = .75f; chorus.wetMix2 = .375f;
        chorus.delay = delay; chorus.rate = 5; chorus.depth = .5f;
        return chorus;
    }
    static void SetComponent(Behaviour component, string parameter, float value)
    {
        if (component is AudioDistortionFilter dist) { dist.distortionLevel = value; return; }
        var chorus = (AudioChorusFilter)component;
        switch (parameter) {
            case "Dry mix": chorus.dryMix = value; break;
            case "Wet mix tap 1": chorus.wetMix1 = value; break;
            case "Wet mix tap 2": chorus.wetMix2 = value; break;
            case "Delay": chorus.delay = value; break;
            case "Rate": chorus.rate = value; break;
            case "Depth": chorus.depth = value; break;
            default: throw new Exception("Unknown component parameter " + parameter);
        }
    }
    bool captured;
    System.Collections.IEnumerator Capture()
    {
        if (captured) yield break; captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("EFFECT_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("AudioSettings.Reset failed");
        float delta = 1024f / config.sampleRate;
        Time.captureDeltaTime = delta;
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        string output = Environment.GetEnvironmentVariable("EFFECT_PROBE_OUT"); Directory.CreateDirectory(output);
        var records = new List<string> { "\"release\":\""+Application.unityVersion+"\"", "\"rate\":"+AudioSettings.outputSampleRate };
        var sinks = new List<NativeArray<float>>();
        string only = Environment.GetEnvironmentVariable("EFFECT_PROBE_CASES");
        foreach (var c in Cases) {
            if (!string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), c.Key) < 0) continue;
            Debug.Log("Live effect case " + c.Key);
            var mixer = Resources.Load<AudioMixer>(c.Key); if (!mixer) throw new Exception("Missing mixer " + c.Key);
            var go = new GameObject(c.Key); var source = go.AddComponent<AudioSource>();
            Behaviour component = null;
            if (c.Component) {
                var host = c.Mode == "listener" ? gameObject : go;
                if (c.Kind == "Distortion") component = host.AddComponent<AudioDistortionFilter>();
                else {
                    component = AddChorus(host, 17.5f);
                }
                SetComponent(component, c.Parameter, c.Initial);
            }
            source.outputAudioMixerGroup = mixer.FindMatchingGroups("Master")[0]; source.volume = 1;
            int channels = c.Mode == "mono" ? 1 : 2;
            var clip = AudioClip.Create(c.Key, Blocks * 1024, channels, config.sampleRate, false);
            var signal = new float[Blocks * 1024 * channels]; uint seed = 12345;
            for (int frame = 0; frame < Blocks * 1024; frame++)
                for (int channel = 0; channel < channels; channel++) {
                    seed = seed * 1664525u + 1013904223u;
                    signal[frame * channels + channel] = frame < 1024 ? 0 : ((int)(seed >> 18) - 8192) / 32768f;
                }
            clip.SetData(signal, 0); source.clip = clip;
            if (c.Mode == "prime") {
                // Start the first control early, then align its integer sample position.
                const int leadBuffers = 4;
                source.PlayScheduled(AudioSettings.dspTime + leadBuffers * 1024.0 / config.sampleRate);
                using (var prepare = new NativeArray<float>(2048, Allocator.Persistent))
                    for (int i = 0; i <= leadBuffers; i++) {
                        if (Engine) FlushEngine(Time.captureDeltaTime); else yield return null;
                        if (!AudioRenderer.Render(prepare)) throw new Exception("Preparation render failed");
                    }
                source.timeSamples = 0;
            } else source.Play();
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            using (var buffer = new NativeArray<float>(2048, Allocator.Persistent))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, c.Key+".f32"))))
            using (var master = new BinaryWriter(File.Create(Path.Combine(output, c.Key+".master.f32")))) {
                for (int block = 0; block < Blocks; block++) {
                    if (c.Mode.StartsWith("dry")) {
                        if (block == 4 || block == 12) mixer.SetFloat("WetControl", block == 4 ? -80 : -6);
                        if (c.Mode == "dryedit" && (block == 6 || block == 8)) {
                            if (c.Kind == "Chorus" && block == 6) mixer.SetFloat("DelayControl", 2f);
                            mixer.SetFloat("Value", block == 6 ? c.Changed : c.Initial);
                        }
                    } else if (block == 6 || block == 12 || block == 13 || block == 18) {
                        bool changed = block == 6 || block == 13;
                        float value = changed ? c.Changed : c.Initial;
                        if (c.Mode == "recreate" && block == 12) {
                            DestroyImmediate(component);
                            component = AddChorus(go, c.Changed);
                        } else if (c.Mode == "delaydepth" && block >= 12) {
                            if (block == 12) ((AudioChorusFilter)component).depth = .3f;
                        } else if (component) SetComponent(component, c.Parameter, value);
                        else if ((c.Mode == "set" || c.Mode == "setwet" || c.Mode == "prime")) {
                            if (!mixer.SetFloat("Value", value)) throw new Exception("SetFloat failed");
                        } else mixer.FindSnapshot(changed ? "Other" : "Base").TransitionTo(c.Mode == "timed" ? delta * 4 : 0);
                    }
                    if (Engine) FlushEngine(Time.captureDeltaTime); else yield return null;
                    if (Environment.GetEnvironmentVariable("EFFECT_PROBE_TRACE") == "1" && mixer.GetFloat("Value", out float traced))
                        Debug.Log("PARAMETER " + c.Key + " " + block + " " + traced.ToString("R") + " " + Time.deltaTime.ToString("R") + " " + Time.captureDeltaTime.ToString("R"));
                    if (!(bool)typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags).Invoke(null, new object[] { source.outputAudioMixerGroup, sink, false })) throw new Exception("Sink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); master.Write(sink[i]); }
                }
            }
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\""+c.Key+suffix+"\":{\"file\":\""+c.Key+suffix+".f32\",\"frames\":"+(Blocks * 1024)+",\"channels\":2}");
            File.WriteAllText(Path.Combine(output, "capture.json"), "{"+string.Join(",", records)+"}\n");
            source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
            if (c.Mode == "listener") DestroyImmediate(component);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Exit(0);
    }
}
