using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// A mesh an effect draws as it is (`Output Single Mesh`, a system of its own): the effect's
    /// mesh value, with the material made from the output's shader, at the output's transform in
    /// the effect's space, one draw for each submesh its mask names, in the submeshes' order; the
    /// shader's properties the output exposes take the effect's values. Matches the Unity
    /// 6000.0.71f1 Editor (tests/projects/vfxlit `single mesh`: the render pipeline's unlit and
    /// lit shaders, a turned and scaled transform, a mask of one submesh). One whose shader was
    /// not imported draws nothing and says so once. Not measured: the order of such a mesh
    /// against the effect's particle outputs (drawn after them here), its shadows, the effect's
    /// bounds and culling with it (the effect's bounds are its particle systems'), two outputs
    /// of one shader (the importer makes one material for both), vector and texture properties,
    /// and a mesh value changed while the effect plays.
    /// </summary>
    internal sealed class VFXMeshSystem : VFXSystem
    {
        /// <summary>The system's values that name the expressions of its mesh, its transform
        /// and the mask of submeshes drawn.</summary>
        const string MeshValue = "mesh", TransformValue = "transform", MaskValue = "subMeshMask";

        readonly Material m_Material;
        readonly int m_Mesh, m_Transform, m_Mask;
        /// <summary>The shader properties the output's task names, with their expressions, the
        /// type of each expression's value, and whether the shader declares the property a
        /// colour.</summary>
        readonly (int Name, int Expression, VFXValueType Type, bool Color)[] m_Properties;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        /// <summary>The value each property was last set to (numbers as the bits of their four
        /// words and matrices, compared bit for bit; objects, by reference), so that a frame
        /// sets only what changed.</summary>
        readonly uint[] m_LastBits;
        readonly Matrix4x4[] m_LastMatrix;
        readonly object[] m_LastObject;
        readonly bool[] m_Set;

        internal VFXMeshSystem(VisualEffect effect, int index) : base(effect, index)
        {
            var data = effect.Data;
            var tasks = Desc.tasks ?? Array.Empty<VFXEditorTaskDesc>();
            var processors = index < data.Processors.Length ? data.Processors[index] : Array.Empty<UnityEngine.Object>();
            m_Material = processors.Length > 0 ? processors[0] as Material : null;
            m_Mesh = Value(Desc.values, MeshValue);
            m_Transform = Value(Desc.values, TransformValue);
            m_Mask = Value(Desc.values, MaskValue);
            var properties = new List<(int, int, VFXValueType, bool)>();
            var shader = m_Material != null ? m_Material.shader : null;
            if (tasks.Length > 0 && tasks[0].values != null)
                foreach (var mapping in tasks[0].values)
                {
                    if (mapping.index < 0 || mapping.index >= data.ExpressionTypes.Length) continue;
                    int declared = shader != null ? shader.FindPropertyIndex(mapping.name) : -1;
                    bool color = declared >= 0 && shader.GetPropertyType(declared) == UnityEngine.Rendering.ShaderPropertyType.Color;
                    properties.Add((Shader.PropertyToID(mapping.name), mapping.index, data.ExpressionTypes[mapping.index], color));
                }
            m_Properties = properties.ToArray();
            m_LastBits = new uint[m_Properties.Length * 4];
            m_LastMatrix = new Matrix4x4[m_Properties.Length];
            m_LastObject = new object[m_Properties.Length];
            m_Set = new bool[m_Properties.Length];
            if (m_Material == null) effect.WarnOutputNotDrawn(Desc.name, "its shader was not imported");
        }

        /// <summary>Why a mesh output system is not run, null when it is: one of other than one
        /// task or without a mesh value is not one the graph compiler writes.</summary>
        internal static string Refusal(VisualEffectCompiledData data, int index)
        {
            var desc = data.Systems[index];
            if ((desc.tasks?.Length ?? 0) != 1) return $"the mesh output system '{desc.name}' of {desc.tasks?.Length ?? 0} tasks";
            if (Value(desc.values, MeshValue) < 0) return $"the mesh output system '{desc.name}' without a mesh";
            return null;
        }

        internal override void Reset() { }

        internal override void Update(VisualEffectState state) { }

        /// <summary>The system's draws for a camera that sees the effect's renderer.</summary>
        internal void Collect(Renderer renderer, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (m_Material == null) return;
            var values = Effect.Values;
            if (!(values.GetObject(m_Mesh) is Mesh mesh) || !mesh) return;
            uint mask = m_Mask >= 0 ? values.GetWord(m_Mask) : uint.MaxValue;
            var model = renderer.transform.localToWorldMatrix;
            if (m_Transform >= 0) model *= values.GetMatrix(m_Transform);
            SetProperties(values);
            int subMeshes = Math.Min(mesh.subMeshCount, VFXOutput.MaskBits);
            for (int subMesh = 0; subMesh < subMeshes; subMesh++)
                if ((mask & (1u << subMesh)) != 0)
                    FrameBuilder.AddInstancedDraw(renderer, mesh, subMesh, m_Material, model, 1, m_Block.m_Values, camera, into);
        }

        /// <summary>Whether a property's words differ from the ones it was last set to (or it
        /// was never set), remembering them.</summary>
        bool Changed(int property, uint x, uint y, uint z, uint w)
        {
            int at = property * 4;
            if (m_Set[property] && m_LastBits[at] == x && m_LastBits[at + 1] == y && m_LastBits[at + 2] == z && m_LastBits[at + 3] == w) return false;
            (m_LastBits[at], m_LastBits[at + 1], m_LastBits[at + 2], m_LastBits[at + 3]) = (x, y, z, w);
            return true;
        }

        /// <summary>Whether two matrices hold the same bits.</summary>
        static bool SameBits(in Matrix4x4 a, in Matrix4x4 b)
        {
            for (int k = 0; k < 16; k++)
                if (BitConverter.SingleToUInt32Bits(a[k]) != BitConverter.SingleToUInt32Bits(b[k])) return false;
            return true;
        }

        /// <summary>The exposed shader properties, from the effect's values of this frame. A
        /// property the shader declares a colour is set as a colour, which a project in linear
        /// colour space takes from gamma to linear, as the Editor sets it (the effect's colour
        /// value drawn as the colour's linear value, tests/projects/vfxlit `single mesh`).</summary>
        void SetProperties(VisualEffectValues values)
        {
            for (int i = 0; i < m_Properties.Length; i++)
            {
                var (name, expression, type, color) = m_Properties[i];
                switch (type)
                {
                    case VFXValueType.Float:
                    case VFXValueType.Int32:
                    case VFXValueType.Uint32:
                    {
                        uint word = values.GetWord(expression);
                        if (!Changed(i, word, 0u, 0u, 0u)) break;
                        if (type == VFXValueType.Float) m_Block.SetFloat(name, BitConverter.UInt32BitsToSingle(word));
                        else m_Block.SetInteger(name, unchecked((int)word));
                        break;
                    }
                    case VFXValueType.Float2:
                    case VFXValueType.Float3:
                    case VFXValueType.Float4:
                    {
                        var value = values.GetVector(expression, type == VFXValueType.Float2 ? 2 : type == VFXValueType.Float3 ? 3 : 4);
                        if (!Changed(i, BitConverter.SingleToUInt32Bits(value.x), BitConverter.SingleToUInt32Bits(value.y),
                            BitConverter.SingleToUInt32Bits(value.z), BitConverter.SingleToUInt32Bits(value.w))) break;
                        if (color && type == VFXValueType.Float4) m_Block.SetColor(name, (Color)value);
                        else m_Block.SetVector(name, value);
                        break;
                    }
                    case VFXValueType.Matrix4x4:
                    {
                        var value = values.GetMatrix(expression);
                        if (m_Set[i] && SameBits(m_LastMatrix[i], value)) break;
                        m_LastMatrix[i] = value;
                        m_Block.SetMatrix(name, value);
                        break;
                    }
                    case VFXValueType.Texture2D:
                    case VFXValueType.Texture2DArray:
                    case VFXValueType.Texture3D:
                    case VFXValueType.TextureCube:
                    case VFXValueType.TextureCubeArray:
                    {
                        object value = values.GetObject(expression);
                        if (m_Set[i] && ReferenceEquals(m_LastObject[i], value)) break;
                        m_LastObject[i] = value;
                        if (value is Texture texture && texture) m_Block.SetTexture(name, texture);
                        else m_Block.Remove(name);
                        break;
                    }
                    // Values of other types (a boolean, a curve, a camera's buffer) are not set: not
                    // measured.
                    default: continue;
                }
                m_Set[i] = true;
            }
        }
    }
}
