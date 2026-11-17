# RenderSettings.sun, the forward main light and the sky's sun

Unity 6000.0.71f1, built-in render pipeline, linear colour space, batchmode Editor on the GPU
(Linux, OpenGL Core, NVIDIA driver). `Assets/Scripts/RenderSettingsSunProbe.cs` records, for each
case, which light `RenderSettings.sun` returns (before and after drawing), and the
`_LightColor0`/`_WorldSpaceLightPos0` that a ForwardBase pass (`Hidden/SunProbeForward`, a quad on
its own layer) and a skybox (`Hidden/SunProbeSky`) are given, read back from a 2x1 float target.
The light a pass was given is named by its direction (every light of a case has its own).

The scene has two directional lights and no named sun (`m_Sun: {fileID: 0}`): `AuthoredBright`
(intensity 1) and `AuthoredShadowed` (intensity 0.1, soft shadows). The other cases create their
lights at run time, record, and destroy them; the scene's two are deactivated first.

Documentation: [RenderSettings.sun](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-sun.html)
says only "The light used by the procedural skybox. If none is assigned, the brightest
directional light is used." It defines neither "brightest" nor what the forward pass uses.

## What the Editor does (two identical captures)

* Without a named sun the getter returns the brightest enabled directional light that is not
  Not Important (`ForceVertex`), ranked by 0.3 r + 0.59 g + 0.11 b of its colour (beyond 1
  too) times its intensity, sixteen times when it casts shadows (hard or soft). An Important
  (`ForcePixel`) light gets no bonus. Culled (culling mask 0), cookie and zero-intensity lights
  count, and so does a light set to Baked without a bake (it stays realtime). Of equal lights
  the first enabled wins; disabling and enabling one, or deactivating and activating its object,
  puts it behind.
* A named light is returned whatever its state: disabled, inactive, a point or a spot light. A
  destroyed one, or `sun = null`, leaves the brightest.
* The getter follows intensity, shadow and type changes at once.
* The skybox is drawn with the getter's light, whichever it is: its colour (as the light's final
  colour) and the direction its transform's -z points to; without one, (0, 0, -1) and black.
* The ForwardBase pass's main light is ranked the same way but ignores the name: a named dim
  light leaves the brighter one the main light. Disabled, inactive, Not Important,
  zero-intensity, empty-culling-mask and cookie lights are not it. Between equal lights the
  Editor's choice varies between runs, so the tie cases record no forward readback.

Not measured here (tests/projects/urpsunscenes measures URP, lights of other scenes than the
active one and an unloaded sun's scene): area lights, a
fully baked light (one a bake marks baked) beside realtime ones, a Not Important directional
light with no other directional light, and the colour temperature (this project's lights do not
apply it: the `color temperature` case cannot tell the colour as set from the final colour).

```sh
python3 tests/projects/rendersettingssun/run.py record --label <L>   # Editor capture
python3 tests/projects/rendersettingssun/run.py publish <L1> <L2>    # two identical captures
python3 tests/projects/rendersettingssun/run.py gate                 # engine vs Reference
```

The gate compares every value; numbers within 1e-5.
