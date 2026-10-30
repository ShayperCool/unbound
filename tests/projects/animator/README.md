# Animator probe

Generic root motion and movement callbacks on Unity 6000.0.71f1. The Editor-created clip animates
its root's position and rotation and a child's position. Two controllers play it, one with a
StateMachineBehaviour that implements OnStateMove, one without it.

The 658 records cover applyRootMotion on/off, enabled/disabled MonoBehaviour callbacks, observing
motion, applying it manually or through ApplyBuiltinRootMotion, repeated builtin calls, builtin
calls outside evaluation, root-pose getters/setters, zero/positive/negative time, several crossed
loops and nested nonuniform/negative scales. Callback traces include the child's old pose and
the root's immediate position after builtin application.

The API root pose is independent of the Transform. Setters do not move the GameObject; evaluation
replaces the API values. Under a sheared hierarchy the animation stream converts through the
parent's decomposed TRS, so rootPosition can differ from Transform.position even after builtin
application. Disabled movement handlers request deltas but receive no callback. OnStateMove
intercepts automatic root application without requesting deltas by itself.

The assets are committed. To create them in a fresh project:

```bash
Unity -batchmode -nographics -quit -projectPath tests/projects/animator \
  -executeMethod ProbeRunner.Create -logFile /tmp/animator-create.log
```

Record the Editor or run Unbound:

```bash
ANIMATOR_PROBE_OUT=/tmp/animator-unity Unity -batchmode -nographics \
  -projectPath tests/projects/animator -executeMethod ProbeRunner.Record -logFile /tmp/animator.log
ANIMATOR_PROBE_OUT=/tmp/animator-unbound unbound run -p tests/projects/animator --headless 30000 --no-watch
python3 tests/projects/animator/compare.py tests/projects/animator/Reference /tmp/animator-unbound
```

The comparison fails for missing records and recursively compares numbers within 2e-4. It does
not establish humanoid, imported root-node, matching or body/controller parity.
