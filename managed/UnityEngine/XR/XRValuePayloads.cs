using System;

namespace UnityEngine.XR
{
    // Shared identity for feature handles. Provider queries use the generated API separately.
    internal static class XRFeatureIdentity
    {
        internal static bool Equals(ulong device, uint feature, ulong otherDevice, uint otherFeature)
            => device == otherDevice && feature == otherFeature;
        internal static int Hash(ulong device, uint feature)
            => device.GetHashCode() ^ (feature.GetHashCode() << 1);
    }

    public partial struct Bone : IEquatable<Bone>
    {
        private ulong m_DeviceId;
        private uint m_FeatureIndex;
        internal ulong deviceId => m_DeviceId;
        internal uint featureIndex => m_FeatureIndex;

        public bool Equals(Bone other)
            => XRFeatureIdentity.Equals(m_DeviceId, m_FeatureIndex, other.m_DeviceId, other.m_FeatureIndex);
        public override bool Equals(object obj) => obj is Bone other && Equals(other);
        public override int GetHashCode() => XRFeatureIdentity.Hash(m_DeviceId, m_FeatureIndex);
        public static bool operator ==(Bone a, Bone b) => a.Equals(b);
        public static bool operator !=(Bone a, Bone b) => !a.Equals(b);
    }

    public partial struct Eyes : IEquatable<Eyes>
    {
        private ulong m_DeviceId;
        private uint m_FeatureIndex;
        internal ulong deviceId => m_DeviceId;
        internal uint featureIndex => m_FeatureIndex;

        public bool Equals(Eyes other)
            => XRFeatureIdentity.Equals(m_DeviceId, m_FeatureIndex, other.m_DeviceId, other.m_FeatureIndex);
        public override bool Equals(object obj) => obj is Eyes other && Equals(other);
        public override int GetHashCode() => XRFeatureIdentity.Hash(m_DeviceId, m_FeatureIndex);
        public static bool operator ==(Eyes a, Eyes b) => a.Equals(b);
        public static bool operator !=(Eyes a, Eyes b) => !a.Equals(b);
    }

    public partial struct Hand : IEquatable<Hand>
    {
        private ulong m_DeviceId;
        private uint m_FeatureIndex;
        internal ulong deviceId => m_DeviceId;
        internal uint featureIndex => m_FeatureIndex;

        public bool Equals(Hand other)
            => XRFeatureIdentity.Equals(m_DeviceId, m_FeatureIndex, other.m_DeviceId, other.m_FeatureIndex);
        public override bool Equals(object obj) => obj is Hand other && Equals(other);
        public override int GetHashCode() => XRFeatureIdentity.Hash(m_DeviceId, m_FeatureIndex);
        public static bool operator ==(Hand a, Hand b) => a.Equals(b);
        public static bool operator !=(Hand a, Hand b) => !a.Equals(b);
    }

    public partial struct HapticCapabilities : IEquatable<HapticCapabilities>
    {
        private uint m_NumChannels;
        private bool m_SupportsImpulse;
        private bool m_SupportsBuffer;
        private uint m_BufferFrequencyHz;
        private uint m_BufferMaxSize;
        private uint m_BufferOptimalSize;

        public uint numChannels { get => m_NumChannels; internal set => m_NumChannels = value; }
        public bool supportsImpulse { get => m_SupportsImpulse; internal set => m_SupportsImpulse = value; }
        public bool supportsBuffer { get => m_SupportsBuffer; internal set => m_SupportsBuffer = value; }
        public uint bufferFrequencyHz { get => m_BufferFrequencyHz; internal set => m_BufferFrequencyHz = value; }
        public uint bufferMaxSize { get => m_BufferMaxSize; internal set => m_BufferMaxSize = value; }
        public uint bufferOptimalSize { get => m_BufferOptimalSize; internal set => m_BufferOptimalSize = value; }

