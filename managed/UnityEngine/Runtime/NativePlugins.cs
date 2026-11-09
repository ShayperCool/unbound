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
        static bool s_LogSink;

        /// <summary>Native pointers without a shared OpenGL texture (no plugin interop on this
        /// machine, or a texture it cannot share) are the renderer handle with this bit, above any
        /// OpenGL name: unique and stable, but not an OpenGL texture.</summary>
        const ulong UnsharedPointer = 1UL << 48;
        /// <summary>Marks the depth buffer of an <see cref="UnsharedPointer"/>.</summary>
        const ulong UnsharedDepth = 1UL << 47;
        /// <summary>Render buffer pointers (<c>RenderBuffer.GetNativeRenderBufferPtr</c>, a custom
        /// blit's destination): the render texture's instance ID and the buffer's kind under this
        /// base, distinct from every texture pointer as the Editor's are.</summary>
        const ulong RenderBufferBase = 0x5500_0000_0000_0000UL;

        static bool s_WarnedUnshared;

        /// <summary>A native library was loaded (by its first call or by preloading): its
        /// <c>UnityPluginLoad</c> runs once.</summary>
        internal static void Loaded(IntPtr library)
        {
            if (library == IntPtr.Zero || !Native.IsReady) return;
            lock (s_Loaded)
            {
                if (s_Loaded.Contains(library)) return;
                s_Loaded.Add(library);
            }
            if (!NativeLibrary.TryGetExport(library, "UnityPluginLoad", out var load)) return;
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
        internal static IntPtr TexturePointer(ulong handle, bool depth)
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
            return (IntPtr)(long)(UnsharedPointer | (depth ? UnsharedDepth : 0) | handle);
        }

        /// <summary>A render buffer's native pointer: stable for the render texture (0 the screen)
        /// and the buffer's kind.</summary>
        internal static IntPtr RenderBufferPointer(int renderTextureInstanceId, bool depth) =>
            (IntPtr)(long)(RenderBufferBase | ((ulong)(uint)renderTextureInstanceId << 1) | (depth ? 1UL : 0UL));

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
