# Texture sampler bias

Reuses the UI fixture's cached scene and replaces its behaviour with a synchronous sampler
probe. Constant-per-mip textures distinguish LOD selection from UV filtering. It covers byte,
half and float textures, readable and unreadable storage, bias set before first upload or on
a live texture, point/trilinear filtering, anisotropic levels 1/7, and seven legacy/object-style
sampling operations. A denser fractional LOD curve separates interpolation from bias itself.
The inherited project quality settings remain fixed, including forced anisotropic filtering.

```sh
sampler_work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/sampler-replay"
python3 tests/projects/samplerbias/run.py native --release 6000.0.71f1 --output "$sampler_work/native-a"
python3 tests/projects/samplerbias/run.py native --release 6000.0.71f1 --output "$sampler_work/native-b"
cmp "$sampler_work/native-a/capture/values.jsonl" "$sampler_work/native-b/capture/values.jsonl"
```

Publish one agreeing Native capture under `Reference/unity-<version>/`. Both independent
repeats and execution manifests stay in cache. The runner wraps its own heavy child; do not
wrap it again. For engine runs use `run.py engine`, explicit `--release`, a fresh `--output`,
and matching private `--cli`/`--managed` artifacts.

`verify.py --release <version> --candidate <capture>/values.jsonl --before
<baseline>/values.jsonl --output <cache-report.json>` preserves the complete key union and
every missing, extra, retained, resolved and introduced observation. GPU float tolerance is
0.002, and state tolerance is 1e-6, as in the original clone control. Native repeat agreement
is checked byte for byte. No reference is derived from engine output.
