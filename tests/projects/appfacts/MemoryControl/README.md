# Object-memory scope controls

Two independent Unity 6000.0.71f1 Editor captures on the NVIDIA GPU agree on
61 values. This diagnostic supplements the original appfacts reference. It does
not replace its strict comparison or admit its remaining differences.

A Transform's reported size depends on its hierarchy. Setting root capacity to
1, 32 and 128 yields 576, 3944 and 14408 bytes. The child reports 576 bytes when
detached and 296 when parented; the root retains extra storage after detachment.
The [6000.0 hierarchyCapacity documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform-hierarchyCapacity.html)
describes shared hierarchy storage and resizing. A constant per Transform would
miss these ownership and capacity changes.

Material sizes depend on shader data and changes too. The new Unlit/Color
material reports 2128 bytes and 2172 after adding a float property. Standard
reports 5072 and 6588. Copies and later edits distinguish shared data from a
constant instance header. These examples reject a fixed per-class size table;
they do not establish a complete allocation model.

The text controls identify a separate rule by UTF-8 byte length, exercised again
as the fully gated [textassetmemory](../../textassetmemory/README.md) probe. Names,
text/bytes reads, binary constructors and clone lifetime controls are included.
Other component sizes, sprites, tiny-clip initialization, texture allocation
history and wider hierarchy/material cases remain separate work.

```sh
python3 tests/projects/appfacts/memory_control.py editor --release 6000.0.71f1 --label fresh
python3 tests/projects/appfacts/memory_control.py publish --release 6000.0.71f1 --label fresh
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/appfacts/memory_control.py check --release 6000.0.71f1
```

The check command reports all raw differences, including the unsupported memory
kinds. This broader diagnostic is not a new passing parity gate. The original
appfacts gate remains unchanged.
