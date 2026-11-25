# plugindefaults

What the PluginImporter answers for a plug-in whose `.meta` holds its GUID alone, as the Editor
writes it for a new plug-in (Unity 6000.0.71f1, two identical macOS batch Editor captures; the
settings are written into the project's `.meta` files and depend on neither the OS nor the graphics
device, apart from the installation's Android module, installed on both machines).

`run.py` stages, in a fresh copy of the probe, about 170 plug-ins: every kind the PluginImporter
imports in `Assets/Plugins` (managed and Windows `.dll`, `.so`, `.dylib`, `.a`, `.jar`, `.aar`,
C/C++ sources and headers, `.m`, `.mm`, `.swift`, `.java`, `.kt`, `.jslib`, `.jspre`, and the
folder plug-ins `.bundle`, `.plugin`, `.framework`, `.xcframework`, `.androidlib`), then kinds in the
folders whose names set platforms (`Editor` at any depth, `Plugins/x86`, `x86_64`, `x64`, `ARM64`,
`Plugins/Android` with its ABI folders, `iOS`, `visionOS`, `WebGL`, `WSA`, `Metro`) and in
folders that do not (`tvOS`, `OSX`, `Win64`, `Linux64`, `Switch`, a platform folder below another
folder or in a package), and in an embedded package. Managed plug-ins are compiled each under its own
name and file name (the Editor compiles against one precompiled assembly per file name), native
libraries outside `Assets/Plugins` are numbered (the Editor reports two loadable libraries of one
name). None is managed in a 64-bit CPU folder: whether the Editor compiles against it depends on the
machine's CPU. Hand-written sections (`Assets/Controls`) check "Any Platform" with exclusions, the
Editor alone, an Editor CPU (`x86`) and OS (`Windows`) of no Editor here, a section without
platform settings, another importer's section and a shared object's section without its Android
entry.

`Assets/Scripts/PluginDefaultsProbe.cs` records in Play mode, for each plug-in, the importer's type,
`GetCompatibleWithAnyPlatform`, `GetCompatibleWithEditor` (also with a build target), the Editor's
exclusion, `isNativePlugin`, `isPreloaded`, `GetIsOverridable`, `DefineConstraints` (`*.flags`), the
platforms `GetCompatibleWithPlatform` gives (`*.compatible`) and `GetExcludeFromAnyPlatform` gives
(`*.excluded`), `GetPlatformData` of the platforms' setting keys (`*.data`), `GetEditorData`
(`*.editorData`); `PluginImporter.GetImporters` for four platforms (`importers.*`), the managed
plug-ins `CompilationPipeline.GetPrecompiledAssemblyNames` names (`precompiled`), and each `.meta`
after `AssetDatabase.SetLabels` writes it again (`*.labelled`, GUID replaced). Values compare
exactly.

Measured: the rule is in `crates/unbound-project/src/plugin_defaults.rs`. A section that has
platform settings is read with its kind's missing entries and an Editor CPU that is none of the
Editor's architectures turns the Editor off and excludes it; `GetCompatibleWithPlatform` and
`GetCompatibleWithEditor` answer the platform's own setting; `GetPlatformData` gives "" for `Any` and
`Editor`.

`GetImporters("Android")` leaves out a package's static library that has the name of one in the
project: as Unity's own C# `PluginImporter.GetImporters`, an overridable (package) plug-in going to
the same place in a build as another is left out.

Residual (`residuals.json`): a section that has platform settings, written again, is written as it
was (the Editor writes it as it reads it).

Unmeasured: other release families, an installation without the Android module, a section that
has platform settings but no `DefaultValueInitialized` (the Editor gives some kinds their defaults
again; Unbound reads it as it is), the importer's
answers after the Editor has written a plug-in's `.meta` with its defaults (an `x86` plug-in is then
read with the Editor off), the Editor's log line for an Android shared object without an ABI folder.

```sh
python3 tests/projects/plugindefaults/run.py record --label a   # macOS: holds the heavy-job lock
python3 tests/projects/plugindefaults/run.py record --label b
python3 tests/projects/plugindefaults/run.py publish a b
python3 tests/projects/plugindefaults/run.py gate
```
