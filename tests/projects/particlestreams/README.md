# Particle vertex streams

Unity 6000.0.71f1, Linux. BakeMesh records every non-trail stream individually and in packed
combinations: attribute formats/dimensions, geometry, normals, tangents, colors, all eight UV
channels and indices. A procedural mesh has authored UV0/UV1 and tangents. Billboard cases
include texture-sheet interpolation, 3D rotation, normalDirection and nonsquare sizes. A seed
sweep records stable/varying random vectors. Particle data is set explicitly and paused.

```bash
PARTICLE_STREAMS_OUT=/tmp/streams-editor Unity -batchmode \
  -projectPath "$PWD/tests/projects/particlestreams" -executeMethod ProbeRunner.Record \
  -logFile /tmp/streams-editor.log
PARTICLE_STREAMS_OUT=/tmp/streams-engine unbound run \
  -p tests/projects/particlestreams --frames 8 --size 320x180 --no-watch
python3 tests/projects/particlestreams/compare.py tests/projects/particlestreams/Reference /tmp/streams-engine
```

The Editor needs a display. ProbeRunner.Create creates the empty scene. The 953-record
reference measures the current gap; engine implementation is in progress. No random values,
missing attributes or stream layouts are excluded from comparison.

The extension adds all 256 input color bytes, age-dependent random vectors and an actual GPU
shader reading packed Custom1/2 across multiple UV channels. The reference has 968 records and
one image. Unity's layouts without any UV-packed stream produce uninitialized BakeMesh vertex
buffers, observed in three Editor runs; their stream lists and declared attributes are checked,
while undefined geometry contents are neither saved nor compared. All usable geometry and
random streams retain the original tolerance.

The initial implementation matches all 968 records at the original tolerance. Its custom-data
GPU image is byte-identical. Broad particle regression remains 1488/1560 records, 9/94 known
differing scenarios and worst image mean 0.93. Active noise exports, custom trail streams,
transformed/multiple particles and further lifecycle/API coverage are still in progress.

The lifecycle/transform extension contains 1420 records. It loads and clones a native prefab
with custom main/trail stream declarations, reuses a mesh with an existing explicit layout,
sorts two particles with distinct custom data and records all BakeMesh flag combinations in
World/Local/Custom simulation spaces under reflected/nonuniform scale. This stage is under
engine verification. The single custom-stream image remains byte-identical across Editor runs.

All 1420 lifecycle/transform records now match. The GPU image remains byte-identical. Bake flags
select simulation-pose parts, while World systems ignore them; emitter scale acts after billboard
particle rotation, and Size streams retain particle units. Prefab trail declarations load and
clone correctly. Active noise and trail geometry are the remaining stream work.

Trail coverage expands the Editor reference to 2860 records. Eighteen cases measure default
and explicit lighting/stream layouts, point sampling, distance thresholds, partial tail expiry,
size-dependent lifetime/width, colors, all texture modes, orphan trails, ribbons and every bake
flag for local/world trail storage under reflected scale. Packed neighbor centers, percentage
and width are recorded with the actual baked geometry. Trail runtime implementation is ongoing.
