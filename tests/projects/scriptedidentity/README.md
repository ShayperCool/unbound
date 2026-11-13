# Scripted-import leaf identity

Two independent Unity 6000.0.71f1 Editor GPU captures agree on all 136 records;
committed sources match both captured projects. Fifteen assets vary only their
name-table metadata. Each importer adds a named main Texture2D, an unnamed
Texture2D and an unnamed LightmapParameters. The latter has a public managed
type and class-catalog entry but no registered serialized-field loader in Unbound.
This measures metadata identity, not support for its serialized fields.

```bash
python3 tests/projects/scriptedidentity/run.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/scriptedidentity/run.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/scriptedidentity/run.py check --release 6000.0.71f1
```

For leaf assets, a matching class and identifier in internalIDToNameTable
selects the file ID even when that class has no field loader. Unknown classes,
wrong classes and wrong identifiers are ignored without warnings in these
Editor captures. They retain the derived ID. Modern matching entries win over
legacy entries. fileIDToRecycleName contributes only for serializedVersion 1
(or an absent version); version 2 ignores it. For old schemas, legacy entries
fill missing class/identifier pairs in numeric file-ID order, independently of
YAML map order. All unnamed subobjects retain empty names; the stable identifier
is not a display-name fallback. The original scriptedfileids 184-record oracle
and comparator remain unchanged.

Unbound uses the existing selected-release class catalog for otherwise
unregistered builtin leaf types; it does not add class-ID constants or register
unsupported loaders. GameObject and Component identities have a separate
hierarchy context and are excluded from this new catalog fallback. The separate
scriptedfileids/HierarchyControl diagnostic records an unresolved root ID.
The engine regenerates this managed object map on import; there is no persisted
ScriptedImporter-output cache format to bump. Serialized loader caches do not
change.

The public [AssetImportContext.AddObjectToAsset API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetImporters.AssetImportContext.AddObjectToAsset.html)
requires a stable unique identifier for each produced object. The table schema,
precedence and empty names above are measured release/context observations.
