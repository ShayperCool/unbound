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
    /// lit shaders, a turned and scaled transform, a mask of one submesh). Not measured: the
    /// order of such a mesh against the effect's particle outputs (drawn after them here), its
    /// shadows, and a mesh value changed while the effect plays.
    /// </summary>
    internal sealed class VFXMeshSystem : VFXSystem
    {
        /// <summary>The system's values that name the expressions of its mesh, its transform
        /// and the mask of submeshes drawn.</summary>
        const string MeshValue = "mesh", TransformValue = "transform", MaskValue = "subMeshMask";

        /// <summary>The submeshes a mask can name: its bits.</summary>
        const int MaskBits = 32;

        readonly Material m_Material;
        readonly int m_Mesh, m_Transform, m_Mask;
        /// <summary>The shader properties the output's task names, with their expressions, the
        /// type of each expression's value, and whether the shader declares the property a
        /// colour.</summary>
        readonly (int Name, int Expression, VFXValueType Type, bool Color)[] m_Properties;
        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();

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
        }

        /// <summary>Why a mesh output system is not drawn, null when it is: one without a
        /// material (its shader was not imported) or without a mesh value is not one the
        /// graph compiler writes.</summary>
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
            int subMeshes = Math.Min(mesh.subMeshCount, MaskBits);
            for (int subMesh = 0; subMesh < subMeshes; subMesh++)
                if ((mask & (1u << subMesh)) != 0)
                    FrameBuilder.AddInstancedDraw(renderer, mesh, subMesh, m_Material, model, 1, m_Block.m_Values, camera, into);
        }

        /// <summary>The exposed shader properties, from the effect's values of this frame. A
        /// property the shader declares a colour is set as a colour, which a project in linear
        /// colour space takes from gamma to linear, as the Editor sets it (the effect's colour
        /// value drawn as the colour's linear value, tests/projects/vfxlit `single mesh`).</summary>
        void SetProperties(VisualEffectValues values)
        {
            foreach (var (name, expression, type, color) in m_Properties)
            {
                if (color && type == VFXValueType.Float4)
                {
                    m_Block.SetColor(name, (Color)values.GetVector(expression, 4));
                    continue;
                }
                switch (type)
                {
                    case VFXValueType.Float: m_Block.SetFloat(name, values.GetFloat(expression)); break;
                    case VFXValueType.Float2: m_Block.SetVector(name, values.GetVector(expression, 2)); break;
                    case VFXValueType.Float3: m_Block.SetVector(name, values.GetVector(expression, 3)); break;
                    case VFXValueType.Float4: m_Block.SetVector(name, values.GetVector(expression, 4)); break;
                    case VFXValueType.Int32: m_Block.SetInteger(name, (int)values.GetWord(expression)); break;
                    case VFXValueType.Uint32: m_Block.SetInteger(name, (int)values.GetWord(expression)); break;
                    case VFXValueType.Matrix4x4: m_Block.SetMatrix(name, values.GetMatrix(expression)); break;
                    default:
                        if (values.GetObject(expression) is Texture texture) m_Block.SetTexture(name, texture);
                        else m_Block.Remove(name);
                        break;
                }
            }
        }
    }
}
