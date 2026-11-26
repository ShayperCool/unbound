# The quality level Play mode starts with

Six built-in quality levels (Very Low ... Ultra, each with its own shadow distance), the current
level `m_CurrentQuality` 1 (Low), the Standalone default 4 (Very High) and the Android default 2
(Medium), so each candidate rule gives another level. `Assets/Scripts/QualityStartupProbe.cs`
records, at SubsystemRegistration, BeforeSceneLoad, Awake and on the third frame, the level
`QualitySettings.GetQualityLevel()` answers, its name and settings, and `QualitySettings.names`.

Profiles (`run.py`; the default profile's reference is `Reference/unity-<release>/`, the others
are subdirectories):

| profile | build target | quality levels excluding a platform |
|---|---|---|
| (default) | Standalone | none |
| `android` | Android (`-buildTarget Android`, Unbound `--target Android`) | none |
| `excluded` | Standalone | levels 1 (the current one) and 3 exclude Standalone |
| `excludeddefault` | Standalone | as `excluded`, the Standalone default 3 (excluded too) |
| `excludedmiddle` | Standalone | levels 0 and 3 exclude Standalone, current 3, Standalone default 5 |
| `excludedandroid` | Android | as `excluded` (they exclude Standalone only) |

Measured in the Unity 6000.0.71f1 Editor (batch mode, two identical captures per profile): Play
mode starts at the current level, not the platform's default, on either target. A level whose
`excludedTargetPlatforms` names the active target's platform is not offered: `names` lists only
the other levels, and levels are numbered among them. When the current level is excluded, Play
mode starts at the first level offered (index 0), neither the default level nor the excluded
level's neighbour (`excludedmiddle`: Low, not Medium, Very High or Ultra). Levels excluding
another platform are offered (`excludedandroid`).

Inferred, not measured: the other members that take a level index (`GetRenderPipelineAssetAt`,
`SetQualityLevel`, `count`) number the offered levels as `names` does (the probe's built-in
project has no pipeline assets); iOS and WebGL behave as Standalone and Android (their levels are
excluded as `iPhone` and `WebGL`); a project whose levels all exclude the platform; a current level
past the last level.

    python3 tests/projects/qualitystartup/run.py record --label A [--profile P]   # Editor capture
    python3 tests/projects/qualitystartup/run.py publish A B [--profile P]        # two identical captures
    python3 tests/projects/qualitystartup/run.py gate                             # the engine vs Reference/, every profile
