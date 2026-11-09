using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Unbound.Runtime
{
    /// <summary>
    /// The machine as the Linux Editor's SystemInfo reports it (measured: tests/projects/appfacts).
    /// The graphics device is the Editor's OpenGL Core 4.5 device, so its name, vendor, version
    /// and memory are the OpenGL driver's own strings, read from a context made on the GPU's EGL
    /// device: GL_RENDERER, GL_VENDOR, "OpenGL " + GL_VERSION, and the dedicated video memory
    /// (NVX_gpu_memory_info, or the kernel's VRAM total). The ids are the GPU's PCI vendor and
    /// device. The processor's manufacturer and clock are /proc/cpuinfo's vendor_id and current
    /// "cpu MHz"; the OS is "Linux major.minor NAME VERSION_ID 64bit" from /etc/os-release.
    /// </summary>
    internal static class MachineIdentity
    {
        internal sealed class Gpu
        {
            internal string Name, Vendor, Version;
            internal int VendorId, DeviceId, MemoryMb;
        }

        static readonly Lazy<Dictionary<string, string>> s_Cpu = new Lazy<Dictionary<string, string>>(ReadCpu);

        /// <summary>The GPU's identity as the run's graphics device reads it, null when it could
        /// not be read.</summary>
        internal static Gpu Graphics
        {
            get
            {
                if (OperatingSystem.IsLinux()) return GraphicsDeviceProfile.Current.Identity;
                Stubs.Ignored("SystemInfo graphics device", "is not read on this OS yet: the device name, vendor, version, ids and memory answer placeholders");
                return null;
            }
        }

        internal static string CpuField(string name) => s_Cpu.Value.TryGetValue(name, out var value) ? value : null;

        static Dictionary<string, string> ReadCpu()
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.Length == 0) break; // the first processor's block
                    int colon = line.IndexOf(':');
                    if (colon > 0) fields.TryAdd(line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim());
                }
            }
            catch (Exception) { }
            return fields;
        }

        /// <summary>"Linux 7.0 Ubuntu 26.04 64bit": the kernel's major.minor, then the
        /// distribution's NAME and VERSION_ID.</summary>
        internal static string LinuxOperatingSystem()
        {
            var version = Environment.OSVersion.Version;
            string text = $"Linux {version.Major}.{version.Minor}";
            try
            {
                string name = null, id = null;
                foreach (var line in File.ReadLines("/etc/os-release"))
                {
                    if (line.StartsWith("NAME=", StringComparison.Ordinal)) name = line.Substring(5).Trim('"');
                    else if (line.StartsWith("VERSION_ID=", StringComparison.Ordinal)) id = line.Substring(11).Trim('"');
                }
                if (!string.IsNullOrEmpty(name)) text += " " + name;
                if (!string.IsNullOrEmpty(id)) text += " " + id;
            }
            catch (Exception) { }
            return text + (Environment.Is64BitOperatingSystem ? " 64bit" : " 32bit");
        }

        // ------------------------------------------------------------------ GPU through EGL

        const uint EGL_PLATFORM_DEVICE_EXT = 0x313F, EGL_OPENGL_API = 0x30A2;
        const int EGL_DRM_DEVICE_FILE_EXT = 0x3233, EGL_NONE = 0x3038;
        const int EGL_DRAW = 0x3059, EGL_READ = 0x305A;
        const int EGL_CONTEXT_MAJOR_VERSION = 0x3098, EGL_CONTEXT_MINOR_VERSION = 0x30FB;
        const int EGL_CONTEXT_OPENGL_PROFILE_MASK = 0x30FD, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT = 1;
        const uint GL_VENDOR = 0x1F00, GL_RENDERER = 0x1F01, GL_VERSION = 0x1F02;
        const uint GL_GPU_MEMORY_INFO_DEDICATED_VIDMEM_NVX = 0x9047, GL_NO_ERROR = 0;

        // CoreModule disables runtime marshalling, so every signature is blittable: EGLBoolean and
        // EGLenum are uint, EGLint int, handles (EGLDisplay, EGLContext, EGLSurface, EGLDeviceEXT)
        // IntPtr, strings and lists pointers; extension and GL entry points are function pointers.
        [DllImport("libEGL.so.1")] static extern unsafe IntPtr eglGetProcAddress(byte* name);
        [DllImport("libEGL.so.1")] static extern unsafe uint eglInitialize(IntPtr display, int* major, int* minor);
        [DllImport("libEGL.so.1")] static extern uint eglBindAPI(uint api);
        [DllImport("libEGL.so.1")] static extern uint eglQueryAPI();
        [DllImport("libEGL.so.1")] static extern IntPtr eglGetCurrentDisplay();
        [DllImport("libEGL.so.1")] static extern IntPtr eglGetCurrentContext();
        [DllImport("libEGL.so.1")] static extern IntPtr eglGetCurrentSurface(int readDraw);
        [DllImport("libEGL.so.1")] static extern unsafe IntPtr eglCreateContext(IntPtr display, IntPtr config, IntPtr share, int* attributes);
        [DllImport("libEGL.so.1")] static extern uint eglDestroyContext(IntPtr display, IntPtr context);
        [DllImport("libEGL.so.1")] static extern uint eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);

        static unsafe IntPtr Proc(string name)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* p = bytes) return eglGetProcAddress(p);
        }

        /// <summary>The calling thread's EGL state this read changes: the bound client API and the
        /// OpenGL context current on the thread with its display and surfaces.</summary>
        internal struct Binding
        {
            internal IntPtr Display, Draw, Read, Context;
        }

        /// <summary>The OpenGL Core device's identity (read once, by its profile).</summary>
        internal static unsafe Gpu ReadOpenGl()
        {
            try
            {
                var queryDevices = (delegate* unmanaged[Cdecl]<int, IntPtr*, int*, uint>)Proc("eglQueryDevicesEXT");
                var platformDisplay = (delegate* unmanaged[Cdecl]<uint, IntPtr, int*, IntPtr>)Proc("eglGetPlatformDisplayEXT");
                var deviceString = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr>)Proc("eglQueryDeviceStringEXT");
                if (queryDevices == null || platformDisplay == null) return Fail("EGL cannot list the GPUs");
                var devices = new IntPtr[16];
                int count = 0;
                fixed (IntPtr* p = devices)
                    if (queryDevices(16, p, &count) == 0 || count <= 0) return Fail("EGL lists no GPU");
                int deviceCount = Math.Min(count, 16);
                var egl = new DriverEgl();
                return WithOpenGl(egl, () =>
                {
                    var previous = new Binding
                    {
                        Display = eglGetCurrentDisplay(), Draw = eglGetCurrentSurface(EGL_DRAW),
                        Read = eglGetCurrentSurface(EGL_READ), Context = eglGetCurrentContext(),
                    };
                    for (int i = 0; i < deviceCount; i++)
                    {
                        var gpu = ReadDevice(devices[i], platformDisplay, deviceString, previous, egl);
                        if (gpu != null) return gpu;
                    }
                    return Fail("no GPU makes an OpenGL 4.5 Core context");
                });
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is InvalidOperationException)
            {
                return Fail("EGL identity read failed: " + e.Message);
            }
        }

        static Gpu Fail(string why)
        {
            Stubs.Ignored("SystemInfo graphics device", $"is not read ({why}): the device name, vendor, version, ids and memory answer placeholders");
            return null;
        }

        // Local injection seam for the identity reader's ownership and failure paths. There is
        // deliberately no Terminate operation: device displays have process-shared lifetimes.
        internal abstract class EglCalls
        {
            internal abstract uint QueryApi();
            internal abstract bool BindApi(uint api);
            internal abstract bool Initialize(IntPtr display);
            internal abstract IntPtr CreateContext(IntPtr display);
            internal abstract bool MakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);
            internal abstract bool DestroyContext(IntPtr display, IntPtr context);
        }

        sealed unsafe class DriverEgl : EglCalls
        {
            internal override uint QueryApi() => eglQueryAPI();
            internal override bool BindApi(uint api) => eglBindAPI(api) != 0;
            internal override bool Initialize(IntPtr display)
            {
                int major, minor;
                return eglInitialize(display, &major, &minor) != 0;
            }
            internal override IntPtr CreateContext(IntPtr display)
            {
                var attributes = stackalloc int[]
                {
                    EGL_CONTEXT_MAJOR_VERSION, 4, EGL_CONTEXT_MINOR_VERSION, 5,
                    EGL_CONTEXT_OPENGL_PROFILE_MASK, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT, EGL_NONE,
                };
                return eglCreateContext(display, IntPtr.Zero, IntPtr.Zero, attributes);
            }
            internal override bool MakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context) =>
                eglMakeCurrent(display, draw, read, context) != 0;
            internal override bool DestroyContext(IntPtr display, IntPtr context) => eglDestroyContext(display, context) != 0;
        }

        internal static Gpu WithOpenGl(EglCalls egl, Func<Gpu> read)
        {
            uint api = egl.QueryApi();
            if (!egl.BindApi(EGL_OPENGL_API)) throw new InvalidOperationException("cannot bind the OpenGL API");
            Gpu gpu = null;
            Exception failure = null;
            try { gpu = read(); }
            catch (Exception e) { failure = e; }
            if (!egl.BindApi(api))
                throw new InvalidOperationException("cannot restore the caller's EGL API" + (failure == null ? "" : ": " + failure.Message), failure);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return gpu;
        }

        internal static Gpu ReadContext(EglCalls egl, IntPtr display, Binding previous, Func<Gpu> read)
        {
            // Initialization is retained for the process, bounded by the single Lazy read of at
            // most 16 devices. Querying a version cannot prove exclusive display ownership.
            if (!egl.Initialize(display)) return null;
            var context = egl.CreateContext(display);
            if (context == IntPtr.Zero) return null;
            bool current = false;
            string cleanupFailure = null;
            Gpu gpu = null;
            Exception readFailure = null;
            try
            {
                current = egl.MakeCurrent(display, IntPtr.Zero, IntPtr.Zero, context);
                if (current) gpu = read();
            }
            catch (Exception e) { readFailure = e; }
            if (current)
            {
                bool restored = previous.Context != IntPtr.Zero
                    ? egl.MakeCurrent(previous.Display, previous.Draw, previous.Read, previous.Context)
                    : egl.MakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (!restored)
                {
                    cleanupFailure = "cannot restore the caller's EGL context/surfaces";
                    // A failed restore leaves our context current. Release it before destruction.
                    // On release failure retain it, report failure and never touch foreign state.
                    if (!egl.MakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                        throw new InvalidOperationException(cleanupFailure + "; temporary context release failed; current context retained", readFailure);
                }
            }
            if (!egl.DestroyContext(display, context))
                cleanupFailure = (cleanupFailure == null ? "" : cleanupFailure + "; ") + "temporary EGL context destruction failed";
            if (cleanupFailure != null) throw new InvalidOperationException(cleanupFailure, readFailure);
            if (readFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(readFailure).Throw();
            return gpu;
        }

        /// <summary>The device's OpenGL strings, using an owned temporary context on a retained
        /// process-shared display. Cleanup failures cannot produce a successful identity.</summary>
        static unsafe Gpu ReadDevice(IntPtr device, delegate* unmanaged[Cdecl]<uint, IntPtr, int*, IntPtr> platformDisplay,
            delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr> deviceString, Binding previous, EglCalls egl)
        {
            int none = EGL_NONE;
            var display = platformDisplay(EGL_PLATFORM_DEVICE_EXT, device, &none);
            if (display == IntPtr.Zero) return null;
            var gpu = ReadContext(egl, display, previous, ReadStrings);
            if (gpu == null) return null;
            string node = deviceString != null ? Marshal.PtrToStringUTF8(deviceString(device, EGL_DRM_DEVICE_FILE_EXT)) : null;
            ReadPci(gpu, node);
            if (gpu.MemoryMb == 0)
                Stubs.Ignored("SystemInfo.graphicsMemorySize", $"is not read for {gpu.Name}: it answers 0");
            return gpu;
        }

        /// <summary>GL_RENDERER, GL_VENDOR, "OpenGL " + GL_VERSION and the dedicated video memory
        /// of the current context; null when the driver does not answer the strings.</summary>
        static unsafe Gpu ReadStrings()
        {
            var getString = (delegate* unmanaged[Cdecl]<uint, IntPtr>)Proc("glGetString");
            var getInteger = (delegate* unmanaged[Cdecl]<uint, int*, void>)Proc("glGetIntegerv");
            var getError = (delegate* unmanaged[Cdecl]<uint>)Proc("glGetError");
            if (getString == null) return null;
            string name = Marshal.PtrToStringUTF8(getString(GL_RENDERER));
            string vendor = Marshal.PtrToStringUTF8(getString(GL_VENDOR));
            string version = Marshal.PtrToStringUTF8(getString(GL_VERSION));
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(vendor) || string.IsNullOrEmpty(version)) return null;
            var gpu = new Gpu { Name = name, Vendor = vendor, Version = "OpenGL " + version };
            if (getInteger != null && getError != null)
            {
                for (int i = 0; i < 64 && getError() != GL_NO_ERROR; i++) { }
                int kib = 0;
                getInteger(GL_GPU_MEMORY_INFO_DEDICATED_VIDMEM_NVX, &kib);
                if (getError() == GL_NO_ERROR && kib > 0) gpu.MemoryMb = kib / 1024;
            }
            return gpu;
        }

        /// <summary>The PCI ids of the device's DRM node (else of the first GPU of the GL vendor),
        /// and the kernel's VRAM total where the driver has no NVX memory query.</summary>
        static void ReadPci(Gpu gpu, string node)
        {
            string directory = null;
            if (!string.IsNullOrEmpty(node)) directory = $"/sys/class/drm/{Path.GetFileName(node)}/device";
            if (directory == null || !File.Exists(directory + "/vendor"))
            {
                string wanted = gpu.Vendor?.StartsWith("NVIDIA", StringComparison.Ordinal) == true ? "0x10de"
                    : gpu.Vendor?.Contains("AMD", StringComparison.Ordinal) == true || gpu.Vendor?.Contains("ATI", StringComparison.Ordinal) == true ? "0x1002"
                    : gpu.Vendor?.Contains("Intel", StringComparison.Ordinal) == true ? "0x8086" : null;
                directory = null;
                try
                {
                    foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*"))
                        if (File.Exists(card + "/device/vendor") && File.ReadAllText(card + "/device/vendor").Trim() == wanted)
                        {
                            directory = card + "/device";
                            break;
                        }
                }
                catch (Exception) { }
            }
            if (directory == null) return;
            gpu.VendorId = Hex(directory + "/vendor");
            gpu.DeviceId = Hex(directory + "/device");
            if (gpu.MemoryMb == 0)
            {
                try { gpu.MemoryMb = (int)(long.Parse(File.ReadAllText(directory + "/mem_info_vram_total").Trim()) >> 20); }
                catch (Exception) { }
            }
        }

        static int Hex(string path)
        {
            try { return Convert.ToInt32(File.ReadAllText(path).Trim(), 16); }
            catch (Exception) { return 0; }
        }
    }
}
