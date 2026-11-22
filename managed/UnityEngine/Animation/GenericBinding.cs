using System.Runtime.InteropServices;

namespace UnityEngine.Animations
{
    [System.Flags]
    internal enum Flags
    {
        kNone = 0,
        kDiscrete = 1,
        kPPtr = 2,
        kSerializeReference = 4,
        kPhantom = 8,
        kUnknown = 16,
    }

    /// <summary>A compiled property binding targeting an animated object and serialized property
    /// by CRC32 hashes, component class id, optional script instance id and curve flags
    /// (Unity 6000.0.71f1, tests/projects/animrest <c>generic.*</c>,
    /// tests/projects/animatorapi <c>util.generic</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly partial struct GenericBinding
    {
        readonly uint m_Path;
        readonly uint m_PropertyName;
        readonly int m_ScriptInstanceID;
        readonly int m_TypeID;
        readonly byte m_CustomType;
        internal readonly Flags m_Flags;

        internal GenericBinding(uint pathHash, uint propertyNameHash, int scriptInstanceID, int typeID, byte customTypeID, Flags flags)
        {
            m_Path = pathHash;
            m_PropertyName = propertyNameHash;
            m_ScriptInstanceID = scriptInstanceID;
            m_TypeID = typeID;
            m_CustomType = customTypeID;
            m_Flags = flags;
        }

        public bool isObjectReference => (m_Flags & Flags.kPPtr) != 0;
        public bool isDiscrete => (m_Flags & Flags.kDiscrete) != 0;
        public bool isSerializeReference => (m_Flags & Flags.kSerializeReference) != 0;
        public uint transformPathHash => m_Path;
        public uint propertyNameHash => m_PropertyName;
        public int scriptInstanceID => m_ScriptInstanceID;
        public int typeID => m_TypeID;
        public byte customTypeID => m_CustomType;
    }
}
