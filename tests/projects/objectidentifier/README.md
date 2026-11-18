# Build object identifier serialization

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 8 rows (managed serialization only; independent of
the OS and the graphics device). The gate compares every row exactly:

```sh
python3 tests/projects/objectidentifier/run.py check
```

`UnityEditor.Build.Content.ObjectIdentifier` is serializable, but its fields are internal and not
serialized:

- `serializable`, `json.root`, `json.nested`: JsonUtility writes it as an empty object at the root and
  inside a serialized class, beside a struct with a public field and an int that are written.
- `json.overwrite`, `json.overwriteRoot`, `json.roundTrip`: overwriting from JSON that names its
  internal fields, and a round trip, leave the identifier as it was (a field keeps its initializer).
- `clone`: Object.Instantiate of a ScriptableObject keeps the clone's field initializer (29) instead of
  the source's identifier (17), and copies the fields beside it.

Other releases are unmeasured.
