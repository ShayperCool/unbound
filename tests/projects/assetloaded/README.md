# Whether an asset's main object is loaded

`Assets/Scripts/AssetLoadedProbe.cs` writes what `AssetDatabase.IsMainAssetAtPathLoaded` answers for
a ScriptableObject asset the scene references (`Assets/Data/Referenced.asset`, held by the scene's
`LoadedHolder`), one nothing references (`Unreferenced.asset`) and a missing path, after loading the
unreferenced one and after `Resources.UnloadAsset` of each, and what the references read after the
unload. URP's probe volume system unloads a baking set it loads only when the asset was not loaded
before (`ProbeVolumeBakingSet.SyncBakingSets`).

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA driver; another
capture crashed at startup twice while other Editors ran, and was discarded): a referenced asset is loaded,
an unreferenced one only after it is loaded, neither after it is unloaded, and the scene's reference
reads null after its asset is unloaded.

    python3 tests/projects/assetloaded/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/assetloaded/run.py publish A B        # two identical captures
    python3 tests/projects/assetloaded/run.py gate               # the engine vs Reference/
