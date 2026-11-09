# UI Toolkit bindings in UXML, style bindings, binding logs and view data

Measures what the Unity 6000.0.71f1 Editor does with bindings declared in UXML and bindings to style
properties on a runtime panel (a UIDocument made in Play mode, drawing into an 800x600 target so the
layout does not depend on the screen). The Editor runner re-imports the documents while recording
the importer's warnings and errors, then enters Play mode, where `Assets/Scripts/BindingsProbe.cs`
clones the documents and steps through source and field changes; each step runs two frames (a
panel update, then a repaint, when bindings update) and records every probe element's text or
value, its binding ids (in the order GetBindingInfos lists them) and last results, the style values
and resolved style of the styled elements, and the sources.

- `Assets/Resources/Bindings.uxml`: a box whose `data-source` is `BindData.asset` (with
  `data-source-type`), and labels and fields with `<Bindings>`: DataBinding with each binding mode,
  `update-trigger` WhenDirty and EveryUpdate, its own `data-source` asset, a hierarchy
  `data-source-path`, `source-to-ui-converters` (one group, an unknown id followed by two groups,
  a group registered after cloning), `ui-to-source-converters`, `style.*` targets by C# and USS
  names, a game `[UxmlObject]` binding (`ProbeCustomBinding`, its `[UxmlAttribute] prefix`), a
  path and a target property that do not exist, two bindings of one property, a child after the
  bindings;
- `NoProperty.uxml`, `NotBinding.uxml`, `UnknownType.uxml`: a binding without `property`, a Column
  among the bindings, a tag naming no type;
- bindings to style properties made in code: from a float, an int, a Length, a StyleKeyword, a
  string (no conversion), a Color under the USS name, an enum, `style.width.value`, a missing style
  property, and ToSource bindings from `style.width` and `resolvedStyle.width`;
- `loglevel`: warnings of a binding failing on every update after `Binding.ResetPanelLogLevel`,
  `SetPanelLogLevel(All)`, and with `PanelSettings.bindingLogLevel` All, Once and None;
- `viewdata`: a ScrollView offset, a Foldout value and a ListView selection, each element removed
  and a new one with the same `viewDataKey` added, and the removed ones added back.

Documented (Unity 6000.0 Manual "Define data binding in UXML", "Runtime data binding", "View data
persistence"; Scripting API DataBinding, Binding, ConverterGroups, BindingLogLevel): the attributes
and their meaning, `style.*` binding targets, view data persistence being an Editor feature.
Observed in this capture: converter ids resolved when cloning (a later group is not used, unknown
ids skipped, a later group replacing an earlier one's converter); a UXML object other than a
binding dropped silently and an unknown tag reported by the importer; a binding without `property`
reported at the panel's first update; bindings not processed yet listed newest first; a path that
leads nowhere naming the source's type; a runtime panel applying its PanelSettings binding log
level on every update (so SetPanelLogLevel and ResetPanelLogLevel do not last); no view data
restored on a runtime panel.

```sh
python3 tests/projects/uitkbindings/run.py record --label native-a
python3 tests/projects/uitkbindings/run.py record --label native-b
python3 tests/projects/uitkbindings/run.py publish native-a native-b
python3 tests/projects/uitkbindings/run.py gate
```
