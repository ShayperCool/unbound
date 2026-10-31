# Camera MSAA oracle

The Unity 6000.0.71f1 oracle covers explicit render targets, supported sample
counts, live sample changes, HDR intermediates, viewports, buffer bindings,
effect chains, clear preservation, screen reads and valid/rejected resolves.
`CAMERA_MSAA_FOCUSED=1` selects the separate effect-chain and clear-preservation
matrix. Linear and Gamma are distinct cases.

`Reference/unity-6000.0.71f1/{Linear,Gamma}` contains 3,261 observations,
433 complete float images and 76 raw Texture2D readbacks per color space.
`FocusedLinear` and `FocusedGamma` each contain 481 observations and 54 float
images. The full matrix contains all 948 original observations and 195 original
images unchanged. Baseline and engine captures are disposable run artifacts.
Only the Native oracle belongs in `Reference`.

`compare.py` checks every observation, buffer and captured PNG name with its
original absolute/relative tolerance of 2e-4. Missing and unexpected keys fail.
Rejected inactive resolves and differing Editor framebuffer reads remain in the
strict totals. The expanded engine comparison retains 3,201/3,261 observations
and 381/433 images in Linear, 3,219/3,261 and 405/433 in Gamma. Both focused
comparisons pass 481/481 observations, 54/54 images and 3,268,608 channels.
The retained full failures concern rejected resolves and screen-source/attachment
differences. They do not establish full screen API parity.

Run outputs, logs, manifests and reports default to
`${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/cameramsaa/<label>/`.
The runners acquire `~/.cache/compat/with-heavy-job`. Set `CAMERA_MSAA_CLI`,
`UNBOUND_MANAGED_DIR` and `UNBOUND_NATIVE_DIR` to use an existing isolated build.
`CAMERA_MSAA_PROJECT` overrides the fixture. Gamma uses a generated project copy
under `Library/Unbound`; the authored settings retain their exact bytes.
`UNBOUND_PROBE_UNITY_VERSION` selects both the Editor release and reference
version. `UNBOUND_PROBE_UNITY` can override the Editor executable.

```sh
f=tests/projects/cameramsaa
bash "$f/run.sh" build
bash "$f/run.sh" engine current
CAMERA_MSAA_COLOR_SPACE=Gamma bash "$f/run.sh" engine current
python3 "$f/followup-evidence.py" current
CAMERA_MSAA_FOCUSED=1 bash "$f/run.sh" engine current
CAMERA_MSAA_FOCUSED=1 CAMERA_MSAA_COLOR_SPACE=Gamma bash "$f/run.sh" engine current
python3 "$f/followup-focused-evidence.py" current
```

The full comparison intentionally exits 1 for the retained differences. Each
report script accepts `--linear`, `--gamma`, `--out` and Native/repeat overrides,
so existing captures can be checked without a build or a migration archive.
For example, retained machine-local captures can be compared explicitly:

```sh
python3 "$f/followup-evidence.py" retained \
  --linear "$HOME/.cache/compat/camerafollowup-final3-linear" \
  --gamma "$HOME/.cache/compat/camerafollowup-final3-gamma"
```

To record a new release, set `UNBOUND_PROBE_UNITY_VERSION` and use two independent
Editor processes for each color space. For full captures, run `native` for Linear
and `gamma` for Gamma with labels `native1` and `native2`. For focused captures,
set `CAMERA_MSAA_FOCUSED=1`. Pass the four capture directories to the appropriate
report script using `--native-linear`, `--repeat-linear`, `--native-gamma` and
`--repeat-gamma`, together with `--record-reference`. It requires byte-identical
repeats and refuses to overwrite an existing oracle. Hash manifests stay in the
run cache. Float and raw buffers are compressed with deterministic gzip metadata.

`regress.sh <label>` writes other probe captures to `<label>/regress`.
`check-regress.py <label>` compares those with the versioned Native references;
`CAMERA_MSAA_BEFORE` and `CAMERA_MSAA_AFTER` select existing regression sets.
`followup-metrics.py` accepts `--before-root`, `--actual-root`,
`--regress-before`, `--regress-actual` and `--out` for detailed strict failures and
changed-pixel measurements. Every comparator's status is retained in its report.
