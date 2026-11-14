# GUI serialization payloads

Unity 6000.0.71f1, recorded twice with the NVIDIA GPU. All 43 rows match between independent Editor runs.

The [serialization rules](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization-rules.html) describe field eligibility and inline value copies. [GUIStyle](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUIStyle.html) documents its copy constructor as making an identical style. [JsonUtility.FromJsonOverwrite](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.FromJsonOverwrite.html) preserves fields that are absent from the JSON. The overwrite control first changes the style and its enclosing control value. It distinguishes preservation of the enclosing object's fields from replacement of a supplied built-in style payload.

The captures establish the selected release's wrapper behavior. A GUIStyle root and a standalone GUIStyleState write `{}`. Nested GUIStyle values write their full built-in payload, including state backgrounds, scaled backgrounds, colors, offsets, font settings and the SDF bit. A supplied nested style replaces its previous payload; omitted fields inside that new payload have style defaults. The ordinary enclosing control and array remain unchanged. A partial color has black defaults with alpha one. These details are observations for this release, not additional documented guarantees.

Controls cover nondefault values across all eight states and four offsets, root and nested JSON, partial overwrite, null values, texture references, array ownership, Object.Instantiate and the GUIStyle copy constructor. The authored GUISkin loads box/custom styles through YAML, then exercises independent skin copies. A standalone state field is a control outside the built-in style payload. Other releases and device-specific rendering behavior are unmeasured. This probe does not compare rendering.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/guipayload/run.py native --label reference-a
python3 tests/projects/guipayload/run.py native --label reference-b
python3 tests/authored_project.py record guipayload default --release "$UNBOUND_PROBE_UNITY_VERSION" --project <reference-a>/project --command 'python3 tests/projects/guipayload/run.py native --label reference-a'
python3 tests/projects/guipayload/run.py publish reference-a reference-b
python3 tests/projects/guipayload/run.py check
```

The gate assembles the committed authored set and uses only the engine. The comparator reports every missing, extra or unequal row exactly. Captures, logs and engine outputs stay in the run cache.
