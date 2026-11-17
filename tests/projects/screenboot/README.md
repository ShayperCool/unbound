# The screen size while Play mode starts

`Assets/Scripts/ScreenBootProbe.cs` records `Screen.width`, `Screen.height` and `Screen.safeArea`
at each `RuntimeInitializeOnLoadMethod` stage (SubsystemRegistration, AfterAssembliesLoaded,
BeforeSplashScreen, BeforeSceneLoad, AfterSceneLoad), in its component's `Awake` and `Start` and
in the first three `Update`s. The Editor enters Play mode with the Game view at a rendering
resolution of 960x540 (`ProbeRunner.Record`); the engine runs with a 960x540 window.

Matches the Unity 6000.0.71f1 Editor: the Game view exists before Play mode, so scripts see its
size (and a safe area covering it) from the first callback on, not a default that the first
frame replaces. Values compare exactly. `Screen.currentResolution` describes the host's display
and is not recorded here.

    python3 tests/projects/screenboot/run.py record --label A    # windowed Editor on the GPU
    python3 tests/projects/screenboot/run.py publish A B
    python3 tests/projects/screenboot/run.py gate                # the engine against Reference/
