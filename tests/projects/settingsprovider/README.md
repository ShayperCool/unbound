# SettingsProvider and SettingsService

`UnityEditor.SettingsProvider`, `UnityEditor.SettingsProviderAttribute`,
`UnityEditor.SettingsProviderGroupAttribute`, `UnityEditor.PreferenceItem` and
`UnityEditor.SettingsService`: constructor path normalization (`\n` -> `" "`, `\\` -> `"/"`),
default label fallback to the last path token, keyword deduplication, handler delegation from
virtual callbacks, `HasSearchInterest` multi-word ordered matching (including boundary character
overlap and `hasSearchInterestHandler` override), keyword extraction helpers
(`GetSearchKeywordsFromGUIContentProperties`, `GetSearchKeywordsFromSerializedObject`,
`GetSearchKeywordsFromPath`), attribute-based provider discovery, invalid-signature and
throwing-provider error logs, duplicate path filtering per scope with warning, `Exists` ordinal
matching, and notification events (`NotifySettingsProviderChanged`, `RepaintAllSettingsWindow`).
Recorded in Play mode, in batch mode, with the Unity 6000.0.71f1 Editor; two identical captures.

Not measured: `SettingsService.OpenProjectSettings` and `OpenUserPreferences` (Editor settings
window UI is not implemented in Unbound), built-in Editor settings pages, and which duplicate
wins when two `[SettingsProvider]` methods register the same path in the same scope (Unity does
not guarantee registration order; the probe checks that only one is kept and the warning is logged).

The scene, Play-mode runner and packages are `tests/projects/imguiskin`'s, and the project
settings are `tests/projects/analyticssession`'s. Values compare exactly.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/settingsprovider/run.py record --label a
python3 tests/projects/settingsprovider/run.py record --label b
python3 tests/projects/settingsprovider/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/settingsprovider/run.py gate
```

`fetch.repeatedLogs` counts each discovery message over two more lookups (a method of the wrong
signature is reported once, when the providers are first looked up; a throwing factory and a
duplicate path on every lookup), and `fetch.wrongShapeLogs` what an instance method, a generic
method and a `[PreferenceItem]` of the wrong signature log on the first lookups;
`exists.wrongShapes` that none of them provides a page.
