# Canvas vertex colors

Unity 6000.0.71f1, Linux. Each Linear/Gamma reference has 49201 observations, covering all256
vertex-color bytes through a passthrough shader, the original UI/Default shader and a shader
exposing _UIVertexColorAlwaysGammaSpace. Engine matches all98402 observations at absolute/relative
2e-4 tolerance. Two independent Native recordings agree on all49201 values in each color space.
The comparator has no exceptions. Readback uses RGBAFloat with Linear render-target storage.

World Space and Screen Space Camera cases cover root/nested/independently sorted canvases with
opposing flags, renderer tint/alpha, CanvasGroup inheritance, flag toggles without SetMesh,
source snapshots, explicit mesh replacement, pop draws and Canvas cloning. Live state changes
check cache invalidation and expose GetAlpha/GetColor consistency.

## Reproduce

Run every Editor/build/engine invocation through ~/.cache/compat/with-heavy-job. Build with
--disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false. The Editor needs a GPU
display. UI_COLORS_OUT selects the destination directory.

```sh
Unity -batchmode -quit -projectPath "$PWD/tests/projects/uicolors" -executeMethod ProbeRunner.Create
UI_COLORS_OUT=/tmp/ui-linear Unity -batchmode -projectPath "$PWD/tests/projects/uicolors" -executeMethod ProbeRunner.Record
UI_COLORS_OUT=/tmp/ui-engine unbound run -p tests/projects/uicolors --frames 8 --size 128x128 --no-watch
python3 tests/projects/uicolors/compare.py tests/projects/uicolors/Reference/Linear /tmp/ui-engine
```

The committed project is Linear. For Gamma, ProbeRunner.RecordGamma changes the project setting
and records. Run Unbound before restoring the project, then compare Reference/Gamma. Restore
with ProbeRunner.SetLinear or an explicit serialized m_ActiveColorSpace=1 edit after both runs
have exited. ProbeRunner.Record itself preserves the current project color space.

## Measured behavior

Canvas batches renderer tint and inherited alpha into vertex bytes using clamp,*255,+.5,truncation.
In Linear with flag false, both input vertex RGB and renderer RGB convert to scalar linear values
before multiplication. With flag true they retain gamma values; Canvas binds integer flag1 and
UI/Default applies its original shader conversion. Custom shaders can ignore the flag. Gamma
projects keep gamma RGB regardless of flag. Alpha never converts through a gamma curve.

The nearest Canvas owns color settings even when it does not override sorting. Source geometry
remains unchanged by batching. SetMesh copies a snapshot; mutating the supplied mesh has no effect
until SetMesh is called again. SetAlpha updates the alpha returned by GetColor; SetColor updates
GetAlpha. Instantiate preserves the Canvas gamma flag.

The original implementation, using independently built pre-fix managed binaries with the final
explicit world poses, matched4651/36901 observations. The repaired implementation matches both
full references. World poses are set explicitly to isolate color batching: creation-time driven
Canvas transform scheduling differs and is a separate pending parity item. Overlay screenshots,
HDR/float vertex attributes and additional material/texture tint combinations remain unmeasured.

## Evidence

Under ~/.cache/compat/: uicolors-unity5/6 for Linear, uicolors-gamma-unity3/4 for Gamma;
uicolors-after3 and uicolors-gamma-after2 for engine; uicolors-before2 for the pre-fix build.
uicolors-build1.log has zero errors, uicolors-api.log checks2266 types with zero errors, and
uicolors-tests1.log has261/261 Canvas/UIElements/Yoga tests passing. The uitk record, all11
interaction steps and all three PNGs match the independently built earlier implementation
exactly, in uicolors-uitk versus ui-transitions/uitk-baseline.
