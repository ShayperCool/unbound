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

Pass `--conflicts` to record a diagnostic pair with one shared GUID on a
mutable Assets DLL and an immutable package DLL. The Editor reassigns the
Assets metadata GUID. Independent fresh imports with shared GUIDs can pick
different copies. The diagnostic mode retains each capture separately.
Unbound reports missing or shared GUIDs across the equal-version group and
keeps the existing selection order in those cases. Incremental Editor
import history, built Player compilation and other release families remain
unmeasured by the gate.
