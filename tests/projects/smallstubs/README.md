# smallstubs

API members of smaller systems that Unbound had as stubs (Q820), measured in the Unity
6000.0.71f1 Editor (`Assets/Scripts/SmallStubsProbe.cs`; two byte-identical Linux batch Editor
captures in `Reference/unity-6000.0.71f1/`):

* `raw.*`: `Texture2D.LoadRawTextureData<T>(NativeArray<T>)`: the pixels of a 2x2 RGBA32 texture
  after loading `Color32` and `byte` arrays (an array longer than the texture loads its first
  bytes), and its errors: too little data, an empty or uncreated array (it throws, unlike the
  `byte[]` overload, which logs), an unreadable texture (its error first).
* `shader.*`: `ShaderUtil.HasProceduralInstancing` for built-in shaders and the probe's
  `Assets/Shaders` (one with `#pragma instancing_options procedural:setup`, one with plain
  instancing), and a null shader; `ShaderUtil.IsPassCompiled` before and after
  `CompilePass` (synchronous and not), for another material of the same shader, another pass, a
  pass the SubShader does not have (its error) and a null material.

Not measured here: `QualitySettings.GetActiveQualityLevelsForPlatform[Count]` (the answers did
not follow the levels' excluded platforms in a first capture: rule not found) and
`ShaderUtil.allowAsyncCompilation` (batch mode may decide it).

```sh
python3 tests/projects/smallstubs/run.py record --label a
python3 tests/projects/smallstubs/run.py record --label b
python3 tests/projects/smallstubs/run.py publish a b
python3 tests/projects/smallstubs/run.py gate
```
