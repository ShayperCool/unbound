# Native plugin bundles on macOS

A macOS plugin may be a bundle directory, `Foo.bundle/Contents/MacOS/<executable>`, whose
executable is named by `CFBundleExecutable` in `Contents/Info.plist` and need not be called like
the bundle. `run.py` builds two from `Plugins/pluginbundle.c` (clang, arm64) into each fresh copy's
`Assets/Plugins`, both Editor plugins for macOS on any CPU: `Renamed.bundle`, whose executable is
`RenamedLib` (its function answers 42), and `Same.bundle`, whose executable is `Same` (7).
`Assets/Scripts/PluginBundleProbe.cs` calls the function through `[DllImport]` by the bundle's name
(`renamed.byBundle`, `same.byBundle`) and by the executable's (`renamed.byExecutable`) and records
the answer or the exception's type. Then `Same`'s native `pluginbundle_call` calls a managed
`Action<string, int, string>` back through the pointer `Marshal.GetFunctionPointerForDelegate`
makes of it, a delegate of a generic type that the Editor's Mono marshals (`callback.generic`:
the arguments the action received, or the exception's type). Values compare exactly.

Plugin loading is decided by the OS, so the reference is the macOS Editor's
(`Reference/unity-6000.0.71f1/macos.jsonl`, two identical batchmode captures on Apple silicon);
on another OS `check` does not apply and passes.

    python3 tests/projects/pluginbundle/run.py native --label L     # two macOS Editor captures
    python3 tests/projects/pluginbundle/run.py publish L-1 L-2
    python3 tests/projects/pluginbundle/run.py check                # the engine against the reference
