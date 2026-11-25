# moduleassemblies

Which of Unity's module assemblies are loaded in Unity 6000.0.71f1 Play mode, by name
(`AppDomain.CurrentDomain.GetAssemblies()`), and what a lookup through one finds
(`Assets/Scripts/ModuleAssembliesProbe.cs`; two byte-identical macOS batch Editor captures): the
engine and Editor modules the project's packages do not use, the internal
`UnityEditor.UIElements.DefaultWindowBackend` type through `UnityEditor.UIElementsModule` and its
`visualTree` property (as packages reach it by reflection), and `UnityEngine.Rigidbody` through
`UnityEngine.PhysicsModule`.

Measured: every module listed is loaded, and lookups through a module find its types. Unbound has
one engine assembly and a facade per module that forwards the module's types to it; the facades are
loaded when the game's code starts, so a module is found by name and its types resolve to Unbound's.

```sh
python3 tests/projects/moduleassemblies/run.py record --label a
python3 tests/projects/moduleassemblies/run.py record --label b
python3 tests/projects/moduleassemblies/run.py publish a b
python3 tests/projects/moduleassemblies/run.py gate
```
