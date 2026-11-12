# Prologic output

The `prologic` profile measures Unity 6000.0.71f1 Linux at 48000 and 44100 Hz,
1024-frame DSP buffers, with an isolated eight-channel PulseAudio sink. It runs
21 matching controls in Stereo and Prologic, each mode in a separate process.
The engine uses the same two-session boundary at each rate.

| Basis | Contract or measured result |
| --- | --- |
| Public contract | [AudioSpeakerMode.Prologic](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.Prologic.html) describes stereo carrying a surround matrix for a decoder. [AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) allows the nearest supported configuration. |
| Observed configuration | The Editor reports that requested mode 7 falls back to Stereo. The configuration getter retains Prologic; the listener callback reports two channels. |
| Distinguishing PCM controls | Dry mono/stereo clips, front/rear/left/right 3D sources and a rotated listener distinguish spatial behavior. Six live wet paths, linked dynamics, Send/Receive, source Chorus, listener Reverb, a zone, startup reverb and a +30 dB reverb slot control exercise processing. |
| Paired result | Each repeat has 2,752,512 bit-identical Stereo/Prologic PCM samples at each rate, including Master floats and device PCM. Separate processes give each mode the same history. `prologic_pairs.py` checks this relationship from the committed reference and reports every differing sample. |
| Independent repeats | The complete 42-control captures agree exactly on 5,505,368 values per rate. All eight Editor sessions use the GPU and contain no audio callback assertions. |

This implements the measured stereo fallback. Matrix-encoded output on other
drivers or releases remains unmeasured. The existing surround compatibility
selector admits Normalize, wet slots and default Pitch Shifter for this fallback
only at 48000/44100 Hz and 1024 frames. Other releases retain their previous
unmeasured two-channel path and effect guards. Other Prologic rate/buffer pairs
remain outside this admission.

The engine passes all 5,505,368 values per rate under the existing gate. The
strict comparison retains 236 device-only one-step differences at 48000 Hz and
250 at 44100 Hz; every Master float passes ATOL 2e-5 / RTOL 2e-4. No extra float
allowance or sample window is used. Prior references and all 1,582 prior authored
inputs remain byte-identical; 84 authored inputs are added.

Record two independent captures with `native.py --release 6000.0.71f1 --rate RATE
--set prologic --work FRESH_CACHE_DIRECTORY`. Publish one with `publish.py
--prologic --repeat REPEAT`; raw repeats stay outside Git. The engine-only gate
is `context_run.py --release 6000.0.71f1 --profile prologic`.


The separate [six-input availability profile](ENCODING.md) adds actual
six-channel generated clips with isolated impulses and dense independent input.
The same Linux eight-channel reference device produces bit-identical Stereo and
Prologic output at both rates, including the complete six-input response. Its
canonical Native availability relationship is checked by the `prologic` gate.
The engine's six-channel voice-input diagnostic remains open; it does not extend
the 42-control mono/stereo-input PCM admission above.
