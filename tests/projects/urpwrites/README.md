# Material writes in a URP project

Which project files a Play mode session writes when the project's materials were saved in states
that the Editor's material validation or the Universal Render Pipeline's material upgrade change.
Recorded with the Unity 6000.0.71f1 macOS Editor in batch mode (two identical captures): none of
this depends on the OS or the graphics device.

`make_materials.py` writes `Assets/Resources/M` from the URP Lit material the Editor wrote
(`Assets/Settings/Lit.mat`) and a Standard material of `tests/projects/materialvalidation`:

| material | saved state |
| --- | --- |
| `LitClean` | as the Editor writes it |
| `LitStale`, `LitStaleEdited` | transparent, without the alpha blend factors and with its disabled passes in an older order and spelling (`SHADOWCASTER`, `DepthOnly`, `MOTIONVECTORS`) |
| `LitPassesAddedEdited` | transparent without disabled passes |
| `LitOpaquePassesEdited` | opaque with the shadow caster and depth passes disabled |
| `LitV9`, `LitNoVersion` | the pipeline's material version 9, no version object |
| `StandardStale` | a Standard material with an emission colour and no `_EMISSION` keyword |
| `PassNamesEdited` | the probe disables sixteen passes by script and enables one again |

`Assets/Editor/UrpWritesProbe.cs` (`-executeMethod UrpWritesProbe.Record` / `unbound run`) enters
Play mode, loads the materials and records whether each is dirty, its keywords and its disabled passes (`load.*`), reads a property of
the Standard one (`access.*`), edits the `*Edited` ones by script (`edit.*`), and after 30 frames
whether each is still dirty (`played.*`). The project has no `URPProjectSettings.asset`, so the
pipeline's material reimporter runs and saves the assets during Play mode. `run.py` adds
`exit.<path>` for every file of `Assets/`, `Packages/` and `ProjectSettings/` after the process
ended: `unchanged`, or its text (GUIDs as the path they name, random file ids as their order, the
material's document first; `.idsAscending` records the file's order).

Measured: the Editor leaves a URP Lit material as saved when it loads it (`load.*.keywords`,
`load.*.passes`) and validates it only once it is changed and saved, so a material nobody changes
is not written (`LitStale`); the deferred validation of the Editor's own Standard editor on a property
read dirties the material, which is then written with the keyword it set. A material written keeps
its disabled passes in the saved order, matched case-insensitively; a pass disabled is added at
the end (the engine's own pass names in upper case, also saved ones, tests/projects/toolapis: `ALWAYS`, `FORWARDBASE`,
`FORWARDADD`, `DEFERRED`, `SHADOWCASTER`, `MOTIONVECTORS`, `META`, `GRABPASS`, `SRPDEFAULTUNLIT`;
`Vertex`, the pipeline's and other names as given), and a pass enabled again leaves its place to
the last entry. `ProjectSettings/URPProjectSettings.asset` is created in Play mode.

Residuals (`residuals.json`): Unbound validates the materials of pipeline and project shader
editors as they load (without dirtying them), so their keywords and passes are corrected from the
start and passes that validation disables come before those a script disables later
(`PassNamesEdited`); the Editor's first import of the project runs the pipeline's material
postprocessor on every material and saves the upgraded `LitV9` (version 10) and `LitNoVersion` (a
version object); Unbound does not import the opened project and writes neither.

Unmeasured: the spelling of other pass names (`VertexLM`, `PrepassBase`, ...: kept as given), a
saved list holding the same pass twice (Unbound keeps the first), a project whose
`URPProjectSettings.asset` is up to date (no save during Play mode; the edited materials are then
written when the Editor quits, as tests/projects/scenesave measures for edited assets).

Record (macOS, holding the machine's heavy-job lock), publish and check:

    python3 tests/projects/urpwrites/run.py native --label a
    python3 tests/projects/urpwrites/run.py native --label b
    python3 tests/projects/urpwrites/run.py publish a b
    python3 tests/projects/urpwrites/run.py check
