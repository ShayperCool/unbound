# Light colour probe

`Assets/Scripts/LightColorProbe.cs` records `Mathf.CorrelatedColorTemperatureToRGB` over a sweep
of temperatures (every 250 K from 1017.25 K to 39767.25 K, the common lamp temperatures, both sides
of 1000 K, 6570 K and 40000 K, negative values, the infinities and NaN), and the final colour
(`VisibleLight.finalColor`) of seven lights culled by a scriptable pipeline that draws nothing:
white, coloured, HDR and dark colours (channels on both sides of the sRGB curve's 0.04045 knee),
with and without a colour temperature, a zero intensity, a colour alpha below 1. Each light is
recorded under every combination of `GraphicsSettings.lightsUseLinearIntensity` and
`GraphicsSettings.lightsUseColorTemperature`. Floats are the hex of their bits; values compare
exactly. The project is in the linear colour space.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA driver):

* The colour temperature is a rational fit per channel in t = kelvin / 1000, clamped to 1..40
  (below 1000 K and for negative infinity, t = 1; above 40000 K and for positive infinity, t = 40);
  red is 1 below 6570 K and blue is 1 above it; NaN gives NaN channels and alpha 1.
* With linear intensity, a light's final colour is each linear colour channel times the
  temperature's tint (when the light uses a temperature) times the intensity; the alpha is the
  colour's alpha times the intensity.
* Without linear intensity, it is the colour times the intensity converted to linear (alpha times
  the intensity, unconverted); the colour temperature is not applied.
* `GraphicsSettings.lightsUseColorTemperature` does not change the final colour either way.
* A light of zero intensity is not visible.

Not measured: a gamma-space project.

    python3 tests/projects/lightcolor/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/lightcolor/run.py publish A B        # two identical captures
    python3 tests/projects/lightcolor/run.py gate               # the engine against Reference/
