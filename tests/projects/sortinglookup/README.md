# SortingGroup lookup controls

Unity 6000.0.71f1 controls for the internal `GetSortingGroupByIndex`,
`invalidSortingGroupID` and `index` members. Declarations come from the selected
release through StubGenerator. Two independent GPU Editor captures match exactly.

The [SortingGroup API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.SortingGroup.html)
documents grouping and nesting of renderers, and an immediate update method.
It does not define the internal registry's numeric indices or activation order.
This probe records those details in this release and context. It covers inactive
creation, enable/disable, ancestor activation, nesting, destruction, compaction,
reuse, sibling controls, growth through 70 groups and main-thread restrictions.
Disabled groups use the invalid index. Removing an active group moves the last
entry into its slot and updates that group's index. A newly activated hierarchy
registers descendants before their parent. The indices are registry positions,
not persistent object identities.

All captures were recorded on the NVIDIA GPU driver; other devices and releases
were not measured. The registry does not change the renderer's sorting rules.

```sh
python3 tests/projects/sortinglookup/run.py native --release 6000.0.71f1 --label native-a
python3 tests/projects/sortinglookup/run.py native --release 6000.0.71f1 --label native-b
python3 tests/projects/sortinglookup/run.py publish native-a native-b --release 6000.0.71f1
python3 tests/projects/sortinglookup/run.py check --release 6000.0.71f1
```

The gate runs only the engine, reads the committed release reference, and reports
every missing, extra or differing row with exact comparison.

Ungrouped SpriteRenderer and MeshRenderer IDs retain the invalid sentinel across
activation changes, so a uGUI physics raycaster cannot resolve them to the first
active group. Automatic renderer association and group-relative order remain a
follow-up; these stored renderer fields are not populated from the hierarchy yet.
