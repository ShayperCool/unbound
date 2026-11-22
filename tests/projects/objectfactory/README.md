# objectfactory

What `ObjectFactory` makes and changes in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/ObjectFactoryProbe.cs`, with `NeedsBody`, `OnlyOne` and `FactoryAsset` of
`Assets/Scripts/FactoryProbeTypes.cs`; two byte-identical macOS batch Editor captures; nothing
depends on the OS or the graphics device). In the loaded scene, after `Undo.IncrementCurrentGroup`, it
creates GameObjects with and without components (also in a scene with hide flags, and with a null
name), adds components (again, a required one, a `DisallowMultipleComponent` one twice, a
Transform, null arguments, a non-component type), makes every primitive, `CreateInstance`s a
ScriptableObject, a Material, a GameObject and a component type, places objects with and without a
parent, then undoes once. Each object is recorded by name, hide flags, scene, active state, layer,
parent, positions and components with their hide flags; each call with the `componentWasAdded`
events it raised, what it threw, the undo group's name and the scene's dirty state.

Measured: a new GameObject raises `componentWasAdded` for its Transform and joins the current undo
group named "Create NAME" (the group takes the name of its latest operation); each added component,
required ones first, raises it too and takes its GameObject's hide flags; in Play mode nothing is
Reset and the scene stays clean; adding a second component of a kind a GameObject holds one of
throws `ArgumentException` ("Can't add component 'Rigidbody' to A because such a component is already
added to the game object!", and for a `DisallowMultipleComponent` script "Can't add '<NULL>' to A
because a 'OnlyOne' is already added to the game object!") without a log; primitives have the
built-in mesh, Default-Material and the shape's collider; `CreateInstance` gives an unnamed object
(a Material with the error shader); `PlaceGameObject` puts an object under a parent at the parent's
position and leaves one without a parent where it is; one undo destroys everything the factory made
in the group.

```sh
python3 tests/projects/objectfactory/run.py record --label a
python3 tests/projects/objectfactory/run.py record --label b
python3 tests/projects/objectfactory/run.py publish a b
python3 tests/projects/objectfactory/run.py gate
```
