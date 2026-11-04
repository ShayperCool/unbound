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

At `before-render` (before the camera exists) and `after-render` it writes each subject's serialized
`stringTagMap` (through SerializedObject), `GetTag("RenderType", false|true, sentinel)` and
`GetTag("RenderType", false)`, shader and asset identity, raw and effective queue and the
`_BaseMap`/`_EmissionMap`/`_BumpMap` bindings, and, separately, the configured pipeline asset
(`GraphicsSettings.currentRenderPipeline`, default, quality), the actual
`RenderPipelineManager.currentPipeline` and `Shader.globalRenderPipeline`. The render is the normal
loop drawing the fixture camera or a `UniversalRenderPipeline.SingleCameraRequest` after
`SupportsRenderRequest`; completion is `endCameraRendering` for that camera. Camera.Render is not
used. Without a completion the run fails (exit 3); a failed render is not a tag observation.

The observer never writes the imported or package materials, never imports or reimports, never
validates through a ShaderGUI, and is not an AssetPostprocessor. Canonical rows hold stable values
only; instance IDs, process, paths, clocks, ticks and device go to a diagnostics file that is hashed,
not compared.

The project is the model import probe's URP composition (`modelimport/run.py copy_project`) plus
the fixture script beside `ProbeRunner.cs`; `inputs.json` lists every composed file with its hash
and whether it is an exact copy, a setting the composition derives, or the fixture.

    python3 tests/projects/materialtagstate/run.py compose --release 6000.0.71f1 --label <L>
    python3 tests/projects/materialtagstate/run.py editor --release 6000.0.71f1 --label <L>   # plan only
    MATERIAL_TAG_STATE_GRANT=<release file> python3 tests/projects/materialtagstate/run.py editor --release 6000.0.71f1 --label <L> --execute
    python3 tests/projects/materialtagstate/run.py check --release 6000.0.71f1 --label <L>
    python3 tests/projects/materialtagstate/compare.py validate|classify <capture.jsonl>
    python3 tests/projects/materialtagstate/compare.py pair <native1> <native2>
    python3 tests/projects/materialtagstate/compare.py saved <capture.jsonl> <saved-native-materials.json>
    python3 tests/projects/materialtagstate/mock_control.py --out <cache folder>      # comparator self-test
    python3 tests/projects/materialtagstate/bind_rsp_graph.py --release 6000.0.71f1 --saved <modelimport run>/projects --logs <modelimport run> --out <cache folder>

`bind_rsp_graph.py` proves which saved genuine Editor compile a response file belongs to and
derives a response file that compiles the fixture against exactly those references, defines and
analyzers (outputs redirected to the cache). No Reference is recorded yet.
