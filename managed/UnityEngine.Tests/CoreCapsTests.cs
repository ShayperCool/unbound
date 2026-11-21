using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using Unity.Baselib.LowLevel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Xunit;
using static Unity.Baselib.LowLevel.Binding;

namespace Unbound.Tests
{
    /// <summary>Core system capabilities and low-level memory page controls (Q636):
    /// Baselib mmap/mprotect/madvise page allocation and state transitions, sticky ErrorState,
    /// Vulkan PCI vendor lookup, Linux sysfs battery reader, and GL/Vulkan buffer/tiled-storage limits.</summary>
    public sealed unsafe class CoreCapsTests
    {
        sealed class LogCapture : ILogHandler
        {
            public readonly List<string> Messages = new List<string>();
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) =>
                Messages.Add(logType + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
            public void LogException(Exception exception, UnityEngine.Object context) =>
                Messages.Add("Exception: " + exception.Message);
        }

        static string LinuxVmaPerms(IntPtr address)
        {
            ulong target = (ulong)address;
            foreach (var line in File.ReadLines("/proc/self/maps"))
            {
                int dash = line.IndexOf('-');
                int sp = line.IndexOf(' ');
                if (dash < 0 || sp < 0) continue;
                ulong start = Convert.ToUInt64(line.Substring(0, dash), 16);
                ulong end = Convert.ToUInt64(line.Substring(dash + 1, sp - dash - 1), 16);
                if (target >= start && target < end)
                    return line.Substring(sp + 1, 4);
            }
            return null;
        }

