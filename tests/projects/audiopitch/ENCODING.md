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

The engine retains every six-channel input sample through SetData/GetData, but
voices currently read only their first two input channels. The complete engine
diagnostic retains 262052 strict and 262014 gate discrepancies per rate,
including every missing extra-input response and noise discrepancy. All raw strict and gate reports are
retained. Multichannel voice input, source processing and routing need a separate
implementation; the supported mono/stereo-input fallback remains the earlier
42-control scope. Original float bounds and full sample ranges apply throughout.

Record two fresh projects with `native.py --release 6000.0.71f1 --rate RATE
--set encoding --work FRESH_CACHE_DIRECTORY`, then publish with `publish.py
--encoding --repeat REPEAT`. The canonical availability reference is under
`Reference/unity-6000.0.71f1/encoding`. The `prologic` land gate validates its
recorded mode-pair relationship before the existing engine replay.
`context_run.py --profile encoding` records the open six-input engine diagnostic
and returns its actual comparison result.
