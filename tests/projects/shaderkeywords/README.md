# Shader keyword declarations and shader metadata

Unity 6000.0.71f1, built-in pipeline (`Assets/Scripts/ShaderKeywordsProbe.cs`). The canonical
`Reference/unity-6000.0.71f1.jsonl` contains 1510 rows from two independent, byte-identical
batchmode GPU captures. Both exited 0 without a forced stop on NVIDIA RTX 4060. This is a
measured API contract for that capture context. The matching Unbound package is admitted for
the measured declarations, keyword and material-variant behavior, with the gaps below retained.

## Fixture

The probe shaders declare keywords in every form: `KwForms` has `multi_compile`,
`multi_compile_local` with `_` and `__`, `shader_feature(_local)`, stage suffixes (`_vertex`,
`_local_fragment`), `dynamic_branch(_local)(_fragment)`, a keyword global in one pass and local
in another, a built-in name declared by the user (`LIGHTMAP_ON`), `multi_compile_fwdbase`,
`_fog`, `_shadowcaster`, `_instancing`, `skip_variants`, a declaration inside
`#if defined(SHADER_API_D3D11)`, an `#include_with_pragmas` file and a plain `#include` with
pragmas, a second SubShader for another pipeline, a tagless pass and a `Fallback`. `KwPlain` has
a CG pass and a fixed-function pass, `KwHlsl` an HLSLPROGRAM pass without includes, `KwSurface`
a surface shader with its own declarations, `KwFixed` three fixed-function SubShaders with LODs
and tags. `Resources/KwCompute.compute` declares global, local and dynamic keywords.
`KwSaved.mat` has saved valid and invalid keywords. `VarParent.mat` locks `_Other`,
`VarChild.mat` is its variant (overrides and locks `_Color`) and `VarGrand.mat` the child's
variant (overrides `_Value` and the render queue). The scene, materials and settings are the
bytes an Editor serialized for the earlier draft of this probe; they are reused as they are and
never regenerated (there is no setup Editor run).

## Record

`values.jsonl`, one `{"k": key, "v": value}` per line, in a fixed order. Each case is isolated:
a read records its value under its key or, when it throws, the exception text under
`key.exception`; a step (a call made for its effect) records only its exception; a block ends
with `block.log`, the log messages its cases caused. Expected API errors and exceptions are data.
The last lines are `capture.unscopedLogs` (logs outside every block) and the single marker
`{"k":"complete","v":true}`. The file is written once, through a temporary name.

Groups:

* `context.*`: Unity version, batchmode, platform, graphics device type and shader level,
  active quality level/name/shadows, color space, render pipeline. The enabled global keywords
  and platform-conditional declarations hold for the recorded context only: batchmode
  LinuxEditor, OpenGLCore shader level 50, NVIDIA RTX 4060, Ultra quality level 5, All shadows,
  Linear color space and the built-in pipeline.
* `probe.<shader>.*` (five probe shaders) and `builtin.<shader>.*` (32 built-in shaders):
  `found`; `meta`: `subshaderCount`, `passCount`, `GetPropertyCount` and each property's name,
  type and flags, `GetPassCountInSubshader` per SubShader, `FindSubshaderTagValue` (RenderType,
  Queue, RenderPipeline, ProbeTag, IgnoreProjector, PreviewType, DisableBatching, LightMode),
  `FindPassTagValue(LightMode)` per pass; `space`: `keywordCount`, `keywordNames`, every
  keyword's type/isDynamic/isOverridable/isValid, `FindKeyword` for each declared name (and a
  list of undeclared/conditional ones for the probe shaders); `outOfRange` (probe shaders):
  `GetPassCountInSubshader(-1)`/`(count)`, `FindSubshaderTagValue(count)`/`(-1)`.
* `compute.*`: the compute shader's space; keywords enabled by LocalKeyword and by an
  undeclared name; `shaderKeywords`, `enabledKeywords`, `IsKeywordEnabled`, space equality.
