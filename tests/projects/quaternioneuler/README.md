# Euler factories and vector rotation

This probe records both `Quaternion.Euler` overloads without renaming or
normalizing their outputs. It retains each component, the normalized quaternion,
the squared norm and rotation of four vectors. Cases include tilted and mixed
angles, negative angles, neighbourhoods of zero, 90, 180 and 360 degrees,
arbitrary values and large numbers of complete turns. Independent vector controls
use raw unit, nonunit, zero, small and large quaternions with axis, mixed, zero
and differently scaled vectors. Raw inputs pass directly to the public operator.

The release-named canonical reference is published after two independent Unity
Editor recordings agree exactly. The comparator preserves the full missing,
extra and failing-field union, including quaternion signs and indexed vectors.
All captures, projects, input hashes and reports stay in cache.

```sh
WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/euler-local"
python3 tests/projects/quaternioneuler/run.py prepare --release 6000.0.71f1 --work "$WORK" --label native1
python3 tests/projects/quaternioneuler/run.py native --release 6000.0.71f1 --work "$WORK" --label native1
python3 tests/projects/quaternioneuler/run.py prepare --release 6000.0.71f1 --work "$WORK" --label native2
python3 tests/projects/quaternioneuler/run.py native --release 6000.0.71f1 --work "$WORK" --label native2
python3 tests/projects/quaternioneuler/run.py compare --release 6000.0.71f1 --work "$WORK" --label agreement --reference "$WORK/native1.json" --candidate "$WORK/native2.json" --output "$WORK/agreement.json"
```

The driver wraps each Editor or engine process with the shared job pool. Do not
wrap the driver again. Engine mode selects a prepared `--project` and a private
`--runtime`. Compare the replay using `--baseline` to retain every resolved,
retained and introduced value with exact numeric comparison.

## Dot, normalization and dependent APIs

The Unity 6000.0.71f1 reference adds 163 scenarios to the original 126, for 7300
values. Every original row is retained. Two independent GPU recordings agree
byte for byte. The comparator still uses exact comparisons and retains the one
original non-unit vector-rotation residual, `raw/9/5 result.z`.

The new controls store float bits and nine-digit round-trip text. Fixed component
inputs isolate `Dot`, static/instance `Normalize`, `normalized`, equality and
`Angle` from the precision of rotation factories. They include 46 rotations,
72 pairs around the equality threshold, adjacent threshold floats, opposite-sign
representations, angles near zero and 180 degrees, non-unit inputs, zero, signed
zero, subnormal inputs, underflow, overflow, NaN and infinity. `RotateTowards`
controls distinguish returning the target at zero angle from taking a zero step.
`normalized` controls retain the original input before instance normalization.
Lerp controls and axis-conversion controls distinguish the arithmetic of those
APIs from public `Normalize`, including the axis conversion's small-input cutoff.

The measured script rule is a left-associated double sum of four double products,
rounded once to float. Normalize rounds its length to float before division.
Equality uses the rounded public Dot result and a strict comparison with the
float `0.999999f`; the float at that boundary is unequal. Angle takes the absolute
dot, clamps at one and uses the same equality threshold. NaN dot produces zero
Angle in this release. Lerp keeps its single-precision sum and normalization.
Axis conversion also uses single-precision normalization, with a `1e-5` magnitude
cutoff, then float `sqrt(1-w*w)` and reciprocal multiplication for the axis.
These are observations of this release, not additional documented guarantees.

The Unity 6000.0 contract and limitations are recorded at:

- [Dot](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion.Dot.html):
  returns the dot product of two quaternions.
- [Normalize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion.Normalize.html)
  and [normalized](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion-normalized.html):
  preserve orientation and produce unit magnitude; too-small input becomes
  identity. The property leaves the input unchanged, while the instance method
  changes it. Zero, subnormal, non-unit and before/after controls distinguish these.
- [Equality](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion-operator_eq.html):
  tests dot close to one and may reject equivalent rotations. Positive and
  negative representations and both sides of the threshold distinguish this.
