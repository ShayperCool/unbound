# Constraints probe

Unity 6000.0.71f1, Linux. Records 13498 values for Position, Rotation, Scale,
Parent, Aim and LookAt constraints. The probe covers API defaults and errors,
source list copies, signed and zero weights, local axis masks, rest values,
offsets, nonuniform and negative parent scales, source movement, enable/active/lock
changes, and serialized prefab loading and cloning.

The eight frames distinguish repeated evaluation from external Transform changes.
The scale diagnostics distinguish multiplicative offsets and geometric source blending.
Scale logarithm and exponential diagnostics sample the mantissa across its range.
All 13498 records match. Additional cases cover every Aim world-up mode, arbitrary
aim/up vectors, LookAt roll and up objects, source weight sums of zero and below zero,
weights outside 0–1, no sources, runtime activation commands, initial rest capture,
and dependency evaluation between Update and LateUpdate. LookAt keeps an unclamped
global weight, while the other constraints clamp during evaluation.

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
