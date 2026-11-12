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

Unity 6000.0.71f1 retains stationary and moving-onto particles at all three z bit
patterns, `3f333335`, `3f333336`, `3f333337`. Their exported remaining lifetime
is 1.663. Moving-away particles reach approximately 0.8000002 and are removed.
Scalar current size and all three 3D components are 0.4, radiusScale is 1, and
both computed query radii have bits `3e4ccccd`, or 0.2. At each retained boundary
position, all three public query APIs return false. The ignore twins give the
same exact endpoints as the corresponding retained kill twins.

The accepted runtime agrees on inputs, sizes, geometry, queries and ignore-twin
endpoints but removes those six stationary/moving-onto particles. The focused
union is 55 rows, 49 passing, 6 failing, 18 failing values, including complete
missing particles. This is a positive-life classification difference. Stationary
retention excludes a simple before-movement check as its cause; moving-away
removal also contradicts using only the initial position's classification.
Scalar versus 3D size selection does not explain these inputs, since they agree.

The responsible boundary is `ParticleSystem.Collision.cs`, `Trigger`, where the
inside flag currently comes from `Physics.OverlapSphereNonAlloc`. The probe
establishes that this public-query classification is not equivalent to Native's
particle trigger test at these values. It does not identify the exact internal
arithmetic, effective radius or general rule for other geometry. No radius
margin, snapping, shape rewrite, phase change or lifetime patch is supported by
this bounded control alone. Physical actor/query poses remain separate concerns.

The canonical reference also includes one bounded arithmetic discriminator,
bringing the total to 565 rows. Its original 55 rows are byte-identical. Six
collider-radius/particle-size pairs are `.5/.4`, `.25/.2`, `1/.8`, `.2/1`,
`.6/.2`, and `.5/0`. The first three scale the same geometry; the next two change
the radius split at the original summed radius; the last removes particle size.
Each uses axial `[0,0,1]` and diagonal `[.6,0,.8]` directions with relative
separations `-.001`, `0`, `2^-22`, `.00001`, `.0001`, `.001`, and `.05` from
the summed-radius boundary. All 84 configurations have stationary Ignore/Kill
twins and positive life. Inputs record ordinary scalar float predicates before
simulation. The separations were fixed before either Native capture.

The script writes the placement `sum * (1 + offset)` and the predicate
comparisons in double precision with one rounding to float. That is how the
Editor's scripting runtime already evaluates these float expressions (a capture
with the explicit form is byte-identical to the reference); without it, a
single-precision runtime places the particle one float step away and the rows
test that arithmetic instead of the trigger. The Editor runs in batch mode on
the GPU (`run.py native` uses `tests/editor_runner.py`).

Both independent Native captures agree byte for byte. Every tested diagonal
retains its particle, including radius `.5`, zero particle size and distance
`.525`. Every axial case at relative separation `.0001` is removed. At radius
`.5`/size `.4`, axial distance `.700006962` survives and `.700069964` is removed.
At radius `1`/size `.8`, the corresponding relative `.00001` case is removed.
This rejects a simple spherical-distance interpretation across these directions.
It does not establish the complete geometry or its internal arithmetic.

Run the recorded predicate analysis without a new capture:

```sh
python3 tests/projects/particletriggerboundary/analyze.py --unity-version 6000.0.71f1 \
  --output "$work/arithmetic-native.json"
```

Of the 84 Native classifications, squared summed-radius distance disagrees on
47, scalar distance on 41, analytic radial projection on 45, collider-normalized
squared distance on 46, public closest-point distance on 43, and expanded AABB
on 11. The analysis retains every case, predicate disagreement, input, geometry,
before/after state and Ignore twin. An expanded AABB alone is also insufficient.
No fitted margin, approximate reciprocal, snapping or bit condition is justified.

The accepted replay has 42 new retention disagreements, plus the original six.
The complete unchanged-tolerance union has 356 passing and 209 failing rows,
695 differing values, and no missing or extra recording keys. This includes
pre-simulation position-bit and public-query differences. The constructed new
positions differ by up to two float32 ULPs between runtimes in 40 of 84 distinct
configurations, which limits near-boundary cross-runtime attribution. The raw
observations and all such failures are retained rather than masked. The large
diagonal discrepancy is independently repeated in Native and exceeds this drift.

This pair did not establish a general replacement predicate. No runtime edit,
runtime build, further Native discriminator, broad gate, or installation of the
unaccepted 3D pose change followed it. The original 402-row control union remains
separate evidence. In particular, no resolution of its 20 introductions is claimed.

## Sphere predicate implementation receipt

The sphere trigger path now uses the measured padded swept bounds and sphere
sweep described in `SPHERE_MECHANISM.md`. The cached actor pose comes from the
existing native body query; sphere local geometry uses the same cached
collider-to-body matrix and shape-construction formula as `CreateShape`.
It does not read the collider's current Transform. Public query bindings and
non-sphere trigger classification are unchanged.

The matched 875 runtime replay on 2026-11-01 closes all 20 introductions from the
old cached-pose package. All 55 original boundary rows and all 150 original
histories pass. The 402-row control has 608 resolved, 21 retained and zero
introduced failing values. The remaining values belong to synced inside-kill
histories with Native zero-life particles; lifetime/removal code was not changed.

The complete 565-row strict union remains red: 144 resolved, 551 retained and
48 introduced values. These 48 paths are three exact z-bit strings for each of
16 restored particles. Each restored particle equals its pre-existing baseline
IgnoreOutside twin byte for byte, including the original construction drift.
The recorded classification agrees for all 84 arithmetic and nine boundary
configurations, but that does not waive any bit-string difference. Conditional
Physics3D and broad particle/image gates were not run because the strict
zero-introduction condition was not met. Eight focused unit cases pass.

`Sources/CrossKindProbe.cs` is a baseline/candidate equivalence control for box
and capsule triggers, with inside/outside/enter/exit Kill histories. Prepare two
private engine projects using `run.py prepare`, replace their generated
`Assets/Scripts/BoundaryProbe.cs` with that source, then run each with `run.py
engine` and its matching runtime. No Native oracle is claimed for that control.
All 96 rows were byte-identical in this replay.

Full before/candidate/Native unions, causal twins, all 33 runtime hashes, source
bindings, inherited references and commands are retained under
`~/.cache/unbound/validation/runs/sphere-trigger-implementation-20261101-t3-9132e216/`.
There were no new Native captures or tolerance changes. Root integration requires
review of the strict introductions and matching conditional gates.
