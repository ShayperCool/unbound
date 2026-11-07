# particletrails

What a particle system with a Trails module reports and draws while its particles die, in Unity
6000.0.71f1. Systems built in code are stepped with `Simulate` (0.05 s per call); after every call
the probe records the state (`particleCount`, `IsAlive`, `isPlaying`, `isEmitting`, `isStopped`,
`isPaused`, `time`), every particle `GetParticles` returns (position, velocity, remaining and start
lifetime, current size and alpha, seed) and the trail mesh `BakeTrailsMesh` builds (positions and
alpha). Scenarios: `dieWithParticles` false and true, PerParticle and Ribbon trails, local and
world trail space, world simulation space and a moving emitter, a trail lifetime range with a
ratio and a larger vertex distance, trail lifetime by size, a Death sub-emitter, collision kills,
`Stop`, `Clear`, `SetParticles` (written back as read, and a living particle written at zero
lifetime), a four-particle cap, a looping system, a restart, the trail ratio over 600 particles,
two systems played over frames with a stop callback, and images of fading trails.

    python3 tests/projects/particletrails/run.py native --label native1   # Editor capture (GPU, job pool)
    python3 tests/projects/particletrails/run.py publish native1 native2  # two identical captures
    python3 tests/projects/particletrails/run.py check                    # Unbound run + compare (the gate)

The reference is `Reference/unity-6000.0.71f1/` (`values.jsonl`, 15 images); both captures were
identical, values and image bytes. Values compare at the particle probes' bound (0.0002 absolute
plus 0.0002 relative); images go through `particles/compare_images.py` (report only). Run output
stays under `$XDG_CACHE_HOME/unbound/validation/runs/particletrails/`.

## Documented contract (Unity 6000.0 Scripting API)

| Source | What it says |
| --- | --- |
| [TrailModule.dieWithParticles](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.TrailModule-dieWithParticles.html) | "Specifies whether trails disappear immediately when their owning particle dies. When false, each trail persists until all its points have naturally expired, based on its lifetime." Nothing about what the API reports meanwhile. |
| [TrailModule.ratio](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.TrailModule-ratio.html) | "Choose what proportion of particles receive a trail", 0 none, 1 every particle. No selection rule. |
| [ParticleSystemTrailMode.Ribbon](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemTrailMode.Ribbon.html) | "Draws a line between each particle, connecting the youngest particle to the oldest." |
| [ParticleSystem.SetParticles](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.SetParticles.html) | A particle written with a negative lifetime is removed. Nothing about its trail. |

Everything below is measured in this release, not documented.

## Measured behaviour

| Records | 6000.0.71f1 |
| --- | --- |
| `pp/local/die0`, `pp/localmove`, `pp/worldsim`, `pp/stop`, `pp/clear`, `pp/restart` | A particle whose trail outlives it stays in the buffer: `particleCount` and `GetParticles` include it at zero remaining lifetime, at rest (zero velocity), at its death position. It leaves in the first update that starts with its trail spent (all points expired), with that update's dead, in the four-slot death order. `Stop`/`Clear`/restart remove it like any particle. |
| `img/die0_*` | It is not drawn; its trail is, fading as before. |
| `pp/local/die1`, `pp/*/die0` meshes | Trails are baked and drawn in the order of their particles: a death that moves the last particle into a freed slot moves its trail along. |
| `pp/maxp` | It holds its slot against `maxParticles`. |
| `pp/ratio`, `pp/ratiobig/*` | The ratio selects a particle when a seeded draw is below it (one draw per particle seed; the 0.25 selection is a subset of the 0.5 one); particles without a trail leave at death. |
| `pp/setparticles`, `pp/killlife` | `SetParticles` removes a particle written at zero or negative lifetime, a carrier included, with its trail at once, whatever `dieWithParticles` says: the trail does not fade. |
| `pp/collision` | A particle killed by a collision (lifetime loss) shows zero lifetime and its bounced velocity for one update; the next update makes it a carrier at rest where it was. |
| `pp/deathsub` | Death sub-emitters fire when the particle dies, not when its trail ends (same with `dieWithParticles` true). |
| `ribbon/*` | Ribbons keep no dead particles; `dieWithParticles` changes nothing. |
| `played/*` | Played over frames the same holds; the stop callback comes when the last carrier leaves. |

## Known differences (`known.json`)

Listed by exact key and class with its reason; the comparator prints each as KNOWN and fails on
any other difference. None is caused by the death handling above; each is also seen with
`dieWithParticles` on or while every particle lives, except `world-trail-death-head`:

- `newborn-trail-quad`: a newborn trail whose points still coincide gets its first quad across
  another direction in Unity (images `die1_9`, `world_9`).
- `death-child-age`: the separate Death sub-emitter child age residual.
- `ribbon-order`: particles of one burst (equal ages) are joined in an order Unbound does not
  reproduce.
- `rate-at-capacity`: short trails of rate births sample other points, and a rate birth after a
  carrier leaves a full buffer comes one step later in Unbound.
- `collision-bounce-trail`: trail points around a bounce.
- `world-trail-death-head`: with world-space trails behind a moving emitter in local simulation
  space, a dead particle's trail ends at its newest point in Unity, at its last head in Unbound
  (image `world_13`); with world simulation space both end at the last head
  (tests/projects/particlestreams `trail.22`).

Unmeasured: other releases, trigger kills of trailed particles, `SetTrails` after carriers were
dropped, trail texture modes on fading trails, `BakeMesh` of carriers (Unbound bakes what it
draws).
