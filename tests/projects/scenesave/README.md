# Scene and asset saves

What Unbound writes into a project when editor code saves, compared byte for byte with the files
the Unity 6000.0.71f1 Editor writes (`Reference/unity-6000.0.71f1/`, recorded in the macOS Editor:
none of this depends on the OS or the graphics device). Each case runs the probe's tests with the
Unity Test Framework (unmodified) on a fresh copy of the project, in the Editor
(`-runTests`) or in Unbound (`unbound test`), and keeps the rows the tests log and every project
file the run added, changed or removed.

- `edit` (Edit Mode, `SaveEditTests`): a scene edited by script (transforms, script fields of every
  value kind in `Kinds`, a [SerializeReference] graph in `Graph`, a renamed and deactivated
  GameObject, a disabled renderer, a resized collider, a light's intensity, a new GameObject with a
  script and a collider, a prefab instance moved, its script changed without and with
  `PrefabUtility.RecordPrefabInstancePropertyModifications`, a component added to it) and saved
  as a copy, in place and to a new path; a second scene saved by `SaveOpenScenes` and
  `SaveScenes` before and after `MarkSceneDirty`, and asked about by
  `SaveCurrentModifiedScenesIfUserWantsTo`; a new scene with the default objects saved; a prefab
  made by `SaveAsPrefabAsset`, a prefab edited through `LoadPrefabContents`, an instance applied
  with `ApplyPrefabInstance`; ScriptableObject and material assets edited with and without
  `EditorUtility.SetDirty`, saved by `AssetDatabase.SaveAssets` or left for the Editor's exit.
- `prefab-api` (Edit Mode, `PrefabApiTests`): the stage and scene queries (current, main and an
  object's stage, scene culling masks, preview scenes, placing a GameObject in the current stage),
  an instance's prefab root, the instances of a prefab, an instance's property modifications and
  object overrides; SetPropertyModifications, ApplyPrefabInstance, ConvertToPrefabInstance and the
  obsolete CreatePrefab and ReplacePrefab with the prefabInstanceUpdated callbacks they raise;
  Undo.postprocessModifications on a recorded SerializedObject apply, and a SerializedObject's
  native pointer before and after Dispose.
- `play-nosave`, `play-save` (Play Mode, `SavePlayTests.NoSave` and `.Save`, each alone): assets
  edited in Play mode, left unsaved or saved by `AssetDatabase.SaveAssets` there; scene saves and
  `MarkSceneDirty` in Play mode.

Measured: `SaveScene` writes whether or not the scene is dirty, `SaveScenes` and
`SaveOpenScenes` only dirty scenes; script edits dirty no scene; in Play mode scene saves and
`MarkSceneDirty` throw; the modified-scenes dialog is cancelled with the batch-mode assertion and
answers false. A scene's documents are ordered by file id (settings first, SceneRoots last); a
prefab's by GameObject (GameObjects by file id, each followed by its components in component
order). Numbers are written with their shortest digits, with an exponent once the decimal exponent
reaches the type's precision (9 for floats, 17 for doubles) or falls below its negation; integer,
boolean and enum arrays as hex strings, float and double arrays as sequences; strings as libyaml
writes them without Unicode (escaped), plain text wrapped past column 80. Material edits dirty the
asset, script field edits do not; `SaveAssets` writes dirty assets in Edit and Play mode, the
Editor's exit writes those still dirty. A prefab instance records its engine components' changes
as modifications (ordered by target file id) and a script's only once recorded.

The Editor gives new objects random file ids (per session), new managed references ids from its
clock and new assets random GUIDs, and the Test Framework names its Play Mode scene with a GUID: two
Editor captures differ exactly there. The comparison (`scenesave_compare.py`) therefore maps those
ids before comparing bytes, and compares rows holding file hashes by which rows share a hash.
Everything else is exact. Differences named in `Reference/unity-6000.0.71f1/known-gaps.json`
(exact values, each with its reason) are queued; any other difference fails.

`defaults.unity` is the Editor's document for each built-in component class as a new component
(`-executeMethod ProbeSetup.Defaults`, one GameObject per class, named after it): Unbound starts a
new object's document from it.

Record (the Editor's two captures must compare identical; publish one):

    python3 tests/projects/scenesave/run.py editor --out <dir A>
    python3 tests/projects/scenesave/run.py editor --out <dir B>
    python3 tests/projects/scenesave/run.py compare <dir A> <dir B>
    SCENESAVE_DEFAULTS=<file> <Unity> -batchmode -nographics -projectPath tests/projects/scenesave \
        -executeMethod ProbeSetup.Defaults -quit

Check Unbound: `python3 tests/projects/scenesave/run.py gate --release 6000.0.71f1`.

The probe's own files were authored once by `-executeMethod ProbeSetup.Create`.
