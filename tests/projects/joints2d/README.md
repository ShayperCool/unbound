# 2D joints probe

Unity 6000.0.71f1, Linux. DistanceJoint2D and SpringJoint2D are measured with fixed,
kinematic and dynamic connected bodies, anchors under reflected nonuniform parents,
automatic anchor/distance configuration, spring frequency zero, disabling/re-enabling,
changing the connected body, and all four break actions. The 214 records include
serialized prefabs and clones, API limits, reaction forces for several time steps,
and OnJointBreak2D callbacks.

```bash
JOINTS2D_PROBE_OUT=/tmp/joints2d-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/joints2d" -executeMethod ProbeRunner.Record \
  -logFile /tmp/joints2d-editor.log
JOINTS2D_PROBE_OUT=/tmp/joints2d-unbound unbound run \
  -p tests/projects/joints2d --headless 30000 --no-watch
python3 tests/projects/joints2d/compare.py tests/projects/joints2d/Reference /tmp/joints2d-unbound
```

ProbeRunner.Create recreates the scene and both serialized prefabs in the Editor.
