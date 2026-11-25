# animbindings

The bindings `AnimationUtility.GetAnimatableBindings` offers for each engine component type in Unity
6000.0.71f1 Play mode (`Assets/Scripts/AnimBindingsProbe.cs`; every built-in module enabled; two
identical Linux batch Editor captures). For each of 115 types the probe adds the component to a fresh
GameObject and records the float, discrete and object reference bindings of that type in the
Editor's order (`UNADDABLE` when `AddComponent` gives none, `NONE` when the type offers none). No
engine type offers a discrete binding; object references (materials, a mesh, a sprite) are offered
as object reference bindings.

Unbound lists an engine component's bindings from this measurement
(`managed/UnityEngine/Editor/AnimatableBindingsTable.cs`), and a float curve on a property its type
does not offer animates nothing (tests/projects/enginediscrete). Scripts keep their serialized
numbers and flags; an engine type outside the table keeps them too, with a notice.

The four renderers whose default material has shader properties (SpriteRenderer, SpriteMask,
TilemapRenderer, SpriteShapeRenderer) list `material.*` bindings first, in the order
tests/projects/animmaterialbindings measures.

```sh
python3 tests/projects/animbindings/run.py record --label a
python3 tests/projects/animbindings/run.py record --label b
python3 tests/projects/animbindings/run.py publish a b
python3 tests/projects/animbindings/run.py gate
```
