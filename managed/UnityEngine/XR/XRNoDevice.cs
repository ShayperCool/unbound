using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine.XR
{
    // The XR API without an XR device or plug-in, answering as the Editor does without an XR
    // provider (measured: tests/projects/xr): XR off, no input devices, nodes or subsystems; the
    // Editor's built-in MockHMD loads by name (its eye textures, scales and view settings) but
    // never becomes active.

    public static partial class XRSettings
    {
        public enum StereoRenderingMode
        {
            MultiPass = 0,
            SinglePass = 1,
            SinglePassInstanced = 2,
            SinglePassMultiview = 3,
        }

        static string s_LoadedDevice = "";
        static float s_EyeTextureResolutionScale, s_RenderViewportScale, s_OcclusionMaskScale;
        static bool s_UseOcclusionMesh, s_ShowDeviceView;
        static GameViewRenderMode s_GameViewRenderMode = GameViewRenderMode.None;
        static TextureDimension s_DeviceEyeTextureDimension = TextureDimension.None;
        static int s_BaseEyeWidth, s_BaseEyeHeight;

        /// <summary>XR stays off: no device can start.</summary>
        [Unbound.EditorFaithful("measured: tests/projects/xr (no XR device or provider)")]
        public static bool enabled { get => false; set { } }
        [Unbound.EditorFaithful("measured: tests/projects/xr (no XR device or provider)")]
        public static bool isDeviceActive => false;
        public static string loadedDeviceName => s_LoadedDevice;
        public static string[] supportedDevices => new[] { "None" };
        public static GameViewRenderMode gameViewRenderMode { get => s_GameViewRenderMode; set => s_GameViewRenderMode = value; }
        public static bool showDeviceView { get => s_ShowDeviceView; set => s_ShowDeviceView = value; }
        public static float eyeTextureResolutionScale { get => s_EyeTextureResolutionScale; set => s_EyeTextureResolutionScale = value; }
        public static int eyeTextureWidth => (int)(s_BaseEyeWidth * s_EyeTextureResolutionScale);
        public static int eyeTextureHeight => (int)(s_BaseEyeHeight * s_EyeTextureResolutionScale);
        public static RenderTextureDescriptor eyeTextureDesc
        {
            get
            {
                // Without a device the descriptor is the default one (256 x 256).
                int w = eyeTextureWidth, h = eyeTextureHeight;
                var desc = new RenderTextureDescriptor(w > 0 ? w : 256, h > 0 ? h : 256, GraphicsFormat.R8G8B8A8_UNorm, 0);
                desc.dimension = TextureDimension.Tex2D;
                desc.msaaSamples = 1;
                desc.vrUsage = VRTextureUsage.None;
                return desc;
            }
        }
        public static TextureDimension deviceEyeTextureDimension => s_DeviceEyeTextureDimension;
        public static float renderViewportScale { get => s_RenderViewportScale; set => s_RenderViewportScale = value; }
        public static float occlusionMaskScale { get => s_OcclusionMaskScale; set => s_OcclusionMaskScale = value; }
        public static bool useOcclusionMesh { get => s_UseOcclusionMesh; set => s_UseOcclusionMesh = value; }
        public static StereoRenderingMode stereoRenderingMode => StereoRenderingMode.MultiPass;

        public static void LoadDeviceByName(string deviceName) => LoadDeviceByName(new[] { deviceName });

        /// <summary>The first name the Editor knows (its built-in MockHMD) loads; it is never active.</summary>
        public static void LoadDeviceByName(string[] prioritizedDeviceNameList)
        {
            if (prioritizedDeviceNameList == null) return;
            foreach (var name in prioritizedDeviceNameList)
            {
                if (!string.Equals(name, "MockHMD", StringComparison.OrdinalIgnoreCase)) continue;
                s_LoadedDevice = "MockHMD";
                s_BaseEyeWidth = 1512;
                s_BaseEyeHeight = 1680;
                s_EyeTextureResolutionScale = 1f;
                s_RenderViewportScale = 1f;
                s_OcclusionMaskScale = 1f;
                s_UseOcclusionMesh = true;
                s_ShowDeviceView = true;
                s_GameViewRenderMode = GameViewRenderMode.BothEyes;
                s_DeviceEyeTextureDimension = TextureDimension.Tex2D;
                XRDevice.RaiseDeviceLoaded(s_LoadedDevice);
                return;
            }
        }
    }

    [Unbound.EditorFaithful("measured: tests/projects/xr (no XR device or provider)")]
    public static partial class XRDevice
    {
        static float s_FovZoomFactor = 1f;

        public static float refreshRate => 0f;
        public static float fovZoomFactor { get => s_FovZoomFactor; set => s_FovZoomFactor = value; }
        public static event Action<string> deviceLoaded;
        internal static void RaiseDeviceLoaded(string name) => deviceLoaded?.Invoke(name);
        public static IntPtr GetNativePtr() => IntPtr.Zero;
        public static TrackingSpaceType GetTrackingSpaceType() => TrackingSpaceType.Stationary;
        public static bool SetTrackingSpaceType(TrackingSpaceType trackingSpaceType) => false;
        public static void DisableAutoXRCameraTracking(Camera camera, bool disabled) { }
        public static void UpdateEyeTextureMSAASetting() { }
    }

    public static partial class XRStats
    {
        public static bool TryGetGPUTimeLastFrame(out float gpuTimeLastFrame) { gpuTimeLastFrame = 0f; return false; }
        public static bool TryGetDroppedFrameCount(out int droppedFrameCount) { droppedFrameCount = 0; return false; }
        public static bool TryGetFramePresentCount(out int framePresentCount) { framePresentCount = 0; return false; }
    }

    [Unbound.EditorFaithful("measured: tests/projects/xr (no XR device or provider)")]
    public partial class InputDevices
    {
        public static InputDevice GetDeviceAtXRNode(XRNode node) => default;
        public static void GetDevicesAtXRNode(XRNode node, List<InputDevice> inputDevices) => Clear(inputDevices);
        public static void GetDevices(List<InputDevice> inputDevices) => Clear(inputDevices);
        public static void GetDevicesWithCharacteristics(InputDeviceCharacteristics desiredCharacteristics, List<InputDevice> inputDevices) => Clear(inputDevices);

        [Obsolete("This API has been marked as deprecated and will be removed in future versions. Please use InputDevices.GetDevicesWithCharacteristics instead.")]
        public static void GetDevicesWithRole(InputDeviceRole role, List<InputDevice> inputDevices) => Clear(inputDevices);

        static void Clear(List<InputDevice> list)
        {
            if (list == null) throw new ArgumentNullException("inputDevices");
            list.Clear();
        }

        public static event Action<InputDevice> deviceConnected { add { } remove { } }
        public static event Action<InputDevice> deviceDisconnected { add { } remove { } }
        public static event Action<InputDevice> deviceConfigChanged { add { } remove { } }
    }

    [Unbound.EditorFaithful("measured: tests/projects/xr (no XR device or provider)")]
    public partial struct InputDevice : IEquatable<InputDevice>
    {
        ulong m_DeviceId;
        bool m_Initialized;

        internal InputDevice(ulong deviceId)
        {
            m_DeviceId = deviceId;
            m_Initialized = true;
        }

        // No device exists: every query fails.
        public XRInputSubsystem subsystem => null;
        public bool isValid => false;
        public string name => null;
        [Obsolete("This API has been marked as deprecated and will be removed in future versions. Please use InputDevice.characteristics instead.")]
        public InputDeviceRole role => InputDeviceRole.Unknown;
        public string manufacturer => null;
        public string serialNumber => null;
        public InputDeviceCharacteristics characteristics => InputDeviceCharacteristics.None;

        public bool SendHapticImpulse(uint channel, float amplitude, float duration = 1f) => false;
        public bool SendHapticBuffer(uint channel, byte[] buffer) => false;
        public bool TryGetHapticCapabilities(out HapticCapabilities capabilities) { capabilities = default; return false; }
        public void StopHaptics() { }
        public bool TryGetFeatureUsages(List<InputFeatureUsage> featureUsages) => false;
        public bool TryGetFeatureValue(InputFeatureUsage<bool> usage, out bool value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<uint> usage, out uint value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<float> usage, out float value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Vector2> usage, out Vector2 value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Vector3> usage, out Vector3 value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Quaternion> usage, out Quaternion value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Hand> usage, out Hand value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Bone> usage, out Bone value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Eyes> usage, out Eyes value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<byte[]> usage, byte[] value) => false;
        public bool TryGetFeatureValue(InputFeatureUsage<InputTrackingState> usage, out InputTrackingState value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<bool> usage, DateTime time, out bool value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<uint> usage, DateTime time, out uint value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<float> usage, DateTime time, out float value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Vector2> usage, DateTime time, out Vector2 value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Vector3> usage, DateTime time, out Vector3 value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<Quaternion> usage, DateTime time, out Quaternion value) { value = default; return false; }
        public bool TryGetFeatureValue(InputFeatureUsage<InputTrackingState> usage, DateTime time, out InputTrackingState value) { value = default; return false; }

        public override bool Equals(object obj) => obj is InputDevice other && Equals(other);
        public bool Equals(InputDevice other) => m_DeviceId == other.m_DeviceId;
        public override int GetHashCode() => m_DeviceId.GetHashCode();
        public static bool operator ==(InputDevice a, InputDevice b) => a.Equals(b);
        public static bool operator !=(InputDevice a, InputDevice b) => !a.Equals(b);
    }

    public partial struct InputFeatureUsage : IEquatable<InputFeatureUsage>
    {
        internal string m_Name;
        internal InputFeatureType m_InternalType;

        internal InputFeatureUsage(string name, InputFeatureType type)
        {
            m_Name = name;
            m_InternalType = type;
        }

        public string name { get => m_Name; internal set => m_Name = value; }
        internal InputFeatureType internalType { get => m_InternalType; set => m_InternalType = value; }

        public Type type => m_InternalType switch
        {
            InputFeatureType.Custom => typeof(byte[]),
            InputFeatureType.Binary => typeof(bool),
            InputFeatureType.DiscreteStates => typeof(uint),
            InputFeatureType.Axis1D => typeof(float),
            InputFeatureType.Axis2D => typeof(Vector2),
            InputFeatureType.Axis3D => typeof(Vector3),
            InputFeatureType.Rotation => typeof(Quaternion),
            InputFeatureType.Hand => typeof(Hand),
            InputFeatureType.Bone => typeof(Bone),
            InputFeatureType.Eyes => typeof(Eyes),
            _ => throw new InvalidCastException("No valid managed type for unknown native type."),
        };

        public InputFeatureUsage<T> As<T>()
        {
            if (type != typeof(T)) throw new ArgumentException("InputFeatureUsage type does not match out variable type.");
            return new InputFeatureUsage<T>(name);
        }

        public override bool Equals(object obj) => obj is InputFeatureUsage other && Equals(other);
        public bool Equals(InputFeatureUsage other) => name == other.name && internalType == other.internalType;
        public override int GetHashCode() => (name ?? "").GetHashCode() ^ ((int)internalType << 1);
        public static bool operator ==(InputFeatureUsage a, InputFeatureUsage b) => a.Equals(b);
        public static bool operator !=(InputFeatureUsage a, InputFeatureUsage b) => !a.Equals(b);
    }

    public partial struct InputFeatureUsage<T> : IEquatable<InputFeatureUsage<T>>
    {
        public string name { get; set; }

        public InputFeatureUsage(string usageName) => name = usageName;

        public override bool Equals(object obj) => obj is InputFeatureUsage<T> other && Equals(other);
        public bool Equals(InputFeatureUsage<T> other) => name == other.name;
        public override int GetHashCode() => (name ?? "").GetHashCode();
        public static bool operator ==(InputFeatureUsage<T> a, InputFeatureUsage<T> b) => a.Equals(b);
        public static bool operator !=(InputFeatureUsage<T> a, InputFeatureUsage<T> b) => !a.Equals(b);

        static InputFeatureType InternalType
        {
            get
            {
                var t = typeof(T);
                if (t == typeof(bool)) return InputFeatureType.Binary;
                if (t == typeof(uint)) return InputFeatureType.DiscreteStates;
                if (t == typeof(float)) return InputFeatureType.Axis1D;
                if (t == typeof(Vector2)) return InputFeatureType.Axis2D;
                if (t == typeof(Vector3)) return InputFeatureType.Axis3D;
                if (t == typeof(Quaternion)) return InputFeatureType.Rotation;
                if (t == typeof(Hand)) return InputFeatureType.Hand;
                if (t == typeof(Bone)) return InputFeatureType.Bone;
                if (t == typeof(Eyes)) return InputFeatureType.Eyes;
                if (t == typeof(byte[])) return InputFeatureType.Custom;
                if (t.IsEnum) return InputFeatureType.DiscreteStates;
                throw new InvalidCastException($"No valid InputFeatureType for {t.Name}.");
            }
        }

        public static explicit operator InputFeatureUsage(InputFeatureUsage<T> self) => new InputFeatureUsage(self.name, InternalType);
    }

    /// <summary>The usages Unity defines, with its names.</summary>
    public static partial class CommonUsages
    {
        public static InputFeatureUsage<bool> isTracked = new InputFeatureUsage<bool>("IsTracked");
        public static InputFeatureUsage<bool> primaryButton = new InputFeatureUsage<bool>("PrimaryButton");
        public static InputFeatureUsage<bool> primaryTouch = new InputFeatureUsage<bool>("PrimaryTouch");
        public static InputFeatureUsage<bool> secondaryButton = new InputFeatureUsage<bool>("SecondaryButton");
        public static InputFeatureUsage<bool> secondaryTouch = new InputFeatureUsage<bool>("SecondaryTouch");
        public static InputFeatureUsage<bool> gripButton = new InputFeatureUsage<bool>("GripButton");
        public static InputFeatureUsage<bool> triggerButton = new InputFeatureUsage<bool>("TriggerButton");
        public static InputFeatureUsage<bool> menuButton = new InputFeatureUsage<bool>("MenuButton");
        public static InputFeatureUsage<bool> primary2DAxisClick = new InputFeatureUsage<bool>("Primary2DAxisClick");
        public static InputFeatureUsage<bool> primary2DAxisTouch = new InputFeatureUsage<bool>("Primary2DAxisTouch");
        public static InputFeatureUsage<bool> secondary2DAxisClick = new InputFeatureUsage<bool>("Secondary2DAxisClick");
        public static InputFeatureUsage<bool> secondary2DAxisTouch = new InputFeatureUsage<bool>("Secondary2DAxisTouch");
        public static InputFeatureUsage<bool> userPresence = new InputFeatureUsage<bool>("UserPresence");
        public static InputFeatureUsage<InputTrackingState> trackingState = new InputFeatureUsage<InputTrackingState>("TrackingState");
        public static InputFeatureUsage<float> batteryLevel = new InputFeatureUsage<float>("BatteryLevel");
        public static InputFeatureUsage<float> trigger = new InputFeatureUsage<float>("Trigger");
        public static InputFeatureUsage<float> grip = new InputFeatureUsage<float>("Grip");
        public static InputFeatureUsage<Vector2> primary2DAxis = new InputFeatureUsage<Vector2>("Primary2DAxis");
        public static InputFeatureUsage<Vector2> secondary2DAxis = new InputFeatureUsage<Vector2>("Secondary2DAxis");
        public static InputFeatureUsage<Vector3> devicePosition = new InputFeatureUsage<Vector3>("DevicePosition");
        public static InputFeatureUsage<Vector3> leftEyePosition = new InputFeatureUsage<Vector3>("LeftEyePosition");
        public static InputFeatureUsage<Vector3> rightEyePosition = new InputFeatureUsage<Vector3>("RightEyePosition");
        public static InputFeatureUsage<Vector3> centerEyePosition = new InputFeatureUsage<Vector3>("CenterEyePosition");
        public static InputFeatureUsage<Vector3> colorCameraPosition = new InputFeatureUsage<Vector3>("CameraPosition");
        public static InputFeatureUsage<Vector3> deviceVelocity = new InputFeatureUsage<Vector3>("DeviceVelocity");
        public static InputFeatureUsage<Vector3> deviceAngularVelocity = new InputFeatureUsage<Vector3>("DeviceAngularVelocity");
        public static InputFeatureUsage<Vector3> leftEyeVelocity = new InputFeatureUsage<Vector3>("LeftEyeVelocity");
        public static InputFeatureUsage<Vector3> leftEyeAngularVelocity = new InputFeatureUsage<Vector3>("LeftEyeAngularVelocity");
        public static InputFeatureUsage<Vector3> rightEyeVelocity = new InputFeatureUsage<Vector3>("RightEyeVelocity");
        public static InputFeatureUsage<Vector3> rightEyeAngularVelocity = new InputFeatureUsage<Vector3>("RightEyeAngularVelocity");
        public static InputFeatureUsage<Vector3> centerEyeVelocity = new InputFeatureUsage<Vector3>("CenterEyeVelocity");
        public static InputFeatureUsage<Vector3> centerEyeAngularVelocity = new InputFeatureUsage<Vector3>("CenterEyeAngularVelocity");
        public static InputFeatureUsage<Vector3> colorCameraVelocity = new InputFeatureUsage<Vector3>("CameraVelocity");
        public static InputFeatureUsage<Vector3> colorCameraAngularVelocity = new InputFeatureUsage<Vector3>("CameraAngularVelocity");
        public static InputFeatureUsage<Vector3> deviceAcceleration = new InputFeatureUsage<Vector3>("DeviceAcceleration");
        public static InputFeatureUsage<Vector3> deviceAngularAcceleration = new InputFeatureUsage<Vector3>("DeviceAngularAcceleration");
        public static InputFeatureUsage<Vector3> leftEyeAcceleration = new InputFeatureUsage<Vector3>("LeftEyeAcceleration");
        public static InputFeatureUsage<Vector3> leftEyeAngularAcceleration = new InputFeatureUsage<Vector3>("LeftEyeAngularAcceleration");
        public static InputFeatureUsage<Vector3> rightEyeAcceleration = new InputFeatureUsage<Vector3>("RightEyeAcceleration");
        public static InputFeatureUsage<Vector3> rightEyeAngularAcceleration = new InputFeatureUsage<Vector3>("RightEyeAngularAcceleration");
        public static InputFeatureUsage<Vector3> centerEyeAcceleration = new InputFeatureUsage<Vector3>("CenterEyeAcceleration");
        public static InputFeatureUsage<Vector3> centerEyeAngularAcceleration = new InputFeatureUsage<Vector3>("CenterEyeAngularAcceleration");
        public static InputFeatureUsage<Vector3> colorCameraAcceleration = new InputFeatureUsage<Vector3>("CameraAcceleration");
        public static InputFeatureUsage<Vector3> colorCameraAngularAcceleration = new InputFeatureUsage<Vector3>("CameraAngularAcceleration");
        public static InputFeatureUsage<Quaternion> deviceRotation = new InputFeatureUsage<Quaternion>("DeviceRotation");
        public static InputFeatureUsage<Quaternion> leftEyeRotation = new InputFeatureUsage<Quaternion>("LeftEyeRotation");
        public static InputFeatureUsage<Quaternion> rightEyeRotation = new InputFeatureUsage<Quaternion>("RightEyeRotation");
        public static InputFeatureUsage<Quaternion> centerEyeRotation = new InputFeatureUsage<Quaternion>("CenterEyeRotation");
        public static InputFeatureUsage<Quaternion> colorCameraRotation = new InputFeatureUsage<Quaternion>("CameraRotation");
        public static InputFeatureUsage<Hand> handData = new InputFeatureUsage<Hand>("HandData");
        public static InputFeatureUsage<Eyes> eyesData = new InputFeatureUsage<Eyes>("EyesData");
        public static InputFeatureUsage<Vector2> dPad = new InputFeatureUsage<Vector2>("DPad");
        public static InputFeatureUsage<float> indexFinger = new InputFeatureUsage<float>("IndexFinger");
        public static InputFeatureUsage<float> middleFinger = new InputFeatureUsage<float>("MiddleFinger");
        public static InputFeatureUsage<float> ringFinger = new InputFeatureUsage<float>("RingFinger");
        public static InputFeatureUsage<float> pinkyFinger = new InputFeatureUsage<float>("PinkyFinger");
        public static InputFeatureUsage<bool> thumbrest = new InputFeatureUsage<bool>("Thumbrest");
        public static InputFeatureUsage<float> indexTouch = new InputFeatureUsage<float>("IndexTouch");
        public static InputFeatureUsage<float> thumbTouch = new InputFeatureUsage<float>("ThumbTouch");
    }

    public static partial class InputTracking
    {
        public static void GetNodeStates(List<XRNodeState> nodeStates)
        {
            if (nodeStates == null) throw new ArgumentNullException(nameof(nodeStates));
            nodeStates.Clear();
        }
    }
}

