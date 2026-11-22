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
  Undo.postprocessModifications on a recorded SerializedObject apply (handlers run when Undo's
  records are flushed, after the values are applied; a filtering or throwing handler changes no
  value, a throwing one is logged), and a SerializedObject's native pointer before and after
  Dispose.
- `prefab-writes` (Edit Mode, `PrefabWriteTests`): one override at a time on an instance, with
  the prefabInstanceUpdated calls, the ObjectChangeEvents of the next Editor update and the log
  messages of each step: ApplyPropertyOverride (then undone and redone), RevertPropertyOverride,
  ApplyAddedComponent and RevertAddedComponent, a removed component reverted and applied,
  ApplyAddedGameObject and RevertAddedGameObject, ObjectOverride.Apply and Revert,
  RevertPrefabInstance, SavePrefabAsset of an edited asset; the writes that make new prefabs
  (SaveAsPrefabAsset of an instance, which makes a variant, and of a prefab's contents to a new
  path, SaveAsPrefabAssetAndConnect), their argument errors, a prefab's contents saved in place;
  EditorSceneManager.ReloadScene of a dirty, a clean, an unsaved and an invalid scene.
- `create-assets` (Edit Mode, `CreateAssetTests`): assets made of objects created by script with
  `AssetDatabase.CreateAsset` (materials of two built-in shaders, one edited in every field a file
  has, with keywords, tags, a disabled pass, a texture and properties its shader does not declare,
  a copy of a material asset, one saved as `.asset`; a mesh, an animation clip, a physics material
  and a texture), a material edited and saved by `SaveAssets`, materials added by
  `AddObjectToAsset` to a material asset and to a ScriptableObject asset; `SaveAsPrefabAssetAndConnect`
  of a GameObject named otherwise than the file and of one referencing a scene object outside its
  hierarchy, with the new scene they are in saved. Recorded in the Linux Editor (batch mode, no
  graphics: two identical captures). The texture `Assets/Textures/Probe.png` is a committed asset.
- `engine-assets` (Edit Mode, `EngineAssetTests`): meshes (every common channel, two submeshes and
  32-bit indices; blend shapes; bone weights and bind poses; a two-stream half-float layout;
  lines; meshes that differ in one trait each, showing which keep their vertex and index data),
  animation clips (rotation, scale, script, GameObject and object reference curves, an
  event, clip settings; a legacy clip) and textures (mipmaps and sampler settings, a linear float
  format, Alpha8) made assets by `CreateAsset`, then a mesh and a texture changed and saved.
- `prefab-nested` (Edit Mode, `NestedPrefabTests`): a new prefab holding an instance with
  overrides, an added component and an added GameObject; `SaveAsPrefabAssetAndConnect` of a
  hierarchy holding an instance; a variant of an instance with an added component, an added
  GameObject and a removed component, and a variant of that variant; their scene saved.
- `settings-writes` (Edit Mode, `SettingsWriteTests`): `EditorBuildSettings` config objects,
  `PlayerSettings.SetPreloadedAssets`, rendering layer names and tags and layers through the
  TagManager's SerializedObject: each file right after the calls, after `SaveAssets` and at exit.
  The last three cases were recorded in the Linux Editor (batch mode, no graphics: two identical
  captures each).
- `mesh-bounds` (Edit Mode, `MeshBoundsTests`): a mesh's bounds and each sub-mesh's bounds and
  vertex range after each of its setters (vertices of the same and another count, `SetVertices`
  with and without `DontRecalculateBounds`, `SetVertexBufferData`, triangles, `SetTriangles` and
  `SetIndices` with and without `calculateBounds`, `SetSubMesh`, assigned bounds, `Clear`,
  `UploadMeshData`) and for half-float positions. Rows only.
- `native-saves` (Edit Mode, `NativeSaveTests`): assets read from their files (`Assets/Native`: a
  mesh, a texture, a physics material, an animation clip, a model's mesh and an untouched mesh)
  changed through their APIs, with their dirty flags, then `SaveAssets`: which files it writes and
  what the save logs. The last two cases were recorded in the Linux Editor (batch mode, no
  graphics: two identical captures each).
- `clip-assets` (Edit Mode, `ClipAssetTests`): animation clips made by script and made assets by
  `CreateAsset`, each with another set of curves: kinds set out of order on several paths, constant
  and changing curves, vector components with other key times or missing, Euler angles, material,
  blend shape, sorting order and other classes' curves, object reference curves, curves set through
  `AnimationUtility.SetEditorCurve`, material properties, clip settings or events set after `SetCurve`, an empty clip;
  each with the curve bindings `AnimationUtility` lists. Recorded in the Linux Editor (batch mode,
  no graphics: two identical captures).
- `controller-assets` (Edit Mode, `ControllerAssetTests`): animator controllers made through the
  Editor's controller API: the file `CreateAnimatorControllerAtPath` writes at once, the file after
  edits (parameters, a second layer, states with clips, the default state, transitions with
  conditions from a state, Any State and Entry and to Exit, two sub-state machines and transitions out
  of them (the later one's first), a blend tree, a synced layer overriding a motion)
  before and after `SaveAssets`, and a controller made with a clip. Recorded in the Linux Editor
  (batch mode, no graphics: two identical captures).
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
as modifications (ordered by target file id) and a script's only once recorded. A scene saved to a
new path is imported as the Editor imports a new file (tests/projects/projectwrites): its `.meta`
is the DefaultImporter's; in a release family without measured `.meta` files Unbound writes the
scene, warns and writes no `.meta`.

The Editor gives new objects random file ids (per session), new managed references ids from its
clock and new assets random GUIDs, and the Test Framework names its Play Mode scene with a GUID: two
Editor captures differ exactly there. The comparison (`scenesave_compare.py`) therefore maps those
ids before comparing bytes (a reference into an asset the run created by that asset's GUID and file
ids), and compares rows holding file hashes by which rows share a hash.
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
