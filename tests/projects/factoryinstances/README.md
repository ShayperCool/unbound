# factoryinstances

What `ObjectFactory.CreateInstance` makes of the engine object types the release declares no public
parameterless constructor for (components, GameObject and ScriptableObjects aside), in Unity
6000.0.71f1 Play mode (`Assets/Scripts/FactoryInstancesProbe.cs`; the list is the release's own,
enumerated in the Editor; two byte-identical macOS batch Editor captures; every engine module in the
project): the object's type, name and hide flags, or what it throws, and for a made texture or
material what it holds (`state.*`: size, depth, graphics format, mip count, readability, whether
Apply throws, a custom render texture's material, a material's shader).

Measured: a type the release declares no parameterless constructor for is made, unnamed, with no
hide flags (Texture2D, Texture3D, Texture2DArray, Cubemap, CubemapArray, CustomRenderTexture,
SparseTexture, SpeedTreeWindAsset, Preset, Material); one whose parameterless constructor is hidden
throws ArgumentException ("<type> constructor is not accessible which prevent this type from being
used in ObjectFactory."); AudioMixer and RuntimeAnimatorController are not supported by the factory;
MovieTexture, ProceduralMaterial and ProceduralTexture are not valid object types. The made textures
are empty (0x0), readable, and take Apply; Texture2D and Cubemap have graphics format 88; a custom
render texture is 256x256 R8G8B8A8_UNorm without a material; a material has the error shader.

Residuals (`residuals.json`): Preset, SparseTexture and SpeedTreeWindAsset, which Unbound cannot make
yet; the made textures' state, which is Unbound's own default.

```sh
python3 tests/projects/factoryinstances/run.py record --label a
python3 tests/projects/factoryinstances/run.py record --label b
python3 tests/projects/factoryinstances/run.py publish a b
python3 tests/projects/factoryinstances/run.py gate
```
