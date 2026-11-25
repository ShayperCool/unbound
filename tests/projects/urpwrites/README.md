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
| `LitStaleRead`, `LitCleanRead`, `LitStaleMeta`, `LitStaleKeyword`, `LitStaleDrawn`, `LitStaleSetPass`, `LitStaleShader`, `LitStaleCopy`, `LitStaleSrcBlend` | transparent and stale like `LitStale` (`LitCleanRead` as the Editor writes it), each used once in one way (below) |
| `LitCleanSame` | as the Editor writes it; the probe writes `_Smoothness` with the value it holds |

`Assets/Editor/UrpWritesProbe.cs` (`-executeMethod UrpWritesProbe.Record` / `unbound run`) enters
Play mode, loads the materials and records whether each is dirty, its keywords and its disabled passes (`load.*`), reads a property of
the Standard one (`access.*`), edits the `*Edited` ones by script (`edit.*`), uses each of the
first-use cases once: reads of `LitStaleMeta`'s queue, tag, keyword, pass count, enabled keywords and
a pass (`meta.*`), a local keyword set on `LitStaleKeyword` (`keyword.*`), a float read of
`LitStaleRead` and `LitCleanRead` (`read.*`), `SetPass(0)` (`setPass.*`), its own shader set again
(`shader.*`), a copy made with `new Material` (`copy.*`), `_SrcBlend` written as 7 (`edit.LitStaleSrcBlend`:
its data's value and the value read), `LitCleanSame`'s write (`same.*`) and `LitStaleDrawn` drawn by a
camera (`drawn.frames` says frames rendered), and after 30 frames whether each is still dirty
(`played.*`). The project has no `URPProjectSettings.asset`, so the
pipeline's material reimporter runs and saves the assets during Play mode. `run.py` adds
`exit.<path>` for every file of `Assets/`, `Packages/` and `ProjectSettings/` after the process
ended: `unchanged`, or its text (GUIDs as the path they name, random file ids as their order, the
material's document first; `.idsAscending` records the file's order).

Measured: the Editor leaves a URP Lit material as saved when it loads it (`load.*.keywords`,
`load.*.passes`) and validates it with its shader's editor at its first property value access
through the Material API: a float read (`read.LitStaleRead`), a write (`edit.*`), `SetPass`, a shader
set, also the same shader (`shader.*`). Reads of its keywords, queue, tag and passes, a keyword
change, a copy and drawing do not validate it (`meta.*`, `keyword.*`, `copy.*`,
`played.LitStaleDrawn`), so a material nobody uses that way is not written (`LitStale`). The
validation sees a value written; the material then reads that value while its data, which is saved,
holds the validation's (`edit.LitStaleSrcBlend.srcBlendRead` 7, `.srcBlend` 1). A validation that
changes a material dirties it; one that changes nothing, and a write of the value a float holds,
leave it clean (`read.LitCleanRead`, `same.LitCleanSame`). The deferred validation of the Editor's own
Standard editor on a property read dirties the material, which is then written with the keyword it
set. A material written keeps its disabled passes in the saved order, matched case-insensitively; a
pass disabled is added at the end (the engine's own pass names in upper case, also saved ones,
tests/projects/toolapis: `ALWAYS`, `FORWARDBASE`, `FORWARDADD`, `DEFERRED`, `SHADOWCASTER`,
`MOTIONVECTORS`, `META`, `GRABPASS`, `SRPDEFAULTUNLIT`; `Vertex`, the pipeline's and other names as
given), and a pass enabled again leaves its place to the last entry; passes a script disables before
the first use come before those its validation disables (`PassNamesEdited`).
`ProjectSettings/URPProjectSettings.asset` is created in Play mode.

Residuals (`residuals.json`): the Editor's first import of the project runs the pipeline's material
postprocessor on every material and saves the upgraded `LitV9` (version 10) and `LitNoVersion` (a
version object); Unbound does not import the opened project and writes neither.

Unmeasured: when the Editor validates a material again after its first use (a project editor counted
a second validation at a later access in a later frame, tests/projects/materialvalidation
`ProjectGui._Value`; Unbound validates once), whether a write of another kind than a float keeps
the value written while its data holds the validation's (Unbound: the validation's), the spelling of
other pass names (`VertexLM`, `PrepassBase`, ...: kept as given), a saved list holding the same pass
twice (Unbound keeps the first), a project whose
`URPProjectSettings.asset` is up to date (no save during Play mode; the edited materials are then
written when the Editor quits, as tests/projects/scenesave measures for edited assets).

Record (macOS, holding the machine's heavy-job lock), publish and check:

    python3 tests/projects/urpwrites/run.py native --label a
    python3 tests/projects/urpwrites/run.py native --label b
    python3 tests/projects/urpwrites/run.py publish a b
    python3 tests/projects/urpwrites/run.py check
