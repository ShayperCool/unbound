# Transform matrix precision

The reference records every float bit in `Transform.localToWorldMatrix` and in the local pose
with Unity 6000.0.71f1 on Linux. It compares a combined pose assignment with separate
assignments, including non-unit, zero and tiny quaternions. The hierarchy cases include a
reflected scale, shear, a zero scale and an ancestor whose pose changes after its matrix was
read. The signed-zero case distinguishes the transform matrix's rotation entries from the
arithmetic of the public `Matrix4x4.TRS` helper.

The Unity 6.0 [Scripting API][matrix] defines the matrix's local-to-world mapping and directs
rendering callers to `Renderer.localToWorldMatrix`. The [`lossyScale` documentation][scale]
describes how a rotated child of a scaled parent can acquire shear. The probe checks a
nontrivial point in a reflected hierarchy and records the unit-scale and sheared matrices
alongside a reconstruction from world position, rotation and lossy scale. The reconstruction
rounds differently even at unit scale and cannot represent the sheared basis.

Run two independent captures with `run.py record --label <name>`, then use
`run.py publish <first> <second>` to publish the byte-identical values. Use
`run.py gate` to check Unbound against that reference.

[matrix]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform-localToWorldMatrix.html
[scale]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform-lossyScale.html
