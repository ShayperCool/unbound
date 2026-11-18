# Scripts, hierarchies, Editor APIs and plugins of real projects

`Assets/Scripts/GameInteropProbe.cs` writes values.jsonl in `$GAMEINTEROP_OUT`
(tests/value_probe.py): what each action returns or throws, the log messages it writes (with
their log type) and the script callbacks it causes (`.events`). `ProbeLog` starts listening before
the scene loads, so what loading the scene logs is kept too (`scene_load.*`).

* `ctor.*`: scripts whose constructors all take arguments (`ArgOnlyBehaviour`, `ArgDerived` under
  a script base class with a parameterless constructor, `ArgOnlyData`), next to a control with a
  private parameterless constructor: from the scene, a prefab in Resources and its instance, a
  scene object's instance, AddComponent, `new GameObject(name, types)`, a ScriptableObject asset,
  CreateInstance (generic, by type, by name), Instantiate and JsonUtility. Rows give the fields
  (serialized, with initializers, non-serialized), whether the components exist and serialized
  references to them resolve, and how often each constructor ran (`counts`, since the row before).
* `hier.*`: hierarchy edits of a freshly loaded prefab asset (`Resources/Hier/Edit*`: Rig with
  Body/{Head,Arm} and Base/Foot): SetParent inside the asset, to no parent, to a scene object, a
  scene object under the asset, the `parent` setter, SetSiblingIndex, Instantiate under the asset
  (local and world), DetachChildren, a RectTransform, Destroy; an OnValidate on the loaded asset
  that reparents or creates a child (`LimbRig`, `ValidateReparent`, `ValidateAdd`); a rig nested
  in a box with an object added inside the instance, a variant of it, and the nested rig's
  OnValidate. Each gives the asset's hierarchy afterwards, where Head is, and the hierarchy of an
  instance made then (`!parent`/`!twice` would mark a transform listed under the wrong parent or
  twice).
* `reentrant.*`: a transform that moves itself under another parent from inside
  OnBeforeTransformParentChanged while a SetParent is under way (and from OnTransformParentChanged
  after it), with instances of the parents.
* `animator.*`: SetFloat, GetFloat, SetTrigger, SetBool by id, parameterCount, the state and Play
  on an Animator without a controller, on an inactive object, disabled, with an override
  controller that has nothing to override (`OverrideEmpty`, set in the scene file by hand: the
  Editor refuses to assign it), with an override controller and with its controller; then a
  controller assigned at run time.
