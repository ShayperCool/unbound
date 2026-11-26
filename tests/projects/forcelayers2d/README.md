# forcelayers2d

`Collider2D.forceSendLayers` and `forceReceiveLayers`, measured in the Unity 6000.0.71f1 Editor
(`Assets/Scripts/ForceLayers2DProbe.cs`; two byte-identical Linux batch Editor captures in
`Reference/unity-6000.0.71f1/`). `Physics2D.Simulate(0.02)` 50 times; every fifth step each
body's position and velocity, the collision messages it got so far (enter, stay, exit), whether
the two touch and the first one's contact count. Tolerance 1e-4.

* `drop/*`: a circle (radius 0.5, layer 9) dropped from 0.2 above static ground (a 4 x 1 box,
  layer 8): every layer in; the circle receiving nothing from the ground's layer; the ground
  sending nothing to the circle's; the circle sending nothing to the ground; the ground receiving
  nothing from the circle.
* `push/*`: a circle at 3 m/s along +x hits one at rest (no gravity, equal masses): every layer
  in; A sending nothing to B; B receiving nothing from A; B sending nothing to A; A receiving
  nothing from B; both A and B sending nothing to each other.

Measured: a body takes a contact's impulses only when the other collider sends to its layer and
its own collider receives from the other's. The contact is still made, touching and sending its
messages, and is solved with both bodies' masses; the body that takes no force is just not
moved by it. A circle that takes nothing from the ground falls through it, touching it until it
leaves; one circle stops against another that takes nothing from it (its approach halves at each
of the solver's 8 velocity iterations: 3/256 m/s is left), or carries along one it takes nothing
from; taking nothing either way they pass through each other. What the static ground sends or
receives changes nothing for itself.

    python3 tests/projects/forcelayers2d/run.py record --label L     # twice, then publish
    python3 tests/projects/forcelayers2d/run.py publish L1 L2
    UNBOUND=<binary> python3 tests/projects/forcelayers2d/run.py gate
