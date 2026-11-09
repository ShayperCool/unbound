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
        new[] { "listener_dist", "Music:Att", "music=noise:0.25", "listener=dist" },
        new[] { "listener_chorus", "Music:Att", "music=noise:0.25", "listener=chorus" },
        new[] { "listener_low_dist", "Music:Att", "music=noise:0.25", "listener=low,dist" },
        new[] { "listener_dist_low", "Music:Att", "music=noise:0.25", "listener=dist,low" },
        new[] { "listener_high_dist", "Music:Att", "music=noise:0.25", "listener=high,dist" },
        new[] { "listener_dist_high", "Music:Att", "music=noise:0.25", "listener=dist,high" },
        new[] { "listener_echo_dist", "Music:Att", "music=noise:0.25", "listener=echo,dist" },
        new[] { "listener_dist_echo", "Music:Att", "music=noise:0.25", "listener=dist,echo" },
        new[] { "listener_reverb_dist", "Music:Att", "music=noise:0.25", "listener=reverb,dist" },
        new[] { "listener_dist_reverb", "Music:Att", "music=noise:0.25", "listener=dist,reverb" },
        new[] { "listener_chorus_dist", "Music:Att", "music=noise:0.25", "listener=chorus,dist" },
        new[] { "listener_dist_chorus", "Music:Att", "music=noise:0.25", "listener=dist,chorus" },
        new[] { "listener_after_mixer", "music=noise:0.25", "Music.Dist.Level=0.25", "listener=dist" },
        new[] { "listener_sum", "Music:Att", "music=noise:0.25", "listener=dist", "second=1" },
        new[] { "listener_mixer_none", "Music:Att", "music=noise:0.25", "listener=dist", "route=mixer" },
        new[] { "listener_none_none", "Music:Att", "music=noise:0.25", "listener=dist", "route=none" },
        new[] { "listener_mixer_listener_bypass", "Music:Att", "music=noise:0.25", "listener=dist", "route=mixer", "listener_bypass=1" },
        new[] { "listener_none_listener_bypass", "Music:Att", "music=noise:0.25", "listener=dist", "route=none", "listener_bypass=1" },
        new[] { "listener_mixer_bypass", "Music:Att", "music=noise:0.25", "listener=dist", "route=mixer", "bypass=1" },
        new[] { "listener_none_bypass", "Music:Att", "music=noise:0.25", "listener=dist", "route=none", "bypass=1" },
        new[] { "listener_mixed_bypass", "Music:Att", "music=noise:0.25", "listener=dist", "route=none", "listener_bypass=1", "second=1" },
        new[] { "listener_disabled", "Music:Att", "music=noise:0.25", "listener=disabled" },
        new[] { "listener_echo_17", "Music:Att", "music=noise:0.25", "listener=echo", "echo_delay=17" },
        new[] { "listener_echo_17p1", "Music:Att", "music=noise:0.25", "listener=echo", "echo_delay=17.1" },
        new[] { "listener_echo_17p5", "Music:Att", "music=noise:0.25", "listener=echo", "echo_delay=17.5" },
        new[] { "listener_echo_17p9", "Music:Att", "music=noise:0.25", "listener=echo", "echo_delay=17.9" },
        new[] { "listener_echo_18", "Music:Att", "music=noise:0.25", "listener=echo", "echo_delay=18" },
        new[] { "listener_echo_10p125", "Music:Att", "music=noise:0.25", "listener=echo", "echo_delay=10.125" },
        new[] { "source_echo_17p5", "Music:Att", "music=noise:0.25", "sourcechain=echo", "echo_delay=17.5" },
        new[] { "source_echo_17p9", "Music:Att", "music=noise:0.25", "sourcechain=echo", "echo_delay=17.9" },
        new[] { "source_echo_mono", "Music:Att", "music=noise:0.25", "sourcechain=echo", "echo_delay=17.5", "mono=1" },
        new[] { "source_echo_imp", "Music:Att", "sourcechain=echo", "echo_delay=17.5", "music=imp" },
        new[] { "source_echo_low", "Music:Att", "music=noise:0.25", "sourcechain=echo", "echo_delay=1.25" },
        new[] { "source_echo_negative", "Music:Att", "music=noise:0.25", "sourcechain=echo", "echo_delay=-1.25" },
        new[] { "source_echo_high", "Music:Att", "music=noise:0.25", "sourcechain=echo", "echo_delay=5000.75" },
    };
    public static readonly string[][] SpatialCases = {
        new[] { "spatial_dry_front_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5" },
        new[] { "spatial_dry_side_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4" },
        new[] { "spatial_dry_spread_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "spread=180" },
        new[] { "spatial_dry_blend_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "blend=0.5" },
        new[] { "spatial_dry_pitch_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "pitch=1.5" },
        new[] { "spatial_dry_doppler_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "move=1" },
        new[] { "spatial_dry_zone_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "zone=1" },
        new[] { "spatial_dist_front_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "sourcechain=dist" },
        new[] { "spatial_dist_side_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=dist" },
        new[] { "spatial_dist_spread_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "spread=180", "sourcechain=dist" },
        new[] { "spatial_dist_blend_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "blend=0.5", "sourcechain=dist" },
        new[] { "spatial_dist_pitch_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "pitch=1.5", "sourcechain=dist" },
        new[] { "spatial_dist_doppler_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "move=1", "sourcechain=dist" },
        new[] { "spatial_dist_zone_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "zone=1", "sourcechain=dist" },
        new[] { "spatial_chorus_front_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "sourcechain=chorus" },
        new[] { "spatial_chorus_side_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=chorus" },
        new[] { "spatial_chorus_spread_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "spread=180", "sourcechain=chorus" },
        new[] { "spatial_chorus_blend_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "blend=0.5", "sourcechain=chorus" },
        new[] { "spatial_chorus_pitch_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "pitch=1.5", "sourcechain=chorus" },
        new[] { "spatial_chorus_doppler_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "move=1", "sourcechain=chorus" },
        new[] { "spatial_chorus_zone_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "zone=1", "sourcechain=chorus" },
        new[] { "spatial_dry_front_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5" },
        new[] { "spatial_dry_side_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4" },
        new[] { "spatial_dry_spread_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=180" },
        new[] { "spatial_dry_blend_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "blend=0.5" },
        new[] { "spatial_dry_pitch_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "pitch=1.5" },
        new[] { "spatial_dry_doppler_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "move=1" },
        new[] { "spatial_dry_zone_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "zone=1" },
        new[] { "spatial_dist_front_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "sourcechain=dist" },
        new[] { "spatial_dist_side_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=dist" },
        new[] { "spatial_dist_spread_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=180", "sourcechain=dist" },
        new[] { "spatial_dist_blend_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "blend=0.5", "sourcechain=dist" },
        new[] { "spatial_dist_pitch_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "pitch=1.5", "sourcechain=dist" },
        new[] { "spatial_dist_doppler_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "move=1", "sourcechain=dist" },
        new[] { "spatial_dist_zone_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "zone=1", "sourcechain=dist" },
        new[] { "spatial_chorus_front_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "sourcechain=chorus" },
        new[] { "spatial_chorus_side_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=chorus" },
        new[] { "spatial_chorus_spread_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=180", "sourcechain=chorus" },
        new[] { "spatial_chorus_blend_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "blend=0.5", "sourcechain=chorus" },
        new[] { "spatial_chorus_pitch_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "pitch=1.5", "sourcechain=chorus" },
        new[] { "spatial_chorus_doppler_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "move=1", "sourcechain=chorus" },
        new[] { "spatial_chorus_zone_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "zone=1", "sourcechain=chorus" },
        new[] { "spatial_dry_gain_linear_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0.5", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,1", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,2", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_10", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,10", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_19", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,19", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_20", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_30", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,30", "rolloff=linear" },
        new[] { "spatial_dry_gain_linear_100", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,100", "rolloff=linear" },
        new[] { "spatial_dry_gain_log_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0", "rolloff=log" },
        new[] { "spatial_dry_gain_log_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0.5", "rolloff=log" },
        new[] { "spatial_dry_gain_log_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,1", "rolloff=log" },
        new[] { "spatial_dry_gain_log_2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,2", "rolloff=log" },
        new[] { "spatial_dry_gain_log_5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "rolloff=log" },
        new[] { "spatial_dry_gain_log_10", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,10", "rolloff=log" },
        new[] { "spatial_dry_gain_log_19", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,19", "rolloff=log" },
        new[] { "spatial_dry_gain_log_20", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "rolloff=log" },
        new[] { "spatial_dry_gain_log_30", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,30", "rolloff=log" },
        new[] { "spatial_dry_gain_log_100", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,100", "rolloff=log" },
        new[] { "spatial_dry_gain_custom_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0.5", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,1", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,2", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_10", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,10", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_19", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,19", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_20", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_30", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,30", "rolloff=custom" },
        new[] { "spatial_dry_gain_custom_100", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,100", "rolloff=custom" },
        new[] { "spatial_dry_gain_blend_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "blend=0" },
        new[] { "spatial_dry_gain_blend_0p25", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "blend=0.25" },
        new[] { "spatial_dry_gain_blend_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "blend=0.5" },
        new[] { "spatial_dry_gain_blend_0p75", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "blend=0.75" },
        new[] { "spatial_dry_gain_blend_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "blend=1" },
        new[] { "spatial_dry_pan_1_10", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=1,0,10", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_1_5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=1,0,5", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_1_2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=1,0,2", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_1_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=1,0,1", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_2_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=2,0,1", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_5_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=5,0,1", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_10_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=10,0,1", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_5_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=5,0,0", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_n3_4", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=-3,0,4", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_3_n4", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,-4", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_0_n5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,-5", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_pan_0_5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "rolloff=custom", "curve=flat" },
        new[] { "spatial_dry_spread_30", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=30" },
        new[] { "spatial_dry_spread_60", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=60" },
        new[] { "spatial_dry_spread_90", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=90" },
        new[] { "spatial_dry_spread_120", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=120" },
        new[] { "spatial_dry_spread_180", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=180" },
        new[] { "spatial_dry_spread_270", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=270" },
        new[] { "spatial_dry_spread_360", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "spread=360" },
        new[] { "spatial_dry_stereopan_0_n1_0p5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=-1", "blend=0.5" },
        new[] { "spatial_dry_stereopan_0_n1_1", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=-1", "blend=1" },
        new[] { "spatial_dry_stereopan_0_n0p5_0p5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=-0.5", "blend=0.5" },
        new[] { "spatial_dry_stereopan_0_n0p5_1", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=-0.5", "blend=1" },
        new[] { "spatial_dry_stereopan_0_0p5_0p5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=0.5", "blend=0.5" },
        new[] { "spatial_dry_stereopan_0_0p5_1", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=0.5", "blend=1" },
        new[] { "spatial_dry_stereopan_0_1_0p5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=1", "blend=0.5" },
        new[] { "spatial_dry_stereopan_0_1_1", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "pan=1", "blend=1" },
        new[] { "spatial_dry_height_0_0_5_0_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,5,0", "spread=0" },
        new[] { "spatial_dry_height_0_0_5_0_90", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,5,0", "spread=90" },
        new[] { "spatial_dry_height_0_0_5_0_180", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,5,0", "spread=180" },
        new[] { "spatial_dry_height_0_3_4_0_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,4,0", "spread=0" },
        new[] { "spatial_dry_height_0_3_4_0_90", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,4,0", "spread=90" },
        new[] { "spatial_dry_height_0_3_4_0_180", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,4,0", "spread=180" },
        new[] { "spatial_dry_height_0_3_4_5_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,4,5", "spread=0" },
        new[] { "spatial_dry_height_0_3_4_5_90", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,4,5", "spread=90" },
        new[] { "spatial_dry_height_0_3_4_5_180", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,4,5", "spread=180" },
        new[] { "spatial_dry_height_0_0_0_0_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,0", "spread=0" },
        new[] { "spatial_dry_height_0_0_0_0_90", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,0", "spread=90" },
        new[] { "spatial_dry_height_0_0_0_0_180", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,0", "spread=180" },
        new[] { "spatial_dry_curve_customrolloff_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "multi=CustomRolloff" },
        new[] { "spatial_dry_curve_spatialblend_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "multi=SpatialBlend" },
        new[] { "spatial_dry_curve_spread_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "multi=Spread" },
        new[] { "spatial_dry_curve_reverbzonemix_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "multi=ReverbZoneMix" },
        new[] { "spatial_dry_listenerturn_0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "listenerturn=90" },
        new[] { "spatial_dry_limits_0_2_10", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "min=2", "max=10" },
        new[] { "spatial_dry_limits_0_0_10", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "min=0", "max=10" },
        new[] { "spatial_dry_limits_0_5_5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "min=5", "max=5" },
        new[] { "spatial_dry_limits_0_10_5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=0,0,5", "min=10", "max=5" },
        new[] { "spatial_dry_stereopan_1_n1_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=-1", "blend=0.5" },
        new[] { "spatial_dry_stereopan_1_n1_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=-1", "blend=1" },
        new[] { "spatial_dry_stereopan_1_n0p5_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=-0.5", "blend=0.5" },
        new[] { "spatial_dry_stereopan_1_n0p5_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=-0.5", "blend=1" },
        new[] { "spatial_dry_stereopan_1_0p5_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=0.5", "blend=0.5" },
        new[] { "spatial_dry_stereopan_1_0p5_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=0.5", "blend=1" },
        new[] { "spatial_dry_stereopan_1_1_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=1", "blend=0.5" },
        new[] { "spatial_dry_stereopan_1_1_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "pan=1", "blend=1" },
        new[] { "spatial_dry_height_1_0_5_0_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,5,0", "spread=0" },
        new[] { "spatial_dry_height_1_0_5_0_90", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,5,0", "spread=90" },
        new[] { "spatial_dry_height_1_0_5_0_180", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,5,0", "spread=180" },
        new[] { "spatial_dry_height_1_3_4_0_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,4,0", "spread=0" },
        new[] { "spatial_dry_height_1_3_4_0_90", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,4,0", "spread=90" },
        new[] { "spatial_dry_height_1_3_4_0_180", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,4,0", "spread=180" },
        new[] { "spatial_dry_height_1_3_4_5_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,4,5", "spread=0" },
        new[] { "spatial_dry_height_1_3_4_5_90", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,4,5", "spread=90" },
        new[] { "spatial_dry_height_1_3_4_5_180", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,4,5", "spread=180" },
        new[] { "spatial_dry_height_1_0_0_0_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0", "spread=0" },
        new[] { "spatial_dry_height_1_0_0_0_90", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0", "spread=90" },
        new[] { "spatial_dry_height_1_0_0_0_180", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,0", "spread=180" },
        new[] { "spatial_dry_curve_customrolloff_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "multi=CustomRolloff" },
        new[] { "spatial_dry_curve_spatialblend_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "multi=SpatialBlend" },
        new[] { "spatial_dry_curve_spread_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "multi=Spread" },
        new[] { "spatial_dry_curve_reverbzonemix_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "multi=ReverbZoneMix" },
        new[] { "spatial_dry_listenerturn_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "listenerturn=90" },
        new[] { "spatial_dry_limits_1_2_10", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "min=2", "max=10" },
        new[] { "spatial_dry_limits_1_0_10", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "min=0", "max=10" },
        new[] { "spatial_dry_limits_1_5_5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "min=5", "max=5" },
        new[] { "spatial_dry_limits_1_10_5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,5", "min=10", "max=5" },
        new[] { "spatial_dry_twod_pan_n1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pan=-1" },
        new[] { "spatial_dry_twod_pan_n0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pan=-0.5" },
        new[] { "spatial_dry_twod_pan_0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pan=0" },
        new[] { "spatial_dry_twod_pan_0p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pan=0.5" },
        new[] { "spatial_dry_twod_pan_1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pan=1" },
        new[] { "spatial_dry_endpoint_m0_b0_p1p25", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=1.25" },
        new[] { "spatial_dry_endpoint_m0_b0_p1p5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=1.5" },
        new[] { "spatial_dry_endpoint_m0_b0p5_p1p25", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0.5", "pitch=1.25" },
        new[] { "spatial_dry_endpoint_m0_b0p5_p1p5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0.5", "pitch=1.5" },
        new[] { "spatial_dry_endpoint_m0_b1_p1p25", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=1", "pitch=1.25" },
        new[] { "spatial_dry_endpoint_m0_b1_p1p5", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=1", "pitch=1.5" },
        new[] { "spatial_dry_step_distance_mono0", "Music:Att", "music=dc0", "mono=0", "spatial=1", "doppler=0", "step=distance" },
        new[] { "spatial_dry_step_blend_mono0", "Music:Att", "music=dc0", "mono=0", "spatial=1", "doppler=0", "step=blend" },
        new[] { "spatial_dry_step_direction_mono0", "Music:Att", "music=dc0", "mono=0", "spatial=1", "doppler=0", "step=direction" },
        new[] { "spatial_dry_step_spread_mono0", "Music:Att", "music=dc0", "mono=0", "spatial=1", "doppler=0", "step=spread" },
        new[] { "spatial_dry_step_listener_mono0", "Music:Att", "music=dc0", "mono=0", "spatial=1", "doppler=0", "step=listener" },
        new[] { "spatial_dry_step_volume_mono0", "Music:Att", "music=dc0", "mono=0", "spatial=1", "doppler=0", "step=volume" },
        new[] { "spatial_dry_endpoint_m1_b0_p1p25", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=1.25" },
        new[] { "spatial_dry_endpoint_m1_b0_p1p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=1.5" },
        new[] { "spatial_dry_endpoint_m1_b0p5_p1p25", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0.5", "pitch=1.25" },
        new[] { "spatial_dry_endpoint_m1_b0p5_p1p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0.5", "pitch=1.5" },
        new[] { "spatial_dry_endpoint_m1_b1_p1p25", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=1", "pitch=1.25" },
        new[] { "spatial_dry_endpoint_m1_b1_p1p5", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=1", "pitch=1.5" },
        new[] { "spatial_dry_step_distance_mono1", "Music:Att", "music=dc0", "mono=1", "spatial=1", "doppler=0", "step=distance" },
        new[] { "spatial_dry_step_blend_mono1", "Music:Att", "music=dc0", "mono=1", "spatial=1", "doppler=0", "step=blend" },
        new[] { "spatial_dry_step_direction_mono1", "Music:Att", "music=dc0", "mono=1", "spatial=1", "doppler=0", "step=direction" },
        new[] { "spatial_dry_step_spread_mono1", "Music:Att", "music=dc0", "mono=1", "spatial=1", "doppler=0", "step=spread" },
        new[] { "spatial_dry_step_listener_mono1", "Music:Att", "music=dc0", "mono=1", "spatial=1", "doppler=0", "step=listener" },
        new[] { "spatial_dry_step_volume_mono1", "Music:Att", "music=dc0", "mono=1", "spatial=1", "doppler=0", "step=volume" },
        new[] { "spatial_dry_boundary_m0_p0p5_r44100", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=0.5", "cliprate=44100", "frames=4096" },
        new[] { "spatial_dry_boundary_m0_p0p5_r48000", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=0.5", "cliprate=48000", "frames=4096" },
        new[] { "spatial_dry_boundary_m0_p1_r44100", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=1", "cliprate=44100", "frames=4096" },
        new[] { "spatial_dry_boundary_m0_p1_r48000", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=1", "cliprate=48000", "frames=4096" },
        new[] { "spatial_dry_boundary_m0_p1p5_r44100", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=1.5", "cliprate=44100", "frames=4096" },
        new[] { "spatial_dry_boundary_m0_p1p5_r48000", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=1.5", "cliprate=48000", "frames=4096" },
        new[] { "spatial_dry_boundary_m1_p0p5_r44100", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=0.5", "cliprate=44100", "frames=4096" },
        new[] { "spatial_dry_boundary_m1_p0p5_r48000", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=0.5", "cliprate=48000", "frames=4096" },
        new[] { "spatial_dry_boundary_m1_p1_r44100", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=1", "cliprate=44100", "frames=4096" },
        new[] { "spatial_dry_boundary_m1_p1_r48000", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=1", "cliprate=48000", "frames=4096" },
        new[] { "spatial_dry_boundary_m1_p1p5_r44100", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=1.5", "cliprate=44100", "frames=4096" },
        new[] { "spatial_dry_boundary_m1_p1p5_r48000", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "blend=0", "pitch=1.5", "cliprate=48000", "frames=4096" },
        new[] { "spatial_dry_head_boundary", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=0.5", "frames=4096", "cliprate=48000", "head=1" },
        new[] { "spatial_dry_velocity_source_vn20_l0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=0", "speed=-20", "move=1" },
        new[] { "spatial_dry_velocity_source_vn20_l1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=1", "speed=-20", "move=1" },
        new[] { "spatial_dry_velocity_source_vn20_l2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=2", "speed=-20", "move=1" },
        new[] { "spatial_dry_velocity_source_v20_l0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=0", "speed=20", "move=1" },
        new[] { "spatial_dry_velocity_source_v20_l1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=1", "speed=20", "move=1" },
        new[] { "spatial_dry_velocity_source_v20_l2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=2", "speed=20", "move=1" },
        new[] { "spatial_dry_velocity_source_v40_l0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=0", "speed=40", "move=1" },
        new[] { "spatial_dry_velocity_source_v40_l1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=1", "speed=40", "move=1" },
        new[] { "spatial_dry_velocity_source_v40_l2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=2", "speed=40", "move=1" },
        new[] { "spatial_dry_velocity_listener_vn20_l0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=0", "speed=-20", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_vn20_l1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=1", "speed=-20", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_vn20_l2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=2", "speed=-20", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_v20_l0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=0", "speed=20", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_v20_l1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=1", "speed=20", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_v20_l2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=2", "speed=20", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_v40_l0", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=0", "speed=40", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_v40_l1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=1", "speed=40", "listenermove=1" },
        new[] { "spatial_dry_velocity_listener_v40_l2", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=2", "speed=40", "listenermove=1" },
        new[] { "spatial_dry_dual_velocity", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=40", "doppler=1", "speed=20", "move=1", "listenermove=1" },
        new[] { "spatial_dry_body_source_dynamic", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=10000", "rolloff=custom", "curve=flat", "body=source", "bodykind=dynamic", "warm=1" },
        new[] { "spatial_dry_body_source_kinematic", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=10000", "rolloff=custom", "curve=flat", "body=source", "bodykind=kinematic", "move=1", "warm=1" },
        new[] { "spatial_dry_body_source_auto", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=10000", "rolloff=custom", "curve=flat", "body=source", "bodykind=auto", "warm=1" },
        new[] { "spatial_dry_body_listener_kinematic", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=10000", "rolloff=custom", "curve=flat", "body=listener", "bodykind=kinematic", "listenermove=1", "warm=1" },
        new[] { "spatial_dry_supersonic_away", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,20", "max=10000", "head=1", "move=1", "speed=-400", "warm=1" },
        new[] { "spatial_dry_supersonic_towards", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=0,0,2000", "max=10000", "head=1", "move=1", "speed=10000", "warm=1" },
        new[] { "spatial_dry_boundary_unrouted_pitch", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "blend=0", "pitch=1.5", "frames=4096", "cliprate=44100", "route=none", "warm=1" },
        new[] { "spatial_dry_wrap_customrolloff", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=40,0,0", "max=20", "rolloff=custom", "curve=flat", "pan=0.3", "wrap=CustomRolloff", "warm=1" },
        new[] { "spatial_dry_wrap_spatialblend", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=40,0,0", "max=20", "rolloff=custom", "curve=flat", "pan=0.3", "wrap=SpatialBlend", "warm=1" },
        new[] { "spatial_dry_wrap_spread", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=40,0,0", "max=20", "rolloff=custom", "curve=flat", "pan=0.3", "wrap=Spread", "warm=1" },
        new[] { "spatial_dry_wrap_reverbzonemix", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=40,0,0", "max=20", "rolloff=custom", "curve=flat", "pan=0.3", "wrap=ReverbZoneMix", "warm=1", "zone=1", "frames=65536" },
    };
    public static readonly string[][] ScriptCases = {
        new[] { "script3d_dist_before_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=dist", "chain_script=before" },
        new[] { "script3d_dist_after_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=dist", "chain_script=after" },
        new[] { "script3d_chorus_before_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=chorus", "chain_script=before" },
        new[] { "script3d_chorus_after_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=chorus", "chain_script=after" },
        new[] { "script3d_dist_chorus_before_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=dist,chorus", "chain_script=before" },
        new[] { "script3d_dist_chorus_after_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=dist,chorus", "chain_script=after" },
        new[] { "script3d_chorus_dist_before_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=chorus,dist", "chain_script=before" },
        new[] { "script3d_chorus_dist_after_mono0", "Music:Att", "music=noise:0.25", "mono=0", "spatial=1", "position=3,0,4", "sourcechain=chorus,dist", "chain_script=after" },
        new[] { "script3d_dist_before_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=dist", "chain_script=before" },
        new[] { "script3d_dist_after_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=dist", "chain_script=after" },
        new[] { "script3d_chorus_before_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=chorus", "chain_script=before" },
        new[] { "script3d_chorus_after_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=chorus", "chain_script=after" },
        new[] { "script3d_dist_chorus_before_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=dist,chorus", "chain_script=before" },
        new[] { "script3d_dist_chorus_after_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=dist,chorus", "chain_script=after" },
        new[] { "script3d_chorus_dist_before_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=chorus,dist", "chain_script=before" },
        new[] { "script3d_chorus_dist_after_mono1", "Music:Att", "music=noise:0.25", "mono=1", "spatial=1", "position=3,0,4", "sourcechain=chorus,dist", "chain_script=after" },
        new[] { "script3d_dry", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=before", "sourcechain=" },
        new[] { "script3d_blend", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=before", "sourcechain=dist,chorus", "blend=0.5" },
        new[] { "script3d_quiet", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=before", "sourcechain=chorus,dist", "volume=0.25" },
        new[] { "script3d_bypass", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=before", "sourcechain=dist,chorus", "bypass=1" },
        new[] { "script3d_live_bypass", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=before", "sourcechain=dist,chorus", "step=bypass" },
        new[] { "script2d_bypass", "Music:Att", "music=noise:0.25", "chain_script=before", "sourcechain=dist,chorus", "bypass=1" },
        new[] { "script_generator_bypass", "Music:Att", "chain_script=before", "sourcechain=", "bypass=1", "clipless=1" },
        new[] { "script_generator_control", "Music:Att", "chain_script=before", "sourcechain=", "clipless=1" },
        new[] { "script3d_live_bypass_initial", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=before", "sourcechain=dist,chorus", "bypass=1", "step=bypass" },
        new[] { "script3d_live_bypass_after", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=after", "sourcechain=dist,chorus", "step=bypass" },
        new[] { "script3d_live_bypass_component", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "sourcechain=dist,chorus", "step=bypass" },
        new[] { "script_generator_live_bypass", "Music:Att", "chain_script=before", "sourcechain=", "clipless=1", "step=bypass" },
        new[] { "script3d_live_bypass_restart", "Music:Att", "music=noise:0.25", "spatial=1", "position=3,0,4", "chain_script=before", "sourcechain=dist,chorus", "step=bypass", "restart=12" },
        new[] { "script_generator_dist_control", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1" },
        new[] { "script_generator_dist_bypass", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "bypass=1" },
        new[] { "script_generator_dist_live", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "step=bypass" },
        new[] { "script_generator_chorus_control", "Music:Att", "chain_script=before", "sourcechain=chorus", "clipless=1", "generator_pattern=1" },
        new[] { "script_generator_chorus_bypass", "Music:Att", "chain_script=before", "sourcechain=chorus", "clipless=1", "generator_pattern=1", "bypass=1" },
        new[] { "script_generator_chorus_live", "Music:Att", "chain_script=before", "sourcechain=chorus", "clipless=1", "generator_pattern=1", "step=bypass" },
        new[] { "script_generator_dist_chorus_control", "Music:Att", "chain_script=before", "sourcechain=dist,chorus", "clipless=1" },
        new[] { "script_generator_dist_chorus_bypass", "Music:Att", "chain_script=before", "sourcechain=dist,chorus", "clipless=1", "bypass=1" },
        new[] { "script_generator_dist_chorus_live", "Music:Att", "chain_script=before", "sourcechain=dist,chorus", "clipless=1", "step=bypass" },
        new[] { "script3d_generator_live_bypass", "Music:Att", "chain_script=before", "sourcechain=chorus,dist", "clipless=1", "step=bypass", "spatial=1", "position=3,0,4" },
        new[] { "script_generator_quiet_control", "Music:Att", "chain_script=before", "sourcechain=dist,chorus", "clipless=1", "volume=0.25" },
        new[] { "script_generator_quiet_bypass", "Music:Att", "chain_script=before", "sourcechain=dist,chorus", "clipless=1", "volume=0.25", "bypass=1" },
        new[] { "script3d_generator_quiet_control", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "volume=0.25", "spatial=1", "blend=0.5", "position=3,0,4" },
        new[] { "script3d_generator_quiet_live", "Music:Att", "chain_script=before", "sourcechain=chorus,dist", "clipless=1", "volume=0.25", "spatial=1", "blend=0.5", "position=3,0,4", "step=bypass" },
        new[] { "script_generator_tiny_control", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "volume=0.0001", "generator_amplitude=32" },
        new[] { "script_generator_tiny_bypass", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "volume=0.0001", "generator_amplitude=32", "bypass=1" },
        new[] { "script_generator_gain_below", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "volume=0.000319", "generator_amplitude=32" },
        new[] { "script_generator_gain_at", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "volume=0.00032", "generator_amplitude=32" },
        new[] { "script_generator_gain_above", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "volume=0.000321", "generator_amplitude=32" },
        new[] { "script_generator_gain_small_step", "Music:Att", "chain_script=before", "sourcechain=dist", "clipless=1", "volume=0.2501", "generator_amplitude=32", "step=volume" },
    };
    public const int BlockCount = 32;
    public const int ClipFrames = BlockCount * 1024;
    public static string[] Selected()
    {
        var only = Environment.GetEnvironmentVariable("EFFECT_PROBE_CASES");
        var keys = new List<string>();
        string profile = Environment.GetEnvironmentVariable("EFFECT_PROBE_PROFILE") ?? "static";
        var cases = profile == "spatial-script" ? ScriptCases : profile.StartsWith("spatial") ? SpatialCases : Cases;
        if (profile.StartsWith("spatial-factor")) cases = Array.FindAll(cases, row => row[0].StartsWith("spatial_dry_velocity_"));
        foreach (var c in cases) if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), c[0]) >= 0) keys.Add(c[0]);
        return keys.ToArray();
    }
    public static string[] Tokens(string key) => Array.Find(Cases, c => c[0] == key) ?? Array.Find(SpatialCases, c => c[0] == key) ?? Array.Find(ScriptCases, c => c[0] == key);
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
        if (!Engine || Environment.GetEnvironmentVariable("EFFECT_PROBE_PROFILE") == "live") return;
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
    void FlushEngine(float deltaTime = 0, bool fixedMotion = false)
    {
        var assembly = Type.GetType("Unbound.Runtime.AudioBackend, UnityEngine.CoreModule", true).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, (double)deltaTime);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, deltaTime);
        double duration = deltaTime > 0 ? (double)typeof(Time).GetProperty("CaptureDuration", Flags).GetValue(null) : 0;
        typeof(Time).GetField("s_FrameDeltaTime", Flags)?.SetValue(null, duration);
        if (fixedMotion) assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("FixedUpdate", Flags).Invoke(null, null);
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
    static List<Behaviour> Components(GameObject host, string chain, float echoDelay = 17.5f)
    {
        var components = new List<Behaviour>();
        foreach (string name in chain.Split(',')) {
            Behaviour component;
            switch (name) {
                case "": continue;
                case "dist": component = host.AddComponent<AudioDistortionFilter>(); break;
                case "disabled": component = host.AddComponent<AudioDistortionFilter>(); component.enabled = false; break;
                case "low": var low = host.AddComponent<AudioLowPassFilter>(); low.cutoffFrequency = 1200; low.lowpassResonanceQ = 2; component = low; break;
                case "high": var high = host.AddComponent<AudioHighPassFilter>(); high.cutoffFrequency = 800; high.highpassResonanceQ = 2; component = high; break;
                case "echo": var echo = host.AddComponent<AudioEchoFilter>(); echo.delay = echoDelay; echo.decayRatio = 0.4f; echo.dryMix = 0.7f; echo.wetMix = 0.6f; component = echo; break;
                case "reverb": var reverb = host.AddComponent<AudioReverbFilter>(); reverb.reverbPreset = AudioReverbPreset.Generic; component = reverb; break;
                case "chorus": var chorus = host.AddComponent<AudioChorusFilter>(); chorus.dryMix = 0.25f; chorus.wetMix1 = 0.75f; chorus.wetMix2 = 0.375f; chorus.wetMix3 = 0.125f; chorus.delay = 17.5f; chorus.rate = 5; chorus.depth = 0.5f; component = chorus; break;
                default: throw new Exception("Unknown filter " + name);
            }
            components.Add(component);
        }
        return components;
    }
    static float Number(string text) => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    static void ToggleBypass(AudioSource source, string key)
    {
        try { source.bypassEffects = !source.bypassEffects; }
        catch (NotSupportedException error) {
            if (!Engine) throw;
            Debug.Log("Unsupported live bypass " + key + ": " + error.Message);
        }
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
        if (Environment.GetEnvironmentVariable("EFFECT_PROBE_PROFILE") == "spatial")
            records.Add("\"captureDeltaTime\":" + Time.captureDeltaTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
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
            float echoDelay = float.Parse(Option(key, "echo_delay=", "17.5"), System.Globalization.CultureInfo.InvariantCulture);
            var listenerComponents = Components(gameObject, Option(key, "listener=", ""), echoDelay);
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
            if (Option(key, "route=", "mixer") == "none") source.outputAudioMixerGroup = null;
            if (Option(key, "chain_script=", "") == "before") go.AddComponent<AffineSignal>();
            var sourceComponents = Components(go, Option(key, "sourcechain=", ""), echoDelay);
            if (Option(key, "chain_script=", "") == "after") go.AddComponent<AffineSignal>();
            if (go.GetComponent<AffineSignal>() is AffineSignal signal) {
                signal.Pattern = Option(key, "generator_pattern=", "0") == "1";
                signal.OutputScale = Number(Option(key, "generator_amplitude=", "1"));
            }
            if (Option(key, "echo_delay=", "") != "") {
                var echo = go.GetComponent<AudioEchoFilter>(); if (!echo) echo = gameObject.GetComponent<AudioEchoFilter>();
                records.Add("\""+key+".echoDelay\":"+echo.delay.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            source.spatialBlend = Number(Option(key, "blend=", Option(key, "spatial=", "0"))); source.volume = Number(Option(key, "volume=", "1"));
            source.pitch = Number(Option(key, "pitch=", "1"));
            source.dopplerLevel = Number(Option(key, "doppler=", "1"));
            transform.position = Vector3.zero;
            source.spread = Number(Option(key, "spread=", "0"));
            source.panStereo = Number(Option(key, "pan=", "0"));
            transform.rotation = Quaternion.Euler(0, Number(Option(key, "listenerturn=", "0")), 0);
            if (Option(key, "spatial=", "0") == "1") {
                var xyz = Option(key, "position=", "0,0,5").Split(',');
                go.transform.position = new Vector3(Number(xyz[0]), Number(xyz[1]), Number(xyz[2]));
                source.rolloffMode = Option(key, "rolloff=", "linear") == "log" ? AudioRolloffMode.Logarithmic : AudioRolloffMode.Linear;
                source.minDistance = Number(Option(key, "min=", "1")); source.maxDistance = Number(Option(key, "max=", "20"));
                if (Option(key, "rolloff=", "") == "custom") {
                    source.rolloffMode = AudioRolloffMode.Custom;
                    source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Option(key, "curve=", "") == "flat"
                        ? AnimationCurve.Linear(0, 1, 1, 1) : AnimationCurve.Linear(0, 1, 1, 0));
                }
                source.velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
            }
            string multi = Option(key, "multi=", "");
            if (multi != "") {
                var curve = new AnimationCurve(new Keyframe(0, 0.2f, 0, 0), new Keyframe(0.4f, 0.8f, 0, 0), new Keyframe(1, 0.3f, 0, 0));
                var type = (AudioSourceCurveType)Enum.Parse(typeof(AudioSourceCurveType), multi);
                source.SetCustomCurve(type, curve);
                if (type == AudioSourceCurveType.CustomRolloff) source.rolloffMode = AudioRolloffMode.Custom;
            }
            string wrapCurve = Option(key, "wrap=", "");
            if (wrapCurve != "") {
                var curve = AnimationCurve.Linear(0, 0.8f, 1, 0.2f);
                curve.postWrapMode = WrapMode.Loop;
                source.SetCustomCurve((AudioSourceCurveType)Enum.Parse(typeof(AudioSourceCurveType), wrapCurve), curve);
            }
            Rigidbody body = null;
            string bodyTarget = Option(key, "body=", "");
            string bodyKind = Option(key, "bodykind=", "dynamic");
            if (bodyTarget != "") {
                body = (bodyTarget == "source" ? go : gameObject).AddComponent<Rigidbody>();
                body.useGravity = false; body.linearDamping = 0;
                body.isKinematic = bodyKind == "kinematic";
                if (!body.isKinematic) body.linearVelocity = new Vector3(0, 0, bodyTarget == "source" ? -20 : 20);
                var mode = bodyKind == "auto" ? AudioVelocityUpdateMode.Auto : AudioVelocityUpdateMode.Dynamic;
                if (bodyTarget == "source") source.velocityUpdateMode = mode;
                else GetComponent<AudioListener>().velocityUpdateMode = mode;
            }
            GameObject zone = null;
            if (Option(key, "zone=", "0") == "1") {
                zone = new GameObject("zone");
                var reverb = zone.AddComponent<AudioReverbZone>(); reverb.reverbPreset = AudioReverbPreset.Generic;
                reverb.minDistance = 10; reverb.maxDistance = 15;
            }
            int channels = Option(key, "mono=", "0") == "1" ? 1 : 2;
            int clipFrames = int.Parse(Option(key, "frames=", ClipFrames.ToString()));
            int clipRate = int.Parse(Option(key, "cliprate=", rate.ToString()));
            var clip = AudioClip.Create(key, clipFrames, channels, clipRate, false);
            var data = Signal(Option(key, "music=", "dc:0.5"), rate);
            if (Option(key, "head=", "0") == "1") { data[0] = 0.5f; data[1] = -0.25f; }
            if (channels == 1) { var mono = new float[ClipFrames]; for (int f = 0; f < ClipFrames; f++) mono[f] = data[2*f]; data = mono; }
            if (clipFrames != ClipFrames) Array.Resize(ref data, clipFrames * channels);
            if (!clip.SetData(data, 0)) throw new Exception("SetData failed");
            source.clip = Option(key, "clipless=", "0") == "1" ? null : clip;
            if (Option(key, "listenermove=", "0") == "1" || Option(key, "warm=", "0") == "1") {
                if (Engine) FlushEngine();
                else yield return null;
            }
            source.Play();
            GameObject second = null;
            if (Option(key, "second=", "0") == "1") {
                second = new GameObject("second"); var other = second.AddComponent<AudioSource>();
                other.clip = clip; other.volume = 0.25f; other.Play();
            }
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                if (Engine) FlushEngine();
                for (int block = 0; block < BlockCount; block++) {
                    if (block == 8 && Option(key, "step=", "") != "") {
                        if (Option(key, "step=", "") == "volume" && Option(key, "clipless=", "0") == "1")
                            records.Add("\""+key+".volumeBefore\":"+source.volume.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                        switch (Option(key, "step=", "")) {
                            case "distance": go.transform.position = new Vector3(0, 0, 10); break;
                            case "blend": source.spatialBlend = 0.25f; break;
                            case "direction": go.transform.position = new Vector3(3, 0, 4); break;
                            case "spread": source.spread = 90; break;
                            case "listener": transform.position = new Vector3(3, 0, 1); break;
                            case "volume": source.volume = 0.25f; break;
                            case "bypass": ToggleBypass(source, key); break;
                        }
                        if (Option(key, "step=", "") == "volume" && Option(key, "clipless=", "0") == "1")
                            records.Add("\""+key+".volumeAfter\":"+source.volume.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                        if (Engine) FlushEngine();
                    }
                    if (block == int.Parse(Option(key, "restart=", "-1"))) {
                        source.Stop(); source.Play();
                        if (Engine) FlushEngine();
                    }
                    if (block == 16 && Option(key, "step=", "") == "bypass") {
                        ToggleBypass(source, key);
                        if (Engine) FlushEngine();
                    }
                    if (Option(key, "move=", "0") == "1") {
                        go.transform.position += new Vector3(0, 0, -Number(Option(key, "speed=", "20")) * 1024 / rate);
                    }
                    if (Option(key, "listenermove=", "0") == "1") {
                        transform.position += new Vector3(0, 0, Number(Option(key, "speed=", "20")) * 1024 / rate);
                    }
                    if (Engine && (Option(key, "move=", "0") == "1" || Option(key, "listenermove=", "0") == "1")) FlushEngine(Time.captureDeltaTime);
                    if (Engine && bodyTarget != "" && Option(key, "move=", "0") != "1" && Option(key, "listenermove=", "0") != "1") FlushEngine(Time.captureDeltaTime, bodyKind == "auto");
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
            if (second) DestroyImmediate(second);
            if (zone) DestroyImmediate(zone);
            if (bodyTarget == "listener") { DestroyImmediate(body); GetComponent<AudioListener>().velocityUpdateMode = AudioVelocityUpdateMode.Dynamic; }
            foreach (var component in listenerComponents) DestroyImmediate(component);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
