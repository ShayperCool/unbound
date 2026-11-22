# initializeorder

How the Unity 6000.0.71f1 Editor loads the editor domain as Play mode is entered, as far as editor
initializers and imports are concerned (two byte-identical macOS batch Editor captures; nothing
depends on the OS or the graphics device). The project has `[InitializeOnLoad]` classes in:

- an embedded package's editor assembly (`Zz.Probe.InitOrder.Editor`, with a sprite postprocessor
  that reports whether its initializer has run, and an `OnPostprocessAllAssets`);
- two Assets asmdef assemblies, `Aa.Dependent.Editor` referencing `Zz.Base.Editor`;
- the predefined `Assembly-CSharp-Editor`, whose code uses the package's;
- four assemblies no other uses: `Aa.Unused.Editor` and `Zy.AssetsUnused.Editor` in Assets,
  `Ab.PackageUnused.Editor` and `Zz.Unused.Editor` in the package.

The project's initializer (`Assets/Editor/GameInit.cs`), as a project's toolbar extension finds its
settings asset, searches `t:Holder`, loads a settings asset that references a sprite (imported at
the project's import) and forces the import of another sprite; an `[InitializeOnLoadMethod]` and a
`[DidReloadScripts]` method force the import of one more each. The rows: the initializers in the
order they ran (`order.initializers`), the project initializer's events and every postprocessor
call while the domain loads (`game.duringLoad`) and from afterAssemblyReload on (`game.afterLoad`),
and the postprocessing of the loaded sprite (`loaded.postprocess`, kept apart).

Measured: initializers run by assembly, the assemblies by name, each moved after the assemblies its
code references (a package's initializer runs before the project's only when the project's code
uses the package; otherwise names decide, so `Assembly-CSharp-Editor` comes before a
`Unity.*` package assembly). Searching and loading import nothing and run no postprocessor. An
import asked for while the domain loads returns at once; the imports run after afterAssemblyReload,
in the order asked, each with its postprocessors, and the domain load's single
`OnPostprocessAllAssets` call (didDomainReload true) lists their assets (their order in it is the
Editor's own: the probe sorts them).

Residual (`residuals.json`): `loaded.postprocess`. Unbound keeps no post-processed artifacts and
replays an asset's postprocessors when it first loads it in a session; for a load during the domain
load the replay runs after the load, with the deferred imports.

```sh
python3 tests/projects/initializeorder/run.py record --label a
python3 tests/projects/initializeorder/run.py record --label b
python3 tests/projects/initializeorder/run.py publish a b
python3 tests/projects/initializeorder/run.py gate
```
