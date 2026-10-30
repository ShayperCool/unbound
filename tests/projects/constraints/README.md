# Constraints probe

Unity 6000.0.71f1, Linux. Records 3272 values for Position, Rotation, Scale,
Parent, Aim and LookAt constraints. The probe covers API defaults and errors,
source list copies, signed and zero weights, local axis masks, rest values,
offsets, nonuniform and negative parent scales, source movement, enable/active/lock
changes, and serialized prefab loading and cloning.

The eight frames distinguish repeated evaluation from external Transform changes.
The scale diagnostics distinguish multiplicative offsets and geometric source blending.
This reference was recorded before implementing the engine components.

```bash
CONSTRAINT_PROBE_OUT=/tmp/constraints-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/constraints" -executeMethod ProbeRunner.Record \
  -logFile /tmp/constraints-editor.log
CONSTRAINT_PROBE_OUT=/tmp/constraints-unbound unbound run \
  -p tests/projects/constraints --headless 30000 --no-watch
python3 tests/projects/constraints/compare.py \
  tests/projects/constraints/Reference /tmp/constraints-unbound
```

`ProbeRunner.Create` recreates the prefab and empty scene through the Editor.
