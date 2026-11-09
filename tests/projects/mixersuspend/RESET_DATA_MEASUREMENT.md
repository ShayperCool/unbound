# Clip data across audio device reset

Measured against the Linux Unity 6000.0.71f1 Editor, stereo 1024-frame offline
rendering at 48000 and 44100 Hz. `reset_data.py` records two independent projects
per rate; both agree bit-for-bit on all 197097 values per rate. The reference is
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

[LoadAudioData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.LoadAudioData.html)
loads clip data explicitly and exposes loading progress through
[loadState](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip-loadState.html).
These pages do not specify which playing source owns a streaming buffer or when
source cleanup releases it. Those details below are release-specific observations.

[Object.Destroy](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Destroy.html)
defers object destruction and includes a destroyed GameObject's components and
children. It does not specify the earlier audio-playback cleanup boundary. The
request controls below measure that boundary without changing script callback
ordering or object-destruction delivery.

## Measured rules

- Reset leaves script-created objects valid but with zero samples/channels,
  unchanged frequency and Loaded state. GetData and SetData return false;
  GetData leaves its destination untouched. Play stays stopped. This holds for
  played, never-played, nonstream callback and streamed callback clips. A newly
  created clip plays after reset.
- The two imported DecompressOnLoad PCM clips retain their sample count, channels,
  rate and import settings. Reset restores saved PCM after SetData, including
  edits through a clone. Their load state becomes Unloaded; GetData changes the reading object to Loaded, as the post-read observation
  explicitly checks. SetData changes the writing object to Loaded.
- Imported clones share PCM storage. Writing the clone changes the original's
  samples while their load-state metadata stays independent. The engine retains
  shared storage until the last owner is destroyed; a managed test also destroys
  the source first and reads the surviving clone.
- A nonstream script-created clone has zero samples but retains channels and
  frequency, both before and after the source played. GetData returns false;
  SetData at offset zero throws ArgumentException because no offset is valid.
  The broader tests/projects/assetinstantiate reference also measures streamed
  script clones as empty and reload after destroying the imported original.
- Script-created duration is the complete number of milliseconds, divided by
  1000. The matrix includes 1,43,44,45,47,48,49,4096,48000 frames at both rates;
  imported duration remains sample-based. This explains the measured boundary
  around one millisecond independently of device reset.
- Non-looping decoded resampling holds the final frame until playback reaches the
  sample count. Imported and newly created 48000-Hz constant clips at 44100-Hz
  output distinguish this from zero padding. A looping control with different
  first/last frames interpolates across the wrap instead. The rate ratio uses
  single precision before multiplication by voice pitch and accumulation of the
  playback position. A second loop with pitch 0.7 controls that composition;
  the two Editor repeats preserve every earlier waveform bit. Mono/stereo unit tests
  cover the endpoint, wrap and the prior behavior of unmeasured releases.

- Streamed Vorbis assets begin Unloaded with either preload setting. Explicit
  LoadAudioData and attempted GetData make them Loaded. An idle Stop preserves
  that state. Playback gives the latest-started source ownership of the buffer;
  stopping an older source leaves it Loaded, while stopping the latest source
  unloads it even if an older source still plays. Source destruction, replacing
  the main clip and stopping a one-shot also unload their owned buffers. The existing bundleaudio
  reference additionally observes Unloaded immediately after Destroy(source),
  before deferred object destruction. The request stops playback before deferred destruction, including destruction
  through the source GameObject or an ancestor. PCM controls stop at the same
  boundary and keep their data Loaded. A separate Compat mechanism delivers this
  request to each object once, preserving required-component removal refusals. Both
  preload settings and both source-stop orders are recorded. Overlapping main and
  one-shot playback of the same clip confirms ownership follows the latest voice.
  Main-clip replacement preserves a later one-shot buffer. The control-thread
  owner list transfers and unlinks each clip in constant time without allocating.
  Native and virtual command receipts transfer ownership only for admitted voices,
  without polling voice counts across an audio update.
  Automatic streamed completion is outside this measurement; the exploratory
  offline-clock disagreement is retained in Diagnostics/stream-clock-v1.

The cleanup implementation defers destruction requests made inside an audio
callback to the regular destruction delivery. Callback regression tests use the
production managed entry point for source, GameObject and ancestor targets. This
keeps the active render free of recursive mixer calls, compatibility lookup and
the new request-notification walk.

Each measured mechanism is selected through Compat. Other releases retain their
prior reset, duration, double rate ratio and zero-padded interpolation paths until
measured. Cloning reuses the AssetCloneResourcesMeasured gate and rejects unmeasured
releases, as tests/projects/assetinstantiate records.
These changes do not alter importer/cache output. The decoded-resampling audio packet flag and command-admission receipt bump
NativeApi to 30; the audio thread only reads a prepared bit and performs bounded
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

The counts above describe the retained pre-Q158 reports. Closing-buffer delivery
now resolves those original reset-listener failures; the full original matrix is
compared strictly. See [RESET_DELIVERY_MEASUREMENT.md](RESET_DELIVERY_MEASUREMENT.md)
for the independent source/listener control and the wider queued-period follow-up.
