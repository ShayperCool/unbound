# Native rendering plugin probe

Unity 6000.0.71f1, built-in pipeline, Linear color space, OpenGL Core (the Linux Editor's device).
The probe's own plugin (`Plugins/nativegfx_probe.c`, built by `run.py` into the project copy)
uses the low-level native plugin interface and records what it gets; the scripts
(`Assets/Scripts/NativeGfxProbe.cs`) drive it and read back what it drew.

    python3 tests/projects/nativegfx/run.py check      # engine run compared with Reference/

Recorded:

* **Load** (`plugin.load`, `interface.*`, `load.logs`, `event.base`): `UnityPluginLoad` runs on the
  main thread inside the script's first call into the plugin, with a GL context current; the
  interfaces found by GUID through `GetInterface` and `GetInterfaceSplit`; an interface the plugin
  registers itself (registering null afterwards keeps it); `GetRenderer` (17, OpenGL Core);
  `ReserveEventIDRange` (the first range starts at 12); `IUnityLog` messages as script log
  messages of type Log, Warning and Error.
* **Callbacks** (`plugin.sequence`): every callback runs on the main thread, synchronously inside
  the call that executes it (`Camera.Render`, `Graphics.ExecuteCommandBuffer`,
  `GL.IssuePluginEvent`), with a GL context current. A camera command buffer's event at
  `AfterForwardOpaque` and `GL.IssuePluginEvent` from `OnPostRender` find the camera's target
  bound (its color texture, its depth texture, the viewport); a custom blit finds its
  destination bound. Elsewhere the plugin draws nothing (another framebuffer is bound).
  `IssuePluginEventAndData` passes its data pointer.
* **Texture updates** (`update*.pixels`, `update.cpu`): `IssuePluginCustomTextureUpdate` (the
  V1 events 7/8, like `V1`) and `V2` (events 9/10) with the format (8, R8G8B8A8 UNorm), size,
  bytes per pixel and user data; Unity uploads the plugin's data between the two events; the
  texture ID is not the GL name; the CPU copy is unchanged.
* **Custom blits** (`blit.pixels`, `execute.blit.texture.logs`): command, parameter, flags and
  the destination render buffer (`colorBuffer.GetNativeRenderBufferPtr()`); a `Texture2D` source
  logs an error and passes source 0 without binding the destination.
* **Native pointers** (`ptr.*`, the `name ...` lines): `GetNativeTexturePtr` is the GL texture
  name (internal format GL_RGBA8, GL_SRGB8_ALPHA8 for sRGB textures) and is 0 for a render
  texture that is not created (it is not created by the call); `GetNativeDepthBufferPtr` is a
  GL_DEPTH24_STENCIL8 texture (the color texture's name for a render texture without depth); render
  buffer pointers are neither of these.
* **Multisampled native images** (device.msaa, device.depthOnly):
  1, 2 and 4 samples, with 0, 16 or 24 depth bits, and both values of bindTextureMS.
  The depth-only controls check that color and depth pointer calls name the same resource.
  Explicit D32 and D32-with-stencil formats distinguish precision and stencil presence.
  Creating a texture with one sample and bindTextureMS fails without a log. Default
  pointers name single-sample images; bound pointers name the samples themselves.
  OpenGL without depth returns its multisampled color for the depth pointer; Vulkan returns
  its resolved color. On OpenGL, depth clears publish current depth, including outside
  a partial clear; drawing retains that image until an explicit self-resolve or shader
  depth read. A resolve into another target leaves source depth unchanged.
  The device.sampleResolve and device.depthRead controls write different depths per sample
  at 2, 4 and 8 samples, distinguishing selected depth, retained state and publication.
  Content controls are OpenGL measurements; Vulkan records pointer identity and image
  metadata. Both devices were captured twice on NVIDIA only, Unity 6000.0.71f1.
  The [bindTextureMS contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture-bindTextureMS.html)
  describes disabled automatic resolves, and
  [ResolveAntiAliasedSurface](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.ResolveAntiAliasedSurface.html)
  describes explicit self and destination resolves. Separate retained depth publication
  is observed behavior in these controls.
* **Managed multisample loads and color resolves** (`device.colorResolve.*`,
  `device.resolveDestination.*`, `device.resolveSourceSingle.*`): 2, 4 and 8 samples,
  color and depth loads, sample-count queries, automatic averaged color, explicit self and
  active-source destination resolves. `bindTextureMS` shaders read individual samples.
  Suppression of retained single-sample color publication for `bindTextureMS` has no
  divergent supported-access control here and remains queued; the direct sample interface
  is covered. Resolving into a multisampled destination leaves its previous content; a single-sample
  source can resolve into a single-sample destination too. Vulkan compute color and depth
  loads match. On OpenGL Core, the six multisample compute rows retain preceding graphics
  bindings instead of the requested resources; these remain strict declared residuals.
  Isolated kernels after alternating color/depth graphics reads distinguish that history
  from color quantization and the requested subelement. Array multisampling and other
  drivers remain outside this capture. All original reference rows are unchanged.
  [ComputeShader.SetTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ComputeShader.SetTexture.html)
  selects an element per kernel and ignores the mip parameter for sampled textures.
  Multisampled depth selection is implemented on the measured profile; ordinary compute
  depth and stencil element selection remain reported as unsupported.
  The Vulkan `device.kernelElements.*` controls give one property opposing element bindings
  in two kernels and replace each binding independently before dispatch.
* **Retained stencil** (`--stencil`, OpenGL only): the plugin initializes the native depth
  image's stencil to 113 before a clear, stencil writes of 37 and 91, and self-resolves.
  A single-sample image follows the writes; a multisample target's retained stencil keeps
  113 while its depth is published. Depth-only publication preserves the destination's
  stencil. The separate canonical profile includes every primary row and the raw callback
  error logs. Run it with `python3 tests/projects/nativegfx/run.py check --stencil`.
  Vulkan stencil publication and other drivers remain unmeasured.
* **External textures** (`external.*`): `Texture2D.CreateExternalTexture` and
  `UpdateExternalTexture` with textures the plugin made, and with another texture's pointer
  (the same texture), also a render texture with depth and mips, which works on after the
  external texture is destroyed.
* **Texture shapes** (`shape.*`, `Assets/Scripts/NativeGfxProbe.Textures.cs`; the plugin's
  texture callback keeps a record of its own, so the records above stay as they are): the
  native pointer of a Cubemap is a cube map (linear `GL_RGBA8` for `TextureFormat.RGBA32`:
  `shape.formats`), of a Texture2DArray a 2D array (one layer and six square layers too), of a Texture3D a 3D
  texture, of a CubemapArray a cube map array, with their levels; render textures of those
  dimensions the same, an array's depth pointer a `GL_DEPTH24_STENCIL8` array, a cube's its
  color texture. What the plugin writes through the names is read back from the textures
  (`shape.*.written`). A multisampled render texture's pointer is its resolved 2D texture, its
  depth pointer a single-sample depth texture (without a depth buffer: the multisampled color
  texture); with `bindTextureMS` both are the multisampled textures. A callback while a
  multisampled target is bound finds a multisampled color texture and a depth render buffer
  attached (`shape.msaa.target*`), and what it draws shows after the resolve
  (`shape.msaa.pixels`); an sRGB multisampled render texture's samples are `GL_SRGB8_ALPHA8`
  (its depth pointer and what a callback finds attached: `shape.rt.msaa.srgb.depth`,
  `shape.msaa.srgb.target*`), so half grey written with `GL_FRAMEBUFFER_SRGB` enabled reads back
  as half grey (`shape.msaa.srgb.pixels`). A callback while one face of a cube render texture or one layer of an
  array render texture is the target finds that image of the color texture attached (with the
  array's depth layer; the cube's depth is a render buffer) and draws into it alone
  (`shape.cube.target*`, `shape.array.target*`). After a buffer that drew into a multisampled target, with a callback
  or without, the next callback (one that does nothing) is blamed for a `GL_INVALID_OPERATION`
  the Editor left itself (`shape.msaa.after.logs`, `shape.msaa.srgb.after.logs`,
  `shape.msaa.plain.logs`).
* **External textures of other formats** (`ext.*`): a plugin's 2D texture is sampled as OpenGL
  samples it, whatever format and color space the external texture declares (they are its
  `format` and `graphicsFormat`): `GL_SRGB8_ALPHA8` texels are decoded declared linear or not,
  `GL_RGBA8` texels declared sRGB are not; `GL_R8`, `GL_RG8`, `GL_RGB8`, `GL_RGBA16F`,
  `GL_RGBA32F` and DXT5 show as they are, one channel declared as four too, and so does a
  texture made with the unsized `GL_RGBA` (`ext.unsized.*`: it reports that format). With a mip chain
  the external texture has the levels of its size and shows the plugin's smaller levels
  (drawn at half, quarter and eighth size); without one it shows the first level alone,
  though the plugin's texture has four.
* **Order** (`order.pixels`, `order.active`): a callback between two draws of an executed buffer
  draws over the first and under the second; after the buffer, the active render texture is the
  one before it.
* **Errors** (`execute.glerror.logs`): a GL error a callback leaves is logged as an assertion.
* **Shutdown** (`plugin.exit`, `plugin.preloaded`, `plugin.prefixed`): the device's Shutdown event
  twice at exit, no `UnityPluginUnload`; a plugin whose importer has "Load on startup"
  (`isPreloaded`) is loaded before Play mode without an Initialize event; the `GfxPlugin` name
  prefix alone does not preload in the Editor.

* **Profiler callbacks** (`profiler.categories`, `profiler.load`, `profiler.finish`,
  `frames.sequence`, `profiler.editor`, `profiler.script`): `IUnityProfilerCallbacks` and `V2` are
  objects of their own. Registering a category callback calls it at once for the 39 built-in
  categories (ID, color, name), a thread callback for the threads that exist (the main thread
  first, without a group), a marker callback for the markers that exist; a category or marker the
  plugin makes is heard of once (the first category ID is 39; the same name is the same category
  or marker, a built-in category's name that category); a callback registered twice is called
  twice. Four category callbacks and four marker callbacks can be registered, a fifth is refused
  (-1); thread, frame and flow callbacks beyond four are taken. A thread's ID is the thread's own
  handle (`self=1`). A marker event callback makes the
  marker's `callback` non-null and `EmitEvent` calls it at once, with no profiler recording; it is
  unregistered for a marker, without a marker for every marker it was registered for with the user
  data, and without user data either for all. A thread that registers is heard of on that thread.
  The frame callback is called once a frame, after the frame's scripts and before the frame count
  advances. Unregistering what is not registered answers -1 for category, marker event and flow
  callbacks and 0 for marker, thread and frame callbacks. `profiler.editor` is what only the
  Editor has (markers and threads of its own, job flows); `profiler.script` is a marker a script
  makes (`ProfilerMarker`): the marker callback hears of it (flag `ScriptUser`) and an event
  callback gets its begin and end; Unbound reports it so too (the markers scripts made before
  a plugin loads exist for that plugin's callbacks).
* **Event queue** (`events.load`, `frames.sequence`; Plugins/nativegfx_events.cpp):
  `IUnityEventQueue` hands the sent events out when the next frame begins, in the order sent: each
  to the handlers of its ID, the last added first, added after the event was sent too, then to
  the type's cleanup handler (the one set for the type, or the destructor its handlers name); an
  event a handler sends is handed out in the same turn; an event nothing handles is dropped, and
  one whose handler was removed reaches the cleanup handler alone. The header's
  `AddEventHandler` and `RemoveEventHandler` events add and remove a handler in their turn. The
  plugin's callbacks call a managed function that calls back into the engine.
* **Rendering extensions** (`ext.sequence`, `ext.preload`, `ext.prefix`): the Editor calls a
  loaded plugin's `UnityRenderingExtQuery` with `kUnityRenderingExtQueryOverrideVRSinglePass` while
  a camera renders, four times between OnPreRender and OnPostRender and once after (three of the
  four before the camera's `AfterForwardOpaque` buffer), the preloaded plugin's too, which
  answers that it does without any other record changing, and never its
  `UnityRenderingExtEvent`. One forward camera of the built-in pipeline, without shadows, image
  effects or XR.
* **No render texture active** (`screen.target`): in this batch-mode Editor an event issued then
  finds a framebuffer of the Editor's without attachments and the screen's size as the viewport.
* **The Vulkan interfaces on OpenGL Core** (`interface.IUnityGraphicsVulkan*`, `profiler.load`
  `distinct`, `vulkan.elsewhere`): both are answered, as objects of their own; intercepting the
  initialization or a Vulkan function and configuring the swapchain answer no. What needs the
  device is not recorded: `Instance`, `CommandRecordingState`, `ConfigureEvent` and the render
  pass calls end the Editor there.

Documented (Manual, "Low-level native plug-in interface", and the plugin headers): plugins
receive `IUnityInterfaces` in `UnityPluginLoad`; the Initialize event may be missed by a plugin
loaded after the device was created; `UnityRenderingEvent` callbacks run on the render thread.
Measured here (Editor, OpenGL Core, Direct threading mode): the render thread is the main thread;
the order of the callbacks against the scripts above; the bound targets; the two Shutdown events
at exit. Unmeasured: multithreaded rendering, the Metal device, MSAA and HDR camera
intermediates, the Game view of a windowed Editor as the target, the rendering extension
queries under a render pipeline, with other cameras or with XR, events sent to the queue from
other threads, a custom player loop.

## macOS

`run.py engine --label L` also runs on macOS arm64: the libraries are built as `.dylib` files in
`Assets/Plugins/macOS` with the importer settings OS OSX and CPU ARM64. There is no
macOS reference: the macOS Editor's device is Metal, which this OpenGL plugin does not draw with,
and `check` stays Linux-only. Compared with the Linux reference (`run.py verify`), an Unbound run
on macOS 26 matches 46/195 records: the interfaces found by GUID, `load.logs`, `event.base`, the
device answers (OpenGL Core, 17: Unbound draws macOS with Metal but does not report a Metal device
yet, so `IUnityGraphicsMetal`/`V1`/`V2` are absent as on Linux), the profiler's categories and
script markers, the event queue's load records, the native pointers' identities and the CPU copy.
Unbound gives plugins no graphics context there (one error at startup in the engine log; the
probe's own OpenGL loader opens the Linux libraries only, so its `gl=` fields are 0 on macOS
whatever is current), rendering callbacks are skipped with a warning, textures are not shared (their pointers are no texture
names), and `Texture2D.CreateExternalTexture` with the null texture the plugin could not make
throws `ArgumentException`, so the scripts stop in their second frame (`complete` false; the
records after it are missing). `IUnityGraphicsVulkan`/`V2` are null on macOS (Linux answers them
on OpenGL Core). Nothing re-checks this run: the count is a measurement, not a gate. Open: the Metal device and `IUnityGraphicsMetal` (device, command buffer and
encoder, textures as Metal textures), and the macOS Editor's answers to the other interfaces.

### External cube, volume and Vulkan storage

`externalShapes.*`, `externalOwned.*` and `externalOwnedGL.*` extend the earlier controls
without changing their reference rows. Both captures for each device use the Unity
6000.0.71f1 GPU Editor on NVIDIA. They cover cube faces and mip levels, non-square volume
slices, metadata passed to shaders, pointer identity, rebinding to different texels, CPU
access, and destroying a wrapper while its storage owner remains alive. The plugin also
creates independent cube and volume storage on OpenGL and 2D, cube and volume images on
Vulkan. Sampling them again through a newly created wrapper checks that destroying the
first wrapper leaves the plugin's image alive.

The documented factories require metadata matching the native object's shape, format and
mip chain. The pointer is a GLuint on OpenGL and an address of VkImage on Vulkan. See the
Unity 6000.0 contracts for [Cubemap.CreateExternalTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Cubemap.CreateExternalTexture.html)
and [Texture3D.CreateExternalTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture3D.CreateExternalTexture.html).
The update controls rebind each wrapper after its image has diverged from the first one.
The owner remains responsible for storage lifetime through the wrapper's last GPU use.

The measured wrappers report isReadable as true while GetPixels throws ArgumentException
because native textures have no CPU pixel storage. Renderer-owned Vulkan aliases retain
the source's stable image address. A wrapper around a plugin's VkImage has its own stable
address containing the same image handle. Plugin-owned Vulkan imports support
RGBA32 linear and sRGB, R8, RG16, R16, and R/RG/RGBA half and float storage, initialized
in GENERAL layout with sampled and transfer usage. Borrowing preserves the physical format;
it cannot convert channel order, expand channels, decode blocks or replace an unsupported
format. BGRA, packed and compressed foreign Vulkan storage remain unsupported. Other import
layouts and drivers remain outside the measured contract. The public creation API carries
neither a layout nor a synchronization handoff; a raw VkImage does not expose either.
`externalVkFormats.*` clears each of three mip levels independently on the GPU, samples
their distinct values and rewraps after destruction. Float controls retain values above 1.0. All new foreign-format images omit mutable-format
permission, including sRGB: their views must match their physical storage
([Vulkan image-view requirements](https://docs.vulkan.org/refpages/latest/refpages/source/VkImageViewCreateInfo.html)).

OpenGL's whole-image copies from external 2D and cube wrappers write one texel; untouched
pixels in these controls retain a defined sentinel color. Volume copies cover the slices.
External OpenGL cube samplers stay on level zero while their mip levels remain visible
to dimension queries and explicit copies. Compat.RenderingMeasured selects these copy and
sampling rules. `externalBounds.*` repeats them at 7x3 and 8x8, checks explicit regions, and
copies normal textures into external destinations. `externalMips.*` initializes distinct
colors in each mip and checks sampling, copies and dimension queries. Logical dimensions
and full mip visibility are preserved. `externalGpuMips.*` repeats the 2D and cube rules
with source mips written by render-target clears. Source mip 1 differs from mip 0; the cube
wrapper samples mip 0 at LOD 1 on OpenGL, while its explicit mip-1 copies read mip 1.
Whole copies still write one texel and region copies fill the target. Vulkan samples and
copies the requested mip. Known-owner aliases retain the owner's actual GPU dimensions and
levels when the wrapper declares a smaller size or no mip chain. These mismatch observations
are bounded diagnostics: the [creation contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.CreateExternalTexture.html)
requires metadata matching the underlying storage. Incompatible formats, shapes and foreign
image metadata mismatches are not established by these controls.

`activeRelease.*` distinguishes uncreated and inactive targets from a target still bound as
RenderTexture.active. Both explicit Release and DestroyImmediate emit the measured warning
and clear the active target. These are diagnostic observations, recorded on both devices,
rather than a documented warning guarantee. The [Release contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.Release.html)
releases hardware resources without destroying the object; [DestroyImmediate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.DestroyImmediate.html)
destroys the object immediately. Each control records the created state and the active
state before and after the call, and all log messages in its own row. Other profiles retain
the previous unmeasured destruction diagnostic.
