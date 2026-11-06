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
        new[] { "mix_mono_noise", "music=noise:0.25", "mono=1", "blocks=256", "Music.Cho.Dry mix=0.25", "Music.Cho.Wet mix tap 1=0.75", "Music.Cho.Wet mix tap 2=0.375", "Music.Cho.Wet mix tap 3=0.125", "Music.Cho.Delay=17.5", "Music.Cho.Rate=5", "Music.Cho.Depth=0.5" },
        new[] { "mix_mono_tone", "music=tone:0.25:997", "mono=1", "blocks=256", "Music.Cho.Dry mix=0.25", "Music.Cho.Wet mix tap 1=0.75", "Music.Cho.Wet mix tap 2=0.375", "Music.Cho.Wet mix tap 3=0.125", "Music.Cho.Delay=17.5", "Music.Cho.Rate=5", "Music.Cho.Depth=0.5" },
        new[] { "mix_delay_invalid_depth1_low", "music=imp", "blocks=256", "Music.Cho.Depth=1", "set:Delay=-1" },
        new[] { "mix_delay_invalid_depth1_high", "music=imp", "blocks=256", "Music.Cho.Depth=1", "set:Delay=200" },
        new[] { "source_mono_noise", "music=noise:0.25", "mono=1", "blocks=256", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "source_mono_tone", "music=tone:0.25:997", "mono=1", "blocks=256", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "source_delay_invalid_depth1_low", "music=imp", "blocks=256", "Music:Att", "filter=default", "filter:depth=1", "filter:delay=-1" },
        new[] { "source_delay_invalid_depth1_high", "music=imp", "blocks=256", "Music:Att", "filter=default", "filter:depth=1", "filter:delay=200" },
        new[] { "mix_dc_0p00001", "music=dc:0.00001" },
        new[] { "mix_dc_4", "music=dc:4" },
        new[] { "mix_dc_neg4", "music=dc:-4" },
        new[] { "source_dc_0p00001", "music=dc:0.00001", "Music:Att", "filter=default" },
        new[] { "source_dc_4", "music=dc:4", "Music:Att", "filter=default" },
        new[] { "source_dc_neg4", "music=dc:-4", "Music:Att", "filter=default" },
        new[] { "plain", "music=ramp", "Music:Att" },
        new[] { "mix_startup", "music=dc0" },
        new[] { "source_startup", "music=dc0", "Music:Att", "filter=default" },
        new[] { "mix_ramp", "music=ramp", "blocks=256" },
        new[] { "source_ramp", "music=ramp", "Music:Att", "filter=default" },
        new[] { "mix_imp", "music=imp" },
        new[] { "source_imp", "music=imp", "Music:Att", "filter=default" },
        new[] { "mix_noise", "music=noise:0.25", "blocks=256" },
        new[] { "source_noise", "music=noise:0.25", "Music:Att", "filter=default", "blocks=256" },
        new[] { "mix_tone", "music=tone:0.25:997", "blocks=256" },
        new[] { "source_tone", "music=tone:0.25:997", "Music:Att", "filter=default", "blocks=256" },
        new[] { "explicit_default", "music=ramp", "Music.Cho.Dry mix=0.5", "Music.Cho.Wet mix tap 1=0.5", "Music.Cho.Wet mix tap 2=0.5", "Music.Cho.Wet mix tap 3=0.5", "Music.Cho.Delay=40", "Music.Cho.Rate=0.8", "Music.Cho.Depth=0.03", "Music.Cho.Feedback=0", "blocks=256" },
        new[] { "tap1_depth0", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0", "blocks=256" },
        new[] { "tap1_depth0p03", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0.03", "blocks=256" },
        new[] { "tap1_depth0p1", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0.1", "blocks=256" },
        new[] { "tap1_depth0p5", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0.5", "blocks=256" },
        new[] { "tap1_depth1", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=1", "blocks=256" },
        new[] { "tap1_imp", "music=imp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0" },
        new[] { "tap1_noise", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0" },
        new[] { "tap2_depth0", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0", "blocks=256" },
        new[] { "tap2_depth0p03", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0.03", "blocks=256" },
        new[] { "tap2_depth0p1", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0.1", "blocks=256" },
        new[] { "tap2_depth0p5", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0.5", "blocks=256" },
        new[] { "tap2_depth1", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=1", "blocks=256" },
        new[] { "tap2_imp", "music=imp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0" },
        new[] { "tap2_noise", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Depth=0" },
        new[] { "tap3_depth0", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=1", "Music.Cho.Depth=0" },
        new[] { "tap3_depth0p03", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=1", "Music.Cho.Depth=0.03" },
        new[] { "tap3_depth0p1", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=1", "Music.Cho.Depth=0.1" },
        new[] { "tap3_depth0p5", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=1", "Music.Cho.Depth=0.5" },
        new[] { "tap3_depth1", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=1", "Music.Cho.Depth=1" },
        new[] { "tap3_imp", "music=imp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=1", "Music.Cho.Depth=0" },
        new[] { "tap3_noise", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=1", "Music.Cho.Depth=0" },
        new[] { "delay0", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Delay=0", "Music.Cho.Depth=1", "blocks=256" },
        new[] { "delay0p1", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Delay=0.1", "Music.Cho.Depth=1", "blocks=256" },
        new[] { "delay17p5", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Delay=17.5", "Music.Cho.Depth=1", "blocks=256" },
        new[] { "delay100", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0", "Music.Cho.Delay=100", "Music.Cho.Depth=1", "blocks=256" },
        new[] { "rate0", "music=ramp", "Music.Cho.Rate=0", "Music.Cho.Depth=0.5", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "blocks=256" },
        new[] { "rate5", "music=ramp", "Music.Cho.Rate=5", "Music.Cho.Depth=0.5", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "blocks=256" },
        new[] { "rate20", "music=ramp", "Music.Cho.Rate=20", "Music.Cho.Depth=0.5", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=1", "Music.Cho.Wet mix tap 2=1", "Music.Cho.Wet mix tap 3=0", "blocks=256" },
        new[] { "feedbackneg1", "music=imp", "Music.Cho.Depth=0.5", "Music.Cho.Feedback=-1" },
        new[] { "feedback0", "music=imp", "Music.Cho.Depth=0.5", "Music.Cho.Feedback=0" },
        new[] { "feedback0p25", "music=imp", "Music.Cho.Depth=0.5", "Music.Cho.Feedback=0.25" },
        new[] { "feedback0p5", "music=imp", "Music.Cho.Depth=0.5", "Music.Cho.Feedback=0.5" },
        new[] { "feedback1", "music=imp", "Music.Cho.Depth=0.5", "Music.Cho.Feedback=1" },
        new[] { "dry0", "music=ramp", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0" },
        new[] { "dry0p25", "music=ramp", "Music.Cho.Dry mix=0.25", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0" },
        new[] { "dry1", "music=ramp", "Music.Cho.Dry mix=1", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0" },
        new[] { "wet1", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0.25", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0" },
        new[] { "wet2", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0.25", "Music.Cho.Wet mix tap 3=0" },
        new[] { "wet3", "music=noise:0.25", "Music.Cho.Dry mix=0", "Music.Cho.Wet mix tap 1=0", "Music.Cho.Wet mix tap 2=0", "Music.Cho.Wet mix tap 3=0.25" },
        new[] { "mix_combo_noise", "music=noise:0.25", "Music.Cho.Dry mix=0.25", "Music.Cho.Wet mix tap 1=0.75", "Music.Cho.Wet mix tap 2=0.375", "Music.Cho.Wet mix tap 3=0.125", "Music.Cho.Delay=17.5", "Music.Cho.Rate=5", "Music.Cho.Depth=0.5", "blocks=256" },
        new[] { "mix_combo_tone", "music=tone:0.25:997", "Music.Cho.Dry mix=0.25", "Music.Cho.Wet mix tap 1=0.75", "Music.Cho.Wet mix tap 2=0.375", "Music.Cho.Wet mix tap 3=0.125", "Music.Cho.Delay=17.5", "Music.Cho.Rate=5", "Music.Cho.Depth=0.5", "blocks=256" },
        new[] { "mix_combo_imp", "music=imp", "Music.Cho.Dry mix=0.25", "Music.Cho.Wet mix tap 1=0.75", "Music.Cho.Wet mix tap 2=0.375", "Music.Cho.Wet mix tap 3=0.125", "Music.Cho.Delay=17.5", "Music.Cho.Rate=5", "Music.Cho.Depth=0.5", "blocks=256" },
        new[] { "mix_mono", "music=imp", "mono=1", "Music.Cho.Dry mix=0.25", "Music.Cho.Wet mix tap 1=0.75", "Music.Cho.Wet mix tap 2=0.375", "Music.Cho.Wet mix tap 3=0.125", "Music.Cho.Delay=17.5", "Music.Cho.Rate=5", "Music.Cho.Depth=0.5" },
        new[] { "source_combo_noise", "music=noise:0.25", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5", "blocks=256" },
        new[] { "source_combo_tone", "music=tone:0.25:997", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5", "blocks=256" },
        new[] { "source_combo_imp", "music=imp", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5", "blocks=256" },
        new[] { "source_mono", "music=imp", "mono=1", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "mix_group_bypass", "music=noise:0.25", "Music.bypass=1" },
        new[] { "mix_slot_bypass", "music=noise:0.25", "Music.Cho.bypass=1" },
        new[] { "mix_prefader", "music=noise:0.25", "Music:Cho,Att" },
        new[] { "mix_parent", "music=noise:0.25", "Master:Att,Cho" },
        new[] { "mix_set_delay_low", "music=imp", "set:Delay=-1" },
        new[] { "source_set_delay_low", "music=imp", "Music:Att", "filter=default", "filter:delay=-1" },
        new[] { "mix_set_delay_high", "music=imp", "set:Delay=200" },
        new[] { "source_set_delay_high", "music=imp", "Music:Att", "filter=default", "filter:delay=200" },
        new[] { "mix_set_rate_low", "music=imp", "set:Rate=-1" },
        new[] { "source_set_rate_low", "music=imp", "Music:Att", "filter=default", "filter:rate=-1" },
        new[] { "mix_set_rate_high", "music=imp", "set:Rate=200" },
        new[] { "source_set_rate_high", "music=imp", "Music:Att", "filter=default", "filter:rate=200" },
        new[] { "mix_set_depth_low", "music=imp", "set:Depth=-1" },
        new[] { "source_set_depth_low", "music=imp", "Music:Att", "filter=default", "filter:depth=-1" },
        new[] { "mix_set_depth_high", "music=imp", "set:Depth=200" },
        new[] { "source_set_depth_high", "music=imp", "Music:Att", "filter=default", "filter:depth=200" },
        new[] { "mix_set_drymix_low", "music=imp", "set:Dry mix=-1" },
        new[] { "source_set_drymix_low", "music=imp", "Music:Att", "filter=default", "filter:dryMix=-1" },
        new[] { "mix_set_drymix_high", "music=imp", "set:Dry mix=200" },
        new[] { "source_set_drymix_high", "music=imp", "Music:Att", "filter=default", "filter:dryMix=200" },
        new[] { "mix_set_wetmixtap1_low", "music=imp", "set:Wet mix tap 1=-1" },
        new[] { "source_set_wetmixtap1_low", "music=imp", "Music:Att", "filter=default", "filter:wetMix1=-1" },
        new[] { "mix_set_wetmixtap1_high", "music=imp", "set:Wet mix tap 1=200" },
        new[] { "source_set_wetmixtap1_high", "music=imp", "Music:Att", "filter=default", "filter:wetMix1=200" },
        new[] { "mix_set_wetmixtap2_low", "music=imp", "set:Wet mix tap 2=-1" },
        new[] { "source_set_wetmixtap2_low", "music=imp", "Music:Att", "filter=default", "filter:wetMix2=-1" },
        new[] { "mix_set_wetmixtap2_high", "music=imp", "set:Wet mix tap 2=200" },
        new[] { "source_set_wetmixtap2_high", "music=imp", "Music:Att", "filter=default", "filter:wetMix2=200" },
        new[] { "mix_set_wetmixtap3_low", "music=imp", "set:Wet mix tap 3=-1" },
        new[] { "source_set_wetmixtap3_low", "music=imp", "Music:Att", "filter=default", "filter:wetMix3=-1" },
        new[] { "mix_set_wetmixtap3_high", "music=imp", "set:Wet mix tap 3=200" },
        new[] { "source_set_wetmixtap3_high", "music=imp", "Music:Att", "filter=default", "filter:wetMix3=200" },
        new[] { "source_disabled", "music=noise:0.25", "disabled=1", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "source_bypass", "music=noise:0.25", "bypass=1", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "source_listener_bypass", "music=noise:0.25", "listener_bypass=1", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "source_serialized", "music=noise:0.25", "serialized=1", "Music:Att", "filter=default" },
        new[] { "source_before_script", "music=noise:0.25", "script=after", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "source_after_script", "music=noise:0.25", "script=before", "Music:Att", "filter=default", "filter:dryMix=0.25", "filter:wetMix1=0.75", "filter:wetMix2=0.375", "filter:wetMix3=0.125", "filter:delay=17.5", "filter:rate=5", "filter:depth=0.5" },
        new[] { "source_tap1", "music=imp", "Music:Att", "filter=default", "filter:dryMix=0", "filter:wetMix1=1", "filter:wetMix2=0", "filter:wetMix3=0" },
        new[] { "source_tap2", "music=imp", "Music:Att", "filter=default", "filter:dryMix=0", "filter:wetMix1=0", "filter:wetMix2=1", "filter:wetMix3=0" },
        new[] { "source_tap3", "music=imp", "Music:Att", "filter=default", "filter:dryMix=0", "filter:wetMix1=0", "filter:wetMix2=0", "filter:wetMix3=1" },
        new[] { "source_feedback", "music=imp", "Music:Att", "filter=default", "filter:feedback=1" },
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
        Option(key, group + ":", group == "Music" ? "Att,Cho" : group == "Master" ? "Att" : null);
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
            if (Chain(key, "Music").Contains("Cho"))
                foreach (var name in new[] { "Dry mix", "Wet mix tap 1", "Wet mix tap 2", "Wet mix tap 3", "Delay", "Rate", "Depth" }) {
                    mixer.GetFloat(name, out float v);
                    records.Add("\""+key+".mixer_"+name+"\":"+v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            var go = Option(key, "serialized=", "0") == "1" ? Instantiate(Resources.Load<GameObject>("Serialized")) : new GameObject(key);
            var source = go.GetComponent<AudioSource>(); if (!source) source = go.AddComponent<AudioSource>();
            if (Option(key, "script=", "") == "before") go.AddComponent<HalfSignal>();
            string level = Option(key, "filter=", "");
            if (level != "") {
                var filter = go.GetComponent<AudioChorusFilter>(); if (!filter) filter = go.AddComponent<AudioChorusFilter>();
                foreach (var token in Tokens(key)) if (token.StartsWith("filter:")) {
                    var pair = token.Substring(7).Split('=');
                    typeof(AudioChorusFilter).GetProperty(pair[0]).SetValue(filter, float.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                foreach (var name in new[] { "dryMix", "wetMix1", "wetMix2", "wetMix3", "delay", "rate", "depth", "feedback" }) {
                    float v = (float)typeof(AudioChorusFilter).GetProperty(name).GetValue(filter);
                    records.Add("\""+key+"."+name+"\":"+v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
                filter.enabled = Option(key, "disabled=", "0") != "1";

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
