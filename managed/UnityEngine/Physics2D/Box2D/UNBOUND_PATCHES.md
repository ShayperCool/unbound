# Box2D changes for Unity compatibility

The port's origin and licenses are in [NOTICE.txt](NOTICE.txt). Existing contact,
shape and solver adjustments are marked in their sources.

## Rounded edges

`Collision/Shapes/EdgeShape.cs`, `Clone`: retain Radius when a fixture copies its
shape. Losing it made EdgeCollider2D.edgeRadius ineffective for simulation and
fixture queries. The confirming Unity 6000.0.71f1 reference is
`tests/projects/physics2dadvanced`, the box and circle edge controls at radius 0.1
with isolated colliders, connected points, explicit adjacent points and duplicate
points. Radius zero is the control.

`Dynamics/Contacts/EdgeAndPolygonContact.cs`, `Evaluate`: route all two-sided
segments through the existing endpoint/adjacency solver, including isolated
segments. The confirming bent-seam controls cover both bend directions, boxes,
circles, adjacent points on/off and both radii. An isolated rounded box/edge
contact previously selected a different endpoint normal and changed the body's
velocity at the upper seam.

## Contact refresh during sub-stepping

`Dynamics/World.cs`, `Step`: an optional updateContacts argument suppresses both
contact refresh phases while retaining integration and the existing solver. The
bridge selects it after the first sub-step when Physics2D.useSubStepContacts is
false. Unity 6000.0.71f1, `tests/projects/physics2dadvanced`, `sub/Script/True`:
a sensor entered during the first sub-step stays reported through the outer step
with contact refresh disabled, and exits during that step with refresh enabled.
The same body motion and force integration are retained in both cases.

## Simulation layers and joints

`Dynamics/Joints/Joints.cs`, `Joint.SimulationEnabled`, and `Dynamics/World.cs`,
`Solve`: omit constraints excluded by the bridge's simulation mask without
destroying their cached impulses. Both connected body layers must participate.
`physics2dadvanced` confirms independent masks with either body moving and a mask
transition that preserves the last reaction force while the joint is omitted.
This is measured Unity 6000.0.71f1 behavior; the bridge selects it through Compat.

## Query storage

`Collision/DistanceProxy.cs`: hold the one/two support points of circles, edges
and chain children inside the proxy value. Polygon support points still borrow
the shape's existing array. This removes the per-cast arrays from repeated Slide
queries. The point-cloud overload keeps its existing copy ownership. Confirmed
by the physics2dadvanced query hits, the existing query/skin probes, and the
managed repeated-compound-Slide allocation control.
