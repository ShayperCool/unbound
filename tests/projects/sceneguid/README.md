# The GUID a scene reports

`Assets/Scripts/SceneGuidProbe.cs` writes `Scene.guid` (internal; what URP's probe volume system
finds a scene's baking set by, through reflection as core does) for the first scene, a scene loaded
additively by build index, a scene made with `SceneManager.CreateScene`, the DontDestroyOnLoad scene
and an invalid scene, and the first scene's path.

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA driver): a scene
loaded from an asset reports that asset's GUID; the others report 32 zeros.

    python3 tests/projects/sceneguid/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/sceneguid/run.py publish A B        # two identical captures
    python3 tests/projects/sceneguid/run.py gate               # the engine vs Reference/
