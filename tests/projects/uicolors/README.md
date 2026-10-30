# Canvas vertex colors

Unity 6000.0.71f1 Linux. GPU probes read RGBAFloat render targets in Linear storage. All256
vertex-color bytes are tested through a passthrough shader, the original UI/Default shader and
a shader exposing _UIVertexColorAlwaysGammaSpace. Root/nested/independently sorted canvases use
opposing flags. Tint/alpha, CanvasGroup inheritance, flag toggles without SetMesh, unchanged
source snapshots, explicit mesh replacement, pop draws and Canvas cloning are recorded.

The Linear oracle has36901 observations. Two recordings agree on the original9229 observations.
The original engine matches397/9229 before implementation. Runtime verification is in progress.

Use ~/.cache/compat/with-heavy-job before every Editor/build/engine run. ProbeRunner.Create
creates the scene; ProbeRunner.Record records Linear and ProbeRunner.RecordGamma switches to
Gamma then records. UI_COLORS_OUT chooses output. Restore the project with ProbeRunner.SetLinear
after Gamma comparison. Run Unbound with --frames8 --size128x128 --no-watch.

python3 tests/projects/uicolors/compare.py tests/projects/uicolors/Reference/Linear OUTPUT

Numbers compare at unchanged absolute/relative2e-4 tolerance; no exclusions. Native and engine
products stay outside authored files, except explicit Editor-compatible fixture/settings edits.
