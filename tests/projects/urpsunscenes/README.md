# The sky's sun under URP, across scenes

Unity 6000.0.71f1, Universal Render Pipeline 17 (the Editor's built-in package, the pipeline asset
of tests/projects/urp), gamma colour space, batchmode Editor on the GPU (Linux, OpenGL Core, NVIDIA
driver). `Assets/Scripts/UrpSunScenesProbe.cs` records, in each state, the active scene,
`RenderSettings.sun`, the skybox material, the ambient probe's constant term, three pixels of the
default procedural sky (below the horizon, above it, high), the `_LightColor0` and
`_WorldSpaceLightPos0` a skybox is drawn with (`Hidden/SunProbeSky` as the skybox, from
tests/projects/rendersettingssun) and the `_MainLightColor` and `_MainLightPosition` URP gives an
opaque pass (`Hidden/UrpSunMainLight`), read back from float render textures.

States: the base scene without lights; a scene made with `SceneManager.CreateScene` holding a
directional light, beside the base scene, made active, given the light as the named sun, left and
made active again, then unloaded; a dim light of the base scene beside a brighter light of another
added scene, active then unloaded, then the dim light destroyed; a light kept with
`DontDestroyOnLoad`; a dim named sun beside a brighter light, then the named sun disabled; last an
added scene given its own sky material, left, made active again and unloaded.

What the Editor does (two identical captures):

* The skybox is drawn with `RenderSettings.sun` as under the built-in pipeline: its colour and the
  direction its -z points to, a named sun even when disabled; without one (no directional light
  left, as after the scenes holding the lights unload) the direction (0, 0, -1) and black. The
  default procedural sky is then markedly brighter above the horizon than with a zero direction
  (0.66 against 0.51 in the `sky.mid` pixel).
* URP's main light is the named sun while it is enabled, else the brightest directional light;
  without one (0, 0, 1) and black. Lights of every loaded scene count, whichever is active.
* A scene made with `CreateScene` has its own lighting settings: no skybox (the sky is black), and
  a sky material set while it is active stays with it.

Documentation: [RenderSettings.sun](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-sun.html)
says only "The light used by the procedural skybox. If none is assigned, the brightest directional
light is used." The rest is observed behaviour of this release; other releases are unmeasured.

`residuals.json` lists the known differences: the lighting settings of a scene made with
`CreateScene` (Unbound keeps those in effect), and sky pixels within 3e-3 of the Editor's. The gate
does not enforce that bound on the listed sky pixels. The light the sky is drawn with
(`skyLightColor`, `skyLightPos`) is compared exactly in every state but those of the scene made
last with `CreateScene` (`thirdActive`, `thirdBrightSky`, `thirdActiveAgain`), where the
Editor's `RenderSettings.sun` is that scene's own (none: the brightest light) and Unbound's the
base scene's named one.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/urpsunscenes/run.py record --label A
python3 tests/projects/urpsunscenes/run.py record --label B
python3 tests/projects/urpsunscenes/run.py publish A B
python3 tests/projects/urpsunscenes/run.py gate
```
