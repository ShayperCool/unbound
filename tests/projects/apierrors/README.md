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
  array sizes, Graphics.CopyTexture regions outside a texture, and an unreadable texture.
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
  log line of such an exception a callback does not catch (`unhandled_*`).

Measured in the Unity 6000.0.71f1 Editor on macOS in batch mode (two byte-identical captures;
none of the values depends on the OS or the graphics device); the reference with the `format.*`
rows was recorded in the Linux Editor (two byte-identical captures, every earlier row unchanged). Asynchronous AssetBundle loads that
fail report later, at a moment that varies between runs, so only their synchronous answers are
recorded. `run.py record` / `publish` / `gate` (tests/value_probe.py).

Recorded residuals (`residuals.json`): a null reference in the probe's own code has the runtime's
message, without a full stop on Mono and with one on CoreCLR.
