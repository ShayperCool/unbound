# CullingGroup probe

`Assets/Scripts/CullingProbe.cs` makes three cameras (Main looking +Z, Second looking back at a
higher depth, a disabled one at z -20), a witness cube and an occluder, and ten culling groups,
then plays 25 scripted frames. The record lists, in order, Update/LateUpdate, every camera
message (`OnPreCull`, `Camera.onPreCull`, `OnPreRender`, ...), the witness's `OnBecameVisible`/
`OnWillRenderObject`, every `onStateChanged` callback with all `CullingGroupEvent` fields and the
current camera, the end of the frame, each group's `IsVisible`/`GetDistance` in Update and
LateUpdate, `QueryIndices` results, and the API's values, errors and console messages.

Measured in the Unity 6000.0.71f1 Editor (built-in pipeline, 960x540 Game view):

* A group is evaluated in its target camera's culling: after the camera's `OnPreCull` and
  `Camera.onPreCull`, before renderers' `OnBecameVisible`/`OnWillRenderObject`; callbacks go out
  there, by sphere index, the groups in creation order (a disposed group's slot takes the last
  group). A manual `Camera.Render` of a disabled camera evaluates its groups; a disabled camera
  otherwise never does. The first evaluation is in the first frame's render.
* A callback is sent when a sphere's state (visible, distance band) differs from the state of its
  last callback; the initial state is invisible, band 0.
* Visible: the sphere touches the camera's frustum (six planes, the radius counted; nothing is
  occluded without occlusion data). A sphere in the last band (beyond the last distance) is
  invisible.
* Band: the number of distances below `|center - reference| - radius` (a sphere exactly at a
  distance is in the nearer band). No distances: band 0. Reference: the origin unless set; a
  reference transform is read at evaluation (moved in LateUpdate: used that frame); a null
  transform keeps the last point.
* A disabled group is not evaluated. A group with no target camera gets its distances updated
  during rendering, but not its visibility, and sends no callbacks.
* The sphere array is read by reference. `SetBoundingSpheres` sets the count to its length and
  keeps the states; `SetBoundingSphereCount` above the array length or negative warns and is
  ignored; spheres coming back into the count start invisible in band 0. `EraseSwapBack(i)`
  moves the last sphere and its state into `i` (in the group's array too) and lowers the count.
* `QueryIndices` writes up to the result's length (a null or empty result counts all matches);
  a negative first index throws. `IsVisible`/`GetDistance` outside the count throw. Eight or more
  distances log an error and are kept; unsorted ones log an error and are ignored. Everything
  but `onStateChanged` throws `NullReferenceException` after `Dispose`, a second `Dispose` too.

## Record and compare

The Editor runs windowed on a private Xvfb display with the GPU through VirtualGL (`run.py`
checks nothing about the GPU: `grep Renderer` the Editor logs). `run.py editor` makes two
independent captures, `publish` copies one to `Reference/` only if they are byte-identical.

    python3 tests/projects/cullinggroup/run.py create --release 6000.0.71f1 --label create-1
    python3 tests/projects/cullinggroup/run.py editor --release 6000.0.71f1 --label native-1
    python3 tests/projects/cullinggroup/run.py publish --release 6000.0.71f1 --label native-1
    UNBOUND_CLI=/path/to/unbound UNBOUND_MANAGED_DIR=/path/to/managed \
        python3 tests/projects/cullinggroup/run.py unbound --release 6000.0.71f1 --label engine-1

`compare.py` compares the records line by line, in order, and lists every missing and extra
record; `summary.py RECORD` prints the callbacks and state changes compactly.

Not covered: occlusion culling with baked data, scriptable render pipelines, the Scene view
camera, stereo, groups never disposed (finalized), and more than 127 bands.
