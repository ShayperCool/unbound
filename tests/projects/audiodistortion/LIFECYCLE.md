# Live component histories

The live profile appends independent `component_lifecycle` and `lifecycle_controls`
sessions. Every existing session and every earlier reference sample is retained.
Each new control renders 24 stereo buffers of 1024 frames at 48000 and 44100 Hz.
Master floats and device PCM are compared with the existing bounds. Independent
Editor repeats must agree bit for bit before publication.

The main matrix exercises all six source filter types. Changes occur at buffers
6, 12, 13 and 18, after the deterministic noise has populated filter histories.
It covers enabled off/on, same-update Stop/Play, Stop followed by idle buffers,
listener bypass, and adding/removing another component. Further controls use mono
input, an unrouted source with an audible listener filter, a second stateful unit,
PlayOneShot without a main voice, and listener component enable changes.

| Basis | Contract or measurement |
| --- | --- |
| Documented, Unity 6000.0 | [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html) describes component order and enabled processing. The [Low Pass](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioLowPassFilter.html), [High Pass](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioHighPassFilter.html), [Echo](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEchoFilter.html), [Distortion](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html), [Chorus](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusFilter.html), and [Reverb](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbFilter.html) pages describe their signal processing and parameters. They do not promise a history reset when a component is disabled. |
| Documented, Unity 6000.0 | [bypassListenerEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassListenerEffects.html) skips listener effects for an unrouted source. It does not skip them when the source routes through a mixer group. The paired routed/unrouted controls include audible listener Distortion. |
| Documented, Unity 6000.0 | [Stop](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.Stop.html) stops the clip and makes the next Play start it at the beginning. The controls stop well into the clip and use a silent first buffer to distinguish restart from continuation. The documentation does not specify component history lifetime. |
| Documented, Unity 6000.0 | [SFX Reverb](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbEffect.html) and the Reverb component define Dry Level in mB. Exposed setters, a timed snapshot, and the component setter change an audible dry path while reverb is already active. |
| Measured, Unity 6000.0.71f1 | Disabled source units retain their histories without processing input. Explicit Stop retains histories across idle buffers as well as an immediate Play. Natural clip completion still cuts the tail and clears histories as measured in `audioreverb`. |
| Measured, Unity 6000.0.71f1 | The first listener-bypass change after a clip starts shares the source-bypass reconnection. Adding/removing components reconnects surviving source units. Reconnection restarts histories and phase, while Chorus retains its sweep span and Low Pass retains its resonant state. These are observations in this capture context, not a public scheduling guarantee. |
| Measured, Unity 6000.0.71f1 | Reverb Dry Level applies at the next rendered buffer without the wet network's coefficient crossfade. The previously implemented processing already matches this control. A second Distortion component is rejected without changing the existing one. |

Run the complete engine-only live gate with `python3
tests/projects/audiodistortion/live_run.py --release 6000.0.71f1` and the matching
`--unbound` build. Diagnostic subsets do not replace that gate. Strict reports
retain every PCM step difference; the separate gate permits the existing one-step
device PCM bound and never relaxes Master float tolerances.

Other releases retain their prior component activation and idle reset behavior.
Insertion/placement/bypass changes to AudioMixer effect slots, listener scripts
interleaved with components, and arbitrary device-buffer/layout live transitions
remain outside this measurement.

Scripting API references for the same release:
[LowPass](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioLowPassFilter.html),
[HighPass](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioHighPassFilter.html),
[Echo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioEchoFilter.html),
[Distortion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioDistortionFilter.html),
[Chorus](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter.html),
and [Reverb](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbFilter.html).
They expose the component parameters and inherited enabled state, without specifying
history lifetime or sample-level transition scheduling.
