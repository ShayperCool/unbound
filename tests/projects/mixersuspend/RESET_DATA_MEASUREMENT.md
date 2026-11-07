# Clip data across audio device reset

Measured against the Linux Unity 6000.0.71f1 Editor, stereo 1024-frame offline
rendering at 48000 and 44100 Hz. `reset_data.py` records two independent projects
per rate; both agree bit-for-bit on all 180543 values per rate. The reference is
`Reference/unity-6000.0.71f1/reset-data-rate-<rate>/`. The authored `reset-data`
project is assembled without an Editor for the engine gate. All existing original
suspension references and comparator tolerances are unchanged.

## Documented contract and limits

The Unity 6000.0 [Audio Settings manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioSettings.html)
says a configuration reset reloads audio objects from disk, loses script-created
or script-modified clip data and playback state, and requires those sounds to be
regenerated through `AudioSettings.OnAudioConfigurationChanged`.
[GetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.GetData.html)
and [SetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.SetData.html)
require accessible sample data: compressed assets need DecompressOnLoad and
streamed sample access is unavailable. Those guarantees do not specify the
remaining object's metadata, clone storage ownership or interpolation at the
last frame. The observations below cover those details in this release/context.

## Measured rules

- Reset leaves script-created objects valid but with zero samples/channels,
  unchanged frequency and Loaded state. GetData and SetData return false;
  GetData leaves its destination untouched. Play stays stopped. This holds for
  played, never-played, nonstream callback and streamed callback clips. A newly
  created clip plays after reset.
- The two imported DecompressOnLoad PCM clips retain their sample count, channels,
  rate and import settings. Reset restores saved PCM after SetData, including
  edits through a clone. Their load state becomes Unloaded; GetData succeeds
  without changing that state. SetData changes the writing object's state to Loaded.
- Imported clones share PCM storage. Writing the clone changes the original's
  samples while their load-state metadata stays independent. The engine retains
  shared storage until the last owner is destroyed; a managed test also destroys
  the source first and reads the surviving clone.
- A nonstream script-created clone has zero samples but retains channels and
  frequency, both before and after the source played. GetData returns false;
  SetData at offset zero throws ArgumentException because no offset is valid.
  Streamed cloning is not measured and remains explicitly unsupported.
- Script-created duration is the complete number of milliseconds, divided by
  1000. The matrix includes 1,43,44,45,47,48,49,4096,48000 frames at both rates;
  imported duration remains sample-based. This explains the measured boundary
  around one millisecond independently of device reset.
- Non-looping decoded resampling holds the final frame until playback reaches the
  sample count. Imported and newly created 48000-Hz constant clips at 44100-Hz
  output distinguish this from zero padding. A looping control with different
  first/last frames interpolates across the wrap instead. Mono/stereo unit tests
  cover the endpoint, wrap and the prior behavior of unmeasured releases.

Each measured mechanism is selected through Compat. Other releases retain their
prior clip ownership, clone, duration and interpolation paths until measured.
These changes do not alter importer/cache output. The new audio packet flag bumps
NativeApi to 29; the audio thread only reads a prepared bit and performs bounded
sample fetches. Clip enumeration, reload and reference ownership run on the control
thread.

## Original reset residuals

The original six replay impulse failures are resolved by losing generated data;
there is no special rule about the frame on which Play follows Reset. The focused
profile compares complete rendered PCM and exact object metadata, without claiming
a fixed number of device callbacks around reset. It has no residual failures.

The original matrix retains 2049 raw failures per rate, solely `reset.listener`:
one metadata record (1049600 Native frames versus 1048576 engine frames) and 2048
missing scalar samples at indices 2097152 through 2099199, an extra 1024-frame
listener callback containing approximately +/-1e-20. Other reset captures have
inconsistent independent callback counts; no universal extra-block rule is inferred.
The full raw reports retain this functional delivery gap. `acceptance.py` admits
only that precise shape and rejects any replay impulse or other new error.
