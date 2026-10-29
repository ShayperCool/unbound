// Native calls pass only blittable values (pointers, numbers, plain structs), so the runtime
// generates no marshalling for them: every P/Invoke is a [LibraryImport] whose stub the compiler
// emits, and calls through function pointers (the native API table) are direct.
[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
