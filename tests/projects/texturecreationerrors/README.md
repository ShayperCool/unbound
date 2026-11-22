# Block-compressed texture size errors

Unity 6000.0.71f1 records 209 values each in Gamma and Linear projects. Two independent
Editor captures agree in each color space. The fixture borrows the empty scene and settings
from `formatmapping`.

A size with either side outside the required block alignment fails with the compressed format's
name in the Application log. The registered `Debug.unityLogger.logHandler` receives no message.
The constructor throws. The fixture checks BC, ETC and signed and unsigned EAC formats,
including crunched inputs, both constructor format types, each axis independently, and the
separate error for a side of zero. DXT3 and signed EAC retain their identity in validation.
A signed BC4 request exercises the earlier device sampling check.
Aligned sizes, an odd RGBA32 size, and odd ASTC sizes succeed. The diagnostic names the effective
color space, including the ETC2 sRGB format for ETC1 in a Linear project. The Scripting API
describes dimensions and format selection. The diagnostic and its delivery are measured here.
[Unity 6000.0 Texture2D constructor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D-ctor.html)

The aligned controls check construction outcome and dimensions. DXT3, signed EAC and
ETC2 punchthrough use uncompressed backing storage in Unbound, which reports this limitation
to its console. PVRTC size validation, other release families and Reinitialize's separate
size, readability and Crunch validation remain open.
[Unity 6000.0 Reinitialize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Reinitialize.html)

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/texturecreationerrors/run.py record --space Gamma --label first
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/texturecreationerrors/run.py record --space Gamma --label second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/texturecreationerrors/run.py publish first second --space Gamma
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/texturecreationerrors/run.py gate
```

Repeat the recording commands with `Linear` for the Linear project. Comparisons are exact and
retain the result and both log channels for every case. `appfacts` compares the original
combined diagnostic row independently.
