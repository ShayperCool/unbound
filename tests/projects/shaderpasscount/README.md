# Shader and material pass counts against the drawn SubShader

Unity 6000.0.71f1, built-in pipeline (`Assets/Scripts/ShaderPassCountProbe.cs`). The canonical
`Reference/unity-6000.0.71f1.jsonl` contains 923 rows from two independent byte-identical
batchmode GPU captures, both exit 0 without a forced stop on NVIDIA RTX 4060.

The shader keyword canonical has `Shader.passCount` equal to SubShader 0's declared count for all
of its 37 shaders; that equality did not establish which SubShader was drawn. The builtinrp LOD
controls draw other SubShaders whose pass counts are all 1. This fixture makes the competing
rules give different answers: its SubShaders and Fallback declare different pass counts, and a
rendered colour identifies the SubShader actually drawn.

## Fixture

`Packages/`, `ProjectSettings/`, the scene and the runner's `.meta` are byte copies of the shader
keyword fixture (Editor-serialized; no setup Editor). The four shaders (`Assets/Shaders`, authored
offline):

| shader | SubShader | LOD | passes | code (R, B) |
|---|---|---|---|---|
| Probe/PassLod (Fallback Probe/PassFallback) | L0 | 400 | 3 | 32, 0 |
| | L1 | 300 | 1 | 64, 0 |
| | L2 | 200 | 2 | 96, 0 |
| Probe/PassFallback (Fallback Off) | F0 | 150 | 4 | 128, 0 |
| | F1 | 50 | 5 | 159, 0 |
| Probe/PassTagged (Fallback Off) | T0 `RenderPipeline=UniversalPipeline` | none | 3 | 191, 0 |
| | T1 | none | 2 | 223, 0 |
| Probe/PassSurface (Fallback Off) | S0 surface Lambert (Unity generates its passes) | 300 | generated | 32, 128 |
| | S1 | 200 | 3 | 64, 128 |

Every vertex/fragment pass draws with `ZTest Always ZWrite Off Cull Off`; pass 0 replaces the
colour with its SubShader's code (red, blue) and one green step (0.125), every further pass adds
one green step (`Blend One One`). So a rendered colour names the drawn SubShader and green/32 is
the number of passes drawn. The surface SubShader renders its emission (no albedo, flat black
ambient, no light). SubShaders carry a `SubId` tag and passes a `PassId` tag.

## Cases (one Play mode run, in order; settings accumulate)

`default`; per-shader `PassLod.maximumLOD` 350, 250, 175 (Fallback F0), 100 (F1), 25 (nothing in
reach), restored; `globalMaximumLOD` 250, 100, 25 (nothing for PassLod/PassFallback/PassSurface,
T1 still in reach), then PassLod 350 and 100 under global 25 (a per-shader limit over the global
one, a Fallback reached under the referring shader's limit while its own global limit excludes
it), both restored; `PassSurface.maximumLOD` 250 (S1) and restored; PassLod 175 with
`PassFallback.maximumLOD` 100 (testing whether its own limit affects the referring shader) and restored. A restore
sets the value read before the first case.

## Record

`values.jsonl`, one `{"k": key, "v": value}` per line, in the probe's fixed order. Reads record
their value or `key.exception`; steps only `step.exception`; every block ends with `block.log`.

* `context.*`: Unity version, batchmode, platform, graphics device and shader level, quality
  level/name, colour space, current/default/quality render pipeline, `Shader.globalRenderPipeline`.
* `setup.*`: each shader `found`, its material made, its initial `maximumLOD`, the initial
  `globalMaximumLOD`, the camera's actual rendering path and the background colour.
* `decl.<shader>.*`: `subshaderCount`, `passCountInSubshader`, `subIds`, `renderPipelineTags`,
  `passIds` and `passLightModes` (`s.p:value` for every pass).