* `stats.*`: `UnityStats.screenRes` has the form "WIDTHxHEIGHT".
* `compilation.*`: `CompilationPipeline.GetAssemblies` for the Editor and for players (the
  probe's assemblies: root namespace, flags, output, sources, references, plugins and compiler
  options; in the Editor's list an optimization equal to `CompilationPipeline.codeOptimization`,
  the machine's Editor preference, reads "editor"), `new ScriptCompilerOptions()`, and the
  `Assembly` constructors.
* `timeline.*`: `Cutscene.playable` (animation, activation, group, marker tracks; signal and
  ProbeMarker markers) and `CutsceneMissing.playable`, the same timeline whose ProbeMarker
  documents name a script GUID that does not exist.
* `plugins.*`: precompiled plugins (`Assets/Plugins`, built by `build_plugins.py` from
  `PluginSources/`): the project's ShapeCore is version 2.0.0.0; ShapeExtras was built against
  1.0.0.0, whose abstract `Shape.Area()` version 2 replaced by `Area(float)`, and ShapeLater
  against 3.0.0.0. Rows give what binds, what reflection lists and what creating each type does.
  Pentagon (a base with a virtual member before the abstract ones), Ruler (an interface) and
  FloatPlug (a generic base) lack members of version 2 too; `circle_*` rows use such a type's
  static member, base type and methods.
* `twice.*`: how often, on which thread and for which object the parameterless constructors of
  scripts run: two scene objects (`CountedBehaviour`), AddComponent, CreateInstance, a prefab in
  Resources and its instance.
* `rsp.*`: response files. `Assets/csc.rsp` (defines with `-define` and `-d`, `-unsafe`, `-r` of
  `Assets/RspPlugins/RspRef.dll`, whose Auto Reference is off, `-nowarn`), `Assets/RspLocal/csc.rsp`
  (`-define`, `-nullable:enable`, `-langversion:10`) and the embedded package `com.probe.rsp`'s
  `Local/csc.rsp`; assemblies without one of their own (Assembly-CSharp, its Editor assembly, an
  asmdef, an asmdef whose define constraint names a project define, the package's), one whose
  constraint names another assembly's define. Rows give each assembly's defines and nullable
  context, the unsafe, referenced and C# 10 code, `ParseResponseFile` and what
  `CompilationPipeline` reports.
* `async.*`: `InstantiateAsync` with a parent in a loaded prefab asset (one, several,
  `InstantiateParameters`) and a scene parent.
* `animator.runtime_invalid.*`: an override controller with nothing to override assigned to an
  active Animator at run time, then a valid one and the invalid one again.
* `editor_controller.*`: the class of a loaded `.controller` and what its Editor API reads.
* `objectchange.*`: `ObjectChangeEvents.changesPublished` streams of undoable operations, a
  plain change, two in one frame, undo and redo.
* `annotation.*`: `AnnotationUtility` (internal, by reflection): the table, SetIconEnabled and
  SetGizmoEnabled on an engine class, a gizmo script and an unlisted script.
* `builddefines.*`: a define added through the internal
  `BuildDefines.getScriptCompilationDefinesDelegates` from `[InitializeOnLoad]`
  (`Assets/Editor/ProbeDefinesHook.cs`): whether compiled code, `GetScriptCompilationDefines`,
  `activeScriptCompilationDefines` and the pipeline see it.
* `mat.*`: a material variant from a file (`Resources/MatVariant.mat` of `MatA`, shader
  `Assets/Shaders/ProbePlain.shader` without a ShaderGUI) whose queue and keywords a
  SerializedObject applies, and a SerializedObject over two materials.

`Assets/Editor/ProbeSetup.cs` authored the scene and assets with the Editor
(`-executeMethod ProbeSetup.Build`); `CutsceneMissing.playable` and the scene's `OverrideEmpty`
controller reference were then edited by hand as described above, and `ProbeSetup.Add` added the
counted objects, `Counted.prefab` and the materials. The engine runs 240 frames, enough for the
probe's frame waits.

Measured in the Unity 6000.0.71f1 Editor on macOS in batch mode (two byte-identical captures; none
of the values depends on the OS or the graphics device):

* A script whose constructors all take arguments is created everywhere the engine creates
  scripts; none of its constructors and no constructor of a script base class runs, so its fields
  stay at their type's defaults (null lists and strings) until deserialization fills the
  serialized ones; serialized references to it resolve; Awake and OnEnable run; nothing is
  logged.
* Scripts cannot change a loaded prefab asset's hierarchy: a parent inside the asset is refused
  with "Setting the parent of a transform which resides in a Prefab Asset is disabled to prevent
  data corruption" (DetachChildren adds the first child's name), an asset's transform moved out of
  it with "Transform resides in a Prefab asset and cannot be set to prevent data corruption", a
  RectTransform with a log; Instantiate with a parent in the asset makes a parentless copy in the
  active scene after "Cannot instantiate objects with a parent which is persistent". SetSiblingIndex
  is allowed. A GameObject made inside OnValidate warns that its OnDidAddComponent message cannot
  be sent.
* A SetParent whose OnBeforeTransformParentChanged moves the transform elsewhere still ends under
  the parent it asked for; the interim parent keeps no child.
* In batch mode `UnityStats.screenRes` is "0x0" (there is no Game view); Unbound answers its Game
  view, the screen.
* Timeline markers whose script is missing are no markers, without warnings at load.
* A precompiled plugin binds to the project's library by name whatever version it was built
  against; Mono lists and finds a type that lacks an implementation and fails only when it is
  used (created, its static members called, its members reflected).
* A scene script's parameterless constructor runs twice on Play mode entry (main thread, then a
  loading thread for the live object); scripts made later construct once.
* An assembly definition's assembly takes its folder's `csc.rsp`, else the project's (package
  assemblies too); the file's arguments follow the Editor's own, `-r` references are no listed
  plugins and its defines are no listed defines, though define constraints see them. A `#`
  comment line breaks the compile (found while authoring; the probe's files have none).
* `InstantiateAsync` with a parent in a prefab asset puts the copies under it, without a message.
* An empty override controller assigned at run time is refused with an error; the Animator keeps
  its controller.
* Undoable operations publish their changes in the frame's Editor update; undo publishes the
  group's changes in reverse order.
* A delegate's define reaches the pipeline's answers but not the code compiled before it.
* A SerializedObject over two materials writes only what changed into each; a variant's
  SerializedObject reads and applies its own stored queue while its renderQueue stays the
  parent's.

`residuals.json` lists what stays open, each with its cause: the text, wrapping and reflection of
a plugin type's deferred failure, the missing-script warning Unbound writes at its first load (the
Editor at import), the scene scripts' constructors the Editor runs twice on Play mode entry,
Unbound's undo that does not recreate destroyed objects, and the two Timeline classes the Gizmos
table lists. `run.py record` / `publish` / `gate` (tests/value_probe.py).
