using System;
using System.Runtime.InteropServices;
using Unbound.Runtime;

namespace UnityEngineInternal.Input
{
    internal unsafe delegate void NativeUpdateCallback(NativeInputUpdateType updateType, NativeInputEventBuffer* buffer);

    internal enum NativeInputEventType
    {
        DeviceRemoved = 1146242381,
        DeviceConfigChanged = 1145259591,
        Text = 1413830740,
        State = 1398030676,
        Delta = 1145852993,
    }

    [Flags]
    internal enum NativeInputUpdateType
    {
        Dynamic = 1,
        Fixed = 2,
        BeforeRender = 4,
        Editor = 8,
        IgnoreFocus = int.MinValue,
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 20)]
    internal partial struct NativeInputEventBuffer
    {
        public const int structSize = 20;
        [FieldOffset(0)] public unsafe void* eventBuffer;
        [FieldOffset(8)] public int eventCount;
        [FieldOffset(12)] public int sizeInBytes;
        [FieldOffset(16)] public int capacityInBytes;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 20)]
    internal partial struct NativeInputEvent
    {
        public const int structSize = 20;
        [FieldOffset(0)] public NativeInputEventType type;
        [FieldOffset(4)] public ushort sizeInBytes;
        [FieldOffset(6)] public ushort deviceId;
        [FieldOffset(8)] public double time;
        [FieldOffset(16)] public int eventId;

        public NativeInputEvent(NativeInputEventType type, int sizeInBytes, int deviceId, double time)
        {
            this.type = type;
            this.sizeInBytes = (ushort)sizeInBytes;
            this.deviceId = (ushort)deviceId;
            this.time = time;
            eventId = 0;
        }
    }

    /// <summary>
    /// The engine side of the Input System package: Unity's managed part (callbacks the native
    /// input module raises) as Unity has it, the native part in <see cref="NativeInput"/>.
    /// </summary>
    internal partial class NativeInputSystem
    {
        public static NativeUpdateCallback onUpdate;
        public static Action<NativeInputUpdateType> onBeforeUpdate;
        public static Func<NativeInputUpdateType, bool> onShouldRunUpdate;
        static Action<int, string> s_OnDeviceDiscoveredCallback;

        static NativeInputSystem()
        {
            hasDeviceDiscoveredCallback = false;
        }

        public static Action<int, string> onDeviceDiscovered
        {
            get => s_OnDeviceDiscoveredCallback;
            set
            {
                s_OnDeviceDiscoveredCallback = value;
                hasDeviceDiscoveredCallback = s_OnDeviceDiscoveredCallback != null;
            }
        }

        internal static bool hasDeviceDiscoveredCallback
        {
            set => NativeInput.HasDiscoveryCallback = value;
        }

        public static double currentTime => NativeInput.CurrentTime;

        public static double currentTimeOffsetToRealtimeSinceStartup => NativeInput.TimeOffsetToRealtimeSinceStartup;

        internal static bool allowInputDeviceCreationFromEvents { get; set; }

        internal static bool normalizeScrollWheelDelta
        {
            get => NativeInput.NormalizeScrollWheelDelta;
            set => NativeInput.NormalizeScrollWheelDelta = value;
        }

        internal static void NotifyBeforeUpdate(NativeInputUpdateType updateType)
        {
            onBeforeUpdate?.Invoke(updateType);
        }

        internal static unsafe void NotifyUpdate(NativeInputUpdateType updateType, IntPtr eventBuffer)
        {
            var callback = onUpdate;
            var buffer = (NativeInputEventBuffer*)eventBuffer.ToPointer();
            if (callback == null)
            {
                buffer->eventCount = 0;
                buffer->sizeInBytes = 0;
            }
            else
            {
                callback(updateType, buffer);
            }
        }

        internal static void NotifyDeviceDiscovered(int deviceId, string deviceDescriptor)
        {
            s_OnDeviceDiscoveredCallback?.Invoke(deviceId, deviceDescriptor);
        }

        internal static void ShouldRunUpdate(NativeInputUpdateType updateType, out bool retval)
        {
            retval = onShouldRunUpdate?.Invoke(updateType) ?? true;
        }

        public static int AllocateDeviceId() => NativeInput.AllocateDeviceId();

        public static unsafe void QueueInputEvent<TInputEvent>(ref TInputEvent inputEvent) where TInputEvent : struct
        {
            QueueInputEvent((IntPtr)System.Runtime.CompilerServices.Unsafe.AsPointer(ref inputEvent));
        }

        public static void QueueInputEvent(IntPtr inputEvent) => NativeInput.QueueEvent(inputEvent);

        public static long IOCTL(int deviceId, int code, IntPtr data, int sizeInBytes) => NativeInput.DeviceCommand(deviceId, code, data, sizeInBytes);

        public static void SetPollingFrequency(float hertz) => NativeInput.PollingFrequency = hertz;

        public static void Update(NativeInputUpdateType updateType) => NativeInput.Update(updateType);

        /// <summary>Events queued while the player is in the background: none, the window's input
        /// is sampled with each frame.</summary>
        internal static ulong GetBackgroundEventBufferSize() => 0;

        [Unbound.EditorFaithful("obsolete: Unity made it a no-op (\"This is not needed any longer.\")")]
        [Obsolete("This is not needed any longer.")]
        public static void SetUpdateMask(NativeInputUpdateType mask)
        {
        }

        internal static float GetScrollWheelDeltaPerTick() => NativeInput.ScrollWheelDeltaPerTick;
    }
}
