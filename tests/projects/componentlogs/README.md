# Duplicate component logs

Unity 6000.0.71f1, Linux, NVIDIA driver. Two independent GPU Editor captures
agree exactly on 1345 keyed values. The comparison is exact (tolerance zero).

The 56 contexts cover the six audio filters and a custom
`[DisallowMultipleComponent]` MonoBehaviour, enabled and disabled instances,
default and forwarding custom log handlers, and enabled and muted public logging.
Each rejects the second component, retains the first instance, and records all
five severities immediately, one frame later and two frames later. A quiet clip
prevents unrelated missing-clip warnings. Full console output stays in the run cache.

The measured rejection publishes one immediate `LogType.Log` to both Application
callbacks, bypassing `Debug.unityLogger.logHandler` and `logEnabled`; neither later
snapshot adds a diagnostic. Public `Debug.Log`, `LogWarning` and `LogError` controls
exercise the custom handler and mute setting where each changes the result.
No callback ordering or exact diagnostic wording is asserted.

Unity 6000.0 contracts:

- [DisallowMultipleComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DisallowMultipleComponent.html)
  prevents a duplicate component. The custom component makes this guarantee observable.
- [Logger.logEnabled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Logger-logEnabled.html)
  toggles that logger; the public log controls verify both states. It does not
  establish how engine diagnostics reach subscribers; that route is measured here.
- [Application.logMessageReceived](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-logMessageReceived.html)
  runs on the main thread. Every snapshot records the callback thread.

The existing `Compat.ComponentFactoryEligibilityMeasured` selects the measured
rejection route. Other releases retain the prior, unqualified public Error route.
Other component eligibility diagnostics are outside this measurement.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/componentlogs/run.py record --label native-a
python3 tests/projects/componentlogs/run.py record --label native-b
python3 tests/projects/componentlogs/run.py publish native-a native-b
python3 tests/projects/componentlogs/run.py gate
```
