# Audio and font asset dispatch

Unity 6000.0.71f1, 177 exact records from two independent GPU Editor captures.
The captures used the NVIDIA driver only. The existing `assetinstantiate` probe adds
rendered bitmap font and audible imported audio copy controls.

[Object.Instantiate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html)
creates a copy. The recorded copies have distinct identities. Font character edits
leave the original unchanged; assigned material and texture references stay shared.
A material color write changes what the copy reads, and destroying the source font
leaves the copy and its material usable.

[AudioClip.GetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.GetData.html)
reads available sample data, with restrictions for compressed and streamed clips.
The PCM controls write distinct nonzero values through the copy and read them from
both objects. The selected runtime clones have no generated samples; their failed
reads retain the seeded output array. The imported copy remains readable after source
destruction and reads the reloaded sample data.

[UnloadAudioData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.UnloadAudioData.html)
is for clips based on sound file assets. The probe records immediate load/unload
results on an imported copy, including the original's state, and separate preloaded,
unloaded and streaming import configurations. These timings and ownership rules are
selected-release observations. The original reader and clone operations are preserved
by the adapter migration.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/audioadapters/run.py native --label first
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/audioadapters/run.py native --label second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/audioadapters/run.py publish first second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/audioadapters/run.py check
```

The gate uses the committed reference and a fresh engine project. Capture repeats,
engine outputs and logs stay outside Git. Managed tests also exercise reader discovery,
nondefault serialized font and bundle metadata, missing audio data diagnostics and
independent character arrays.
