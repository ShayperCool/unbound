using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public partial class AudioReverbProbe : MonoBehaviour
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
        new[] { "clip_end_11264", "filter", "F:reverbPreset=Generic", "frames=11264" },
        new[] { "clip_end_11265", "filter", "F:reverbPreset=Generic", "frames=11265" },
        new[] { "clip_end_11519", "filter", "F:reverbPreset=Generic", "frames=11519" },
        new[] { "clip_end_11521", "filter", "F:reverbPreset=Generic", "frames=11521" },
        new[] { "clip_end_12287", "filter", "F:reverbPreset=Generic", "frames=12287" },
        new[] { "clip_end_12289", "filter", "F:reverbPreset=Generic", "frames=12289" },
        new[] { "clip_end_12500", "filter", "F:reverbPreset=Generic", "frames=12500" },
        new[] { "clip_end_12801", "filter", "F:reverbPreset=Generic", "frames=12801" },
        new[] { "zone_curve_s0_r0_d0", "zone=0", "position=0", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s0_r0_d5", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s0_r0_d10", "zone=0", "position=10", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s0_r0_d15", "zone=0", "position=15", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s0_r0_d20", "zone=0", "position=20", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s0_r0_d25", "zone=0", "position=25", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_send_s0_r0_m0", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0" },
        new[] { "zone_send_s0_r0_m0p5", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0.5" },
        new[] { "zone_send_s0_r0_m1", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1" },
        new[] { "zone_send_s0_r0_m1p1", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1.1" },
        new[] { "zone_curve_s0_r1_d0", "zone=0", "position=0", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s0_r1_d5", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s0_r1_d10", "zone=0", "position=10", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s0_r1_d15", "zone=0", "position=15", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s0_r1_d20", "zone=0", "position=20", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s0_r1_d25", "zone=0", "position=25", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_send_s0_r1_m0", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0", "routed" },
        new[] { "zone_send_s0_r1_m0p5", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0.5", "routed" },
        new[] { "zone_send_s0_r1_m1", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1", "routed" },
        new[] { "zone_send_s0_r1_m1p1", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1.1", "routed" },
        new[] { "zone_curve_s1_r0_d0", "zone=0", "position=0", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s1_r0_d5", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s1_r0_d10", "zone=0", "position=10", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s1_r0_d15", "zone=0", "position=15", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s1_r0_d20", "zone=0", "position=20", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_curve_s1_r0_d25", "zone=0", "position=25", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1" },
        new[] { "zone_send_s1_r0_m0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0" },
        new[] { "zone_send_s1_r0_m0p5", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0.5" },
        new[] { "zone_send_s1_r0_m1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1" },
        new[] { "zone_send_s1_r0_m1p1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1.1" },
        new[] { "zone_curve_s1_r1_d0", "zone=0", "position=0", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s1_r1_d5", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s1_r1_d10", "zone=0", "position=10", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s1_r1_d15", "zone=0", "position=15", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s1_r1_d20", "zone=0", "position=20", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_curve_s1_r1_d25", "zone=0", "position=25", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "curve=1", "routed" },
        new[] { "zone_send_s1_r1_m0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0", "routed" },
        new[] { "zone_send_s1_r1_m0p5", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=0.5", "routed" },
        new[] { "zone_send_s1_r1_m1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1", "routed" },
        new[] { "zone_send_s1_r1_m1p1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "S:reverbZoneMix=1.1", "routed" },
        new[] { "zone_noise_s0_mono0", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768" },
        new[] { "zone_noise_s0_mono1", "zone=0", "position=5", "S:spatialBlend=0", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "mono" },
        new[] { "zone_noise_s1_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768" },
        new[] { "zone_noise_s1_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "mono" },
        new[] { "zone_noise_routed_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed" },
        new[] { "zone_noise_routed_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "mono" },
        new[] { "zone_noise_lateclip", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lateclip" },
        new[] { "zone_noise_pitchset", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "pitchset" },
        new[] { "zone_noise_genericset", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "genericset" },
        new[] { "zone_noise_velocityset", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "velocityset" },
        new[] { "zone_noise_spreadset", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "spreadset" },
        new[] { "zone_lifecycle_cold_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=cold" },
        new[] { "zone_lifecycle_warm_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=warm" },
        new[] { "zone_lifecycle_enable_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=enable" },
        new[] { "zone_lifecycle_late_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=late" },
        new[] { "zone_lifecycle_stop_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=stop" },
        new[] { "zone_lifecycle_end_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=11265", "routed", "lifecycle=end" },
        new[] { "zone_lifecycle_cold_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=cold", "mono" },
        new[] { "zone_lifecycle_warm_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=warm", "mono" },
        new[] { "zone_lifecycle_enable_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=enable", "mono" },
        new[] { "zone_lifecycle_late_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=late", "mono" },
        new[] { "zone_lifecycle_stop_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=stop", "mono" },
        new[] { "zone_lifecycle_end_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=11265", "routed", "lifecycle=end", "mono" },
        new[] { "zone_keys_customrolloff_long", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=CustomRolloff", "curveshape=long" },
        new[] { "zone_keys_customrolloff_negative", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=CustomRolloff", "curveshape=negative" },
        new[] { "zone_keys_customrolloff_short", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=CustomRolloff", "curveshape=short" },
        new[] { "zone_keys_customrolloff_unsorted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=CustomRolloff", "curveshape=unsorted" },
        new[] { "zone_keys_customrolloff_weighted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=CustomRolloff", "curveshape=weighted" },
        new[] { "zone_keys_customrolloff_single", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=CustomRolloff", "curveshape=single" },
        new[] { "zone_keys_spatialblend_long", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=SpatialBlend", "curveshape=long" },
        new[] { "zone_keys_spatialblend_negative", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=SpatialBlend", "curveshape=negative" },
        new[] { "zone_keys_spatialblend_short", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=SpatialBlend", "curveshape=short" },
        new[] { "zone_keys_spatialblend_unsorted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=SpatialBlend", "curveshape=unsorted" },
        new[] { "zone_keys_spatialblend_weighted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=SpatialBlend", "curveshape=weighted" },
        new[] { "zone_keys_spatialblend_single", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=SpatialBlend", "curveshape=single" },
        new[] { "zone_keys_spread_long", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=Spread", "curveshape=long" },
        new[] { "zone_keys_spread_negative", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=Spread", "curveshape=negative" },
        new[] { "zone_keys_spread_short", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=Spread", "curveshape=short" },
        new[] { "zone_keys_spread_unsorted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=Spread", "curveshape=unsorted" },
        new[] { "zone_keys_spread_weighted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=Spread", "curveshape=weighted" },
        new[] { "zone_keys_spread_single", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=Spread", "curveshape=single" },
        new[] { "zone_keys_reverbzonemix_long", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=ReverbZoneMix", "curveshape=long" },
        new[] { "zone_keys_reverbzonemix_negative", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=ReverbZoneMix", "curveshape=negative" },
        new[] { "zone_keys_reverbzonemix_short", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=ReverbZoneMix", "curveshape=short" },
        new[] { "zone_keys_reverbzonemix_unsorted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=ReverbZoneMix", "curveshape=unsorted" },
        new[] { "zone_keys_reverbzonemix_weighted", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=ReverbZoneMix", "curveshape=weighted" },
        new[] { "zone_keys_reverbzonemix_single", "zone=0", "position=7", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "curvekind=ReverbZoneMix", "curveshape=single" },
        new[] { "zone_first_generic", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed" },
        new[] { "zone_first_cave", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reverbPreset=Cave" },
        new[] { "zone_first_user", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reverbPreset=User" },
        new[] { "zone_first_reflect", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reflectionsDelay=0.023" },
        new[] { "zone_first_late", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reverbDelay=0.033" },
        new[] { "zone_first_level", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reverb=350" },
        new[] { "zone_first_changed", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reflectionsDelay=0.023", "Z:reverbDelay=0.033", "Z:reverb=350" },
        new[] { "zone_first_user_changed", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reverbPreset=User", "Z:reflectionsDelay=0.023", "Z:reverbDelay=0.033", "Z:reverb=-302" },
        new[] { "zone_first_cave_changed", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "Z:reverbPreset=Cave", "Z:reflectionsDelay=0.023", "Z:reverbDelay=0.033", "Z:reverb=350" },
        new[] { "zone_voices_dry", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "shotframes=5121", "component=dry" },
        new[] { "zone_voices_distortion", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "shotframes=5121", "component=distortion" },
        new[] { "zone_voices_chorus", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "shotframes=5121", "component=chorus" },
        new[] { "zone_lifecycle_pause_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=pause", "mono" },
        new[] { "zone_lifecycle_pauseactivate_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=pauseactivate", "mono" },
        new[] { "zone_lifecycle_pauseplay_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=pauseplay", "mono" },
    };
    public static readonly string[] FilterProperties = {
        "reverbPreset", "dryLevel", "room", "roomHF", "roomLF", "decayTime", "decayHFRatio", "reflectionsLevel",
        "reflectionsDelay", "reverbLevel", "reverbDelay", "hfReference", "lfReference", "diffusion", "density"
    };
    public static readonly string[] ZoneProperties = {
        "reverbPreset", "minDistance", "maxDistance", "room", "roomHF", "roomLF", "decayTime", "decayHFRatio",
        "reflections", "reflectionsDelay", "reverb", "reverbDelay", "HFReference", "LFReference", "diffusion", "density"
    };
    public static readonly string[][] LiveCases = {
        new[] { "zone_live_toggle_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=toggle" },
        new[] { "zone_live_active_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=active" },
        new[] { "zone_live_bypass_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=bypass" },
        new[] { "zone_live_toggle_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=toggle", "mono" },
        new[] { "zone_live_active_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=active", "mono" },
        new[] { "zone_live_bypass_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=bypass", "mono" },
        new[] { "zone_live_destroy_mono0", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=destroy" },
        new[] { "zone_live_destroy_mono1", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=destroy", "mono" },
        new[] { "zone_live_cave", "zone=0", "position=5", "S:spatialBlend=1", "S:rolloffMode=Linear", "S:minDistance=1", "S:maxDistance=20", "noise", "frames=32768", "routed", "lifecycle=toggle", "Z:reverbPreset=Cave" },
    };
    public static string[] Selected()
    {
        var only = Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_CASES");
        var keys = new List<string>();
        foreach (var c in Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_PROFILE") == "live" ? LiveCases : Cases) if (string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), c[0]) >= 0) keys.Add(c[0]);
        return keys.ToArray();
    }
    public const int Blocks = 24;
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine || Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_PROFILE") == "motion") return;
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
        var routine = Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_PROFILE") == "motion" ? CaptureMotion() : Capture();
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
        zone.reverbPreset = AudioReverbPreset.Cave;
        Add("zone.repeated_preset", Table(zone, ZoneProperties));
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
            if (key.StartsWith("zone_first_")) {
                if (!AudioSettings.Reset(config)) throw new Exception("Case reset failed");
                if (!AudioRenderer.Start()) throw new Exception("Case renderer restart failed");
                // Advance one silent block before scheduling a source on the restarted clock.
                if (!Engine) yield return null;
                using (var warm = new NativeArray<float>(2048, Allocator.Persistent))
                    if (!AudioRenderer.Render(warm)) throw new Exception("Case clock warmup failed");
            }
            var tokens = Array.Find(Cases, c => c[0] == key) ?? Array.Find(LiveCases, c => c[0] == key);
            int channels = Array.IndexOf(tokens, "mono") >= 0 ? 1 : 2;
            int frames = ClipFrames;
            foreach (string token in tokens) if (token.StartsWith("frames=")) frames = int.Parse(token.Substring(7), CultureInfo.InvariantCulture);
            var go = new GameObject(key); var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0; source.volume = 1;
            var clip = AudioClip.Create(key, frames, channels, rate, false);
            float[] signal = Signal(channels, frames);
            if (Array.IndexOf(tokens, "noise") >= 0) {
                uint seed = 12345;
                for (int f = 0; f < frames; f++) {
                    seed = seed * 1664525u + 1013904223u;
                    float left = 0.25f * ((seed >> 8) / 8388608f - 1f);
                    seed = seed * 1664525u + 1013904223u;
                    float right = 0.25f * ((seed >> 8) / 8388608f - 1f);
                    signal[f * channels] = f >= 1024 ? left : 0;
                    if (channels == 2) signal[f * channels + 1] = f >= 1024 ? right : 0;
                }
            }
            if (!clip.SetData(signal, 0)) throw new Exception("SetData failed"); if (Array.IndexOf(tokens, "lateclip") < 0) source.clip = clip;
            AudioReverbFilter filter = null;
            var zones = new List<AudioReverbZone>();
            bool hasZone = false;
            foreach (string token in tokens) if (token.StartsWith("zone=")) hasZone = true;
            if (!hasZone) source.outputAudioMixerGroup = child;
            for (int t = 1; t < tokens.Length; t++) {
                string token = tokens[t];
                if (token == "lateclip") { }
                else if (token.StartsWith("shotframes=") || token.StartsWith("component=")) { }
                else if (token.StartsWith("lifecycle=") || token.StartsWith("curvekind=") || token.StartsWith("curveshape=")) { }
                else if (token == "pitchset") source.pitch = 1;
                else if (token == "genericset") zones[zones.Count - 1].reverbPreset = AudioReverbPreset.Generic;
                else if (token == "velocityset") source.velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
                else if (token == "spreadset") source.spread = 0;
                else if (token == "routed") source.outputAudioMixerGroup = child;
                else if (token.StartsWith("position=")) go.transform.position = new Vector3(0, 0, float.Parse(token.Substring(9), CultureInfo.InvariantCulture));
                else if (token == "curve=1") source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix,
                    new AnimationCurve(new Keyframe(0, 0.2f, 0, 0), new Keyframe(0.4f, 0.8f, 0, 0), new Keyframe(1, 0.3f, 0, 0)));
                else if (token == "filter") filter = go.AddComponent<AudioReverbFilter>();
                else if (token == "listener") filter = gameObject.AddComponent<AudioReverbFilter>();
                else if (token == "disabled") filter.enabled = false;
                else if (token == "mono" || token == "noise" || token.StartsWith("frames=")) { }
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
            string Option(string prefix, string fallback = "") {
                foreach (var token in tokens) if (token.StartsWith(prefix)) return token.Substring(prefix.Length);
                return fallback;
            }
            string lifecycle = Option("lifecycle=");
            if (lifecycle == "enable") zones[0].gameObject.SetActive(false);
            if (lifecycle == "late") { DestroyImmediate(zones[0].gameObject); zones.Clear(); }
            string curveKind = Option("curvekind=");
            if (curveKind != "") {
                var type = (AudioSourceCurveType)Enum.Parse(typeof(AudioSourceCurveType), curveKind);
                string shape = Option("curveshape=");
                var keys = shape == "single" ? new[] { new Keyframe(4, 0.7f, 0.1f, -0.2f) }
                    : shape == "negative" ? new[] { new Keyframe(-2, 0.2f, 0.1f, -0.2f), new Keyframe(-0.5f, 0.8f, 0.3f, -0.4f) }
                    : shape == "short" ? new[] { new Keyframe(-0.2f, 0.2f, 0.1f, -0.2f), new Keyframe(0.5f, 0.8f, 0.3f, -0.4f) }
                    : shape == "unsorted" ? new[] { new Keyframe(4, 0.8f, 0.3f, -0.4f), new Keyframe(-2, 0.2f, 0.1f, -0.2f), new Keyframe(1, 0.6f, -0.1f, 0.2f) }
                    : new[] { new Keyframe(-2, 0.2f, 0.1f, -0.2f), new Keyframe(4, 0.8f, 0.3f, -0.4f) };
                if (shape == "weighted") {
                    for (int i = 0; i < keys.Length; i++) { keys[i].weightedMode = WeightedMode.Both; keys[i].inWeight = 0.2f; keys[i].outWeight = 0.4f; }
                }
                var curve = new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.Loop };
                source.SetCustomCurve(type, curve);
                string KeyTable(AnimationCurve c) {
                    var values = new List<string>();
                    foreach (var k in c.keys) values.Add(string.Join(",", new[] { k.time, k.value, k.inTangent, k.outTangent, k.inWeight, k.outWeight }.Select(v => BitConverter.SingleToInt32Bits(v).ToString(CultureInfo.InvariantCulture))) + "," + (int)k.weightedMode);
                    return ((int)c.preWrapMode) + ":" + ((int)c.postWrapMode) + ":" + string.Join(";", values);
                }
                records.Add("\"" + key + ".inputCurve\":\"" + KeyTable(curve) + "\"");
                records.Add("\"" + key + ".storedCurve\":\"" + KeyTable(source.GetCustomCurve(type)) + "\"");
            }
            if (lifecycle == "warm") {
                if (Engine) FlushEngine(); else yield return null;
            }
            if (Array.IndexOf(tokens, "lateclip") >= 0) source.clip = clip;
            if (filter) records.Add("\"" + key + ".filter\":\"" + Table(filter, FilterProperties) + "\"");
            for (int z = 0; z < zones.Count; z++) records.Add("\"" + key + ".zone" + z + "\":\"" + Table(zones[z], ZoneProperties) + "\"");
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            AudioClip shot = null;
            string componentKind = Option("component=");
            if (componentKind == "distortion") go.AddComponent<AudioDistortionFilter>();
            if (componentKind == "chorus") go.AddComponent<AudioChorusFilter>();
            source.Play();
            string shotFrames = Option("shotframes=");
            if (shotFrames != "") {
                int count = int.Parse(shotFrames, CultureInfo.InvariantCulture);
                shot = AudioClip.Create(key + ".shot", count, channels, rate, false);
                var shotSignal = new float[count * channels];
                Array.Copy(signal, shotSignal, shotSignal.Length);
                shot.SetData(shotSignal, 0); source.PlayOneShot(shot, 0.5f);
            }
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                if (Engine) FlushEngine();
                for (int block = 0; block < Blocks; block++) {
                    if (block == 8) {
                        if (lifecycle == "enable") zones[0].gameObject.SetActive(true);
                        if (lifecycle == "late") zones.Add(new GameObject(key + ".latezone").AddComponent<AudioReverbZone>());
                        if (lifecycle == "stop") source.Stop();
                        if (lifecycle.StartsWith("pause")) {
                            source.Pause();
                            records.Add("\"" + key + ".pausedPlaying\":" + (source.isPlaying ? "true" : "false"));
                        }
                        if (Engine && lifecycle != "") FlushEngine();
                    }
                    if ((block == 8 || block == 16) && (lifecycle == "toggle" || lifecycle == "active" || lifecycle == "bypass")) {
                        bool enabled = block == 16;
                        if (lifecycle == "toggle") zones[0].enabled = enabled;
                        if (lifecycle == "active") zones[0].gameObject.SetActive(enabled);
                        if (lifecycle == "bypass") source.bypassReverbZones = !enabled;
                        if (Engine) FlushEngine();
                    }
                    if (lifecycle == "destroy" && (block == 8 || block == 16)) {
                        if (block == 8) { DestroyImmediate(zones[0].gameObject); zones.Clear(); }
                        else zones.Add(new GameObject(key + ".replacement").AddComponent<AudioReverbZone>());
                        if (Engine) FlushEngine();
                    }
                    if (block == 10 && lifecycle == "pauseactivate") {
                        var added = new GameObject(key + ".pausedzone").AddComponent<AudioReverbZone>();
                        added.reverbPreset = AudioReverbPreset.Cave;
                        zones.Add(added);
                        if (Engine) FlushEngine();
                    }
                    if (block == 12 && (lifecycle.StartsWith("pause"))) {
                        if (lifecycle == "pauseplay") source.Play(); else source.UnPause();
                        records.Add("\"" + key + ".resumedPlaying\":" + (source.isPlaying ? "true" : "false"));
                        if (Engine) FlushEngine();
                    }
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
            if (shot) DestroyImmediate(shot);
            if (Array.IndexOf(tokens, "listener") >= 0) DestroyImmediate(filter);
            foreach (var z in zones) DestroyImmediate(z.gameObject);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
