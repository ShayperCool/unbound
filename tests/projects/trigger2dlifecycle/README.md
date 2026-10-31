# 2D contact lifetime continuation

This stage starts at 7c0bde630f630c55181a31ac7563954323e774da. Historical
trigger2dtiming inputs and evidence stay unchanged. New controls cover both
callback flags, solid and sensor pairs, awake and sleeping dynamic/kinematic
bodies, static/dynamic/kinematic partners, collider disable, simulated disable,
deactivation and destruction of colliders, bodies and GameObjects. Additional
controls exercise changes from enter callbacks and moving static colliders.

Two independent Unity 6000.0.71f1 Linux CPU Editor processes agree byte for byte
on 25,356 observations in 324 cases before runtime implementation. Every callback
has its real phase, step and receiver. Teardown remains in the records.
Evidence/native-manifest.json identifies sources, captures and private paths.

Native collider disable, deactivation and collider/GameObject destruction send
immediate exits when callbacksOnDisable is true. simulated=false exits on the
next Simulate, including when callbacksOnDisable is false. Removing a Rigidbody2D
silently removes its old contacts and converts remaining colliders to static
fixtures. DestroyImmediate of physics components or objects during a physics
callback is rejected by Native; collider disable and deactivation are permitted,
with exit callbacks following the current pair's enter delivery.

These measurements cover this release only. Older releases remain unmeasured.
This is not a full-engine parity claim.
