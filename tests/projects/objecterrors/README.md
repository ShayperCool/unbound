# Object API errors

`Assets/Scripts/ObjectErrorsProbe.cs` writes values.jsonl in `$OBJECTERRORS_OUT`
(tests/value_probe.py): for every action, what it returns or throws (type and message) and the
log messages it writes with their log type. The scene's `Holder` leaves its serialized object
references unassigned (GameObject, Transform, Rigidbody, Collider, a behaviour and a scriptable
object script type, Material, Texture2D, Object, a private field, array and list elements, a
field of a nested serializable class), next to a non-serialized and an assigned control.

* `unassigned.*`, `added.*`, `instantiated.*`: those references in the scene, in a component
  added at runtime and in an instantiated copy: identity, Unity's null checks, `is null`,
  their type and what their members throw.
* `missing.*`: GetComponent and its relatives for a component the object lacks.
* `destroyed.*`: members of destroyed objects and destroyed objects passed as arguments.
* `argument.*`: Instantiate, Destroy, AddComponent, GetComponent, tags, SendMessage,
  CreateInstance, the finders and Transform hierarchy calls with bad arguments.

Measured in the Unity 6000.0.71f1 Editor on macOS in batch mode (three byte-identical captures;
none of the values depends on the OS or the graphics device):

* An unassigned serialized reference of an engine class (not a script class) is a placeholder
  object of the field's class: `== null` is true but it is not a null reference (`is null` is
  false, `??` keeps it), its instance id is 0, and its members throw
  UnassignedReferenceException naming the field and the script; a field of a nested class gets
  a placeholder without a message (NullReferenceException). Script-typed fields and fields of a
  component added at runtime are null references.
* GetComponent (generic, by type, by name, also through a component and on an inactive object)
  for an engine class the object lacks answers such a placeholder (MissingComponentException);
  for a script class, an interface and the InChildren/InParent queries, null.
* A destroyed object's members throw MissingReferenceException; as an argument the message
  ends " Parameter name: <name>".

Unbound gives the measured argument errors, tags, finders and destroyed-object messages. The
placeholder objects and member checks of every dead-object member are open
(`residuals.json`, Q530 slice 2).

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/objecterrors/run.py record --label A     # macOS: batch-mode Editor
    python3 tests/projects/objecterrors/run.py record --label B
    python3 tests/projects/objecterrors/run.py publish A B
    python3 tests/projects/objecterrors/run.py gate
