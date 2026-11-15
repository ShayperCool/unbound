# Runtime panel interface

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 59 rows. The values are managed panel and document
state, so they do not depend on the OS or the graphics device. The gate compares every missing,
extra or unequal row exactly.

The probe creates UI documents on two runtime-made panel settings and reads their panels through
`IRuntimePanel` and `UIDocument.runtimePanel`:

- The panel under a document's root is an `IRuntimePanel` (also `IDisposable`, context `Player`)
  whose settings are the document's.
- The selectable object is stored on the panel: setting, replacing and clearing it through the
  interface reads back the same object from the document's panel.
- Documents on the same settings report one shared panel.
- A document keeps reporting the panel its root was on when it was first enabled on settings or
  first asked, after its settings change, while its root moves to the other panel. Activating or
  re-enabling a document on its settings is enough. A document enabled without settings and first
  asked after a change, or a new document on the other settings, reports the other panel.
- An inactive document has no root and no panel; once active it reports its settings' panel. A
  document without settings has a root on no panel and reports none.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [IRuntimePanel](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.IRuntimePanel.html) | `panel.isRuntimePanel`, `first.settings` and the `selectable*` rows read the panel's own state through the interface after it changed. |
| [UIDocument.runtimePanel](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UIDocument-runtimePanel.html) | `changed.reportsA` with `changed.rootPanelIsB`, `fifth.reportsA` and `sixth.reportsA` (enabled, never asked), against `fourth.reportsB`. |

Only flat (screen-space) panels exist here, so the reference's later resolution for other panels
does not apply. Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/runtimepanels/run.py native --label reference-a
python3 tests/projects/runtimepanels/run.py native --label reference-b
python3 tests/projects/runtimepanels/run.py publish reference-a reference-b
python3 tests/projects/runtimepanels/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
