using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Native plugins and the low-level native plugin interface (crates/unbound-runtime/src/plugin_api.rs).
    /// A library's <c>UnityPluginLoad</c> runs once, when the library is loaded: inside a script's
    /// first call into it, or before the first scene for a plugin whose importer preloads it
    /// ("Load on startup"). No library is unloaded, so <c>UnityPluginUnload</c> is not called; the
    /// application's end sends the device Shutdown events. Plugin log messages are script log
    /// messages. Matches the Unity 6000.0.71f1 Editor (tests/projects/nativegfx).
    /// </summary>
    internal static unsafe class NativePlugins
    {
        static readonly List<IntPtr> s_Loaded = new List<IntPtr>();
        /// <summary>Whether a library was loaded (read every frame, without the lock).</summary>
        static volatile bool s_AnyLoaded;
        static bool s_LogSink;

        /// <summary>Native pointers without a shared OpenGL texture (no plugin interop on this
        /// machine, or a texture it cannot share) are the renderer handle with this bit, above any
        /// OpenGL name: unique and stable, but not an OpenGL texture.</summary>
        const ulong UnsharedPointer = 1UL << 48;
        /// <summary>Marks the depth buffer of an <see cref="UnsharedPointer"/>.</summary>
        const ulong UnsharedDepth = 1UL << 47;
        /// <summary>Render buffer pointers (<c>RenderBuffer.GetNativeRenderBufferPtr</c>, a custom
        /// blit's destination): the render texture's renderer handle and the buffer's kind under
        /// this base, distinct from every texture pointer as the Editor's are (the Vulkan device's
        /// plugin interface finds the texture by it).</summary>
        const ulong RenderBufferBase = 0x5500_0000_0000_0000UL;

        static bool s_WarnedUnshared;

        /// <summary><c>kUnityRenderingExtQueryOverrideVRSinglePass</c> (IUnityRenderingExtensions.h).</summary>
        const int QueryOverrideVRSinglePass = 1 << 3;
        /// <summary>The loaded plugins' <c>UnityRenderingExtQuery</c> exports, replaced whole when
        /// a plugin loads (read by every camera render, without the lock).</summary>
        static volatile IntPtr[] s_Queries = Array.Empty<IntPtr>();
        static bool s_WarnedOverride;

        /// <summary>A native library was loaded (by its first call or by preloading): its
        /// <c>UnityPluginLoad</c> runs once.</summary>
        internal static void Loaded(IntPtr library)
        {
            if (library == IntPtr.Zero || !Native.IsReady) return;
            lock (s_Loaded)
            {
                if (s_Loaded.Contains(library)) return;
                s_Loaded.Add(library);
                s_AnyLoaded = true;
            }
            // UnityRenderingExtEvent is not called: the Editor sent it nothing of its own
            // accord in the measured run (tests/projects/nativegfx ext.sequence: one forward
            // camera, no XR).
            if (NativeLibrary.TryGetExport(library, "UnityRenderingExtQuery", out var query))
            {
                lock (s_Loaded)
                {
                    var queries = new IntPtr[s_Queries.Length + 1];
                    s_Queries.CopyTo(queries, 0);
                    queries[^1] = query;
                    s_Queries = queries;
                }
            }
            if (!NativeLibrary.TryGetExport(library, "UnityPluginLoad", out var load)) return;
            // The markers scripts made so far exist for the plugin's marker callbacks.
            Unbound.Runtime.Profiling.ProfilerCore.ShareMarkersWithPlugins();
            EnsureLogSink();
            Native.Api.PluginLoad((void*)load);
        }

        /// <summary>Loads the plugins whose importer preloads them, before the first scene.</summary>
        internal static void Preload(IReadOnlyList<string> paths)
        {
            foreach (var path in paths)
            {
                try { Loaded(NativeLibrary.Load(path)); }
                catch (Exception e) { Debug.LogError($"Unbound: preloading the native plugin {path} failed: {e.Message}"); }
            }
        }

        /// <summary>While a camera renders, the Editor asks every loaded plugin that exports
        /// <c>UnityRenderingExtQuery</c> whether it overrides single-pass stereo
        /// (<c>kUnityRenderingExtQueryOverrideVRSinglePass</c>), on the main thread with the
        /// graphics context current: <paramref name="times"/> times here. Measured on one
        /// built-in pipeline forward camera without XR, shadows or image effects: three times
        /// after OnPreRender, once before OnPostRender and once after
        /// (tests/projects/nativegfx <c>ext.sequence</c>, <c>ext.preload</c>). A plugin that says
        /// it does (the probe's preloaded one) changes nothing there; here the answer has no
        /// use, which is logged once: there is no XR.</summary>
        internal static void QueryStereoOverride(int times)
        {
            var queries = s_Queries;
            if (queries.Length == 0 || !Native.IsReady) return;
            for (int i = 0; i < times; i++)
            {
                foreach (var query in queries)
                {
                    if (Native.Api.PluginQuery((void*)query, QueryOverrideVRSinglePass) == 0 || s_WarnedOverride) continue;
                    s_WarnedOverride = true;
                    Native.Log(2, "a native plugin answers UnityRenderingExtQuery that it overrides single-pass stereo rendering: Unbound has no XR, the answer changes nothing");
                }
            }
        }

        /// <summary>A frame ended (the player loop's <c>ProfilerEndFrame</c>): the plugins'
        /// profiler frame callbacks, as the Editor calls them after a frame's scripts
        /// (tests/projects/nativegfx <c>frames.sequence</c>).</summary>
        internal static void EndFrame()
        {
            if (s_AnyLoaded && Native.IsReady) Native.Api.PluginFrame();
        }

        /// <summary>Play mode begins, on the main thread and before any plugin loads: the first
        /// dispatch (of nothing) tells the plugin interfaces which thread the main one is.</summary>
        internal static void Boot()
        {
            if (Native.IsReady) Native.Api.PluginEvents();
        }

        /// <summary>A frame began (the player loop's <c>DispatchEventQueueEvents</c>): the
        /// events plugins sent through the event queue reach their handlers
        /// (tests/projects/nativegfx <c>frames.sequence</c>: in the frame after the one they
        /// were sent in).</summary>
        internal static void DispatchEvents()
        {
            if (s_AnyLoaded && Native.IsReady) Native.Api.PluginEvents();
        }

        /// <summary>The application ends: the plugins' device Shutdown events.</summary>
        internal static void Shutdown()
        {
            lock (s_Loaded) if (s_Loaded.Count == 0) return;
            if (Native.IsReady) Native.Api.PluginShutdown();
        }

        /// <summary><c>GL.IssuePluginEvent(int)</c>: every loaded plugin's <c>UnityRenderEvent</c>.</summary>
        internal static void IssueLegacyEvent(int eventId)
        {
            IntPtr[] loaded;
            lock (s_Loaded) loaded = s_Loaded.ToArray();
            foreach (var library in loaded)
                if (NativeLibrary.TryGetExport(library, "UnityRenderEvent", out var callback))
                    GfxStream.PluginEvent(callback, eventId);
        }

        static void EnsureLogSink()
        {
            if (s_LogSink) return;
            s_LogSink = true;
            Native.Api.PluginSetLog(&OnPluginLog);
        }

        /// <summary>A plugin message (<c>IUnityLog</c>, the check after a callback): a script log
        /// message of its <see cref="LogType"/> (UnityLogType has the same values).</summary>
        [UnmanagedCallersOnly]
        static void OnPluginLog(int type, byte* text, int length)
        {
            try
            {
                var message = Encoding.UTF8.GetString(text, Math.Max(length, 0));
                Debug.unityLogger.Log((LogType)type, message);
            }
            catch (Exception e)
            {
                Native.Log(0, "a native plugin message could not be logged: " + e.Message);
            }
        }

        /// <summary>The native pointer of a texture with a renderer handle (<paramref name="depth"/>:
        /// its depth buffer): the OpenGL texture name plugins see, sharing the renderer's memory.</summary>
        internal static IntPtr TexturePointer(ulong handle, bool depth, bool hasDepth = true)
        {
            if (handle == 0) return IntPtr.Zero;
            // The texture exists on the renderer once the commands so far have run.
            GfxStream.Flush();
            ulong name = Native.IsReady ? Native.Api.NativeTexture(handle, depth ? 1 : 0) : 0;
            if (name != 0) return (IntPtr)(long)name;
            if (!s_WarnedUnshared)
            {
                s_WarnedUnshared = true;
                Native.Log(2, "a native texture pointer is not an OpenGL texture: the renderer could not share the texture with native plugins (see the log above)");
            }
            // A texture without a depth buffer gives its color texture for it.
            return (IntPtr)(long)(UnsharedPointer | (depth && hasDepth ? UnsharedDepth : 0) | handle);
        }

        /// <summary>A render buffer's native pointer: stable for the render texture (none: the
        /// screen) and the buffer's kind.</summary>
        internal static IntPtr RenderBufferPointer(RenderTexture texture, bool depth) =>
            (IntPtr)(long)(RenderBufferBase | ((texture != null ? TextureHandles.Of(texture) : 0UL) << 1) | (depth ? 1UL : 0UL));

        /// <summary>The texture ID a plugin's texture update and custom blit receive: the
        /// renderer's handle (the Editor's is not the OpenGL name either).</summary>
        internal static uint TextureId(ulong handle) => (uint)handle;

        /// <summary>The <c>UnityRenderingExtTextureFormat</c> and bytes per pixel a texture update
        /// of a texture format passes, for the formats whose texels the renderer stores as they
        /// are given; false for others.</summary>
        internal static bool UpdateFormat(TextureFormat format, bool srgb, out uint extFormat, out uint bytesPerPixel)
        {
            // kUnityRenderingExtFormatR8G8B8A8_SRGB, _UNorm and R8G8B8_SRGB, _UNorm.
            const uint Rgba8Srgb = 4, Rgba8 = 8, Rgb8Srgb = 3, Rgb8 = 7;
            switch (format)
            {
                case TextureFormat.RGBA32:
                    (extFormat, bytesPerPixel) = (srgb ? Rgba8Srgb : Rgba8, 4);
                    return true;
                case TextureFormat.RGB24:
                    (extFormat, bytesPerPixel) = (srgb ? Rgb8Srgb : Rgb8, 3);
                    return true;
                default:
                    (extFormat, bytesPerPixel) = (0, 0);
                    return false;
            }
        }
    }
}
