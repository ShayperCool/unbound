using System;
using Unbound.Runtime;
using UnityEditor.VFX;

namespace UnityEngine.VFX
{
    /// <summary>What an output event hands its listeners: the event's name and its attributes.</summary>
    public partial struct VFXOutputEventArgs
    {
        public int nameId { get; }
        public VFXEventAttribute eventAttribute { get; }

        public VFXOutputEventArgs(int nameId, VFXEventAttribute eventAttribute)
        {
            this.nameId = nameId;
            this.eventAttribute = eventAttribute;
        }
    }

    /// <summary>
    /// The attributes an event carries to the systems it starts: one value per attribute the
    /// asset's spawn contexts read, laid out as the compiled graph's event attribute buffer and
    /// starting at the graph's values. A read of a name the layout lacks, or with another type,
    /// answers the type's default; such a write does nothing (no message: Unity 6000.0.71f1,
    /// tests/projects/vfx). Not measured: one name with different types in two graphs (a custom
    /// attribute); a copy whose value does not fit this set stops there and says so.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public sealed partial class VFXEventAttribute : IDisposable
    {
        VisualEffectAsset m_Asset;
        VisualEffectCompiledData m_Data = VisualEffectCompiledData.Empty;
        VFXLayoutElementDesc[] m_Layout = Array.Empty<VFXLayoutElementDesc>();
        uint[] m_Words = Array.Empty<uint>();

        VFXEventAttribute() { }

        public VFXEventAttribute(VFXEventAttribute original)
        {
            if (original == null) throw new ArgumentNullException("VFXEventAttribute expect a non null attribute");
            Internal_InitFromEventAttribute(original);
        }

        internal static VFXEventAttribute Internal_InstanciateVFXEventAttribute(VisualEffectAsset vfxAsset)
        {
            var attribute = new VFXEventAttribute();
            attribute.Internal_InitFromAsset(vfxAsset);
            return attribute;
        }

        internal void Internal_InitFromAsset(VisualEffectAsset vfxAsset)
        {
            m_Asset = vfxAsset;
            m_Data = vfxAsset != null ? vfxAsset.m_Data : VisualEffectCompiledData.Empty;
            if (m_Data.CpuBuffers.Length == 0)
            {
                m_Layout = Array.Empty<VFXLayoutElementDesc>();
                m_Words = Array.Empty<uint>();
                return;
            }
            var buffer = m_Data.CpuBuffers[0];
            m_Layout = buffer.layout ?? Array.Empty<VFXLayoutElementDesc>();
            m_Words = new uint[buffer.stride];
            var initial = m_Data.CpuInitialWords[0];
            Array.Copy(initial, m_Words, Math.Min(initial.Length, m_Words.Length));
        }

        internal void Internal_InitFromEventAttribute(VFXEventAttribute vfxEventAttribute)
        {
            m_Asset = vfxEventAttribute.m_Asset;
            m_Data = vfxEventAttribute.m_Data;
            m_Layout = vfxEventAttribute.m_Layout;
            m_Words = (uint[])vfxEventAttribute.Words.Clone();
        }

        internal VisualEffectAsset vfxAsset => m_Asset;

        /// <summary>A disposed attribute set has no values: every later use throws
        /// NullReferenceException, as a use of a released native object does.</summary>
        public void Dispose() => m_Words = null;

        uint[] Words => m_Words ?? throw new NullReferenceException();

        /// <summary>Takes the values of another attribute set, attribute by attribute: each
        /// attribute of this layout takes the value the other set has under the same name, where
        /// it has one (as many words as the other's type has); the rest keep their values.</summary>
        public void CopyValuesFrom(VFXEventAttribute eventAttibute)
        {
            if (eventAttibute == null) throw new ArgumentNullException(nameof(eventAttibute));
            var from = eventAttibute.Words;
            var into = Words;
            if (ReferenceEquals(m_Layout, eventAttibute.m_Layout))
            {
                Array.Copy(from, into, Math.Min(from.Length, into.Length));
                return;
            }
            var names = eventAttibute.m_Data.EventAttributeIds;
            for (int i = 0; i < m_Layout.Length; i++)
            {
                int other = Array.IndexOf(names, m_Data.EventAttributeIds[i]);
                if (other < 0) continue;
                var source = eventAttibute.m_Layout[other];
                int words = VFXExpressionHelper.GetSizeOfType(source.type);
                int at = (int)m_Layout[i].offset.element, sourceAt = (int)source.offset.element;
                if (at + words > into.Length || sourceAt + words > from.Length)
                {
                    // The same name with a larger type in the other set than this set has room for.
                    Debug.LogError($"Unbound: VFXEventAttribute.CopyValuesFrom: '{m_Layout[i].name}' is a {source.type} in the other set and does not fit this one; the copy stopped there (two graphs giving one attribute name different types is not measured against the Editor).");
                    return;
                }
                Array.Copy(from, sourceAt, into, at, words);
            }
        }

        /// <summary>The first word of an attribute of a name and type; -1 without one.</summary>
        int Find(int nameID, VFXValueType type)
        {
            _ = Words;
            int index = Array.IndexOf(m_Data.EventAttributeIds, nameID);
            return index >= 0 && m_Layout[index].type == type ? (int)m_Layout[index].offset.element : -1;
        }

        uint Word(int nameID, VFXValueType type)
        {
            int at = Find(nameID, type);
            return at < 0 ? 0u : m_Words[at];
        }

        void SetWord(int nameID, VFXValueType type, uint value)
        {
            int at = Find(nameID, type);
            if (at >= 0) m_Words[at] = value;
        }

        Vector4 Vector(int nameID, VFXValueType type, int components)
        {
            int at = Find(nameID, type);
            var v = default(Vector4);
            for (int i = 0; at >= 0 && i < components; i++) v[i] = BitConverter.UInt32BitsToSingle(m_Words[at + i]);
            return v;
        }

        void SetVector(int nameID, VFXValueType type, Vector4 value, int components)
        {
            int at = Find(nameID, type);
            for (int i = 0; at >= 0 && i < components; i++) m_Words[at + i] = BitConverter.SingleToUInt32Bits(value[i]);
        }

        const int MatrixWords = 16;

        public bool HasBool(int nameID) => Find(nameID, VFXValueType.Boolean) >= 0;
        public bool HasInt(int nameID) => Find(nameID, VFXValueType.Int32) >= 0;
        public bool HasUint(int nameID) => Find(nameID, VFXValueType.Uint32) >= 0;
        public bool HasFloat(int nameID) => Find(nameID, VFXValueType.Float) >= 0;
        public bool HasVector2(int nameID) => Find(nameID, VFXValueType.Float2) >= 0;
        public bool HasVector3(int nameID) => Find(nameID, VFXValueType.Float3) >= 0;
        public bool HasVector4(int nameID) => Find(nameID, VFXValueType.Float4) >= 0;
        public bool HasMatrix4x4(int nameID) => Find(nameID, VFXValueType.Matrix4x4) >= 0;

        public void SetBool(int nameID, bool b) => SetWord(nameID, VFXValueType.Boolean, b ? 1u : 0u);
        public void SetInt(int nameID, int i) => SetWord(nameID, VFXValueType.Int32, unchecked((uint)i));
        public void SetUint(int nameID, uint i) => SetWord(nameID, VFXValueType.Uint32, i);
        public void SetFloat(int nameID, float f) => SetWord(nameID, VFXValueType.Float, BitConverter.SingleToUInt32Bits(f));
        public void SetVector2(int nameID, Vector2 v) => SetVector(nameID, VFXValueType.Float2, v, 2);
        public void SetVector3(int nameID, Vector3 v) => SetVector(nameID, VFXValueType.Float3, v, 3);
        public void SetVector4(int nameID, Vector4 v) => SetVector(nameID, VFXValueType.Float4, v, 4);

        public void SetMatrix4x4(int nameID, Matrix4x4 v)
        {
            int at = Find(nameID, VFXValueType.Matrix4x4);
            for (int i = 0; at >= 0 && i < MatrixWords; i++) m_Words[at + i] = BitConverter.SingleToUInt32Bits(v[i]);
        }

        public bool GetBool(int nameID) => Word(nameID, VFXValueType.Boolean) != 0;
        public int GetInt(int nameID) => unchecked((int)Word(nameID, VFXValueType.Int32));
        public uint GetUint(int nameID) => Word(nameID, VFXValueType.Uint32);
        public float GetFloat(int nameID) => BitConverter.UInt32BitsToSingle(Word(nameID, VFXValueType.Float));
        public Vector2 GetVector2(int nameID) => Vector(nameID, VFXValueType.Float2, 2);
        public Vector3 GetVector3(int nameID) => Vector(nameID, VFXValueType.Float3, 3);
        public Vector4 GetVector4(int nameID) => Vector(nameID, VFXValueType.Float4, 4);

        public Matrix4x4 GetMatrix4x4(int nameID)
        {
            int at = Find(nameID, VFXValueType.Matrix4x4);
            var m = default(Matrix4x4);
            for (int i = 0; at >= 0 && i < MatrixWords; i++) m[i] = BitConverter.UInt32BitsToSingle(m_Words[at + i]);
            return m;
        }

        public bool HasBool(string name) => HasBool(Shader.PropertyToID(name));
        public bool HasInt(string name) => HasInt(Shader.PropertyToID(name));
        public bool HasUint(string name) => HasUint(Shader.PropertyToID(name));
        public bool HasFloat(string name) => HasFloat(Shader.PropertyToID(name));
        public bool HasVector2(string name) => HasVector2(Shader.PropertyToID(name));
        public bool HasVector3(string name) => HasVector3(Shader.PropertyToID(name));
        public bool HasVector4(string name) => HasVector4(Shader.PropertyToID(name));
        public bool HasMatrix4x4(string name) => HasMatrix4x4(Shader.PropertyToID(name));
        public void SetBool(string name, bool b) => SetBool(Shader.PropertyToID(name), b);
        public void SetInt(string name, int i) => SetInt(Shader.PropertyToID(name), i);
        public void SetUint(string name, uint i) => SetUint(Shader.PropertyToID(name), i);
        public void SetFloat(string name, float f) => SetFloat(Shader.PropertyToID(name), f);
        public void SetVector2(string name, Vector2 v) => SetVector2(Shader.PropertyToID(name), v);
        public void SetVector3(string name, Vector3 v) => SetVector3(Shader.PropertyToID(name), v);
        public void SetVector4(string name, Vector4 v) => SetVector4(Shader.PropertyToID(name), v);
        public void SetMatrix4x4(string name, Matrix4x4 v) => SetMatrix4x4(Shader.PropertyToID(name), v);
        public bool GetBool(string name) => GetBool(Shader.PropertyToID(name));
        public int GetInt(string name) => GetInt(Shader.PropertyToID(name));
        public uint GetUint(string name) => GetUint(Shader.PropertyToID(name));
        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));
        public Vector2 GetVector2(string name) => GetVector2(Shader.PropertyToID(name));
        public Vector3 GetVector3(string name) => GetVector3(Shader.PropertyToID(name));
        public Vector4 GetVector4(string name) => GetVector4(Shader.PropertyToID(name));
        public Matrix4x4 GetMatrix4x4(string name) => GetMatrix4x4(Shader.PropertyToID(name));
    }
}
