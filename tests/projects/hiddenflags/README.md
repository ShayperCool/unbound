# Public hide flags and root ownership

The control sets public flags on ordinary user roots in Play mode, then checks
component flags, child scene membership, disable/enable callbacks, restoration,
a Single scene load and stopping Play with domain reload disabled. Its reference
contains 549 values from two independent Unity 6000.0.71f1 Editor captures on the
existing NVIDIA display.

```bash
python3 tests/projects/hiddenflags/run.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/hiddenflags/run.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/path/to/immutable/unbound python3 tests/projects/hiddenflags/run.py check --release 6000.0.71f1
```

The runner stages private project copies and keeps all output in the run cache.
The gate runs the engine and compares every value exactly with Reference.

Unity 6000.0 [HideFlags](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HideFlags.html)
describes removing objects from their scene and sending OnDisable/OnEnable for
HideInHierarchy, DontSaveInEditor or DontSaveInBuild. The measured release is
narrower: a root detaches immediately when all three bits are present. Each
individual flag and each two-bit subset stays in its scene. The controls include
the three-bit value, DontSave combined with HideInHierarchy, and
[HideAndDontSave](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HideFlags.HideAndDontSave.html).

The scene is already invalid during both callbacks. The GameObject setter
propagates flags to its components, with Transform visited before the script
witness; the witness still sees its previous flags during those callbacks.
Children follow the root's scene ownership but retain their own flags. Disabled
and inactive scripts receive no extra enable. Clearing the detached root's
flags adopts the current active scene, which a separate control changes first,
and does not send another callback cycle.

DontSaveInEditor roots survive the Single load and stopping Play. The combined
hidden roots do too. HideInHierarchy alone does not confer that lifetime. The
control destroys every retained object explicitly after measuring Play exit,
as the documented ownership rule requires.

The compatibility profile selects these observations for 6000.0.71f1. Other
releases retain the earlier setter behavior and remain unmeasured. Changes to
flags on persistent assets, parented objects or standalone components, flag
restoration after a Single load, domain reload enabled, and detailed physics
query effects remain outside this capture.

Activation callbacks also check destruction and reentry. DestroyImmediate on the
callback's own GameObject or component is refused during a hierarchy transition,
but destroying a child's collider is allowed. SetActive reentry leaves activeSelf
unchanged. Disabling the Behaviour directly permits component destruction. These
controls supplement the documented
[DestroyImmediate restrictions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.DestroyImmediate.html);
they do not establish an ordering guarantee between unrelated objects.
