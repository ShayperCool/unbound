# Six-input Prologic availability

Unity 6000.0.71f1 on Linux, with a private eight-channel PulseAudio device,
48000/44100 Hz and 1024-frame DSP periods. The independent `encoding` diagnostic
adds four controls: Stereo and Prologic, each with six isolated channel impulses
and six distinct noise streams. Each mode starts in a separate process.

| Basis | Distinguishing control |
| --- | --- |
| [Prologic](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.Prologic.html) describes encoded stereo for a surround decoder. Two output channels alone do not establish whether encoding is active. | Six input-channel basis impulses expose the complete two-output response. Dense six-channel input checks the same relationship over the full capture. The earlier 42-control profile also compares front/rear 3D sources and processing paths. |
| [Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) selects the closest supported configuration. | The getter retains mode 7, the callback has two channels, and the device capability getter reports 7.1. Stereo and Prologic PCM are compared exactly in independent sessions. |
| [Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.Create.html), [SetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.SetData.html) and [GetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.GetData.html) expose interleaved samples. Streamed and compressed-data limitations do not apply to these generated, non-streamed clips. | Every clip has six channels. Readback must equal every original sample before playback, and each reference retains that complete input stream. Distinct nonzero values on all six channels distinguish truncation or interleaving errors. |

Each isolated input impulse has amplitude 0.25 at frame `4096 + 2048*channel`.
The measured Master-output coefficients are identical in Stereo and Prologic:

| Input index | Left | Right |
| --- | ---: | ---: |
| 0 | 1 | 0 |
| 1 | 0 | 1 |
| 2 | 0.5 | 0.5 |
| 3 | 0.5 | 0.5 |
| 4 | 0.5 | 0 |
| 5 | 0 | 0.5 |

Independent Editor repeats agree on all 1,310,770 metadata and sample values
per rate, including every input, Master and device sample. Paired Stereo/Prologic output is bit-identical
for all 262144 output samples per rate, alongside 393216 input-readback samples.
Together with the earlier spatial controls and the recorded fallback, these
captures qualify Stereo fallback in this reference context. They provide no
encoded-output reference for other drivers, devices or releases.

Q416 adds [36 source-processing controls](VOICE_INPUT.md) and captures the same
availability profile on private 2-, 4- and 6-channel devices as well as fresh
8-channel repeats. Every pair agrees exactly; fresh eight-channel captures also
match this original reference byte-for-byte. All four devices retain the same
Stereo response in Prologic.

The engine now retains all six decoded inputs through interpolation and a
per-voice matrix before source filters/scripts in the measured non-streamed 2D
Stereo/Prologic scope. All 1,310,770 encoding values per rate and device pass
strictly; the source-processing profile passes all 11,665,952 values per rate.
`Compat` selects this mechanism. The earlier engine diagnostic's 262052 strict
and 262014 gate discrepancies per rate are retained as before results, with full
sample ranges and unchanged bounds. Six-input spatial sources, other output
layouts, streamed clips and other releases retain their previous unqualified
first-two-input path and remain Q416-R5a. Physical drivers and encoded output
outside the measured PulseAudio contexts remain unmeasured.

Record two fresh projects with `native.py --release 6000.0.71f1 --rate RATE
--set encoding --work FRESH_CACHE_DIRECTORY`, then publish with `publish.py
--encoding --repeat REPEAT`. The canonical availability reference is under
`Reference/unity-6000.0.71f1/encoding`. The `prologic` land gate validates its
recorded mode-pair relationship before the existing engine replay.
`context_run.py --profile encoding --device-channels C` replays the six-input
engine profile against the matching device reference and returns its actual result.
