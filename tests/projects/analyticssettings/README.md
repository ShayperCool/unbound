# AnalyticsSettings reads

The probe measures `UnityEditor.Analytics.AnalyticsSettings` in Play mode on Unity 6000.0.71f1.
It stages `UnityConnectSettings.asset` with each service flag enabled separately. Every capture
uses a private project and loopback service URLs.

`enabled` reads the Analytics service flag. In the selected build target, `enabledForPlatform`
also returns true for that flag. Player core statistics are requested for enabled Analytics,
Ads, Purchasing, Performance Reporting or Cloud Diagnostics. The global services switch,
Insights flag, and persisted package-presence flag alone leave both answers false. A subscriber
added after the first core statistics read in Play mode leaves the published value unchanged.

The committed profiles use Linux Standalone builds. Separate Linux Editor runs with Android
and iOS targets confirmed the Analytics-enabled state there. Live settings changes and build-time
evaluation of package callbacks remain open.
[Unity 6000.0 `AnalyticsSettings`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Analytics.AnalyticsSettings.html)

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/analyticssettings/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/analyticssettings/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/analyticssettings/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/analyticssettings/run.py gate
```

Use `--profile` to run a single configuration.