* `case.<case>.set` (the authored settings), the steps, `globalMaximumLOD`, then per shader:
  `maximumLOD`, `passCount`, `isSupported`, `passCountInSubshader`, `mat.passCount` (a material
  made in set-up), `newMat.passCount` (made in the case), `mat.subTag` / `mat.subTagFallbacks`
  (`GetTag("SubId", false/true, "none")`), `mat.findPass` (FindPass for every pass name of the
  chain, fixed order), `color` (`{"rgba": [r,g,b,a], "uniform": bool}` at the centre of a 16x16
  linear ARGB32 target of an orthographic forward camera), `passCount.after` and
  `mat.passCount.after` (after the render).
* `cleanup.log`, `capture.unscopedLogs`, then the single marker `{"k":"complete","v":true}`; the
  file is written once, through a temporary name, before the probe yields.

Compared values are counts, flags, names, indices and 8-bit colours only. Real times, frame
numbers and device names go to `diagnostics.jsonl`; the runner's events carry Editor times. Both
are kept and hashed in `artifact-manifest.json`, never compared. No answer is sorted or widened.
Out-of-range pass access (GetPassName, SetPass, Blit, pass-only FindPassTagValue) is not used, so
a wrong pass count can never index past a SubShader natively.

## Hypotheses (precommitted, `PREDICTIONS.json` from `analyze.py predict`)

Derived from the committed shader bytes and case table, with the initial LODs measured by
builtinrp (`maximumLOD` -1, `globalMaximumLOD` 2147483647); a capture with other initial values is
NOT_EVALUABLE, never refitted.

* FIRST: own SubShader 0's declared count, whatever is drawn.
* SELECTED: the drawn SubShader's count (first of own then Fallback SubShaders, untagged for the
  built-in pipeline, LOD at most the shader's limit; none: 0).
* RENDERER: Unbound's renderer list at this base (SELECTED's SubShader; a surface SubShader counts
  forward base + add = 2; none: 0).
* FALLBACK_OWN: own SubShaders under the shader's limit, else the Fallback under its OWN limit.

FIRST/SELECTED differ in 33 cells, FALLBACK_OWN/SELECTED in 4 (shader100, shader25,
global25shader100, fallback100shader175), RENDERER/SELECTED only in the 11 cells drawing the
surface SubShader, and only if Unity's generated count is not 2 (FIRST, SELECTED and FALLBACK_OWN
predict that count relationally as `passCountInSubshader[i]`). `analyze.py fit` reports, per
readout, ONE, MULTIPLE or NONE (a valid negative), the drawn SubShader's fit, and the relations
passCount == passCountInSubshader[drawn] and passes drawn == passCount. Nothing in `run.py` or
`guards.py` reads the analyzer.

## Measured result

Every Shader, existing Material and new Material pass count agrees, before and after the
render. None of the four precommitted count hypotheses fits all 68 cells; the analyzer records
NONE for every count readout. Its SELECTED and RENDERER hypotheses both fit the drawn
SubShader. Predictions and analysis are unchanged after capture.

Within this fixture, the count equals the drawn SubShader's declared count whenever one is
drawn. In nine cells nothing is drawn, yet the count equals the first SubShader's declared
count. This combined description is an observation made after capture, not a predeclared fit
or an accepted production rule. The option-less surface SubShader exposes four passes:
forward base, forward add, deferred and meta.

The referring shader's maximum LOD selects its own SubShaders before its Fallback's, under
the same limit; the Fallback shader's separate maximum does not change that selection. A
per-shader maximum overrides the global maximum. The pipeline-tagged first SubShader of
PassTagged is skipped in this built-in context. Other pipeline and backend exclusions are
outside this fixture.

Both actual compiler-input graphs match the real preflight; only private paths and the
compiler-server flag differ. Both private copies save the runner's async shader compilation
setting on exit; Assets and the authored fixture remain unchanged. Root independently
rehashes both repeats and reruns their strict capture/compiler-graph guards. Evidence:
`validation/runs/shader-pass-count-native-20261102a/receipt.json` and Root
`validation/runs/shader-pass-count-root-20261102/native-adjudication.json`.

