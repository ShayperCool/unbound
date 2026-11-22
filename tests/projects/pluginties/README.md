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
collision log names the conflicting paths. An embedded DLL colliding with
a local tarball reports a current owner. The same collision with a registry
package reports both metadata paths. The rewrite replaces only the plain
GUID token, including when the metadata uses CRLF. This uses Unity's unique
asset IDs and the Package Manager's distinction between mutable folders and
immutable packages.
[Asset metadata](https://docs.unity.com/en-us/engine/6000.0/manual/assets-and-media/import-assets/asset-metadata),
[Package sources](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/packages/upm-concepts).

The three-copy controls show that a batch diagnostic names the other
mutable files before the immutable package. A current-owner diagnostic
names the established owner.

The gate runs both profiles. The import profile checks that every graph
reference follows the newly assigned GUIDs. The archive keeps the reference
with the early GUID and loses it with the late GUID. The remaining pairs
can select either copy on a fresh import. The reference preserves their
assembly and DLL names, the exact conflict diagnostics, GUID ownership and
hashes of every metadata file with its GUID token replaced. The raw captures
include the assigned GUIDs and complete reference paths. The project must
compile with the registry DLL's package-only API, then compile again with
identical metadata. A private loopback registry serves the synthetic package.

Unbound performs the measured repair before startup indexing and compilation
on the 6000.0 family. An interrupted repair resumes from the project cache,
including after a project directory move.
Graph inspection reads the current metadata and reports unresolved ties.
Quoted GUIDs use a different metadata rewrite in Unity. Unbound reports
that case and preserves the prior compile behavior. Unmeasured groups with
multiple immutable owners, aliased metadata or an existing Unity source
asset database stop the repair with a diagnostic. The existing database
can change the owner and the conflict warning. Incremental registration
remains outside this import profile.
