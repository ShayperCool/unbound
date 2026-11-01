# Texture sampler bias

Reuses the UI fixture's cached scene and replaces its behaviour with a synchronous sampler
probe. Constant-per-mip textures distinguish LOD selection from UV filtering. It covers byte,
half and float textures, readable and unreadable storage, bias set before first upload or on
a live texture, point/trilinear filtering, and seven legacy/object-style sampling operations.
The dense float control covers every anisotropic strength from 1 through 16. A 64x32 texture
checks transitions through higher mip levels. The default capture has 19,057 rows and 94,834
scalar/length observations. `--bilinear-control` selects a separate float control using bilinear
filtering, including the non-anisotropic nearest-mip case.

`--anisotropy Force`, `Enable`, and `Disable` select the project's global anisotropy policy.
Each profile fixes the remaining quality settings. Force uses the fixture's existing forced
minimum and maximum. The probe records requested texture properties as well as sampled values.
Native measurements distinguish a bias from the active anisotropic interpolation kernel;
an observed offset alone is insufficient to select a rendering correction.

```sh
sampler_work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/sampler-replay"
python3 tests/projects/samplerbias/run.py native --release 6000.0.71f1 --output "$sampler_work/native-a"
python3 tests/projects/samplerbias/run.py native --release 6000.0.71f1 --output "$sampler_work/native-b"
cmp "$sampler_work/native-a/capture/values.jsonl" "$sampler_work/native-b/capture/values.jsonl"
```

Repeat both captures with `--anisotropy Enable` and `--anisotropy Disable`, using fresh output
paths. Bilinear controls additionally pass `--bilinear-control` to both captures and verification.

Publish one agreeing Native capture under `Reference/unity-<version>/`. Both independent
repeats and execution manifests stay in cache. The runner wraps its own heavy child; do not
wrap it again. For engine runs use `run.py engine`, explicit `--release`, a fresh `--output`,
and matching private `--cli`/`--managed` artifacts.

`verify.py --release <version> --candidate <capture>/values.jsonl --before
<baseline>/values.jsonl --output <cache-report.json>` preserves the complete key union and
every missing, extra, retained, resolved and introduced observation. GPU float tolerance is
0.002, and state tolerance is 1e-6, as in the original clone control. Native repeat agreement
is checked byte for byte. Profile references live in the release directory, `Enable/`, and
`Disable/`; bilinear profiles use the same structure inside `Bilinear/`. No reference is derived
from engine output. Passing float tolerances does not assert byte-exact sampling.

`--live-control` reuses one unreadable float texture across 360 property changes and 7,560
samples. It changes bias after prior draws, switches filtering and anisotropy, and revisits
Force/Disable/Enable global policies. The forced range is an explicit probe input of2..16.
Its paired reference lives in `Live/`; pass the same flag to verification. The complete
capture has7,920 rows. These controls exercise invalidation of both cached binding paths.


The independent `--cache-control` profile keeps one material and named texture
binding across draws. After a warm draw it explicitly sets zero mip bias, draws
again, then changes filtering or wrapping and draws without intervening texture
uploads. Readback stays on the CPU and does not call Apply. This checks that
sampler metadata updates and later texture uploads cannot reuse a cached binding
stamp. Both Native repeats must agree before publishing the Cache reference.
The final two draws modify every CPU mip without calling Apply, change filtering,
and verify that the GPU still holds its prior pixels. Only the following Apply
makes the replacement pixels visible. The complete cache control contains 171
observations. Its paired Native reference preserves all 153 earlier observations.

The combined engine replay matches all 371,166 original sampler observations and
all 171 cache observations. No previously accepted numeric error increases. The
cache control resolves 33 differences, with no introduced or retained differences.
The sampler-only metadata update does not submit retained CPU pixels or recreate
a RenderTexture.
