using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
    /// <summary>
    /// A shader property of one or more materials, as the material inspector edits it
    /// (MaterialEditor.GetMaterialProperties). Its value is the first material's; a bit of
    /// <see cref="mixedValueMask"/> is set where another material differs (bit 0 the value, bits 1
    /// to 4 a texture's scale x and y and offset x and y). Writing a value sets it on every
    /// material. The range limits are a Range's, else the y and z of the shader's default vector
    /// (a Color's green and blue, a Vector's y and z); a property that is not a texture has the
    /// dimension Any. Matches the Unity 6000.0.71f1 Editor, tests/projects/graphicsgaps
    /// materialProperty.*.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public sealed partial class MaterialProperty
    {
        UnityEngine.Object[] m_Targets;
        ApplyPropertyCallback m_ApplyPropertyCallback;
        string m_Name;
        string m_DisplayName;
        object m_Value;
        Vector4 m_TextureScaleAndOffset;
        Vector2 m_RangeLimits;
        ShaderPropertyType m_Type;
        ShaderPropertyFlags m_Flags;
        TextureDimension m_TextureDimension;
        int m_MixedValueMask;

        public MaterialProperty() { }

        public delegate bool ApplyPropertyCallback(MaterialProperty prop, int changeMask, object previousValue);

        public enum PropType
        {
            Color = 0,
            Vector = 1,
            Float = 2,
            Range = 3,
            Texture = 4,
            Int = 5,
        }

        [Flags]
        public enum PropFlags
        {
            None = 0,
            HideInInspector = 1,
            PerRendererData = 2,
            NoScaleOffset = 4,
            Normal = 8,
            HDR = 16,
            Gamma = 32,
            NonModifiableTextureData = 64,
        }

        [Obsolete("Use UnityEngine.Rendering.TextureDimension instead.", false)]
        public enum TexDim
        {
            Unknown = -1,
            None = 0,
            Tex2D = 2,
            Tex3D = 3,
            Cube = 4,
            Any = 6,
        }

        public UnityEngine.Object[] targets => m_Targets;
        public PropType type => (PropType)m_Type;
        public string name => m_Name;
        public string displayName => m_DisplayName;
        public PropFlags flags => (PropFlags)m_Flags;
        public TextureDimension textureDimension => m_TextureDimension;
        public Vector2 rangeLimits => m_RangeLimits;
        public bool hasMixedValue => (m_MixedValueMask & 1) != 0;
        public ApplyPropertyCallback applyPropertyCallback { get => m_ApplyPropertyCallback; set => m_ApplyPropertyCallback = value; }
        internal int mixedValueMask => m_MixedValueMask;

        public Color colorValue
        {
            get => m_Type == ShaderPropertyType.Color ? (Color)m_Value : Color.black;
            set { if (m_Type == ShaderPropertyType.Color && (hasMixedValue || value != (Color)m_Value)) ApplyProperty(value); }
        }

        public Vector4 vectorValue
        {
            get => m_Type == ShaderPropertyType.Vector ? (Vector4)m_Value : Vector4.zero;
            set { if (m_Type == ShaderPropertyType.Vector && (hasMixedValue || value != (Vector4)m_Value)) ApplyProperty(value); }
        }

        public float floatValue
        {
            get => m_Type == ShaderPropertyType.Float || m_Type == ShaderPropertyType.Range ? (float)m_Value : 0f;
            set
            {
                if ((m_Type == ShaderPropertyType.Float || m_Type == ShaderPropertyType.Range) && (hasMixedValue || value != (float)m_Value))
                    ApplyProperty(value);
            }
        }

        public int intValue
        {
            get => m_Type == ShaderPropertyType.Int ? (int)m_Value : 0;
            set { if (m_Type == ShaderPropertyType.Int && (hasMixedValue || value != (int)m_Value)) ApplyProperty(value); }
        }

        public Texture textureValue
        {
            get => m_Type == ShaderPropertyType.Texture ? (Texture)m_Value : null;
            set
            {
                if (m_Type != ShaderPropertyType.Texture || (!hasMixedValue && value == (Texture)m_Value)) return;
                m_MixedValueMask &= -2;
                var previous = m_Value;
                m_Value = value;
                ApplyProperty(previous, 1);
            }
        }

        public Vector4 textureScaleAndOffset
        {
            get => m_Type == ShaderPropertyType.Texture ? m_TextureScaleAndOffset : Vector4.zero;
            set
            {
                if (m_Type != ShaderPropertyType.Texture || (!hasMixedValue && value == m_TextureScaleAndOffset)) return;
                m_MixedValueMask &= 1;
                object previous = m_TextureScaleAndOffset;
                m_TextureScaleAndOffset = value;
                ApplyProperty(previous, ScaleOffsetMask);
            }
        }

        /// <summary>The mixed-value bits of a texture's scale and offset components.</summary>
        const int ScaleOffsetMask = 0b11110;

        internal static bool IsTextureOffsetAndScaleChangedMask(int changedMask) => (changedMask >> 1) != 0;

        public void ReadFromMaterialPropertyBlock(MaterialPropertyBlock block)
        {
            if (block == null) return;
            int id = Shader.PropertyToID(m_Name);
            switch (m_Type)
            {
                case ShaderPropertyType.Color: if (block.HasColor(id)) m_Value = block.GetColor(id); break;
                case ShaderPropertyType.Vector: if (block.HasVector(id)) m_Value = block.GetVector(id); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: if (block.HasFloat(id)) m_Value = block.GetFloat(id); break;
                case ShaderPropertyType.Int: if (block.HasProperty(id)) m_Value = block.GetInt(id); break;
                case ShaderPropertyType.Texture:
                    if (block.HasTexture(id)) m_Value = block.GetTexture(id);
                    if (block.HasVector(Shader.PropertyToID(m_Name + "_ST"))) m_TextureScaleAndOffset = block.GetVector(m_Name + "_ST");
                    break;
            }
        }

        public void WriteToMaterialPropertyBlock(MaterialPropertyBlock materialblock, int changedPropertyMask)
        {
            if (materialblock == null) return;
            switch (m_Type)
            {
                case ShaderPropertyType.Color: materialblock.SetColor(m_Name, (Color)m_Value); break;
                case ShaderPropertyType.Vector: materialblock.SetVector(m_Name, (Vector4)m_Value); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: materialblock.SetFloat(m_Name, (float)m_Value); break;
                case ShaderPropertyType.Int: materialblock.SetInt(m_Name, (int)m_Value); break;
                case ShaderPropertyType.Texture:
                    if ((changedPropertyMask & 1) != 0 && m_Value is Texture texture) materialblock.SetTexture(m_Name, texture);
                    if (IsTextureOffsetAndScaleChangedMask(changedPropertyMask)) materialblock.SetVector(m_Name + "_ST", m_TextureScaleAndOffset);
                    break;
            }
        }

        void ApplyProperty(object newValue)
        {
            m_MixedValueMask = 0;
            var previous = m_Value;
            m_Value = newValue;
            ApplyProperty(previous, 1);
        }

        void ApplyProperty(object previousValue, int changedPropertyMask)
        {
            if (m_Targets == null || m_Targets.Length == 0) throw new ArgumentException("No material targets provided");
            if (m_ApplyPropertyCallback != null && m_ApplyPropertyCallback(this, changedPropertyMask, previousValue)) return;
            foreach (var target in m_Targets)
            {
                if (target is not Material material) continue;
                switch (m_Type)
                {
                    case ShaderPropertyType.Color: material.SetColor(m_Name, (Color)m_Value); break;
                    case ShaderPropertyType.Vector: material.SetVector(m_Name, (Vector4)m_Value); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: material.SetFloat(m_Name, (float)m_Value); break;
                    case ShaderPropertyType.Int: material.SetInteger(m_Name, (int)m_Value); break;
                    case ShaderPropertyType.Texture:
                        if ((changedPropertyMask & 1) != 0) material.SetTexture(m_Name, (Texture)m_Value);
                        if (IsTextureOffsetAndScaleChangedMask(changedPropertyMask))
                        {
                            material.SetTextureScale(m_Name, new Vector2(m_TextureScaleAndOffset.x, m_TextureScaleAndOffset.y));
                            material.SetTextureOffset(m_Name, new Vector2(m_TextureScaleAndOffset.z, m_TextureScaleAndOffset.w));
                        }
                        break;
                }
            }
        }

        /// <summary>The property of the first material's shader at an index, read from every
        /// material (null when the first is not a material with a shader declaring it).</summary>
        internal static MaterialProperty Of(UnityEngine.Object[] mats, int index)
        {
            if (mats == null || mats.Length == 0 || mats[0] is not Material first || first.shader == null) return null;
            var shader = first.shader;
            if (index < 0 || index >= shader.GetPropertyCount()) return null;
            var type = shader.GetPropertyType(index);
            var p = new MaterialProperty
            {
                m_Targets = mats,
                m_Name = shader.GetPropertyName(index),
                m_DisplayName = shader.GetPropertyDescription(index),
                m_Type = type,
                m_Flags = shader.GetPropertyFlags(index),
                m_TextureDimension = type == ShaderPropertyType.Texture ? shader.GetPropertyTextureDimension(index) : TextureDimension.Any,
            };
            if (type == ShaderPropertyType.Range) p.m_RangeLimits = shader.GetPropertyRangeLimits(index);
            else
            {
                var defaults = shader.GetPropertyDefaultVectorValue(index);
                p.m_RangeLimits = new Vector2(defaults.y, defaults.z);
            }
            p.m_Value = Value(first, p.m_Name, type);
            if (type == ShaderPropertyType.Texture) p.m_TextureScaleAndOffset = ScaleOffset(first, p.m_Name);
            for (int i = 1; i < mats.Length; i++)
            {
                if (mats[i] is not Material other) continue;
                if (!Equals(Value(other, p.m_Name, type), p.m_Value)) p.m_MixedValueMask |= 1;
                if (type != ShaderPropertyType.Texture) continue;
                var st = ScaleOffset(other, p.m_Name);
                for (int c = 0; c < 4; c++)
                    if (st[c] != p.m_TextureScaleAndOffset[c]) p.m_MixedValueMask |= 1 << (c + 1);
            }
            return p;
        }

        static object Value(Material material, string name, ShaderPropertyType type) => type switch
        {
            ShaderPropertyType.Color => material.GetColor(name),
            ShaderPropertyType.Vector => material.GetVector(name),
            ShaderPropertyType.Float or ShaderPropertyType.Range => material.GetFloat(name),
            ShaderPropertyType.Int => material.GetInteger(name),
            _ => material.GetTexture(name),
        };

        static Vector4 ScaleOffset(Material material, string name)
        {
            var scale = material.GetTextureScale(name);
            var offset = material.GetTextureOffset(name);
            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }
    }

    public partial class MaterialEditor
    {
        /// <summary>The properties of the first material's shader, in its order, read from every
        /// material (empty without materials).</summary>
        public static MaterialProperty[] GetMaterialProperties(UnityEngine.Object[] mats)
        {
            if (mats == null || mats.Length == 0 || mats[0] is not Material first || first.shader == null) return Array.Empty<MaterialProperty>();
            var result = new MaterialProperty[first.shader.GetPropertyCount()];
            for (int i = 0; i < result.Length; i++) result[i] = MaterialProperty.Of(mats, i);
            return result;
        }

        /// <summary>The property of the name; an empty property (no name, no targets) when the first
        /// material's shader does not declare it (tests/projects/graphicsgaps
        /// materialProperty.missing).</summary>
        public static MaterialProperty GetMaterialProperty(UnityEngine.Object[] mats, string name)
        {
            if (mats == null || mats.Length == 0 || mats[0] is not Material first || first.shader == null) return null;
            return MaterialProperty.Of(mats, first.shader.FindPropertyIndex(name)) ?? new MaterialProperty();
        }

        public static MaterialProperty GetMaterialProperty(UnityEngine.Object[] mats, int propertyIndex) => MaterialProperty.Of(mats, propertyIndex);
    }
}
