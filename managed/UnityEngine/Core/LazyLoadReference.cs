using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>A serialized reference to an asset kept as its instance id. Scenes and assets store
    /// it as an object reference (<c>{fileID, guid, type}</c>); the asset is looked up when
    /// <see cref="asset"/> is read. Only persistent objects can be assigned through
    /// <see cref="asset"/>, any id through <see cref="instanceID"/> (tests/projects/coreapis).</summary>
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public partial struct LazyLoadReference<T> where T : Object
    {
        const int kInstanceID_None = 0;

        [SerializeField] int m_InstanceID;

        public bool isSet => m_InstanceID != kInstanceID_None;

        public bool isBroken => m_InstanceID != kInstanceID_None && !Object.DoesObjectWithInstanceIDExist(m_InstanceID);

        public T asset
        {
            get => m_InstanceID == kInstanceID_None ? null : (T)Object.ForceLoadFromInstanceID(m_InstanceID);
            set => m_InstanceID = IdOf(value);
        }

        public int instanceID
        {
            get => m_InstanceID;
            set => m_InstanceID = value;
        }

        public LazyLoadReference(T asset) => m_InstanceID = IdOf(asset);

        public LazyLoadReference(int instanceID) => m_InstanceID = instanceID;

        public static implicit operator LazyLoadReference<T>(T asset) => new LazyLoadReference<T> { asset = asset };

        public static implicit operator LazyLoadReference<T>(int instanceID) => new LazyLoadReference<T> { instanceID = instanceID };

        static int IdOf(T asset)
        {
            if (asset == null) return kInstanceID_None;
            if (!Object.IsPersistent(asset))
                throw new ArgumentException("Object that does not belong to a persisted asset cannot be set as the target of a LazyLoadReference.");
            return asset.GetInstanceID();
        }
    }
}
