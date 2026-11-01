# Positive-life particle trigger boundary

This control isolates the original outside-kill residual at z=0.700000167,
remaining lifetime 1.663, size 0.4 and velocity [0,0,2]. The original sphere's
physics center was [0,0,0], despite its unsynchronized Transform at z=2.5.
Here a default primitive sphere is explicitly synchronized at the measured center.
The accepted runtime can therefore test the same geometry without the unaccepted
contact-property pose changes.

Three f32 inputs are the measured z and its immediate predecessor/successor.
Each is stationary, moving onto that z, or moving away from it, with paired
outside-ignore/outside-kill systems. One 0.05 step follows a zero-time restart
and injection of one particle with positive remaining lifetime 1.713 and start
lifetime 2.013. Emission stays enabled at the original rate 10; the full output
buffer is recorded. The original seed, shape, main and trigger settings are kept.
The stationary pair changes only velocity. Enter and exit remain Ignore.

The records include input and exported fields, scalar and 3D current sizes,
computed radii, exact z/radius bit patterns, sphere geometry/bounds, and public
allocating/nonallocating overlap and CheckSphere results at identical positions.
The ignore twin exposes the actual endpoint if the kill twin removes a particle.
No mesh-index getter is used.

From the repository root, choose a work directory under the Unbound cache and a
new label for each independent capture:

```sh
python3 tests/projects/particletriggerboundary/run.py prepare --unity-version 6000.0.71f1 --work "$work" --label native-a
python3 tests/projects/particletriggerboundary/run.py native --unity-version 6000.0.71f1 --work "$work" --label native-a
```

The runner admits each actual capture through installed `with-heavy-job` once.
Use another prepared label with mode `engine`, `--cli` and `--runtime` pointing
to a private coherent accepted runtime. It performs no production build.
Publish one release reference only after two independent Native captures agree
byte for byte. Verify without changing original particle tolerances:

```sh
python3 tests/projects/particletriggerboundary/verify.py --unity-version 6000.0.71f1 \
  --native-a "$work/native-a.jsonl" --native-b "$work/native-b.jsonl" \
  --actual "$work/accepted.jsonl" --output "$work/full-union.json"
```

The report retains every missing/extra row, particle slot and failing value.
The existing particletrigger 402-row history and broad original 150 observations
are separate evidence and are not replaced, filtered or replayed by this control.