* `local.*`: a default LocalKeyword's `isValid`, name and `ToString`; constructors (declared,
  undeclared, a global declaration, another shader's, a null shader) and equality.
* `global.*`: the initially enabled set, which names exist, the built-in ones by type,
  `GlobalKeyword.Create`, a GlobalKeyword for an unknown name, enabling and disabling by name,
  by GlobalKeyword and by `SetKeyword`. Global sets are recorded sorted: the registry is
  process-wide and filled while the Editor loads shaders, so its order is not measured.
* `mat.*`: a new material's keywords set by LocalKeyword and by name (undeclared names
  included), after a shader change and back, `shaderKeywords` with duplicates, empty and
  undeclared names, `enabledKeywords` set, another shader's keyword, a `[Toggle]` drawer.
* `saved.*`: `KwSaved.mat` and its copy, then the copy on another shader.
* `variant.*`: `isVariant`, `parent`, `IsChildOf`, `IsPropertyOverriden`, `IsPropertyLocked`,
  `IsPropertyLockedByAncestor` and values for the three assets, an instance and a copy, after
  `RevertPropertyOverride`, `RevertAllPropertyOverrides`, a set of a locked property and
  `SetPropertyLock`, and a material made a variant from script.

Excluded on purpose: the type, isDynamic and isOverridable of an invalid or default
LocalKeyword (only valid keywords have them read; a default keyword's `isValid` and name are
read), `Material.SetKeyword` with a default LocalKeyword (an Editor of the draft crashed right
after these two reads), CommandBuffer keyword calls, and the order of the global registry.

## Measured capture and limits

All five probe shaders, 32 built-in shaders, the compute shader and the saved material family
were found. The record retains 11 expected per-case exceptions and five non-empty block logs;
the unscoped log list is empty. Both invalid `FindSubshaderTagValue` indices throw
`ArgumentOutOfRangeException`. `D3D_ONLY` is invalid in all five probe shaders on OpenGLCore.
The compute space contains `CS_LOCAL`, `CS_GLOBAL_A`, `CS_GLOBAL_B`, `CS_DYN` and `LIGHTMAP_ON`
in that order. The exact keyword, material and variant answers are in the canonical.

The runner disables asynchronous shader compilation through `EditorSettings`. At shutdown,
both private Editor copies saved that change to `ProjectSettings/EditorSettings.asset`.
Prelaunch settings were byte-identical to the fixture, and Assets were unchanged. Both actual
batch compiler rsp graphs match the preflight: references, sources, defines, analyzers and
compiler bytes are equal; only the private project path is rebound. The Editor uses its
compiler server, which the offline preflight omits. Other releases, backends, quality settings,
color spaces and windowed captures remain unmeasured.

## Measured implementation and limits

The matching-runtime comparison at source `b43816e9` passes 1485/1510 canonical keys, up
from 622 matching keys. It resolves 863 reference keys and removes all 11 former extras,
with zero lost matches, missing keys or extra keys. All material, compute, local/global and
variant groups pass. The remaining 25 keys are eight pass counts, four property/count answers,
two terrain pass-tag lists, ten exception-message strings and `context.isBatchMode`.
These are retained failures, with raw answers and exact comparison unchanged.

The implementation uses the following measured rules in this fixture. Source-derived keyword
ordering and built-in global defaults are enabled only for exactly Unity `6000.0.71f1`.
They never gate capture publication.

* A shader's keyword space starts with the four stereo keywords (`BuiltinDefault`); other
  keywords are `UserDefined`, built-in sets included. Each pass adds its new keywords stage by
  stage; within a stage: global declarations in source order, then the fog and instancing sets,
  then local declarations, then the light pass sets. `skip_variants`, inactive `#if` branches
  (here `D3D_ONLY` on OpenGL Core) and plain `#include`s declare nothing. A fixed-function pass
  declares the fog set; a surface shader those of its generated passes. A compute shader lists
  its local keywords, then its global ones. Built-in shaders' spaces equal the keyword lists of
  their serialized shaders.
