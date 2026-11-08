# Doctor realtime GI impact (Q136)

Unity 6000.0.71f1, built-in pipeline. This probe uses the committed lighting scenes and data in
`tests/projects/lightingassets`, copied into a disposable project. Its independent no-precomputation
control opens `RealtimeOff`, assigns new Lighting Settings, captures a frame with realtime GI off,
enables it without precomputing, waits 120 frames and captures again. It records enabled=true,
zero dynamic lightmap contributors and zero changed pixels. The precomputed `Realtime` control
records four contributors. Independent Editor repeats must agree before publishing.

The [Unity 6000.0 Enlighten workflow](https://docs.unity3d.com/6000.0/Documentation/Manual/realtime-gi-using-enlighten.html)
documents the runtime dependence on precomputed visibility. The
[realtimeGI API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightingSettings-realtimeGI.html)
describes the scene enable switch. Neither says that enabling the switch creates runtime output.
The no-output frame comparison is an observation of this release and fixture.

Doctor gets scene references and the settings switch from the same managed readers as the
loader, then reads referenced LightingData with the runtime asset reader. The runtime warning
and the audit share the dynamic-contributor predicate. The gate compares the native contributor
count and checks enabled-without-output and unassigned controls. Rust tests additionally cover
shared data, missing references, local references, ordinary baked data and settings disabled
after precomputation. Failed reads remain incomplete, never evidence of absent output.
Game code can assign other data at runtime; this static audit covers serialized references.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/doctorgi/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/doctorgi/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/doctorgi/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/doctorgi/run.py gate
```

Gate uses `$UNBOUND`, then `$UNBOUND_CLI`, then `target/release/unbound`. Output goes under
`$XDG_CACHE_HOME/unbound/validation/runs/doctorgi/`.
