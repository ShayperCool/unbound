# Activation order controls

The order of OnDisable and OnEnable when an already-awake hierarchy is deactivated and reactivated
with `GameObject.SetActive`, in cases that tell apart the orders that fit
tests/projects/activationorder's capture (where deactivation is post-order and reactivation
follows the order the recorders were added, confounded with other orders). Unity 6000.0.71f1,
recorded with the macOS Editor in batch mode (two identical captures); the order depends on
neither the OS nor the graphics device.

`Assets/Scripts/ActivationControlsProbe.cs` builds each case's tree P { C1 { G }, C2 } from code,
waits for every Start, then deactivates and reactivates P. `<case>.off` and `<case>.on` list the
callbacks in raw order: the recorder's label, `D` or `E`, and its GameObject's activeSelf and
activeInHierarchy (`+`/`-`) at that moment.

* `add`: recorders added C2, C1, P, G. `addHierarchy`: added in pre-order P, C1, G, C2.
* `reenabled`: as `add`, with C1's recorder disabled and enabled again before the toggle.
* `reactivated`: as `add`, with C2's GameObject deactivated and activated again before it.
* `two`: two recorders on P, one added first and one last (P.a, C2, C1, G, P.b).
* `late`: as `add`, with a second recorder added to C1 after every Start.
* `created`: GameObjects created G, C2, P, C1, recorders added in pre-order.

What it shows: OnDisable runs in hierarchy post-order (children before their parent, the first
child's subtree before the next, a GameObject's components in their order); OnEnable runs in the
order the behaviours were created, across the hierarchy: neither enabling a behaviour again nor
activating its GameObject again moves it, a behaviour added later comes last, and the order the
GameObjects were created does not count. Each GameObject's own state during its callbacks is the
new one (its OnDisable sees activeSelf false).

Residuals (residuals.json): Unbound runs both in hierarchy pre-order. The rule is not
implemented yet: scene-loaded hierarchies (their objects' creation is the load) and instantiated
ones (the clones' creation order) take the same path and are not measured here; a later slice
records them before the activation order changes.

Record and gate:

    python3 tests/projects/activationcontrols/run.py record --label a   # twice, then
    python3 tests/projects/activationcontrols/run.py publish a b
    python3 tests/projects/activationcontrols/run.py gate
