# Modules probe

What a Unity 6000.0.71f1 project gets when its `Packages/manifest.json` leaves out built-in module
packages. This project has only `com.unity.modules.imageconversion` and
`com.unity.modules.jsonserialize`; Physics, Screen Capture, Cloth and the other package-controlled
modules are left out.

The engine's module lists are the installation's `PlaybackEngines/<platform>/modules.asset` (the
Editor's own in `Resources/modules.asset`). A module marked `controlledByBuiltinPackage: 1` belongs
to the project only when its package `com.unity.modules.<lowercase name>` is resolved (directly or
as another package's dependency). The Editor compiles:

* a runtime assembly against `UnityEngine.dll` and the `UnityEngine.<Name>Module.dll` of each module
  that the active build target's list has and the project has;
* an Editor-only assembly (`includePlatforms: ["Editor"]`, an `Editor` folder) against every module.

`overrideReferences` and `precompiledReferences` choose plugins only; `noEngineReferences` drops
the engine. This rule matched every assembly of 699 Editor compile records (2661 assemblies, the
Linux Standalone and Android targets) of the probe projects and test games.

## Scenarios

`compile` adds `Variants/CompileErrors/Assets` to a copy of the project: a runtime assembly using
Screen Capture, Physics and Cloth names in each way they can be written, the same with
`overrideReferences`, one with `noEngineReferences`, an Editor-only helper exposing Screen Capture
and Physics types, a runtime assembly calling that helper, and `Assembly-CSharp` using Screen
Capture. Measured:

* Only the Editor-only helper compiles; the Editor's batch mode exits 1 ("Scripts have compiler
  errors"). `Assembly-CSharp` is not compiled: the asmdef assemblies it references failed.
* A name `UnityEngine.dll` forwards to a left-out module is CS1069. The Console replaces the
  compiler's "Consider adding a reference to that assembly." with "Enable the built in package
  '<displayName>' in the Package Manager window to fix this error." (the package's `displayName`).
  The Editor log has both texts; `compare.py` takes the Console's.
* A runtime assembly reaching such a type through the Editor-only helper gets CS0012, unchanged.
* With declaration errors (a field of a left-out type), the compiler reports no errors from method
  bodies: as the command-line compiler, it stops after parsing when that has errors, then after
  declarations.

`play` enters Play mode in `Assets/Scenes/Probe.unity` (made by `ProbeRunner.Create`): a box
with a Rigidbody and a BoxCollider, both from the left-out Physics module. When Play mode loads the
scene, the Editor deletes both components, logging (LogType.Log) `BoxCollider component deleted:
Component belongs to a disabled built-in package.` and then the same for the Rigidbody: the order
the GameObject lists its components (the scene file has the Rigidbody first). The box
keeps its Transform and does not fall. The module's types are still found by name. Opening the
scene in Edit mode logs nothing. Prefabs instantiated at runtime were not measured.

## Running

The reference is two byte-identical Native repeats (only one is kept in Git):

    python3 tests/projects/modules/run.py native compile --release 6000.0.71f1 \
        --label native-compile-1 --work ~/.cache/unbound/validation/runs/modules
    python3 tests/projects/modules/run.py native play --release 6000.0.71f1 \
        --label native-play-1 --work ~/.cache/unbound/validation/runs/modules

Unbound (the CLI and the managed directory of one build):

    python3 tests/projects/modules/run.py engine compile --release 6000.0.71f1 --label engine-compile \
        --work ~/.cache/unbound/validation/runs/modules --cli target/release/unbound --runtime target/managed
    python3 tests/projects/modules/compare.py ~/.cache/unbound/validation/runs/modules/engine-compile/result.json

`unbound graph -p <project> --compare-bee <project>/Library/Bee/*-inputdata.json` also compares
every assembly's engine module references with an Editor's record.
