# Type and assembly names

`Assets/Scripts/TypeNamesProbe.cs` records, in Play mode, for class library types (mscorlib,
System, System.Core and System.Xml ones, arrays, a nested type, nullable and generic types, an open
generic), game types and engine types (a CoreModule one, a PhysicsModule one, a list of one):
`AssemblyQualifiedName`, `FullName`, `ToString()`, `Assembly.FullName`, `Assembly.GetName().Name`
and `.FullName`, `Assembly.ToString()`, and whether `Type.GetType` of the qualified name gives the
type back; and what `Type.GetType` finds for ten names (Mono's, .NET's, partial ones, an engine type
under its module and under UnityEngine.CoreModule).

Measured in the Unity 6000.0.71f1 Editor (batch mode, two identical captures): class library types
are named after the Mono assembly that defines them, `Version=4.0.0.0, Culture=neutral,
PublicKeyToken=b77a5c561934e089` (`System.Boolean, mscorlib`, `System.Uri, System`,
`System.Collections.Generic.HashSet`1`, `System.Linq.Enumerable` in System.Core,
`System.Xml.XmlDocument, System.Xml`, `BinaryFormatter` in mscorlib, whose .NET assembly Unbound ships itself); a constructed generic type's `FullName` and qualified name
hold its arguments' qualified names; engine types are named after their module
(`UnityEngine.Rigidbody, UnityEngine.PhysicsModule, Version=0.0.0.0, Culture=neutral,
PublicKeyToken=null`); every qualified name reads back; `Type.GetType` finds Mono's names, not
.NET's (`System.Private.CoreLib`) nor an engine type under UnityEngine.CoreModule.

Unbound (`managed/UnityEngine/Runtime/MonoTypeNames.cs`) redirects game and package assemblies'
`Type.AssemblyQualifiedName`, `Type.FullName`, `Assembly.FullName` and `Assembly.GetName()`; the
known residuals (`residuals.json`) are the assembly-level names it cannot map and the lookups it
answers more leniently.

    python3 tests/projects/typenames/run.py record --label A   # Editor capture (batch mode)
    python3 tests/projects/typenames/run.py publish A B        # two identical captures
    python3 tests/projects/typenames/run.py gate               # the engine vs Reference/
