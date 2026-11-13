# Imported hierarchy identity scope

Two independent normal Unity 6000.0.71f1 Editor runs on the existing NVIDIA
GPU display agree on four values, with exact source copies. An importer adds
a main texture and a GameObject identified as Root, named root. Its modern
name table maps class 1 and Root to 9876543. The Editor reports local ID
-3282990117334249768, neither that entry nor the generic leaf-derived ID
1121063750183690423. Unbound currently matches 3/4 records. Hierarchy identifiers
need their own general mechanism; applying leaf catalog matching is incorrect.
Child/component identity and hierarchy stability remain unmeasured (M follow-up).

```bash
python3 tests/projects/scriptedfileids/hierarchy_control.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/scriptedfileids/hierarchy_control.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/scriptedfileids/hierarchy_control.py check --release 6000.0.71f1
```

This strict diagnostic keeps the raw failure. It does not replace or weaken the
parent scriptedfileids gate or the separate scriptedidentity leaf gate.
