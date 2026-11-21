using System;
using System.Runtime.InteropServices;

namespace Unbound.Runtime
{
    /// <summary>
    /// The GPU as the Editor's Vulkan device names it (measured on the Unity 6000.0.71f1 Linux
    /// Editor with an NVIDIA RTX 4060): the physical device's name; the vendor named by its PCI
    /// id ("NVIDIA"); "Vulkan 1.1.0 [major.minor.patch (0xRAW)]", the API version the Editor asks
    /// for and the driver version decoded as a Vulkan version; the PCI ids; and the sum of the
    /// device-local memory heaps in MiB. The discrete GPU is the one read, else the first.
    /// </summary>
    internal static class VulkanIdentity
    {
        const int VK_SUCCESS = 0;
        const int VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1;
        const int VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU = 2;
        const uint VK_MEMORY_HEAP_DEVICE_LOCAL_BIT = 1;

        // VkPhysicalDeviceProperties: apiVersion, driverVersion, vendorID, deviceID, deviceType,
        // deviceName[256], then limits; read by offset from a buffer larger than the struct.
        const int PropertiesSize = 1024;
        const int DriverVersionOffset = 4, VendorIdOffset = 8, DeviceIdOffset = 12, DeviceTypeOffset = 16, NameOffset = 20, NameLength = 256;
        // VkPhysicalDeviceMemoryProperties: memoryTypeCount, memoryTypes[32] (8 bytes each),
        // memoryHeapCount, memoryHeaps[16] ({ VkDeviceSize size; flags }: 16 bytes, 8-aligned).
        const int MemoryPropertiesSize = 520, HeapCountOffset = 260, HeapsOffset = 264, HeapStride = 16;

        [StructLayout(LayoutKind.Sequential)]
        struct InstanceCreateInfo
        {
            public int sType;
            public IntPtr pNext;
            public uint flags;
            public IntPtr pApplicationInfo;
            public uint enabledLayerCount;
            public IntPtr ppEnabledLayerNames;
            public uint enabledExtensionCount;
            public IntPtr ppEnabledExtensionNames;
        }

        [DllImport("libvulkan.so.1")] static extern unsafe int vkCreateInstance(InstanceCreateInfo* info, IntPtr allocator, IntPtr* instance);
        [DllImport("libvulkan.so.1")] static extern void vkDestroyInstance(IntPtr instance, IntPtr allocator);
        [DllImport("libvulkan.so.1")] static extern unsafe int vkEnumeratePhysicalDevices(IntPtr instance, uint* count, IntPtr* devices);
        [DllImport("libvulkan.so.1")] static extern unsafe void vkGetPhysicalDeviceProperties(IntPtr device, byte* properties);
        [DllImport("libvulkan.so.1")] static extern unsafe void vkGetPhysicalDeviceMemoryProperties(IntPtr device, byte* properties);

        /// <summary>The Vulkan device's identity, null (reported) when Vulkan cannot be read.</summary>
        internal static unsafe MachineIdentity.Gpu Read()
        {
            try
            {
                var info = new InstanceCreateInfo { sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO };
                IntPtr instance;
                if (vkCreateInstance(&info, IntPtr.Zero, &instance) != VK_SUCCESS) return Fail("vkCreateInstance failed");
                try
                {
                    uint count = 0;
                    if (vkEnumeratePhysicalDevices(instance, &count, null) != VK_SUCCESS || count == 0) return Fail("Vulkan lists no GPU");
                    var devices = new IntPtr[count];
                    fixed (IntPtr* p = devices)
                        if (vkEnumeratePhysicalDevices(instance, &count, p) < VK_SUCCESS) return Fail("Vulkan lists no GPU");
                    var properties = new byte[PropertiesSize];
                    MachineIdentity.Gpu first = null;
                    for (int i = 0; i < count; i++)
                    {
                        fixed (byte* p = properties) vkGetPhysicalDeviceProperties(devices[i], p);
                        bool discrete = BitConverter.ToInt32(properties, DeviceTypeOffset) == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU;
                        if (!discrete && first != null) continue;
                        var gpu = Describe(devices[i], properties);
                        if (discrete) return gpu;
                        first = gpu;
                    }
                    return first;
                }
                finally { vkDestroyInstance(instance, IntPtr.Zero); }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return Fail("Vulkan identity read failed: " + e.Message);
            }
        }

        static unsafe MachineIdentity.Gpu Describe(IntPtr device, byte[] properties)
        {
            uint driver = BitConverter.ToUInt32(properties, DriverVersionOffset);
            int vendor = BitConverter.ToInt32(properties, VendorIdOffset);
            int nameEnd = Array.IndexOf(properties, (byte)0, NameOffset, NameLength);
            var memory = new byte[MemoryPropertiesSize];
            fixed (byte* p = memory) vkGetPhysicalDeviceMemoryProperties(device, p);
            long local = 0;
            uint heaps = Math.Min(BitConverter.ToUInt32(memory, HeapCountOffset), 16u);
            for (int h = 0; h < heaps; h++)
            {
                int at = HeapsOffset + h * HeapStride;
                if ((BitConverter.ToUInt32(memory, at + 8) & VK_MEMORY_HEAP_DEVICE_LOCAL_BIT) != 0)
                    local += BitConverter.ToInt64(memory, at);
            }
            return new MachineIdentity.Gpu
            {
                Name = System.Text.Encoding.UTF8.GetString(properties, NameOffset, (nameEnd < 0 ? NameOffset + NameLength : nameEnd) - NameOffset),
                Vendor = VendorName(vendor),
                Version = $"Vulkan 1.1.0 [{driver >> 22}.{(driver >> 12) & 0x3ff}.{driver & 0xfff} (0x{driver:x8})]",
                VendorId = vendor,
                DeviceId = BitConverter.ToInt32(properties, DeviceIdOffset),
                MemoryMb = (int)(local >> 20),
            };
        }

        /// <summary>Maps a PCI/Vulkan vendor ID to the vendor name reported by SystemInfo.graphicsDeviceVendor.
        /// Measured for NVIDIA (0x10de) on the Linux Editor (tests/projects/urp device-vulkan.jsonl);
        /// other vendor IDs and the unknown-ID fallback are unmeasured on this NVIDIA-only machine.</summary>
        internal static string VendorName(int vendor) => vendor switch
        {
            0x0000 => "REFERENCE",
            0x1002 => "ATI",
            0x1010 => "Imagination Technologies",
            0x1023 => "Trident",
            0x102b => "Matrox",
            0x1039 => "SIS",
            0x104a => "Imagination Technologies",
            0x10de => "NVIDIA",
            0x1106 => "VIA/S3",
            0x121a => "3dfx",
            0x13b5 => "ARM",
            0x1414 => "Microsoft",
            0x144d => "Samsung",
            0x15ad => "VMWare",
            0x18ca => "XGI",
            0x1ab8 => "Parallels",
            0x1d9e => "Anka",
            0x3d3d => "3dLabs",
            0x4144564e => "NVIDIA",
            0x4d4f4351 => "Qualcomm",
            0x5143 => "Qualcomm",
            0x5333 => "S3",
            0x8086 => "Intel",
            0x80ee => "VirtualBox",
            _ => $"Unknown (ID={vendor:x})",
        };

        static MachineIdentity.Gpu Fail(string why)
        {
            Stubs.Ignored("SystemInfo graphics device", $"is not read ({why}): the device name, vendor, version, ids and memory answer placeholders");
            return null;
        }
    }
}
