# Sub-emitter first age

Unity 6000.0.71f1, built-in pipeline (`Assets/Scripts/SubEmitterTimingProbe.cs`). Status: prepared,
not captured. There is no `Reference/` yet. It is published only from two independent,
byte-identical batchmode GPU captures (see Capture).

## Question

A Death sub-emitter's particle starts older in Unity than in Unbound. In every recorded `.025`
history its first age is Td = 0.0199999921 (bits `3ca3d706`). Td is the frame-1 `deltaTime`, and it
is also the project's rational fixed step 2822399/141120000. Three facts constrain the rule:

- a `.015` call gives age 0 (cached particledeathtiming delta-0.015 pair);
- the current fixed delta `.01` changes nothing (fixed-0.01 pair);
- a `.05` call with death in its first step gives 2Td (broad `sub_emitters_child`).

Two rules fit all of that: R1, `floor(T/Td)*Td` per call, and R2, Td for each internal step of
length at least Td from the event's step on. Neither is known to be Unity's rule, and Td's
clock binding is unknown: every history ran in the first frame, where all clocks equal Td. This
probe collects the smallest discriminating matrix in one capture. Its evaluation never gates
publication. No fit, or several fits, is a valid negative.

## Fixture

`Packages/`, `ProjectSettings/` and the non-script `Assets/` files are byte copies of
`tests/projects/particles` (Root `b6c50957`). That is also the fixture of the
`particletriggerlifetime` canonical. Its Editor-compiled capture copy
`particletriggerlifetime-20261101/focused/project-native-c` wrote the canonical's bytes
(`c014a837…`), and its Bee response files are this probe's compile graph (see Preflight).
TimeManager is unchanged: fixed step 2822399/141120000, maximum particle step .03. The manifest
has four modules: particlesystem, physics, imageconversion and jsonserialize. The scene is the
empty `Probe.unity`. Nothing is imported, set up or baked by an Editor outside the capture.

The old particledeathtiming captures (`validation/runs/particledeathtiming-20261101`, Native pairs
`b0326adb…` default, `081a97d9…` fixed-0.01 and `d900e252…` delta-0.015) are cached evidence. They
are not in Root, and their source branch is not merged. `analyze.py history` reads them, and only
as an input control. The baseline case reuses their system set-up code. The Birth control
copies the broad probe's `sub_emitters_birth` code. Controls check that copy byte for byte,
whitespace aside.

## Matrix

Frames are relative to the probe component's first frame, its `Start`. Every case uses one
stationary parent particle at z .46 with velocity 0. The parent has a Death sub-emitter: speed 1
along +z, lifetime 2.013, one burst at 0, InheritNothing. It also has an ordinary transform child
with one speed-1 particle. Each case runs two calls of
`Simulate(T, withChildren true, restart false, fixedTimeStep false)` and is destroyed before the
probe yields.

| Key | Frame | Settings before the case | T | Parent life | Internal steps | Death step |
|---|---|---|---|---|---|---|
| `case.baseline` | 0 | startup | .025 | .013 | 1 x .025 | 1 |
| `case.d1` | 0 | startup | .035 | .013 | 2 x .0175 | 1 |
| `case.d2` | 0 | startup | .05 | .038 | 2 x .025 | 2 |
| `case.birth` | 0 | startup | broad `sub_emitters_birth`: 15 x Simulate(.05, true, i == 0, false) | | 2 x .025 | call end (inferred) |
| `phase.d3` | 0 | sets `captureDeltaTime` 1/30 and `timeScale` .5 | | | | |
| `case.d3` | 2 | the above | .025 | .013 | 1 x .025 | 1 |
| `phase.fixed` | 2 | sets `fixedDeltaTime` .0075 | | | | |
| `case.fixed` | 4 | the above | .025 | .013 | 1 x .025 | 1 |
| `restore` | 4 | puts back the startup fixed delta, time scale and capture delta | | | | |

