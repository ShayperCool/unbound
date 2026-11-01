# Routed voice submission measurement

This is a separate experiment for the main suspension/wake stage, defined before
capture. It does not reset the audio system during recording and is not a subset
or replacement of the unresolved controlled reset profile.

Each case captures 128 complete stereo DSP blocks. A source plays an 8192-frame
clip before the first yield. At block 64, after the usual yield and before Render,
the probe requests replay, Stop/Play, two Play calls, disable/enable with and without
explicit Play, a fresh source, reassignment, restart while playing, or Pause/UnPause.
The same source/group setup and input arrays are used at both output rates.
An unrouted-source control distinguishes group graph submission from device playback.
The routed mixer disables suspension and its Echo wet output, so submission timing
can be measured without a suspension timer or delayed effect output.

Every device, listener and raw sink block is retained. Exact source playing/sample
fields before the action, immediately after it and after Render are recorded, along
with every listener callback's stage, frame count and channels. Two independently
agreeing Native runs at each claimed rate are required. Float tolerances remain
ATOL 2e-5 / RTOL 2e-4; metadata and Native repeat bits are exact. There is no fixture
sample alignment, buffer trimming or inferred delay compensation. Native and engine
run the same coroutine and production audio update ordering.
