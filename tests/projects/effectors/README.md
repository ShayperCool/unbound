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

Surface variation now matches Unity's private seed-zero xorshift128 generator. It draws on
every solved contact, including zero variation, preserves its state across enable changes,
and restarts on cloning. The signed variation multiplies a sample in [0,1]; global Random
state is untouched. The expanded 684 records include 64 consecutive samples, lifecycle arrays,
positive/negative variation, rotated/mirrored/curved conveyors and inactive material assignment.
674 records match at the original tolerance, with 10 known native contact-order records and
zero unexplained differences. Area, Point and Buoyancy remain to be implemented.

API signatures match 2254/2254 types. Broad physics retains 8 existing differences;
Joint2D remains 1382/1401 plus 19 known rounding records, and ConstantForce remains 608/608.

The volume fixture adds 1365 Editor records for Area, Point and Buoyancy defaults, limits,
serialized assets, clones and 45 motion cases each. It measures post-integration force and
damping, force selection, attenuation, compound bodies, partial immersion, masks, triggers,
non-trigger contacts, dynamic enable/simulation changes and private variation sequences.
The expanded reference contains 2049 records; volume runtime implementation is in progress.

Extra volume cases separate angular and linear drag, full immersion, zero gravity scale and
different masses. Inertia and world center of mass are recorded with each volume state.

Buoyancy angular drag is skipped when the drag torque magnitude is at most the configured
angular sleep tolerance converted to radians. Low-spin tests vary drag, mass, immersion and
angularSleepTolerance to distinguish this torque cutoff from a velocity cutoff.
A circular non-trigger contact avoids the ambiguous face selection of an initially overlapping
box. Compound Point contacts also have measured native first/last-collider alternatives,
retained as complete states rather than relaxed numerical tolerances.

The shape/low-force extension increases the reference to 2559 records. It covers triangular
colliders with overridden mass centers, horizontal/rotated/degenerate capsules, edges, scaled
fluid levels with mirrored Y, and low linear forces while varying linearSleepTolerance.
Unity clips a capsule's oriented bounding rectangle for buoyancy. Collider force selection
uses bounds centers, with Area combining the body's collider bounds. Low fluid force is
compared after buoyancy, flow and linear drag are combined, while angular drag is tested
separately. Engine checks of the extension are in progress.
