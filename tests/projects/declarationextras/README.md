# Slide results, struct copies and hierarchy equality

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 13 rows. The values are declarations, serialized
text and copied values (no physics step), so they do not depend on the OS or the graphics device.
The gate compares every missing, extra or unequal row exactly.

- `Rigidbody2D.SlideResults` marks all five backing fields `[SerializeField]`, the two hits
  included. `RaycastHit2D` is not a serializable type, so the hits are never written:
  JsonUtility and EditorJsonUtility text holds the velocity, position and iteration count only.
  FromJson leaves the hits at their defaults, and FromJsonOverwrite leaves a target's differing
  hits as they were. Instantiate of a component holding slide results gives the copy default
  hits.
- Instantiate copies a serializable struct's serialized members over the copy's own value.
  `clone.pair` gives the non-serialized members of the struct, and of a struct nested in it, the
  values the copy's field initializer gave them (9 and 4, not the original's 42 and 43). A new
  array element starts from the default (0).
- `VisualElement.Hierarchy` implements no interface. The default equality comparer still compares
  hierarchies by owner, through `Equals(object)`, and hashes them by owner.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [Rigidbody2D.SlideResults](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody2D.SlideResults.html), [Script serialization](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization.html) | `slide.fields`; `json.overwrite` and `clone` start from hits that differ from the source's. |
| [Object.Instantiate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html) | `clone.pair`: the original's non-serialized values differ from the copy's initializer values. |
| [VisualElement.Hierarchy](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.VisualElement.Hierarchy.html) | `hierarchy.*`. |

Not measured: a struct inside a class field that the copy creates. Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/declarationextras/run.py native --label reference-c
python3 tests/projects/declarationextras/run.py native --label reference-d
python3 tests/projects/declarationextras/run.py publish reference-c reference-d
python3 tests/projects/declarationextras/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
