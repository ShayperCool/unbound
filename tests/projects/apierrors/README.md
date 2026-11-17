# API argument and lookup errors

`Assets/Scripts/ApiErrorsProbe.cs` writes values.jsonl in `$APIERRORS_OUT`
(tests/value_probe.py): for every action, what it returns or throws (type and message) and the
log messages it writes with their log type (`.logs`; logs written on other threads would be
`.thread_logs`). The temporary folder's path in a message reads `<temp>`.

* `resources.*`: Resources.Load, LoadAll, LoadAsync, UnloadAsset and FindObjectsOfTypeAll with
  missing or bad paths and types.
* `scene.*`: SceneManager loads, lookups, SetActiveScene, unloads, moves, creation and merges
  with names, indices and scenes that do not exist.
* `bundle.*`: AssetBundle loads from a missing file, a file, bytes and a stream that hold no
  archive, and null arguments.
* `mesh.*`: Mesh submesh, index, channel and size errors.
* `texture.*`: Texture2D creation and reinitialization sizes, mip levels, pixel blocks and
  array sizes, Graphics.CopyTexture regions outside a texture, and an unreadable texture;
  `texture.copy_case.*`: copies from a red into a blue texture that are refused or done (the
  destination's corner, centre and far corner after each): regions outside a level, negative
  region sizes, whole and element copies of other sizes and formats, elements and levels that do
  not exist; `texture.{cubemap,array,volume,cube_array}.*`: the pixel APIs of Cubemap,
  Texture2DArray, Texture3D and CubemapArray with faces, elements and levels that do not exist,
  missing, short and long arrays, and unreadable textures.
* `animator.*`: Animator parameters, layers and states that do not exist or have another type,
  with `Resources/Ctrl.controller` (state Idle; parameters go: trigger, speed: float,
  flag: bool, count: int), with `logWarnings` off (`quiet_*`) and without a controller (`bare_*`).
* `playable.*`, `playable_bound.*`: the same lookups on an AnimatorControllerPlayable of that
  controller alone, and on one an AnimationPlayableOutput plays on an Animator.
* `format.*`: what game code and the log see of argument exceptions with and without a parameter
  name (ArgumentException, ArgumentNullException, ArgumentOutOfRangeException with and without an
  actual value), ObjectDisposedException and an exception with an inner one, thrown by the
  probe's own code (`game_*`), by the class library (`library_*`) and by Unity (`unity_*`): the
  type and Message, ParamName (`.param`), ActualValue (`.actual`), ObjectName (`.object`),
  ToString() without its stack lines (`.string`) and Debug.LogException's line (`.log`); and the
  log line of such an exception a callback does not catch (`unhandled_*`); and a game exception
  whose Message is its own ToString(), which reads base.Message and its inner exception, as the
  Addressables' remote provider exception does (`game_message_from_string`).
* `format.extract.*`: StackTraceUtility.ExtractStringFromException's text (the probe's project folder
  reads `<project>`) for exceptions thrown by the probe's code: with Mono's message layouts, an inner
  exception, nested rethrows, `throw;`, ExceptionDispatchInfo, a blank message, one never thrown, throws at
  several places in a method (`frame_*`: first statement, branches, loop, switch, try, catch, using,
  conditional expression, constructor, generic and constructed parameters); the first line only for
  exceptions the class library and Unity throw (their stacks hold their own frames); a null and a
  non-exception argument; asked from a coroutine, from LateUpdate and from a coroutine's first step
  started there (`started_*`), each with StackTraceUtility.ExtractStackTrace's text.
* `format.number.*`: actual values made of floats (tuples of a float and a double) and tuples in
  game code's ToString, string.Format and concatenation; then, under the de-DE culture (a decimal
  comma), float, double and tuple actual values, float and tuple texts, and a Vector3's.

Measured in the Unity 6000.0.71f1 Editor on macOS in batch mode (two byte-identical captures;
none of the values depends on the OS or the graphics device); the reference with the `format.*`
rows was recorded in the Linux Editor (two byte-identical captures, every earlier row unchanged).
The `format.extract.*` rows name Unity's own frames with the source paths the release's
UnityEngine.CoreModule .pdb holds, here the Linux release's; on macOS they are the macOS release's
(unmeasured). Asynchronous AssetBundle loads that
fail report later, at a moment that varies between runs, so only their synchronous answers are
recorded. `run.py record` / `publish` / `gate` (tests/value_probe.py).

Recorded residuals (`residuals.json`): a null reference in the probe's own code has the runtime's
message, without a full stop on Mono and with one on CoreCLR. The `format.extract.*` residuals: frames of small
methods the Editor's JIT inlines (Unbound lists them), throws in a branch that the Editor reports at
the method's last statement, a rethrow (the Editor names the `throw;`), the frame that called
ExceptionDispatchInfo.Throw (CoreCLR places it at the method's first statement), and
MonoBehaviour.StartCoroutine's own frames below a coroutine's first step. A copy between formats of other groups
with as many bytes logs the Editor's OpenGL internal formats and copies the CPU bytes as they are
(Unbound copies the bytes without the error), a new RGBAHalf texture's
unwritten texels are 0xCD bytes in the Editor (205/255 per channel in Unbound); Texture3D.GetPixelBilinear is not implemented.
