# Material color transfer oracle

Unity 6000.0.71f1, built-in pipeline. The probe measures CPU material getters,
shader defaults, color/vector setters, property IDs, arrays, material property
blocks, copied properties, imported materials and variants. Shader buffers and
readbacks use float precision. Live asset tests restore the exact authored
material bytes after the Editor exits.

`Reference/unity-6000.0.71f1/{Linear,Gamma}` contains the canonical Native
recordings. Each color space has 2,986 observations and 424 complete 4x4 float
buffers, totaling 27,136 channels. Two independent Native processes produced
byte-identical captures per space. Baseline and engine captures, manifests and
logs belong in the validation cache.

The strict comparator keeps its original absolute/relative tolerance of 2e-5.
It checks all keys, complete image manifests, finite GPU values and every float
channel. Nonfinite CPU observations use explicit JSON strings. Both final engine
spaces pass all observations, images and channels. No failures are excluded.

In Linear, controlled color properties and plain vector properties have different
transfer behavior. Material setters retain source values internally; getters and
shader bindings follow the measured conversion for the property type and API.
Gamma remains a separate oracle. Compatibility with other releases requires
recording those releases, not assuming the 6000.0.71f1 result.

The default workspace is
`${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/materialcolortransfer/`.
`run.sh` puts captures in `<label>/<Linear|Gamma>` and builds in `build/`.
`MATERIAL_COLOR_TRANSFER_ROOT`, `MATERIAL_COLOR_TRANSFER_OUT`,
`MATERIAL_COLOR_TRANSFER_CLI`, `UNBOUND_MANAGED_DIR` and
`MATERIAL_COLOR_TRANSFER_NATIVE_DIR` override those locations.
`MATERIAL_COLOR_TRANSFER_COLOR_SPACE=Gamma` uses a generated project copy under
`Library/Unbound`. Every heavy command acquires `~/.cache/compat/with-heavy-job`.
`UNBOUND_PROBE_UNITY_VERSION` selects the Editor and reference release;
`UNBOUND_PROBE_UNITY` overrides its executable.

```sh
f=tests/projects/materialcolortransfer
bash "$f/run.sh" build
bash "$f/run.sh" engine current
MATERIAL_COLOR_TRANSFER_COLOR_SPACE=Gamma bash "$f/run.sh" engine current
python3 "$f/summarize.py" current
```

`summarize.py` accepts `--linear`, `--gamma`, `--before-linear`, `--before-gamma`
and `--out` for existing captures. It returns a failure when any strict comparison
fails. Historical captures are usable through explicit paths:

```sh
python3 "$f/summarize.py" retained \
  --linear "$HOME/.cache/compat/materialcolortransfer-6239018d/final2-linear" \
  --gamma "$HOME/.cache/compat/materialcolortransfer-6239018d/final2-gamma"
```

For a new reference release, record `native` twice per color space with different
labels. Pass `--native-linear`, `--repeat-linear`, `--native-gamma` and
`--repeat-gamma` to `summarize.py --record-reference`. The script requires
byte-identical observation/buffer repeats and refuses to overwrite an existing
oracle. The only committed products are observations and compressed Native float
buffers. Reports and hash manifests remain in the run cache.

`regress.py <label>` runs the existing graphics, resources, builtin pipeline, clipping,
UI colors, focused camera and UI Toolkit probes. `MATERIAL_COLOR_REGRESS_OUT`
selects its output directory, which defaults to `<label>/regress`.
`measure-regress.py` compares `before/regress` and `current/regress`; `--before`,
`--after` and `--out` select existing captures and a report destination. Setting
`MATERIAL_COLOR_TRANSFER_ROOT` retains the historical `regress-before` and
`regress-final` layout. `MATERIAL_COLOR_TRANSFER_EVIDENCE_DIR` can select the
external report directory.
The inherited UI Toolkit 53-versus-54-pixel label width remains visible in its
Native report. Its before/final captures must still compare identically.
