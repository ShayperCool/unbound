# Native rendering plugin probe

Unity 6000.0.71f1, built-in pipeline, Linear color space, OpenGL Core (the Linux Editor's device).
The probe's own plugin (`Plugins/nativegfx_probe.c`, built by `run.py` into the project copy)
uses the low-level native plugin interface and records what it gets; the scripts
(`Assets/Scripts/NativeGfxProbe.cs`) drive it and read back what it drew.

    python3 tests/projects/nativegfx/run.py check      # engine run compared with Reference/

Current NVIDIA primary results against Unity 6000.0.71f1 are OpenGL Core 554/613 with
59 declared residuals and Vulkan 1217/1236 with 19. The OpenGL stencil profile is
582/654 with 72; the Vulkan stencil profile is 1451/1470 with 19 declared
residuals. Vulkan attachment capabilities and multisample color
usage close 63 of its 64 earlier differences. All 518 earlier Vulkan reference
rows are byte-identical; 49 allocation controls and 298 resolve-access and
setter controls are appended. The foreign-format extension adds 70 rows to both Vulkan profiles; the stencil
profile also includes its 25 publication rows and 209 publication controls. Both Vulkan
profiles append 174 readback controls; the three earlier source-diagnostic differences close.
The depth-shape extension adds 49 OpenGL rows and 73 Vulkan rows to both profiles.
It exposes 11 OpenGL and 16 Vulkan depth-storage or diagnostic differences; these are
recorded separately from the earlier residuals. All prior 564, 935, 605 and 960 rows
remain byte-identical within their respective profiles. Comparator tolerances are unchanged.

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
* **Retained stencil** (`--stencil`, OpenGL Core): the plugin initializes the native depth
  image's stencil to 113 before a clear, stencil writes of 37 and 91, and self-resolves.
  A single-sample image follows the writes; a multisample target's retained stencil keeps
  113 while its depth is published. Depth-only publication preserves the destination's
  stencil. The separate canonical profile includes every primary row and the raw callback
  error logs. Run it with `python3 tests/projects/nativegfx/run.py check --stencil`.
  The Vulkan profile uses `--device vulkan --stencil`. At 1, 2, 4 and 8 samples,
  the Editor publishes the initialized stencil, clear, two successive writes
  and explicit self-resolves. Unbound matches all 25 publication rows.
  `sampleStencil.*` additionally writes 37 into sample zero and 91 into the other
  samples, then changes only the left half of sample zero to 203. Two depth/stencil
  formats, 7x3 storage, 1/2/4/8 samples and explicit self-resolves select sample zero
  while preserving the other half. One native pointer is reused across all writes.
  Overwriting the published stencil with 113 before an explicit self-resolve
  shows that a previously published image keeps native edits. A later color-only
  clear, color-only draw, or partial stencil clear inside a native plugin pass
  publishes the attachment again. The single-sample controls distinguish direct
  edits from a separate published image. All 209 new rows match; two independent
  NVIDIA captures preserve every earlier 1087 reference row. Other drivers remain
  unmeasured. Within one callback, `sameCallbackRead` records a partial stencil
  clear of 173 after `EnsureOutsideRenderPass`; `sameCallbackAfter` confirms that
  the callback's later edit to 113 survives its return.
* **Depth storage and mip attachments** (`depthShape.*`): 2D targets, three-layer arrays
  and cubes, both with and without mipmaps, distinguish ARGB32 attachments from Shadowmap
  depth-only images. Public mip counts and native image metadata are recorded before and
  after a clear at level 1 or level 0, using array layer 2 and cube face NegativeZ.
  On the measured NVIDIA devices, separate depth images have one physical level even when
  the public mip count is four; a normal cube target's depth pointer instead names its color
  image. Shadowmap storage has no stencil. The render packet now preserves 24-bit depth
  without stencil, including when a descriptor requests that format directly.
  The mip allocation and binding-diagnostic differences remain explicit
  residuals. These metadata controls do not establish depth content at another mip or layer,
  or successful binding where the Editor logs an assertion.
  The Unity 6000.0 [D24_UNorm contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Experimental.Rendering.GraphicsFormat.D24_UNorm.html)
  specifies depth without stencil; D24_UNorm_S8_UInt includes stencil. The no-mipmap Shadowmap
  and ARGB32 controls distinguish the two physical storages. The managed release controls
  retain the earlier packet mapping for unmeasured releases.
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

## macOS: the Metal device

The macOS Editor's only device is Metal (tests/projects/urp CapsProbe), so is Unbound's there.
`run.py ... --device metal` (macOS arm64 only) builds the probe's Metal plugin
(`Plugins/nativegfx_metal.m`, `libNativeGfxMetal.dylib`, Objective-C against the system's Metal
framework) and `NativeGfxMetalProbe.cs` records the Metal plugin interface:

    python3 tests/projects/nativegfx/run.py native --label L --device metal   # two macOS Editor captures
    python3 tests/projects/nativegfx/run.py publish L-1 L-2 --device metal
    python3 tests/projects/nativegfx/run.py check --device metal              # macOS only, not in the Linux gate

`Reference/unity-6000.0.71f1/metal.jsonl` is two identical captures of the macOS Editor in batchmode
(arm64, Apple M2 Pro, macOS 26). Recorded: at load `GetRenderer` 16, `IUnityGraphicsMetal`, `V1` and
`V2` (three tables), no `IUnityGraphicsVulkan`, the device (the system default device), Metal's
bundle, the command queue (of the device); in callbacks from `GL.IssuePluginEvent`, a camera's
command buffer and an executed command buffer: the main thread, the current command buffer (of
the queue, not committed), the current encoder and render pass descriptor (attachments named by the
native pointers handed over, their format and shape; load and store actions and texture memory as
records of their own); a draw with the current render encoder (a camera with HDR and MSAA draws
into a 2-sample half-float intermediate with a resolve; without them into its target), ending it
and blitting with the current command buffer, `CommitCurrentCommandBuffer` (it returns the
committed buffer and goes on in a new one), a blit with a command buffer of the plugin's own after
that commit, the same commit from a script outside callbacks (a blit with the current command
buffer, committed and waited on), a copy from a texture updated with `Apply` just before the
event in the same frame (the plugin copies its earlier texels: the update runs after), a draw
into a multisampled render texture sampled right after in the same command buffer, the render
pass descriptor before the plugin asks for the encoder (there after a draw into the target or
an event that drew through the encoder, nil after only a clear) and after it ends the encoder
(nil, in a later event too), changes the plugin makes to it before
ending the encoder, in the next event and in the same one (the target keeps its contents), the
pointers of 2D, sRGB, mip-mapped, three-channel, cube, array and 3D textures and
of render textures (color, depth, multisampled samples, depth-only), what the render buffer
pointers give (`RenderBufferFromHandle` returns the pointer; their texture, resolved texture,
stencil texture), an external texture of the plugin's, and the pixels.

The screen's color texture is BGRA8Unorm (format 80), as Unbound's screen on Metal is; a screen
camera with command buffers draws into a texture standing for the screen, stored as the screen is,
which a callback in its buffer finds as that format too. Such a camera's buffer blitting
`CameraTarget` onto itself through a shader (`screenself.blit`) samples white there, and
ReadPixels in its onPostRender (`screenself.read`) reads the screen's unencoded bytes. Screen
copies (`screencopy.*`, `screencamera.*`, appended to the reference by two further identical
captures): in this batch-mode Editor, which has no Game view, `CommandBuffer.CopyTexture` naming
`CameraTarget`, from an executed buffer and inside a camera drawing to the screen (the plugin finds
the screen's own color texture there), copies nothing and logs `Graphics.CopyTexture called with
null source texture` (or `destination`); a `Blit` from that camera's target reads it.

Device conversions (`srgb.*`, `loadoutside.*`, appended the same way): an sRGB render texture
drawn into with a color whose encoded red would be 127.5 stores 127 (presumably a tie; a
`GL.Clear` color is taken as linear and stored sRGB-encoded: 0.5 stores 188), its texels decode to
32-bit floats (191 to 0.52088),
and texel loads (`Texture2D.Load`) outside a texture read zero. Volumes (`volume.*`):
`SystemInfo.supportsCompressed3DTextures` is true; BC1 and ASTC 4x4 volumes made from their
blocks are sampled per slice and read by GetPixels32 (`*.cpu`); a volume of every compressed TextureFormat is made, except Crunched
ones (`ArgumentException: Crunched Texture3D is not supported.`) and PVRTC, ETC and EAC ones
(`Metal does not support <format> texture format for Texture3D` logged, then `UnityException:
Failed to create texture because of invalid parameters.`). Arrays of ETC2_RGBA1, signed EAC and
PVRTC (`array.make.*`, `array.decode.*`, 8x8 arrays of the same bytes) keep their blocks (a 4x4
PVRTC level is 32 bytes at 2 and 4 bits); GetPixels32 decodes ETC2_RGBA1 and PVRTC (the RGB ones
keep the alpha they decode to) and throws for signed EAC (`texture uses an unsupported format`).

Frame state (`mipbias.*`, `active.*`, `outside.*`): `Texture.mipMapBias` changes nothing (the
level sampled at one texel per pixel is 0 for biases 0, 1, 2 and -1, point, bilinear and
trilinear); a render texture made active in one frame's Update is no longer active in the next
frame's; a command buffer executed outside cameras draws with GL's matrices (in batchmode
identity, also right after a camera rendered; GL.LoadOrtho's inside a GL.LoadOrtho), the right
way up, and its own matrices do not last past it (a DrawMeshNow after it takes GL's). In the
windowed Editor (`metal-windowed.jsonl`) GL's matrices there are its own window drawing's pixel
matrix, so `outside.fresh`, `aftercamera`, `corner` and `after` draw nothing; Unbound follows the
windowed Editor (Q515), which makes those four rows declared residuals against `metal.jsonl`.
tests/projects/framestate measures both rules on the Linux Editors (windowed and batchmode,
OpenGL Core and Vulkan).
`compressed.formats` lists the
TextureFormat values `GraphicsFormatUtility.IsCompressedFormat` answers true for.

Dynamic resolution (`dynres.*`, frames 13 and 14, `Shaders/DynamicParams.shader`): 16x16 render
textures, one marked `useDynamicScale`, then `ScalableBufferManager.ResizeBuffers(0.5, 0.25)`.
The marked one's native texture becomes 8x4 in that frame (also one made after the resize, a
temporary one, and 15x13 ones: rounded up) while its `width` and `height` stay; at 1 it is 16x16
again and still holds its last color. A camera drawing into it has `pixelWidth` 16 but
`scaledPixelWidth` and `pixelRect` 8x4, with `allowDynamicResolution` on or off; into the plain one
16x16. A Blit from it samples its whole 8x4 image and its `_MainTex_TexelSize` is 16x16; a draw
into it from a camera's command buffer sees `_ScreenParams` 8x4 (`cameraparams.sampled`), while
the image a Blit into it leaves samples as zero there (`blitparams.sampled`; the rows without
`.sampled` read through ReadPixels, which reads zeros). `ReadPixels` of 16x16 from it logs
`Reading pixels out of bounds of the current active render texture` and reads nothing; of 8x4 it
reads zeros. Factors outside (0, 1] are asserted against (`Invalid widthScale: 2.000000`, NaN
printed `nan`) and kept within [0.05, 1] (0 keeps 0.0500000119, NaN 1); 16 x 0.3 and 16 x 0.7
give 5x12, 10 x 0.3 gives 3 (the product in single precision). A texture marked
`useDynamicScaleExplicit` too stays 16x16 until `ApplyDynamicScale`, then takes the factors of
the time (`dynres.explicit.*`); the call changes nothing for a plain dynamic one. Content across scales (`dynres.content.*`, a left
red, right green pattern): blitted at 0.5 it reads uniform red at 1; blitted at 1 it reads
garbled when sampled at 0.5 and whole again back at 1 (`back`). With quadrants (red, green
below blue, white), sampled at 0.5 then at 0.25x0.5 and written at neither, it is whole again at
1 (`twice`), as is one marked `useDynamicScaleExplicit` scaled and back by `ApplyDynamicScale`
(`explicit`), a multisampled one (4 samples), sampled and read (`msaa.sampled`, `msaa`), one
scaled right after it was drawn into (`msaa.direct`), and one
sampled at factors of 0.99, which round its size up to the full one (`near`).

Windowed (`run.py native --device metal --windowed`, the Editor with its windows, Play mode in
its Game view, Run In Background set in the copy): `Reference/unity-6000.0.71f1/metal-windowed.jsonl`
holds the rows other than the plugin's records (whose order varies with the windowed Editor's
render thread), two captures identical in them. There the screen is the Game view's
BGRA8Unorm_sRGB texture (607x505 in these captures), `CommandBuffer.CopyTexture` of
`CameraTarget` is refused as in batch mode, `active.*`, `outside.identity` and `outside.ortho`
match batch mode, and `outside.fresh`, `aftercamera`, `corner` and `after` draw nothing (GL's
matrices outside cameras were observed not to be identity there; presumably those its windows'
drawing left). Plugin events run later there, on the Editor's render thread: `camera.pixels` and
`executed.pixels` show colors the scripts set after the events. The engine run is compared with
the batch-mode reference only.

Unbound matches 346/392 records with 46 declared residuals, the four `outside.*` rows among them
since Q515 (rerun on macOS) (`residuals-metal.json`, each with its
reason): the textures' memory (the Editor's colors are shared, the renderer's private; its depth
textures have pixel format views), the descriptor's load action over a render texture (the
Editor's is the pass it began, clearing; Unbound begins a pass of its own that loads), a Blit from a
screen camera's target green 1/255 apart (the batch-mode screen is not sRGB-encoded), PVRTC arrays' texels (an
approximate decoder), a dynamically scaled texture's content after a change of scale
(Unbound's is remade cleared), what a Blit into one leaves there (zero in the Editor), a screen
camera's target blitted onto itself (white in the Editor) and ReadPixels of the screen in such a
camera (Unbound's bytes sRGB-encoded). The screen is shared:
outside render texture targets the current encoder is over the screen's color texture and depth
buffer (loaded), in callbacks and from scripts, and its descriptor is there before the plugin asks
for the encoder (`plugin.peek.screen`, `plugin.peek.outside`); store actions are unknown until the
encoder ends. After `EndCurrentCommandEncoder`, inside a render texture's pass, over the screen
and from a script alike, there is no descriptor until the plugin asks for an encoder again, which
is over the screen with its contents not loaded (`*.camera2.2`, `*.mutate`, `*.screen`,
`*.outside`); `CommitCurrentCommandBuffer` keeps the target (`*.commitrt`). `run.py` runs Unbound
at 64x64 for this device, the size of the Editor's batchmode screen. Measured on macOS 26 only;
Intel Macs, other GPUs, an HDR (EDR) screen and foveation are unmeasured. The OpenGL probe
(`run.py engine` without `--device`) still builds on macOS, but its plugin draws with OpenGL,
which plugins do not get on Metal: it is not a macOS measurement.

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
format. RG32, RGBA64 and signed-normalized R/RG/RGBA 8- and 16-bit storage also retain
their exact physical formats. DXT1 and DXT5 support linear and sRGB storage; BC4
and BC5 retain their component counts. Borrowed block images need the device
compression feature and block-aligned base dimensions. Devices that would expand
or decode storage are refused before import. BGRA32 linear/sRGB and RGB9e5Float also retain their physical storage. Other packed
formats (including RGB565 and RGBA4444) and unmeasured compressed formats remain unsupported. Other import
layouts and drivers remain outside the measured contract. The public creation API carries
neither a layout nor a synchronization handoff; a raw VkImage does not expose either.
`externalVkFormats.*` clears each of three mip levels independently on the GPU, samples
their distinct values and rewraps after destruction. Float controls retain values above 1.0. All new foreign-format images omit mutable-format
permission, including sRGB: their views must match their physical storage
([Vulkan image-view requirements](https://docs.vulkan.org/refpages/latest/refpages/source/VkImageViewCreateInfo.html)).
Each block-format control uploads three valid mip blocks with distinct colors.
The 14 added format/color-space cases produce 70 mip and rewrapping controls.
The shared coherent transfer allocator also serves stencil readback; upload
buffers remain alive until fenced readbacks finish all uses.
The RGBA32 sRGB control also copies raw bytes into compatible RGBA8 storage. Incompatible copies
between that image and R8 leave both divergent destinations unchanged, with diagnostics.
The [CopyTexture contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.CopyTexture.html)
requires compatible formats; physical sRGB storage is never repacked through an invalid
linear view.

`externalVkBgraCopy.*` and `externalVkPackedCopy.RGB9e5Float.*` extend the two Vulkan
profiles by 54 rows each, preserving every earlier row byte-for-byte. BGRA imports use
B8G8R8A8 linear or sRGB storage and RGB9e5 imports use shared-exponent storage, with
no alternate-format views. Capability checks reject unsupported sampled/transfer storage
before borrowing. Each source has three independently written mip levels; copies overwrite
a different destination at level zero, leave its other mip and the source unchanged, and
keep the same native image alive after wrapper destruction and rewrapping. The RGB9e5
controls retain a green value of 1.5. Vulkan BGRA copies currently require identical physical
formats; cross-order and cross-encoding copies fail explicitly and remain queued. Other
backends retain their existing copy rules.

Direct converted readbacks request RGBA bytes with matching encoding for BGRA, and RGBAFloat
for RGB9e5. Their returned values are strict controls for channel order, sRGB bytes and HDR
precision. The [Unity 6000.0 readback contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.Request.html)
automatically converts to the requested destination format and advises checking ReadPixels
support. In both NVIDIA Editor repeats these three requests return valid data with hasError
false but also emit unsupported-source-format errors. Each new `readback.logs` row retains
all those diagnostics independently; the earlier aggregate log row remains unchanged.
Unbound now reports the same source-format errors and returns the matching data.
`readbackValidation.*` adds 174 controls per Vulkan profile. Owned 3x2 RGBA32 and
BGRA32 textures distinguish implicit and explicit destination formats, whole and
regional requests, and native-array/slice destinations. Supported RGBA requests
are quiet. Direct explicit BGRA requests log the unsupported source and still return
data; implicit requests omit that warning. A bad mip reports the source warning first
only for direct explicit-format requests. A depth-only source instead fails its
source-format check before mip validation, with the extra ReadPixels warning only
on the direct explicit request.

Command-buffer requests omit that entry-point diagnostic, including native-array
and slice destinations. Recording is quiet; a bad mip reports its error when the
buffer executes. The selected release's [direct bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Runtime/Export/Graphics/AsyncGPUReadback.bindings.cs)
and [command-buffer bindings](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Runtime/Export/Graphics/RenderingCommandBuffer.bindings.cs)
use those different request paths. The shared conversion and completion code retains
that distinction.

Vulkan Alpha8 readback reports ReadPixels support for its unnamed graphics-format
value and reads its stored value as red, filling the other channels with one. An
Alpha8 destination therefore reads the filled alpha; an RGBA destination shows the
varying red values. RGBA source controls use alpha 32 with different RGB bytes:
conversion to Alpha8 returns 32. Unbound maps its own RGBA alpha storage into that
measured red representation before conversion. OpenGL's unsupported Alpha8 destination
keeps its immediate failure, dimensions and single source diagnostic, as recorded
by `tests/projects/gpubuffers --controls`.

Two independent NVIDIA Editor captures agree on every added row and preserve all
earlier rows. Compat.RenderingMeasured selects source validation and
Compat.DeviceTextureReadback selects the measured Alpha8 device rule. New source
diagnostics require measured usage information: named graphics formats and Alpha8
on the measured Linux profiles. Unnamed ARGB32 capability values and Metal Alpha8
remain unmeasured and retain their earlier source-diagnostic policy. Other releases
retain the earlier unmeasured validation and Alpha8 policies. These observations
do not establish other unsupported-source requests as safe.
The [Texture2D factory contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.CreateExternalTexture.html)
requires metadata matching the underlying 2D image. These controls use matching metadata;
wrapper destruction is tested after writes and rewrapping reads the surviving owner's
mip contents. Other drivers, formats, layouts and synchronization handoffs are unmeasured.

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


### Vulkan image allocation capabilities

Owned optimal 2D attachments request input-attachment and both transfer usages
when the device supports the complete format, creation flags, sample count, mip
count, layer count and extent. Unsupported combinations retain their original
usages. Transient, foreign, linear, DRM-modifier and volume allocations keep their
contracts; swapchains keep their configured usage flags. The plugin receives the
actual HAL allocation usages, including capabilities wgpu needs for initialization,
rather than a reconstruction from the public descriptor. Imported images report
the importer's declared minimum because Vulkan cannot query an existing image's
creation descriptor.

The SDK's IUnityGraphicsVulkan.h defines UnityVulkanImage.usage as VkImageUsageFlags.
The [Vulkan image creation rules](https://docs.vulkan.org/refpages/latest/refpages/source/VkImageCreateInfo.html)
require supported usage combinations, extents and sample counts. Existing
plugin.device.*, render-buffer, resolve and sRGB controls verify the measured
NVIDIA result without changing any reference rows. GPU unit controls also use a
7x3 attachment, sample counts 1 and 4, a conservative borrowed view, a sampled
copy destination and volume initialization.

The physical Vulkan multisample color attachment has sampled usage only when
bindTextureMS exposes its samples to shaders. Otherwise it is an attachment and
resolve source. OpenGL retains texture storage for native plugin access. This is
a backend resource permission, selected at allocation without changing the
public texture format or the release profile. The [bindTextureMS contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture-bindTextureMS.html)
permits shader access to multisampled storage; it does not specify native usage
flags. The measured flags are 147 without shader access and 151 with it, on
NVIDIA only. allocation.* records 7x3 RGBA8 and RGBAHalf targets at 2, 4 and 8
samples, before a clear, after a clear and after native pointer access. All 36
physical-image observations match. A GPU unit control independently checks the
actual sampled and attachment usage flags for both formats and bind modes.

`allocation.*.resolve` also records resolve-image access for every 7x3 target.
Without bindTextureMS, the accessor returns the separate single-sample image.
With bindTextureMS, both render-buffer accessors expose the same multisampled
image. Single-sample targets have no resolve image. This does not establish a
supported accessor for a hidden retained single-sample image in bound mode.

`setter.*` records setter state, diagnostics and native metadata before
and after pointer access, including failed creation, release and recreation.
Changing the flag on created storage is refused; assigning the same value is
quiet. Released or failed storage permits changes. Enabling the flag before
creation is accepted for 2D and 2D-array shapes and refused for cubes and volumes.
These are measured Unity 6000.0.71f1 state rules selected by
Compat.RenderingMeasured. The public contract does not specify setter refusal
or diagnostic text. Other releases retain the earlier unverified setter policy.
Every native-image query checks successful creation first.

Two sRGB block mip rows retain a nine-digit decimal midpoint formatting difference,
0.223632813 in the Editor and 0.223632812 here. Both parse back to float32 bits
0x3e650000. The raw strings remain strict declared residuals; no formatter
emulation or comparison tolerance was added.

The remaining Vulkan metadata row retains its raw difference: plugin.dontcare.update
has a command buffer in Unbound and none in this Editor history. The SDK header
IUnityGraphicsVulkan.h leaves DontCare unconstrained; no recording-state policy
is inferred from that one history.

OpenGL's 42 original residuals remain: 39 callback error records, two attachment
kind differences and Editor profiler activity. Six added multisample-compute
rows retain earlier graphics resources in the Editor. This task does not
synthesize those diagnostic histories or replace real GPU resources with
metadata. Retained-color publication for bindTextureMS, multisampled arrays,
other foreign Vulkan formats/layout handoffs and other drivers remain queued.
The OpenGL stencil profile's content controls match; its additional callback
error rows remain visible. Vulkan now publishes both aspects of supported 2D
single-layer multisample depth/stencil attachments, including ordinary writes.
Broader depth-image
shapes and mip allocation also remain queued. Exploratory 2D, array and cube
measurements distinguish public mip counts from actual depth storage; implementing
those allocation rules requires corresponding attachment and shader-load controls.

### Vulkan depth and stencil publication

The selected release's retained-depth flag also enables publication of stencil on
Vulkan. Render passes and framebuffers are prepared with each texture allocation,
reused for writes, and retired through the GPU completion fence when storage is
replaced or released. Both images are initialized through wgpu before native
writes. A prepared native command records the cached pass and both attachment
transitions in the existing wgpu command stream, retaining both Core views for
submission. Recording reuses that command without creating extra encoders,
labels or transition vectors. Plugin passes include the depth/stencil resolve
attachment so publication happens before later outside segments of that callback.
The device must provide Vulkan 1.2 and sample-zero resolve support for both aspects;
matching formats, extents and single-layer 2D storage are required. Unsupported
devices retain depth-only publication with a diagnostic, and stencil remains an
explicit unsupported case there. Multisample arrays remain outside this measured
publication scope. Ordinary attachment passes, native plugin passes and internal
depth-copy passes share the publication operation. Subsequent shader reads and
explicit self-resolves do not repeat an already completed Vulkan publication.

| Basis | Unity 6000.0.71f1 contract or measured control |
| --- | --- |
| [ResolveAntiAliasedSurface](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.ResolveAntiAliasedSurface.html) | An omitted target resolves into the source. The native stencil image is already published after drawing; `sampleStencil.*.overwritten` and `.forcedSelf` record that a later explicit call preserves native edits. No stronger stencil guarantee is inferred from this color-surface API. |
| [GetNativeDepthBufferPtr](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.GetNativeDepthBufferPtr.html) | Supplies the graphics resource for native manipulation. `sampleStencil.*` reuses that pointer before and after attachment writes. |
| [Vulkan plugin SDK](https://github.com/Unity-Technologies/NativeRenderingPlugin/blob/master/PluginSource/source/Unity/IUnityGraphicsVulkan.h) | Ending a plugin pass permits outside work. `sameCallbackRead` observes the attachment write before the following native image edit, all in one callback. The SDK leaves use with the SRP RenderPass API undefined; these controls use built-in pipeline targets. |
| [Vulkan depth/stencil resolve rules](https://docs.vulkan.org/refpages/latest/refpages/source/VkSubpassDescriptionDepthStencilResolve.html) | Both resolve modes must be supported; the source is multisampled and the destination single-sampled with matching depth/stencil components. The implementation queries both modes and resolves both with sample zero. |

Sample selection and publication timing are observations on NVIDIA, not a public
Unity guarantee for other devices. OpenGL's retained-stencil controls continue to
use depth-only publication, which preserves destination stencil.
