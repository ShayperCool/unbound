# factoryinstances

What `ObjectFactory.CreateInstance` makes of the engine object types that have no public
parameterless constructor (scripts, components and ScriptableObjects aside), in Unity 6000.0.71f1
Play mode (`Assets/Scripts/FactoryInstancesProbe.cs`; two byte-identical macOS batch Editor captures;
every engine module in the project): the object's type, name and hide flags, or what it throws.

Measured: Texture2D, Texture3D, Texture2DArray, Cubemap, CubemapArray, CustomRenderTexture,
SparseTexture, SpeedTreeWindAsset, Preset and Material are made, unnamed, with no hide flags; every
other type listed throws ArgumentException ("<type> constructor is not accessible which prevent this
type from being used in ObjectFactory."), ProceduralMaterial its own ("'type' parameter is not a
valid UnityEngine.Object type : ProceduralMaterial").

Residuals (`residuals.json`): Preset, SparseTexture and SpeedTreeWindAsset, which Unbound cannot make
yet (NotSupportedException).

```sh
python3 tests/projects/factoryinstances/run.py record --label a
python3 tests/projects/factoryinstances/run.py record --label b
python3 tests/projects/factoryinstances/run.py publish a b
python3 tests/projects/factoryinstances/run.py gate
```
