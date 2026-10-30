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
