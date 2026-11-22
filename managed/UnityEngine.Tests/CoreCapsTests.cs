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
                var skipped = Baselib_Memory_AllocatePages(page, 1UL, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(IntPtr.Zero, skipped.ptr);
                Assert.Equal(0UL, skipped.pageCount);
                Assert.Equal(Baselib_ErrorCode.InvalidArgument, err.code);
                Baselib_Memory_SetPageState(valid.ptr, 123UL, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidArgument, err.code);
                Baselib_Memory_SetPageState(valid.ptr, page, 1UL, Baselib_Memory_PageState.Reserved, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidArgument, err.code);
                Baselib_Memory_ReleasePages(valid, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidArgument, err.code);
                if (OperatingSystem.IsLinux()) Assert.Equal("rw-p", LinuxVmaPerms(valid.ptr));
                *(byte*)valid.ptr = 0x5A;
                Assert.Equal((byte)0x5A, *(byte*)valid.ptr);

                // Syscall failure paths: capture POSIX errno via LibraryImport SetLastError.
                err = default;
                ulong hugePages = ((ulong)long.MaxValue / page) / 2UL;
                var oom = Baselib_Memory_AllocatePages(page, hugePages, 1UL, Baselib_Memory_PageState.ReadWrite, &err);
                Assert.Equal(IntPtr.Zero, oom.ptr);
                Assert.Equal(Baselib_ErrorCode.OutOfMemory, err.code);
                Assert.Equal(Baselib_ErrorState_NativeErrorCodeType.PlatformDefined, err.nativeErrorCodeType);
                Assert.Equal(12UL, err.nativeErrorCode); // ENOMEM

                err = default;
                Baselib_Memory_SetPageState(valid.ptr + 1, page, 1UL, Baselib_Memory_PageState.ReadOnly, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidAddressRange, err.code);
                Assert.Equal(Baselib_ErrorState_NativeErrorCodeType.PlatformDefined, err.nativeErrorCodeType);
                Assert.Equal(22UL, err.nativeErrorCode); // EINVAL (mprotect)

                err = default;
                Baselib_Memory_SetPageState(valid.ptr + 1, page, 1UL, Baselib_Memory_PageState.Reserved, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidAddressRange, err.code);
                Assert.Equal(Baselib_ErrorState_NativeErrorCodeType.PlatformDefined, err.nativeErrorCodeType);
                Assert.Equal(22UL, err.nativeErrorCode); // EINVAL (madvise)

                if (OperatingSystem.IsLinux())
                {
                    // Address 0x1000 is below mmap_min_addr on Linux and never mapped: mprotect fails with ENOMEM.
                    err = default;
                    Baselib_Memory_SetPageState((IntPtr)page, page, 1UL, Baselib_Memory_PageState.ReadOnly, &err);
                    Assert.Equal(Baselib_ErrorCode.InvalidAddressRange, err.code);
                    Assert.Equal(Baselib_ErrorState_NativeErrorCodeType.PlatformDefined, err.nativeErrorCodeType);
                    Assert.Equal(12UL, err.nativeErrorCode); // ENOMEM
                }

                err = default;
                Baselib_Memory_ReleasePages(new Baselib_Memory_PageAllocation { ptr = valid.ptr + 1, pageSize = page, pageCount = 1UL }, &err);
                Assert.Equal(Baselib_ErrorCode.InvalidAddressRange, err.code);
                Assert.Equal(Baselib_ErrorState_NativeErrorCodeType.PlatformDefined, err.nativeErrorCodeType);
                Assert.Equal(22UL, err.nativeErrorCode); // EINVAL (munmap)

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

        static int Hits(string member)
        {
            foreach (var hit in Unbound.Runtime.Stubs.HitReport())
                if (hit.Key == member) return hit.Value;
            return 0;
        }

        [Fact]
        public void OnlyTheMeasuredVulkanVendorIsNamedWithoutANotice()
        {
            const string Member = "SystemInfo.graphicsDeviceVendor";
            int before = Hits(Member);
            // NVIDIA's name is measured: no notice.
            Assert.Equal("NVIDIA", VulkanIdentity.VendorName(0x10de));
            Assert.Equal(before, Hits(Member));
            // Another vendor is named from the list, an unlisted one by its ID in hexadecimal;
            // each use says that the name is not measured.
            Assert.Equal("Intel", VulkanIdentity.VendorName(0x8086));
            Assert.Equal(before + 1, Hits(Member));
            Assert.Equal("Unknown (ID=abcd)", VulkanIdentity.VendorName(0xabcd));
            Assert.Equal("Unknown (ID=1)", VulkanIdentity.VendorName(0x0001));
            Assert.Equal(before + 3, Hits(Member));
        }

        [Fact]
        public void LinuxBatteryIsReadAsSdlReadsThePowerSupplies()
        {
            var root = Path.Combine(Path.GetTempPath(), "unbound-battery-test-" + Guid.NewGuid().ToString("N"));
            string Supply(string name, params (string File, string Text)[] files)
            {
                var dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                foreach (var existing in Directory.GetFiles(dir)) File.Delete(existing);
                foreach (var (file, text) in files) File.WriteAllText(Path.Combine(dir, file), text + "\n");
                return dir;
            }
            try
            {
                // No directory, and only a mains supply: no battery, which the Editor answers as -1 and Full.
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));
                Supply("AC0", ("type", "Mains"));
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // A peripheral's battery does not count.
                Supply("hidpp_battery_0", ("type", "Battery"), ("scope", "Device"), ("status", "Discharging"), ("capacity", "42"));
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // A system battery: its status and capacity.
                Supply("BAT0", ("type", "Battery"), ("present", "1"), ("status", "Charging"), ("capacity", "73"));
                Assert.Equal((0.73f, BatteryStatus.Charging), SystemInfo.ReadLinuxBattery(root));
                Supply("BAT0", ("type", "Battery"), ("status", "Discharging"), ("capacity", "40"));
                Assert.Equal((0.4f, BatteryStatus.Discharging), SystemInfo.ReadLinuxBattery(root));
                // Full and Not charging are both a charged battery; a capacity over 100 is 100.
                Supply("BAT0", ("type", "Battery"), ("status", "Not charging"), ("capacity", "105"));
                Assert.Equal((1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));
                // A status that is missing or unheard of is unknown.
                Supply("BAT0", ("type", "Battery"), ("capacity", "12"));
                Assert.Equal((0.12f, BatteryStatus.Unknown), SystemInfo.ReadLinuxBattery(root));
                Supply("BAT0", ("type", "Battery"), ("status", "Recalibrating"), ("capacity", "12"));
                Assert.Equal((0.12f, BatteryStatus.Unknown), SystemInfo.ReadLinuxBattery(root));
                // A battery that is not present says "no battery" (Full), whatever its status file says.
                Supply("BAT0", ("type", "Battery"), ("present", "0"), ("status", "Discharging"));
                Assert.Equal((-1f, BatteryStatus.Full), SystemInfo.ReadLinuxBattery(root));

                // Two batteries: the one with more seconds left, whatever the percents.
                Supply("BAT0", ("type", "Battery"), ("status", "Discharging"), ("capacity", "90"), ("time_to_empty_now", "1200"));
                Supply("BAT1", ("type", "Battery"), ("status", "Charging"), ("capacity", "30"), ("time_to_empty_now", "5400"));
                Assert.Equal((0.3f, BatteryStatus.Charging), SystemInfo.ReadLinuxBattery(root));
                // Seconds beat a battery that reports none; zero seconds are none.
                Supply("BAT1", ("type", "Battery"), ("status", "Charging"), ("capacity", "95"), ("time_to_empty_now", "0"));
                Assert.Equal((0.9f, BatteryStatus.Discharging), SystemInfo.ReadLinuxBattery(root));
                // Without seconds on either, the higher percent.
                Supply("BAT0", ("type", "Battery"), ("status", "Discharging"), ("capacity", "90"));
                Assert.Equal((0.95f, BatteryStatus.Charging), SystemInfo.ReadLinuxBattery(root));
                // Seconds from energy over power count only while on battery.
                Supply("BAT0", ("type", "Battery"), ("status", "Discharging"), ("capacity", "20"), ("energy_now", "1000"), ("power_now", "500"));
                Assert.Equal((0.2f, BatteryStatus.Discharging), SystemInfo.ReadLinuxBattery(root));
                Supply("BAT0", ("type", "Battery"), ("status", "Charging"), ("capacity", "20"), ("energy_now", "1000"), ("power_now", "500"));
                Assert.Equal((0.95f, BatteryStatus.Charging), SystemInfo.ReadLinuxBattery(root));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>The factor kept for a scale: rows of tests/projects/corecaps dynres.factors.*
        /// on Vulkan, and what the same rule gives beside them.</summary>
        [Fact]
        public void ScaleFactorsKeepTheStepThatCoversTheScaleInSinglePrecision()
        {
            Assert.Equal(1f, ScalableBufferManager.Quantize(1f));
            Assert.Equal(1f, ScalableBufferManager.Quantize(2f));
            Assert.Equal(1f, ScalableBufferManager.Quantize(0.99f));
            Assert.Equal(1f, ScalableBufferManager.Quantize(0.96f));
            Assert.Equal(0.95f, ScalableBufferManager.Quantize(0.95f));
            Assert.Equal(0.95f, ScalableBufferManager.Quantize(0.94f));
            Assert.Equal(0.85f, ScalableBufferManager.Quantize(0.85f));
            Assert.Equal(0.75f, ScalableBufferManager.Quantize(0.73f));
            Assert.Equal(0.65f, ScalableBufferManager.Quantize(0.65f));
            Assert.Equal(0.45f, ScalableBufferManager.Quantize(0.45f));
            // Single precision decides the edges: 0.35f is over the step named 0.35.
            Assert.Equal(0.399999976f, ScalableBufferManager.Quantize(0.35f));
            Assert.Equal(0.399999976f, ScalableBufferManager.Quantize(0.35000002f));
            Assert.Equal(0.349999964f, ScalableBufferManager.Quantize(0.34999996f));
            Assert.Equal(0.349999964f, ScalableBufferManager.Quantize(0.33f));
            Assert.Equal(0.199999988f, ScalableBufferManager.Quantize(0.2f));
            Assert.Equal(0.149999976f, ScalableBufferManager.Quantize(0.15f));
            Assert.Equal(0.0999999642f, ScalableBufferManager.Quantize(0.06f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(0.05f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(0.0500000119f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(0.04f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(0f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(-1f));
            // A quotient that is no integer counts as step 0: NaN and +infinity keep 1, and
            // -infinity (measured) or a scale too negative to index keep 0.95.
            Assert.Equal(1f, ScalableBufferManager.Quantize(float.NaN));
            Assert.Equal(1f, ScalableBufferManager.Quantize(float.PositiveInfinity));
            Assert.Equal(1f, ScalableBufferManager.Quantize(3e9f));
            Assert.Equal(0.95f, ScalableBufferManager.Quantize(float.NegativeInfinity));
            Assert.Equal(0.95f, ScalableBufferManager.Quantize(-1e30f));
            Assert.Equal(0.0500000119f, ScalableBufferManager.Quantize(-1e6f));
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
