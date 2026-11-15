using System;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// The expression values of one effect: a value per expression of its asset's compiled graph,
    /// laid out by <see cref="VisualEffectCompiledData.ValueOffsets"/>. Numbers, vectors, matrices
    /// and booleans are 32-bit words (a matrix column by column); textures, meshes, renderers,
    /// buffers, curves and gradients are objects. A new container holds the asset's constants.
    /// </summary>
    internal sealed class VisualEffectValues
    {
        readonly VisualEffectCompiledData m_Data;
        readonly uint[] m_Words;
        readonly object[] m_Objects;

        internal VisualEffectValues(VisualEffectCompiledData data)
        {
            m_Data = data;
            m_Words = new uint[data.WordCount];
            m_Objects = new object[data.ObjectCount];
            for (int i = 0; i < data.ConstantOf.Length; i++)
                if (data.ConstantOf[i] >= 0) LoadConstant(i);
        }

        /// <summary>Puts the asset's constant back as an expression's value.</summary>
        internal void LoadConstant(int expression)
        {
            int constant = m_Data.ConstantOf[expression], at = m_Data.ValueOffsets[expression];
            if (constant < 0 || at < 0) return;
            switch (m_Data.Values[constant])
            {
                case VFXExpressionValueContainerDesc<float> v: m_Words[at] = BitConverter.SingleToUInt32Bits(v.value); break;
                case VFXExpressionValueContainerDesc<int> v: m_Words[at] = unchecked((uint)v.value); break;
                case VFXExpressionValueContainerDesc<uint> v: m_Words[at] = v.value; break;
                case VFXExpressionValueContainerDesc<bool> v: m_Words[at] = v.value ? 1u : 0u; break;
                case VFXExpressionValueContainerDesc<Vector2> v: SetVector(expression, v.value, 2); break;
                case VFXExpressionValueContainerDesc<Vector3> v: SetVector(expression, v.value, 3); break;
                case VFXExpressionValueContainerDesc<Vector4> v: SetVector(expression, v.value, 4); break;
                case VFXExpressionValueContainerDesc<Matrix4x4> v: SetMatrix(expression, v.value); break;
                case VFXExpressionValueContainerDesc<AnimationCurve> v: m_Objects[at] = v.value; break;
                case VFXExpressionValueContainerDesc<Gradient> v: m_Objects[at] = v.value; break;
                case VFXExpressionValueContainerDesc<GraphicsBuffer> v: m_Objects[at] = v.value; break;
                case VFXExpressionObjectValueContainerDesc<Texture> v: m_Objects[at] = ObjectRegistry.FindById(v.instanceID); break;
                case VFXExpressionObjectValueContainerDesc<Mesh> v: m_Objects[at] = ObjectRegistry.FindById(v.instanceID); break;
                case VFXExpressionObjectValueContainerDesc<SkinnedMeshRenderer> v: m_Objects[at] = ObjectRegistry.FindById(v.instanceID); break;
            }
        }

        internal uint GetWord(int expression) => m_Words[m_Data.ValueOffsets[expression]];

        internal void SetWord(int expression, uint value) => m_Words[m_Data.ValueOffsets[expression]] = value;

        internal float GetFloat(int expression) => BitConverter.UInt32BitsToSingle(GetWord(expression));

        internal void SetFloat(int expression, float value) => SetWord(expression, BitConverter.SingleToUInt32Bits(value));

        /// <summary>The first <paramref name="components"/> components of a vector value.</summary>
        internal Vector4 GetVector(int expression, int components)
        {
            int at = m_Data.ValueOffsets[expression];
            var v = default(Vector4);
            for (int i = 0; i < components; i++) v[i] = BitConverter.UInt32BitsToSingle(m_Words[at + i]);
            return v;
        }

        internal void SetVector(int expression, Vector4 value, int components)
        {
            int at = m_Data.ValueOffsets[expression];
            for (int i = 0; i < components; i++) m_Words[at + i] = BitConverter.SingleToUInt32Bits(value[i]);
        }

        internal Matrix4x4 GetMatrix(int expression)
        {
            int at = m_Data.ValueOffsets[expression];
            var m = default(Matrix4x4);
            for (int i = 0; i < MatrixWords; i++) m[i] = BitConverter.UInt32BitsToSingle(m_Words[at + i]);
            return m;
        }

        internal void SetMatrix(int expression, Matrix4x4 value)
        {
            int at = m_Data.ValueOffsets[expression];
            for (int i = 0; i < MatrixWords; i++) m_Words[at + i] = BitConverter.SingleToUInt32Bits(value[i]);
        }

        const int MatrixWords = 16;

        internal object GetObject(int expression) => m_Objects[m_Data.ValueOffsets[expression]];

        internal void SetObject(int expression, object value) => m_Objects[m_Data.ValueOffsets[expression]] = value;
    }
}
