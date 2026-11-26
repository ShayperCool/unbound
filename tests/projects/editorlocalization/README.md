# Editor interface language settings

`Assets/Scripts/EditorLocalizationProbe.cs` records, in Play mode, what the internal
`UnityEditor.LocalizationDatabase` answers (through reflection, as packages reach it): the current
and default language, whether localization is enabled, the available languages, the localization
folder (relative to the installation), each `SystemLanguage`'s culture code, the translations of a
known, an unknown and an empty string (in the current language, in Japanese and English, with a
group name), the context group name, `noLocalizationGroupName`, `MarkForTranslation`, and
`L10n.Tr`; then the preference `Editor.kEnableEditorLocalization` against the setting: written with
EditorPrefs to the other value and back (`afterPrefWrite`, `afterPrefRestore`), the property set to
the other value (`afterSet`, `afterSet.pref`), the preference written again (`afterSetThenPrefWrite`).

Recorded in the windowed Unity 6000.0.71f1 Linux Editor (two identical captures per profile on the
private display), each with a private HOME whose `~/.local/share/unity3d/prefs` holds only the
profile's preference (`value_probe.private_editor_home`; the machine's may hold the key, which a
package, the tutorial framework, writes when it turns localization on): the reference in
`Reference/unity-6000.0.71f1/` without the key, `one/` with 1, `zero/` with 0. The engine sees the
same preferences through `UNBOUND_EDITOR_PREFS` (`value_probe.private_editor_prefs`). Measured:
English is the current, default and only available language; every string is its own translation;
the folder is `<contents>/Localization`; culture codes are ISO codes (`zh-hans`/`zh-hant`, `""` for
Unknown); the context group is empty and `noLocalizationGroupName` is `.`; localization is enabled
when the preference is 1 (absent or 0: disabled), read once: writing the preference afterwards
does not change it, and setting the property changes it without writing the preference. An
earlier batch-mode capture gave the same answers for the key.

Unmeasured: a macOS installation's languages and its answer without the key, installed language
packs (Unbound gives a notice), null strings, `GetLocalizedStringWithGroupNameInLang`, and what the
setters and context groups change beyond the values read back.

    python3 tests/projects/editorlocalization/run.py record --label A [--profile one|zero]   # windowed Editor capture
    python3 tests/projects/editorlocalization/run.py publish A B [--profile one|zero]        # two identical captures
    python3 tests/projects/editorlocalization/run.py gate                                    # every profile vs Reference/
