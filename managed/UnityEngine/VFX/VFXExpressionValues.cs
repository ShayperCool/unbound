using System;
using Unbound.Runtime;
using UnityEditor.VFX;

namespace UnityEngine.VFX
{
    /// <summary>
    /// The inputs of a custom spawner block, by name: the values of the expressions the graph
    /// connects to them, as of the step being run. A name the block has no input for is an
    /// <see cref="ArgumentException"/>.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial class VFXExpressionValues
    {
        readonly VisualEffect m_Effect;
        readonly int[] m_Names, m_Expressions;

        VFXExpressionValues() { }

        internal VFXExpressionValues(VisualEffect effect, VFXMapping[] values)
        {
            m_Effect = effect;
            int count = values?.Length ?? 0;
            m_Names = new int[count];
            m_Expressions = new int[count];
            for (int i = 0; i < count; i++)
            {
                m_Names[i] = Shader.PropertyToID(values[i].name);
                m_Expressions[i] = values[i].index;
            }
        }

        /// <summary>The expression an input reads; an unknown name, or an input without a value,
        /// is an error.</summary>
        int Expression(int nameID)
        {
            if (m_Names != null)
            {
                int at = Array.IndexOf(m_Names, nameID);
                if (at >= 0 && m_Expressions[at] >= 0 && m_Effect.Data.ValueOffsets[m_Expressions[at]] >= 0) return m_Expressions[at];
            }
            throw new ArgumentException("Unable to retrieve value for : " + Shader.IdToName(nameID));
        }

        VisualEffectValues Values => m_Effect.Values;

        public bool GetBool(int nameID) => (Values.GetWord(Expression(nameID)) & 0xff) != 0;
        public int GetInt(int nameID) => unchecked((int)Values.GetWord(Expression(nameID)));
        public uint GetUInt(int nameID) => Values.GetWord(Expression(nameID));
        public float GetFloat(int nameID) => Values.GetFloat(Expression(nameID));
        public Vector2 GetVector2(int nameID) => Values.GetVector(Expression(nameID), 2);
        public Vector3 GetVector3(int nameID) => Values.GetVector(Expression(nameID), 3);
        public Vector4 GetVector4(int nameID) => Values.GetVector(Expression(nameID), 4);
        public Matrix4x4 GetMatrix4x4(int nameID) => Values.GetMatrix(Expression(nameID));
        public Texture GetTexture(int nameID) => Values.GetObject(Expression(nameID)) as Texture;
        public Mesh GetMesh(int nameID) => Values.GetObject(Expression(nameID)) as Mesh;

        /// <summary>A copy of the curve the input holds.</summary>
        public AnimationCurve GetAnimationCurve(int nameID)
        {
            var curve = new AnimationCurve();
            Internal_GetAnimationCurveFromScript(nameID, curve);
            return curve;
        }

        internal void Internal_GetAnimationCurveFromScript(int nameID, AnimationCurve curve)
        {
            if (Values.GetObject(Expression(nameID)) is AnimationCurve source) curve.CopyFrom(source);
        }

        /// <summary>A copy of the gradient the input holds.</summary>
        public Gradient GetGradient(int nameID)
        {
            var gradient = new Gradient();
            Internal_GetGradientFromScript(nameID, gradient);
            return gradient;
        }

        internal void Internal_GetGradientFromScript(int nameID, Gradient gradient)
        {
            if (Values.GetObject(Expression(nameID)) is not Gradient source) return;
            gradient.mode = source.mode;
            gradient.colorSpace = source.colorSpace;
            gradient.SetKeys(source.colorKeys, source.alphaKeys);
        }

        public bool GetBool(string name) => GetBool(Shader.PropertyToID(name));
        public int GetInt(string name) => GetInt(Shader.PropertyToID(name));
        public uint GetUInt(string name) => GetUInt(Shader.PropertyToID(name));
        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));
        public Vector2 GetVector2(string name) => GetVector2(Shader.PropertyToID(name));
        public Vector3 GetVector3(string name) => GetVector3(Shader.PropertyToID(name));
        public Vector4 GetVector4(string name) => GetVector4(Shader.PropertyToID(name));
        public Matrix4x4 GetMatrix4x4(string name) => GetMatrix4x4(Shader.PropertyToID(name));
        public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
        public AnimationCurve GetAnimationCurve(string name) => GetAnimationCurve(Shader.PropertyToID(name));
        public Gradient GetGradient(string name) => GetGradient(Shader.PropertyToID(name));
        public Mesh GetMesh(string name) => GetMesh(Shader.PropertyToID(name));
    }
}
