# Legacy UXML base API audit (Q85)

The five errors reported during the R6 validation came from `unbound api-check`, category
`silent`, rather than a source UXML asset audit or a `doctor` content check. The same five
findings reproduced with each of the three validation projects on Unity 6000.0.71f1. The
underlying audit checks the handwritten engine API against the release's public API assemblies,
so these findings do not depend on a project's UXML content.

| Finding | Classification and decision |
| --- | --- |
| `BaseUxmlFactory<TCreatedType, TTraits>.AcceptsAttributeBag` always true | Correct base default. The Editor accepts empty and null bags. Marked `EditorFaithful`; custom factory overrides still decide acceptance. |
| Factory `uxmlAttributesDescription` always empty | Reader bug. The factory must expose its traits' attribute descriptions. Fixed. |
| Factory `uxmlChildElementsDescription` always empty | Reader bug. The factory must expose its traits' child descriptions. Fixed. |
| `BaseUxmlTraits.uxmlChildElementsDescription` always empty | Correct base default. Marked `EditorFaithful`; derived traits can supply children. |
| `UxmlTraits.Init` empty | Correct base default. It leaves existing name, tooltip and children intact and accepts null arguments. Marked `EditorFaithful`; derived initializers still run. |

One extra control found reversed factory substitution metadata. A factory for exactly
`VisualElement` has no substitution, while a factory for a derived element substitutes for
`UnityEngine.UIElements.VisualElement`. Name, namespace and qualified name now follow that rule.
These are observations of the 6000.0.71f1 Editor. Independent GPU Editor captures must agree
before publishing. The generic factory and custom traits are controls independent of the
validation projects. Empty traits, overridden children, overridden initialization and a
rejecting factory guard against replacing all base defaults with one blanket exception.

The Unity 6000.0 [migration guide](https://docs.unity3d.com/6000.0/Documentation/Manual/ui-systems/migrate-custom-control.html)
documents legacy traits/factory fallback for elements without `UxmlElement`, and calls the base
initializer before custom attribute assignments. Its example does not establish the base API's
exact return values. The [public C# API reference source for 6000.0](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Modules/UIElements/Core/UXML/UxmlFactory.cs)
also describes these APIs. Factory getter summaries that describe empty enumerables do not
match the measured custom-traits controls, so the capture is the oracle for those getters.

None of the five findings is an unsupported UXML feature or a Unity content error. Broader
custom-control serialization coverage, UxmlObjects and template attribute overrides remain
tracked by Q17 in `docs/COMPATIBILITY_GAPS.md`. This probe validates only the base API behavior
above; it does not close that broader row.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlbaseapi/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlbaseapi/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlbaseapi/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/uxmlbaseapi/run.py gate
```

The runner copies `lightingassets` into disposable output and enables the UIElements built-in
module there. The publisher stores the verified TSV key/value pairs as JSON, preserving empty strings.
The gate compares every record, retaining every missing, extra or different value,
then runs `api-check` and requires zero errors. `$UNBOUND`, then `$UNBOUND_CLI`, selects the CLI.
Artifacts stay under `$XDG_CACHE_HOME/unbound/validation/runs/uxmlbaseapi/`.
