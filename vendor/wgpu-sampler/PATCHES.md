# Sampler backend patch ledger

Upstream packages are pinned to **30.0.1**, unchanged from the workspace lockfile.
The SHA-256 values below identify the published `.crate` archives and are checked by
`reproduce.py` before extraction. All original upstream licenses remain in each package.

| Package | Archive SHA-256 |
| --- | --- |
| wgpu | `527ccdf43dd5b2e8676eed9984ce00e2bbb0a1b85b70c1969dcb6cd2eb55ab9e` |
| wgpu-core | `14c018fce9b6270aa203c2fdd56f3cce996713534bd757e4ea58c8560b121f14` |
| wgpu-hal | `b6b7fb58561a792bc237628ba0792e332de418fefe145f13b5ed8201e6d52f58` |

`native-lod-bias.patch` contains every source change relative to those archives.
`reproduce.py --output <fresh-cache-directory>` recreates the three patched source trees.
An optional `--archive-dir` supplies previously downloaded archives. The script does not
read the Cargo registry or depend on machine-specific paths. No dependency version changes
are required; Cargo's patch entries replace only these three packages.

## APIs and ownership

- `wgpu::Device::create_sampler_with_lod_bias(desc, bias)` returns an ordinary owned sampler.
  Zero bias delegates to the existing creation method. Non-Core backends return `None`.
- The Core backend adds `lod_bias` to its internal sampler descriptor and validates finite
  values, supported backend, and the physical device's actual bias range. Trace descriptors
  preserve it and default old serialized descriptors to zero.
- Vulkan HAL adds the bias to `VkSamplerCreateInfo` through the existing creation path.
  Its existing cache already hashes/compares `mip_lod_bias`. Core's sampler owns its device
  through `Arc`; its existing `Drop` destroys the sampler through HAL and the existing cache.
  No foreign handle is imported, borrowed, cast, or destroyed separately.
- `hal::vulkan::Device::sampler_lod_bias_limit()` returns the cached physical limit, or `None`
  on portability-subset devices because this backend does not enable `samplerMipLodBias`.
  Other native backends reject nonzero bias with `UnsupportedLodBias`. There is no silent
  shader approximation. The ordinary zero-bias path remains available everywhere.

NativeApi12, render-command protocol5, texture packet layouts and PhysX shim9 are unchanged.
These are Rust dependency APIs, not additions to the managed/native packet contract.

## Measured cause and controls

Shader gradient scaling changed accepted values even when the old tolerance still passed.
The replacement applies bias in the sampler's LOD operation and preserves all implicit and
explicit gradients. Explicit-level, explicit-gradient, implicit-bias and implicit sampling
are checked separately through both legacy and object-style bindings. Integer texture loads
remain independent of samplers. See the
[Vulkan sampling operations](https://docs.vulkan.org/spec/latest/chapters/textures.html#textures-lod-and-scale-factor)
and [sampler bias rules](https://docs.vulkan.org/spec/latest/chapters/samplers.html).

`tests/projects/samplerbias` publishes independent byte-agreeing Unity6000.0.71f1 Native
references for Force/Enable/Disable. Main controls include initial/live properties, point and
trilinear filters, strengths1..16, and higher mip transitions. Bilinear and reused-handle live
policy controls are separate profiles. Full scalar unions keep every value and residual;
GPU tolerance0.002/state1e-6 are unchanged. A passing float tolerance is not exact sampling.

The measured NVIDIA Vulkan anisotropic transition has the same slopes as Native but a
different origin. Only measured 2D samplers on that backend receive centering selected by
the effective sampler strength/filter policy. Other backends and release profiles have not
been certified. Shader cache14 uses a distinct native-sampler implementation key; rejected
shader-emulation entries cannot be reused. Final validation status and rejected experiments
belong in cache handoff reports, never in this source ledger.