namespace UnityEngine.XR
{
    // The module's enums (Unity's values) and its structs, whose members the stubs give.
    public enum GameViewRenderMode
    {
        None = 0,
        LeftEye = 1,
        RightEye = 2,
        BothEyes = 3,
        OcclusionMesh = 4,
        MotionVectors = 5,
    }
    public enum TrackingSpaceType
    {
        Stationary = 0,
        RoomScale = 1,
    }
    [global::System.Flags]
    public enum InputDeviceCharacteristics : uint
    {
        None = 0U,
        HeadMounted = 1U,
        Camera = 2U,
        HeldInHand = 4U,
        HandTracking = 8U,
        EyeTracking = 16U,
        TrackedDevice = 32U,
        Controller = 64U,
        TrackingReference = 128U,
        Left = 256U,
        Right = 512U,
        Simulated6DOF = 1024U,
    }
    public enum InputDeviceRole : uint
    {
        Unknown = 0U,
        Generic = 1U,
        LeftHanded = 2U,
        RightHanded = 3U,
        GameController = 4U,
        TrackingReference = 5U,
        HardwareTracker = 6U,
        LegacyController = 7U,
    }
    internal enum InputFeatureType : uint
    {
        Custom = 0U,
        Binary = 1U,
        DiscreteStates = 2U,
        Axis1D = 3U,
        Axis2D = 4U,
        Axis3D = 5U,
        Rotation = 6U,
        Hand = 7U,
        Bone = 8U,
        Eyes = 9U,
        kUnityXRInputFeatureTypeInvalid = 4294967295U,
    }
    [global::System.Flags]
    public enum InputTrackingState : uint
    {
        None = 0U,
        Position = 1U,
        Rotation = 2U,
        Velocity = 4U,
        AngularVelocity = 8U,
        Acceleration = 16U,
        AngularAcceleration = 32U,
        All = 63U,
    }
    public enum XRNode
    {
        LeftEye = 0,
        RightEye = 1,
        CenterEye = 2,
        Head = 3,
        LeftHand = 4,
        RightHand = 5,
        GameController = 6,
        TrackingReference = 7,
        HardwareTracker = 8,
    }

