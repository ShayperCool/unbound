# ConstantForce probe

Unity 6000.0.71f1, Linux. The 608 unique records cover ConstantForce and ConstantForce2D defaults,
required bodies, native duplicate limits and their C# metadata, serialization/cloning, world and
relative forces/torques, fixed and Update simulation modes, direct manual simulation, force
accumulation while simulation is suspended, enabling/disabling, inactive bodies and sleeping.
Transforms include nonuniform and reflected parents. 3D inertia is set explicitly; a separate
2D AddTorque control distinguishes ConstantForce2D's angular-force units.

Player-loop instrumentation measures forces before/after ScriptRunBehaviourFixedUpdate,
PhysicsFixedUpdate and Physics2DFixedUpdate. It shows that both force components queue their
values during the fixed script update, even in Script simulation mode. Native force updates
follow negative execution orders and precede order-zero and positive-order scripts. A normal
FixedUpdate setter therefore changes the next force evaluation. Simulate alone does
not update either component. Three fixed updates can accumulate force for a later manual step.

```bash
FORCES_PROBE_OUT=/tmp/forces-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/forces" -executeMethod ProbeRunner.Record \
  -logFile /tmp/forces-editor.log
FORCES_PROBE_OUT=/tmp/forces-unbound unbound run \
  -p tests/projects/forces --headless 30000 --no-watch
python3 tests/projects/forces/compare.py tests/projects/forces/Reference /tmp/forces-unbound
```

All 608 records match. The API check reports zero errors in 2235 hand-written types.

ProbeRunner.Create recreates the empty scene and serialized component prefab through the Editor.
