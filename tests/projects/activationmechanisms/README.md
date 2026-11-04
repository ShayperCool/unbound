# Activation mechanisms: traversal, rank, wake and built-in interleaving

Unity 6000.0.71f1, built-in pipeline (`Assets/Scripts/ActivationMechanismsProbe.cs`). Prepared and
UNLAUNCHED: there is no Native capture or reference yet, and no Native launch is granted here.
This probe discriminates the mechanisms the activation order reference left open. It separates
the OFF traversal (post-order vs reverse level order) and the ON rank (component add, first Awake,
last enable, document/hierarchy order, recorder/GameObject/Transform ids). It also separates
script OnDisable from built-in disables (Rigidbody2D/Collider2D with 2D exit callbacks, and
ParticleSystem). Every answer is data; none is a gate.

## Fixture and compile graph

`Packages/`, `ProjectSettings/`, the folder metas, the runner's `.meta` and the scene `.meta` are
byte copies of the sprite import fixture (`tests/projects/spriteimport`). That fixture's Editor-written
runtime graph references `UnityEngine.Physics2DModule` and `UnityEngine.ParticleSystemModule`. The
activation order graph has neither. No setup Editor ran, and no reference was appended. The
runner is the admitted activation order runner with only the probe names changed. The probe
script's `.meta` is new and has no importer settings (default execution order). The only compile
inputs are the two scripts.

The graph is `spriteimport/cap1` (`projects/native1`, repeat `projects/native2`). Both copies
still hold the committed fixture bytes and wrote byte-identical response files. Both captures are
the published sprite import reference. Compared with the spent activation order graph, the runtime
response file adds exactly four module references (AssetBundle, ParticleSystem, Physics2D,
Tilemap). Its defines, options, language version, analyzers and compiler are the same, and the
Editor response file is byte-identical. That capture predates editor_runner launch receipts and
ran `-batchmode -nographics` (Null Device). `preflight.py` therefore proves identity from the
copies, the committed fixture, the Editor log (command, release, Bee Csc/copy steps, normal
shutdown), the repeat and the reference. A launched capture must still match the preflight's
rebound graph exactly (`check`).

`Assets/Scenes/Probe.unity` is hand-authored from the Editor-serialized sprite import scene
(settings documents byte-identical). Root `S` has children `S1`, `S2` (child `S21`) and `S3`. `S2`
holds recorders `b`, `a` in component-list order but `a`, `b` in document order. The GameObject
document order, MonoBehaviour document order, fileID order and hierarchy order all differ. No id
is assigned or inferred: the Editor's actual ids go to diagnostics.

## Matrix (one Play mode session)

The driver (its own class, message Start only, on a never-toggled root) builds the runtime trees
at the start of Play mode. One default-order recorder class (`ActivationMechanismsProbe`: Awake,
OnEnable, Start, OnDisable, the admitted message set) is used throughout. A separate
`Contact2DObserver` records 2D begin/end messages only. Every entry records label, slot, type,
callback, other collider, contact count, own flags and the case's public built-in state.

| rows | tree / setup | operation | separates |
|---|---|---|---|
| base | admitted P{C1{G},C2}, U; created G,C2,P,C1; added C2,C1,P,G | P off/on | baseline equivalent of the admitted plain off/on |
| offA, mirror | A{A1{A11},A2{A21}} and M (same shape, construction/add orders mirrored) | off/on | POST vs reverse level vs construction/add, sibling vs input order |
| on1 | R{RY,RX,RZ}; RX self-inactive at its add; adds RX,R,RZ,RY; then RX wakes | off/on | ADD vs first Awake vs traversals |
| on3 | on1's shape (L); after the wake L's recorder is disabled and enabled | off/on | last enable vs first Awake vs ADD |
| on2 | scene tree S, plus a runtime recorder `rt` on S1 | S off/on | document/fileID/component-list/hierarchy vs actual load wake and actual ids |
| contact x8 | P (Kinematic Rigidbody2D, full kinematic contacts) with child C (circle 0.25) on a static 2x2 box E | P off, then one Simulate | F vs S vs others: solid/trigger, callbacksOnDisable true/false, scripts before/after built-ins |
| particles x2 | P and child C, each with a playing ParticleSystem (emission off, 2 resp. 3 emitted) | P off | built-in vs script disable order through public ParticleSystem state |

The 2D geometry is the 2D lifecycle reference's (box 2x2, circle 0.25 at -0.5, Kinematic
target), with two explicit `Physics2D.Simulate(0.02)` steps before the call. Each 2D case sets
`simulationMode = Script` and its `callbacksOnDisable` and restores both in `finally`. Cases are
isolated by their own objects and positions; nothing is destroyed. No audio.

## Record

`values.jsonl` has one `{"k","v"}` per line in fixed emission order (`guards.expected`). It holds
`context.*`, `boot.*`, `setup.*`, `settle.*`, `op.<name>.*`, `contact.<name>.*`,
`particles.<name>.*`, `capture.strayCalls`, `capture.unscopedLogs` and the single completion
marker. Raw callback lists are kept in emitted order. Instance ids, absolute frames, real time and
device names go only to `diagnostics.jsonl`. Its phases are start, ids:boot, ids:setup, one ids:
entry per built-in case, and written. That file is hashed and never compared.

## Hypotheses (`PREDICTIONS.json` from `analyze.py predict`)

Static predictions come from the committed source tables and scene bytes:

