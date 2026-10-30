# Particle vertex streams and trails

Unity 6000.0.71f1, Linux. The fixture records 3544 observations and three GPU images. The engine
matches every observation at the original absolute/relative tolerance of 2e-4, and all three
images are byte-identical. No tolerance was widened.

```bash
PARTICLE_STREAMS_OUT=/tmp/streams-editor Unity -batchmode \
  -projectPath "$PWD/tests/projects/particlestreams" -executeMethod ProbeRunner.Record \
  -logFile /tmp/streams-editor.log
PARTICLE_STREAMS_OUT=/tmp/streams-engine unbound run \
  -p tests/projects/particlestreams --frames 8 --size 320x180 --no-watch
python3 tests/projects/particlestreams/compare.py tests/projects/particlestreams/Reference /tmp/streams-engine
```

Use the shared `~/.cache/compat/with-heavy-job` wrapper for each Editor, build or engine run.
The Editor needs a display. ProbeRunner.Create creates the scene and native prefab.

Main stream coverage includes every non-trail declaration, packed combinations, attribute
formats/dimensions, geometry, normals, tangents, colors, all eight UV channels and indices.
Cases exercise authored mesh UV0/UV1 and tangents, texture-sheet interpolation, 3D rotation,
normalDirection, nonsquare sizes, all 256 color bytes, stable/varying random vectors, manual
Custom1/2 data and a shader reading packed custom values. Lifecycle cases load and clone native
prefab declarations, reuse an explicit-layout mesh, sort distinct custom data, and measure all
BakeMesh flags in World/Local/Custom spaces under reflected/nonuniform scale.

The 29 trail cases measure default/explicit lighting and streams, point distance thresholds,
coincident live/sample heads, exact tail expiry, orphan particle retention and zero lifetime,
clamped lifetime setters, size-dependent lifetime, scalar/3D width, both render-mode width
rules, colors, all texture modes, ribbons, actual single/multiple-ribbon GPU draws and every bake flag for local/world trail storage.
Packed neighbor centers, percentage and width are checked alongside actual geometry. Zigzags
exercise sequential short-segment smoothing and miter caps. Independent Editor recordings
agree on all 3364 observations preceding the last three 3D/short-segment cases.

Native exceptions are preserved explicitly:

- Layouts with no UV-packed stream expose uninitialized BakeMesh vertex buffers, observed in
  three Editor runs. Only their stream lists and declared attributes are recorded. Undefined
  heap contents are neither saved nor compared.
- Unity 6000.0 rejects indices when baking multiple ribbon strips, with an out-of-bounds-index
  error. The fixture records its empty index/mesh result; the engine's compatibility profile
  reproduces that bake failure. Regular ribbon rendering retains valid separate strips.

Active noise streams remain under work. GetTrails/SetTrails and texture baking are separate
open API gaps. The broad particle probe retains 1488/1560 matching records in nine known
scenarios; see tests/projects/particles/README.md. Four existing ParticleSystemTests failures
also reproduce from the parent HEAD before these trail changes.

Final evidence for this stage is under ~/.cache/compat/: particle-trails-unity5,
particle-trails-unbound6, particle-trails-build7.log, particle-trails-api.log,
particle-trails-regression2*, particle-trails-tests*.log and particle-tests-baseline.log.
