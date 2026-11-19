# Compile setup of real projects

Unity 6000.0.71f1, batch mode, Play mode (two byte-identical captures). The gate runs the engine
and compares every value of `values.jsonl` exactly. `build_assets.py` rebuilds the committed
plugins, the local tarball package and the `.meta` files from `PluginSources/` and
`PackageSources/`.

`Assets/Scripts/CompileSetupProbe.cs` writes, in Play mode:

* `plugins.*`: precompiled plugins that declare the same assembly name. `ProbeDup` 1.0.0.0 in
  `Assets/Plugins/Dup` and 2.0.0.0 in the embedded package `com.probe.setup` (its `.meta` has no
  importer settings); `ProbeSame` 1.0.0.0 in `Assets/Plugins/Same` and in the package; `ProbeOrder`
  1.0.0.0 in `Assets/Plugins/Order1` and `Order2`. Rows give the constant the scripts compiled
  against and the value of the loaded copy. Measured: the highest version is the one every
  assembly references and loads (the Editor logs "Duplicate assembly 'ProbeDup.dll' with
  different versions detected, using '…' and ignoring '…'."); of copies with the same version it
  references one, silently, and loads the same one. Which one is not specified: it changed with
  the project's other plugins between captures (a pair whose GUIDs and paths were ordered either
  way picked different copies), so the rows record only that compile and run time agree.
* `assemblies`, `assembly.<name>.*`: `CompilationPipeline.GetAssemblies(AssembliesType.Editor)`
  for the probe's assemblies: flags, references among them, plugins (package plugins by project
  path, same-version copies by file name). Measured: the predefined assemblies reference every
  auto-referenced assembly, Editor-only ones (`Probe.Setup.Editor`) and test ones included, not
  `Probe.Setup.EditorHidden` (Auto Referenced off); test assemblies (`UNITY_INCLUDE_TESTS`) build
  for the project's `Assets` and for the embedded package (`Probe.Setup.Tests`, not listed in
  `testables`), not for the local folder package `com.probe.localtests` or the local tarball
  `com.probe.consumer` (both unlisted).
* `call.editor_only`, `codedom.*`: a runtime script calls the Editor-only package assembly under
  `#if UNITY_EDITOR`; that assembly uses `CSharpCodeProvider` and `CodeTypeReference`.
* `call.tarball`: the tarball package's runtime assembly.
* `call.converter`: `com.unity.2d.pixel-perfect` 5.0.3 with URP 17.0.4. Its Editor assembly names
  URP's `PixelPerfectCamera` through `UnityEngine.Experimental.Rendering.Universal`, where the type
  was before it moved (`[MovedFrom]`): the Editor's script updater rewrites the package's scripts
  in its package cache and the assembly compiles (the row names the converter type it holds).
  The updater runs for names that do not exist (CS0234, CS0246, CS0103) when a referenced
  assembly whose file name starts with "Unity" moved a type from there, and changes files under
  the project's `Library/PackageCache/` without asking; other files need consent, which batch
  mode declines (seen for the same packages through a symbolic link, whose paths the compiler
  then names absolutely; `run.py` records from the resolved run directory). Not measured: updates
  of member names (`(UnityUpgradable)` obsolete messages), which change warnings only here.