The suggested `.01` fixed control is unsound. R1 at `.01` gives `floor(2.5)*.01 = .02`, and on
the fixed-0.01 history that has Td's exact bits (`analyze.py history`). `.0075` keeps the
current-fixed candidates apart from every other one: R1 gives .0225 and R2 gives .0075, while the
fixed-unscaled value at .015 is apart from 1/60. The fixed control is D3's clock context with one
setting changed.

The record reads every clock in each case's own frame (`*.clock.before/after`):

- `deltaTime`, `unscaledDeltaTime`, `fixedDeltaTime` and `fixedUnscaledDeltaTime`;
- `captureDeltaTime`, `captureFramerate` and `timeScale`;
- `maximumDeltaTime`, `maximumParticleDeltaTime` and `inFixedTimeStep`;
- the relative `frameOffset`.

The startup clocks are in `clock.startup.clocks`, and each settings change has a read-back
(`phase.*.readback`, `restore.readback`). Nothing asserts that a set value reads back unchanged,
or what `captureDeltaTime` does to `deltaTime`: the actual values are data.

## Record

`capture/values.jsonl` holds one `{"k": key, "v": value}` per line, in a fixed order, written once
through a temporary name. Each case records:

- `.input`: the authored constants;
- `.clock.before` and `.clock.after`;
- `.parent|death|ordinary.before|after1|after2` (Birth: `.parent|child.0..14`);
- `.log`: the log messages the case caused.

Each system state holds count, readCount, time, the playing/emitting/stopped/paused flags and
IsAlive(true). It also lists every particle in GetParticles order, never sorted or filtered, with:

- position, velocity, totalVelocity, animatedVelocity and axisOfRotation;
- remainingLifetime, randomSeed and startLifetime;
- startSize3D and the current size;
- startColor and the current color;
- rotation3D and angularVelocity3D.

There are no mesh-index, custom-data, rendering, image or bake reads. The last lines are
`capture.unscopedLogs` and the single marker `{"k":"complete","v":true}`. Absolute frame numbers
and real times go to `capture/diagnostics.jsonl`, which is raw and not compared.

## Predictions (precommitted)

`analyze.py` defines every candidate in executable form and `PREDICTIONS.json` is its output at
nominal clocks (`analyze.py predict`; the controls regenerate it byte for byte). The evaluation
(`analyze.py fit`) uses the capture's ACTUAL clocks with the same formulas.

- **Model.** The particle-death-source `age_model.py` (sha256 `fe154f28…`), in float32 with the
  percent age.
- **Step split.** `n = ceil(T / maximumParticleDeltaTime)` equal steps.
- **Death step.** The first step whose end reaches the parent's remaining life.
- **Clocks.** `frame.deltaTime`, `frame.unscaledDeltaTime`, `frame.fixedDeltaTime`,
  `frame.fixedUnscaledDeltaTime`, `frame.captureDeltaTime`, `startup.deltaTime`,
  `startup.unscaledDeltaTime` and `startup.fixedDeltaTime`.
