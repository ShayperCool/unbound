# Material tag state probe

Whether stored `RenderType` overrides stay authoritative before and after an observed SRP render,
and whether imported materials already differ in stored overrides before any query (Unity
6000.0.71f1, URP 17.0.4). It observes; it does not explain which import step produces an override.

`MaterialTagStateDiscriminator` (Assets/Editor, run with `-executeMethod
MaterialTagStateDiscriminator.Run`) records seven subjects:

| Label | Material |
|---|---|
| fresh-opaque | new Material of URP Lit, `SetOverrideTag("RenderType", "Opaque")` |
| fresh-cleared | new Lit material set to Opaque, then cleared with `""` |
| fresh-transparent | new Lit material with override Transparent |
| legacy-M_Tex, legacy-M_Red | the existing imports in `Assets/Models/MaterialsLegacy.fbx` |
| description-M_Tex | the existing import in `Assets/Models/Materials.fbx` |
| package-Lit.mat | URP's `Runtime/Materials/Lit.mat`, untouched |

Run first resets the scene (`NewScene(EmptyScene, Single)`), before any subject exists, and then
waits for a startup settle predicate before it creates or loads a subject: one complete
`EditorApplication.update` cycle (from the fixture's previous update callback to the current one, so
every other update subscriber ran once inside it) with no `AssetPostprocessor.OnPostprocessAllAssets`
batch reporting an imported, deleted or moved asset, `EditorApplication.isCompiling` false and
`EditorApplication.isUpdating` false; without it within 600 callbacks the run fails before any
subject exists. On the first GPU run, URP's deferred material upgrader reimported every Material
asset, including both model subjects and `Lit.mat`, from an update callback after the old baseline.
Public Editor API cannot show whether such deferred work is still pending, so the predicate is not a
proof of quiescence. The measurement interval (subject creation to the last after-render digest) is
observed by the same recording-only postprocessor, `MaterialTagStateImportObserver`, which implements
nothing but the static callback. Any import batch or compilation inside it fails the run
(`fixture/status` `failed: import-during-measurement` / `compilation-during-measurement`, exit 3).
Every batch path, per phase (startup since the last domain load, settle, measurement), and the
per-callback settle trace go to the diagnostics. The baseline therefore follows initial automatic
import processing and the actual pipeline may already exist then. It is recorded as observed;
`render/pipelineCreatedDuringRun` counts creations after the baseline only.

At `before-render` (before the camera exists) and `after-render` it writes each subject's serialized
`stringTagMap` (through SerializedObject), `GetTag("RenderType", false|true, sentinel)` and
`GetTag("RenderType", false)`, shader and asset identity, raw and effective queue and the
`_BaseMap`/`_EmissionMap`/`_BumpMap` bindings, and, separately, the configured pipeline asset
(`GraphicsSettings.currentRenderPipeline`, default, quality), the actual
`RenderPipelineManager.currentPipeline` and `Shader.globalRenderPipeline`. The render is the normal
loop drawing the fixture camera or a `UniversalRenderPipeline.SingleCameraRequest` after
`SupportsRenderRequest`; completion is `endCameraRendering` for that camera. Camera.Render is not
used. Without a completion the run fails (exit 3); a failed render is not a tag observation. A
failure writes the render rows and `fixture/status` once and keeps every partial row.

The observer never writes the imported or package materials, never imports or reimports, and never
validates through a ShaderGUI. Its postprocessor has no per-asset import callback. Canonical rows
hold stable values only; instance IDs, process, paths, clocks, ticks and device go to a diagnostics
file that is hashed, not compared.

The project is the model import probe's URP composition (`modelimport/run.py copy_project`) plus
the fixture script beside `ProbeRunner.cs`; `inputs.json` lists every composed file with its hash
and whether it is an exact copy, a setting the composition derives, or the fixture.

    python3 tests/projects/materialtagstate/run.py compose --release 6000.0.71f1 --label <L>
    python3 tests/projects/materialtagstate/run.py editor --release 6000.0.71f1 --label <L>   # plan only
    MATERIAL_TAG_STATE_GRANT=<release file> python3 tests/projects/materialtagstate/run.py editor --release 6000.0.71f1 --label <L> --execute
    python3 tests/projects/materialtagstate/run.py check --release 6000.0.71f1 --label <L>
    python3 tests/projects/materialtagstate/run.py mutations --release 6000.0.71f1 --label <L> --project <run project>
    python3 tests/projects/materialtagstate/compare.py validate|classify <capture.jsonl>
    python3 tests/projects/materialtagstate/compare.py pair <native1> <native2>
    python3 tests/projects/materialtagstate/compare.py saved <capture.jsonl> <saved-native-materials.json>
    python3 tests/projects/materialtagstate/mock_control.py --out <cache folder>      # comparator self-test
    python3 tests/projects/materialtagstate/bind_rsp_graph.py --release 6000.0.71f1 --saved <modelimport run>/projects --logs <modelimport run> --out <cache folder>

`bind_rsp_graph.py` proves which saved genuine Editor compile a response file belongs to and
derives a response file that compiles the fixture against exactly those references, defines and
analyzers (outputs redirected to the cache). Relative Bee, package, compiler and analyzer paths are
resolved against the project the Csc action runs in, never the caller's working directory.

`authored_mutations.json` lists the exact authored-file changes an Editor run makes (measured once on
the first GPU run: package lock, InputManager, ProjectVersion, QualitySettings anti-aliasing 2 -> 0,
added ShaderGraphSettings and URPProjectSettings), each with prelaunch and post hashes. `run.py
mutations` and `check` fail on every unlisted, missing or different change. Child processes get
environments built from a fixed list of key names, never the parent environment. No Reference is
recorded yet.
