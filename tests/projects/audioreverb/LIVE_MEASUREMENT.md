# Live zone controls

Unity 6000.0.71f1, Linux Editor, stereo 48000/44100 Hz, 1024-frame DSP buffers.
Two independent Editor repeats agree exactly on all 1,081,379 records per rate.
The eleven controls retain every sample of 24 complete buffers, both device PCM
and the dry master sink. They cross mono/stereo clips with zone enable/disable,
GameObject activation, source bypassReverbZones and zone destruction/recreation.
A Cave preset control also switches from a flat high-frequency response during the
transition, checking that input history remains current before the next change.
Two static controls, an AudioReverbFilter and a User zone with Room HF -10000 mB at
HF Reference 1000 Hz, reach the 0.99 limit of the Room HF input pole.
The original 141-case reference is unchanged and all five formerly rejected
activation controls are now required by its gate.

| Unity 6000.0 contract | Divergent Editor control |
| --- | --- |
| [AudioReverbZone](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioReverbZone.html) modifies reverb according to listener position; [Behaviour.enabled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Behaviour-enabled.html) controls component activity. | The listener stays inside the zone while it is disabled at block 8 and enabled at block 16. Its audible return changes; the source continues. The original distance and overlapping-zone controls remain required. |
| [GameObject.SetActive](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GameObject.SetActive.html) disables components on an inactive object. | The same timed control uses SetActive instead of enabled and produces the same full PCM. |
| [bypassReverbZones](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassReverbZones.html) bypasses zone effects on a source. | A source changes bypass while its zone remains enabled. Its dry master is unchanged while the return changes. |

Measured details, rather than additional documented guarantees: disabling a zone
retains its return and histories while its parameters transition over a DSP buffer.
Early reflection gain and delay changes occur before the shared allpasses and their
output taps. Late input taps and output delays retain separate histories; feedback
delay/gain changes crossfade together. Disabled zones retain their return until the
last initialized zone object is destroyed. A newly connected return fades each
already playing voice's send over 64 frames; starting a new voice keeps its ordinary
startup behavior. The destroy/recreate controls and the original late-creation
controls distinguish this connection ramp from retaining an old return.

`live.py gate --release 6000.0.71f1` runs the complete profile at both rates with the
fixture's unchanged float tolerances and existing one-step device PCM allowance.
The ordinary gate also keeps a full strict report for all 141 original cases.

Deferred: the strict live reports retain 14 device PCM ties at 48000 Hz and 18
at 44100 Hz, each differing by one 1/32768 step. The unchanged gate accepts one
step; all float streams pass their original bounds. Real-device queued periods
are not measurable through this offline renderer.