* `isOverridable` is true for global declarations, `isDynamic` for `dynamic_branch`. The
  LocalKeyword constructor logs "Local keyword X doesn't exist in the shader." for an
  undeclared name; a default keyword's `isValid` logs "LocalKeyword not initialized.".
* Global keywords are the engine's built-in ones and those made by `GlobalKeyword.Create` or
  `Shader.EnableKeyword(string)`; shader declarations make none. `new GlobalKeyword(name)` for
  another name logs "Global keyword X doesn't exist." and enabling it does nothing.
* A material keeps every keyword name it is given; `IsKeywordEnabled` and `enabledKeywords` see
  only declared ones; `shaderKeywords` lists the declared ones sorted, then the others sorted,
  as often as given. A compute shader ignores names it does not declare.
* `subshaderCount` counts the Fallback chain's SubShaders after the shader's own;
  `GetPassCountInSubshader` is -1 out of range; `FindSubshaderTagValue` throws
  `ArgumentOutOfRangeException` for both out-of-range indices (Unity's public C# reference
  checks `subshaderIndex < 0 || subshaderIndex >= subshaderCount` before the native call).
  Tags read back as stored (built-in light modes upper-cased, boolean tags lower-cased).
* A variant overrides the properties it saves except those an ancestor locks; instances and
  copies keep parent, overrides and locks; reverting takes the parent's value, reverting all
  also the render queue.

The source-reader cross-check covers 196 built-in shader sources: 103 keyword spaces are
known and exact against Unity's serialized declarations, 93 are explicitly unknown, and none
is known-but-wrong. Unknown includes surface options, fixed-function `Lighting On`, Fallbacks
that add keywords, forward-base options, GLSL, light-pass order and fixed-function fog modes.
Eleven forms that matched in an earlier unaccepted reader are now unknown, including eight
terrain shaders and the source Fallback paths of Standard, Standard Specular and Autodesk
Interactive. Unknown, unreadable or unmeasured spaces retain name-based behavior with a
warning. An empty known space is distinct from unknown. Direct serialized built-in answers
and source-derived answers are checked separately. Mid-session shader/compute/include edits,
other contexts, headless pass/property answers and variant `Resources.UnloadAsset` remain
unmeasured; CommandBuffer keyword calls retain their previous behavior.

Validation used 49 Rust test results, a fresh API check with 0 errors over 2319 types,
108 byte-equal generated files, and the full managed suite at 1620/1631 with the historical
11 failures unchanged. A red control on the previous Core fails exactly the four variant
assertions and the new inline-property-reader test that the current Core passes. The logger
test passes on both Cores after observing the console diagnostic rather than game callbacks.

All 11 gate groups completed, with 46 comparisons per side. Control captures are byte-equal
except `builtinfx/flare_LensFlareObject.png`, an existing expected fade-in control using frame
time: 13264 pixels differ between runs, maximum channel delta 9/255. Its mean maximum-channel
error against Native improves from 11.59 to 9.62/255. Frame-time variation is consistent with
this difference but was not isolated by a repeat. No tolerance or expected-image list changed.
The full comparison, remaining keys and controls are in the cache receipts
`validation/runs/shaderkw-matching-validation-5-20261102/` and
`validation/runs/shaderkw-root-admission-20261102/`.

Values of the draft's failed Editor runs are diagnostics only, never expected values. They
also showed that the draft wrote `global.all.contains[STRING_ENABLED_KW]` twice (now
`global.after.contains[...]`) and that a throwing case ended its block early (now isolated).

## Capture

