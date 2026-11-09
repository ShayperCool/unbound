# Sampler backend patch ledger

Upstream packages are pinned to **30.0.1**, unchanged from the workspace lockfile.
The SHA-256 values below identify the published `.crate` archives and are checked by
`reproduce.py` before extraction. All original upstream licenses remain in each package.

| Package | Archive SHA-256 |
| --- | --- |
| wgpu | `527ccdf43dd5b2e8676eed9984ce00e2bbb0a1b85b70c1969dcb6cd2eb55ab9e` |
| wgpu-core | `14c018fce9b6270aa203c2fdd56f3cce996713534bd757e4ea58c8560b121f14` |
| wgpu-hal | `b6b7fb58561a792bc237628ba0792e332de418fefe145f13b5ed8201e6d52f58` |

`native-lod-bias.patch` and then `gl-device.patch` contain every source change relative to those
archives. `reproduce.py --output <fresh-cache-directory>` recreates the three patched source trees.
An optional `--archive-dir` supplies previously downloaded archives. The script does not
read the Cargo registry or depend on machine-specific paths. No dependency version changes
are required; Cargo's patch entries replace only these three packages.

## APIs and ownership

- `wgpu::Device::create_sampler_with_lod_bias(desc, bias)` returns an ordinary owned sampler.
  Ordinary zero bias delegates to existing creation. The native method additionally permits
  anisotropic nearest-mip filtering on Vulkan. Non-Core backends return `None`.
- `wgpu::Device::native_sampler_lod_bias_limit()` reports support and the physical limit.
- The Core backend adds `lod_bias` and explicit `native_mip_filter` to its internal sampler descriptor and validates finite
  values, supported backend, and the physical device's actual bias range. Trace descriptors
  preserve it and default old serialized descriptors to zero.
- Vulkan HAL adds the bias to `VkSamplerCreateInfo` through the existing creation path.
  Its existing cache already hashes/compares `mip_lod_bias`. Core's sampler owns its device
  through `Arc`; its existing `Drop` destroys the sampler through HAL and the existing cache.
  No foreign handle is imported, borrowed, cast, or destroyed separately.
- `hal::vulkan::Device::sampler_lod_bias_limit()` returns the cached physical limit, or `None`
  on portability-subset devices because this backend does not enable `samplerMipLodBias`.
  Other native backends than Vulkan and desktop OpenGL (`gl-device.patch`, below) reject nonzero
  bias or the native filter opt-in with `UnsupportedLodBias`. There is no shader approximation. The engine keeps its prior
  zero-bias filtering policy when this native extension is unavailable.

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

Bilinear Native controls demonstrate nearest-mip selection even with active anisotropy.
The native creation opt-in permits that Vulkan combination while ordinary WebGPU sampler
validation stays unchanged. It does not enable nearest magnification/minification with
anisotropy. The same measured anisotropic origin governs the nearest-mip decision. Vulkan's
[sampler valid-usage rules](https://docs.vulkan.org/refpages/latest/refpages/source/VkSamplerCreateInfo.html)
permit independent mip selection and anisotropy for normalized coordinates.

The renderer does not permanently cache every nonzero float bias. Cached bind groups own
those sampler references, and the existing HAL cache deduplicates live creation descriptors.
This allows obsolete bias states to reach the ordinary destruction path.

## OpenGL device (`gl-device.patch`)

Unity's OpenGL Core device draws with wgpu's OpenGL backend (EGL). Each change below is marked
`Unbound:` in the source. "Measured" is what a Unity 6000.0.71f1 Editor capture or a GL query on
the measured machine (NVIDIA, driver 580) showed; the rest is what the OpenGL specification
requires. `crates/unbound-app/src/render/gl_backend_tests.rs` checks each change on a real
OpenGL device (skipped without one).

| File / function | Change | Why |
| --- | --- | --- |
| `wgpu-hal/src/gles/egl.rs` `Inner::create` | A 4.5 core context is requested first, then the 3.3 one as before. | The OpenGL Core Editor's device is a 4.5 context (measured: tests/projects/urp CapsProbe `graphicsDeviceVersion` "OpenGL 4.5.0 NVIDIA"); the measured driver gives a 3.3 request exactly 3.3, without compute shaders, which the renderer needs (compute shaders, indirect validation). |
| `wgpu-hal/src/gles/adapter.rs` `Adapter::expose` | Loads `glTextureView` (OpenGL 4.3 or `GL_ARB_texture_view`, immutable storage) and reports `VIEW_FORMATS` only then; reads `GL_MAX_TEXTURE_LOD_BIAS` on desktop OpenGL. | Unity samples and renders textures through sRGB and linear views of one storage (sRGB render textures, `RenderTextureReadWrite`), samples single layers and faces, and biases samplers (`Texture.mipMapBias`). |
| `wgpu-hal/src/gles/device.rs` `create_texture_view`, `destroy_texture_view`; `mod.rs` `TextureView` | A view of another format, dimension, level range or layer range is its own OpenGL texture (`glTextureView`, levels and layers counted from the view's first; depth/stencil views keep the texture's format, their aspect still chosen when bound); the view owns and deletes it. Views equal to the whole texture stay the texture, as before. | Without it the backend ignored a view's format (sRGB views drew linear), could not sample a layer other than 0, and shared one base/max level between views of one texture. |
| `wgpu-hal/src/gles/device.rs` `create_texture` | A texture with view formats is never a renderbuffer. | A renderbuffer has no views. |
| `wgpu-hal/src/gles/mod.rs` `Texture::get_info_from_desc`, `conv.rs` `is_layered_target`, `queue.rs` `get_2d_target`, `set_attachment` | A multisampled 2D texture is a `GL_TEXTURE_2D_MULTISAMPLE` texture and is attached as one. | `glTexStorage2DMultisample` refuses `GL_TEXTURE_2D` (measured: `GL_INVALID_ENUM`, then incomplete framebuffers), and the attachment path used the OpenGL ES render-to-texture extension. Unity's multisampled render textures with a sampled depth (camera MSAA, `bindTextureMS`) need it. |
| `wgpu-hal/src/gles/device.rs` `create_sampler`, `Device::sampler_lod_bias_limit`; `wgpu-core/src/device/resource.rs` `native_sampler_lod_bias_limit` | The native sampler LOD bias (above) on desktop OpenGL: `GL_TEXTURE_LOD_BIAS`, bounded by `GL_MAX_TEXTURE_LOD_BIAS`. | `Texture.mipMapBias` on the OpenGL Core device (tests/projects/samplerbias). |
| `wgpu-hal/src/gles/egl.rs` `AdapterContext::lock`, `AdapterContextLock::drop` | Another EGL context the thread had current before the lock is made current again after it (before: none). | Native plugins call OpenGL from scripts with their context current on the game thread, which also drives the renderer (tests/projects/nativegfx). |
