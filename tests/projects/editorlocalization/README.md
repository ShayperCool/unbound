# Editor interface language settings

`Assets/Scripts/EditorLocalizationProbe.cs` records, in Play mode, what the internal
`UnityEditor.LocalizationDatabase` answers (through reflection, as packages reach it): the current
and default language, whether localization is enabled, the available languages, the localization
folder (relative to the installation), each `SystemLanguage`'s culture code, the translations of a
known, an unknown and an empty string (in the current language, in Japanese and English, with a
group name), the context group name, `noLocalizationGroupName`, `MarkForTranslation`, and
`L10n.Tr`.

Recorded in the windowed Unity 6000.0.71f1 Linux Editor (two identical captures on the private
display) with clean preferences: the reference's Editor ran with a private HOME whose
`~/.local/share/unity3d/prefs` has no `Editor.kEnableEditorLocalization` (the machine's had 1, which a
package, the tutorial framework, writes when it turns localization on). Measured: English is the
current, default and only available language; localization is disabled; every string is its own
translation; the folder is `<contents>/Localization`; culture codes are ISO codes
(`zh-hans`/`zh-hant`, `""` for Unknown); the context group is empty and `noLocalizationGroupName`
is `.`.

`enableEditorLocalization` follows that preference, measured with private homes (one capture
each, not published): absent or 0 gives false and 1 gives true, in the windowed and the batch-mode
Editor alike. Unbound answers the value without the key and does not read the preference
(docs/COMPATIBILITY_GAPS.md).

Unmeasured: a macOS installation's languages and its answer without the key, installed language
packs (Unbound gives a notice), null strings, `GetLocalizedStringWithGroupNameInLang`, and what the
setters and context groups change beyond the values read back.

    python3 tests/projects/editorlocalization/run.py record --label A   # windowed Editor capture
    python3 tests/projects/editorlocalization/run.py publish A B        # two identical captures
    python3 tests/projects/editorlocalization/run.py gate               # the engine vs Reference/
