# cameramatrices

The exact bits of a camera's matrices and of its transform's matrices, in the Unity Editor and in
Unbound (built-in render pipeline, an empty scene; `Assets/Scripts/CameraMatricesProbe.cs` runs
after the scene loads and writes `values.jsonl` into `$CAMERA_MATRICES_OUT`). Each value is the
matrix's 16 elements in `Matrix4x4` index order (column-major), as hexadecimal float bits, so a
difference of one float step or of the sign of a zero shows.

- `projection/fov F`: `Camera.projectionMatrix` at fields of view 1 to 179 (aspect 16:9 set on
  the camera, clip planes 0.3 and 1000); `projection/aspect A`: aspects set on the camera;
  `projection/target WxH`: the aspect of a target texture (its value follows the matrix);
  `projection/clip N F`: other clip planes; `projection/orthographic ...`: orthographic sizes.
- `gpu projection/...`: `GL.GetGPUProjectionMatrix` of the 60-degree projection, into the screen
  and into a texture (OpenGL Core: as the device draws with it).
- `world to camera/R at P`, `camera to world/R at P`, `world to local/R at P`,
  `local to world/R at P`: `Camera.worldToCameraMatrix`, `Camera.cameraToWorldMatrix` and the
  transform's `worldToLocalMatrix` and `localToWorldMatrix` of a root camera at nine rotations
  (Euler angles, a raw quaternion, identity; `rotation/R` records the quaternion the transform
  holds) and four positions (0: (0, 1.5, -6), 1: (123.456, -7.89, 1000.5), 2: the origin,
  3: (-0.001, 200000, 3)); `culling/R`: `Camera.cullingMatrix` at position 0.
- `... child of scaled parent`, `... child of nonuniform parent`: the camera under a parent at
  (1, 2, 3), turned 30 degrees about y, scaled 2 (then (1, 3, 0.5)).

Measured in the Unity 6000.0.71f1 Editor (Linux, OpenGL Core; two identical batch captures).
Unbound matches all 198 rows. The rules: `projectionMatrix` is the single-precision
`Matrix4x4.Perspective`; the transform's `worldToLocalMatrix` builds each level from the inverse
rotation's matrix with its exact off-diagonal zeros positive, rows scaled by the reciprocal scale,
translated by the negated position (summed x + (y + z)); `worldToCameraMatrix` is
`Matrix4x4.Scale(1, 1, -1)` times that inverse of the world position and rotation without scale
(also under a scaled parent); `cameraToWorldMatrix` is the single-precision `Matrix4x4.inverse` of
`worldToCameraMatrix`; `cullingMatrix` is the product of projection and view. Physical-camera
projections and a camera to world matrix of a set `worldToCameraMatrix` are not measured.

```bash
python3 tests/projects/cameramatrices/run.py record --label a
python3 tests/projects/cameramatrices/run.py record --label b
python3 tests/projects/cameramatrices/run.py publish a b
python3 tests/projects/cameramatrices/run.py gate
```
