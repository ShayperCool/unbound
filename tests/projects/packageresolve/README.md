# Package resolution without a lock file

Which package versions the Package Manager resolves for a project without
`Packages/packages-lock.json`. `Assets/Editor/PackageResolveProbe.cs` records, in Play mode, every
registered package but the engine modules: its version, source and whether it is a direct
dependency.

The project asks for `com.unity.collections` 2.1.4 (below the Editor's minimum, 2.6.2),
`com.unity.nuget.newtonsoft-json` 3.2.1 and `com.unity.ugui` 1.0.0 (a built-in package) directly;
its embedded package `com.probe.resolve` asks for `com.unity.burst` 1.8.4, `com.unity.mathematics`
1.2.1, `com.unity.timeline` 1.7.0, `com.unity.settings-manager` 1.0.3 (a major version below its
minimum) and `com.unity.nuget.newtonsoft-json` 3.0.2. Every version the Editor picks ships with the
installation (`Resources/PackageManager/Editor`), so neither the Editor nor Unbound needs the
registry.

Measured with the Unity 6000.0.71f1 macOS Editor in batch mode (two byte-identical captures; the
result depends on the installation's manifest, not on the OS): a package another package asks for
below the minimum version the installation's manifest (`Resources/PackageManager/Editor/manifest.json`)
names resolves to that minimum (Burst 1.8.28, Mathematics 1.3.2, Timeline 1.8.10, Settings
Manager 2.1.0, Collections' Mono Cecil 1.11.5); the project's own dependencies keep the version
they name (Collections 2.1.4); the built-in uGUI is the installation's 2.0.0; the embedded package
is a direct dependency. The Manual's resolution pages
([Package dependency and resolution](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-dependencies.html),
[Lock files](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-conflicts-auto.html)) state
the highest version requested wins and do not mention the minimum.

Not measured: an indirect request above the minimum that only the registry has, a direct
dependency below another package's request, `resolutionStrategy`, other releases (Unbound keeps the
highest version asked for there).

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/packageresolve/run.py record --label native-a
python3 tests/projects/packageresolve/run.py record --label native-b
python3 tests/projects/packageresolve/run.py publish native-a native-b
UNBOUND=/path/to/unbound python3 tests/projects/packageresolve/run.py gate
```

Values compare exactly. Unbound on macOS, 2026-11-18: 10/10.
