# Transform matrix precision

The reference records every float bit in `Transform.localToWorldMatrix` and in the local pose
with Unity 6000.0.71f1 on Linux. It compares a combined pose assignment with separate
assignments and covers non-unit, zero and tiny quaternions. The [combined local setter][setter]
is equivalent to the world setter for an unparented transform. The probe starts them from
different poses and applies the same input. The hierarchy cases include a reflected scale, shear,
a zero scale and a cached child matrix that must update after its ancestor's pose changes. The
signed-zero case records the transform matrix and the public `Matrix4x4.TRS` and `Rotate` helpers.

The Unity 6.0 [Scripting API][matrix] defines the matrix's local-to-world mapping and directs
rendering callers to `Renderer.localToWorldMatrix`. The [Transform Manual][manual] and
[`lossyScale` documentation][scale] describe shear in a rotated child of a scaled parent. The probe checks a
nontrivial point in a reflected hierarchy and records the unit-scale and sheared matrices
alongside a reconstruction from world position, rotation and lossy scale. The reconstruction
rounds differently even at unit scale and cannot represent the sheared basis.

On Linux, run two independent captures with `run.py record --label <name>`, then use
`run.py publish <first> <second>` to publish the byte-identical values. Use
`run.py gate` to check Unbound against that reference.

[matrix]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform-localToWorldMatrix.html
[scale]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform-lossyScale.html
[setter]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform.SetLocalPositionAndRotation.html
[manual]: https://docs.unity3d.com/6000.0/Documentation/Manual/class-Transform.html
