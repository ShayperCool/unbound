# Light probe proxy volumes

Unity 6000.0.71f1, built-in render pipeline, the batchmode Linux Editor on OpenGL Core, NVIDIA
(RTX 4060) only. The project is tests/projects/lightprobes' baked Main scene (a mixed directional
and a mixed point light, 18 probes, Shadowmask, so the probes carry occlusion), its Editor runner
and settings (`run.py`'s `BASE`), with `Assets/Scripts/ProxyVolumeProbe.cs` and
`Assets/Resources/ProxyVolumeDump.shader` laid over them.

```bash
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/lppv/run.py record --label L1
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/lppv/run.py record --label L2
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/lppv/run.py publish L1 L2
python3 tests/projects/lppv/run.py gate
```

The script makes boxes with volumes in Start and measures three frames later (a volume made in
a frame is built later in it: before that its renderers draw with `unity_ProbeVolumeParams.x`
0 and no texture). For each volume a quad on its own layer, whose renderer uses the volume
through `lightProbeProxyVolumeOverride`, draws `Hidden/ProxyVolumeDump` into a 128x1026 float
target, read back as:

* `<case>.settings`, `.bounds`: the API's answers (modes, data format, `gridResolution*`, density;
  `boundsGlobal`);
* `.params`, `.worldToObject`, `.sizeInv`, `.min`: `unity_ProbeVolumeParams`, `...WorldToObject`,
  `...SizeInv`, `...Min`; `.centre`: the texture sampled at the box's centre;
* `.sh`: the draw's `unity_SHAr` .. `unity_SHC`;
* `.cells` (probes per axis) and `.texels`: the texture's rows (`[z, y, texel...]`), every row of
  a volume up to 128 probes, else the first and last.

Cases: the defaults over a box whose renderer does not use the volume (`default`); an image box
whose renderer does (`lit`, 8x4x2, cell centres); world bounds with half floats (`world`) and
the same rotated, scaled box in local bounds (`local`); custom bounds with Low quality
(`custom`); custom bounds at density 1 for sizes 0.3 to 40 at cell centres (`res*`, `ties`) and
corners (`corner*`); density 2 and 0.5 with both qualities and position modes (`d*`); automatic
bounds of a scaled box (`scaledworld`, `scaledlocal`), of a mesh whose renderer does not use the
volume (`oddworld`, `oddlocal`) and of a mesh and its child that do (`ownlocal`, `ownworld`); a
custom box on a rotated, scaled object (`rotatedcustom`). `volume.png` and `blend.png` are the
lit box with the lights off, through its volume and with Blend Probes.

What the Editor does (also the class summary of `LightProbeProxyVolume`):

* Texels are the probes `LightProbes.CalculateInterpolatedLightAndOcclusionProbes` gives at the
  cells' centres, 4 * X wide: red, green and blue in `unity_SHAr`'s layout (L1 x, y, z; L0 minus
  a third of L2's zonal term), then the occlusion; 16-bit floats for Half Float.
* The box: automatic modes enclose the renderers of the object and its children whose Light
  Probes use the volume, in the object's space (local) or the world's; with none a unit box.
  Custom: its size around its origin in the object's space. Cell Corner grows it by one cell.
* The resolution (Automatic): per axis the box's size in the world times the density (densities
  above 1 count as 1), rounded, at least 1, to the nearest power of two, a tie upwards, at most 32.
  `gridResolution*` answer the custom values whatever the mode (2 by default).
* A renderer drawn through a volume keeps the ambient probe's constants (Normal), none (Low).

Unmeasured: refreshing (Every Frame, Via Scripting, an Automatic volume whose inputs change), the
Vulkan device, `Graphics.DrawMesh*`'s volume argument and a renderer using a volume that is not
built (lit as Blend Probes lights it), a graphics tier without proxy volumes, a render pipeline
that asks for them. Particle system renderers do not draw through volumes in Unbound yet. Values
compare within 2e-4; images report their mean and worst difference.
