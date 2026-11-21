# Toolbar controls probe

Records the Unity 6000.0.71f1 Editor's UI Toolkit toolbar controls (`UnityEditor.UIElements`):
`Toolbar`, `ToolbarButton`, `ToolbarToggle`, `ToolbarMenu`, `ToolbarSpacer`, `ToolbarSearchField`,
`ToolbarPopupSearchField` and `ToolbarBreadcrumbs`.

- `skin`: the Editor's skin and UI font (`EditorResources.currentFontName`).
- `new.*`: each control as constructed: hierarchy, names, USS classes, text, style sheets, focus.
- `button.clicks`, `toggle`, `menu`, `spacer`, `search`, `popupSearch`, `crumbs`: clicks, value
  changes and events, menu text/variant/items, the spacer's flex, the search field's cancel button,
  Escape and placeholder, breadcrumb push/pop and the error for popping something else.
- `uxml`: the controls created from `Assets/Resources/ToolbarControls.uxml`, including attributes
  their UXML data does not declare (`variant`, `flex`, the search field's `value`).

Menus are not opened (the Editor shows them as its own menus).

## Usage

```bash
python3 tests/projects/toolbarcontrols/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/toolbarcontrols/run.py publish run-a run-b
python3 tests/projects/toolbarcontrols/run.py gate
```

The values depend on neither the OS nor the graphics device (`macos_batch`).

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical captures.
