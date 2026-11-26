# Package samples

`PackageManager.UI.Sample` as the Unity 6000.0.71f1 Editor answers it, recorded with the macOS Editor
in batch mode (two identical captures; nothing here depends on the OS or the graphics device).

`Packages/com.probe.samples` is an embedded package (version 1.2.3, display name `Probe: Samples`)
whose package.json lists three samples: `Basic Sample` (`Samples~/Basic`: `Readme.txt` with its
`.meta`, `Sub/Data.txt` without one), `Inter: Active?` (`Samples~/Inter`, `interactiveImport`) and
`Gone` (a folder that does not exist); `Packages/com.probe.plain` has none.

`Assets/Scripts/PackageSamplesProbe.cs` records in Play mode into values.jsonl in
`$PACKAGESAMPLES_OUT` (tests/value_probe.py), the package's folder read `<package>` and the project's
`<project>`: `find/*` (`FindByPackage` with the package's version, none, null, another version, for a
package without samples, a built-in module, a missing package and a null name: each sample's names,
paths and flags), `import/*` (`Import` with no option, again, with `OverridePreviousImports` after an
earlier version's import was made, `HideImportWindow` of the interactive one, the missing one) and
`files/*` (what is under `Assets/Samples` then).

What it shows: the samples of the installed package of that name, when the version is none or its
own; each sample's `resolvedPath` is the package's folder and its path, its `importPath`
`Assets/Samples/<display name>/<version>/<sample display name>` (absolute; `:` and `?` read `_`).
`Import` copies the sample's folder there (its `.meta` files as they are; the Editor's refresh gives
the others theirs) and answers true; a second import, while any version's import of the sample
exists, answers false and writes nothing; `OverridePreviousImports` removes every version's import of
it (with its `.meta` and the folders it leaves empty) first; a sample whose folder is missing answers
false and logs `[Package Manager Window] Cannot import sample <name>: <the IO error>`.

Not covered: a sample holding a `.unitypackage` (imported as a package), a package from the
registry or a `file:` path, `Window.Open`.

Record and gate:

```bash
python3 tests/projects/packagesamples/run.py record --label <label>   # twice, then publish one
python3 tests/projects/packagesamples/run.py gate
```
