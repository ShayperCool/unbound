# UnityEngine.CoreModule (Unbound)

Unbound's implementation of the Unity engine API.

Conventions:

- Every public type is declared `partial`, so generated compatibility stubs can add the members a
  Unity release has that are not implemented yet.
- Signatures follow Unity's reference assemblies of the selected release exactly (names, parameter
  names, default values, `ref`/`out`, attributes that affect behaviour). Bodies are Unbound's own:
  never copy or decompile Unity's implementation; reproduce documented and observed behaviour.
- Behaviour that differs between Unity releases asks `Unbound.Compat.Profile` (never compares versions
  inline).
- Folders: `Core/` object model and lifecycle, `Math/` value types, `Runtime/` player loop, loading
  and the bridge to the native side.