- **Clock families.** R1, R2 and PERSTEP, each applied to every clock.
- **Already contradicted, still evaluated.** `CONT_REMAINING` (the engine's current rule),
  `CONT_TAIL`, `LITERAL_002`, `STEP_MINUS_005`, `STEP_X08`, and PERSTEP at Td.

Primary verdict: the bits of the Death child's life and pos.z after call 1 equal the prediction.
This is the first-age stage. Agreement after call 2 is the model's continuation. It is reported
separately because the model's later ageing is proven for .025 steps only.

First age in seconds at nominal clocks: startup Td everywhere, capture 0; D3 has dt 1/60 and
unscaled 1/30. "Fixed" is the current `fixedDeltaTime`.

| Case | R1(Td) | R2(Td) | R1/R2(dt) | R1/R2(unscaled) | R1/R2(fixed) | CONT_REMAINING |
|---|---|---|---|---|---|---|
| baseline | Td | Td | Td / Td | Td / Td | Td / Td | .025 |
| d1 | **Td** | **0** | Td / 0 | Td / 0 | Td / 0 | .035 |
| d2 | **2Td** | **Td** | 2Td / Td | 2Td / Td | 2Td / Td | .025 |
| d3 | Td | Td | **1/60** / 1/60 | **0** / 0 | Td / Td | .025 |
| fixed | Td | Td | 1/60 / 1/60 | 0 / 0 | **.0225 / .0075** | .025 |
| birth (event at call end) | 2Td | Td | 2Td / Td | 2Td / Td | 2Td / Td | .025 |

Predicted after-call-1 bits (life / pos.z) of the Death child:

- Td: `3fff1aa0` / `3ef5c28f`
- 0: `4000d4fe` / `3eeb851f`
- 2Td: `3ffc8b44` / `3f000000`
- 1/60: `3fff87da` / `3ef40da8`
- .0225: `3ffec8b4` / `3ef70a3e`
- .0075: `40005a1d` / `3eef5c29`

The Birth child's first record is predicted as `3ebef9dc` (R1) or `3ec9374b` (R2). R2 equals the
broad canonical's .392999977. The model's R2 continuation `3eaf9db2` is one float step from the
canonical `3eaf9db1`, so Birth R2 is expected FIRST, not FIT (precommitted).

Reading the outcome:

- D1/D2 separate R1 from R2.
- D3 separates frame delta, unscaled/capture delta and the startup or current-fixed group.
- `case.fixed` separates the current fixed delta from the startup clocks.
- The Birth control asks whether the Death-fitting rule also gives the Birth child's first age,
  which would be a shared first-update stage rather than a Death-only fitted rule.

There are 22 distinguishable classes at nominal clocks. These remain indistinguishable by design,
and limit the conclusion:

- `startup.deltaTime`, `startup.unscaledDeltaTime` and `startup.fixedDeltaTime` are all Td in
  the first frame;
- `frame.captureDeltaTime` gives 0 in every non-zero case for R1 and R2;
- PERSTEP at the startup clocks.

Other limits:

- The Birth event step is inferred, not measured.
- Positions of Birth children are not modelled.
- Collision, trigger and manual sub-emitters, moving or world-space parents, restarts and the
  played player loop are not covered.
- Other releases and backends are not covered.
- A fit means "consistent with", not "Unity's mechanism".

## Capture

`run.py editor` makes one fresh private copy per repeat under
`$XDG_CACHE_HOME/unbound/validation/runs/particlesubemittertiming/<label>`. It launches
`EditorJob(batch=True, graphics=True)` from `tests/editor_runner.py`:

```
vglrun -d egl Unity -batchmode -projectPath P -executeMethod ProbeRunner.Record -logFile L
```

That runs on a private Xvfb inside one GPU lease, with no `-quit` and no `-nographics`. The
deadlines are a hard 1200 s, a 300 s external no-progress limit (only `runner-events.jsonl` and
the completed `values.jsonl` count, cold import included), shutdown 30 s and kill 10 s. The
launch records `prelaunch-inputs.json` (input bytes, Editor hash, environment) and `inputs.json`
(prepared fixture, sources, tool hashes, preflight, git head). After the run it writes
`authored-after.json` and `authored-delta.json`.

`ProbeRunner.Record` opens the scene and enters Play mode from a delayCall. Only its
`EditorApplication.update` handler changes state:

- it sees the probe's `finishedCode` and leaves Play mode;
- it waits 30 Edit-mode updates;
- it removes both of its handlers and closes its event output;
- it quits with the code.

The play mode callback only records, and nothing it owns writes or queries at shutdown. It clears
Error Pause, and an Editor-side watchdog quits with 4 after 600 s.

Exactly two sequential launches are reserved, `native1` then `native2`, and each collects the
whole matrix. `native2` is admitted only after `native1` passes `check`: the strict gate plus the
actual-rsp-versus-preflight graph check. A label directory is write-once. There are no retries,
no windowed or `-nographics` fallback, and no third launch. Any failure or disagreement stops the
pair. `publish` writes `Reference/unity-6000.0.71f1.jsonl` only if both repeats pass and their
`values.jsonl` are byte-identical. It never overwrites, and both raw captures stay outside Git.

## Gates

`guards.py` (used by `check` and `publish`) refuses:

- **Editor result:** any forced stop (shutdown guard included, even after a complete marker), an
  exit status other than 0, a non-NVIDIA renderer, no shutdown line, a launcher failure.
- **Command:** anything but the planned batchmode GPU Record command of the release.
- **Inputs:** a prelaunch receipt whose release, capture variable or input bytes differ from the
  prepared fixture.
- **Runner events:** anything but record, enter, finished 0, leave, exit 0, once each and in
  order, with exit last.
- **Editor-side results:** the watchdog fired, or a partial record was left behind.
- **Editor log:** compile errors, native faults, or no renderer line.
- **Fixture:** a changed or removed fixture asset. Settings and package changes, and the script
  `.meta` files the Editor writes, are listed in `authored-delta.json`, not refused.
- **Record:** anything that does not follow the source-derived schema record by record. That
  covers a missing, extra, duplicate or reordered key, a wrong type, a changed authored input, a
  phase read in another relative frame, a probe fault or fatal record, a marker that is not
  single and last, unscoped error logs, a context that is not the release in batchmode on
  OpenGLCore, and missing raw diagnostics.

No count, particle, state or clock answer is gated. `compare.py` compares the full leaf union
exactly, with no tolerance, and keeps every missing, extra and differing leaf with both values.

## Preflight and controls

`preflight.py` compiles the two scripts with the Editor's own Roslyn (`NetCoreRuntime/dotnet exec
DotNetSdkRoslyn/csc.dll`, no compiler server) and the response files of the Editor-compiled
fixture copy above. First it proves the copy is this fixture: Packages, ProjectSettings and
non-script Assets are byte-identical, the Editor compiled both assemblies, and the copy has one
script per assembly folder. Then it rebinds only paths: the one source per assembly, the working
directory and the generators' additional file. References, defines, analyzers and options stay
the same bytes. The copy was compiled by a `-nographics` Editor. A batchmode GPU Editor's compile
(the shader keyword probe's `shaderkw-native-20261102a/native1`) wrote the same bytes for the
Editor-assembly response file (`fbbf6c6d…`). Its Assembly-CSharp response file has the same
defines, options and analyzers, and its references differ only by this fixture's two extra
manifest modules (ParticleSystem, Physics). The launched pair's actual response files are checked
against the preflight anyway.

`Controls/controls.py` (`with-heavy-job --kind tool`):

- compiles the real scripts at C# 9 against `FakeUnity.cs`;
- drives them through the Editor protocol in seven scenarios (`Driver.cs`);
- runs positive and mocked negative controls of every guard, the pair gate, launch admission, the
  graph check, the preflight judgement and the comparator;
- checks the analyzer against synthetic, broad and cached histories.

Fake answers are not Unity's.

```
python3 tests/projects/particlesubemittertiming/preflight.py --rsp-dir <copy>/Library/Bee/artifacts/2400b0aE.dag --out <cache>/preflight
python3 tests/projects/particlesubemittertiming/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native1 --preflight <cache>/preflight/preflight.json [--dry-run]
python3 tests/projects/particlesubemittertiming/run.py check --release 6000.0.71f1 --label <fresh> --repeat native1
python3 tests/projects/particlesubemittertiming/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native2 --preflight <cache>/preflight/preflight.json
python3 tests/projects/particlesubemittertiming/run.py publish --release 6000.0.71f1 --label <fresh>
~/.cache/compat/with-heavy-job --kind tool python3 tests/projects/particlesubemittertiming/analyze.py fit <cache>/<label>/native1/capture/values.jsonl --report <cache>/fit.json
UNBOUND_CLI=/path/to/unbound python3 tests/projects/particlesubemittertiming/run.py unbound --release 6000.0.71f1 --label <fresh>
~/.cache/compat/with-heavy-job --kind tool python3 tests/projects/particlesubemittertiming/compare.py \
    tests/projects/particlesubemittertiming/Reference/unity-6000.0.71f1.jsonl <cache>/unbound --report <cache>/report.json
```

Do not wrap `run.py` in with-heavy-job: each Editor takes its own lease.
