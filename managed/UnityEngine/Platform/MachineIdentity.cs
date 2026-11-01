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

        static readonly Lazy<Gpu> s_Gpu = new Lazy<Gpu>(ReadGpu);
        static readonly Lazy<Dictionary<string, string>> s_Cpu = new Lazy<Dictionary<string, string>>(ReadCpu);

        /// <summary>The GPU's identity, null when no OpenGL device could be read.</summary>
        internal static Gpu Graphics
        {
            get
            {
                if (OperatingSystem.IsLinux()) return s_Gpu.Value;
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

        const int EGL_PLATFORM_DEVICE_EXT = 0x313F, EGL_DRM_DEVICE_FILE_EXT = 0x3233;
        const int EGL_OPENGL_API = 0x30A2, EGL_NONE = 0x3038;
        const int EGL_CONTEXT_MAJOR_VERSION = 0x3098, EGL_CONTEXT_MINOR_VERSION = 0x30FB;
        const int EGL_CONTEXT_OPENGL_PROFILE_MASK = 0x30FD, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT = 1;
        const uint GL_VENDOR = 0x1F00, GL_RENDERER = 0x1F01, GL_VERSION = 0x1F02;
        const uint GL_GPU_MEMORY_INFO_DEDICATED_VIDMEM_NVX = 0x9047, GL_NO_ERROR = 0;

        // CoreModule disables runtime marshalling: blittable signatures and function pointers only.
        [DllImport("libEGL.so.1")] static extern unsafe IntPtr eglGetProcAddress(byte* name);
        [DllImport("libEGL.so.1")] static extern unsafe uint eglInitialize(IntPtr display, int* major, int* minor);
        [DllImport("libEGL.so.1")] static extern uint eglTerminate(IntPtr display);
        [DllImport("libEGL.so.1")] static extern uint eglBindAPI(int api);
        [DllImport("libEGL.so.1")] static extern unsafe IntPtr eglCreateContext(IntPtr display, IntPtr config, IntPtr share, int* attributes);
        [DllImport("libEGL.so.1")] static extern uint eglDestroyContext(IntPtr display, IntPtr context);
        [DllImport("libEGL.so.1")] static extern uint eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);

        static unsafe IntPtr Proc(string name)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* p = bytes) return eglGetProcAddress(p);
        }

        static unsafe Gpu ReadGpu()
        {
            try
            {
                var queryDevices = (delegate* unmanaged[Cdecl]<int, IntPtr*, int*, uint>)Proc("eglQueryDevicesEXT");
                var platformDisplay = (delegate* unmanaged[Cdecl]<int, IntPtr, int*, IntPtr>)Proc("eglGetPlatformDisplayEXT");
                var deviceString = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr>)Proc("eglQueryDeviceStringEXT");
                if (queryDevices == null || platformDisplay == null) return Fail("EGL cannot list the GPUs");
                var devices = stackalloc IntPtr[16];
                int count;
                if (queryDevices(16, devices, &count) == 0 || count == 0) return Fail("EGL lists no GPU");
                for (int i = 0; i < Math.Min(count, 16); i++)
                {
                    var gpu = ReadDevice(devices[i], platformDisplay, deviceString);
                    if (gpu != null) return gpu;
                }
                return Fail("no GPU makes an OpenGL 4.5 Core context");
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return Fail("libEGL is not available: " + e.Message);
            }
        }

        static Gpu Fail(string why)
        {
            Stubs.Ignored("SystemInfo graphics device", $"is not read ({why}): the device name, vendor, version, ids and memory answer placeholders");
            return null;
        }

        /// <summary>The device's OpenGL strings, from a 4.5 Core context without a surface.</summary>
        static unsafe Gpu ReadDevice(IntPtr device, delegate* unmanaged[Cdecl]<int, IntPtr, int*, IntPtr> platformDisplay,
            delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr> deviceString)
        {
            int none = EGL_NONE;
            var display = platformDisplay(EGL_PLATFORM_DEVICE_EXT, device, &none);
            int major, minor;
            if (display == IntPtr.Zero || eglInitialize(display, &major, &minor) == 0) return null;
            try
            {
                if (eglBindAPI(EGL_OPENGL_API) == 0) return null;
                var attributes = stackalloc int[]
                {
                    EGL_CONTEXT_MAJOR_VERSION, 4, EGL_CONTEXT_MINOR_VERSION, 5,
                    EGL_CONTEXT_OPENGL_PROFILE_MASK, EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT, EGL_NONE,
                };
                var context = eglCreateContext(display, IntPtr.Zero, IntPtr.Zero, attributes);
                if (context == IntPtr.Zero) return null;
                try
                {
                    if (eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, context) == 0) return null;
                    var getString = (delegate* unmanaged[Cdecl]<uint, IntPtr>)Proc("glGetString");
                    var getInteger = (delegate* unmanaged[Cdecl]<uint, int*, void>)Proc("glGetIntegerv");
                    var getError = (delegate* unmanaged[Cdecl]<uint>)Proc("glGetError");
                    if (getString == null) return null;
                    var gpu = new Gpu
                    {
                        Name = Marshal.PtrToStringUTF8(getString(GL_RENDERER)),
                        Vendor = Marshal.PtrToStringUTF8(getString(GL_VENDOR)),
                        Version = "OpenGL " + Marshal.PtrToStringUTF8(getString(GL_VERSION)),
                    };
                    if (getInteger != null && getError != null)
                    {
                        while (getError() != GL_NO_ERROR) { }
                        int kib = 0;
                        getInteger(GL_GPU_MEMORY_INFO_DEDICATED_VIDMEM_NVX, &kib);
                        if (getError() == GL_NO_ERROR && kib > 0) gpu.MemoryMb = kib / 1024;
                    }
                    eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    string node = deviceString != null ? Marshal.PtrToStringUTF8(deviceString(device, EGL_DRM_DEVICE_FILE_EXT)) : null;
                    ReadPci(gpu, node);
                    if (gpu.MemoryMb == 0)
                        Stubs.Ignored("SystemInfo.graphicsMemorySize", $"is not read for {gpu.Name}: it answers 0");
                    return gpu;
                }
                finally
                {
                    eglDestroyContext(display, context);
                }
            }
            finally
            {
                eglTerminate(display);
            }
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
