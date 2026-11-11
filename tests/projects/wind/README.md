# Wind probe

How wind zones move particles through the External Forces module: `WindProbe` records the
WindZone's defaults, then for each scenario a particle system (no gravity, no emission) with five
particles at rest and one wind zone, simulated in five 0.1 s steps: velocities and positions after
each step. Scenarios cover directional zones (strength, rotation, distance, the module's
multiplier, disabled module, the List influence filter, a local-space system), spherical zones,
turbulence and pulsing. Finally the pulse over ten seconds for three magnitude/frequency pairs.

What it measured: a directional zone accelerates along its forward axis by windMain × pulse at any
distance; a spherical one along the direction away from its center by
windMain × pulse × (1 − d²/r²), nothing past its radius; turbulence moves no particles; wind zones
pass the influence filter by layer only (a List filter gives no wind); the pulse is
1 + magnitude × 0.333 × (cos(πx) + cos(3π/8 x) + cos(π/20 x)) with x = frequency × Time.time.

The probe sets `Time.captureDeltaTime = 0.01f` before scene loading. As documented
for [Unity 6000.0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-captureDeltaTime.html),
scaled time then advances by the capture interval regardless of frame duration. This
makes the two pulsing scenarios and the three pulse sample series repeatable.

Record and publish two independent windowed GPU Editor captures on the existing display:

```
python3 tests/projects/wind/run.py editor --release 6000.0.71f1 --label <fresh>
python3 tests/projects/wind/run.py publish --release 6000.0.71f1 --label <fresh>
python3 tools/agents/suite.py --only wind
```

`run.py` takes one launcher lease per Editor, verifies the NVIDIA renderer and normal
exit, checks the 18-record shape and requires byte-identical repeats before publication.
It does not create an X server. Captures and logs stay under the validation cache.
The canonical reference is `Reference/unity-6000.0.71f1/wind.jsonl`.

The fixed-clock re-record changes exactly rows 14-18: scenarios `pulse` and `pulse fast`,
then the three `pulse` rows at frequencies 0.25, 1 and 0.05. Their phases and sampling
clocks previously depended on wall time. Rows 1-13 are byte-identical. The comparator
and both tolerances are unchanged. Failing records decrease from 2/18 to 0/18 in this
fixture; this does not establish wind parity outside these cases.
