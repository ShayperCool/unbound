# Core remainder: onBeforeRender order, UnityString, API compatibility attribute, settings objects

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode. None of the values
depends on the OS or the graphics device. The gate compares every value exactly.

`Assets/Scripts/CoreRemainderProbe.cs`:

- Adds `Application.onBeforeRender` callbacks with `BeforeRenderOrderAttribute` orders 10, none
  (0), -5, 10, the same delegate again and -100 (an instance method), removes some, empties an
  order and fills it again, removes a callback that is not there, adds a throwing callback and one
  that adds callbacks of orders 20 and -50 while it runs, adds and removes null and a lambda. Each
  step runs the event through the helper the engine invokes before rendering (batch mode renders
  nothing, so the event does not run by itself there) and records the calls.
- Formats numbers with `UnityString.Format` (reached by reflection; internal) and passes it null
  arguments.
- Constructs `UnityAPICompatibilityVersionAttribute` with each constructor (the one-string one,
  obsolete as an error, by reflection) and with `checkOnlyUnityVersion` false.
- Reads `QualitySettings.GetQualitySettings()` and `GraphicsSettings.GetGraphicsSettings()`: names,
  types, identity with the settings singletons and assets, and properties of their SerializedObjects.

Measured:

- Callbacks run by ascending order, an order's callbacks in the order they were added (a delegate
  added twice runs twice); a removed order's block goes, and is made again in its place.
- A throwing callback ends the run (later orders do not run); the exception leaves the event.
- A callback that adds one of a lower order runs again in the same run (the blocks move along); one
  of a higher order runs in the same run, the lower one from the next run on.
- Adding or removing null throws NullReferenceException; removing a callback that is not there
  does nothing.
- `UnityString.Format` uses the invariant culture with Mono's number texts; null arguments throw
  ArgumentNullException.
- `checkOnlyUnityVersion` false throws ArgumentException; the others keep the version.
- The settings objects are the ProjectSettings assets' objects (QualitySettings lists the project's
  six levels).

Unmeasured: when the event runs in a rendering Editor, other releases.
