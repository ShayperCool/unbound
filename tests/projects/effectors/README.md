# 2D effectors probe

Unity 6000.0.71f1, Linux. PlatformEffector2D and SurfaceEffector2D defaults, API limits,
native singleton limits, serialized prefabs and cloning are measured alongside motion,
contact normals, IsTouching, GetContacts and collision/trigger callbacks. Cases include
one-way passage, surface/side arcs, side friction/bounce, reflected transforms, multiple
body colliders, enable changes, trigger colliders, masks overriding the layer matrix,
and conveyor force scale/contact-force/friction/bounce settings.

```bash
EFFECTORS_PROBE_OUT=/tmp/effectors-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/effectors" -executeMethod ProbeRunner.Record \
  -logFile /tmp/effectors-editor.log
EFFECTORS_PROBE_OUT=/tmp/effectors-unbound unbound run \
  -p tests/projects/effectors --headless 30000 --no-watch
python3 tests/projects/effectors/compare.py tests/projects/effectors/Reference /tmp/effectors-unbound
```

ProbeRunner.Create recreates the empty scene and prefabs through the Editor.

Implementation is in progress. The 563 records include ten material-assignment/mutation checks.
543 match, and 20 compound-contact records match another measured native outcome or contact
order. Creating and destroying an unrelated material-test collider before the same initial
overlap changed Unity's contact order and grouped resolution. Both native outcomes are retained
in `Reference/initial-overlap-variants.json`, linked to the probe commits. Only those measured
states and permutations of the same normals are accepted; other positions, contact counts,
callbacks and values still fail. Eight comparator tests check those restrictions.

Nonzero Surface speedVariation fails explicitly until its simulation is measured.
API signatures match 2254/2254 types. Broad physics retains 8 existing differences;
Joint2D remains 1382/1401 plus 19 known rounding records, and ConstantForce remains 608/608.
