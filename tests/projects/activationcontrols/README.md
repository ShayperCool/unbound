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
* `lateStarted`: as `late`, two frames later (the second recorder's Start has run).
* `orders`: a recorder of a class with an earlier script execution order (-10) on P, added after
  the default-order recorders of C2, C1 and G.
* `created`: GameObjects created G, C2, P, C1, recorders added in pre-order.

`<case>.offStates` and `<case>.onStates` are the same callbacks sorted, so their states are compared
apart from their order.

What it shows, in these cases: OnDisable runs in hierarchy post-order (children before their
parent, the first child's subtree before the next, a GameObject's components in their order),
whatever the script execution order; OnEnable runs in script execution order (the `orders` case:
the earlier-order recorder first, though added last), and within one execution order in the order
the behaviours were created, across the hierarchy: neither enabling a behaviour again nor
activating its GameObject again moves it, a behaviour added later comes last (before or after its
Start), and the order the GameObjects were created does not count. Each GameObject's own state
during its callbacks is the new one (its OnDisable sees activeSelf false).

These are observations, not a documented contract: the 6000.0 Manual's Script Execution Order page
(https://docs.unity3d.com/6000.0/Documentation/Manual/script-execution-order.html) orders scripts of
different execution orders and leaves the order among scripts of the same order undefined ("you
should never rely on this behavior").

Limits: runtime-built hierarchies only (GameObjects made with `new GameObject`, recorders added with
`AddComponent`), two script classes (so whether different classes of one execution order keep
creation order or group by class is unmeasured), one tree shape, plain `SetActive` on the root,
the macOS Editor in batch mode.

Residuals (residuals.json): the order rows; Unbound runs OnDisable in hierarchy pre-order and
OnEnable in execution order, then pre-order. Not implemented yet: scene-loaded hierarchies (their
objects' creation is the load) and instantiated ones (the clones' creation order) take the same
path and are not measured here, nor are other activation paths (`SetGameObjectsActive`, a parent
activated with its children); a later slice records them before the activation order changes.

Record and gate:

    python3 tests/projects/activationcontrols/run.py record --label a   # twice, then
    python3 tests/projects/activationcontrols/run.py publish a b
    python3 tests/projects/activationcontrols/run.py gate