- [Angle](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion.Angle.html):
  returns an angle in degrees between zero and 180. Controls include both ends
  and opposite-sign representations of the same rotation.
- [Lerp](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion.Lerp.html):
  clamps the parameter and normalizes its result. Endpoint controls here use
  non-unit inputs so normalization changes the result.
- [RotateTowards](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion.RotateTowards.html):
  steps towards the target without overshooting. The zero-step controls here
  distinguish the equal-angle endpoint rule; other interpolation cases remain
  covered by the existing math tests.
- [ToAngleAxis](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion.ToAngleAxis.html):
  converts to an axis and an angle in degrees. Raw component and cutoff controls
  retain the measured behavior for inputs outside the usual unit assumption.
- [Rotation manual](https://docs.unity3d.com/6000.0/Documentation/Manual/QuaternionAndEulerRotationsInUnity.html):
  describes quaternion rotations and their limitations. The edge controls above
  are measured behavior for raw inputs, rather than promises for valid rotations.

## Engine caller audit

`Math/QuaternionMath.cs` retains the engine's previous arithmetic. All entries
below keep that arithmetic; no engine caller is switched to the public wider
sum. The listed probes check existing content paths, not every possible input
or branch. The precision choice at each unmeasured threshold remains open.
Paths are relative to `managed/UnityEngine`.

| Caller | Operations retained | Existing regression coverage; unmeasured scope |
|---|---|---|
| `AI/NavMeshAgent.cs` | Angle, RotateTowards | navmesh, navlinks, navpath; exact rounding/angle thresholds unmeasured |
| `AI/NavMeshObstacle.cs` | equality, Dot | navmesh, navbaked; carving equality thresholds unmeasured |
| `Animation/AnimationPose.cs` | Dot sign | animator, animatoradditive, playables; cancellation sign boundary unmeasured |
| `Animation/AnimatorPlayer.RootMotion.cs` | Dot, Normalize, axis conversion | animator, playables, rootcurves, animculling; exact arithmetic across all motion/target-match inputs unmeasured |
| `Animation/Constraints/ConstraintData.Evaluation.cs` | Dot, Normalize | constraints; cancellation/normalization boundaries unmeasured; Lerp already retains its arithmetic |
| `Animation/Playables/AnimationJobs.cs` | Normalize | playables; raw non-unit stream normalization unmeasured |
| `Animation/Playables/AnimationOutputs.cs` | Dot, axis conversion | playables, animculling; quaternion cancellation/axis thresholds unmeasured |
| `Animation/Playables/AnimationPlayables.cs` | Normalize, equality | playables, animculling; initial-pose normalization and offset equality boundaries unmeasured |
| `Core/Transform.cs` | Normalize | physics, playables, animculling; arbitrary raw input normalization unmeasured |
| `Math/Random.cs` | Normalize | managed RandomTests; exact normalization arithmetic of generated rotations unmeasured |
| `Physics/PhysicsQueries.cs` | Normalize | physics, rayhits, physicsbatch; arbitrary raw box orientation normalization unmeasured |
| `Physics/PhysicsWorld.cs` | Dot | physics, contactevents3d, joints; pose-sync thresholds unmeasured |
| `Physics/Rigidbody.cs` | Normalize | physics, physicsdefaults, joints; raw rotation assignment/MoveRotation normalization unmeasured |
| `Text/TextCoreText/TextGenerator.Layout.cs` | equality | textcore; rotated text equality threshold unmeasured |
| `UI/CanvasSystem.cs` | Normalize | uirectclipping; arbitrary parented canvas rotation normalization unmeasured |
| `UIElements/Core/VisualElementLayout.cs` | axis conversion | uitransitions; raw rotation-to-style conversion unmeasured |
| `Math/Quaternion.cs` | Lerp Dot/Normalize and script axis-conversion Normalize | new result-normalization/raw axis controls; other Lerp interpolation and extreme axis inputs remain unmeasured |

Other releases keep the previous unmeasured arithmetic through Compat. The
engine's existing, separate rotation mechanisms are unchanged. This audit does
not establish that every engine path must use single precision in every release.
