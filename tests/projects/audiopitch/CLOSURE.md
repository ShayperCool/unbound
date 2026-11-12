# Q373 audio scope and follow-ups

Q373 measures Unity 6000.0.71f1 on Linux using an isolated eight-channel device.
Each published profile has two independent GPU Editor captures with exact
metadata and raw PCM agreement. The table lists measured contexts, not a promise
for other releases, devices, platforms or arbitrary graph edits.

| Item | Landed measurement | Engine result at the existing bounds |
|---|---|---|
| 1. Live surround | [45 controls](TRANSITIONS.md) at 48000/44100 Hz, 1024 frames, Quad/5.0/5.1/7.1 plus a Stereo control. Live parameters, component enabled states and zone presets. | 16,712,055 values per rate pass; current strict device differences 494/841. Reset PCM remains Q373-R1. |
| 2. Wet and linked paths | [238 wet and 18 placement controls](WET.md) at those rates/buffer and surround layouts. Six wet kinds, Send/Receive, Normalize and reverb startup. | 94,505,124 values per rate pass; strict device differences 721/694 in wet and 29/33 in wet-edges. Silent groups retain their actual history before sources arrive. |
| 3. Rate/buffer pairs | [168 controls](SETTINGS.md): 32000/256 and 48000/512 in Stereo/Quad/5.0/5.1/7.1; 96000/2048 in Stereo/Quad. | 54,134,190 values pass; 3921 strict device differences. Wider 96000/2048 captures remain Q373-R2. |
| 4. Prologic | [42 paired controls](PROLOGIC.md) at 48000/44100 Hz, 1024 frames. The recorded device falls back to plain Stereo while retaining the requested getter. | 5,505,368 values per rate pass; strict device differences 236/250. Paired Stereo/Prologic PCM is exact in both repeats. |
| 5. Live zones | [77 controls](ZONES.md) at 48000/44100 Hz, 1024 frames, Stereo/Quad/5.0/5.1/7.1. Movement, radii, User edits, overlap and listener components. | 24,838,909 values per rate pass; strict device differences 10,836/11,006. |
| 6. Mono effects | [64 controls](MONO.md) at 48000/44100 Hz, 1024 frames. Chorus taps, reverb folding between effects, wet boundaries and linked processing, with Stereo-output mono-clip controls. | 4,456,925 values per rate pass; strict device differences 329/401. |

Every Master float in these profiles passes ATOL `2e-5` / RTOL `2e-4`.
The retained device differences fit the existing one-step PCM gate. Reports keep
all raw differences, with no added residual allowance, alignment or sample limit.
All earlier canonical references and authored inputs remain unchanged. The
original Pitch/layout/device/surround recipe, each new profile and the ten prior
audio probes remain required validation gates.

## Open queue work

- **Q373-R1. Post-reset playback publication and device timing.** Immediate and
  future-scheduled recovery differ between independent Editor repeats at both
  rates. API callback flags, requested state and generated-clip loss agree.
  Both diagnostic sessions remain runnable. No canonical reset PCM, alignment
  correction or scheduling change is certified by this task.
- **Q373-R2. Wider 96000 Hz / 2048-frame callback captures.** Fresh Surround
  captures assert and can crash; 5.1/7.1 at this pair are also outside admission.
  Existing clean Stereo/Quad captures do not establish wider behavior.
- **Q373-R3. Listener scripts on zone returns.** The measured return joins after
  listener components. Processing relative to listener `OnAudioFilterRead`
  scripts is unmeasured; component-only Chorus controls do not certify it.
- **Prologic matrix encoding.** The recorded Stereo fallback does not measure an
  encoder, other devices, other setting pairs or other releases.
- **Additional context.** Mixer-slot identity/insertion/retargeting/placement and
  bypass edits, surround component membership edits, additional parameter
  combinations and wet-effect kinds, live/overlapping Mono zones, tail-only zone
  edits, same-frame zone toggles, and rate/buffer pairs outside the listed
  profiles remain unmeasured. Newly admitted Normalize/static wet settings fail
  clearly outside their measured contexts. Other releases retain the documented
  previous paths until measured.
