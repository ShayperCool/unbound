# Managed DLL ties in the Editor compile graph

Unity 6000.0.71f1, Linux OpenGL Core. The runner builds small, unsigned DLLs
with identical file and assembly names from `Plugin.cs`. It places them in
`Assets`, two embedded packages and a local package archive. All affected
assemblies report their actual precompiled reference paths through
`CompilationPipeline.GetAssemblies(AssembliesType.Editor)`. Two assemblies
call a method available only in the winning package copy, so a wrong binding
also changes whether the project compiles.

The 6000.0 Manual describes platform compatibility and the Auto Reference
setting. `GetAssemblies` reports the script assemblies for the requested
compilation mode. Neither page specifies a winner between matching plug-in
copies. [Plugin Inspector](https://docs.unity.com/en-us/engine/6000.0/manual/programming-environment/plug-ins/plug-in-inspector),
[CompilationPipeline.GetAssemblies](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/compilation/compilationpipeline/getassemblies).

## Recording and replay

```sh
python3 tests/projects/pluginties/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/pluginties/run.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=<unbound> python3 tests/projects/pluginties/run.py gate --release 6000.0.71f1
```

Every run generates its own private project. Publication requires two
normally exiting Editor captures with identical records. The record lists
every probe reference from the predefined runtime and Editor assemblies,
the automatic asmdef and the asmdef with Override References. The runner
also records DLL version and asset GUID conflict diagnostics. It replaces
machine-specific package locations with Unity project paths, lists each
distinct diagnostic once, and omits `PublicKeyToken=null` for these unsigned
DLLs. Raw Editor logs stay in the run directory. The engine gate compares
the complete record and compiles the project, including the Editor assembly.

## Observations

- When compatible DLLs share a file name, assembly name and version, the
  earlier asset GUID wins.
  Compare GUID bytes from left to right, reading the low hex digit first in
  each printed pair. Controls reverse the selected copy in two Assets paths,
  two embedded packages, and Assets versus an embedded package or archive.
  Additional controls distinguish byte order from numeric text order in all
  four GUID words. Equal versions give no duplicate-version diagnostic.
- Platform eligibility precedes selection. The higher eligible version wins
  in either directory; the duplicate-version diagnostic names that copy.
  The three-copy cases tie for the higher version and check that the
  diagnostic names the selected one, with either matching or mixed reference modes.
- Auto Reference applies after selection. With mixed settings, either copy
  can win by GUID. If the winner requires an explicit reference, only the
  asmdef that names it receives the DLL.

Pass `--conflicts` to record and publish the fresh-import cases. Unity gives
each mutable DLL a fresh GUID when they share a GUID on first import.
Registry and local tarball packages retain their original GUIDs. The
rewrite replaces the plain GUID token and preserves the other bytes,
including CRLF. The batch diagnostic names the other metadata paths.
An earlier import can establish an owner before a later file arrives, and
the diagnostic then names the current owner. The local-tarball and embedded
package controls record that import history. [Asset metadata](https://docs.unity.com/en-us/engine/6000.0/manual/assets-and-media/import-assets/asset-metadata),
[Package sources](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/packages/upm-concepts).

Both the ordinary and linked Assets profiles cover the same two- and three-copy
groups. The order of diagnostic peer paths varied across independent linked
captures. The comparison checks every peer path and owner label as an unordered
list. Complete logs and assigned GUIDs stay in each run directory.

The fresh profiles check that every graph reference follows the assigned GUIDs.
The archive keeps the reference with the early GUID and loses it with the late
GUID. The other pairs can select either copy. The reference preserves their
assembly and DLL names, diagnostic text, GUID ownership and a hash of each
metadata file with its GUID token replaced. The project compiles with the
registry DLL's package-only API, then compiles again with identical metadata.
A private loopback registry serves the synthetic package.

Pass `--conflicts --established` to record or replay reopening an imported
project. The runner restores the authored Assets metadata and recorded file
timestamps after the first import. On reopening, the Editor leaves all four
metadata files byte-for-byte unchanged and emits no GUID reassignment message.
The registry paths retain the authored GUIDs in the database, and the Assets
paths retain their distinct registered GUIDs. The early registry GUID selects
the registry DLL and the late registry GUID selects the Assets DLL.
[AssetDatabase.AssetPathToGUID](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.AssetPathToGUID.html),
[AssetDatabase.GUIDToAssetPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.GUIDToAssetPath.html).

On the 6000.0 family, Unbound repairs measured fresh groups before startup
indexing and compilation. Each replacement uses an atomic rename from the
project cache. An interrupted import starts again from the GUIDs currently
on disk. Graph inspection stays read-only. On Linux with liblmdb, the script
graph uses the registered GUIDs for a measured established Assets and registry
pair once the path and GUID lookups agree. That replay compares the reference
paths, emitted diagnostics, and complete metadata bytes.

Quoted tokens, mixed asset types, aliased paths, groups outside the measured
package sources, and failed reads or writes produce a diagnostic and retain
the previous reference-selection rule for unresolved GUIDs. Asset database
registration beyond managed DLL selection, incremental refresh, and other
established-database formats and hosts remain outside this profile.
