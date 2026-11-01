# Texture2D generic raw data

A CPU-only witness for `GetRawTextureData<T>`, kept separate from the
non-generic copy API and the per-mip `GetPixelData<T>` API.

The 4x2 textures have three explicitly filled mip levels. Formats are R8,
RGB24, RGBA32, RGB565, R16, RHalf, RGFloat and RGBAFloat. The witness records
whole-chain byte contents and lengths for ushort, float and a packed three-byte
struct. It keeps raw and pixel views alive while writing through both APIs and
`SetPixels`, and reads `GetPixels` after raw writes. It also records old-view
access after reinitialization, removal of CPU data and destruction. Unreadable
access through the generic, non-generic and pixel APIs is recorded separately.

Existing contracts for byte/packed float conversion and per-mip data are in
`../texturepixels/Reference/unity-6000.0.71f1/`. The non-generic unreadable copy
control is also covered by `../resources/Assets/Scripts/ResProbe.Mips.cs` and
that probe's canonical references. This witness does not redefine those APIs.

Run commands from the repository root. The runner calls the installed heavy-job
launcher for each child; do not wrap it again.

```sh
python3 tests/projects/textureraw/run.py native --release 6000.0.71f1 --label paired-cpu
python3 tests/projects/textureraw/run.py publish --release 6000.0.71f1 --label paired-cpu
python3 tests/projects/textureraw/managed.py --release 6000.0.71f1 --label managed-cpu
python3 tests/projects/textureraw/run.py engine --release 6000.0.71f1 --label runtime-cpu \
  --runtime /path/to/matching/runtime --manifest /path/to/runtime-manifest.json
python3 tests/projects/textureraw/compare.py \
  tests/projects/textureraw/Reference/unity-6000.0.71f1.jsonl \
  ~/.cache/unbound/validation/runs/textureraw/runtime-cpu/engine.jsonl \
  --report ~/.cache/unbound/validation/runs/textureraw/runtime-cpu/complete-union.json
```

`publish` requires two complete, identical independent captures and successful
cases. It publishes one reference per release. Comparison is exact, including
encoded pixel float bits and exception types. Every missing, extra and failing
key is retained. Repeats, copied projects, logs, manifests and comparison outputs
stay under `$XDG_CACHE_HOME/unbound/validation/runs/textureraw/`.

`managed/TextureRaw.Tests` runs the same witness against the release reference
without a native runtime. It also checks pointer identity regardless of which
getter enters storage first, typed writes, allocation-free repeated getters,
borrowed disposal, job conflicts, and owner-driven job completion before unpin.
Before Root regenerates the declared stub, the focused project can build with
`-p:UnboundNoStubs=true`. Use a private `UnboundTarget` and disable build servers,
shared compilation and node reuse. A successful managed run does not establish
acceptance of a combined runtime. Never pair a new v15 CoreModule with a v14 CLI.

## Measured lifetime differences

The 207-value release reference records `UnityException` from both generic raw
and pixel getters after `Apply(false, true)`, while the non-generic getter still
returns the twelve-byte chain. Reinitialization and destruction invalidate old
raw and pixel views with `ObjectDisposedException`.

After `Apply(false, true)`, Native's old raw and pixel safety handles still permit
reads. The witness checks those handles without dereferencing the removed CPU
data. Unbound's existing store releases and invalidates these handles, and its
pixel getter currently throws `ArgumentException` for unreadable textures. The
new raw getter preserves owner-driven invalidation and throws `UnityException`.
The exact comparator and managed witness retain all three disagreements. They
must be resolved by the storage/API owner before declaring full parity; this
task does not loosen lifetime checks or alter the existing pixel getter.
