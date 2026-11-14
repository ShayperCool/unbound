using System;
using System.Runtime.InteropServices;

namespace Unbound.Runtime
{
    /// <summary>The GPU as the macOS Editor's Metal device names it (tests/projects/urp CapsProbe
    /// `device-metal.jsonl` on an Apple M2 Pro): the system default MTLDevice's name, the
    /// vendor as the name's first word ("Apple"), the version "Metal", vendor and device ids 0,
    /// recommendedMaxWorkingSetSize in MiB as the memory and maxBufferLength as the largest
    /// graphics buffer (SystemInfo.maxGraphicsBufferSize: 9534832640 on an Apple M2 Pro with
    /// 16 GB, two macOS Editor captures). Only Apple silicon is measured.</summary>
    internal static class MetalIdentity
    {
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

        [DllImport(MetalFramework)] static extern IntPtr MTLCreateSystemDefaultDevice();
        [DllImport(ObjC)] static extern unsafe IntPtr sel_registerName(byte* name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern ulong SendUInt64(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void Send(IntPtr receiver, IntPtr selector);

        static unsafe IntPtr Selector(ReadOnlySpan<byte> name)
        {
            fixed (byte* p = name) return sel_registerName(p);
        }

        /// <summary>The Metal device's identity, null (reported) when Metal cannot be read.</summary>
        internal static MachineIdentity.Gpu Read()
        {
            try
            {
                var device = MTLCreateSystemDefaultDevice();
                if (device == IntPtr.Zero) throw new InvalidOperationException("there is no Metal device");
                try
                {
                    var nameObject = SendPointer(device, Selector("name\0"u8));
                    var name = Marshal.PtrToStringUTF8(SendPointer(nameObject, Selector("UTF8String\0"u8))) ?? "";
                    ulong memory = SendUInt64(device, Selector("recommendedMaxWorkingSetSize\0"u8));
                    ulong maxBuffer = SendUInt64(device, Selector("maxBufferLength\0"u8));
                    int space = name.IndexOf(' ');
                    return new MachineIdentity.Gpu
                    {
                        Name = name,
                        Vendor = space > 0 ? name.Substring(0, space) : name,
                        Version = "Metal",
                        MemoryMb = (int)(memory / (1024 * 1024)),
                        MaxBufferBytes = (long)maxBuffer,
                    };
                }
                finally
                {
                    Send(device, Selector("release\0"u8));
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                Stubs.Ignored("SystemInfo graphics device", $"is not read ({e.Message}): the device name, vendor, version, ids and memory answer placeholders");
                return null;
            }
        }
    }
}
