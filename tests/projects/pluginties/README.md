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

On the 6000.0 family, Unbound repairs measured groups before startup indexing
and compilation using the authored metadata. Each replacement uses an atomic
rename from the project cache. An interrupted import starts again from the GUIDs
currently on disk. The gate compares the reference paths, emitted diagnostics
and metadata bytes, and checks graph inspection after import.

Quoted tokens, mixed asset types, aliased paths, groups outside the measured
package sources, and failed reads or writes produce a diagnostic and retain
the previous reference-selection rule for unresolved GUIDs. The gate also
checks fallbacks for quoted tokens, mixed text and native assets, and read-only
metadata. Changes found by incremental refresh and other release families remain
outside the measured scope.
