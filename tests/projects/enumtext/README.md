# Enum names

`Assets/Scripts/EnumTextProbe.cs` writes the text enum values print with into values.jsonl in
`$ENUM_TEXT_OUT` (tests/value_probe.py):

- `call.*`: one value that has two names (`SortedAliases.C`, 2, declared after `B`) through each
  call that turns an enum into text: `ToString()` on the value, a boxed value and an `Enum`
  reference, `ToString` with "G", "F", "D" and "X", concatenation, interpolation,
  `string.Format`, `StringBuilder.Append/AppendFormat`, `Convert.ToString`, `Enum.GetName`,
  `Type.GetEnumName`, `Enum.Format`, `Debug.Log`, a generic parameter's `ToString()`, a nullable
  enum, `string.Join` of an array and a list, a format provider whose custom formatter leaves
  most arguments to the class library, `IFormattable.ToString` and Enum's `ToString` overloads
  through type parameters; `call.direct.others` prints aliased values of the other enums and of
  `CameraClearFlags`, `KeyCode` and `CollisionFlags` by a direct `ToString()`.
- `enum.<Enum>.*`: for enums whose aliases are declared in and out of value order, with 3 to 24
  constants, [Flags] or not, signed, byte, short and ulong: the order of `Enum.GetNames` and
  `Enum.GetValues`, then every value (boxed `ToString()`, "F" and `Enum.GetName`) and some
  values without a name.
- `unity.<Enum>.<value>`: every value with more than one name among the public enums in
  `UnityEngine` namespaces of the loaded `UnityEngine*` assemblies (boxed `ToString()`,
  `Enum.GetName`, its names).

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA GPU display; the
values depend on neither the OS nor the graphics device). Unity's Mono sorts an enum's constants
by value (as unsigned 64-bit numbers) unless they are declared in order, and names a value by a
binary search of the sorted constants, so `CameraClearFlags.SolidColor` (2, declared after
`Color`) prints "SolidColor", `KeyCode.LeftApple` "LeftMeta" and `SortedAliases.C` "C", where .NET
prints the first of them ("Color", "B"). [Flags] enums and the "F" format take names from the
highest sorted constant down (`ComputeBufferType.IndirectArguments` prints "IndirectArguments"
while `Enum.GetName` gives "DrawIndirect"). The order of `Enum.GetNames` is the same in both
runtimes (`Unbound.MonoEnumNames`). The Scripting API documents none of these choices
([Enum](https://learn.microsoft.com/dotnet/api/system.enum.getname) leaves the name of a value
with several names unspecified); they are observed behaviour of this release.

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/enumtext/run.py record --label A
    python3 tests/projects/enumtext/run.py record --label B
    python3 tests/projects/enumtext/run.py publish A B
    python3 tests/projects/enumtext/run.py gate
