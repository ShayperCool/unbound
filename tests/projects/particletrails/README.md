# particletrails

What a particle system with a Trails module reports and draws while its particles die, in Unity
6000.0.71f1. Systems built in code are stepped with `Simulate` (0.05 s per call); after every call
the probe records the state (`particleCount`, `IsAlive`, `isPlaying`, `isEmitting`, `isStopped`,
`isPaused`, `time`), every particle `GetParticles` returns (position, velocity, remaining and start
lifetime, current size and alpha, seed) and the trail mesh `BakeTrailsMesh` builds (positions and
alpha). Scenarios: `dieWithParticles` false and true, PerParticle and Ribbon trails, local and
world trail space, world simulation space and a moving emitter, a trail lifetime range with a
ratio and a larger vertex distance, trail lifetime by size, a Death sub-emitter, collision kills
(lifetime loss with and without trails, and by speed with a Death sub-emitter), `Stop`, `Clear`,
`SetParticles` (written back as read, and a living particle written at zero lifetime), a
four-particle cap (with `dieWithParticles` off and on), a looping system, a restart, the trail ratio over 600 particles,
two systems played over frames with a stop callback, and images of fading trails. `api/*` records what `GetTrails`
returns and the `Trails.capacity` it leaves (fresh, presized, refilled; growing particle counts and trail lengths;
after `Clear` and a restart; Ribbon mode), and `GetTrails`/`SetParticles`/`SetPlaybackState`/`SetTrails` round trips
(per-particle, a fading carrier, Ribbon, world space, a trail ratio): the state after the restore and the steps after
it, the capture applied with its first particle left out, applied without `SetParticles`, before `SetParticles`, and
to another system. `cap/*` records births the cap blocks: a 20/s and a 30/s rate, cycling bursts and both, with
trails that fade, trails that die with their particles, no trails and fading trails off the step grid (also with no
minimum vertex distance and with one of 0.3), stepped by
`Simulate` calls of 0.02, 0.05 and 0.1 s and played over frames.

    python3 tests/projects/particletrails/run.py native --label native1   # Editor capture (GPU, job pool)
    python3 tests/projects/particletrails/run.py publish native1 native2  # two identical captures
    python3 tests/projects/particletrails/run.py check                    # Unbound run + compare (the gate)

The reference is `Reference/unity-6000.0.71f1/` (`values.jsonl`, 15 images); both captures were
identical, values and image bytes (re-recorded with the `cap/*/die0ntmv0.29` and `die0ntmv0.31`
controls: every earlier record and image unchanged). Values compare at the particle probes' bound (0.0002 absolute
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
| `pp/maxp/*` | It holds its slot against `maxParticles`. |
| `pp/ratio`, `pp/ratiobig/*` | The ratio selects a particle when a seeded draw is below it (one draw per particle seed; the 0.25 selection is a subset of the 0.5 one); particles without a trail leave at death. |
| `pp/setparticles`, `pp/killlife` | `SetParticles` removes a particle written at zero or negative lifetime, a carrier included, with its trail at once, whatever `dieWithParticles` says: the trail does not fade. |
| `pp/collision/*` | A particle killed by a collision, by lifetime loss or by speed, with or without trails, shows zero lifetime and its bounced velocity for one update; the next update removes it (or makes it a carrier at rest where it was), and its Death sub-emitters fire then. |
| `pp/deathsub` | Death sub-emitters fire when the particle dies, not when its trail ends (same with `dieWithParticles` true). |
| `ribbon/*` | Ribbons keep no dead particles; `dieWithParticles` changes nothing. |
| `played/*` | Played over frames the same holds; the stop callback comes when the last carrier leaves. |
| `api/*` | `GetTrails` returns the size of the system's trail storage, not the number of trails (the documented return): particle slots (at least 32, a power of two) times points per trail (at least 4, a power of two), from the most particles and the longest trail so far; it never shrinks (also after `Clear` and a restart) and is 0 in Ribbon mode. The `Trails`' capacity becomes that size when it is smaller; a larger presized capacity stays. |
| `api/rt/*` | `SetTrails` puts trails back by particle index: the particle at index i takes the trail captured at index i (or none, if the particle there had none), whatever particle held it, also in another system of the same setup. A round trip with `SetParticles` and `SetPlaybackState` restores the trails exactly and the later steps repeat. `SetParticles` leaves each trail at its index: a particle written into a live slot keeps that slot's trail; one written past the live slots starts its own. |
| `cap/*` | A birth the cap blocks is dropped, not retried later (single and multi-step calls, Play, rate and bursts); with fading trails the waiting birth takes the slot in the step its carrier leaves. |

## Known differences (`known.json`)

Listed by exact key and class with its reason; the comparator prints each as KNOWN and fails on
any other difference. Two concern dead particles, `world-trail-death-head` and the carrier part of
`rate-at-capacity`; the others also show with `dieWithParticles` on or while every particle lives:

- `newborn-trail-quad`: a newborn trail whose points still coincide gets its first quad across
  another direction in Unity (images `die1_9`, `world_9`).
- `death-child-age`: the separate Death sub-emitter child age residual (also after a speed kill).
- `ribbon-order`: particles of one burst (equal ages) are joined in an order Unbound does not
  reproduce.
- `rate-at-capacity`: short trails of rate births sample other points (`die0` and `die1`); with
  `dieWithParticles` off a rate birth into the slot a leaving carrier frees comes one step later in
  Unbound (the `die1` control matches every particle): the `carrier-expiry-tie` below.
- `carrier-expiry-tie`: with trail lifetimes that are whole numbers of steps, carriers leave (and
  free their slot) a step apart from Unity, in both directions: an insignificant float tie where a
  carrier's newest point reaches the trail lifetime exactly at a step's end. The same systems with
  the trail lifetime off the step grid (`cap/*/die0nt`) match every particle, also with no minimum
  vertex distance (`cap/*/die0ntmv0`), so the last trail point a carrier keeps is the one its own
  steps added (no point at its death).
- `vertex-distance-birth-slot`: with a minimum vertex distance that keeps a particle from taking a point
  every step (0.3 with particles moving 0.1 a step, `cap/*/die0ntmv3`), a birth waiting for the slot
  of a particle leaving at zero life comes a step later in Unbound. The controls off that tie
  (`cap/*/die0ntmv0.29`, `die0ntmv0.31`, recorded last) differ the same way, so it is not a float
  tie but an unresolved rule tied to the minimum distance; with no minimum distance every particle
  matches.
- `restored-trail-other-index`: right after `SetTrails` gives a particle a trail another particle
  left, Unity draws it ending at its oldest kept point, Unbound cut at the lifetime; with the trail
  ratio such a carrier's trail samples other points afterwards.
- `collision-bounce-trail`: trail points around a bounce.
- `world-trail-death-head`: with world-space trails behind a moving emitter in local simulation
  space, a dead particle's trail ends at its newest point in Unity, at its last head in Unbound
  (image `world_13`); with world simulation space both end at the last head
  (tests/projects/particlestreams `trail.22`).

Unmeasured: other releases, trigger kills of trailed particles, a speed kill that also loses lifetime, `SetTrails` with a
`Trails` that `GetTrails` never filled (Unbound refuses it with an error), the storage of a system with trails disabled, trail texture modes on fading trails, `BakeMesh` of carriers (Unbound bakes what it
draws).
