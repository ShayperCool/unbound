# 3D layer overrides probe

Measures which collider pairs contact, trigger, sweep and block for every combination of the
layer collision matrix, `Collider.includeLayers`/`excludeLayers`, `Rigidbody.includeLayers`/
`excludeLayers` and `Collider.layerOverridePriority` (-1, 0, 1). It also checks which scene
queries see overridden colliders. Layers 8 and 9 are the pair's and layer 10 is unrelated.
`Assets/Scripts/LayerOverrideProbe.cs` builds every case at runtime and writes one JSON line per
case:

- `pair/...`: a dynamic box (collider and body masks) overlapping a static box, solid or trigger.
  Values: collision enters, collision stay, trigger enters, trigger stay, moved.
- `bodies/...`: two dynamic boxes whose bodies carry the masks.
- `runtime/...`: masks, priorities and the matrix change between steps on a live contact or
  trigger. Values: enters, exits and stay in the last step.
- `query/...`: Raycast, CheckSphere, OverlapSphere, SphereCast, BoxCast, CapsuleCast and
  RaycastAll against an overridden box.
- `sweep/...`: `Rigidbody.SweepTest`/`SweepTestAll` of an overridden body.
- `controller/...`, `controllers/...`: `CharacterController.Move` into an overridden wall or
  controller. Values: collision flags, blocked.
- `controller-body/...`: simulation contacts between a controller and an overlapping dynamic box.

The canonical Native oracle is `Reference/unity-6000.0.71f1.jsonl` (6021 records, Unity
6000.0.71f1 on Linux, GPU Editor). Two independent captures were byte-identical. Only one copy is
kept here.

Measured (6000.0.71f1):

- Contacts and triggers follow the documented `layerOverridePriority` rule in all 5,472 pair and
  body cases. A collider's masks are its own OR its body's. Includes apply first, then excludes.
  A collider "uses an override" when either combined mask is non-zero, even when it names only
  layers that do not change the decision. When the decisions differ, the one overriding side
  decides. With two overriding sides, the higher priority decides; equal priorities create no
  contact. Changes apply to live contacts at the next step.
- Controller-vs-body simulation contacts follow the same rule.
- `CharacterController.Move` against a collider or another controller follows the rule, except
  that equal priorities count as a hit (the move is blocked).
- Scene queries and `Rigidbody.SweepTest` ignore include/exclude layers. Queries test only the
  query's layer mask; SweepTest tests the body layer's matrix row.

ArticulationBody is not measured: Unbound does not implement it.

```bash
python3 tests/projects/layeroverrides3d/run.py native --label native1
python3 tests/projects/layeroverrides3d/run.py native --label native2
python3 tests/projects/layeroverrides3d/run.py publish native1 native2   # refuses to overwrite
python3 tests/projects/layeroverrides3d/run.py engine --label final      # UNBOUND_CLI selects the CLI
python3 tests/projects/layeroverrides3d/run.py verify                    # or --candidate PATH
```

Run output stays under `${XDG_CACHE_HOME:-~/.cache}/unbound/validation/runs/layeroverrides3d/`.
`UNBOUND_PROBE_UNITY_VERSION` selects the release.
