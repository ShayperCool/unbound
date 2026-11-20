# Editor service APIs in Play mode

The Unity 6000.0.71f1 reference contains 147 exact key/value rows from two independent
windowed GPU Editor runs. Both exited normally on the NVIDIA GeForce RTX 4060.
The fixture disables Unity services in `UnityConnectSettings.asset`. Analytics stays
disabled even after assigning `enabled = true`; the controls verify this before calling
events or transactions. No service data is sent. The probe never requests a crash.

An additional pair with global Unity services enabled and legacy analytics disabled
produces the same 147 rows. Both repeats exited normally on the same NVIDIA GPU. The
`--global-enabled` runner variant applies those settings to a staged copy, directs service
URLs to a closed loopback port and also rejects unexpected unsupported-analytics diagnostics.
The original reference rows are unchanged. The `--legacy-enabled` variant has 153 exact rows.
Two independent captures for each global services value agree on the legacy-enabled results.
Runtime Analytics starts enabled, valid events and transactions return `Ok`, and disabling the
runtime returns `AnalyticsDisabled` for valid calls. Toggling runtime state preserves the
authored settings. Two captures with `--deferred-startup` give the same Play mode results.
The `--authored-cloud-id` variant uses a synthetic ID. Both Editor repeats with that ID and
loopback-only networking also report an unbound project and agree with the 153 runtime rows.
All legacy-enabled controls use loopback service URLs.

The controls cover consent assignment, repeated values, one-field changes, duplicate
subscriptions, removal, unknown enum values, preference writes and `DeleteAll`;
crash-reporting state, buffer limits, missing/replaced/removed metadata, null and empty
keys and UTF-8 key lengths; installation-ID format and agreement with its preference;
analytics name and payload validation; and replacement and removal of obsolete log
callbacks alongside an ordinary log subscriber. Cold and cached worker-thread calls record
thread checks, validation precedence, and unchanged state after rejected setters. Installation IDs are never published.

The documented [consent API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UnityConsent.EndUserConsent.html)
provides state access and a change event. The exact notification and preference behavior
here is a measured observation for this release. The
[crash-reporting API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CrashReportHandler.CrashReportHandler.html)
describes exception capture, metadata and the 50-message buffer limit. The default capture
flag in this fixture comes from its project settings. The
[obsolete callback API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application.RegisterLogCallback.html)
is exercised with different handlers so replacement changes the result. The ordering of
ordinary and threaded log callbacks is recorded for main-thread calls in this release.

From the repository root:

```sh
python3 tests/projects/editorservices/run.py editor --release 6000.0.71f1 --label native
python3 tests/projects/editorservices/run.py publish --release 6000.0.71f1 --label native
UNBOUND_CLI=/path/to/unbound python3 tests/projects/editorservices/run.py unbound --release 6000.0.71f1 --label engine
```

The runner stages authored inputs in the validation cache, uses the shared heavy-job
launcher, verifies Editor completion and repeat equality before publishing, and takes
an immutable runtime snapshot for engine captures. Comparisons retain every missing,
extra or different value in `report.json` and return nonzero for any difference.

Unmeasured contexts remain open: linked Analytics projects, fresh-session malformed
consent preferences, consent changes across domain reloads, and crash-report upload or
snapshot production. `ForceCrash` is outside this probe. Other releases retain the
previous complete preference deletion until their service-owned keys are measured.
