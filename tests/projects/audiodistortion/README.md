# Distortion

Unity 6000.0.71f1 Linux Editor, offline AudioRenderer, stereo output, 1024-frame
buffers at 48000 and 44100 Hz. Each of 44 cases records 32768 frames of device
PCM and the mixer's Master float sink. Two fresh private Editor projects per
rate must agree bit for bit before publishing one reference.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | [Mixer Distortion](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionEffect.html) has Distortion 0..1, default 0.5. The Editor names the serialized parameter `Level`. |
| Documented, Unity 6000.0 | [AudioDistortionFilter](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html) applies to sources or the listener. [distortionLevel](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiodistortionfilter/distortionlevel) has range 0..1, default 0.5. Neither page specifies a formula or setter clamping. |
| Documented, Unity 6000.0 | [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html) requires enabled components in Inspector order. [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) occupies its Inspector position, receives interleaved floats on the audio thread, and is unsupported on Web. |
| Measured in this release/context | Mixer and source share `y = (1+k)*x/(1+k*abs(x))`, `k = 2*d/(1-d)`, `d = min(clamp(Level,0,1),0.9999)`. No history or sample-rate dependence is visible. These equations describe the captured samples; the internal implementation is an inference. |
| Measured in this release/context | Component and exposed mixer setters retain -1 and 2, while their DSP clamps. Defaults, endpoints, intermediate levels, stereo ramps of both signs, small/large/DC/impulse/noise/tone, mono source, source/component/group/slot bypass, parent/prefader routing, serialized 0.37, and a halving callback before/after the source filter are captured. |
| Measured in this release/context | Source startup has the existing 64-frame input fade. An active or bypassed mixer slot retains the group's 1024-frame input fade. `mix_clear` also records exposed parameter restoration before rendering, consistent with the [GetFloat contract](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audio/audiomixer/getfloat): after ClearFloat the getter returns the snapshot value. DSP changes still enter through the existing audio update. |
| Unmeasured / unsupported | Wet mixing, timed parameter transitions, listener Distortion, 3D sources, changing bypass during playback, and more than two channels. Enabled mixer wet mixing and listener components fail clearly. Chorus, Flange, Normalize and Pitch Shifter are separate work. |

Record each Native repeat with `native.py --release 6000.0.71f1 --rate RATE
--work FRESH_CACHE_DIRECTORY`. It takes one heavy-job lease. Compare with
`compare.py --release 6000.0.71f1 --rate RATE --reference FIRST/capture
--candidate SECOND/capture --exact --report CACHE/repeat.jsonl.gz`.
Use `publish.py` only after exact agreement. Authored mixer/prefab/scene inputs
are recorded with `tests/authored_project.py record`; gate assembly verifies
their input hashes and never launches the Editor.

The engine gate is `python3 tests/projects/audiodistortion/run.py --release
"$UNBOUND_PROBE_UNITY_VERSION"`, with `UNBOUND` pointing at this checkout's
release build. The comparator retains every failing or missing/extra value,
using the existing audio float tolerances of 2e-5 absolute plus 2e-4 relative.
The strict report uses those bounds for everything; the gate separately admits
one device PCM step, never extra float-sink slack. All reports stay in the cache.
