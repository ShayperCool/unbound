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
desktop OpenGL device through EGL (skipped without one; it must then be a 4.5 context).

| File / function | Change | Why |
| --- | --- | --- |
| `wgpu-hal/src/gles/egl.rs` `Inner::create` | A 4.5 core context is requested first, then the 3.3 one as before. | The OpenGL Core Editor's device is a 4.5 context (measured: tests/projects/urp CapsProbe `graphicsDeviceVersion` "OpenGL 4.5.0 NVIDIA"); the measured driver gives a 3.3 request exactly 3.3, without compute shaders, which the renderer needs (compute shaders, indirect validation). |
| `wgpu-hal/src/gles/egl.rs` `Instance::init` | Without a display handle, the X11 platform display of `$DISPLAY` (EGL opens it) where `EGL_EXT_platform_x11` is offered; else surfaceless, as before. | The device is made before its window (Bevy passes no display handle, and the device must exist before the game boots, when Unity loads preloaded plugins, tests/projects/nativegfx `plugin.load`); X11 windows of any connection to the server are presented to (measured: the surfaceless display has no window configs). |
| `wgpu-hal/src/gles/egl.rs` `choose_config` | On Linux a config that makes window surfaces is presentable, as on Android and Windows (before: only natively renderable ones). | Measured: the NVIDIA driver's X11 configs are not natively renderable, yet present to X11 windows (tests/projects/rtdepth `target.screen.*`). |
| `wgpu-hal/src/gles/adapter.rs` `Adapter::expose` | Loads `glTextureView` (OpenGL 4.3 or `GL_ARB_texture_view`, immutable storage) and reports `VIEW_FORMATS` only then; reads `GL_MAX_TEXTURE_LOD_BIAS` on desktop OpenGL. | Unity samples and renders textures through sRGB and linear views of one storage (sRGB render textures, `RenderTextureReadWrite`), samples single layers and faces, and biases samplers (`Texture.mipMapBias`). |
| `wgpu-hal/src/gles/device.rs` `create_texture_view`, `destroy_texture_view`; `mod.rs` `TextureView` | A view of another format, dimension, level range or layer range is its own OpenGL texture (`glTextureView`, levels and layers counted from the view's first; depth/stencil views keep the texture's format, their aspect still chosen when bound); the view owns and deletes it. Views equal to the whole texture stay the texture, as before. | Without it the backend ignored a view's format (sRGB views drew linear), could not sample a layer other than 0, and shared one base/max level between views of one texture. |
| `wgpu-hal/src/gles/device.rs` `create_texture` | A texture with view formats is never a renderbuffer. | A renderbuffer has no views. |
| `wgpu-hal/src/gles/mod.rs` `Texture::get_info_from_desc`, `log_failing_target_heuristics`, `conv.rs` `is_layered_target`, `queue.rs` `get_2d_target`, `set_attachment`, `device.rs` `create_texture` | A multisampled 2D texture is a `GL_TEXTURE_2D_MULTISAMPLE` texture (without filtering state), attached and bound as one. | `glTexStorage2DMultisample` refuses `GL_TEXTURE_2D` (measured: `GL_INVALID_ENUM`, then incomplete framebuffers), and the attachment path used the OpenGL ES render-to-texture extension. Unity's multisampled render textures with a sampled depth (camera MSAA, `bindTextureMS`) need it. |
| `wgpu-hal/src/gles/device.rs` `create_sampler`, `Device::sampler_lod_bias_limit`; `wgpu-core/src/device/resource.rs` `native_sampler_lod_bias_limit` | The native sampler LOD bias (above) on desktop OpenGL: `GL_TEXTURE_LOD_BIAS`, bounded by `GL_MAX_TEXTURE_LOD_BIAS`. | `Texture.mipMapBias` on the OpenGL Core device (tests/projects/samplerbias). |
| `wgpu-hal/src/gles/egl.rs` `AdapterContext::lock`, `AdapterContextLock::drop` | Another EGL context the thread had current before the lock is made current again after it (before: none). EGL reports the current context of the thread's bound client API, so the thread binds desktop OpenGL (`eglBindAPI`) before making its context current. | Native plugins call OpenGL from scripts with their context current on the game thread, which also drives the renderer (tests/projects/nativegfx). |
| `wgpu-hal/src/gles/queue.rs` `CopyTextureToBuffer` | A cube's face is read by its face target and a cube array's by its layer (before: an error was logged and nothing read); a volume's slices are read from the region's first slice, one after the other in the buffer (before: from the first slice up to the count, each at its own index). | `Cubemap.GetPixels`, `Camera.RenderToCubemap` into a Cubemap, reads of a cube render texture's faces (tests/projects/builtinfx `cubemap_*`, cmdbuffers `convert.*`, assetinstantiate `cube.*`) and `AsyncGPUReadback` of a Texture3D's slices (tests/projects/gpubuffers `rb.volume.*`). |
| `wgpu-hal/src/gles/queue.rs` `CopyTextureToTexture`; `adapter.rs` `Adapter::expose`, `mod.rs` `AdapterShared::copy_image` | Where the context copies images (`glCopyImageSubData`: OpenGL 4.3 or `GL_ARB_copy_image`), a copy between textures is one image copy over every layer or slice of its region, a cube's faces being its layers; else the framebuffer copy, as before. | The framebuffer copy attached a cube by the cube's own target (an OpenGL error, and the last attachment was copied instead) and copied one layer whatever the region's depth: `Graphics.CopyTexture` and `ConvertTexture` of cube faces (tests/projects/cmdbuffers `convert.toface`, `convert.cubetocube`). |
| `wgpu-hal/src/gles/adapter.rs` `Adapter::expose`, `texture_format_capabilities`; `mod.rs` `AdapterShared::desktop_image_formats` | On desktop OpenGL with image load and store (4.2 or `GL_ARB_shader_image_load_store`), every format of its image format table that the backend has is a storage format: the 8- and 16-bit one- and two-channel formats, `Rg32*`, `Rgb10a2*` and `Rg11b10Ufloat` too (before: OpenGL ES 3.1's formats only). | Kernels write RW render textures in their own format: without storage access the renderer keeps such a texture in a wider format, and a kernel then read channels and bits the format does not have (tests/projects/textureformats `rt.R16_SInt.kernel.load`, `rt.ARGB2101010.kernel.read`). |
| `wgpu-hal/src/gles/adapter.rs` `Adapter::renders_into`, `Adapter::expose`, `texture_format_capabilities`; `mod.rs` `AdapterShared::snorm8_renderable` | On desktop OpenGL the 8- and 16-bit signed normalized formats are drawn into where the driver makes a framebuffer of one complete (asked once, of `GL_RGBA8_SNORM` and `GL_RGBA16_SNORM`); before: never the 8-bit ones, the 16-bit ones by `GL_EXT_render_snorm`, which a desktop context does not list. | URP draws view-space normals into an `R8G8B8A8_SNorm` target on the OpenGL Core Editor; kept as unsigned 8 bits the normals lost their sign and SSAO differed (tests/projects/urp `ssao*`, `paths-opengl/ssao_*`). Measured: the NVIDIA driver's framebuffers of both formats are complete. |
| `wgpu-hal/src/gles/egl.rs` `Instance::enumerate_adapters` | Desktop OpenGL contexts enable `GL_TEXTURE_CUBE_MAP_SEAMLESS`. | Unity's devices filter a cube map across the edges of its faces, as OpenGL ES and the other backends always do; without it reflections sampled near a face's edge differed from the Editor's (tests/projects/assetbundles `thing`, builtinfx `probe_*`). |
| `wgpu/src/api/queue.rs` `Queue::submit`, `SUBMISSION`; `wgpu/src/api/surface.rs` `Surface::configure` | A submission and a surface configuration hold one process-wide lock, so they never overlap on different threads. A callback a configuration fires must not submit. | The game thread submits the renderer's commands while Bevy's render thread configures the window's surface (when it is made, resized or found outdated): Core's configuration waits for the GPU and fails when a submission arrives meanwhile ("Failed to wait for GPU to come idle", then a panic acquiring the unconfigured surface: tests/projects/display on the OpenGL device), and Vulkan's waits for the device to idle, which no submission may overlap. |
