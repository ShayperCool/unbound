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
* **External textures** (`external.*`): `Texture2D.CreateExternalTexture` and
  `UpdateExternalTexture` with textures the plugin made, and with another texture's pointer
  (the same texture), also a render texture with depth and mips, which works on after the
  external texture is destroyed.
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
  (-1); thread, frame and flow callbacks beyond four are taken. A marker event callback makes the
  marker's `callback` non-null and `EmitEvent` calls it at once, with no profiler recording; it is
  unregistered for a marker, without a marker for every marker it was registered for with the user
  data, and without user data either for all. A thread that registers is heard of on that thread.
  The frame callback is called once a frame, after the frame's scripts and before the frame count
  advances. Unregistering what is not registered answers -1 for category, marker event and flow
  callbacks and 0 for marker, thread and frame callbacks. `profiler.editor` is what only the
  Editor has (markers and threads of its own, job flows); `profiler.script` is a marker a script
  makes (`ProfilerMarker`): the marker callback hears of it (flag `ScriptUser`) and an event
  callback gets its begin and end.
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