The Editor runs in batchmode with graphics: `EditorJob(batch=True, graphics=True)` from
`tests/editor_runner.py`, i.e. `vglrun -d egl Unity -batchmode -projectPath P -executeMethod
ProbeRunner.Record -logFile L` on a private Xvfb inside one installed GPU lease, with no
`-nographics` and no `-quit` (Play mode is asynchronous; `ProbeRunner` quits). Nothing depends
on a Game view. `ProbeRunner.Record` opens the scene and asks for Play mode from a delayCall. The
probe writes its record and then sets `ShaderKeywordsProbe.finishedCode`. Only an
`EditorApplication.update` handler changes state: it leaves Play mode, waits 30 Edit-mode
updates and quits with the code. The probe's coroutine and the play mode state callback only
record. The handler also clears Error Pause (expected errors are kept as data, not suppressed)
and quits with 4 600 s after Record (watchdog). Every transition goes to
`capture/runner-events.jsonl`. The supervisor's hard deadline is 1200 s. Its external
no-progress deadline is 300 s, including cold import. Only the runner event file and the
completed values file reset it. Editor log activity does not. A forced stop still rejects
the capture, even when the probe wrote its completion marker.

```
python3 tests/projects/shaderkeywords/preflight.py --rsp-dir <copy>/Library/Bee/artifacts/<hash>.dag --out <cache>/preflight
python3 tests/projects/shaderkeywords/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native1 --preflight <cache>/preflight/preflight.json [--dry-run]
python3 tests/projects/shaderkeywords/run.py editor --release 6000.0.71f1 --label <fresh> --repeat native2 --preflight <cache>/preflight/preflight.json
python3 tests/projects/shaderkeywords/run.py publish --release 6000.0.71f1 --label <fresh>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/shaderkeywords/run.py unbound --release 6000.0.71f1 --label <fresh>
~/.cache/compat/with-heavy-job --kind tool python3 tests/projects/shaderkeywords/compare.py \
    tests/projects/shaderkeywords/Reference/unity-6000.0.71f1.jsonl <cache>/unbound --report <cache>/report.json
```

Do not wrap `run.py` in with-heavy-job: each Editor takes its own lease. A repeat directory is
write-once (no retry inside a label); `native2` starts only after `native1` passes `check`, and
no Editor starts without a passing preflight whose compiled sources equal the checkout's.

## Gates

`guards.py` (used by `check` and `publish`) refuses a capture on any of: an Editor result with
a forced stop of any kind (the shutdown guard included), an exit status other than 0, a renderer
that is not the NVIDIA GPU, no logged shutdown, a launcher failure; a command that is not the
planned batchmode GPU Record command of the selected release (no `-quit`, no `-nographics`);
a prelaunch receipt whose release, capture directory or input bytes differ from the prepared
fixture; runner events other than record, enter, finished 0, leave, exit 0 in order; an
Editor-side watchdog or a partial record; compile errors, shader errors or native faults in the
Editor log; a changed fixture file under Assets; and a record that does not follow the schema
case by case (missing/extra/duplicate key, wrong value type, probe fault, missing group or
block log, marker not single and last, fatal, unscoped error logs, a probe shader or fixture
asset not found, context not the selected release/batchmode/OpenGLCore/built-in). The schema is
derived from the probe source, not from any capture; no observed keyword, order or answer is
gated. `publish` additionally requires both repeats byte-identical and never overwrites.
`compare.py` compares the whole key union exactly (no tolerance) and keeps every missing, extra
and differing key with both values.

`preflight.py` compiles the two scripts with the Editor's own Roslyn and this fixture's own
`Assembly-CSharp(-Editor).rsp` from an earlier Editor-compiled copy, after proving the copy's
Packages, ProjectSettings and non-script Assets are this fixture's bytes; only paths are
rebound. `Controls/controls.py` (run under `with-heavy-job --kind tool`) compiles the real
scripts at C# 9 against fake Unity types and drives them through the runner protocol (reloads,
Error Pause, watchdog, fatal, no output), then checks positive captures and mocked negative
controls of every guard, the pair gate, launch admission, the preflight judgement and the
comparator. Fake answers are not Unity's: the controls prove emission, schema and control flow
only.