        public bool Equals(HapticCapabilities other)
            => m_NumChannels == other.m_NumChannels && m_SupportsImpulse == other.m_SupportsImpulse
                && m_SupportsBuffer == other.m_SupportsBuffer && m_BufferFrequencyHz == other.m_BufferFrequencyHz
                && m_BufferMaxSize == other.m_BufferMaxSize && m_BufferOptimalSize == other.m_BufferOptimalSize;
        public override bool Equals(object obj) => obj is HapticCapabilities other && Equals(other);
        // Selected component hash mixing, including signed right shifts; xrvalues checks high bits.
        public override int GetHashCode()
            => m_NumChannels.GetHashCode() ^ (m_SupportsImpulse.GetHashCode() << 1)
                ^ (m_SupportsBuffer.GetHashCode() >> 1) ^ (m_BufferFrequencyHz.GetHashCode() << 2)
                ^ (m_BufferMaxSize.GetHashCode() >> 2) ^ (m_BufferOptimalSize.GetHashCode() << 3);
        public static bool operator ==(HapticCapabilities a, HapticCapabilities b) => a.Equals(b);
        public static bool operator !=(HapticCapabilities a, HapticCapabilities b) => !a.Equals(b);
    }

    [Flags]
    internal enum AvailableTrackingData
    {
        None = 0,
        PositionAvailable = 1,
        RotationAvailable = 2,
        VelocityAvailable = 4,
        AngularVelocityAvailable = 8,
        AccelerationAvailable = 16,
        AngularAccelerationAvailable = 32,
    }

    public partial struct XRNodeState
    {
        private XRNode m_Type;
        private AvailableTrackingData m_AvailableFields;
        private Vector3 m_Position;
        private Quaternion m_Rotation;
        private Vector3 m_Velocity;
        private Vector3 m_AngularVelocity;
        private Vector3 m_Acceleration;
        private Vector3 m_AngularAcceleration;
        private int m_Tracked;
        private ulong m_UniqueID;

        public ulong uniqueID { get => m_UniqueID; set => m_UniqueID = value; }
        public XRNode nodeType { get => m_Type; set => m_Type = value; }
        public bool tracked { get => m_Tracked == 1; set => m_Tracked = value ? 1 : 0; }
        public Vector3 position { set { m_Position = value; m_AvailableFields |= AvailableTrackingData.PositionAvailable; } }
        public Quaternion rotation { set { m_Rotation = value; m_AvailableFields |= AvailableTrackingData.RotationAvailable; } }
        public Vector3 velocity { set { m_Velocity = value; m_AvailableFields |= AvailableTrackingData.VelocityAvailable; } }
        public Vector3 angularVelocity { set { m_AngularVelocity = value; m_AvailableFields |= AvailableTrackingData.AngularVelocityAvailable; } }
        public Vector3 acceleration { set { m_Acceleration = value; m_AvailableFields |= AvailableTrackingData.AccelerationAvailable; } }
        public Vector3 angularAcceleration { set { m_AngularAcceleration = value; m_AvailableFields |= AvailableTrackingData.AngularAccelerationAvailable; } }

        public bool TryGetPosition(out Vector3 position) => TryGet(m_Position, AvailableTrackingData.PositionAvailable, out position);
        public bool TryGetRotation(out Quaternion rotation) => TryGet(m_Rotation, AvailableTrackingData.RotationAvailable, out rotation);
        public bool TryGetVelocity(out Vector3 velocity) => TryGet(m_Velocity, AvailableTrackingData.VelocityAvailable, out velocity);
        public bool TryGetAngularVelocity(out Vector3 angularVelocity) => TryGet(m_AngularVelocity, AvailableTrackingData.AngularVelocityAvailable, out angularVelocity);
        public bool TryGetAcceleration(out Vector3 acceleration) => TryGet(m_Acceleration, AvailableTrackingData.AccelerationAvailable, out acceleration);
        public bool TryGetAngularAcceleration(out Vector3 angularAcceleration) => TryGet(m_AngularAcceleration, AvailableTrackingData.AngularAccelerationAvailable, out angularAcceleration);

        private bool TryGet(Vector3 value, AvailableTrackingData field, out Vector3 result)
        {
            bool available = (m_AvailableFields & field) != 0;
            result = available ? value : Vector3.zero;
            return available;
        }
        private bool TryGet(Quaternion value, AvailableTrackingData field, out Quaternion result)
        {
            bool available = (m_AvailableFields & field) != 0;
            result = available ? value : Quaternion.identity;
            return available;
        }
    }
}
