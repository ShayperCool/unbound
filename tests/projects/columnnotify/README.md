# Multi-column binding notifications

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 60 rows. The values are managed event sequences, so
they do not depend on the OS or the graphics device. The gate compares every missing, extra or
unequal row exactly.

Column, Columns and SortColumnDescription report property changes through
`INotifyBindablePropertyChanged`. The probe listens to every object and records, per change, the
notifications in order as `sender:propertyName`:

- Each of a column's thirteen bindable properties (name, title, icon, visible, width, minWidth,
  maxWidth, sortable, stretchable, optional, resizable, headerTemplate, cellTemplate) notifies once
  when it changes and not when set to an equal value; delegates, the comparison and the binding
  path do not notify.
- Columns notifies its own five properties and forwards a column's change as
  `columns[index].property`, but only for a column above index zero: the first column's changes are
  not forwarded, and a column moved to index one by an insertion is. A removed column no longer
  forwards; a column added to another collection forwards there only. Adding, inserting and
  removing columns do not notify.
- A sort description notifies its column name, column index and direction, also inside a
  description list and after construction with initial values.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [INotifyBindablePropertyChanged](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.INotifyBindablePropertyChanged.html) | Every `second.*` and `sort.*` change against its `.same` repeat. |
| [Columns](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.Columns.html) | `first.title` against `first.title.atOne`, `third.title.removed` and `third.title.moved`. |

Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/columnnotify/run.py native --label reference-a
python3 tests/projects/columnnotify/run.py native --label reference-b
python3 tests/projects/columnnotify/run.py publish reference-a reference-b
python3 tests/projects/columnnotify/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
