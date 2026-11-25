# OnPostprocessAllAssets as a project opens

What a project's import at the Editor's start gives `AssetPostprocessor.OnPostprocessAllAssets`,
in which order its callbacks run, and what the first open writes. Recorded with the Unity
6000.0.71f1 macOS Editor in batch mode (two identical captures): none of this depends on the OS or
the graphics device.

Documented (Scripting API 6000.0,
[OnPostprocessAllAssets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor.OnPostprocessAllAssets.html),
[RunAfterClassAttribute](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Callbacks.RunAfterClassAttribute.html)):
it is called after the import of any number of assets, with the imported, deleted and moved
assets; the `didDomainReload` form also after a domain reload; the RunAfter/RunBefore attributes
order the callbacks. Unity's own C# (6000.0, `Editor/Mono/AssetPostprocessor.cs`
`PostprocessAllAssets`, `Editor/Mono/OrderedCallbackCollection.cs`) calls the `didDomainReload`
form always and the four-argument form only when a list holds an asset, and orders the callbacks
by Kahn's algorithm over the classes sorted by full name.

The project is a URP project (`ProjectSettings/URPProjectSettings.asset` up to date, so the
pipeline does not re-import every material) with materials in `Assets/M` (`LitV9`, saved by the
pipeline's material version 9; `LitNoVersion`, without a version object; the others as the Editor
writes them), a folder, a texture, an audio clip and a model in `Assets/Types`, and the recorder
`Assets/Editor/FirstImportProbe.cs`: seven postprocessor classes, `AAAFive` (first by name, the
`didDomainReload` form), `AAAFour` (the four-argument form), `aLower` and `BUpper` (an ordinal
order and a culture's disagree), `ZZZFive`, `ZDependent` (`RunAfterClass(AAAFive)`) and `ZLast`
(`RunAfterClass` of the pipeline's `MaterialPostprocessor`). Each call records its class, form,
`didDomainReload`, Edit or Play mode and the version LitV9 holds; the first class of a dispatch
also its lists (`imported.assets` and `imported.settings` sorted, `imported.packages` a count,
`imported.other`, `deleted`, `moved`, `counts`) and, in Edit mode, the main asset type of each of
`Assets/Types` and whether asking for it loaded the asset (`types.*`).

`run.py` opens one copy of the project three times (the Editor with `-executeMethod
FirstImportProbe.Record`, which enters Play mode; `unbound run`; each leaves after a few Play mode
frames): `open1` with nothing imported (no `Library/`, no Unbound project directory), `open2` with
nothing changed, `open3` after changes made outside: `Changed.mat` edited, `New.mat` (a copy of
LitV9 without a `.meta`) added, `Gone.mat` deleted, `Mover.mat` moved into a new folder
`Assets/Moved`, `Touched.mat`'s time changed alone, a label added to `Labelled.mat.meta`, a tag
added to `ProjectSettings/TagManager.asset`. After each open it adds every file of the project
(`<open>.exit.<path>`, `tests/file_writes.py`).

Measured: the start's dispatch (`start.*`, Edit mode, `didDomainReload` true) lists every asset at
the first open (the project's assets and folders with `Assets` itself, the packages' files and
folders, the settings files `ProjectSettings/<manager>.asset`, and two files of the Editor's
`Library/`), nothing at the second (the four-argument form is not called), and at the third the
files changed (content or `.meta`, also a settings file) and the new ones (with the new folder),
the deleted one and the moved one (not imported); a file only touched is not listed. The callbacks
run with no RunAfter/RunBefore first, by full name in a culture's order (`aLower` before
`BUpper`), then each one following another as that one has run: `ZDependent` after `ZZZFive`, the
pipeline's material postprocessor after its `UniversalRenderPipelineGlobalSettingsPostprocessor`
(LitV9 is still version 9 for `ZZZFive` and `ZDependent`, 10 for `ZLast`). Play mode's domain
reload lists nothing. After the first open's dispatch the Editor writes the assets its
postprocessors changed (LitV9 upgraded, LitNoVersion given a version object, its documents by file
id) and imports them again (`session.0`); at a later open the changed assets stay dirty until saved
(the pipeline saves `New.mat` in Play mode, `open3.session.0`). `GetMainAssetTypeAtPath` answers
the types of a folder, a texture, an audio clip and a model without loading them.

Residuals (`residuals.json`): the two `Library/` files the Editor lists, and the Editor's imports
of the files saved during a session (`session.*`), which Unbound does not make.

Record (macOS, holding the machine's heavy-job lock), publish and check:

    python3 tests/projects/firstimport/run.py native --label a
    python3 tests/projects/firstimport/run.py native --label b
    python3 tests/projects/firstimport/run.py publish a b
    python3 tests/projects/firstimport/run.py check
