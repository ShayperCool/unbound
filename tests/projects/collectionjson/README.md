# Collection view selection through JsonUtility

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode; independent repeats are
byte-identical and the canonical reference has 26 rows (managed collection view state in a runtime
panel, independent of the OS and the graphics device). The gate compares every row exactly.

BaseVerticalCollectionView is an `ISerializationCallbackReceiver`: `JsonUtility.ToJson` writes its
serialized selected ids, and `FromJsonOverwrite` replaces them, after which the view takes them as
its selection and refreshes its items. Each state row lists the selected ids and indices,
`selectedIndex`, `selectedItem`, the number of selected items and the selection events
(`selectionChanged`/`selectedIndicesChanged`) since the previous row; `*.marked` rows add the rows
bound and those marked selected.

- `toJson.*`: the selected ids ToJson writes; `direct.*` (control): calling either callback
  directly, with nothing deserialized, changes nothing and sends no event.
- `overwrite.*`: ids replaced in a ListView in a panel: the indices follow at once, one selection
  event, the rows marked; then AddToSelection of a selected and of a previously selected index,
  RemoveFromSelection and ClearSelection.
- `missing.*`, `empty.*`: an id beyond the items still gives its index (a list's id is its index);
  an empty list clears the selection.
- `noSource.*`: without an items source the ids are kept and no index is selected until a source
  is set; `detached.*`: outside a panel, then attached; `detachedControl.*` (control): the same
  selection made through the selection API.
- `tree.*`: a TreeView takes item ids: a collapsed item's id stays selected without an index until
  its parent is expanded.

Other releases are unmeasured.