        [Fact]
        public void BaselibMemoryPagesRoundTripAndProtectWithoutStubWarnings()
        {
            var capture = new LogCapture();
            var prev = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                ulong page = (ulong)Environment.SystemPageSize;
                Baselib_Memory_PageSizeInfo info = default;
                Baselib_Memory_GetPageSizeInfo(&info);
                Assert.Equal(page, info.defaultPageSize);
                Assert.Equal(1UL, info.pageSizesLen);
                Assert.Equal(page, info.pageSizes0);

                // Allocate aligned ReadWrite pages, verify zeroing & read/write.
                Baselib_ErrorState err = default;
                var aligned = Baselib_Memory_AllocatePages(page, 2UL, 4UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                Assert.NotEqual(IntPtr.Zero, aligned.ptr);
                Assert.Equal(0UL, (ulong)aligned.ptr % (4UL * page));
                Assert.Equal(0u, *(uint*)aligned.ptr);
                *(uint*)aligned.ptr = 0x12345678u;
                Assert.Equal(0x12345678u, *(uint*)aligned.ptr);
                Baselib_Memory_ReleasePages(aligned, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);

                // Reserved -> ReadWrite -> write -> ReadOnly -> read -> Reserved -> ReadWrite -> zeroed.
                // On Linux, also assert real OS page table protections in /proc/self/maps.
                err = default;
                var alloc = Baselib_Memory_AllocatePages(page, 2UL, 1UL, Baselib_Memory_PageState.Reserved, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                if (OperatingSystem.IsLinux()) Assert.Equal("---p", LinuxVmaPerms(alloc.ptr));
                Baselib_Memory_SetPageState(alloc.ptr, page, 2UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                if (OperatingSystem.IsLinux()) Assert.Equal("rw-p", LinuxVmaPerms(alloc.ptr));
                *(uint*)alloc.ptr = 0xCAFEBABEu;
                Baselib_Memory_SetPageState(alloc.ptr, page, 2UL, Baselib_Memory_PageState.ReadOnly, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                if (OperatingSystem.IsLinux()) Assert.Equal("r--p", LinuxVmaPerms(alloc.ptr));
                Assert.Equal(0xCAFEBABEu, *(uint*)alloc.ptr);
                Baselib_Memory_SetPageState(alloc.ptr, page, 2UL, Baselib_Memory_PageState.Reserved, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                if (OperatingSystem.IsLinux()) Assert.Equal("---p", LinuxVmaPerms(alloc.ptr));
                Baselib_Memory_SetPageState(alloc.ptr, page, 2UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                if (OperatingSystem.IsLinux())
                {
                    Assert.Equal("rw-p", LinuxVmaPerms(alloc.ptr));
                    Assert.Equal(0u, *(uint*)alloc.ptr);
                }
                Baselib_Memory_ReleasePages(alloc, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                if (OperatingSystem.IsLinux()) Assert.Null(LinuxVmaPerms(alloc.ptr));

                // Ex variants with extPageState = 0.
                var exAlloc = Baselib_Memory_AllocatePagesEx(page, 1UL, 1UL, Baselib_Memory_PageState.ReadWrite, 0u, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                Baselib_Memory_SetPageStateEx(exAlloc.ptr, page, 1UL, Baselib_Memory_PageState.ReadOnly, 0u, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                if (OperatingSystem.IsLinux()) Assert.Equal("r--p", LinuxVmaPerms(exAlloc.ptr));
                Baselib_Memory_ReleasePages(exAlloc, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);

                Assert.DoesNotContain(capture.Messages, m => m.Contains("Unbound:"));
            }
            finally
            {
                Debug.unityLogger.logHandler = prev;
            }
        }

        [Fact]
        public void BaselibMemoryErrorsAndStickyErrorStateMatchEditor()
        {
            ulong page = (ulong)Environment.SystemPageSize;
            Baselib_ErrorState err;

            err = default;
            var a = Baselib_Memory_AllocatePages(page, 0UL, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
            Assert.Equal(Baselib_ErrorCode.InvalidPageCount, err.code);
            Assert.Equal(IntPtr.Zero, a.ptr);

            err = default;
            a = Baselib_Memory_AllocatePages(123UL, 1UL, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
            Assert.Equal(Baselib_ErrorCode.InvalidPageSize, err.code);
            Assert.Equal(IntPtr.Zero, a.ptr);

            err = default;
            a = Baselib_Memory_AllocatePages(page, 1UL, 0UL, Baselib_Memory_PageState.ReadWrite, &err);
            Assert.Equal(Baselib_ErrorCode.UnsupportedAlignment, err.code);

            err = default;
            a = Baselib_Memory_AllocatePages(page, 1UL, 3UL, Baselib_Memory_PageState.ReadWrite, &err);
            Assert.Equal(Baselib_ErrorCode.UnsupportedAlignment, err.code);

            err = default;
            a = Baselib_Memory_AllocatePages(page, 1UL, 1UL, (Baselib_Memory_PageState)99, &err);
            Assert.Equal(Baselib_ErrorCode.UnsupportedPageState, err.code);

            err = default;
            a = Baselib_Memory_AllocatePages(page, ulong.MaxValue, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
            Assert.Equal(Baselib_ErrorCode.OutOfMemory, err.code);

            err = default;
            var valid = Baselib_Memory_AllocatePages(page, 1UL, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
            Assert.Equal(Baselib_ErrorCode.Success, err.code);
            try
            {
                // Null ptr and zero count succeed silently.
                err = default;
                Baselib_Memory_SetPageState(IntPtr.Zero, page, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                Baselib_Memory_SetPageState(valid.ptr, page, 0UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                Baselib_Memory_ReleasePages(new Baselib_Memory_PageAllocation { ptr = IntPtr.Zero, pageSize = page, pageCount = 1UL }, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
                Baselib_Memory_ReleasePages(new Baselib_Memory_PageAllocation { ptr = valid.ptr, pageSize = page, pageCount = 0UL }, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);

                err = default;
                Baselib_Memory_SetPageState(valid.ptr, 123UL, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidPageSize, err.code);

                err = default;
                Baselib_Memory_SetPageState(valid.ptr, page, 1UL, (Baselib_Memory_PageState)99, &err);
                Assert.Equal(Baselib_ErrorCode.UnsupportedPageState, err.code);

                err = default;
                Baselib_Memory_SetPageState(valid.ptr, page, ulong.MaxValue, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.OutOfMemory, err.code);

                // Sticky error state: non-Success code is never overwritten and skips the syscall.
                err = new Baselib_ErrorState { code = Baselib_ErrorCode.InvalidArgument };
                Baselib_Memory_SetPageState(valid.ptr, 123UL, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidArgument, err.code);
                Baselib_Memory_SetPageState(valid.ptr, page, 1UL, Baselib_Memory_PageState.Reserved, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidArgument, err.code);
                if (OperatingSystem.IsLinux()) Assert.Equal("rw-p", LinuxVmaPerms(valid.ptr));

                err = default;
                Baselib_Memory_ReleasePages(new Baselib_Memory_PageAllocation { ptr = valid.ptr, pageSize = 123UL, pageCount = 1UL }, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidPageSize, err.code);
            }
            finally
            {
                err = default;
                Baselib_Memory_ReleasePages(valid, &err);
                Assert.Equal(Baselib_ErrorCode.Success, err.code);
            }
        }

        [Fact]
        public void VulkanVendorNameCoversKnownPciIdsAndUnknownFallbackWithoutStubWarnings()
        {
            var capture = new LogCapture();
            var prev = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                var known = new (int Id, string Name)[]
                {
                    (0x0000, "REFERENCE"),
                    (0x1002, "ATI"),
                    (0x1010, "Imagination Technologies"),
                    (0x1023, "Trident"),
                    (0x102b, "Matrox"),
                    (0x1039, "SIS"),
                    (0x104a, "Imagination Technologies"),
                    (0x10de, "NVIDIA"),
                    (0x1106, "VIA/S3"),
                    (0x121a, "3dfx"),
                    (0x13b5, "ARM"),
                    (0x1414, "Microsoft"),
                    (0x144d, "Samsung"),
                    (0x15ad, "VMWare"),
                    (0x18ca, "XGI"),
                    (0x1ab8, "Parallels"),
                    (0x1d9e, "Anka"),
                    (0x3d3d, "3dLabs"),
                    (0x4144564e, "NVIDIA"),
                    (0x4d4f4351, "Qualcomm"),
                    (0x5143, "Qualcomm"),
                    (0x5333, "S3"),
                    (0x8086, "Intel"),
                    (0x80ee, "VirtualBox"),
                };
                foreach (var (id, expected) in known)
                    Assert.Equal(expected, VulkanIdentity.VendorName(id));

                Assert.Equal("Unknown (ID=9999)", VulkanIdentity.VendorName(0x9999));
                Assert.Equal("Unknown (ID=1)", VulkanIdentity.VendorName(0x0001));
                Assert.Equal("Unknown (ID=abcd)", VulkanIdentity.VendorName(0xabcd));
                Assert.DoesNotContain(capture.Messages, m => m.Contains("Unbound:"));
            }
            finally
            {
                Debug.unityLogger.logHandler = prev;
            }
        }

        [Fact]
        public void LinuxSysfsBatteryReaderHandlesDesktopPeripheralAndLaptopBatteries()
        {
            var root = Path.Combine(Path.GetTempPath(), "unbound-battery-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Missing directory: desktop default (-1, Full).
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // Empty directory & AC-only supply: (-1, Full).
                Directory.CreateDirectory(Path.Combine(root, "AC0"));
                File.WriteAllText(Path.Combine(root, "AC0", "type"), "Mains\n");
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // Peripheral device battery (scope=Device): skipped.
                Directory.CreateDirectory(Path.Combine(root, "hidpp_battery_0"));
                File.WriteAllText(Path.Combine(root, "hidpp_battery_0", "type"), "Battery\n");
                File.WriteAllText(Path.Combine(root, "hidpp_battery_0", "scope"), "Device\n");
                File.WriteAllText(Path.Combine(root, "hidpp_battery_0", "status"), "Discharging\n");
                File.WriteAllText(Path.Combine(root, "hidpp_battery_0", "capacity"), "42\n");
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // System battery: charging at 73%.
                var bat0 = Path.Combine(root, "BAT0");
                Directory.CreateDirectory(bat0);
                File.WriteAllText(Path.Combine(bat0, "type"), "Battery\n");
                File.WriteAllText(Path.Combine(bat0, "present"), "1\n");
                File.WriteAllText(Path.Combine(bat0, "status"), "Charging\n");
                File.WriteAllText(Path.Combine(bat0, "capacity"), "73\n");
                Assert.Equal((0.73f, BatteryStatus.Charging), SystemInfo.ReadLinuxBattery(root));

                // Capacity > 100 clamps to 1.0; Not charging maps to Full.
                File.WriteAllText(Path.Combine(bat0, "status"), "Not charging\n");
                File.WriteAllText(Path.Combine(bat0, "capacity"), "105\n");
                Assert.Equal((1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // Absent battery (present=0) maps to (-1, Full).
                File.WriteAllText(Path.Combine(bat0, "present"), "0\n");
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // Two system batteries: picks battery with longer remaining time.
                File.WriteAllText(Path.Combine(bat0, "present"), "1\n");
                File.WriteAllText(Path.Combine(bat0, "status"), "Discharging\n");
                File.WriteAllText(Path.Combine(bat0, "capacity"), "30\n");
                File.WriteAllText(Path.Combine(bat0, "time_to_empty_now"), "1200\n");
                var bat1 = Path.Combine(root, "BAT1");
                Directory.CreateDirectory(bat1);
                File.WriteAllText(Path.Combine(bat1, "type"), "Battery\n");
                File.WriteAllText(Path.Combine(bat1, "present"), "1\n");
                File.WriteAllText(Path.Combine(bat1, "status"), "Discharging\n");
                File.WriteAllText(Path.Combine(bat1, "capacity"), "85\n");
                File.WriteAllText(Path.Combine(bat1, "time_to_empty_now"), "5400\n");
                Assert.Equal((0.85f, BatteryStatus.Discharging), SystemInfo.ReadLinuxBattery(root));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ScalableBufferManagerQuantizeHandlesBoundariesAndNonFiniteInputs()
        {
            Assert.Equal(1f, ScalableBufferManager.Quantize(1f));
            Assert.Equal(1f, ScalableBufferManager.Quantize(2f));
            Assert.Equal(1f, ScalableBufferManager.Quantize(float.PositiveInfinity));
            Assert.Equal(1f, ScalableBufferManager.Quantize(float.NaN));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(0f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(-1f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(float.NegativeInfinity));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(0.05f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(0.0500000119f));
            Assert.Equal(0.0999999642f, ScalableBufferManager.Quantize(0.06f));
            Assert.Equal(0.349999964f, ScalableBufferManager.Quantize(0.33f));
            Assert.Equal(0.95f, ScalableBufferManager.Quantize(0.94f));
            Assert.Equal(0.95f, ScalableBufferManager.Quantize(0.95f));
            Assert.Equal(1f, ScalableBufferManager.Quantize(0.99f));
        }

        [Fact]
        public void GraphicsBufferLimitAndTiledStorageMatchEditorOnOpenGLCoreAndVulkan()
        {
            var prev = GraphicsDeviceProfile.Current;
            var capture = new LogCapture();
            var prevLog = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                foreach (var device in new[] { "OpenGLCore", "Vulkan" })
                {
                    GraphicsDeviceProfile.Select(device);
                    Assert.Equal(2147483648L, SystemInfo.maxGraphicsBufferSize);

                    var ex = Assert.Throws<ArgumentException>(() =>
                        new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)(2147483648L / 4L) + 1, 4));
                    Assert.Equal(
                        "The total size of the graphics buffer (2147483652 bytes) exceeds the maximum buffer size. Maximum supported buffer size: 2147483648 bytes.",
                        ex.Message);

                    Assert.Equal(16, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.R8G8B8A8_UNorm, 4));
                    Assert.Equal(64, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.R16G16B16A16_SFloat, 8));
                    Assert.Equal(8, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.D32_SFloat_S8_UInt, 1));
                    Assert.Equal(0, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.None, 4));
                    Assert.Equal(0, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.R8G8B8A8_UNorm, 0));
                    Assert.Equal(-4, SystemInfo.GetTiledRenderTargetStorageSize(GraphicsFormat.R8G8B8A8_UNorm, -1));
                }
                Assert.DoesNotContain(capture.Messages, m => m.Contains("Unbound:"));
            }
            finally
            {
                Debug.unityLogger.logHandler = prevLog;
                GraphicsDeviceProfile.Select(prev.Type.ToString());
            }
        }
    }
}
