# Live reverb zones

The `zones` profile records Unity 6000.0.71f1 at 48000 and 44100 Hz,
1024-frame DSP buffers, on an isolated eight-channel device. Its 77 controls
cover Stereo, Quad, Surround, 5.1 and 7.1 output. Every change occurs while
the source remains audible. The master sink records the dry mixer output;
the device stream includes the shared zone return and listener components.

## Contracts and controls

The Unity 6000.0 [AudioReverbZone API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbZone.html)
and [Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbZone.html)
describe full effect inside the minimum radius, no effect outside the maximum,
and combined effects in overlapping zones. The radius control starts between
these boundaries, moves outside, returns between them, then moves inside the
minimum radius. Zone and listener movement controls exercise the same boundary
changes. Overlap movement crosses total blend weights above, at and below one.
The precise weighting and parameter transitions are release measurements.
An extra Stereo control starts nearer the minimum radius; another changes
LF Reference through 1000, 250 and 500 Hz while the listener stays between radii.

The [reverbZoneMix API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-reverbZoneMix.html)
describes the linear 0 to 1 range and the extra 10 dB from 1 to 1.1. A live
control changes through 0, 0.5 and 1.1. User Room HF and reflection delay setters,
selected-preset scalar retention, overlap scalar edits and enabled-state changes
have separate controls. A dry listener-movement control isolates source panning.
Doppler is disabled for these movement controls.

Listener Chorus, its dry companion and a routed source requesting listener
bypass test where the zone return enters the output graph. The
[bypassListenerEffects API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassListenerEffects.html)
limits that bypass to sources without an output mixer group. These sources are
routed, so their bypass request does not exempt their dry signal from the filter.
The measured zone return joins the output after the listener component chain.
The low-frequency input shelf retains its transposed state across parameter edits
and bypass. Reference requests also survive return destruction. When a new return
receives the same clamped reference request as its predecessor, it retains its own
default frequency until that request changes. Activation observes the zone's pose
before subsequent script edits, including a move made before the first audio frame. The partial-start controls distinguish
this history from a fresh session's first activation.

## Reproduction and scope

Record two independent `native.py --set zones` sessions per rate, then publish
one with `publish.py --zones --repeat <other capture>`. `context_run.py --profile zones`
replays committed authored inputs against the canonical reference. The declared
recipe in `gate.json` selects the release explicitly. Raw reports preserve every
failure. Float tolerances and the existing one-step device PCM allowance are
unchanged.

Mono, other rate/buffer pairs, other releases and additional listener-filter
combinations remain outside this reference. Other releases retain their previous
low-frequency input-filter arithmetic and zone/listener placement pending measurement.

Both independent captures agree exactly on 24,838,909 values per rate. The engine
gate passes every value using the original tolerances. Strict reports retain
10,836 device samples at 48000 Hz and 11,006 at 44100 Hz that differ by one
16-bit step. Every master-sink float passes without that device allowance. All
1,666 prior authored inputs remain byte-identical; this profile adds 154 inputs.
