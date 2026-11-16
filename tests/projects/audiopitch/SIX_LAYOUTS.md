# Six-input output layouts

Unity 6000.0.71f1 on Linux, with a private eight-channel PulseAudio device, 48000 and
44100 Hz and 1024-frame DSP periods. The `six-layouts` profile adds 17 controls to the
[encoding](ENCODING.md) availability profile: a non-streamed six-channel clip on a 2D source
(`spatialBlend` 0) through every other output speaker mode (Mono, Quad, Surround, 5.1 and
7.1), each with `panStereo` 0, -0.65 and 0.65, plus Stereo with both pans. Each input channel
carries one impulse of amplitude 0.25 at frame `4096 + 2048*channel`, so every Master and
device frame shows one input's complete output row. All controls run in one session.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | [AudioSpeakerMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.html) names the output layouts; [AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) selects one. [panStereo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-panStereo.html) pans a 2D sound left or right. None of them specifies how a six-channel clip maps to a layout's speakers. |
| Measured, Unity 6000.0.71f1, this profile | Every layout first applies the same balance to the inputs: inputs 0 and 4 are scaled by `min(1, 1 - pan)`, inputs 1 and 5 by `min(1, 1 + pan)`; inputs 2 and 3 are unchanged. This profile measures pan 0 and ±0.65; the [voice-input](VOICE_INPUT.md) pan controls support the same law for Stereo. A fixed matrix per layout then maps the six inputs to the speakers (below). Device PCM agrees with the Master floats within one 16-bit step, and both rates give identical rows. |

Rows are inputs 0 to 5, columns the layout's output channels, at pan 0:

| Layout | Input 0 | Input 1 | Input 2 | Input 3 | Input 4 | Input 5 |
| --- | --- | --- | --- | --- | --- | --- |
| Mono | 1 | 1 | 1 | 1 | 1 | 1 |
| Stereo, Prologic ([ENCODING.md](ENCODING.md)) | 1 0 | 0 1 | 0.5 0.5 | 0.5 0.5 | 0.5 0 | 0 0.5 |
| Quad | 1 0 0 0 | 0 1 0 0 | 0.5 0.5 0 0 | 0.25 0.25 0 0 | none | none |
| Surround | 1 0 0 0 0 | 0 1 0 0 0 | 0 0 1 0 0 | none | none | 0 0 0 1 0 |
| 5.1 | output 0 | output 1 | output 2 | output 3 | output 4 | output 5 |
| 7.1 | output 0 | output 1 | output 2 | output 3 | outputs 4 and 6 | outputs 5 and 7 |

"None" means the input reaches no output channel in that layout. These are observations in
this capture context, not a documented mapping; physical drivers and other backends are
unmeasured. Spatial six-input sources and streamed six-channel clips are not covered here.

The engine implements this balance and these matrices for non-streamed 2D six-input voices
on this release (`Compat` AudioOutputRules.six_channel_2d_voices). `context_run.py --profile
six-layouts` gates every control at both rates: all 8,323,278 values per rate pass, with
no failure in the strict comparison either (existing bounds, device PCM compared without
a 16-bit step). Spatial and streamed six-input voices still fail to start with the existing
diagnostic. Record two fresh projects per rate with `native.py --release 6000.0.71f1 --rate
RATE --set six-layouts --work FRESH_CACHE_DIRECTORY`, then publish with `publish.py
--six-layouts --capture FIRST/capture --repeat SECOND/capture`. The canonical reference is under
`Reference/unity-6000.0.71f1/six-layouts`.
