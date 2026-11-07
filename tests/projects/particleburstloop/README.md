# particleburstloop

When a burst at or near the end of the loop fires in Unity 6000.0.71f1, and what its particles
start with. A 1 s system with no rate emits only bursts; its particles live 4 s, do not move, and
their start size runs from 1 at the loop's start to 3 at its end, so the size tells which loop time
a burst took its start values at and the remaining lifetime tells its age. After every
`Simulate` call the probe records the time, the count, `isEmitting` and, per particle, start size,
remaining lifetime and seed. Scenarios: a burst at the duration with fixed and frame steps that
cross or land on the loop end, single steps whose loop end falls 0.00001 to 0.019 s in, one call
over several loops, bursts at the start and the end together, just inside and past the end,
repeats (finite and endless) reaching the end, probability, a count curve, a non-looping system,
a start delay, two bursts in one step (inside a loop and across its end), and a system played
over frames.

    python3 tests/projects/particleburstloop/run.py native --label native1   # Editor capture
    python3 tests/projects/particleburstloop/run.py publish native1 native2  # two identical captures
    python3 tests/projects/particleburstloop/run.py check                    # Unbound run + compare (the gate)

The reference is `Reference/unity-6000.0.71f1.jsonl` (two identical captures). The comparator is
`particletrails/compare.py`: every key and value at the particle probes' bound, no known
differences.

## Documented contract (Unity 6000.0 Scripting API)

| Source | What it says |
| --- | --- |
| [ParticleSystem.Burst](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.Burst.html) | "A burst is a particle emission event, where a number of particles are all emitted at the same time." `time`: "The time that each burst occurs." `cycleCount`: "how many times the system should play the burst", 0 forever. `repeatInterval`: "How often to repeat the burst, in seconds." `probability`: "The probability that the system triggers a burst." Nothing about a burst at the duration, the loop wrap or several bursts in one step. |
| [ParticleSystem.EmissionModule](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.EmissionModule.html) | Bursts are set with `SetBursts`/`SetBurst`. No timing rule. |

Everything below is measured in this release, not documented.

## Measured behaviour

| Records | 6000.0.71f1 |
| --- | --- |
| `end/*`, `edge/*`, `delay` | A burst whose time is the duration fires in the internal step that crosses the loop end (a step ending exactly on it does not; the next one crosses), in Simulate with fixed or frame steps and in a single long call. |
| `once/*` | A non-looping system never emits it. |
| `past/*`, `cycles/three*`, `cycles/four`, `cycles/forever/end` | A burst past the duration never fires; nor does a repeat that lands on or past it. |
| `pair/mid`, `pair/cross*`, `both/*`, `edge/*` | Bursts due in one step share one age, the youngest of theirs, and one initialization batch (seeds and slots as a rate batch's). |
| `edge/*`, `inside/*`, `single/*`, `end/*` | Across the loop end each part of the step stands for the whole step: a burst before the end (or at it) is aged step × (end + 0.0001 − time) / (end + 0.0001 − step start), a first burst of the new loop at its start the whole step; repeats are aged the whole step. Every burst of the step takes its start values at the step's end, in the new loop; its count curve is read at its own time (`countcurve`). |
| `probability` | Probability draws keep their order. |
| `played/*` | Played over frames the same holds. |

The position of the 0.0001 s margin is measured by the `edge/*` series (a step starting 0.0001 s
before the end ages its loop-end bursts half the step). Unmeasured: a first burst of the new loop
after its start within a crossing step (Unbound maps the part after the wrap onto the whole step),
bursts changed with `SetBursts` after their time passed in the current loop (Unbound treats them
as fired), other releases.