## Capture

```
python3 tests/projects/shaderpasscount/preflight.py --rsp-dir <copy>/Library/Bee/artifacts/<hash>.dag --launch <launch dir> --out <cache>/preflight
python3 tests/projects/shaderpasscount/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native1 --preflight <cache>/preflight/preflight.json [--dry-run]
python3 tests/projects/shaderpasscount/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native2 --preflight <cache>/preflight/preflight.json
python3 tests/projects/shaderpasscount/run.py publish --release 6000.0.71f1 --label <fresh>
python3 tests/projects/shaderpasscount/analyze.py fit <cache>/<label>/native1/capture/values.jsonl --report <cache>/fit.json
```

Batchmode with graphics through `tests/editor_runner.py` (`EditorJob(batch=True, graphics=True)`,
`vglrun -d egl Unity -batchmode -projectPath P -executeMethod ProbeRunner.Record`, no `-quit`, no
`-nographics`), the shader keyword runner's protocol with a closed output: Record turns
asynchronous shader compilation off, opens the scene and enters Play mode from a delayCall; only
the update handler leaves Play mode after the probe's finished code, waits 30 Edit-mode updates,
removes both handlers, closes its event output and quits. Hard deadline 1200 s; external
no-progress deadline 300 s, reset only by `runner-events.jsonl` and the completed `values.jsonl`.
Do not wrap `run.py` in with-heavy-job. A repeat directory is write-once; `native2` starts only
after `native1` passes `check`, which includes the actual compile graph against the preflight.
`publish` requires both repeats to pass and agree byte for byte and never overwrites.

`guards.py` refuses: a forced stop of any kind, an exit other than 0, a non-NVIDIA renderer, no
shutdown line, a launcher failure, a command that is not the planned one, prelaunch inputs that
differ from the prepared fixture, runner events other than record/enter/finished 0/leave/exit 0,
the Editor-side watchdog, a partial record, compile errors, shader errors or native faults in the
Editor log (shader warnings are listed in `shader-log.json`, not refused), a changed fixture
asset, a record that does not follow the schema case by case, authored settings that are not the
probe's, fixture shaders not found, a failed set-up, a context that is not the release/batchmode/
OpenGLCore/no SRP, unscoped error logs and missing diagnostics.

`preflight.py` compiles both scripts with the Editor's own Roslyn and the shader keyword batch
GPU capture's response files (`shaderkw-native-20261102a/native1`). It proves from that launch's
receipt that its Packages/ProjectSettings are these bytes, that the copy only changed the runner's
`m_AsyncShaderCompilation` afterwards, that neither project has another compile input under
Assets, and that each assembly folder has one script; only the Assembly-CSharp source path is
rebound. `Controls/controls.py` compiles the real scripts at C# 9 against fake Unity types and
drives them through the runner protocol; fake answers are not Unity's.

## Limits

* Built-in pipeline only, measured context only: batchmode Linux Editor, OpenGLCore, the
  fixture's quality and colour settings. SRP / custom pipelines and `Shader.globalRenderPipeline`
  changes are not covered.
* No backend-excluded SubShader (`exclude_renderers`/`only_renderers` for the active device): that
  the Editor imports such a SubShader without a shader error is unmeasured, and a shader error would
  refuse the whole capture. The pipeline-tagged first SubShader is the excluded-SubShader control
  (the shader keyword fixture imports a UniversalPipeline-tagged SubShader cleanly).
* The surface control decides RENDERER only if Unity's generated count is not 2; deferred,
  vertex-lit and replacement-shader selection, CustomRenderTexture pass validity and Material
  passes of other renderers are not read.
* Colour identification assumes the 8-bit codes survive the linear target (decode tolerance 8).
