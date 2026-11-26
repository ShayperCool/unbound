# Editor interface language settings

`Assets/Scripts/EditorLocalizationProbe.cs` records, in Play mode, what the internal
`UnityEditor.LocalizationDatabase` answers (through reflection, as packages reach it): the current
and default language, whether localization is enabled, the available languages, the localization
folder (relative to the installation), each `SystemLanguage`'s culture code, the translations of a
known, an unknown and an empty string (in the current language, in Japanese and English, with a
group name), the context group name, `noLocalizationGroupName`, `MarkForTranslation`, and
`L10n.Tr`.

Recorded in the windowed Unity 6000.0.71f1 Linux Editor (two identical captures on the private
display): Unbound is the windowed Editor (`Application.isBatchMode` false), and the batch-mode Linux
Editor answers `enableEditorLocalization` false (tests/projects/editorchannels). Measured: English
is the current, default and only available language; localization is enabled; every string is its
own translation; the folder is `<contents>/Localization`; culture codes are ISO codes
(`zh-hans`/`zh-hant`, `""` for Unknown); the context group is empty and `noLocalizationGroupName`
is `.`.

Unmeasured: a macOS installation's available languages and its windowed answer (its batch-mode
Editor enables localization too), installed language packs, null strings, and what the setters and
context groups change beyond the values read back.

    python3 tests/projects/editorlocalization/run.py record --label A   # windowed Editor capture
    python3 tests/projects/editorlocalization/run.py publish A B        # two identical captures
    python3 tests/projects/editorlocalization/run.py gate               # the engine vs Reference/