- traversals PRE, POST, REVPRE, REVPOST, LEVEL, REVLEVEL, REVBFS, each with a *_REVCOMP variant;
- CREATE and ADD (and their reverses);
- authored first-wake and last-enable chronology (AFIRSTAWAKE, ALASTENABLE);
- for the scene, DOCMB, DOCGO, FILEIDMB and FILEIDGO.

Capture-derived rules are evaluated only on each capture's own record and ids: FIRSTAWAKE,
LASTENABLE, IDDESC/IDASC (recorder ids), GOIDDESC/GOIDASC and TRIDDESC/TRIDASC.

Built-in cases are predicted as order model x exit placement x state-read variant:

- order models CUR, F, F_REVCOMP, S, S_POST and SCRIPTS_FIRST;
- exit placements AT_REMOVAL, DEFERRED and NONE;
- state reads REMOVAL and FLAGS.

The claimed separations are asserted by `predict`; indistinguishable cells are kept as such. For
example, with built-ins before scripts F and S predict the same trace; only the scripts-first
layout separates them. Outcomes are ONE, MULTIPLE, NONE or NOT_EVALUABLE; all are valid. A case
whose read-back tree, components, awake state, contact or observable state change is not the
authored precondition is NOT_EVALUABLE. A ParticleSystem state that does not change is the partial
D-1 negative.

## Capture protocol (requires a separate Root grant)

```
python3 tests/projects/activationmechanisms/preflight.py --graph <runs>/spriteimport/cap1 --out <cache>/preflight [--identity-only]
python3 tests/projects/activationmechanisms/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native1 --preflight <cache>/preflight/preflight.json [--dry-run]
python3 tests/projects/activationmechanisms/run.py check  --release 6000.0.71f1 --label <fresh> --repeat native1
python3 tests/projects/activationmechanisms/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native2 --preflight <cache>/preflight/preflight.json
python3 tests/projects/activationmechanisms/run.py check  --release 6000.0.71f1 --label <fresh> --repeat native2
python3 tests/projects/activationmechanisms/run.py publish --release 6000.0.71f1 --label <fresh>
python3 tests/projects/activationmechanisms/analyze.py fit <cache>/<label>/native1/capture/values.jsonl --report <cache>/fit.json
```

The protocol is the activation order probe's batchmode GPU Record protocol, through
`tests/editor_runner.py`. The job is `EditorJob(batch=True, graphics=True)` and the command is
`vglrun -d egl Unity -batchmode -projectPath P -executeMethod ProbeRunner.Record`, without `-quit`
or `-nographics`. Record opens the scene and enters Play mode from a delayCall. Only the update
handler leaves Play mode and quits, after the probe's finished code. It clears Error Pause,
survives domain reloads and keeps the 600 s Editor-side watchdog. The hard deadline is 1200 s,
shutdown 30 s and kill 10 s. The external no-progress deadline is 300 s and is reset only by
`runner-events.jsonl` and the completed `values.jsonl`. A label holds at most native1 and native2,
in sequence; repeat directories are write-once. Do not wrap `run.py` in with-heavy-job.

`check` runs the strict gate. It refuses any forced stop (even after a complete marker), an exit
other than 0, a non-NVIDIA renderer, no shutdown line, or a command or prelaunch input other than
planned. It also refuses runner events other than record/enter/finished 0/leave/exit 0 (exit
last), the watchdog, compile errors or faults, a changed fixture asset, any schema/structure/request
problem, an unrestored Physics2D global and missing diagnostics. It then compares the actual
response files with the preflight's rebound graph. A passing `check` writes `admission.json`.
That file seals every launch artifact except editor_runner's append-only `hub-sweeps.jsonl`, plus
that journal's completed prefix. Registered, well-typed late sweep rows may append; any other
change revokes the admission. `native2` needs an admitted, unchanged `native1` under the same
label manifest. `publish` needs two admitted, byte-identical repeats. It writes a new reference
once and never rewrites one: an existing reference is compared instead. No callback order,
presence, count, flag, contact, particle state or model answer is a gate.

`Controls/controls.py` compiles the real scripts at C# 9 against `Controls/FakeUnity.cs` and drives
them through the Record protocol in many fake scenarios. It also checks the guards, seals, journal
handling, graph and preflight identity/judgement, analyzer and comparator. The fake answers are
not Unity's; they prove emission, control flow, model separation and refusals only.

## Limits

- One default-order recorder class. No execution-order class: a second class with its own
  `[DefaultExecutionOrder]` or `.meta` order needs its own script file, which changes the
  compile graph's source list beyond a one-to-one rebinding. Answers say nothing about other
  script types, Destroy, re-parenting or the PlayerLoop.
- ON-2's document, add and first-wake candidates can remain partly confounded with the actual ids;
  a fit is fixture-scoped consistency, not a ranking contract.
- ParticleSystem is one built-in. D-1 is not claimed for every OnEngineDisable override; if its
  public state does not change on deactivation, D-1 rests on Physics2D only.
- State reads inside OnDisable observe public API results; the FLAGS variant keeps "reads follow
  activeInHierarchy" as an explicit confound of removal order.
- The compile graph comes from a `-nographics` capture without a launch receipt; its identity
  rests on the evidence above and on the native1 graph check.
- The scene was never opened by an Editor; its loading is part of what native1 tests.
- Batchmode Linux Editor with OpenGLCore only.
