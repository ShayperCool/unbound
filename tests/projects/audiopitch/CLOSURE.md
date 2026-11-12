# Audio scope and follow-ups

Q373 and Q390 measure Unity 6000.0.71f1 on Linux using an isolated
eight-channel device.
Each published profile has two independent GPU Editor captures with exact
metadata and raw PCM agreement. The table lists measured contexts, not a promise
for other releases, devices, platforms or arbitrary graph edits.

| Item | Landed measurement | Engine result at the existing bounds |
|---|---|---|
| 1. Live surround | [45 controls](TRANSITIONS.md) at 48000/44100 Hz, 1024 frames, Quad/5.0/5.1/7.1 plus a Stereo control. Live parameters, component enabled states and zone presets. | 16,712,055 values per rate pass; current strict device differences 494/841. Reset PCM remains Q373-R1. |
| 2. Wet and linked paths | [238 wet and 18 placement controls](WET.md) at those rates/buffer and surround layouts. Six wet kinds, Send/Receive, Normalize and reverb startup. | 94,505,124 values per rate pass; strict device differences 224/200 in wet and 29/33 in wet-edges. Silent groups retain their actual history before sources arrive. |
| 3. Rate/buffer pairs | [168 controls](SETTINGS.md): 32000/256 and 48000/512 in Stereo/Quad/5.0/5.1/7.1; 96000/2048 in Stereo/Quad. | 54,134,190 values pass; 3921 strict device differences. Further output settings are covered by row 3b. |
| 3b. Wide settings | [2940 controls](WIDE.md): four rates, eleven requested buffer sizes, Stereo/Quad/5.0/5.1/7.1; two entire rate/buffer pairs excluded. | 964,835,412 values pass; 85,136 strict device differences. Complete 3080-control diagnostics retain all failures. |
| 4. Prologic | [42 paired mono/stereo-input controls](PROLOGIC.md) at 48000/44100 Hz, 1024 frames. Four additional [six-input Native controls](ENCODING.md) qualify Stereo fallback on the reference device. | 5,505,368 values per rate pass; strict device differences 236/250. Paired Stereo/Prologic PCM is exact in both repeats. |
| 5. Live zones | [77 controls](ZONES.md) at 48000/44100 Hz, 1024 frames, Stereo/Quad/5.0/5.1/7.1. Movement, radii, User edits, overlap and listener components. | 24,838,909 values per rate pass; strict device differences 10,836/11,006. |
| 6. Mono effects | [64 controls](MONO.md) at 48000/44100 Hz, 1024 frames. Chorus taps, reverb folding between effects, wet boundaries and linked processing, with Stereo-output mono-clip controls. | 4,456,925 values per rate pass; strict device differences 329/401. |
| 7. Listener scripts | [66 controls](LISTENER_SCRIPTS.md) at both rates, 1024 frames. Moving wide zones, static Mono zones, script gains and routed/unrouted listener bypass. | 17,826,478 values per rate pass; strict device differences 2528/2902. |

Every Master float in the admitted engine PCM profiles passes ATOL `2e-5` /
RTOL `2e-4`.
The retained device differences fit the existing one-step PCM gate. Reports keep
all raw differences, with no added residual allowance, alignment or sample limit.
All earlier canonical references and authored inputs remain unchanged. The
original Pitch/layout/device/surround recipe, each admitted PCM profile and the
ten prior audio probes remain in the sheriff's full suite. Individual landings
check affected profiles and follow the complete-recipe rule in `land.py`.
The table reports completed comparisons; it does not claim that the final
Prologic landing repeated every profile. That landing used a user-approved,
one-time Prologic-only audio gate. Reset and six-input engine diagnostics are
not PCM admissions.

## Open queue work

- **Q373-R1 / Q390-R1. Post-reset playback publication and device timing.**
  Earlier immediate and scheduled sessions differ between independent Editor
  repeats at both rates. Fresh unchanged immediate 48000 Hz repeats agree on
  24,543,998 values, while scheduled 44100 Hz repeats retain 421,351 differences
  across six controls. The new [19-control callback-recovery profile](RECOVERY.md)
  retains 404,561 repeat differences at 48000 Hz and agrees exactly at 44100 Hz.
  Its engine gate has 382,753 failures at 48000 Hz and passes at 44100 Hz.
  These are distinct startup contexts; matching pairs do not remove the
  counterexamples. Callback flags, requested state and generated-clip loss agree.
  All diagnostic profiles remain runnable and their raw captures are retained.
  No canonical reset PCM, alignment correction or scheduling change is certified.
- **Q390-R2. Excluded settings and retained histories.** The 32000 Hz /
  340-frame and 48000 Hz / 256-frame pairs remain outside new Normalize/static
  wet admission. Full original repeats disagree in those contexts. Per-case
  Reset also exposes device-only disagreements at 32000 Hz / 512 frames in
  7.1, so it is diagnostic rather than a canonical startup correction.
  Wider 96000 Hz / 2048-frame callbacks are captured by the wide profile.
- **Listener component/script combinations.** The listener-script zone profile
  certifies independent returns after scripts. Filter components interleaved
  with those scripts remain rejected and need separate measurements.
- **Prologic matrix encoding.** The six-input availability profile and earlier
  spatial controls qualify Stereo fallback on this Linux PulseAudio reference
  device. They supply no encoded-output reference; other drivers, devices,
  setting pairs and releases remain unmeasured.
- **Six-channel clip rendering.** Generated six-channel clips retain all input
  samples through SetData/GetData, but the current voice path reads only the
  first two channels. The complete diagnostic retains 262,014 engine gate
  failures per rate. Multichannel voice input, source processing and routing
  remain a separate implementation; the fallback's admitted PCM scope uses
  mono/stereo input. See [ENCODING.md](ENCODING.md).
- **Additional context.** Mixer-slot identity/insertion/retargeting/placement and
  bypass edits, surround component membership edits, additional parameter
  combinations and wet-effect kinds, live/overlapping Mono zones, tail-only zone
  edits, same-frame zone toggles, and rate/buffer pairs outside the listed
  profiles remain unmeasured. Newly admitted Normalize/static wet settings fail
  clearly outside their measured contexts. Other releases retain the documented
  previous paths until measured.