    public partial struct Hand { }
    public partial struct Bone { }
    public partial struct Eyes { }
    public partial struct HapticCapabilities { }
    public partial struct XRNodeState { }
    public partial class XRInputSubsystem : IntegratedSubsystem<XRInputSubsystemDescriptor> { }
    public partial class XRInputSubsystemDescriptor : IntegratedSubsystemDescriptor<XRInputSubsystem> { }
}

namespace UnityEngine
{
    // Declared here so the subsystem manager compiles on its own; the stubs give the members.
    public partial interface ISubsystem { }
    public partial interface ISubsystemDescriptor { }
    internal partial interface ISubsystemDescriptorImpl : ISubsystemDescriptor { }
    public partial class IntegratedSubsystem : ISubsystem { }
    public partial class IntegratedSubsystem<TSubsystemDescriptor> : IntegratedSubsystem where TSubsystemDescriptor : ISubsystemDescriptor { }
    public abstract partial class IntegratedSubsystemDescriptor : ISubsystemDescriptorImpl, ISubsystemDescriptor { }
    public partial class IntegratedSubsystemDescriptor<TSubsystem> : IntegratedSubsystemDescriptor where TSubsystem : IntegratedSubsystem { }

    /// <summary>No subsystem providers: every query comes back empty.</summary>
    public static partial class SubsystemManager
    {
        public static void GetAllSubsystemDescriptors(List<ISubsystemDescriptor> descriptors) => descriptors?.Clear();
        public static void GetSubsystemDescriptors<T>(List<T> descriptors) where T : ISubsystemDescriptor => descriptors?.Clear();
        public static void GetSubsystems<T>(List<T> subsystems) where T : ISubsystem => subsystems?.Clear();

        [Obsolete("Use GetSubsystems instead. (UnityUpgradable) -> GetSubsystems<T>(*)")]
        public static void GetInstances<T>(List<T> subsystems) where T : ISubsystem => subsystems?.Clear();
    }
}
