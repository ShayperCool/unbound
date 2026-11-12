using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public partial class EffectProbe : MonoBehaviour
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
        new[] { "slot_wet_zero", "music=noise:0.25", "Music.Shift.Wet=-80" },
        new[] { "slot_wet_quarter", "music=noise:0.25", "Music.Shift.Wet=-12" },
        new[] { "slot_wet_half", "music=noise:0.25", "Music.Shift.Wet=-6" },
        new[] { "slot_wet_threequarter", "music=noise:0.25", "Music.Shift.Wet=-2.5" },
        new[] { "slot_wet_full", "music=noise:0.25", "Music.Shift.Wet=0" },
        new[] { "slot_wet_above", "music=noise:0.25", "Music.Shift.Wet=6" },
        new[] { "slot_wet_quiet", "music=noise:0.25", "Music.Shift.Wet=-70" },
        new[] { "slot_wet_audible", "music=noise:0.25", "Music.Shift.Wet=-68" },
        new[] { "slot_wet_set", "music=noise:0.25", "wetset=-6", "Music.Shift.Wet=-6" },
        new[] { "slot_wet_set_above", "music=noise:0.25", "wetset=6", "Music.Shift.Wet=-6" },
        new[] { "slot_wet_startup", "music=dc0", "Music.Shift.Wet=-6" },
        new[] { "slot_wet_before", "music=dc0", "Music:Shift,Att", "Music.Volume=-12", "Music.Shift.Wet=-6" },
        new[] { "slot_wet_bypass", "music=noise:0.25", "Music.Shift.bypass=1", "Music.Shift.Wet=-6" },
        new[] { "slot_wet_dry", "music=noise:0.25", "Music:Att" },
        new[] { "slot_wet_processed", "music=noise:0.25" },
        new[] { "slot_wet_boostlimit", "music=noise:0.25", "Music.Shift.Wet=20" },
        new[] { "slot_wet_boostclamp", "music=noise:0.25", "Music.Shift.Wet=30" },
        new[] { "slot_wet_threshold", "music=noise:0.25", "Music.Shift.Wet=-69" },
        new[] { "slot_wet_thresholdlower", "music=noise:0.25", "Music.Shift.Wet=-69.9" },
        new[] { "slot_wet_dc_dry", "music=dc0", "Music:Att" },
        new[] { "slot_wet_dc_processed", "music=dc0" },
        new[] { "slot_wet_startup_zero", "music=dc0", "Music.Shift.Wet=-80" },
        new[] { "slot_wet_before_zero", "music=dc0", "Music:Shift,Att", "Music.Volume=-12", "Music.Shift.Wet=-80" },
        new[] { "fft_256_imp", "music=imp", "Music.Shift.FFT size=256", "Music.Shift.Pitch=1" },
        new[] { "fft_256_noise", "music=noise:0.25", "Music.Shift.FFT size=256", "Music.Shift.Pitch=0.75" },
        new[] { "fft_256_tone", "music=tone:0.25:997", "Music.Shift.FFT size=256", "Music.Shift.Pitch=1.5" },
        new[] { "fft_512_imp", "music=imp", "Music.Shift.FFT size=512", "Music.Shift.Pitch=1" },
        new[] { "fft_512_noise", "music=noise:0.25", "Music.Shift.FFT size=512", "Music.Shift.Pitch=0.75" },
        new[] { "fft_512_tone", "music=tone:0.25:997", "Music.Shift.FFT size=512", "Music.Shift.Pitch=1.5" },
        new[] { "fft_2048_imp", "music=imp", "Music.Shift.FFT size=2048", "Music.Shift.Pitch=1" },
        new[] { "fft_2048_noise", "music=noise:0.25", "Music.Shift.FFT size=2048", "Music.Shift.Pitch=0.75" },
        new[] { "fft_2048_tone", "music=tone:0.25:997", "Music.Shift.FFT size=2048", "Music.Shift.Pitch=1.5" },
        new[] { "fft_4096_imp", "music=imp", "Music.Shift.FFT size=4096", "Music.Shift.Pitch=1" },
        new[] { "fft_4096_noise", "music=noise:0.25", "Music.Shift.FFT size=4096", "Music.Shift.Pitch=0.75" },
        new[] { "fft_4096_tone", "music=tone:0.25:997", "Music.Shift.FFT size=4096", "Music.Shift.Pitch=1.5" },
        new[] { "fft_raw_255", "music=noise:0.25", "set:FFT size=255", "Music.Shift.Pitch=0.75" },
        new[] { "fft_raw_256_5", "music=noise:0.25", "set:FFT size=256.5", "Music.Shift.Pitch=0.75" },
        new[] { "fft_raw_512_5", "music=noise:0.25", "set:FFT size=512.5", "Music.Shift.Pitch=0.75" },
        new[] { "fft_raw_1024_5", "music=noise:0.25", "set:FFT size=1024.5", "Music.Shift.Pitch=0.75" },
        new[] { "fft_raw_2048_5", "music=noise:0.25", "set:FFT size=2048.5", "Music.Shift.Pitch=0.75" },
        new[] { "fft_raw_4097", "music=noise:0.25", "set:FFT size=4097", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_1", "music=noise:0.25", "set:Overlap=1", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_2", "music=noise:0.25", "set:Overlap=2", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_3", "music=noise:0.25", "set:Overlap=3", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_5", "music=noise:0.25", "set:Overlap=5", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_8", "music=noise:0.25", "set:Overlap=8", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_16", "music=noise:0.25", "set:Overlap=16", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_32", "music=noise:0.25", "set:Overlap=32", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_3_5", "music=noise:0.25", "set:Overlap=3.5", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_0", "music=noise:0.25", "set:Overlap=0", "Music.Shift.Pitch=0.75" },
        new[] { "overlap_33", "music=noise:0.25", "set:Overlap=33", "Music.Shift.Pitch=0.75" },
        new[] { "fft_live_up", "music=noise:0.25", "Music.Shift.Pitch=0.75", "live:8:FFT size=2048" },
        new[] { "fft_live_down", "music=noise:0.25", "Music.Shift.Pitch=1.5", "Music.Shift.FFT size=4096", "live:9:FFT size=256" },
        new[] { "overlap_live", "music=noise:0.25", "Music.Shift.Pitch=0.75", "live:8:Overlap=32" },
        new[] { "buffer_256", "music=noise:0.25", "buffer=256", "blocks=128", "Music.Shift.FFT size=2048", "Music.Shift.Pitch=1.5" },
        new[] { "buffer_512", "music=noise:0.25", "buffer=512", "blocks=64", "Music.Shift.FFT size=2048", "Music.Shift.Pitch=1.5" },
        new[] { "buffer_2048", "music=noise:0.25", "buffer=2048", "blocks=16", "Music.Shift.FFT size=2048", "Music.Shift.Pitch=1.5" },
        new[] { "maxch_1", "music=noise:0.25", "set:Max channels=1", "Music.Shift.Pitch=0.75" },
        new[] { "maxch_2", "music=noise:0.25", "set:Max channels=2", "Music.Shift.Pitch=0.75" },
        new[] { "maxch_16", "music=noise:0.25", "set:Max channels=16", "Music.Shift.Pitch=0.75" },
        new[] { "maxch_raw_1_5", "music=noise:0.25", "set:Max channels=1.5", "Music.Shift.Pitch=0.75" },
        new[] { "maxch_raw_2_5", "music=noise:0.25", "set:Max channels=2.5", "Music.Shift.Pitch=0.75" },
        new[] { "maxch_raw_neg", "music=noise:0.25", "set:Max channels=-1", "Music.Shift.Pitch=0.75" },
        new[] { "maxch_raw_17", "music=noise:0.25", "set:Max channels=17", "Music.Shift.Pitch=0.75" },
        new[] { "maxch_live_same", "music=noise:0.25", "Music.Shift.Pitch=0.75", "live:8:Max channels=2" },
        new[] { "maxch_live_grow", "music=noise:0.25", "Music.Shift.Pitch=0.75", "live:8:Max channels=4" },
        new[] { "maxch_live_bypass", "music=noise:0.25", "Music.Shift.Pitch=0.75", "live:8:Max channels=1", "live:16:Max channels=0" },
        new[] { "maxch_live_resume", "music=noise:0.25", "Music.Shift.Pitch=0.75", "live:4:Max channels=1", "live:12:Max channels=0" },
        new[] { "context_mono", "music=noise:0.25", "mode=Mono", "Music.Shift.Pitch=0.75" },
        new[] { "context_mono_plain", "music=imp", "mode=Mono", "Music:Att" },
        new[] { "context_mono_clip", "music=imp", "mode=Mono", "mono=1", "Music:Att" },
        new[] { "context_mono_pan", "music=imp", "mode=Mono", "pan=-0.5", "Music:Att" },
        new[] { "context_mono_clip_pan", "music=imp", "mode=Mono", "mono=1", "pan=-0.5", "Music:Att" },
        new[] { "context_mono_clip_shift", "music=noise:0.25", "mode=Mono", "mono=1", "Music.Shift.Pitch=1.5" },
        new[] { "context_mono_max1", "music=noise:0.25", "mode=Mono", "set:Max channels=1", "Music.Shift.Pitch=0.75" },
        new[] { "context_mono_live", "music=noise:0.25", "mode=Mono", "Music.Shift.Pitch=0.75", "live:8:Max channels=2" },
        new[] { "context_quad_clip", "music=imp", "mode=Quad", "mono=1", "Music:Att" },
        new[] { "context_surround_clip", "music=imp", "mode=Surround", "mono=1", "Music:Att" },
        new[] { "context_mode5point1_clip", "music=imp", "mode=Mode5point1", "mono=1", "Music:Att" },
        new[] { "context_mode5point1_clip_pan", "music=imp", "mode=Mode5point1", "mono=1", "pan=-0.5", "Music:Att" },
        new[] { "context_mode5point1_pan", "music=imp", "mode=Mode5point1", "pan=-0.5", "Music:Att" },
        new[] { "context_mode5point1_max2", "music=noise:0.25", "mode=Mode5point1", "set:Max channels=2", "Music.Shift.Pitch=0.75" },
        new[] { "context_mode5point1_max6", "music=noise:0.25", "mode=Mode5point1", "set:Max channels=6", "Music.Shift.Pitch=0.75" },
        new[] { "context_mode7point1_clip", "music=imp", "mode=Mode7point1", "mono=1", "Music:Att" },
        new[] { "context_mode7point1_max6", "music=noise:0.25", "mode=Mode7point1", "set:Max channels=6", "Music.Shift.Pitch=0.75" },
        new[] { "context_mono_3d_clip", "music=imp", "mode=Mono", "mono=1", "blend=1", "pos=2:0:1", "Music:Att" },
        new[] { "context_mono_3d_stereo", "music=imp", "mode=Mono", "blend=1", "pos=2:0:1", "Music:Att" },
        new[] { "context_mono_3d_spread", "music=imp", "mode=Mono", "blend=1", "pos=2:0:1", "spread=120", "Music:Att" },
        new[] { "context_mono_3d_half", "music=imp", "mode=Mono", "mono=1", "blend=0.5", "pan=-0.5", "pos=-1:0:2", "Music:Att" },
        new[] { "context_mono_3d_half_right", "music=imp", "mode=Mono", "mono=1", "blend=0.5", "pan=0.5", "pos=1:0:2", "Music:Att" },
        new[] { "context_mono_3d_stereo_half", "music=imp", "mode=Mono", "blend=0.5", "pan=-0.5", "pos=2:0:1", "Music:Att" },
        new[] { "context_stereo_3d_clip", "music=imp", "mono=1", "blend=1", "pos=2:0:1", "Music:Att" },
        new[] { "context_stereo_generator", "music=generator", "Music:Att" },
        new[] { "context_mono_generator", "music=generator", "mode=Mono", "Music:Att" },
        new[] { "context_mode5point1_generator", "music=generator", "mode=Mode5point1", "Music:Att" },
        new[] { "context_mono_listener", "music=imp", "mode=Mono", "listener=0.125:0", "Music:Att" },
        new[] { "context_mode5point1_listener", "music=imp", "mode=Mode5point1", "listener=0.125:4", "Music:Att" },
        new[] { "context_quad_3d_a30", "music=imp", "mode=Quad", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_quad_3d_a100", "music=imp", "mode=Quad", "mono=1", "blend=1", "pos=1.97:0:-0.347", "Music:Att" },
        new[] { "context_quad_3d_a160", "music=imp", "mode=Quad", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att" },
        new[] { "context_quad_3d_a300", "music=imp", "mode=Quad", "mono=1", "blend=1", "pos=-1.732:0:1", "Music:Att" },
        new[] { "context_quad_3d_stereo", "music=imp", "mode=Quad", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_surround_3d_a30", "music=imp", "mode=Surround", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_surround_3d_a100", "music=imp", "mode=Surround", "mono=1", "blend=1", "pos=1.97:0:-0.347", "Music:Att" },
        new[] { "context_surround_3d_a160", "music=imp", "mode=Surround", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att" },
        new[] { "context_surround_3d_a300", "music=imp", "mode=Surround", "mono=1", "blend=1", "pos=-1.732:0:1", "Music:Att" },
        new[] { "context_surround_3d_stereo", "music=imp", "mode=Surround", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_mode5point1_3d_a30", "music=imp", "mode=Mode5point1", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_mode5point1_3d_a100", "music=imp", "mode=Mode5point1", "mono=1", "blend=1", "pos=1.97:0:-0.347", "Music:Att" },
        new[] { "context_mode5point1_3d_a160", "music=imp", "mode=Mode5point1", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att" },
        new[] { "context_mode5point1_3d_a300", "music=imp", "mode=Mode5point1", "mono=1", "blend=1", "pos=-1.732:0:1", "Music:Att" },
        new[] { "context_mode5point1_3d_stereo", "music=imp", "mode=Mode5point1", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_mode7point1_3d_a30", "music=imp", "mode=Mode7point1", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_mode7point1_3d_a100", "music=imp", "mode=Mode7point1", "mono=1", "blend=1", "pos=1.97:0:-0.347", "Music:Att" },
        new[] { "context_mode7point1_3d_a160", "music=imp", "mode=Mode7point1", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att" },
        new[] { "context_mode7point1_3d_a300", "music=imp", "mode=Mode7point1", "mono=1", "blend=1", "pos=-1.732:0:1", "Music:Att" },
        new[] { "context_mode7point1_3d_stereo", "music=imp", "mode=Mode7point1", "blend=1", "pos=1:0:1.732", "Music:Att" },
        new[] { "context_mode5point1_3d_spread", "music=imp", "mode=Mode5point1", "mono=1", "blend=1", "spread=90", "pos=1.97:0:-0.347", "Music:Att" },
        new[] { "context_mode7point1_3d_spread", "music=imp", "mode=Mode7point1", "blend=1", "spread=60", "pos=-1.732:0:1", "Music:Att" },
        new[] { "context_mode5point1_3d_half", "music=imp", "mode=Mode5point1", "mono=1", "blend=0.5", "pan=-0.5", "pos=1.97:0:-0.347", "Music:Att" },
        new[] { "context_mode5point1_3d_stereo_half", "music=imp", "mode=Mode5point1", "blend=0.5", "pan=0.5", "pos=0.684:0:-1.879", "Music:Att" },
        new[] { "context_mode5point1_3d_centre", "music=imp", "mode=Mode5point1", "mono=1", "blend=1", "pos=0:0:0", "Music:Att" },
        new[] { "context_mode7point1_3d_above", "music=imp", "mode=Mode7point1", "blend=1", "pos=0:2:0", "Music:Att" },
        new[] { "context_quad_3d_centre_half", "music=imp", "mode=Quad", "mono=1", "blend=0.5", "pan=0.5", "pos=0:0:0", "Music:Att" },
        new[] { "context_mode7point1_3d_elevated", "music=imp", "mode=Mode7point1", "mono=1", "blend=1", "pos=1:1.5:1", "Music:Att" },
        new[] { "context_mode5point1_clip_shift", "music=noise:0.25", "mode=Mode5point1", "mono=1", "Music.Shift.Pitch=0.75" },
        new[] { "context_mode5point1_3d_shift", "music=noise:0.25", "mode=Mode5point1", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music.Shift.Pitch=1.5" },
        new[] { "context_mode5point1_generator_all", "music=generator", "gen=all", "mode=Mode5point1", "Music:Att" },
        new[] { "context_mode7point1_generator_all", "music=generator", "gen=all", "mode=Mode7point1", "Music:Att" },
        new[] { "context_mode5point1_script", "music=imp", "mode=Mode5point1", "mono=1", "script=0.125:4", "Music:Att" },
        new[] { "context_mode7point1_listener", "music=imp", "mode=Mode7point1", "listener=0.125:6", "Music:Att" },
        new[] { "context_quad", "music=noise:0.25", "mode=Quad", "Music.Shift.Pitch=0.75" },
        new[] { "context_surround", "music=noise:0.25", "mode=Surround", "Music.Shift.Pitch=0.75" },
        new[] { "context_mode5point1", "music=noise:0.25", "mode=Mode5point1", "Music.Shift.Pitch=0.75" },
        new[] { "context_mode7point1", "music=noise:0.25", "mode=Mode7point1", "Music.Shift.Pitch=0.75" },
        new[] { "context_fx_quad_chorus_imp", "mode=Quad", "music=generator", "gen=imp", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_quad_flange_imp", "mode=Quad", "music=generator", "gen=imp", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_quad_reverb_imp", "mode=Quad", "music=generator", "gen=imp", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_quad_chorus_burst", "mode=Quad", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_quad_flange_burst", "mode=Quad", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_quad_reverb_burst", "mode=Quad", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_quad_compressor_0", "mode=Quad", "music=generator", "gen=link", "dominant=0", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_quad_duck_0", "mode=Quad", "music=generator", "gen=link", "dominant=0", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_quad_compressor_1", "mode=Quad", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_quad_duck_1", "mode=Quad", "music=generator", "gen=link", "dominant=1", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_quad_compressor_2", "mode=Quad", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_quad_duck_2", "mode=Quad", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_quad_compressor_3", "mode=Quad", "music=generator", "gen=link", "dominant=3", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_quad_duck_3", "mode=Quad", "music=generator", "gen=link", "dominant=3", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_quad_chorus_source", "mode=Quad", "music=generator", "gen=imp", "Music:Att", "component=chorus:source" },
        new[] { "context_fx_quad_chorus_listener", "mode=Quad", "music=generator", "gen=imp", "Music:Att", "component=chorus:listener" },
        new[] { "context_fx_quad_reverb_source", "mode=Quad", "music=generator", "gen=imp", "Music:Att", "component=reverb:source" },
        new[] { "context_fx_quad_reverb_listener", "mode=Quad", "music=generator", "gen=imp", "Music:Att", "component=reverb:listener" },
        new[] { "context_fx_quad_zone_rear", "mode=Quad", "music=imp", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att", "zone=1" },
        new[] { "context_fx_quad_zone_front", "mode=Quad", "music=imp", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1" },
        new[] { "context_fx_mode5point1_chorus_imp", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_mode5point1_flange_imp", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_mode5point1_reverb_imp", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_mode5point1_chorus_burst", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_mode5point1_flange_burst", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_mode5point1_reverb_burst", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_mode5point1_compressor_0", "mode=Mode5point1", "music=generator", "gen=link", "dominant=0", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode5point1_duck_0", "mode=Mode5point1", "music=generator", "gen=link", "dominant=0", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode5point1_compressor_1", "mode=Mode5point1", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode5point1_duck_1", "mode=Mode5point1", "music=generator", "gen=link", "dominant=1", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode5point1_compressor_2", "mode=Mode5point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode5point1_duck_2", "mode=Mode5point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode5point1_compressor_3", "mode=Mode5point1", "music=generator", "gen=link", "dominant=3", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode5point1_duck_3", "mode=Mode5point1", "music=generator", "gen=link", "dominant=3", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode5point1_compressor_4", "mode=Mode5point1", "music=generator", "gen=link", "dominant=4", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode5point1_duck_4", "mode=Mode5point1", "music=generator", "gen=link", "dominant=4", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode5point1_compressor_5", "mode=Mode5point1", "music=generator", "gen=link", "dominant=5", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode5point1_duck_5", "mode=Mode5point1", "music=generator", "gen=link", "dominant=5", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode5point1_chorus_source", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att", "component=chorus:source" },
        new[] { "context_fx_mode5point1_chorus_listener", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att", "component=chorus:listener" },
        new[] { "context_fx_mode5point1_reverb_source", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att", "component=reverb:source" },
        new[] { "context_fx_mode5point1_reverb_listener", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att", "component=reverb:listener" },
        new[] { "context_fx_mode5point1_zone_rear", "mode=Mode5point1", "music=imp", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att", "zone=1" },
        new[] { "context_fx_mode5point1_zone_front", "mode=Mode5point1", "music=imp", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1" },
        new[] { "context_fx_mode7point1_chorus_imp", "mode=Mode7point1", "music=generator", "gen=imp", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_mode7point1_flange_imp", "mode=Mode7point1", "music=generator", "gen=imp", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_mode7point1_reverb_imp", "mode=Mode7point1", "music=generator", "gen=imp", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_mode7point1_chorus_burst", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_mode7point1_flange_burst", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_mode7point1_reverb_burst", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_mode7point1_compressor_0", "mode=Mode7point1", "music=generator", "gen=link", "dominant=0", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_0", "mode=Mode7point1", "music=generator", "gen=link", "dominant=0", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_compressor_1", "mode=Mode7point1", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_1", "mode=Mode7point1", "music=generator", "gen=link", "dominant=1", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_compressor_2", "mode=Mode7point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_2", "mode=Mode7point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_compressor_3", "mode=Mode7point1", "music=generator", "gen=link", "dominant=3", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_3", "mode=Mode7point1", "music=generator", "gen=link", "dominant=3", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_compressor_4", "mode=Mode7point1", "music=generator", "gen=link", "dominant=4", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_4", "mode=Mode7point1", "music=generator", "gen=link", "dominant=4", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_compressor_5", "mode=Mode7point1", "music=generator", "gen=link", "dominant=5", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_5", "mode=Mode7point1", "music=generator", "gen=link", "dominant=5", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_compressor_6", "mode=Mode7point1", "music=generator", "gen=link", "dominant=6", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_6", "mode=Mode7point1", "music=generator", "gen=link", "dominant=6", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_compressor_7", "mode=Mode7point1", "music=generator", "gen=link", "dominant=7", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_mode7point1_duck_7", "mode=Mode7point1", "music=generator", "gen=link", "dominant=7", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode7point1_chorus_source", "mode=Mode7point1", "music=generator", "gen=imp", "Music:Att", "component=chorus:source" },
        new[] { "context_fx_mode7point1_chorus_listener", "mode=Mode7point1", "music=generator", "gen=imp", "Music:Att", "component=chorus:listener" },
        new[] { "context_fx_mode7point1_reverb_source", "mode=Mode7point1", "music=generator", "gen=imp", "Music:Att", "component=reverb:source" },
        new[] { "context_fx_mode7point1_reverb_listener", "mode=Mode7point1", "music=generator", "gen=imp", "Music:Att", "component=reverb:listener" },
        new[] { "context_fx_mode7point1_zone_rear", "mode=Mode7point1", "music=imp", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att", "zone=1" },
        new[] { "context_fx_mode7point1_zone_front", "mode=Mode7point1", "music=imp", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1" },
        new[] { "context_fx_surround_chorus_imp", "mode=Surround", "music=generator", "gen=imp", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_surround_flange_imp", "mode=Surround", "music=generator", "gen=imp", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_surround_reverb_imp", "mode=Surround", "music=generator", "gen=imp", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_surround_chorus_burst", "mode=Surround", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_surround_flange_burst", "mode=Surround", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7" },
        new[] { "context_fx_surround_reverb_burst", "mode=Surround", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_fx_surround_compressor_0", "mode=Surround", "music=generator", "gen=link", "dominant=0", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_surround_duck_0", "mode=Surround", "music=generator", "gen=link", "dominant=0", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_surround_compressor_1", "mode=Surround", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_surround_duck_1", "mode=Surround", "music=generator", "gen=link", "dominant=1", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_surround_compressor_2", "mode=Surround", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_surround_duck_2", "mode=Surround", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_surround_compressor_3", "mode=Surround", "music=generator", "gen=link", "dominant=3", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_surround_duck_3", "mode=Surround", "music=generator", "gen=link", "dominant=3", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_surround_compressor_4", "mode=Surround", "music=generator", "gen=link", "dominant=4", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=35" },
        new[] { "context_fx_surround_duck_4", "mode=Surround", "music=generator", "gen=link", "dominant=4", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_surround_chorus_source", "mode=Surround", "music=generator", "gen=imp", "Music:Att", "component=chorus:source" },
        new[] { "context_fx_surround_chorus_listener", "mode=Surround", "music=generator", "gen=imp", "Music:Att", "component=chorus:listener" },
        new[] { "context_fx_surround_reverb_source", "mode=Surround", "music=generator", "gen=imp", "Music:Att", "component=reverb:source" },
        new[] { "context_fx_surround_reverb_listener", "mode=Surround", "music=generator", "gen=imp", "Music:Att", "component=reverb:listener" },
        new[] { "context_fx_surround_zone_rear", "mode=Surround", "music=imp", "mono=1", "blend=1", "pos=0.684:0:-1.879", "Music:Att", "zone=1" },
        new[] { "context_fx_surround_zone_front", "mode=Surround", "music=imp", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1" },
        new[] { "context_fx_stereo_duck", "music=generator", "gen=link", "dominant=1", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1" },
        new[] { "context_fx_mode5point1_chorus_tap3", "mode=Mode5point1", "music=generator", "gen=imp", "Music:Att,Chorus", "Music.Chorus.Dry mix=0", "Music.Chorus.Wet mix tap 1=0", "Music.Chorus.Wet mix tap 2=0", "Music.Chorus.Wet mix tap 3=1", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7" },
        new[] { "context_fx_stereo_comp_attack10", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=10", "Music.Comp.Release=35" },
        new[] { "context_fx_stereo_comp_attack500", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=500", "Music.Comp.Release=35" },
        new[] { "context_fx_stereo_comp_release10", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=10" },
        new[] { "context_fx_stereo_comp_release1500", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=8", "Music.Comp.Release=1500" },
        new[] { "context_fx_stereo_comp_times0", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=0", "Music.Comp.Release=0" },
        new[] { "context_fx_stereo_comp_times_negative", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=-2", "Music.Comp.Release=-1" },
        new[] { "context_fx_stereo_comp_threshold_low", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Attack=10", "Music.Comp.Release=35", "Music.Comp.Threshold=-80" },
        new[] { "context_fx_stereo_comp_threshold_high", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Attack=10", "Music.Comp.Release=35", "Music.Comp.Threshold=6" },
        new[] { "context_fx_stereo_comp_makeup_low", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=10", "Music.Comp.Release=35", "Music.Comp.Make up gain=-6" },
        new[] { "context_fx_stereo_comp_makeup_high", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=10", "Music.Comp.Release=35", "Music.Comp.Make up gain=40" },
        // Device controls: one Editor session per output device channel count, metadata only.
        new[] { "device_1", "device=1" }, new[] { "device_2", "device=2" }, new[] { "device_3", "device=3" },
        new[] { "device_4", "device=4" }, new[] { "device_5", "device=5" }, new[] { "device_6", "device=6" },
        new[] { "device_7", "device=7" }, new[] { "device_8", "device=8" },
        new[] { "context_live_quad_chorus", "mode=Quad", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "live:5:Chorus_Delay=23", "live:8:Chorus_Depth=0.3", "live:11:Chorus_Rate=7" },
        new[] { "context_live_quad_flange", "mode=Quad", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "live:5:Flange_Depth=0.2", "live:8:Flange_Rate=7" },
        new[] { "context_live_quad_reverb", "mode=Quad", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "live:5:Reverb_Room HF=-1800", "live:8:Reverb_Dry Level=-1800", "live:11:Reverb_Decay Time=0.7" },
        new[] { "context_live_quad_zone_enable", "mode=Quad", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=enable" },
        new[] { "context_live_quad_zone_preset", "mode=Quad", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=preset" },
        new[] { "context_live_surround_chorus", "mode=Surround", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "live:5:Chorus_Delay=23", "live:8:Chorus_Depth=0.3", "live:11:Chorus_Rate=7" },
        new[] { "context_live_surround_flange", "mode=Surround", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "live:5:Flange_Depth=0.2", "live:8:Flange_Rate=7" },
        new[] { "context_live_surround_reverb", "mode=Surround", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "live:5:Reverb_Room HF=-1800", "live:8:Reverb_Dry Level=-1800", "live:11:Reverb_Decay Time=0.7" },
        new[] { "context_live_surround_zone_enable", "mode=Surround", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=enable" },
        new[] { "context_live_surround_zone_preset", "mode=Surround", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=preset" },
        new[] { "context_live_51_chorus", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "live:5:Chorus_Delay=23", "live:8:Chorus_Depth=0.3", "live:11:Chorus_Rate=7" },
        new[] { "context_live_51_flange", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "live:5:Flange_Depth=0.2", "live:8:Flange_Rate=7" },
        new[] { "context_live_51_reverb", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "live:5:Reverb_Room HF=-1800", "live:8:Reverb_Dry Level=-1800", "live:11:Reverb_Decay Time=0.7" },
        new[] { "context_live_51_zone_enable", "mode=Mode5point1", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=enable" },
        new[] { "context_live_51_zone_preset", "mode=Mode5point1", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=preset" },
        new[] { "context_live_71_chorus", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "live:5:Chorus_Delay=23", "live:8:Chorus_Depth=0.3", "live:11:Chorus_Rate=7" },
        new[] { "context_live_71_flange", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "live:5:Flange_Depth=0.2", "live:8:Flange_Rate=7" },
        new[] { "context_live_71_reverb", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "live:5:Reverb_Room HF=-1800", "live:8:Reverb_Dry Level=-1800", "live:11:Reverb_Decay Time=0.7" },
        new[] { "context_live_71_zone_enable", "mode=Mode7point1", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=enable" },
        new[] { "context_live_71_zone_preset", "mode=Mode7point1", "music=dc:0.25", "mono=1", "blend=1", "pos=1:0:1.732", "Music:Att", "zone=1", "zone_live=preset" },
        new[] { "context_live_quad_chorus_source", "mode=Quad", "music=generator", "gen=burst", "Music:Att", "component=chorus:source", "component_live=1" },
        new[] { "context_live_quad_chorus_listener", "mode=Quad", "music=generator", "gen=burst", "Music:Att", "component=chorus:listener", "component_live=1" },
        new[] { "context_live_quad_reverb_source", "mode=Quad", "music=generator", "gen=burst", "Music:Att", "component=reverb:source", "component_live=1" },
        new[] { "context_live_quad_reverb_listener", "mode=Quad", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener", "component_live=1" },
        new[] { "context_live_surround_chorus_source", "mode=Surround", "music=generator", "gen=burst", "Music:Att", "component=chorus:source", "component_live=1" },
        new[] { "context_live_surround_chorus_listener", "mode=Surround", "music=generator", "gen=burst", "Music:Att", "component=chorus:listener", "component_live=1" },
        new[] { "context_live_surround_reverb_source", "mode=Surround", "music=generator", "gen=burst", "Music:Att", "component=reverb:source", "component_live=1" },
        new[] { "context_live_surround_reverb_listener", "mode=Surround", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener", "component_live=1" },
        new[] { "context_live_51_chorus_source", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att", "component=chorus:source", "component_live=1" },
        new[] { "context_live_51_chorus_listener", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att", "component=chorus:listener", "component_live=1" },
        new[] { "context_live_51_reverb_source", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att", "component=reverb:source", "component_live=1" },
        new[] { "context_live_51_reverb_listener", "mode=Mode5point1", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener", "component_live=1" },
        new[] { "context_live_71_chorus_source", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att", "component=chorus:source", "component_live=1" },
        new[] { "context_live_71_chorus_listener", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att", "component=chorus:listener", "component_live=1" },
        new[] { "context_live_71_reverb_source", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att", "component=reverb:source", "component_live=1" },
        new[] { "context_live_71_reverb_listener", "mode=Mode7point1", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener", "component_live=1" },
        new[] { "context_recovery_quad_mode7point1_dry", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_quad_mode7point1_dry" },
        new[] { "context_recovery_quad_mode7point1_chorus_source", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_quad_mode7point1_chorus_source" },
        new[] { "context_recovery_quad_mode7point1_reverb_listener", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_quad_mode7point1_reverb_listener" },
        new[] { "context_recovery_quad_mode7point1_reverb_mixer", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_quad_mode7point1_reverb_mixer" },
        new[] { "context_recovery_mode7point1_quad_dry", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode7point1_quad_dry" },
        new[] { "context_recovery_mode7point1_quad_chorus_source", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode7point1_quad_chorus_source" },
        new[] { "context_recovery_mode7point1_quad_reverb_listener", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode7point1_quad_reverb_listener" },
        new[] { "context_recovery_mode7point1_quad_reverb_mixer", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode7point1_quad_reverb_mixer" },
        new[] { "context_recovery_surround_mode5point1_dry", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_surround_mode5point1_dry" },
        new[] { "context_recovery_surround_mode5point1_chorus_source", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_surround_mode5point1_chorus_source" },
        new[] { "context_recovery_surround_mode5point1_reverb_listener", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_surround_mode5point1_reverb_listener" },
        new[] { "context_recovery_surround_mode5point1_reverb_mixer", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_surround_mode5point1_reverb_mixer" },
        new[] { "context_recovery_mode5point1_surround_dry", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode5point1_surround_dry" },
        new[] { "context_recovery_mode5point1_surround_chorus_source", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode5point1_surround_chorus_source" },
        new[] { "context_recovery_mode5point1_surround_reverb_listener", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode5point1_surround_reverb_listener" },
        new[] { "context_recovery_mode5point1_surround_reverb_mixer", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_mode5point1_surround_reverb_mixer" },
        new[] { "context_recovery_stereo_quad_idle", "mode=Stereo", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=0", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_stereo_quad_idle" },
        new[] { "context_recovery_stereo_stereo_dry", "mode=Stereo", "reset_to=Stereo", "music=dc:0.25", "Music:Att", "recover=1", "preflight=4", "recovery_renderer=1", "recovery_pause_ms=60", "asset=context_live_reset_stereo_quad_idle" },
        new[] { "context_recovery_stereo_stereo_idle", "mode=Stereo", "reset_to=Stereo", "music=dc:0.25", "Music:Att", "recover=0", "preflight=4", "recovery_renderer=1", "asset=context_live_reset_stereo_quad_idle" },
        new[] { "context_live_reset_quad_mode7point1_dry", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1" },
        new[] { "context_live_reset_quad_mode7point1_chorus_source", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source" },
        new[] { "context_live_reset_quad_mode7point1_reverb_listener", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener" },
        new[] { "context_live_reset_quad_mode7point1_reverb_mixer", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600" },
        new[] { "context_live_reset_mode7point1_quad_dry", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1" },
        new[] { "context_live_reset_mode7point1_quad_chorus_source", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source" },
        new[] { "context_live_reset_mode7point1_quad_reverb_listener", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener" },
        new[] { "context_live_reset_mode7point1_quad_reverb_mixer", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600" },
        new[] { "context_live_reset_surround_mode5point1_dry", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1" },
        new[] { "context_live_reset_surround_mode5point1_chorus_source", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source" },
        new[] { "context_live_reset_surround_mode5point1_reverb_listener", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener" },
        new[] { "context_live_reset_surround_mode5point1_reverb_mixer", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600" },
        new[] { "context_live_reset_mode5point1_surround_dry", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1" },
        new[] { "context_live_reset_mode5point1_surround_chorus_source", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source" },
        new[] { "context_live_reset_mode5point1_surround_reverb_listener", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener" },
        new[] { "context_live_reset_mode5point1_surround_reverb_mixer", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600" },
        new[] { "context_live_reset_stereo_quad_idle", "mode=Stereo", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=0" },
        new[] { "context_live_quad_comp", "mode=Quad", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=20", "Music.Comp.Release=80", "live:8:Comp_Threshold=-30", "live:5:Comp_Attack=50", "live:11:Comp_Release=30" },
        new[] { "context_live_quad_duck", "mode=Quad", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1", "live:8:Duck_Threshold=-30", "live:5:Duck_Attack Time=0.05", "live:11:Duck_Release Time=0.02" },
        new[] { "context_live_surround_comp", "mode=Surround", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=20", "Music.Comp.Release=80", "live:8:Comp_Threshold=-30", "live:5:Comp_Attack=50", "live:11:Comp_Release=30" },
        new[] { "context_live_surround_duck", "mode=Surround", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1", "live:8:Duck_Threshold=-30", "live:5:Duck_Attack Time=0.05", "live:11:Duck_Release Time=0.02" },
        new[] { "context_live_51_comp", "mode=Mode5point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=20", "Music.Comp.Release=80", "live:8:Comp_Threshold=-30", "live:5:Comp_Attack=50", "live:11:Comp_Release=30" },
        new[] { "context_live_51_duck", "mode=Mode5point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1", "live:8:Duck_Threshold=-30", "live:5:Duck_Attack Time=0.05", "live:11:Duck_Release Time=0.02" },
        new[] { "context_live_71_comp", "mode=Mode7point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=20", "Music.Comp.Release=80", "live:8:Comp_Threshold=-30", "live:5:Comp_Attack=50", "live:11:Comp_Release=30" },
        new[] { "context_live_71_duck", "mode=Mode7point1", "music=generator", "gen=link", "dominant=2", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-18", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.02", "Master.Duck.Release Time=0.04", "Master.Duck.Sidechain Mix=1", "live:8:Duck_Threshold=-30", "live:5:Duck_Attack Time=0.05", "live:11:Duck_Release Time=0.02" },
        new[] { "context_live_stereo_comp_thresholds", "mode=Stereo", "music=generator", "gen=link", "dominant=0", "Music:Att,Comp", "Music.Comp.Threshold=-18", "Music.Comp.Attack=20", "Music.Comp.Release=80", "live:5:Comp_Attack=50", "live:8:Comp_Threshold=-30", "live:11:Comp_Release=30", "live:16:Comp_Threshold=-12", "live:21:Comp_Make up gain=6" },
        new[] { "context_live_reset_quad_mode7point1_dry_scheduled", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "scheduled=1" },
        new[] { "context_live_reset_quad_mode7point1_chorus_source_scheduled", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "scheduled=1" },
        new[] { "context_live_reset_quad_mode7point1_reverb_listener_scheduled", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "scheduled=1" },
        new[] { "context_live_reset_quad_mode7point1_reverb_mixer_scheduled", "mode=Quad", "reset_to=Mode7point1", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "scheduled=1" },
        new[] { "context_live_reset_mode7point1_quad_dry_scheduled", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "scheduled=1" },
        new[] { "context_live_reset_mode7point1_quad_chorus_source_scheduled", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "scheduled=1" },
        new[] { "context_live_reset_mode7point1_quad_reverb_listener_scheduled", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "scheduled=1" },
        new[] { "context_live_reset_mode7point1_quad_reverb_mixer_scheduled", "mode=Mode7point1", "reset_to=Quad", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "scheduled=1" },
        new[] { "context_live_reset_surround_mode5point1_dry_scheduled", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "scheduled=1" },
        new[] { "context_live_reset_surround_mode5point1_chorus_source_scheduled", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "scheduled=1" },
        new[] { "context_live_reset_surround_mode5point1_reverb_listener_scheduled", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "scheduled=1" },
        new[] { "context_live_reset_surround_mode5point1_reverb_mixer_scheduled", "mode=Surround", "reset_to=Mode5point1", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "scheduled=1" },
        new[] { "context_live_reset_mode5point1_surround_dry_scheduled", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "scheduled=1" },
        new[] { "context_live_reset_mode5point1_surround_chorus_source_scheduled", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "component=chorus:source", "scheduled=1" },
        new[] { "context_live_reset_mode5point1_surround_reverb_listener_scheduled", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att", "recover=1", "component=reverb:listener", "scheduled=1" },
        new[] { "context_live_reset_mode5point1_surround_reverb_mixer_scheduled", "mode=Mode5point1", "reset_to=Surround", "music=dc:0.25", "Music:Att,Reverb", "recover=1", "Music.Reverb.Room=-500", "Music.Reverb.Dry Level=-600", "scheduled=1" },
        new[] { "context_live_reset_stereo_quad_idle_scheduled", "mode=Stereo", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=0", "scheduled=1" },
        new[] { "context_live_reset_stereo_quad_fraction_play_scheduled", "mode=Stereo", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "scheduled=1", "schedule_fraction=0.75", "schedule_edit=play" },
        new[] { "context_live_reset_stereo_quad_fraction_start_scheduled", "mode=Stereo", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "scheduled=1", "schedule_fraction=0.75", "schedule_edit=start" },
        new[] { "context_live_reset_stereo_quad_fraction_end_scheduled", "mode=Stereo", "reset_to=Quad", "music=dc:0.25", "Music:Att", "recover=1", "scheduled=1", "schedule_fraction=0.75", "schedule_edit=end" },
        new[] { "context_wet_quad_dist_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wet_quad_dist_floor", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-70" },
        new[] { "context_wet_quad_dist_half", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_quad_dist_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wet_quad_dist_boost", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=6" },
        new[] { "context_wet_quad_dist_max", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=30" },
        new[] { "context_wet_quad_dist_before", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_quad_dist_live", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:5:Dist_Wet=0", "live:8:Dist_Wet=-80", "live:11:Dist_Wet=-70", "live:14:Dist_Wet=-6", "live:17:Dist_Wet=6", "live:20:Dist_Wet=-6" },
        new[] { "context_wet_quad_chorus_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-80" },
        new[] { "context_wet_quad_chorus_floor", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-70" },
        new[] { "context_wet_quad_chorus_half", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_quad_chorus_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_wet_quad_chorus_boost", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=6" },
        new[] { "context_wet_quad_chorus_max", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=30" },
        new[] { "context_wet_quad_chorus_before", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Chorus,Att", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_quad_chorus_live", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6", "live:5:Chorus_Wet=0", "live:8:Chorus_Wet=-80", "live:11:Chorus_Wet=-70", "live:14:Chorus_Wet=-6", "live:17:Chorus_Wet=6", "live:20:Chorus_Wet=-6" },
        new[] { "context_wet_quad_flange_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-80" },
        new[] { "context_wet_quad_flange_floor", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-70" },
        new[] { "context_wet_quad_flange_half", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_quad_flange_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=0" },
        new[] { "context_wet_quad_flange_boost", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=6" },
        new[] { "context_wet_quad_flange_max", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=30" },
        new[] { "context_wet_quad_flange_before", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Flange,Att", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_quad_flange_live", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6", "live:5:Flange_Wet=0", "live:8:Flange_Wet=-80", "live:11:Flange_Wet=-70", "live:14:Flange_Wet=-6", "live:17:Flange_Wet=6", "live:20:Flange_Wet=-6" },
        new[] { "context_wet_quad_norm_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-80" },
        new[] { "context_wet_quad_norm_floor", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-70" },
        new[] { "context_wet_quad_norm_half", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_quad_norm_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=0" },
        new[] { "context_wet_quad_norm_boost", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=6" },
        new[] { "context_wet_quad_norm_max", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=30" },
        new[] { "context_wet_quad_norm_before", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Norm,Att", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_quad_norm_live", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:5:Norm_Wet=0", "live:8:Norm_Wet=-80", "live:11:Norm_Wet=-70", "live:14:Norm_Wet=-6", "live:17:Norm_Wet=6", "live:20:Norm_Wet=-6" },
        new[] { "context_wet_quad_shift_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-80" },
        new[] { "context_wet_quad_shift_floor", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-70" },
        new[] { "context_wet_quad_shift_half", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_quad_shift_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=0" },
        new[] { "context_wet_quad_shift_boost", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=6" },
        new[] { "context_wet_quad_shift_max", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=30" },
        new[] { "context_wet_quad_shift_before", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Shift,Att", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_quad_shift_live", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:5:Shift_Wet=0", "live:8:Shift_Wet=-80", "live:11:Shift_Wet=-70", "live:14:Shift_Wet=-6", "live:17:Shift_Wet=6", "live:20:Shift_Wet=-6" },
        new[] { "context_wet_quad_reverb_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wet_quad_reverb_floor", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-70" },
        new[] { "context_wet_quad_reverb_half", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_quad_reverb_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wet_quad_reverb_boost", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=6" },
        new[] { "context_wet_quad_reverb_max", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=30" },
        new[] { "context_wet_quad_reverb_before", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_quad_reverb_live", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:5:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:11:Reverb_Wet=-70", "live:14:Reverb_Wet=-6", "live:17:Reverb_Wet=6", "live:20:Reverb_Wet=-6" },
        new[] { "context_wet_quad_receive_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0" },
        new[] { "context_wet_quad_receive_half", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-6" },
        new[] { "context_wet_quad_receive_off", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-80" },
        new[] { "context_wet_quad_receive_live", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0", "live:14:Send_Level=-12" },
        new[] { "context_wet_quad_receive_post", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Send", "Music.Volume=-6", "Master:Receive,Att", "Master.Volume=-6", "Music.Send.Level=-6" },
        new[] { "context_wet_quad_receive_bypass", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "Music.Send.bypass=1", "Master.Receive.bypass=1" },
        new[] { "context_wet_quad_norm_link_0", "mode=Quad", "preflight=4", "music=generator", "gen=link", "dominant=0", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_quad_norm_link_3", "mode=Quad", "preflight=4", "music=generator", "gen=link", "dominant=3", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_surround_dist_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wet_surround_dist_floor", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-70" },
        new[] { "context_wet_surround_dist_half", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_surround_dist_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wet_surround_dist_boost", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=6" },
        new[] { "context_wet_surround_dist_max", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=30" },
        new[] { "context_wet_surround_dist_before", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_surround_dist_live", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:5:Dist_Wet=0", "live:8:Dist_Wet=-80", "live:11:Dist_Wet=-70", "live:14:Dist_Wet=-6", "live:17:Dist_Wet=6", "live:20:Dist_Wet=-6" },
        new[] { "context_wet_surround_chorus_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-80" },
        new[] { "context_wet_surround_chorus_floor", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-70" },
        new[] { "context_wet_surround_chorus_half", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_surround_chorus_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_wet_surround_chorus_boost", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=6" },
        new[] { "context_wet_surround_chorus_max", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=30" },
        new[] { "context_wet_surround_chorus_before", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Chorus,Att", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_surround_chorus_live", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6", "live:5:Chorus_Wet=0", "live:8:Chorus_Wet=-80", "live:11:Chorus_Wet=-70", "live:14:Chorus_Wet=-6", "live:17:Chorus_Wet=6", "live:20:Chorus_Wet=-6" },
        new[] { "context_wet_surround_flange_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-80" },
        new[] { "context_wet_surround_flange_floor", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-70" },
        new[] { "context_wet_surround_flange_half", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_surround_flange_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=0" },
        new[] { "context_wet_surround_flange_boost", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=6" },
        new[] { "context_wet_surround_flange_max", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=30" },
        new[] { "context_wet_surround_flange_before", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Flange,Att", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_surround_flange_live", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6", "live:5:Flange_Wet=0", "live:8:Flange_Wet=-80", "live:11:Flange_Wet=-70", "live:14:Flange_Wet=-6", "live:17:Flange_Wet=6", "live:20:Flange_Wet=-6" },
        new[] { "context_wet_surround_norm_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-80" },
        new[] { "context_wet_surround_norm_floor", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-70" },
        new[] { "context_wet_surround_norm_half", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_surround_norm_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=0" },
        new[] { "context_wet_surround_norm_boost", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=6" },
        new[] { "context_wet_surround_norm_max", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=30" },
        new[] { "context_wet_surround_norm_before", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Norm,Att", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_surround_norm_live", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:5:Norm_Wet=0", "live:8:Norm_Wet=-80", "live:11:Norm_Wet=-70", "live:14:Norm_Wet=-6", "live:17:Norm_Wet=6", "live:20:Norm_Wet=-6" },
        new[] { "context_wet_surround_shift_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-80" },
        new[] { "context_wet_surround_shift_floor", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-70" },
        new[] { "context_wet_surround_shift_half", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_surround_shift_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=0" },
        new[] { "context_wet_surround_shift_boost", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=6" },
        new[] { "context_wet_surround_shift_max", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=30" },
        new[] { "context_wet_surround_shift_before", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Shift,Att", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_surround_shift_live", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:5:Shift_Wet=0", "live:8:Shift_Wet=-80", "live:11:Shift_Wet=-70", "live:14:Shift_Wet=-6", "live:17:Shift_Wet=6", "live:20:Shift_Wet=-6" },
        new[] { "context_wet_surround_reverb_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wet_surround_reverb_floor", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-70" },
        new[] { "context_wet_surround_reverb_half", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_surround_reverb_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wet_surround_reverb_boost", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=6" },
        new[] { "context_wet_surround_reverb_max", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=30" },
        new[] { "context_wet_surround_reverb_before", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_surround_reverb_live", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:5:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:11:Reverb_Wet=-70", "live:14:Reverb_Wet=-6", "live:17:Reverb_Wet=6", "live:20:Reverb_Wet=-6" },
        new[] { "context_wet_surround_receive_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0" },
        new[] { "context_wet_surround_receive_half", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-6" },
        new[] { "context_wet_surround_receive_off", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-80" },
        new[] { "context_wet_surround_receive_live", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0", "live:14:Send_Level=-12" },
        new[] { "context_wet_surround_receive_post", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Send", "Music.Volume=-6", "Master:Receive,Att", "Master.Volume=-6", "Music.Send.Level=-6" },
        new[] { "context_wet_surround_receive_bypass", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "Music.Send.bypass=1", "Master.Receive.bypass=1" },
        new[] { "context_wet_surround_norm_link_0", "mode=Surround", "preflight=4", "music=generator", "gen=link", "dominant=0", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_surround_norm_link_4", "mode=Surround", "preflight=4", "music=generator", "gen=link", "dominant=4", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_51_dist_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wet_51_dist_floor", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-70" },
        new[] { "context_wet_51_dist_half", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_51_dist_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wet_51_dist_boost", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=6" },
        new[] { "context_wet_51_dist_max", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=30" },
        new[] { "context_wet_51_dist_before", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_51_dist_live", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:5:Dist_Wet=0", "live:8:Dist_Wet=-80", "live:11:Dist_Wet=-70", "live:14:Dist_Wet=-6", "live:17:Dist_Wet=6", "live:20:Dist_Wet=-6" },
        new[] { "context_wet_51_chorus_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-80" },
        new[] { "context_wet_51_chorus_floor", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-70" },
        new[] { "context_wet_51_chorus_half", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_51_chorus_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_wet_51_chorus_boost", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=6" },
        new[] { "context_wet_51_chorus_max", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=30" },
        new[] { "context_wet_51_chorus_before", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Chorus,Att", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_51_chorus_live", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6", "live:5:Chorus_Wet=0", "live:8:Chorus_Wet=-80", "live:11:Chorus_Wet=-70", "live:14:Chorus_Wet=-6", "live:17:Chorus_Wet=6", "live:20:Chorus_Wet=-6" },
        new[] { "context_wet_51_flange_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-80" },
        new[] { "context_wet_51_flange_floor", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-70" },
        new[] { "context_wet_51_flange_half", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_51_flange_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=0" },
        new[] { "context_wet_51_flange_boost", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=6" },
        new[] { "context_wet_51_flange_max", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=30" },
        new[] { "context_wet_51_flange_before", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Flange,Att", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_51_flange_live", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6", "live:5:Flange_Wet=0", "live:8:Flange_Wet=-80", "live:11:Flange_Wet=-70", "live:14:Flange_Wet=-6", "live:17:Flange_Wet=6", "live:20:Flange_Wet=-6" },
        new[] { "context_wet_51_norm_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-80" },
        new[] { "context_wet_51_norm_floor", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-70" },
        new[] { "context_wet_51_norm_half", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_51_norm_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=0" },
        new[] { "context_wet_51_norm_boost", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=6" },
        new[] { "context_wet_51_norm_max", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=30" },
        new[] { "context_wet_51_norm_before", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Norm,Att", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_51_norm_live", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:5:Norm_Wet=0", "live:8:Norm_Wet=-80", "live:11:Norm_Wet=-70", "live:14:Norm_Wet=-6", "live:17:Norm_Wet=6", "live:20:Norm_Wet=-6" },
        new[] { "context_wet_51_shift_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-80" },
        new[] { "context_wet_51_shift_floor", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-70" },
        new[] { "context_wet_51_shift_half", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_51_shift_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=0" },
        new[] { "context_wet_51_shift_boost", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=6" },
        new[] { "context_wet_51_shift_max", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=30" },
        new[] { "context_wet_51_shift_before", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Shift,Att", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_51_shift_live", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:5:Shift_Wet=0", "live:8:Shift_Wet=-80", "live:11:Shift_Wet=-70", "live:14:Shift_Wet=-6", "live:17:Shift_Wet=6", "live:20:Shift_Wet=-6" },
        new[] { "context_wet_51_reverb_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wet_51_reverb_floor", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-70" },
        new[] { "context_wet_51_reverb_half", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_51_reverb_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wet_51_reverb_boost", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=6" },
        new[] { "context_wet_51_reverb_max", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=30" },
        new[] { "context_wet_51_reverb_before", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_51_reverb_live", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:5:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:11:Reverb_Wet=-70", "live:14:Reverb_Wet=-6", "live:17:Reverb_Wet=6", "live:20:Reverb_Wet=-6" },
        new[] { "context_wet_51_receive_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0" },
        new[] { "context_wet_51_receive_half", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-6" },
        new[] { "context_wet_51_receive_off", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-80" },
        new[] { "context_wet_51_receive_live", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0", "live:14:Send_Level=-12" },
        new[] { "context_wet_51_receive_post", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Send", "Music.Volume=-6", "Master:Receive,Att", "Master.Volume=-6", "Music.Send.Level=-6" },
        new[] { "context_wet_51_receive_bypass", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "Music.Send.bypass=1", "Master.Receive.bypass=1" },
        new[] { "context_wet_51_norm_link_0", "mode=Mode5point1", "preflight=4", "music=generator", "gen=link", "dominant=0", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_51_norm_link_3", "mode=Mode5point1", "preflight=4", "music=generator", "gen=link", "dominant=3", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_71_dist_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wet_71_dist_floor", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-70" },
        new[] { "context_wet_71_dist_half", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_71_dist_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wet_71_dist_boost", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=6" },
        new[] { "context_wet_71_dist_max", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=30" },
        new[] { "context_wet_71_dist_before", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_wet_71_dist_live", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:5:Dist_Wet=0", "live:8:Dist_Wet=-80", "live:11:Dist_Wet=-70", "live:14:Dist_Wet=-6", "live:17:Dist_Wet=6", "live:20:Dist_Wet=-6" },
        new[] { "context_wet_71_chorus_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-80" },
        new[] { "context_wet_71_chorus_floor", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-70" },
        new[] { "context_wet_71_chorus_half", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_71_chorus_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_wet_71_chorus_boost", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=6" },
        new[] { "context_wet_71_chorus_max", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=30" },
        new[] { "context_wet_71_chorus_before", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Chorus,Att", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_wet_71_chorus_live", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6", "live:5:Chorus_Wet=0", "live:8:Chorus_Wet=-80", "live:11:Chorus_Wet=-70", "live:14:Chorus_Wet=-6", "live:17:Chorus_Wet=6", "live:20:Chorus_Wet=-6" },
        new[] { "context_wet_71_flange_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-80" },
        new[] { "context_wet_71_flange_floor", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-70" },
        new[] { "context_wet_71_flange_half", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_71_flange_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=0" },
        new[] { "context_wet_71_flange_boost", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=6" },
        new[] { "context_wet_71_flange_max", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=30" },
        new[] { "context_wet_71_flange_before", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Flange,Att", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_wet_71_flange_live", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6", "live:5:Flange_Wet=0", "live:8:Flange_Wet=-80", "live:11:Flange_Wet=-70", "live:14:Flange_Wet=-6", "live:17:Flange_Wet=6", "live:20:Flange_Wet=-6" },
        new[] { "context_wet_71_norm_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-80" },
        new[] { "context_wet_71_norm_floor", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-70" },
        new[] { "context_wet_71_norm_half", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_71_norm_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=0" },
        new[] { "context_wet_71_norm_boost", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=6" },
        new[] { "context_wet_71_norm_max", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=30" },
        new[] { "context_wet_71_norm_before", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Norm,Att", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_71_norm_live", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:5:Norm_Wet=0", "live:8:Norm_Wet=-80", "live:11:Norm_Wet=-70", "live:14:Norm_Wet=-6", "live:17:Norm_Wet=6", "live:20:Norm_Wet=-6" },
        new[] { "context_wet_71_shift_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-80" },
        new[] { "context_wet_71_shift_floor", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-70" },
        new[] { "context_wet_71_shift_half", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_71_shift_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=0" },
        new[] { "context_wet_71_shift_boost", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=6" },
        new[] { "context_wet_71_shift_max", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=30" },
        new[] { "context_wet_71_shift_before", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Shift,Att", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_wet_71_shift_live", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:5:Shift_Wet=0", "live:8:Shift_Wet=-80", "live:11:Shift_Wet=-70", "live:14:Shift_Wet=-6", "live:17:Shift_Wet=6", "live:20:Shift_Wet=-6" },
        new[] { "context_wet_71_reverb_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wet_71_reverb_floor", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-70" },
        new[] { "context_wet_71_reverb_half", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_71_reverb_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wet_71_reverb_boost", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=6" },
        new[] { "context_wet_71_reverb_max", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=30" },
        new[] { "context_wet_71_reverb_before", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_71_reverb_live", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:5:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:11:Reverb_Wet=-70", "live:14:Reverb_Wet=-6", "live:17:Reverb_Wet=6", "live:20:Reverb_Wet=-6" },
        new[] { "context_wet_71_receive_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0" },
        new[] { "context_wet_71_receive_half", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-6" },
        new[] { "context_wet_71_receive_off", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=-80" },
        new[] { "context_wet_71_receive_live", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0", "live:14:Send_Level=-12" },
        new[] { "context_wet_71_receive_post", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Send", "Music.Volume=-6", "Master:Receive,Att", "Master.Volume=-6", "Music.Send.Level=-6" },
        new[] { "context_wet_71_receive_bypass", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "Music.Send.bypass=1", "Master.Receive.bypass=1" },
        new[] { "context_wet_71_norm_link_0", "mode=Mode7point1", "preflight=4", "music=generator", "gen=link", "dominant=0", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_71_norm_link_7", "mode=Mode7point1", "preflight=4", "music=generator", "gen=link", "dominant=7", "Music:Att,Norm", "Music.Norm.Fade in time=0", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_wet_stereo_reverb_live", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Room=-500", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Wet=-6", "live:5:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:11:Reverb_Wet=-6", "live:14:Reverb_Wet=6" },
        new[] { "context_wet_stereo_receive_live", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0" },
        new[] { "context_wet_quad_reverb_unwrapped", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_wet_quad_reverb_late", "mode=Quad", "preflight=4", "music=generator", "gen=lateall", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_surround_reverb_unwrapped", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_wet_surround_reverb_late", "mode=Surround", "preflight=4", "music=generator", "gen=lateall", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_51_reverb_unwrapped", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_wet_51_reverb_late", "mode=Mode5point1", "preflight=4", "music=generator", "gen=lateall", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_71_reverb_unwrapped", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_wet_71_reverb_late", "mode=Mode7point1", "preflight=4", "music=generator", "gen=lateall", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_wet_stereo_dist_floor", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-70" },
        new[] { "context_wet_stereo_dist_floor_exposed", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-70", "live:31:Dist_Wet=-70" },
        new[] { "context_wet_stereo_dist_full", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wet_stereo_dist_full_exposed", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=0", "live:31:Dist_Wet=0" },
        new[] { "context_wetedge_quad_dist_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wetedge_quad_dist_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wetedge_quad_reverb_full", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wetedge_quad_reverb_dry", "mode=Quad", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wetedge_surround_dist_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wetedge_surround_dist_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wetedge_surround_reverb_full", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wetedge_surround_reverb_dry", "mode=Surround", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wetedge_51_dist_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wetedge_51_dist_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wetedge_51_reverb_full", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wetedge_51_reverb_dry", "mode=Mode5point1", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wetedge_71_dist_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wetedge_71_dist_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_wetedge_71_reverb_full", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_wetedge_71_reverb_dry", "mode=Mode7point1", "preflight=4", "music=generator", "gen=all", "Music:Reverb,Att", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-80" },
        new[] { "context_wetedge_stereo_dist_full", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_wetedge_stereo_dist_dry", "mode=Stereo", "preflight=4", "music=generator", "gen=all", "Music:Dist,Att", "Music.Dist.Level=0.7", "Music.Dist.Wet=-80" },
        new[] { "context_settings_32000_256_2_dry", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_32000_256_2_dist", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:16:Dist_Wet=0", "live:32:Dist_Wet=-80", "live:64:Dist_Wet=-6" },
        new[] { "context_settings_32000_256_2_chorus", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:16:Chorus_Wet=0", "live:32:Chorus_Wet=-80", "live:64:Chorus_Wet=-6" },
        new[] { "context_settings_32000_256_2_flange", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:16:Flange_Wet=0", "live:32:Flange_Wet=-80", "live:64:Flange_Wet=-6" },
        new[] { "context_settings_32000_256_2_norm", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:16:Norm_Wet=0", "live:32:Norm_Wet=-80", "live:64:Norm_Wet=-6" },
        new[] { "context_settings_32000_256_2_shift", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:16:Shift_Wet=0", "live:32:Shift_Wet=-80", "live:64:Shift_Wet=-6" },
        new[] { "context_settings_32000_256_2_reverb", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:16:Reverb_Wet=0", "live:32:Reverb_Wet=-80", "live:64:Reverb_Wet=-6" },
        new[] { "context_settings_32000_256_2_reverb_startup", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_32000_256_2_receive", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:16:Send_Level=-6", "live:32:Send_Level=-80", "live:64:Send_Level=0" },
        new[] { "context_settings_32000_256_2_duck", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_32000_256_2_compressor", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_32000_256_2_source_chorus", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_32000_256_2_listener_reverb", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_32000_256_2_zone", "rate=32000", "buffer=256", "mode=Stereo", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_32000_256_4_dry", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_32000_256_4_dist", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:16:Dist_Wet=0", "live:32:Dist_Wet=-80", "live:64:Dist_Wet=-6" },
        new[] { "context_settings_32000_256_4_chorus", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:16:Chorus_Wet=0", "live:32:Chorus_Wet=-80", "live:64:Chorus_Wet=-6" },
        new[] { "context_settings_32000_256_4_flange", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:16:Flange_Wet=0", "live:32:Flange_Wet=-80", "live:64:Flange_Wet=-6" },
        new[] { "context_settings_32000_256_4_norm", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:16:Norm_Wet=0", "live:32:Norm_Wet=-80", "live:64:Norm_Wet=-6" },
        new[] { "context_settings_32000_256_4_shift", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:16:Shift_Wet=0", "live:32:Shift_Wet=-80", "live:64:Shift_Wet=-6" },
        new[] { "context_settings_32000_256_4_reverb", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:16:Reverb_Wet=0", "live:32:Reverb_Wet=-80", "live:64:Reverb_Wet=-6" },
        new[] { "context_settings_32000_256_4_reverb_startup", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_32000_256_4_receive", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:16:Send_Level=-6", "live:32:Send_Level=-80", "live:64:Send_Level=0" },
        new[] { "context_settings_32000_256_4_duck", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_32000_256_4_compressor", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=link", "dominant=3", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_32000_256_4_source_chorus", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_32000_256_4_listener_reverb", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_32000_256_4_zone", "rate=32000", "buffer=256", "mode=Quad", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_32000_256_5_dry", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_32000_256_5_dist", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:16:Dist_Wet=0", "live:32:Dist_Wet=-80", "live:64:Dist_Wet=-6" },
        new[] { "context_settings_32000_256_5_chorus", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:16:Chorus_Wet=0", "live:32:Chorus_Wet=-80", "live:64:Chorus_Wet=-6" },
        new[] { "context_settings_32000_256_5_flange", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:16:Flange_Wet=0", "live:32:Flange_Wet=-80", "live:64:Flange_Wet=-6" },
        new[] { "context_settings_32000_256_5_norm", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:16:Norm_Wet=0", "live:32:Norm_Wet=-80", "live:64:Norm_Wet=-6" },
        new[] { "context_settings_32000_256_5_shift", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:16:Shift_Wet=0", "live:32:Shift_Wet=-80", "live:64:Shift_Wet=-6" },
        new[] { "context_settings_32000_256_5_reverb", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:16:Reverb_Wet=0", "live:32:Reverb_Wet=-80", "live:64:Reverb_Wet=-6" },
        new[] { "context_settings_32000_256_5_reverb_startup", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_32000_256_5_receive", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:16:Send_Level=-6", "live:32:Send_Level=-80", "live:64:Send_Level=0" },
        new[] { "context_settings_32000_256_5_duck", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_32000_256_5_compressor", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=link", "dominant=4", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_32000_256_5_source_chorus", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_32000_256_5_listener_reverb", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_32000_256_5_zone", "rate=32000", "buffer=256", "mode=Surround", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_32000_256_6_dry", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_32000_256_6_dist", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:16:Dist_Wet=0", "live:32:Dist_Wet=-80", "live:64:Dist_Wet=-6" },
        new[] { "context_settings_32000_256_6_chorus", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:16:Chorus_Wet=0", "live:32:Chorus_Wet=-80", "live:64:Chorus_Wet=-6" },
        new[] { "context_settings_32000_256_6_flange", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:16:Flange_Wet=0", "live:32:Flange_Wet=-80", "live:64:Flange_Wet=-6" },
        new[] { "context_settings_32000_256_6_norm", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:16:Norm_Wet=0", "live:32:Norm_Wet=-80", "live:64:Norm_Wet=-6" },
        new[] { "context_settings_32000_256_6_shift", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:16:Shift_Wet=0", "live:32:Shift_Wet=-80", "live:64:Shift_Wet=-6" },
        new[] { "context_settings_32000_256_6_reverb", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:16:Reverb_Wet=0", "live:32:Reverb_Wet=-80", "live:64:Reverb_Wet=-6" },
        new[] { "context_settings_32000_256_6_reverb_startup", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_32000_256_6_receive", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:16:Send_Level=-6", "live:32:Send_Level=-80", "live:64:Send_Level=0" },
        new[] { "context_settings_32000_256_6_duck", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_32000_256_6_compressor", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=link", "dominant=5", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_32000_256_6_source_chorus", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_32000_256_6_listener_reverb", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_32000_256_6_zone", "rate=32000", "buffer=256", "mode=Mode5point1", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_32000_256_8_dry", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_32000_256_8_dist", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:16:Dist_Wet=0", "live:32:Dist_Wet=-80", "live:64:Dist_Wet=-6" },
        new[] { "context_settings_32000_256_8_chorus", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:16:Chorus_Wet=0", "live:32:Chorus_Wet=-80", "live:64:Chorus_Wet=-6" },
        new[] { "context_settings_32000_256_8_flange", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:16:Flange_Wet=0", "live:32:Flange_Wet=-80", "live:64:Flange_Wet=-6" },
        new[] { "context_settings_32000_256_8_norm", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:16:Norm_Wet=0", "live:32:Norm_Wet=-80", "live:64:Norm_Wet=-6" },
        new[] { "context_settings_32000_256_8_shift", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:16:Shift_Wet=0", "live:32:Shift_Wet=-80", "live:64:Shift_Wet=-6" },
        new[] { "context_settings_32000_256_8_reverb", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:16:Reverb_Wet=0", "live:32:Reverb_Wet=-80", "live:64:Reverb_Wet=-6" },
        new[] { "context_settings_32000_256_8_reverb_startup", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_32000_256_8_receive", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:16:Send_Level=-6", "live:32:Send_Level=-80", "live:64:Send_Level=0" },
        new[] { "context_settings_32000_256_8_duck", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_32000_256_8_compressor", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=link", "dominant=7", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_32000_256_8_source_chorus", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_32000_256_8_listener_reverb", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_32000_256_8_zone", "rate=32000", "buffer=256", "mode=Mode7point1", "preflight=4", "blocks=128", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_48000_512_2_dry", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_48000_512_2_dist", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:8:Dist_Wet=0", "live:16:Dist_Wet=-80", "live:32:Dist_Wet=-6" },
        new[] { "context_settings_48000_512_2_chorus", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:8:Chorus_Wet=0", "live:16:Chorus_Wet=-80", "live:32:Chorus_Wet=-6" },
        new[] { "context_settings_48000_512_2_flange", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:8:Flange_Wet=0", "live:16:Flange_Wet=-80", "live:32:Flange_Wet=-6" },
        new[] { "context_settings_48000_512_2_norm", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:8:Norm_Wet=0", "live:16:Norm_Wet=-80", "live:32:Norm_Wet=-6" },
        new[] { "context_settings_48000_512_2_shift", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:8:Shift_Wet=0", "live:16:Shift_Wet=-80", "live:32:Shift_Wet=-6" },
        new[] { "context_settings_48000_512_2_reverb", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:8:Reverb_Wet=0", "live:16:Reverb_Wet=-80", "live:32:Reverb_Wet=-6" },
        new[] { "context_settings_48000_512_2_reverb_startup", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_48000_512_2_receive", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:8:Send_Level=-6", "live:16:Send_Level=-80", "live:32:Send_Level=0" },
        new[] { "context_settings_48000_512_2_duck", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_48000_512_2_compressor", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_48000_512_2_source_chorus", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_48000_512_2_listener_reverb", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_48000_512_2_zone", "rate=48000", "buffer=512", "mode=Stereo", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_48000_512_4_dry", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_48000_512_4_dist", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:8:Dist_Wet=0", "live:16:Dist_Wet=-80", "live:32:Dist_Wet=-6" },
        new[] { "context_settings_48000_512_4_chorus", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:8:Chorus_Wet=0", "live:16:Chorus_Wet=-80", "live:32:Chorus_Wet=-6" },
        new[] { "context_settings_48000_512_4_flange", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:8:Flange_Wet=0", "live:16:Flange_Wet=-80", "live:32:Flange_Wet=-6" },
        new[] { "context_settings_48000_512_4_norm", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:8:Norm_Wet=0", "live:16:Norm_Wet=-80", "live:32:Norm_Wet=-6" },
        new[] { "context_settings_48000_512_4_shift", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:8:Shift_Wet=0", "live:16:Shift_Wet=-80", "live:32:Shift_Wet=-6" },
        new[] { "context_settings_48000_512_4_reverb", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:8:Reverb_Wet=0", "live:16:Reverb_Wet=-80", "live:32:Reverb_Wet=-6" },
        new[] { "context_settings_48000_512_4_reverb_startup", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_48000_512_4_receive", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:8:Send_Level=-6", "live:16:Send_Level=-80", "live:32:Send_Level=0" },
        new[] { "context_settings_48000_512_4_duck", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_48000_512_4_compressor", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=link", "dominant=3", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_48000_512_4_source_chorus", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_48000_512_4_listener_reverb", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_48000_512_4_zone", "rate=48000", "buffer=512", "mode=Quad", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_48000_512_5_dry", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_48000_512_5_dist", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:8:Dist_Wet=0", "live:16:Dist_Wet=-80", "live:32:Dist_Wet=-6" },
        new[] { "context_settings_48000_512_5_chorus", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:8:Chorus_Wet=0", "live:16:Chorus_Wet=-80", "live:32:Chorus_Wet=-6" },
        new[] { "context_settings_48000_512_5_flange", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:8:Flange_Wet=0", "live:16:Flange_Wet=-80", "live:32:Flange_Wet=-6" },
        new[] { "context_settings_48000_512_5_norm", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:8:Norm_Wet=0", "live:16:Norm_Wet=-80", "live:32:Norm_Wet=-6" },
        new[] { "context_settings_48000_512_5_shift", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:8:Shift_Wet=0", "live:16:Shift_Wet=-80", "live:32:Shift_Wet=-6" },
        new[] { "context_settings_48000_512_5_reverb", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:8:Reverb_Wet=0", "live:16:Reverb_Wet=-80", "live:32:Reverb_Wet=-6" },
        new[] { "context_settings_48000_512_5_reverb_startup", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_48000_512_5_receive", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:8:Send_Level=-6", "live:16:Send_Level=-80", "live:32:Send_Level=0" },
        new[] { "context_settings_48000_512_5_duck", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_48000_512_5_compressor", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=link", "dominant=4", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_48000_512_5_source_chorus", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_48000_512_5_listener_reverb", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_48000_512_5_zone", "rate=48000", "buffer=512", "mode=Surround", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_48000_512_6_dry", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_48000_512_6_dist", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:8:Dist_Wet=0", "live:16:Dist_Wet=-80", "live:32:Dist_Wet=-6" },
        new[] { "context_settings_48000_512_6_chorus", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:8:Chorus_Wet=0", "live:16:Chorus_Wet=-80", "live:32:Chorus_Wet=-6" },
        new[] { "context_settings_48000_512_6_flange", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:8:Flange_Wet=0", "live:16:Flange_Wet=-80", "live:32:Flange_Wet=-6" },
        new[] { "context_settings_48000_512_6_norm", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:8:Norm_Wet=0", "live:16:Norm_Wet=-80", "live:32:Norm_Wet=-6" },
        new[] { "context_settings_48000_512_6_shift", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:8:Shift_Wet=0", "live:16:Shift_Wet=-80", "live:32:Shift_Wet=-6" },
        new[] { "context_settings_48000_512_6_reverb", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:8:Reverb_Wet=0", "live:16:Reverb_Wet=-80", "live:32:Reverb_Wet=-6" },
        new[] { "context_settings_48000_512_6_reverb_startup", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_48000_512_6_receive", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:8:Send_Level=-6", "live:16:Send_Level=-80", "live:32:Send_Level=0" },
        new[] { "context_settings_48000_512_6_duck", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_48000_512_6_compressor", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=link", "dominant=5", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_48000_512_6_source_chorus", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_48000_512_6_listener_reverb", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_48000_512_6_zone", "rate=48000", "buffer=512", "mode=Mode5point1", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_48000_512_8_dry", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_48000_512_8_dist", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:8:Dist_Wet=0", "live:16:Dist_Wet=-80", "live:32:Dist_Wet=-6" },
        new[] { "context_settings_48000_512_8_chorus", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:8:Chorus_Wet=0", "live:16:Chorus_Wet=-80", "live:32:Chorus_Wet=-6" },
        new[] { "context_settings_48000_512_8_flange", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:8:Flange_Wet=0", "live:16:Flange_Wet=-80", "live:32:Flange_Wet=-6" },
        new[] { "context_settings_48000_512_8_norm", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:8:Norm_Wet=0", "live:16:Norm_Wet=-80", "live:32:Norm_Wet=-6" },
        new[] { "context_settings_48000_512_8_shift", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:8:Shift_Wet=0", "live:16:Shift_Wet=-80", "live:32:Shift_Wet=-6" },
        new[] { "context_settings_48000_512_8_reverb", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:8:Reverb_Wet=0", "live:16:Reverb_Wet=-80", "live:32:Reverb_Wet=-6" },
        new[] { "context_settings_48000_512_8_reverb_startup", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_48000_512_8_receive", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:8:Send_Level=-6", "live:16:Send_Level=-80", "live:32:Send_Level=0" },
        new[] { "context_settings_48000_512_8_duck", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_48000_512_8_compressor", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=link", "dominant=7", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_48000_512_8_source_chorus", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_48000_512_8_listener_reverb", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_48000_512_8_zone", "rate=48000", "buffer=512", "mode=Mode7point1", "preflight=4", "blocks=64", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_96000_2048_2_dry", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_96000_2048_2_dist", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:2:Dist_Wet=0", "live:4:Dist_Wet=-80", "live:8:Dist_Wet=-6" },
        new[] { "context_settings_96000_2048_2_chorus", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:2:Chorus_Wet=0", "live:4:Chorus_Wet=-80", "live:8:Chorus_Wet=-6" },
        new[] { "context_settings_96000_2048_2_flange", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:2:Flange_Wet=0", "live:4:Flange_Wet=-80", "live:8:Flange_Wet=-6" },
        new[] { "context_settings_96000_2048_2_norm", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:2:Norm_Wet=0", "live:4:Norm_Wet=-80", "live:8:Norm_Wet=-6" },
        new[] { "context_settings_96000_2048_2_shift", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:2:Shift_Wet=0", "live:4:Shift_Wet=-80", "live:8:Shift_Wet=-6" },
        new[] { "context_settings_96000_2048_2_reverb", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:2:Reverb_Wet=0", "live:4:Reverb_Wet=-80", "live:8:Reverb_Wet=-6" },
        new[] { "context_settings_96000_2048_2_reverb_startup", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_96000_2048_2_receive", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:2:Send_Level=-6", "live:4:Send_Level=-80", "live:8:Send_Level=0" },
        new[] { "context_settings_96000_2048_2_duck", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_96000_2048_2_compressor", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_96000_2048_2_source_chorus", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_96000_2048_2_listener_reverb", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_96000_2048_2_zone", "rate=96000", "buffer=2048", "mode=Stereo", "preflight=4", "blocks=24", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_settings_96000_2048_4_dry", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_settings_96000_2048_4_dist", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:2:Dist_Wet=0", "live:4:Dist_Wet=-80", "live:8:Dist_Wet=-6" },
        new[] { "context_settings_96000_2048_4_chorus", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:2:Chorus_Wet=0", "live:4:Chorus_Wet=-80", "live:8:Chorus_Wet=-6" },
        new[] { "context_settings_96000_2048_4_flange", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:2:Flange_Wet=0", "live:4:Flange_Wet=-80", "live:8:Flange_Wet=-6" },
        new[] { "context_settings_96000_2048_4_norm", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:2:Norm_Wet=0", "live:4:Norm_Wet=-80", "live:8:Norm_Wet=-6" },
        new[] { "context_settings_96000_2048_4_shift", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:2:Shift_Wet=0", "live:4:Shift_Wet=-80", "live:8:Shift_Wet=-6" },
        new[] { "context_settings_96000_2048_4_reverb", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:2:Reverb_Wet=0", "live:4:Reverb_Wet=-80", "live:8:Reverb_Wet=-6" },
        new[] { "context_settings_96000_2048_4_reverb_startup", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_settings_96000_2048_4_receive", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:2:Send_Level=-6", "live:4:Send_Level=-80", "live:8:Send_Level=0" },
        new[] { "context_settings_96000_2048_4_duck", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_settings_96000_2048_4_compressor", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=link", "dominant=3", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_settings_96000_2048_4_source_chorus", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_settings_96000_2048_4_listener_reverb", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_settings_96000_2048_4_zone", "rate=96000", "buffer=2048", "mode=Quad", "preflight=4", "blocks=24", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_prologic_2_dry", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_prologic_2_dist", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:4:Dist_Wet=0", "live:8:Dist_Wet=-80", "live:16:Dist_Wet=-6" },
        new[] { "context_prologic_2_chorus", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:4:Chorus_Wet=0", "live:8:Chorus_Wet=-80", "live:16:Chorus_Wet=-6" },
        new[] { "context_prologic_2_flange", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:4:Flange_Wet=0", "live:8:Flange_Wet=-80", "live:16:Flange_Wet=-6" },
        new[] { "context_prologic_2_norm", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:4:Norm_Wet=0", "live:8:Norm_Wet=-80", "live:16:Norm_Wet=-6" },
        new[] { "context_prologic_2_shift", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:4:Shift_Wet=0", "live:8:Shift_Wet=-80", "live:16:Shift_Wet=-6" },
        new[] { "context_prologic_2_reverb", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:4:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:16:Reverb_Wet=-6" },
        new[] { "context_prologic_2_reverb_startup", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_prologic_2_receive", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:4:Send_Level=-6", "live:8:Send_Level=-80", "live:16:Send_Level=0" },
        new[] { "context_prologic_2_duck", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_prologic_2_compressor", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_prologic_2_source_chorus", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_prologic_2_listener_reverb", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_prologic_2_zone", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_prologic_2_spatial_front", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=0:0:10" },
        new[] { "context_prologic_2_spatial_rear", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=0:0:-10" },
        new[] { "context_prologic_2_spatial_left", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=-10:0:0" },
        new[] { "context_prologic_2_spatial_right", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=10:0:0" },
        new[] { "context_prologic_2_spatial_rear_rotated", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=0:0:-10", "listener_yaw=90" },
        new[] { "context_prologic_2_stereo_clip", "mode=Stereo", "blocks=32", "preflight=4", "music=noise:0.2", "Music:Att" },
        new[] { "context_prologic_2_reverb_boost", "mode=Stereo", "blocks=32", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=30" },
        new[] { "context_prologic_7_dry", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att" },
        new[] { "context_prologic_7_dist", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:4:Dist_Wet=0", "live:8:Dist_Wet=-80", "live:16:Dist_Wet=-6" },
        new[] { "context_prologic_7_chorus", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Depth=0.7", "Music.Chorus.Rate=3", "Music.Chorus.Wet=-6", "live:4:Chorus_Wet=0", "live:8:Chorus_Wet=-80", "live:16:Chorus_Wet=-6" },
        new[] { "context_prologic_7_flange", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Depth=0.7", "Music.Flange.Rate=3", "Music.Flange.Wet=-6", "live:4:Flange_Wet=0", "live:8:Flange_Wet=-80", "live:16:Flange_Wet=-6" },
        new[] { "context_prologic_7_norm", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Norm", "Music.Norm.Fade in time=50", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:4:Norm_Wet=0", "live:8:Norm_Wet=-80", "live:16:Norm_Wet=-6" },
        new[] { "context_prologic_7_shift", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:4:Shift_Wet=0", "live:8:Shift_Wet=-80", "live:16:Shift_Wet=-6" },
        new[] { "context_prologic_7_reverb", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:4:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:16:Reverb_Wet=-6" },
        new[] { "context_prologic_7_reverb_startup", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011" },
        new[] { "context_prologic_7_receive", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=all", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:4:Send_Level=-6", "live:8:Send_Level=-80", "live:16:Send_Level=0" },
        new[] { "context_prologic_7_duck", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_prologic_7_compressor", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=link", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2" },
        new[] { "context_prologic_7_source_chorus", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_prologic_7_listener_reverb", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=burst", "Music:Att", "component=reverb:listener" },
        new[] { "context_prologic_7_zone", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone=1" },
        new[] { "context_prologic_7_spatial_front", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=0:0:10" },
        new[] { "context_prologic_7_spatial_rear", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=0:0:-10" },
        new[] { "context_prologic_7_spatial_left", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=-10:0:0" },
        new[] { "context_prologic_7_spatial_right", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=10:0:0" },
        new[] { "context_prologic_7_spatial_rear_rotated", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "mono=1", "Music:Att", "blend=1", "pos=0:0:-10", "listener_yaw=90" },
        new[] { "context_prologic_7_stereo_clip", "mode=Prologic", "blocks=32", "preflight=4", "music=noise:0.2", "Music:Att" },
        new[] { "context_prologic_7_reverb_boost", "mode=Prologic", "blocks=32", "preflight=4", "music=generator", "gen=all", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=30" },
        new[] { "context_zone_2_listener_dry", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_dry" },
        new[] { "context_zone_2_roomhf", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=roomhf", "zone=1", "zone_profile=user" },
        new[] { "context_zone_2_delay", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=delay", "zone=1", "zone_profile=user" },
        new[] { "context_zone_2_radius", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=radius", "zone=1", "zone_profile=user", "listener_start=12.5" },
        new[] { "context_zone_2_zone_move", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=zone_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_2_listener_move", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_2_source_mix", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=source_mix", "zone=1", "zone_profile=user" },
        new[] { "context_zone_2_preset_then_user", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_then_user", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_2_preset_control", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_control", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_2_overlap_move", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_move", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_2_overlap_scalar", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_scalar", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_2_overlap_enable", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_enable", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_2_listener_chorus", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener" },
        new[] { "context_zone_4_listener_dry", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_dry" },
        new[] { "context_zone_4_roomhf", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=roomhf", "zone=1", "zone_profile=user" },
        new[] { "context_zone_4_delay", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=delay", "zone=1", "zone_profile=user" },
        new[] { "context_zone_4_radius", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=radius", "zone=1", "zone_profile=user", "listener_start=12.5" },
        new[] { "context_zone_4_zone_move", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=zone_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_4_listener_move", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_4_source_mix", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=source_mix", "zone=1", "zone_profile=user" },
        new[] { "context_zone_4_preset_then_user", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_then_user", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_4_preset_control", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_control", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_4_overlap_move", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_move", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_4_overlap_scalar", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_scalar", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_4_overlap_enable", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_enable", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_4_listener_chorus", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener" },
        new[] { "context_zone_5_listener_dry", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_dry" },
        new[] { "context_zone_5_roomhf", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=roomhf", "zone=1", "zone_profile=user" },
        new[] { "context_zone_5_delay", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=delay", "zone=1", "zone_profile=user" },
        new[] { "context_zone_5_radius", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=radius", "zone=1", "zone_profile=user", "listener_start=12.5" },
        new[] { "context_zone_5_zone_move", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=zone_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_5_listener_move", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_5_source_mix", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=source_mix", "zone=1", "zone_profile=user" },
        new[] { "context_zone_5_preset_then_user", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_then_user", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_5_preset_control", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_control", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_5_overlap_move", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_move", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_5_overlap_scalar", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_scalar", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_5_overlap_enable", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_enable", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_5_listener_chorus", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener" },
        new[] { "context_zone_6_listener_dry", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_dry" },
        new[] { "context_zone_6_roomhf", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=roomhf", "zone=1", "zone_profile=user" },
        new[] { "context_zone_6_delay", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=delay", "zone=1", "zone_profile=user" },
        new[] { "context_zone_6_radius", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=radius", "zone=1", "zone_profile=user", "listener_start=12.5" },
        new[] { "context_zone_6_zone_move", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=zone_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_6_listener_move", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_6_source_mix", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=source_mix", "zone=1", "zone_profile=user" },
        new[] { "context_zone_6_preset_then_user", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_then_user", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_6_preset_control", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_control", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_6_overlap_move", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_move", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_6_overlap_scalar", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_scalar", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_6_overlap_enable", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_enable", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_6_listener_chorus", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener" },
        new[] { "context_zone_8_listener_dry", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_dry" },
        new[] { "context_zone_8_roomhf", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=roomhf", "zone=1", "zone_profile=user" },
        new[] { "context_zone_8_delay", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=delay", "zone=1", "zone_profile=user" },
        new[] { "context_zone_8_radius", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=radius", "zone=1", "zone_profile=user", "listener_start=12.5" },
        new[] { "context_zone_8_zone_move", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=zone_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_8_listener_move", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_move", "zone=1", "zone_profile=user" },
        new[] { "context_zone_8_source_mix", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=source_mix", "zone=1", "zone_profile=user" },
        new[] { "context_zone_8_preset_then_user", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_then_user", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_8_preset_control", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=preset_control", "zone=1", "zone_profile=generic" },
        new[] { "context_zone_8_overlap_move", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_move", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_8_overlap_scalar", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_scalar", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_8_overlap_enable", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=overlap_enable", "zone=1", "zone_profile=user", "overlap=1" },
        new[] { "context_zone_8_listener_chorus", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener" },
        new[] { "context_zone_2_listener_chorus_dry", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "component=chorus:listener" },
        new[] { "context_zone_2_listener_chorus_bypass", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener", "listener_bypass=1" },
        new[] { "context_zone_4_listener_chorus_dry", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "component=chorus:listener" },
        new[] { "context_zone_4_listener_chorus_bypass", "mode=Quad", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener", "listener_bypass=1" },
        new[] { "context_zone_5_listener_chorus_dry", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "component=chorus:listener" },
        new[] { "context_zone_5_listener_chorus_bypass", "mode=Surround", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener", "listener_bypass=1" },
        new[] { "context_zone_6_listener_chorus_dry", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "component=chorus:listener" },
        new[] { "context_zone_6_listener_chorus_bypass", "mode=Mode5point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener", "listener_bypass=1" },
        new[] { "context_zone_8_listener_chorus_dry", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "component=chorus:listener" },
        new[] { "context_zone_8_listener_chorus_bypass", "mode=Mode7point1", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=listener_chorus", "zone=1", "zone_profile=user", "component=chorus:listener", "listener_bypass=1" },
        new[] { "context_zone_2_radius_near", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=radius", "zone=1", "zone_profile=user", "listener_start=10.5" },
        new[] { "context_zone_2_radius_reference", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "blend=1", "pos=3:0:4", "zone_control=radius_reference", "zone=1", "zone_profile=user", "listener_start=12.5" },
        new[] { "context_monofx_1_dry_mono_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att" },
        new[] { "context_monofx_1_dry_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att" },
        new[] { "context_monofx_1_dist_half", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6" },
        new[] { "context_monofx_1_dist_full", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=0" },
        new[] { "context_monofx_1_dist_boost", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=6" },
        new[] { "context_monofx_1_dist_live", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-6", "live:5:Dist_Wet=0", "live:8:Dist_Wet=-80", "live:11:Dist_Wet=-70", "live:14:Dist_Wet=-6", "live:17:Dist_Wet=6", "live:20:Dist_Wet=-6" },
        new[] { "context_monofx_1_chorus_half", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6" },
        new[] { "context_monofx_1_chorus_full", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_monofx_1_chorus_boost", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=6" },
        new[] { "context_monofx_1_chorus_live", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-6", "live:5:Chorus_Wet=0", "live:8:Chorus_Wet=-80", "live:11:Chorus_Wet=-70", "live:14:Chorus_Wet=-6", "live:17:Chorus_Wet=6", "live:20:Chorus_Wet=-6" },
        new[] { "context_monofx_1_flange_half", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6" },
        new[] { "context_monofx_1_flange_full", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=0" },
        new[] { "context_monofx_1_flange_boost", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=6" },
        new[] { "context_monofx_1_flange_live", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Flange", "Music.Flange.Drymix=0.2", "Music.Flange.Wetmix=0.8", "Music.Flange.Rate=3", "Music.Flange.Depth=0.7", "Music.Flange.Wet=-6", "live:5:Flange_Wet=0", "live:8:Flange_Wet=-80", "live:11:Flange_Wet=-70", "live:14:Flange_Wet=-6", "live:17:Flange_Wet=6", "live:20:Flange_Wet=-6" },
        new[] { "context_monofx_1_norm_half", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6" },
        new[] { "context_monofx_1_norm_full", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=0" },
        new[] { "context_monofx_1_norm_boost", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=6" },
        new[] { "context_monofx_1_norm_live", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Norm", "Music.Norm.Fade in time=20", "Music.Norm.Lowest volume=0.01", "Music.Norm.Maximum amp=10", "Music.Norm.Wet=-6", "live:5:Norm_Wet=0", "live:8:Norm_Wet=-80", "live:11:Norm_Wet=-70", "live:14:Norm_Wet=-6", "live:17:Norm_Wet=6", "live:20:Norm_Wet=-6" },
        new[] { "context_monofx_1_shift_half", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6" },
        new[] { "context_monofx_1_shift_full", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=0" },
        new[] { "context_monofx_1_shift_boost", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=6" },
        new[] { "context_monofx_1_shift_live", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Shift", "Music.Shift.Pitch=0.75", "Music.Shift.FFT size=1024", "Music.Shift.Overlap=4", "Music.Shift.Max channels=0", "Music.Shift.Wet=-6", "live:5:Shift_Wet=0", "live:8:Shift_Wet=-80", "live:11:Shift_Wet=-70", "live:14:Shift_Wet=-6", "live:17:Shift_Wet=6", "live:20:Shift_Wet=-6" },
        new[] { "context_monofx_1_reverb_half", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6" },
        new[] { "context_monofx_1_reverb_full", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_monofx_1_reverb_boost", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=6" },
        new[] { "context_monofx_1_reverb_live", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=-6", "live:5:Reverb_Wet=0", "live:8:Reverb_Wet=-80", "live:11:Reverb_Wet=-70", "live:14:Reverb_Wet=-6", "live:17:Reverb_Wet=6", "live:20:Reverb_Wet=-6" },
        new[] { "context_monofx_1_dist_edge_m64", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-64" },
        new[] { "context_monofx_1_dist_edge_m63", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Dist", "Music.Dist.Level=0.7", "Music.Dist.Wet=-63" },
        new[] { "context_monofx_1_chorus_edge_m64", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-64" },
        new[] { "context_monofx_1_chorus_edge_m63", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=-63" },
        new[] { "context_monofx_1_chorus_tap1", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0", "Music.Chorus.Dry mix=0", "Music.Chorus.Wet mix tap 1=0.7", "Music.Chorus.Wet mix tap 2=0", "Music.Chorus.Wet mix tap 3=0" },
        new[] { "context_monofx_1_chorus_tap2", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0", "Music.Chorus.Dry mix=0", "Music.Chorus.Wet mix tap 1=0", "Music.Chorus.Wet mix tap 2=0.7", "Music.Chorus.Wet mix tap 3=0" },
        new[] { "context_monofx_1_chorus_tap3", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0", "Music.Chorus.Dry mix=0", "Music.Chorus.Wet mix tap 1=0", "Music.Chorus.Wet mix tap 2=0", "Music.Chorus.Wet mix tap 3=0.7" },
        new[] { "context_monofx_1_chorus_then_dist", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus,Dist", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0", "Music.Dist.Level=0.8" },
        new[] { "context_monofx_1_group_chorus_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_monofx_1_source_chorus_mono_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_monofx_1_source_chorus_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att", "component=chorus:source" },
        new[] { "context_monofx_1_listener_chorus_mono_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "component=chorus:listener" },
        new[] { "context_monofx_1_listener_chorus_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att", "component=chorus:listener" },
        new[] { "context_monofx_1_group_reverb_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_monofx_1_source_reverb_mono_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "component=reverb:source" },
        new[] { "context_monofx_1_source_reverb_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att", "component=reverb:source" },
        new[] { "context_monofx_1_listener_reverb_mono_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "component=reverb:listener" },
        new[] { "context_monofx_1_listener_reverb_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att", "component=reverb:listener" },
        new[] { "context_monofx_1_zone_mono_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "zone=1", "zone_profile=user" },
        new[] { "context_monofx_1_zone_stereo_clip", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "Music:Att", "zone=1", "zone_profile=user" },
        new[] { "context_monofx_1_receive_full", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0" },
        new[] { "context_monofx_1_receive_live", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0", "live:14:Send_Level=-12" },
        new[] { "context_monofx_1_receive_post", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Send", "Music.Volume=-6", "Master:Receive,Att", "Master.Volume=-6", "Music.Send.Level=-6" },
        new[] { "context_monofx_1_receive_after_chorus", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Chorus,Send,Att", "Music.Volume=-80", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0", "live:14:Send_Level=-12", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7" },
        new[] { "context_monofx_1_compressor", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "dominant=1", "Music:Att,Comp", "Music.Comp.Threshold=-25", "Music.Comp.Attack=10", "Music.Comp.Release=100", "Music.Comp.Make up gain=2", "live:4:Comp_Threshold=-6", "live:8:Comp_Threshold=-30" },
        new[] { "context_monofx_1_duck", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Send", "Master:Att,Duck", "Music.Send.Level=0", "Master.Duck.Threshold=-30", "Master.Duck.Ratio=4", "Master.Duck.Attack Time=0.01", "Master.Duck.Release Time=0.03" },
        new[] { "context_monofx_1_reverb_max_quiet", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Volume=-40", "Music.Reverb.Wet=30" },
        new[] { "context_monofx_1_source_chorus_generator", "mode=Mono", "preflight=4", "blocks=32", "Music:Att", "component=chorus:source", "music=generator", "gen=all" },
        new[] { "context_monofx_2_group_chorus_mono_clip", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_monofx_2_group_reverb_mono_clip", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
        new[] { "context_monofx_2_source_chorus_mono_clip", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "component=chorus:source" },
        new[] { "context_monofx_2_listener_chorus_mono_clip", "mode=Stereo", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att", "component=chorus:listener" },
        new[] { "context_monofx_1_reverb_then_chorus", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb,Chorus", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_monofx_1_chorus_then_reverb", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0" },
        new[] { "context_monofx_1_reverb_parent_chorus", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0", "Master:Att,Chorus", "Master.Chorus.Dry mix=0.2", "Master.Chorus.Wet mix tap 1=0.2", "Master.Chorus.Wet mix tap 2=0.5", "Master.Chorus.Wet mix tap 3=0.9", "Master.Chorus.Delay=11", "Master.Chorus.Rate=3", "Master.Chorus.Depth=0.7", "Master.Chorus.Wet=0" },
        new[] { "context_monofx_1_source_reverb_group_chorus", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Chorus", "Music.Chorus.Dry mix=0.2", "Music.Chorus.Wet mix tap 1=0.2", "Music.Chorus.Wet mix tap 2=0.5", "Music.Chorus.Wet mix tap 3=0.9", "Music.Chorus.Delay=11", "Music.Chorus.Rate=3", "Music.Chorus.Depth=0.7", "Music.Chorus.Wet=0", "component=reverb:source" },
        new[] { "context_monofx_1_group_reverb_listener_chorus", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0", "component=chorus:listener" },
        new[] { "context_monofx_1_receive_after_reverb", "mode=Mono", "preflight=4", "blocks=32", "music=noise_full:0.2", "mono=1", "Music:Att,Reverb,Send", "Master:Att,Receive", "Music.Send.Level=0", "live:5:Send_Level=-6", "live:8:Send_Level=-80", "live:11:Send_Level=0", "live:14:Send_Level=-12", "Music.Reverb.Dry Level=-600", "Music.Reverb.Room=-500", "Music.Reverb.Room HF=-300", "Music.Reverb.Decay Time=0.3", "Music.Reverb.Reflections=-500", "Music.Reverb.Reflect Delay=0.007", "Music.Reverb.Reverb=-500", "Music.Reverb.Reverb Delay=0.011", "Music.Reverb.Wet=0" },
    };
    static readonly Dictionary<string, string[]> CaseTokens = new Dictionary<string, string[]>();
    static EffectProbe()
    {
        var rows = new List<string[]>(Cases);
        string wide = Path.Combine(Application.streamingAssetsPath, "WideCases.tsv");
        if (File.Exists(wide)) foreach (string line in File.ReadAllLines(wide))
            if (!string.IsNullOrEmpty(line)) rows.Add(line.Split('\t'));
        Cases = rows.ToArray();
        foreach (var row in Cases) CaseTokens.Add(row[0], row);
    }
    static int BlockCount, ClipFrames;
    public static string[] Selected()
    {
        var only = Environment.GetEnvironmentVariable("EFFECT_PROBE_CASES");
        var selected = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
        var keys = new List<string>();
        foreach (var c in Cases) if (selected == null || selected.Contains(c[0])) keys.Add(c[0]);
        return keys.ToArray();
    }
    public static string[] Tokens(string key) => CaseTokens[key];
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
                case "noise_full":
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
            if (f >= 16384 && parts[0] != "noise_full") l = r = 0;
            data[2 * f] = l; data[2 * f + 1] = r;
        }
        return data;
    }
    static void ConfigureZone(AudioReverbZone zone)
    {
        zone.reverbPreset = AudioReverbPreset.User;
        zone.room = -500; zone.roomHF = -300; zone.roomLF = -200;
        zone.decayTime = 0.3f; zone.decayHFRatio = 0.7f;
        zone.reflections = -500; zone.reflectionsDelay = 0.007f;
        zone.reverb = -500; zone.reverbDelay = 0.011f;
        zone.HFReference = 5000; zone.LFReference = 250;
        zone.diffusion = 100; zone.density = 100;
    }

    static bool ChangeZone(string kind, int block, AudioReverbZone zone,
        AudioReverbZone other, AudioSource source, Transform listener)
    {
        int phase = block == 4 ? 0 : block == 8 ? 1 : block == 16 ? 2 : block == 20 ? 3 : block == 24 ? 4 : -1;
        if (phase < 0) return false;
        if (kind == "overlap_move") {
            if (phase == 0) other.transform.position = new Vector3(12.5f, 0, 0);
            if (phase == 1) zone.transform.position = new Vector3(12.5f, 0, 0);
            if (phase == 2) other.transform.position = new Vector3(20, 0, 0);
            if (phase == 3) zone.transform.position = Vector3.zero;
            if (phase == 4) other.transform.position = Vector3.zero;
            return true;
        }
        if (phase >= 3) return false;
        if (kind == "listener_move" || kind == "listener_dry") {
            listener.position = new Vector3(phase == 0 ? 12.5f : phase == 1 ? 20 : 0, 0, 0);
            return true;
        }
        if (zone == null) return false;
        switch (kind) {
            case "roomhf": case "listener_chorus": zone.roomHF = phase == 0 ? -1800 : phase == 1 ? 0 : -300; break;
            case "delay": zone.reflectionsDelay = phase == 0 ? 0.03f : phase == 1 ? 0.001f : 0.007f; break;
            case "radius_reference": zone.LFReference = phase == 0 ? 1000 : phase == 1 ? 250 : 500; break;
            case "radius":
                zone.minDistance = 0; zone.maxDistance = phase == 0 ? 5 : 25;
                if (phase == 2) zone.minDistance = 20;
                break;
            case "zone_move": zone.transform.position = new Vector3(phase == 0 ? 20 : phase == 1 ? 12.5f : 0, 0, 0); break;
            case "source_mix": source.reverbZoneMix = phase == 0 ? 0 : phase == 1 ? 0.5f : 1.1f; break;
            case "preset_then_user": case "preset_control":
                if (phase == 0) { if (kind == "preset_control") return false; zone.roomHF = -1800; }
                if (phase == 1) zone.reverbPreset = AudioReverbPreset.User;
                if (phase == 2) zone.reverbPreset = AudioReverbPreset.Generic;
                break;
            case "overlap_scalar":
                if (phase == 0) zone.roomHF = -1800;
                if (phase == 1) { other.reverbPreset = AudioReverbPreset.User; other.room = -1600; other.roomHF = -1200; }
                if (phase == 2) { zone.decayTime = 0.8f; other.reflectionsDelay = 0.03f; }
                break;
            case "overlap_enable":
                if (phase < 2) other.enabled = phase == 1;
                else other.reverbPreset = AudioReverbPreset.Generic;
                break;
            default: throw new Exception("Unknown zone control " + kind);
        }
        return true;
    }

    static void RecordZoneState(List<string> records, string key, int block,
        AudioReverbZone zone, AudioReverbZone other, AudioSource source, Transform listener)
    {
        int Bits(float value) => BitConverter.SingleToInt32Bits(value);
        if (key.EndsWith("_radius_reference"))
            records.Add("\""+key+".zone_refs_"+block+"\":{\"hf_bits\":"+Bits(zone.HFReference)+",\"lf_bits\":"+Bits(zone.LFReference)+"}");
        records.Add("\""+key+".zone_update_"+block+"\":{\"listener_x_bits\":"+Bits(listener.position.x)
            +",\"zone_x_bits\":"+Bits(zone != null ? zone.transform.position.x : 0)
            +",\"min_bits\":"+Bits(zone != null ? zone.minDistance : 0)+",\"max_bits\":"+Bits(zone != null ? zone.maxDistance : 0)
            +",\"preset\":"+(zone != null ? (int)zone.reverbPreset : -1)+",\"room_hf\":"+(zone != null ? zone.roomHF : 0)
            +",\"delay_bits\":"+Bits(zone != null ? zone.reflectionsDelay : 0)+",\"mix_bits\":"+Bits(source.reverbZoneMix)
            +",\"other_x_bits\":"+Bits(other != null ? other.transform.position.x : 0)
            +",\"other_enabled\":"+(other != null && other.enabled ? "true" : "false")+"}");
    }

    System.Collections.IEnumerator Capture()
    {
        if (m_Captured) yield break;
        m_Captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("EFFECT_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = int.Parse(Environment.GetEnvironmentVariable("EFFECT_PROBE_BUFFER") ?? "1024");
        if (!AudioSettings.Reset(config)) throw new Exception("AudioSettings.Reset failed");
        if (!Engine) yield return null;
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("EFFECT_PROBE_OUT"); Directory.CreateDirectory(output);
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        bool resetCases = Environment.GetEnvironmentVariable("EFFECT_PROBE_RESET_CASES") == "1";
        if (resetCases) records.Add("\"reset_cases\":true");
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Save();
        foreach (string key in Selected()) {
            if (key.StartsWith("device_")) {
                // Each requested layout on this session's device: the getter, the driver's caps
                // and the channel count the listener callback actually receives.
                foreach (AudioSpeakerMode mode in new[] { AudioSpeakerMode.Mono, AudioSpeakerMode.Stereo, AudioSpeakerMode.Quad,
                    AudioSpeakerMode.Surround, AudioSpeakerMode.Mode5point1, AudioSpeakerMode.Mode7point1, AudioSpeakerMode.Prologic }) {
                    AudioRenderer.Stop();
                    config.speakerMode = mode;
                    if (!AudioSettings.Reset(config)) throw new Exception("device reset failed");
                    if (!Engine) yield return null;
                    if (!AudioRenderer.Start()) throw new Exception("device restart failed");
                    OutputLayoutProbe.Channels = 0;
                    var probe = gameObject.AddComponent<OutputLayoutProbe>();
                    var warmup = new NativeArray<float>(config.dspBufferSize * 8, Allocator.Persistent);
                    for (int attempt = 0; attempt < 16 && OutputLayoutProbe.Channels == 0; attempt++) {
                        if (!Engine) yield return null; else FlushEngine();
                        if (!AudioRenderer.Render(warmup)) throw new Exception("device preflight render failed");
                    }
                    warmup.Dispose(); DestroyImmediate(probe);
                    string prefix = "\""+key+"."+mode+".";
                    records.Add(prefix+"getter\":"+(int)AudioSettings.GetConfiguration().speakerMode);
                    records.Add(prefix+"caps\":"+(int)AudioSettings.driverCapabilities);
                    records.Add(prefix+"channels\":"+OutputLayoutProbe.Channels);
                }
                Save();
                continue;
            }
            if (key.StartsWith("context_zone_")) {
                gameObject.transform.position = new Vector3(float.Parse(Option(key, "listener_start=", "0"), System.Globalization.CultureInfo.InvariantCulture), 0, 0);
                gameObject.transform.rotation = Quaternion.identity;
            }
            int requestedBuffer = int.Parse(Option(key, "buffer=", "1024"));
            int requestedRate = int.Parse(Option(key, "rate=", rate.ToString()));
            var requestedMode = (AudioSpeakerMode)Enum.Parse(typeof(AudioSpeakerMode), Option(key, "mode=", "Stereo"));
            if (resetCases || config.sampleRate != requestedRate || config.dspBufferSize != requestedBuffer || config.speakerMode != requestedMode) {
                AudioRenderer.Stop();
                config.sampleRate = requestedRate; config.dspBufferSize = requestedBuffer; config.speakerMode = requestedMode;
                if (!AudioSettings.Reset(config)) throw new Exception("context reset failed");
                if (!Engine) yield return null;
                if (!AudioRenderer.Start()) throw new Exception("context restart failed");
            }
            var actual = AudioSettings.GetConfiguration();
            AudioSettings.GetDSPBufferSize(out int actualBuffer, out int buffers);
            int outputChannels = actual.speakerMode == AudioSpeakerMode.Mono ? 1
                : actual.speakerMode == AudioSpeakerMode.Quad ? 4
                : actual.speakerMode == AudioSpeakerMode.Surround ? 5
                : actual.speakerMode == AudioSpeakerMode.Mode5point1 ? 6
                : actual.speakerMode == AudioSpeakerMode.Mode7point1 ? 8 : 2;
            Time.captureDeltaTime = (float)actualBuffer / actual.sampleRate;
            OutputLayoutProbe layoutProbe = null;
            bool liveContext = key.StartsWith("context_live_") || Option(key, "preflight=", "") != "";
            if ((!Engine && key.StartsWith("context_")) || liveContext) {
                OutputLayoutProbe.Channels = 0;
                if (!Engine) layoutProbe = gameObject.AddComponent<OutputLayoutProbe>();
                // A configuration getter may retain the request when the output driver falls back.
                // Measure the active DSP layout before choosing the capture buffer dimensions.
                // The listener callback can start a few frames after a reset; this precedes the
                // case's mixer and source, so the number of warm buffers does not reach the capture.
                int warmBlocks = liveContext ? int.Parse(Option(key, "preflight=", "4")) : 16;
                var warm = new NativeArray<float>(actualBuffer * (liveContext ? outputChannels : 8), Allocator.Persistent);
                for (int attempt = 0; attempt < warmBlocks && (liveContext || OutputLayoutProbe.Channels == 0); attempt++) {
                    if (!Engine) yield return null; else FlushEngine();
                    if (!AudioRenderer.Render(warm)) throw new Exception("layout preflight render failed");
                }
                warm.Dispose();
                if (!Engine && OutputLayoutProbe.Channels == 0) throw new Exception("missing layout callback");
                if (!Engine) outputChannels = OutputLayoutProbe.Channels;
                if (liveContext) records.Add("\""+key+".preflight_blocks\":"+warmBlocks);
            }
            if (key.StartsWith("context_") || key.StartsWith("buffer_")) {
                records.Add("\""+key+".buffer\":"+actualBuffer);
                records.Add("\""+key+".mode\":"+(int)actual.speakerMode);
                records.Add("\""+key+".rate\":"+actual.sampleRate);
                // The engine's configured layout is its active layout; the Editor's is measured above.
                if (key.StartsWith("context_")) records.Add("\""+key+".render_channels\":"+outputChannels);
            }
            BlockCount = int.Parse(Option(key, "blocks=", "32")); ClipFrames = int.Parse(Option(key, "clip=", (BlockCount * actualBuffer).ToString()));
            Debug.Log("Effect case " + key);
            if (Option(key, "reset=", "0") == "1") {
                AudioRenderer.Stop();
                if (!AudioSettings.Reset(config)) throw new Exception("case reset failed");
                if (!Engine) yield return null;
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
            if (key.StartsWith("slot_wet_") && key != "slot_wet_dry" && key != "slot_wet_processed" && key != "slot_wet_dc_dry" && key != "slot_wet_dc_processed") {
                string set = Option(key, "wetset=", "");
                if (set != "") records.Add("\""+key+".wetset\":"+(mixer.SetFloat("Wet", float.Parse(set, System.Globalization.CultureInfo.InvariantCulture)) ? "true" : "false"));
                bool got = mixer.GetFloat("Wet", out float value);
                records.Add("\""+key+".wetget\":"+(got ? "true" : "false"));
                records.Add("\""+key+".wetvalue\":"+value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            var go = new GameObject(key);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var position = Option(key, "pos=", "0:0:0").Split(':');
            go.transform.position = new Vector3(float.Parse(position[0], inv), float.Parse(position[1], inv), float.Parse(position[2], inv));
            if (key.StartsWith("context_prologic_"))
                gameObject.transform.rotation = Quaternion.Euler(0, float.Parse(Option(key, "listener_yaw=", "0"), inv), 0);
            var source = go.AddComponent<AudioSource>();
            source.bypassEffects = Option(key, "bypass=", "0") == "1";
            source.bypassListenerEffects = Option(key, "listener_bypass=", "0") == "1";
            source.outputAudioMixerGroup = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Music");
            if (!source.outputAudioMixerGroup) throw new Exception("Missing group Music");
            source.spatialBlend = float.Parse(Option(key, "blend=", "0"), inv); source.volume = 1;
            source.spread = float.Parse(Option(key, "spread=", "0"), inv);
            source.panStereo = float.Parse(Option(key, "pan=", "0"), inv);
            if (key.StartsWith("context_zone_")) source.dopplerLevel = 0;
            var listenerWrite = Option(key, "listener=", "");
            OutputLayoutProbe.WriteChannel = -1;
            if (listenerWrite != "") {
                // A listener filter writes a constant into one output channel.
                OutputLayoutProbe.WriteValue = float.Parse(listenerWrite.Split(':')[0], inv);
                OutputLayoutProbe.WriteChannel = int.Parse(listenerWrite.Split(':')[1], inv);
                if (layoutProbe == null) layoutProbe = gameObject.AddComponent<OutputLayoutProbe>();
            }
            AudioClip clip = null;
            if (Option(key, "music=", "") == "generator") {
                ProbeGenerator.Channels = 0;
                ProbeGenerator.Signal = Option(key, "gen=", "");
                ProbeGenerator.Dominant = int.Parse(Option(key, "dominant=", "0"));
                ProbeGenerator.Frame = 0;
                ProbeGenerator.Seed = 12345;
                ProbeGenerator.AllChannels = Option(key, "gen=", "") == "all";
                go.AddComponent<ProbeGenerator>();
            } else {
                int channels = Option(key, "mono=", "0") == "1" ? 1 : 2;
                int clipRate = key.StartsWith("context_settings_") || key.StartsWith("context_wide_") ? actual.sampleRate : rate;
                clip = AudioClip.Create(key, ClipFrames, channels, clipRate, false);
                var data = Signal(Option(key, "music=", "dc:0.5"), clipRate);
                if (channels == 1) { var mono = new float[ClipFrames]; for (int f = 0; f < ClipFrames; f++) mono[f] = data[2*f]; data = mono; }
                if (!clip.SetData(data, 0)) throw new Exception("SetData failed");
                source.clip = clip;
                var script = Option(key, "script=", "");
                if (script != "") {
                    // A clip source's script adds a constant to one channel.
                    ProbeScript.WriteValue = float.Parse(script.Split(':')[0], inv);
                    ProbeScript.WriteChannel = int.Parse(script.Split(':')[1], inv);
                    go.AddComponent<ProbeScript>();
                }
            }
            GameObject zoneObject = null;
            AudioReverbZone liveZone = null, overlapZone = null;
            GameObject overlapObject = null;
            if (Option(key, "zone=", "0") == "1") {
                zoneObject = new GameObject("ProbeZone");
                var zone = zoneObject.AddComponent<AudioReverbZone>();
                liveZone = zone;
                zone.reverbPreset = AudioReverbPreset.Generic;
                zone.minDistance = 10; zone.maxDistance = 15;
                if (Option(key, "zone_profile=", "") == "user") ConfigureZone(zone);
                if (Option(key, "overlap=", "0") == "1") {
                    overlapObject = new GameObject("OverlapZone"); overlapZone = overlapObject.AddComponent<AudioReverbZone>();
                    overlapZone.reverbPreset = AudioReverbPreset.Cave;
                    overlapZone.minDistance = 10; overlapZone.maxDistance = 15;
                }
            }
            Component component = null;
            var filter = Option(key, "component=", "");
            if (filter != "") {
                var target = filter.EndsWith(":listener") ? gameObject : go;
                if (filter.StartsWith("chorus:")) {
                    var chorus = target.AddComponent<AudioChorusFilter>();
                    chorus.dryMix = 0.2f; chorus.wetMix1 = 0.2f; chorus.wetMix2 = 0.5f;
                    chorus.wetMix3 = 0.9f; chorus.delay = 11; chorus.rate = 3; chorus.depth = 0.7f;
                    component = chorus;
                } else {
                    var reverb = target.AddComponent<AudioReverbFilter>();
                    reverb.reverbPreset = AudioReverbPreset.User;
                    reverb.dryLevel = -600; reverb.room = -500; reverb.roomHF = -300;
                    reverb.decayTime = 0.3f; reverb.reflectionsLevel = -500;
                    reverb.reflectionsDelay = 0.007f; reverb.reverbLevel = -500; reverb.reverbDelay = 0.011f;
                    component = reverb;
                }
            }
            source.Play();
            if (Option(key, "reset_to=", "") != "") {
                var resetting = CaptureLayoutReset(key, source, clip, mixer, config, output, records, sinks);
                while (resetting.MoveNext()) yield return resetting.Current;
                config = AudioSettings.GetConfiguration();
                outputChannels = Channels(config.speakerMode);
            } else {
            var sink = new NativeArray<float>(actualBuffer * outputChannels, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(actualBuffer * outputChannels, Allocator.Persistent);
            var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                if (Engine) FlushEngine();
                for (int block = 0; block < BlockCount; block++) {
                    foreach (var token in Tokens(key)) if (token.StartsWith("live:"+block+":")) {
                        var pair = token.Substring(token.IndexOf(':', 5)+1).Split('=');
                        bool ok = mixer.SetFloat(pair[0], float.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                        records.Add("\""+key+".live_set\":"+(ok ? "true" : "false"));
                        if (Engine) FlushEngine();
                    }
                    if (component && Option(key, "component_live=", "0") == "1") {
                        if (block == 8 || block == 12) ((Behaviour)component).enabled = block == 12;
                        if (block == 14) {
                            if (component is AudioChorusFilter chorus) { chorus.delay = 23; chorus.depth = 0.3f; }
                            if (component is AudioReverbFilter reverb) { reverb.roomHF = -1800; reverb.dryLevel = -1800; }
                        }
                        if ((block == 8 || block == 12 || block == 14) && Engine) FlushEngine();
                    }
                    string zoneChange = Option(key, "zone_live=", "");
                    if (liveZone && (block == 8 || block == 16)) {
                        if (zoneChange == "enable") liveZone.enabled = block == 16;
                        if (zoneChange == "preset") liveZone.reverbPreset = block == 8 ? AudioReverbPreset.Cave : AudioReverbPreset.Generic;
                        if (zoneChange != "" && Engine) FlushEngine();
                    }
                    if (key.StartsWith("context_zone_") && ChangeZone(Option(key, "zone_control=", ""), block,
                        liveZone, overlapZone, source, gameObject.transform)) {
                        RecordZoneState(records, key, block, liveZone, overlapZone, source, gameObject.transform);
                        if (Engine) FlushEngine();
                    }
                    if (!Engine) yield return null;
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("AddMixerGroupSink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("AudioRenderer.Render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                    if (Engine) FlushEngine();
                }
            }
            if (Option(key, "music=", "") == "generator") records.Add("\""+key+".generator_channels\":"+ProbeGenerator.Channels);
            if (listenerWrite != "") records.Add("\""+key+".listener_channels\":"+OutputLayoutProbe.Channels);
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(BlockCount*actualBuffer)+",\"channels\":"+outputChannels+"}");
            Save(); buffer.Dispose();
            }
            OutputLayoutProbe.WriteChannel = -1;
            if (layoutProbe != null) DestroyImmediate(layoutProbe);
            source.Stop(); if (component) DestroyImmediate(component); if (zoneObject) DestroyImmediate(zoneObject); if (overlapObject) DestroyImmediate(overlapObject); DestroyImmediate(go); if (clip) DestroyImmediate(clip);
            var silence = new NativeArray<float>(actualBuffer * outputChannels, Allocator.Persistent);
            bool resetTail = Option(key, "reset_to=", "") != "";
            var drainSink = resetTail ? new NativeArray<float>(silence.Length, Allocator.Persistent) : default;
            if (resetTail) sinks.Add(drainSink);
            using (var drainWriter = resetTail ? new BinaryWriter(File.Create(Path.Combine(output, key + ".drain.f32"))) : null)
            using (var drainMaster = resetTail ? new BinaryWriter(File.Create(Path.Combine(output, key + ".drain.master.f32"))) : null) {
                for (int drain = 0; drain < 8; drain++) {
                    if (!Engine) yield return null; else FlushEngine();
                    if (resetTail) {
                        var drainGroup = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
                        if (!(bool)typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags).Invoke(null, new object[] { drainGroup, drainSink, false }))
                            throw new Exception("drain sink failed");
                    }
                    if (!AudioRenderer.Render(silence)) throw new Exception("drain render failed");
                    if (resetTail) for (int i = 0; i < silence.Length; i++) { drainWriter.Write(silence[i]); drainMaster.Write(drainSink[i]); }
                }
            }
            if (resetTail) foreach (string suffix in new[] { "", ".master" })
                records.Add("\"" + key + ".drain" + suffix + "\":{\"file\":\"" + key + ".drain" + suffix + ".f32\",\"frames\":"
                    + (8 * actualBuffer) + ",\"channels\":" + outputChannels + "}");
            silence.Dispose();
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}

public class OutputLayoutProbe : MonoBehaviour
{
    public static volatile int Channels;
    public static volatile int WriteChannel = -1;
    public static float WriteValue;
    void OnAudioFilterRead(float[] data, int channels)
    {
        Channels = channels;
        if (WriteChannel >= 0 && WriteChannel < channels)
            for (int i = WriteChannel; i < data.Length; i += channels) data[i] += WriteValue;
    }
}

/// <summary>A clipless source's generator: constants on the first two channels, or a distinct
/// constant on every channel.</summary>
public class ProbeGenerator : MonoBehaviour
{
    public static volatile int Channels;
    public static volatile bool AllChannels;
    public static string Signal;
    public static int Dominant, Frame;
    public static uint Seed;
    void OnAudioFilterRead(float[] data, int channels)
    {
        Channels = channels;
        for (int i = 0; i < data.Length; i += channels)
        {
            if (Signal == "imp" || Signal == "burst" || Signal == "burst_full" || Signal == "link") {
                for (int c = 0; c < channels; c++) {
                    float value = 0;
                    if (Signal == "imp" && Frame == 2048 + c * 37) value = (c % 2 == 0 ? 0.25f : -0.1875f);
                    if ((Signal == "burst" && Frame >= 2048 && Frame < 16384) || Signal == "burst_full") {
                        Seed = unchecked(Seed * 1664525 + 1013904223);
                        value = ((int)(Seed >> 18) - 8192) / 131072f;
                    }
                    if (Signal == "link") value = c == Dominant && Frame >= 2048 && Frame < 12288 ? 0.75f : 0.015625f;
                    data[i+c] += value;
                }
                Frame++; continue;
            }
            if (Signal == "lateall") {
                if (Frame >= 2048) for (int c = 0; c < channels; c++) data[i+c] += 0.0625f * (c+1);
                Frame++; continue;
            }
            if (AllChannels) { for (int c = 0; c < channels; c++) data[i + c] += 0.0625f * (c + 1); continue; }
            data[i] += 0.25f; if (channels > 1) data[i + 1] -= 0.125f;
        }
    }
}

/// <summary>A clip source's script: adds a constant to one channel.</summary>
public class ProbeScript : MonoBehaviour
{
    public static volatile int WriteChannel;
    public static float WriteValue;
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (WriteChannel < channels)
            for (int i = WriteChannel; i < data.Length; i += channels) data[i] += WriteValue;
    }
}
