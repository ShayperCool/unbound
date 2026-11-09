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
  (the same texture).
* **Errors** (`execute.glerror.logs`): a GL error a callback leaves is logged as an assertion.
* **Shutdown** (`plugin.exit`, `plugin.preloaded`, `plugin.prefixed`): the device's Shutdown event
  twice at exit, no `UnityPluginUnload`; a plugin whose importer has "Load on startup"
  (`isPreloaded`) is loaded before Play mode without an Initialize event; the `GfxPlugin` name
  prefix alone does not preload in the Editor.

Documented (Manual, "Low-level native plug-in interface", and the plugin headers): plugins
receive `IUnityInterfaces` in `UnityPluginLoad`; the Initialize event may be missed by a plugin
loaded after the device was created; `UnityRenderingEvent` callbacks run on the render thread.
Measured here (Editor, OpenGL Core, Direct threading mode): the render thread is the main thread;
the order of the callbacks against the scripts above; the bound targets; the two Shutdown events
at exit. Unmeasured: multithreaded rendering, Vulkan and Metal devices, MSAA and HDR camera
intermediates, the screen as the target.
