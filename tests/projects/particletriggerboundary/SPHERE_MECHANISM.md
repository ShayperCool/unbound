# Sphere trigger classification, Unity 6000.0.71f1 Linux

Offline inspection establishes a sphere-specific call path that predicts all 84
recorded arithmetic classifications and all nine original boundary classifications.
The latter include stationary, moving-onto and moving-away controls. Native repeat
agreement and the entire existing runtime failure union remain separate checks.
No production implementation or runtime acceptance follows from this result.

## Static evidence

The installed Editor executable and its `Unity_s.debug` have matching ELF build ID
`9a4f6559dbeb9b8aa47d7c970b8858ddda4f717c`. Inspection used `nm`, `objdump` and
read-only ELF byte extraction. It never executed the Editor. Addresses below are
virtual addresses in that executable, not source line numbers. The accompanying
cache receipt contains executable/debug hashes, the exact instructions, extracted
constants, all inputs and predictions, and every counterexample to other variants.

- `TriggerModule::Update`, `0x2d39fba` through `0x2d3a060`, forms a segment from
  current position minus total velocity times the step to current position. Radius
  is maximum current size times half the trigger radius scale. Local simulation
  segments are then transformed to world space. These controls have identity
  particle transforms, as their authored source establishes.
- `PhysXRaycast::FindParticleOverlaps`, `0x2390d70` through `0x2390e4b`, obtains
  cached shape world bounds through command `0x10000011` and adds float32
  `0x3727c5ac`, exactly the representation of `1e-5`, to all three collider extents.
  `ShapeGetWorldBounds`, `0x287c3f0`, calls `PxGeometryQuery::getWorldBounds` with
  inflation `1`. This is collider-bounds padding, not a measured size adjustment.
- `0x2391010` through `0x23910ae` constructs the swept particle AABB using the
  half segment, its midpoint and its absolute half segment plus particle radius.
  `ParticleOverlap`, `0x238e9fa`, calls `IntersectAABBAABBInclusive` against the
  padded collider bounds. `IRaycast::IntersectAny`, `0x2029a80`, also performs
  center/extents SIMD comparisons as an earlier batch rejection.
- `ParticleOverlap`, `0x238ead4`, dispatches command `0x1400000a`.
  `RegisterQueryBindings`, `0x287b884` through `0x287b8a0`, binds query index 10
  to `SweepAgainstShape`, `0x287a500`. That function calls
  `PxGeometryQuery::sweep` at `0x287a8a4` with zero additional inflation.
- The sphere dispatch constructs a capsule with coincident endpoints. See
  `native/physx/source/geomutils/src/GuGeometryQuery.cpp`,
  `GuSweepSharedTests.cpp` and `sweep/GuSweepSphereCapsule.cpp`. It reaches
  `sweepSphereSphere` for a spherical target.
- In `sweep/GuSweepSphereSphere.cpp`, `sphereSphereSweep` returns true when
  `c <= 0` **or `a == 0`**, where `a` is squared relative motion. Thus zero-motion
  sphere sweeps return true even for geometrically separated spheres. The installed
  implementation at `0x4fc61ea` through `0x4fc629e` confirms both branches.
  This is actual control flow, not a hypothesis about an uninitialized mask.

## Finite predicate

For the measured identity-space sphere controls, let `f` round each operation to
float32, `p` be the recorded endpoint, `v` recorded total velocity, `dt` the
recorded step, `C/E` cached collider bounds center/extents, and
`r = f(maximumCurrentSize * f(radiusScale * 0.5))`. Construct each axis in this order:

```text
start = f(p - f(v * dt))
half = f(f(p - start) * 0.5)
midpoint = f(start + half)
particleExtent = f(abs(half) + r)
colliderExtent = f(E + float32_from_bits(0x3727c5ac))
broadphase = all(abs(f(midpoint - C)) <= f(particleExtent + colliderExtent))
inside = broadphase && PhysX_sphere_sweep(start, p, C, colliderRadius, r)
```

The sweep uses the source quadratic, including its `a == 0` branch. The offline
analyzer retains its arithmetic and branch reason for every case. For stationary
controls, the predicate reduces to padded AABB overlap because that branch returns
true. Moving-onto particles start inside the summed sphere radii. Moving-away
particles start outside and move outward, so the actual sweep rejects them.

The endpoint-only padded AABB diagnostic also predicts this dataset, but static
inspection establishes a swept path. The observations alone do not distinguish
those predicates for other trajectories. No new discriminator is needed to
identify the path here, and none was prepared or run.

## What this resolves

All 11 failures of the previous expanded-AABB model lie on positive world Z, with
zero recorded collider/world translation. Their positive endpoint gaps range from
`8.940696716308594e-8` to `6.973743438720703e-6`. Switching between center/extents,
min/max endpoints, reconstructed bounds, or clamp arithmetic does not resolve
them. PhysX four-bit broadphase quantization still leaves five failures. The
literal padding and the zero-motion sweep branch explain the observations across
all radius splits, both directions, and the zero-size family without fitted
coefficients, position-construction replay or key-based predicate exceptions.

All 42 diagonal particles survive because their per-axis coordinates pass the
bounds check and their zero-motion sweep returns true. For example, the zero-size
particle at `[0.315, 0, 0.42]` is outside the radius-0.5 sphere but inside its AABB.
The size-0.8/radius-1 axial point at `1.40001392` fails the bounds padding, while
the size-0.4/radius-0.5 point at `0.700006962` passes. This is an absolute bounds
padding constant read from the actual call path, not a radial or proportional fit.

The original 55 rows provide nine independent control configurations. Their
before/after/twin histories, all public query results and safe particle fields
remain intact in the report. The analyzer infers only the inside classification
from positive-life KillOutside retention, with enter/exit actions Ignore. It makes
no inference about previous-trigger flags, event lists or death visibility.

## Portable replay and limits

Run from a checkout containing this script and the existing particle comparator:

```bash
python3 tests/projects/particletriggerboundary/analyze_intervals.py \
  --unity-version 6000.0.71f1 \
  --recording /path/to/recorded-native-a.jsonl \
  --repeat /path/to/recorded-native-b.jsonl \
  --actual /path/to/recorded-accepted.jsonl \
  --output /path/to/cache/predictions.json
```

Omit `--recording` to use the existing canonical for the explicitly selected
release. No canonical is added by this change. The analyzer never creates a Unity
project or runs a build, Editor or engine. It requires cache output, preserves
exact repeat differences and the full raw cross-runtime union, and invokes the
original comparator without changing its tolerances. An exit of zero means that
the input Native pair is exact; it does not mean the accepted engine passes.

The existing accepted engine still has all 695 strict differing values on 565
rows, including actual input construction differences up to two ULP. The Native
model uses each runtime's recorded inputs independently and does not waive those
differences. The prior 402-row union, its 20 introduced values and all changed
retained values are preserved unchanged. A separate offline receipt maps the
original outside witnesses to the source-derived predicate without rewriting slots
or projecting lifetime/removal behavior.

This establishes the measured sphere path for this release. Other shapes,
nonidentity simulation transforms, disabled colliders and generalized event
behavior are outside the evidence. Runtime implementation requires Root's separate
authorization and validation. Use actual cached shape/body pose data when needed:
`Collider.m_ShapeMatrix` is collider-to-body, not the world pose. This research
changes no collider pose, API, simulation, trigger action or previous-trigger flag.
