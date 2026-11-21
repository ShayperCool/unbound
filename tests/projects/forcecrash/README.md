# Utils.ForceCrash

Unity 6000.0.71f1, recorded with the windowed Linux GPU Editor (the crash handler's output and the
signal depend on the OS): one Editor run per `ForcedCrashCategory`, twice. The Editor crashes in each
run, so `run.py` records and compares per category rather than through value_probe; nothing else
runs in that Editor.

`Assets/Scripts/ForceCrashProbe.cs`, in Play mode after the scene loads, writes "forcing
<category>" to a file, logs it, and calls `Utils.ForceCrash`. Values per category: the file, whether
the call returned, the lines logged after the probe's line and before the crash report (stack and
location lines left out), and the signal the Editor died of.

Measured:

- AccessViolation prints "Forcing a crash -- Intentionally Dereferencing NULL pointer" and dies of
  SIGSEGV.
- Abort prints nothing and dies of SIGABRT; PureVirtualFunction prints "pure virtual method called"
  and "terminate called without an active exception", MonoAbort and values outside the enum (`7`,
  `-1`) print the Mono assertion line, and all die of SIGABRT.
- FatalError logs "Intentionally caused fatal error" as an error, starts shutting down and does not
  exit by itself (the run's watchdog ends it), so its exit is not compared; Unbound aborts after the
  error.
- No call returns.

Unmeasured: the macOS Editor, batch mode, worker threads, other releases.
