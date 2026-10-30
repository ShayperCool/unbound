using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering
{
    /// <summary>A shader tag name (a pass's LightMode, a SubShader's RenderType) as an id.</summary>
    public partial struct ShaderTagId : IEquatable<ShaderTagId>
    {
        public static readonly ShaderTagId none;

        int m_Id;

        public ShaderTagId(string name) => m_Id = Shader.TagToID(name);

        internal int id { get => m_Id; set => m_Id = value; }

        public string name => Shader.IDToTag(m_Id);

        public override bool Equals(object obj) => obj is ShaderTagId other && Equals(other);
        public bool Equals(ShaderTagId other) => m_Id == other.m_Id;
        public override int GetHashCode() => m_Id;
        public static bool operator ==(ShaderTagId tag1, ShaderTagId tag2) => tag1.Equals(tag2);
        public static bool operator !=(ShaderTagId tag1, ShaderTagId tag2) => !tag1.Equals(tag2);
        public static explicit operator ShaderTagId(string name) => new ShaderTagId(name);
        public static explicit operator string(ShaderTagId tagId) => tagId.name;
    }

    public partial struct RenderQueueRange : IEquatable<RenderQueueRange>
    {
        int m_LowerBound;
        int m_UpperBound;

        public static readonly int minimumBound = 0;
        public static readonly int maximumBound = 5000;

        public static RenderQueueRange all => new RenderQueueRange { m_LowerBound = 0, m_UpperBound = 5000 };
        public static RenderQueueRange opaque => new RenderQueueRange { m_LowerBound = 0, m_UpperBound = 2500 };
        public static RenderQueueRange transparent => new RenderQueueRange { m_LowerBound = 2501, m_UpperBound = 5000 };

        public RenderQueueRange(int lowerBound, int upperBound)
        {
            Check(lowerBound, nameof(lowerBound));
            Check(upperBound, nameof(upperBound));
            m_LowerBound = lowerBound;
            m_UpperBound = upperBound;
        }

        static void Check(int value, string name)
        {
            if (value < 0 || value > 5000)
                throw new ArgumentOutOfRangeException(name, value, "The bound must be at least 0 and at most 5000.");
        }

        public int lowerBound { get => m_LowerBound; set { Check(value, "lowerBound"); m_LowerBound = value; } }
        public int upperBound { get => m_UpperBound; set { Check(value, "upperBound"); m_UpperBound = value; } }

        internal bool Contains(int queue) => queue >= m_LowerBound && queue <= m_UpperBound;

        public bool Equals(RenderQueueRange other) => m_LowerBound == other.m_LowerBound && m_UpperBound == other.m_UpperBound;
        public override bool Equals(object obj) => obj is RenderQueueRange other && Equals(other);
        public override int GetHashCode() => (m_LowerBound * 397) ^ m_UpperBound;
        public static bool operator ==(RenderQueueRange left, RenderQueueRange right) => left.Equals(right);
        public static bool operator !=(RenderQueueRange left, RenderQueueRange right) => !left.Equals(right);
    }

    public partial struct SortingLayerRange : IEquatable<SortingLayerRange>
    {
        short m_LowerBound;
        short m_UpperBound;

        public SortingLayerRange(short lowerBound, short upperBound)
        {
            m_LowerBound = lowerBound;
            m_UpperBound = upperBound;
        }

        public short lowerBound { get => m_LowerBound; set => m_LowerBound = value; }
        public short upperBound { get => m_UpperBound; set => m_UpperBound = value; }

        public static SortingLayerRange all => new SortingLayerRange(short.MinValue, short.MaxValue);

        public bool Equals(SortingLayerRange other) => m_LowerBound == other.m_LowerBound && m_UpperBound == other.m_UpperBound;
        public override bool Equals(object obj) => obj is SortingLayerRange other && Equals(other);
        public override int GetHashCode() => (m_UpperBound << 16) | (m_LowerBound & 0xFFFF);
        public static bool operator ==(SortingLayerRange lhs, SortingLayerRange rhs) => lhs.Equals(rhs);
        public static bool operator !=(SortingLayerRange lhs, SortingLayerRange rhs) => !lhs.Equals(rhs);
    }

    public partial struct SortingSettings : IEquatable<SortingSettings>
    {
        Matrix4x4 m_WorldToCameraMatrix;
        Vector3 m_CameraPosition;
        Vector3 m_CustomAxis;
        SortingCriteria m_Criteria;
        DistanceMetric m_DistanceMetric;

        /// <summary>The camera's matrix and position, and the distance metric its transparency sort
        /// mode (or the project's) gives.</summary>
        public SortingSettings(Camera camera)
        {
            m_WorldToCameraMatrix = camera.worldToCameraMatrix;
            m_CameraPosition = camera.transform.position;
            var mode = camera.transparencySortMode != TransparencySortMode.Default ? camera.transparencySortMode : GraphicsSettings.transparencySortMode;
            m_CustomAxis = camera.transparencySortMode == TransparencySortMode.CustomAxis ? camera.transparencySortAxis : GraphicsSettings.transparencySortAxis;
            m_DistanceMetric = mode switch
            {
                TransparencySortMode.Orthographic => DistanceMetric.Orthographic,
                TransparencySortMode.Perspective => DistanceMetric.Perspective,
                TransparencySortMode.CustomAxis => DistanceMetric.CustomAxis,
                _ => camera.orthographic ? DistanceMetric.Orthographic : DistanceMetric.Perspective,
            };
            m_Criteria = SortingCriteria.CommonOpaque;
        }

        public Matrix4x4 worldToCameraMatrix { get => m_WorldToCameraMatrix; set => m_WorldToCameraMatrix = value; }
        public Vector3 cameraPosition { get => m_CameraPosition; set => m_CameraPosition = value; }
        public Vector3 customAxis { get => m_CustomAxis; set => m_CustomAxis = value; }
        public SortingCriteria criteria { get => m_Criteria; set => m_Criteria = value; }
        public DistanceMetric distanceMetric { get => m_DistanceMetric; set => m_DistanceMetric = value; }

        public bool Equals(SortingSettings other) =>
            m_WorldToCameraMatrix == other.m_WorldToCameraMatrix && m_CameraPosition == other.m_CameraPosition && m_CustomAxis == other.m_CustomAxis
            && m_Criteria == other.m_Criteria && m_DistanceMetric == other.m_DistanceMetric;
        public override bool Equals(object obj) => obj is SortingSettings other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_WorldToCameraMatrix, m_CameraPosition, m_CustomAxis, m_Criteria, m_DistanceMetric);
        public static bool operator ==(SortingSettings left, SortingSettings right) => left.Equals(right);
        public static bool operator !=(SortingSettings left, SortingSettings right) => !left.Equals(right);
    }

    public partial struct DrawingSettings : IEquatable<DrawingSettings>
    {
        public static readonly int maxShaderPasses = 16;

        SortingSettings m_SortingSettings;
        internal unsafe fixed int shaderPassNames[16];
        PerObjectData m_PerObjectData;
        int m_Flags;
        int m_OverrideShaderID;
        int m_OverrideShaderPassIndex;
        int m_OverrideMaterialInstanceId;
        int m_OverrideMaterialPassIndex;
        int m_FallbackMaterialInstanceId;
        int m_MainLightIndex;
        int m_UseSrpBatcher;

        public unsafe DrawingSettings(ShaderTagId shaderPassName, SortingSettings sortingSettings)
        {
            m_SortingSettings = sortingSettings;
            m_PerObjectData = PerObjectData.None;
            m_Flags = 1;
            m_OverrideShaderID = 0;
            m_OverrideShaderPassIndex = 0;
            m_OverrideMaterialInstanceId = 0;
            m_OverrideMaterialPassIndex = 0;
            m_FallbackMaterialInstanceId = 0;
            m_MainLightIndex = -1;
            m_UseSrpBatcher = 0;
            fixed (int* p = shaderPassNames)
            {
                p[0] = shaderPassName.id;
                for (int i = 1; i < 16; i++) p[i] = -1;
            }
        }

        public SortingSettings sortingSettings { get => m_SortingSettings; set => m_SortingSettings = value; }
        public PerObjectData perObjectData { get => m_PerObjectData; set => m_PerObjectData = value; }
        public bool enableDynamicBatching { get => (m_Flags & 2) != 0; set => m_Flags = value ? m_Flags | 2 : m_Flags & ~2; }
        public bool enableInstancing { get => (m_Flags & 1) != 0; set => m_Flags = value ? m_Flags | 1 : m_Flags & ~1; }

        public Material overrideMaterial
        {
            get => m_OverrideMaterialInstanceId != 0 ? Unbound.Runtime.ObjectRegistry.FindById(m_OverrideMaterialInstanceId) as Material : null;
            set => m_OverrideMaterialInstanceId = value != null ? value.GetInstanceID() : 0;
        }

        public Shader overrideShader
        {
            get => m_OverrideShaderID != 0 ? Unbound.Runtime.ObjectRegistry.FindById(m_OverrideShaderID) as Shader : null;
            set => m_OverrideShaderID = value != null ? value.GetInstanceID() : 0;
        }

        public int overrideMaterialPassIndex { get => m_OverrideMaterialPassIndex; set => m_OverrideMaterialPassIndex = value; }
        public int overrideShaderPassIndex { get => m_OverrideShaderPassIndex; set => m_OverrideShaderPassIndex = value; }

        public Material fallbackMaterial
        {
            get => m_FallbackMaterialInstanceId != 0 ? Unbound.Runtime.ObjectRegistry.FindById(m_FallbackMaterialInstanceId) as Material : null;
            set => m_FallbackMaterialInstanceId = value != null ? value.GetInstanceID() : 0;
        }

        public int mainLightIndex { get => m_MainLightIndex; set => m_MainLightIndex = value; }

        public unsafe ShaderTagId GetShaderPassName(int index)
        {
            if (index < 0 || index >= 16) throw new ArgumentOutOfRangeException(nameof(index), "Index should range from 0 to DrawingSettings.maxShaderPasses (16), was " + index);
            fixed (int* p = shaderPassNames) return new ShaderTagId { id = p[index] };
        }

        public unsafe void SetShaderPassName(int index, ShaderTagId shaderPassName)
        {
            if (index < 0 || index >= 16) throw new ArgumentOutOfRangeException(nameof(index), "Index should range from 0 to DrawingSettings.maxShaderPasses (16), was " + index);
            fixed (int* p = shaderPassNames) p[index] = shaderPassName.id;
        }

        /// <summary>The pass names set, in order (ids of -1 and 0 end or skip none).</summary>
        internal List<ShaderTagId> PassNames()
        {
            var names = new List<ShaderTagId>();
            for (int i = 0; i < 16; i++)
            {
                var tag = GetShaderPassName(i);
                if (tag.id > 0) names.Add(tag);
            }
            return names;
        }

        public unsafe bool Equals(DrawingSettings other)
        {
            for (int i = 0; i < 16; i++)
                if (GetShaderPassName(i) != other.GetShaderPassName(i)) return false;
            return m_SortingSettings == other.m_SortingSettings && m_PerObjectData == other.m_PerObjectData && m_Flags == other.m_Flags
                && m_OverrideShaderID == other.m_OverrideShaderID && m_OverrideShaderPassIndex == other.m_OverrideShaderPassIndex
                && m_OverrideMaterialInstanceId == other.m_OverrideMaterialInstanceId && m_OverrideMaterialPassIndex == other.m_OverrideMaterialPassIndex
                && m_FallbackMaterialInstanceId == other.m_FallbackMaterialInstanceId && m_MainLightIndex == other.m_MainLightIndex;
        }

        public override bool Equals(object obj) => obj is DrawingSettings other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_SortingSettings, m_PerObjectData, m_Flags, m_OverrideMaterialInstanceId, m_OverrideMaterialPassIndex, m_MainLightIndex);
        public static bool operator ==(DrawingSettings left, DrawingSettings right) => left.Equals(right);
        public static bool operator !=(DrawingSettings left, DrawingSettings right) => !left.Equals(right);
    }

    public partial struct FilteringSettings : IEquatable<FilteringSettings>
    {
        RenderQueueRange m_RenderQueueRange;
        int m_LayerMask;
        uint m_RenderingLayerMask;
        uint m_BatchLayerMask;
        int m_ExcludeMotionVectorObjects;
        int m_ForceAllMotionVectorObjects;
        SortingLayerRange m_SortingLayerRange;

        public static FilteringSettings defaultValue => new FilteringSettings(RenderQueueRange.all);

        public FilteringSettings([Internal.DefaultValue("RenderQueueRange.all")] RenderQueueRange? renderQueueRange = null, int layerMask = -1,
            uint renderingLayerMask = uint.MaxValue, int excludeMotionVectorObjects = 0)
        {
            m_RenderQueueRange = renderQueueRange ?? RenderQueueRange.all;
            m_LayerMask = layerMask;
            m_RenderingLayerMask = renderingLayerMask;
            m_BatchLayerMask = uint.MaxValue;
            m_ExcludeMotionVectorObjects = excludeMotionVectorObjects;
            m_ForceAllMotionVectorObjects = 0;
            m_SortingLayerRange = SortingLayerRange.all;
        }

        public RenderQueueRange renderQueueRange { get => m_RenderQueueRange; set => m_RenderQueueRange = value; }
        public int layerMask { get => m_LayerMask; set => m_LayerMask = value; }
        public uint renderingLayerMask { get => m_RenderingLayerMask; set => m_RenderingLayerMask = value; }
        public uint batchLayerMask { get => m_BatchLayerMask; set => m_BatchLayerMask = value; }
        public bool excludeMotionVectorObjects { get => m_ExcludeMotionVectorObjects != 0; set => m_ExcludeMotionVectorObjects = value ? 1 : 0; }
        public bool forceAllMotionVectorObjects { get => m_ForceAllMotionVectorObjects != 0; set => m_ForceAllMotionVectorObjects = value ? 1 : 0; }
        public SortingLayerRange sortingLayerRange { get => m_SortingLayerRange; set => m_SortingLayerRange = value; }

        public bool Equals(FilteringSettings other) =>
            m_RenderQueueRange == other.m_RenderQueueRange && m_LayerMask == other.m_LayerMask && m_RenderingLayerMask == other.m_RenderingLayerMask
            && m_BatchLayerMask == other.m_BatchLayerMask && m_ExcludeMotionVectorObjects == other.m_ExcludeMotionVectorObjects
            && m_ForceAllMotionVectorObjects == other.m_ForceAllMotionVectorObjects && m_SortingLayerRange == other.m_SortingLayerRange;
        public override bool Equals(object obj) => obj is FilteringSettings other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_RenderQueueRange, m_LayerMask, m_RenderingLayerMask, m_BatchLayerMask, m_ExcludeMotionVectorObjects, m_SortingLayerRange);
        public static bool operator ==(FilteringSettings left, FilteringSettings right) => left.Equals(right);
        public static bool operator !=(FilteringSettings left, FilteringSettings right) => !left.Equals(right);
    }

    public partial struct RenderTargetBlendState : IEquatable<RenderTargetBlendState>
    {
        byte m_WriteMask;
        byte m_SourceColorBlendMode;
        byte m_DestinationColorBlendMode;
        byte m_SourceAlphaBlendMode;
        byte m_DestinationAlphaBlendMode;
        byte m_ColorBlendOperation;
        byte m_AlphaBlendOperation;
        byte m_Padding;

        public static RenderTargetBlendState defaultValue => new RenderTargetBlendState(ColorWriteMask.All, BlendMode.One, BlendMode.Zero, BlendMode.One, BlendMode.Zero, BlendOp.Add, BlendOp.Add);

        public RenderTargetBlendState(ColorWriteMask writeMask = ColorWriteMask.All, BlendMode sourceColorBlendMode = BlendMode.One, BlendMode destinationColorBlendMode = BlendMode.Zero,
            BlendMode sourceAlphaBlendMode = BlendMode.One, BlendMode destinationAlphaBlendMode = BlendMode.Zero, BlendOp colorBlendOperation = BlendOp.Add, BlendOp alphaBlendOperation = BlendOp.Add)
        {
            m_WriteMask = (byte)writeMask;
            m_SourceColorBlendMode = (byte)sourceColorBlendMode;
            m_DestinationColorBlendMode = (byte)destinationColorBlendMode;
            m_SourceAlphaBlendMode = (byte)sourceAlphaBlendMode;
            m_DestinationAlphaBlendMode = (byte)destinationAlphaBlendMode;
            m_ColorBlendOperation = (byte)colorBlendOperation;
            m_AlphaBlendOperation = (byte)alphaBlendOperation;
            m_Padding = 0;
        }

        public ColorWriteMask writeMask { get => (ColorWriteMask)m_WriteMask; set => m_WriteMask = (byte)value; }
        public BlendMode sourceColorBlendMode { get => (BlendMode)m_SourceColorBlendMode; set => m_SourceColorBlendMode = (byte)value; }
        public BlendMode destinationColorBlendMode { get => (BlendMode)m_DestinationColorBlendMode; set => m_DestinationColorBlendMode = (byte)value; }
        public BlendMode sourceAlphaBlendMode { get => (BlendMode)m_SourceAlphaBlendMode; set => m_SourceAlphaBlendMode = (byte)value; }
        public BlendMode destinationAlphaBlendMode { get => (BlendMode)m_DestinationAlphaBlendMode; set => m_DestinationAlphaBlendMode = (byte)value; }
        public BlendOp colorBlendOperation { get => (BlendOp)m_ColorBlendOperation; set => m_ColorBlendOperation = (byte)value; }
        public BlendOp alphaBlendOperation { get => (BlendOp)m_AlphaBlendOperation; set => m_AlphaBlendOperation = (byte)value; }

        public bool Equals(RenderTargetBlendState other) =>
            m_WriteMask == other.m_WriteMask && m_SourceColorBlendMode == other.m_SourceColorBlendMode && m_DestinationColorBlendMode == other.m_DestinationColorBlendMode
            && m_SourceAlphaBlendMode == other.m_SourceAlphaBlendMode && m_DestinationAlphaBlendMode == other.m_DestinationAlphaBlendMode
            && m_ColorBlendOperation == other.m_ColorBlendOperation && m_AlphaBlendOperation == other.m_AlphaBlendOperation;
        public override bool Equals(object obj) => obj is RenderTargetBlendState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_WriteMask, m_SourceColorBlendMode, m_DestinationColorBlendMode, m_SourceAlphaBlendMode, m_DestinationAlphaBlendMode, m_ColorBlendOperation, m_AlphaBlendOperation);
        public static bool operator ==(RenderTargetBlendState left, RenderTargetBlendState right) => left.Equals(right);
        public static bool operator !=(RenderTargetBlendState left, RenderTargetBlendState right) => !left.Equals(right);
    }

    public partial struct BlendState : IEquatable<BlendState>
    {
        RenderTargetBlendState m_BlendState0;
        RenderTargetBlendState m_BlendState1;
        RenderTargetBlendState m_BlendState2;
        RenderTargetBlendState m_BlendState3;
        RenderTargetBlendState m_BlendState4;
        RenderTargetBlendState m_BlendState5;
        RenderTargetBlendState m_BlendState6;
        RenderTargetBlendState m_BlendState7;
        byte m_SeparateMRTBlendStates;
        byte m_AlphaToMask;
        short m_Padding;

        public static BlendState defaultValue => new BlendState(false, false);

        public BlendState(bool separateMRTBlend = false, bool alphaToMask = false)
        {
            m_BlendState0 = m_BlendState1 = m_BlendState2 = m_BlendState3 = RenderTargetBlendState.defaultValue;
            m_BlendState4 = m_BlendState5 = m_BlendState6 = m_BlendState7 = RenderTargetBlendState.defaultValue;
            m_SeparateMRTBlendStates = (byte)(separateMRTBlend ? 1 : 0);
            m_AlphaToMask = (byte)(alphaToMask ? 1 : 0);
            m_Padding = 0;
        }

        public bool separateMRTBlendStates { get => m_SeparateMRTBlendStates != 0; set => m_SeparateMRTBlendStates = (byte)(value ? 1 : 0); }
        public bool alphaToMask { get => m_AlphaToMask != 0; set => m_AlphaToMask = (byte)(value ? 1 : 0); }
        public RenderTargetBlendState blendState0 { get => m_BlendState0; set => m_BlendState0 = value; }
        public RenderTargetBlendState blendState1 { get => m_BlendState1; set => m_BlendState1 = value; }
        public RenderTargetBlendState blendState2 { get => m_BlendState2; set => m_BlendState2 = value; }
        public RenderTargetBlendState blendState3 { get => m_BlendState3; set => m_BlendState3 = value; }
        public RenderTargetBlendState blendState4 { get => m_BlendState4; set => m_BlendState4 = value; }
        public RenderTargetBlendState blendState5 { get => m_BlendState5; set => m_BlendState5 = value; }
        public RenderTargetBlendState blendState6 { get => m_BlendState6; set => m_BlendState6 = value; }
        public RenderTargetBlendState blendState7 { get => m_BlendState7; set => m_BlendState7 = value; }

        public bool Equals(BlendState other) =>
            m_BlendState0 == other.m_BlendState0 && m_BlendState1 == other.m_BlendState1 && m_BlendState2 == other.m_BlendState2 && m_BlendState3 == other.m_BlendState3
            && m_BlendState4 == other.m_BlendState4 && m_BlendState5 == other.m_BlendState5 && m_BlendState6 == other.m_BlendState6 && m_BlendState7 == other.m_BlendState7
            && m_SeparateMRTBlendStates == other.m_SeparateMRTBlendStates && m_AlphaToMask == other.m_AlphaToMask;
        public override bool Equals(object obj) => obj is BlendState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_BlendState0, m_BlendState1, m_SeparateMRTBlendStates, m_AlphaToMask);
        public static bool operator ==(BlendState left, BlendState right) => left.Equals(right);
        public static bool operator !=(BlendState left, BlendState right) => !left.Equals(right);
    }

    public partial struct RasterState : IEquatable<RasterState>
    {
        public static readonly RasterState defaultValue = new RasterState(CullMode.Back, 0, 0f, true);

        CullMode m_CullingMode;
        int m_OffsetUnits;
        float m_OffsetFactor;
        byte m_DepthClip;
        byte m_Conservative;
        byte m_Padding1;
        byte m_Padding2;

        public RasterState(CullMode cullingMode = CullMode.Back, int offsetUnits = 0, float offsetFactor = 0f, bool depthClip = true)
        {
            m_CullingMode = cullingMode;
            m_OffsetUnits = offsetUnits;
            m_OffsetFactor = offsetFactor;
            m_DepthClip = (byte)(depthClip ? 1 : 0);
            m_Conservative = 0;
            m_Padding1 = m_Padding2 = 0;
        }

        public CullMode cullingMode { get => m_CullingMode; set => m_CullingMode = value; }
        public bool depthClip { get => m_DepthClip != 0; set => m_DepthClip = (byte)(value ? 1 : 0); }
        public bool conservative { get => m_Conservative != 0; set => m_Conservative = (byte)(value ? 1 : 0); }
        public int offsetUnits { get => m_OffsetUnits; set => m_OffsetUnits = value; }
        public float offsetFactor { get => m_OffsetFactor; set => m_OffsetFactor = value; }

        public bool Equals(RasterState other) =>
            m_CullingMode == other.m_CullingMode && m_OffsetUnits == other.m_OffsetUnits && m_OffsetFactor.Equals(other.m_OffsetFactor)
            && m_DepthClip == other.m_DepthClip && m_Conservative == other.m_Conservative;
        public override bool Equals(object obj) => obj is RasterState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_CullingMode, m_OffsetUnits, m_OffsetFactor, m_DepthClip, m_Conservative);
        public static bool operator ==(RasterState left, RasterState right) => left.Equals(right);
        public static bool operator !=(RasterState left, RasterState right) => !left.Equals(right);
    }

    public partial struct DepthState : IEquatable<DepthState>
    {
        byte m_WriteEnabled;
        sbyte m_CompareFunction;

        public static DepthState defaultValue => new DepthState(true, CompareFunction.Less);

        public DepthState(bool writeEnabled = true, CompareFunction compareFunction = CompareFunction.Less)
        {
            m_WriteEnabled = (byte)(writeEnabled ? 1 : 0);
            m_CompareFunction = (sbyte)compareFunction;
        }

        public bool writeEnabled { get => m_WriteEnabled != 0; set => m_WriteEnabled = (byte)(value ? 1 : 0); }
        public CompareFunction compareFunction { get => (CompareFunction)m_CompareFunction; set => m_CompareFunction = (sbyte)value; }

        public bool Equals(DepthState other) => m_WriteEnabled == other.m_WriteEnabled && m_CompareFunction == other.m_CompareFunction;
        public override bool Equals(object obj) => obj is DepthState other && Equals(other);
        public override int GetHashCode() => (m_WriteEnabled.GetHashCode() * 397) ^ m_CompareFunction.GetHashCode();
        public static bool operator ==(DepthState left, DepthState right) => left.Equals(right);
        public static bool operator !=(DepthState left, DepthState right) => !left.Equals(right);
    }

    public partial struct StencilState : IEquatable<StencilState>
    {
        byte m_Enabled;
        byte m_ReadMask;
        byte m_WriteMask;
        byte m_Padding;
        byte m_CompareFunctionFront;
        byte m_PassOperationFront;
        byte m_FailOperationFront;
        byte m_ZFailOperationFront;
        byte m_CompareFunctionBack;
        byte m_PassOperationBack;
        byte m_FailOperationBack;
        byte m_ZFailOperationBack;

        public static StencilState defaultValue => new StencilState(true, byte.MaxValue, byte.MaxValue, CompareFunction.Always, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);

        public StencilState(bool enabled = true, byte readMask = byte.MaxValue, byte writeMask = byte.MaxValue, CompareFunction compareFunction = CompareFunction.Always,
            StencilOp passOperation = StencilOp.Keep, StencilOp failOperation = StencilOp.Keep, StencilOp zFailOperation = StencilOp.Keep)
            : this(enabled, readMask, writeMask, compareFunction, passOperation, failOperation, zFailOperation, compareFunction, passOperation, failOperation, zFailOperation) { }

        public StencilState(bool enabled, byte readMask, byte writeMask, CompareFunction compareFunctionFront, StencilOp passOperationFront, StencilOp failOperationFront,
            StencilOp zFailOperationFront, CompareFunction compareFunctionBack, StencilOp passOperationBack, StencilOp failOperationBack, StencilOp zFailOperationBack)
        {
            m_Enabled = (byte)(enabled ? 1 : 0);
            m_ReadMask = readMask;
            m_WriteMask = writeMask;
            m_Padding = 0;
            m_CompareFunctionFront = (byte)compareFunctionFront;
            m_PassOperationFront = (byte)passOperationFront;
            m_FailOperationFront = (byte)failOperationFront;
            m_ZFailOperationFront = (byte)zFailOperationFront;
            m_CompareFunctionBack = (byte)compareFunctionBack;
            m_PassOperationBack = (byte)passOperationBack;
            m_FailOperationBack = (byte)failOperationBack;
            m_ZFailOperationBack = (byte)zFailOperationBack;
        }

        public bool enabled { get => m_Enabled != 0; set => m_Enabled = (byte)(value ? 1 : 0); }
        public byte readMask { get => m_ReadMask; set => m_ReadMask = value; }
        public byte writeMask { get => m_WriteMask; set => m_WriteMask = value; }
        public CompareFunction compareFunctionFront { get => (CompareFunction)m_CompareFunctionFront; set => m_CompareFunctionFront = (byte)value; }
        public StencilOp passOperationFront { get => (StencilOp)m_PassOperationFront; set => m_PassOperationFront = (byte)value; }
        public StencilOp failOperationFront { get => (StencilOp)m_FailOperationFront; set => m_FailOperationFront = (byte)value; }
        public StencilOp zFailOperationFront { get => (StencilOp)m_ZFailOperationFront; set => m_ZFailOperationFront = (byte)value; }
        public CompareFunction compareFunctionBack { get => (CompareFunction)m_CompareFunctionBack; set => m_CompareFunctionBack = (byte)value; }
        public StencilOp passOperationBack { get => (StencilOp)m_PassOperationBack; set => m_PassOperationBack = (byte)value; }
        public StencilOp failOperationBack { get => (StencilOp)m_FailOperationBack; set => m_FailOperationBack = (byte)value; }
        public StencilOp zFailOperationBack { get => (StencilOp)m_ZFailOperationBack; set => m_ZFailOperationBack = (byte)value; }

        public void SetCompareFunction(CompareFunction value) { compareFunctionFront = value; compareFunctionBack = value; }
        public void SetPassOperation(StencilOp value) { passOperationFront = value; passOperationBack = value; }
        public void SetFailOperation(StencilOp value) { failOperationFront = value; failOperationBack = value; }
        public void SetZFailOperation(StencilOp value) { zFailOperationFront = value; zFailOperationBack = value; }

        public bool Equals(StencilState other) =>
            m_Enabled == other.m_Enabled && m_ReadMask == other.m_ReadMask && m_WriteMask == other.m_WriteMask
            && m_CompareFunctionFront == other.m_CompareFunctionFront && m_PassOperationFront == other.m_PassOperationFront
            && m_FailOperationFront == other.m_FailOperationFront && m_ZFailOperationFront == other.m_ZFailOperationFront
            && m_CompareFunctionBack == other.m_CompareFunctionBack && m_PassOperationBack == other.m_PassOperationBack
            && m_FailOperationBack == other.m_FailOperationBack && m_ZFailOperationBack == other.m_ZFailOperationBack;
        public override bool Equals(object obj) => obj is StencilState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_Enabled, m_ReadMask, m_WriteMask, m_CompareFunctionFront, m_PassOperationFront, m_FailOperationFront, m_ZFailOperationFront, m_CompareFunctionBack);
        public static bool operator ==(StencilState left, StencilState right) => left.Equals(right);
        public static bool operator !=(StencilState left, StencilState right) => !left.Equals(right);
    }

    public partial struct RenderStateBlock : IEquatable<RenderStateBlock>
    {
        BlendState m_BlendState;
        RasterState m_RasterState;
        DepthState m_DepthState;
        StencilState m_StencilState;
        int m_StencilReference;
        RenderStateMask m_Mask;

        public RenderStateBlock(RenderStateMask mask)
        {
            m_BlendState = BlendState.defaultValue;
            m_RasterState = RasterState.defaultValue;
            m_DepthState = DepthState.defaultValue;
            m_StencilState = StencilState.defaultValue;
            m_StencilReference = 0;
            m_Mask = mask;
        }

        public BlendState blendState { get => m_BlendState; set => m_BlendState = value; }
        public RasterState rasterState { get => m_RasterState; set => m_RasterState = value; }
        public DepthState depthState { get => m_DepthState; set => m_DepthState = value; }
        public StencilState stencilState { get => m_StencilState; set => m_StencilState = value; }
        public int stencilReference { get => m_StencilReference; set => m_StencilReference = value; }
        public RenderStateMask mask { get => m_Mask; set => m_Mask = value; }

        public bool Equals(RenderStateBlock other) =>
            m_BlendState == other.m_BlendState && m_RasterState == other.m_RasterState && m_DepthState == other.m_DepthState
            && m_StencilState == other.m_StencilState && m_StencilReference == other.m_StencilReference && m_Mask == other.m_Mask;
        public override bool Equals(object obj) => obj is RenderStateBlock other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_BlendState, m_RasterState, m_DepthState, m_StencilState, m_StencilReference, m_Mask);
        public static bool operator ==(RenderStateBlock left, RenderStateBlock right) => left.Equals(right);
        public static bool operator !=(RenderStateBlock left, RenderStateBlock right) => !left.Equals(right);
    }

    /// <summary>A list of renderers to draw, made by a ScriptableRenderContext; drawn with
    /// CommandBuffer.DrawRendererList.</summary>
    public partial struct RendererList
    {
        internal UIntPtr context;
        internal uint index;
        internal uint frame;
        internal uint type;
        internal uint contextID;

        public static readonly RendererList nullRendererList = new RendererList(UIntPtr.Zero, uint.MaxValue);

        internal RendererList(UIntPtr ctx, uint indx)
        {
            context = ctx;
            index = indx;
            frame = 0;
            type = 0;
            contextID = 0;
        }

        public bool isValid => index != uint.MaxValue && Unbound.Runtime.RendererLists.IsValid(this);
    }

    public partial struct RendererListParams : IEquatable<RendererListParams>
    {
        public static readonly RendererListParams Invalid = default;

        public CullingResults cullingResults;
        public DrawingSettings drawSettings;
        public FilteringSettings filteringSettings;
        public ShaderTagId tagName;
        public bool isPassTagName;
        public NativeArray<ShaderTagId>? tagValues;
        public NativeArray<RenderStateBlock>? stateBlocks;

        public RendererListParams(CullingResults cullingResults, DrawingSettings drawSettings, FilteringSettings filteringSettings)
        {
            this.cullingResults = cullingResults;
            this.drawSettings = drawSettings;
            this.filteringSettings = filteringSettings;
            tagName = ShaderTagId.none;
            isPassTagName = false;
            tagValues = null;
            stateBlocks = null;
        }

        internal void Dispose() { }

        internal void Validate()
        {
            if (tagValues.HasValue && stateBlocks.HasValue && tagValues.Value.Length != stateBlocks.Value.Length)
                throw new ArgumentException($"Arrays {nameof(tagValues)} and {nameof(stateBlocks)} should have same length.");
        }

        public bool Equals(RendererListParams other) =>
            cullingResults == other.cullingResults && drawSettings == other.drawSettings && filteringSettings == other.filteringSettings
            && tagName == other.tagName && isPassTagName == other.isPassTagName;
        public override bool Equals(object obj) => obj is RendererListParams other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(cullingResults, drawSettings, filteringSettings, tagName, isPassTagName);
        public static bool operator ==(RendererListParams left, RendererListParams right) => left.Equals(right);
        public static bool operator !=(RendererListParams left, RendererListParams right) => !left.Equals(right);
    }

    /// <summary>A light the culling found visible (the data a pipeline reads, not the Light).</summary>
    public partial struct VisibleLight : IEquatable<VisibleLight>
    {
        LightType m_LightType;
        Color m_FinalColor;
        Rect m_ScreenRect;
        Matrix4x4 m_LocalToWorldMatrix;
        float m_Range;
        float m_SpotAngle;
        int m_InstanceId;
        int m_Flags;

        internal VisibleLight(Light light, Rect screenRect, bool linear, bool intersectsNear, bool intersectsFar)
        {
            m_LightType = light.type;
            m_FinalColor = light.SrpFinalColor(linear);
            m_ScreenRect = screenRect;
            m_LocalToWorldMatrix = light.transform.localToWorldMatrix;
            m_Range = light.range;
            m_SpotAngle = light.spotAngle;
            m_InstanceId = light.GetInstanceID();
            m_Flags = (intersectsNear ? 1 : 0) | (intersectsFar ? 2 : 0);
        }

        public Light light => Unbound.Runtime.ObjectRegistry.FindById(m_InstanceId) as Light;
        public LightType lightType { get => m_LightType; set => m_LightType = value; }
        public Color finalColor { get => m_FinalColor; set => m_FinalColor = value; }
        public Rect screenRect { get => m_ScreenRect; set => m_ScreenRect = value; }
        public Matrix4x4 localToWorldMatrix { get => m_LocalToWorldMatrix; set => m_LocalToWorldMatrix = value; }
        public float range { get => m_Range; set => m_Range = value; }
        public float spotAngle { get => m_SpotAngle; set => m_SpotAngle = value; }
        public bool intersectsNearPlane { get => (m_Flags & 1) != 0; set => m_Flags = value ? m_Flags | 1 : m_Flags & ~1; }
        public bool intersectsFarPlane { get => (m_Flags & 2) != 0; set => m_Flags = value ? m_Flags | 2 : m_Flags & ~2; }
        public bool forcedVisible => (m_Flags & 4) != 0;

        public bool Equals(VisibleLight other) =>
            m_LightType == other.m_LightType && m_FinalColor == other.m_FinalColor && m_ScreenRect == other.m_ScreenRect
            && m_LocalToWorldMatrix == other.m_LocalToWorldMatrix && m_Range == other.m_Range && m_SpotAngle == other.m_SpotAngle
            && m_InstanceId == other.m_InstanceId && m_Flags == other.m_Flags;
        public override bool Equals(object obj) => obj is VisibleLight other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_LightType, m_FinalColor, m_ScreenRect, m_LocalToWorldMatrix, m_Range, m_SpotAngle, m_InstanceId, m_Flags);
        public static bool operator ==(VisibleLight left, VisibleLight right) => left.Equals(right);
        public static bool operator !=(VisibleLight left, VisibleLight right) => !left.Equals(right);
    }

    public partial struct VisibleReflectionProbe : IEquatable<VisibleReflectionProbe>
    {
        Bounds m_Bounds;
        Matrix4x4 m_LocalToWorldMatrix;
        Vector4 m_HdrData;
        Vector3 m_Center;
        float m_BlendDistance;
        int m_Importance;
        int m_BoxProjection;
        int m_InstanceId;
        int m_TextureId;

        /// <summary>A culled probe's data as the culling gives it.</summary>
        internal VisibleReflectionProbe(ReflectionProbe probe)
        {
            m_Bounds = probe.bounds;
            var t = probe.transform;
            m_LocalToWorldMatrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            m_HdrData = probe.textureHDRDecodeValues;
            m_Center = probe.center;
            m_BlendDistance = probe.blendDistance;
            m_Importance = probe.importance;
            m_BoxProjection = probe.boxProjection ? 1 : 0;
            m_InstanceId = probe.GetInstanceID();
            m_TextureId = probe.texture != null ? probe.texture.GetInstanceID() : 0;
        }

        public Texture texture => Unbound.Runtime.ObjectRegistry.FindById(m_TextureId) as Texture;
        public ReflectionProbe reflectionProbe => Unbound.Runtime.ObjectRegistry.FindById(m_InstanceId) as ReflectionProbe;
        public Bounds bounds { get => m_Bounds; set => m_Bounds = value; }
        public Matrix4x4 localToWorldMatrix { get => m_LocalToWorldMatrix; set => m_LocalToWorldMatrix = value; }
        public Vector4 hdrData { get => m_HdrData; set => m_HdrData = value; }
        public Vector3 center { get => m_Center; set => m_Center = value; }
        public float blendDistance { get => m_BlendDistance; set => m_BlendDistance = value; }
        public int importance { get => m_Importance; set => m_Importance = value; }
        public bool isBoxProjection { get => m_BoxProjection != 0; set => m_BoxProjection = value ? 1 : 0; }

        public bool Equals(VisibleReflectionProbe other) =>
            m_Bounds == other.m_Bounds && m_LocalToWorldMatrix == other.m_LocalToWorldMatrix && m_HdrData == other.m_HdrData && m_Center == other.m_Center
            && m_BlendDistance == other.m_BlendDistance && m_Importance == other.m_Importance && m_BoxProjection == other.m_BoxProjection
            && m_InstanceId == other.m_InstanceId && m_TextureId == other.m_TextureId;
        public override bool Equals(object obj) => obj is VisibleReflectionProbe other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_Bounds, m_LocalToWorldMatrix, m_HdrData, m_Center, m_BlendDistance, m_Importance, m_InstanceId, m_TextureId);
        public static bool operator ==(VisibleReflectionProbe left, VisibleReflectionProbe right) => left.Equals(right);
        public static bool operator !=(VisibleReflectionProbe left, VisibleReflectionProbe right) => !left.Equals(right);
    }

    public partial struct AttachmentDescriptor : IEquatable<AttachmentDescriptor>
    {
        RenderBufferLoadAction m_LoadAction;
        RenderBufferStoreAction m_StoreAction;
        GraphicsFormat m_Format;
        RenderTargetIdentifier m_LoadStoreTarget;
        RenderTargetIdentifier m_ResolveTarget;
        Color m_ClearColor;
        float m_ClearDepth;
        uint m_ClearStencil;

        public AttachmentDescriptor(GraphicsFormat format)
        {
            this = default;
            m_LoadAction = RenderBufferLoadAction.DontCare;
            m_StoreAction = RenderBufferStoreAction.DontCare;
            m_Format = format;
            m_LoadStoreTarget = new RenderTargetIdentifier(BuiltinRenderTextureType.None);
            m_ResolveTarget = new RenderTargetIdentifier(BuiltinRenderTextureType.None);
            m_ClearColor = new Color(0f, 0f, 0f, 0f);
            m_ClearDepth = 1f;
        }

        public AttachmentDescriptor(RenderTextureFormat format)
            : this(GraphicsFormatUtility.GetGraphicsFormat(format, RenderTextureReadWrite.Default)) { }

        public AttachmentDescriptor(RenderTextureFormat format, RenderTargetIdentifier target, bool loadExistingContents = false, bool storeResults = false, bool resolve = false)
            : this(format)
        {
            ConfigureTarget(target, loadExistingContents, storeResults);
            if (resolve) ConfigureResolveTarget(target);
        }

        public RenderBufferLoadAction loadAction { get => m_LoadAction; set => m_LoadAction = value; }
        public RenderBufferStoreAction storeAction { get => m_StoreAction; set => m_StoreAction = value; }
        public GraphicsFormat graphicsFormat { get => m_Format; set => m_Format = value; }
        public RenderTextureFormat format { get => GraphicsFormatUtility.GetRenderTextureFormat(m_Format); set => m_Format = GraphicsFormatUtility.GetGraphicsFormat(value, RenderTextureReadWrite.Default); }
        public RenderTargetIdentifier loadStoreTarget { get => m_LoadStoreTarget; set => m_LoadStoreTarget = value; }
        public RenderTargetIdentifier resolveTarget { get => m_ResolveTarget; set => m_ResolveTarget = value; }
        public Color clearColor { get => m_ClearColor; set => m_ClearColor = value; }
        public float clearDepth { get => m_ClearDepth; set => m_ClearDepth = value; }
        public uint clearStencil { get => m_ClearStencil; set => m_ClearStencil = value; }

        public void ConfigureTarget(RenderTargetIdentifier target, bool loadExistingContents, bool storeResults)
        {
            m_LoadStoreTarget = target;
            if (loadExistingContents && m_LoadAction != RenderBufferLoadAction.Clear) m_LoadAction = RenderBufferLoadAction.Load;
            if (storeResults)
                m_StoreAction = m_StoreAction == RenderBufferStoreAction.StoreAndResolve || m_StoreAction == RenderBufferStoreAction.Resolve
                    ? RenderBufferStoreAction.StoreAndResolve : RenderBufferStoreAction.Store;
        }

        public void ConfigureResolveTarget(RenderTargetIdentifier target)
        {
            m_ResolveTarget = target;
            m_StoreAction = m_StoreAction == RenderBufferStoreAction.Store || m_StoreAction == RenderBufferStoreAction.StoreAndResolve
                ? RenderBufferStoreAction.StoreAndResolve : RenderBufferStoreAction.Resolve;
        }

        public void ConfigureClear(Color clearColor, float clearDepth = 1f, uint clearStencil = 0u)
        {
            m_ClearColor = clearColor;
            m_ClearDepth = clearDepth;
            m_ClearStencil = clearStencil;
            m_LoadAction = RenderBufferLoadAction.Clear;
        }

        public bool Equals(AttachmentDescriptor other) =>
            m_LoadAction == other.m_LoadAction && m_StoreAction == other.m_StoreAction && m_Format == other.m_Format
            && m_LoadStoreTarget == other.m_LoadStoreTarget && m_ResolveTarget == other.m_ResolveTarget && m_ClearColor == other.m_ClearColor
            && m_ClearDepth == other.m_ClearDepth && m_ClearStencil == other.m_ClearStencil;
        public override bool Equals(object obj) => obj is AttachmentDescriptor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_LoadAction, m_StoreAction, m_Format, m_LoadStoreTarget, m_ResolveTarget, m_ClearColor, m_ClearDepth, m_ClearStencil);
        public static bool operator ==(AttachmentDescriptor left, AttachmentDescriptor right) => left.Equals(right);
        public static bool operator !=(AttachmentDescriptor left, AttachmentDescriptor right) => !left.Equals(right);
    }

    public partial struct AttachmentIndexArray
    {
        public static AttachmentIndexArray Emtpy = new AttachmentIndexArray(0);
        public const int MaxAttachments = 8;

        int a0, a1, a2, a3, a4, a5, a6, a7;
        int activeAttachments;

        public AttachmentIndexArray(int numAttachments)
        {
            if (numAttachments < 0 || numAttachments > MaxAttachments)
                throw new ArgumentException($"AttachmentIndexArray - numAttachments must be in range of [0, {MaxAttachments}[");
            a0 = a1 = a2 = a3 = a4 = a5 = a6 = a7 = -1;
            activeAttachments = numAttachments;
        }

        public AttachmentIndexArray(int[] attachments) : this(attachments.Length)
        {
            for (int i = 0; i < attachments.Length; i++) this[i] = attachments[i];
        }

        public AttachmentIndexArray(NativeArray<int> attachments) : this(attachments.Length)
        {
            for (int i = 0; i < attachments.Length; i++) this[i] = attachments[i];
        }

        public unsafe int this[int index]
        {
            get
            {
                if ((uint)index >= MaxAttachments) throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {MaxAttachments}[");
                if ((uint)index >= (uint)activeAttachments) throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {activeAttachments}[");
                fixed (int* p = &a0) return p[index];
            }
            set
            {
                if ((uint)index >= MaxAttachments) throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {MaxAttachments}[");
                if ((uint)index >= (uint)activeAttachments) throw new IndexOutOfRangeException($"AttachmentIndexArray - index must be in range of [0, {activeAttachments}[");
                fixed (int* p = &a0) p[index] = value;
            }
        }

        public int Length => activeAttachments;
    }

    public partial struct SubPassDescriptor
    {
        public AttachmentIndexArray inputs;
        public AttachmentIndexArray colorOutputs;
        public SubPassFlags flags;
    }
}

namespace UnityEngine.Rendering.RendererUtils
{
    /// <summary>A renderer list's description: pass names, culling results, camera, filters and
    /// overrides; turned into RendererListParams.</summary>
    public partial struct RendererListDesc
    {
        public SortingCriteria sortingCriteria;
        public PerObjectData rendererConfiguration;
        public RenderQueueRange renderQueueRange;
        public RenderStateBlock? stateBlock;
        public Shader overrideShader;
        public Material overrideMaterial;
        public bool excludeObjectMotionVectors;
        public int layerMask;
        public uint renderingLayerMask;
        public int overrideMaterialPassIndex;
        public int overrideShaderPassIndex;

        static readonly ShaderTagId s_EmptyName = new ShaderTagId("");

        public uint batchLayerMask { get; set; }
        internal CullingResults cullingResult { get; private set; }
        internal Camera camera { get; set; }
        internal ShaderTagId passName { get; private set; }
        internal ShaderTagId[] passNames { get; private set; }

        public RendererListDesc(ShaderTagId passName, CullingResults cullingResult, Camera camera) : this()
        {
            this.passName = passName;
            passNames = null;
            this.cullingResult = cullingResult;
            this.camera = camera;
            layerMask = -1;
            renderingLayerMask = uint.MaxValue;
            batchLayerMask = uint.MaxValue;
            overrideMaterialPassIndex = 0;
            overrideShaderPassIndex = 0;
        }

        public RendererListDesc(ShaderTagId[] passNames, CullingResults cullingResult, Camera camera) : this()
        {
            this.passNames = passNames;
            passName = ShaderTagId.none;
            this.cullingResult = cullingResult;
            this.camera = camera;
            layerMask = -1;
            renderingLayerMask = uint.MaxValue;
            batchLayerMask = uint.MaxValue;
            overrideMaterialPassIndex = 0;
            overrideShaderPassIndex = 0;
        }

        public bool IsValid()
        {
            if (camera == null || (passName == ShaderTagId.none && (passNames == null || passNames.Length == 0)))
                return false;
            return true;
        }

        public static RendererListParams ConvertToParameters(in RendererListDesc desc)
        {
            if (!desc.IsValid()) return RendererListParams.Invalid;
            var sortingSettings = new SortingSettings(desc.camera) { criteria = desc.sortingCriteria };
            var drawSettings = new DrawingSettings(s_EmptyName, sortingSettings) { perObjectData = desc.rendererConfiguration };
            if (desc.passName != ShaderTagId.none)
            {
                drawSettings.SetShaderPassName(0, desc.passName);
            }
            else
            {
                for (int i = 0; i < desc.passNames.Length; i++) drawSettings.SetShaderPassName(i, desc.passNames[i]);
            }
            if (desc.overrideShader != null)
            {
                drawSettings.overrideShader = desc.overrideShader;
                drawSettings.overrideShaderPassIndex = desc.overrideShaderPassIndex;
            }
            if (desc.overrideMaterial != null)
            {
                drawSettings.overrideMaterial = desc.overrideMaterial;
                drawSettings.overrideMaterialPassIndex = desc.overrideMaterialPassIndex;
            }
            var filterSettings = new FilteringSettings(desc.renderQueueRange, desc.layerMask, desc.renderingLayerMask)
            {
                excludeMotionVectorObjects = desc.excludeObjectMotionVectors,
                batchLayerMask = desc.batchLayerMask,
            };
            var p = new RendererListParams(desc.cullingResult, drawSettings, filterSettings)
            {
                tagName = ShaderTagId.none,
                isPassTagName = false,
            };
            if (desc.stateBlock.HasValue)
            {
                p.stateBlocks = new NativeArray<RenderStateBlock>(1, Allocator.Temp) { [0] = desc.stateBlock.Value };
                p.tagValues = new NativeArray<ShaderTagId>(1, Allocator.Temp) { [0] = ShaderTagId.none };
            }
            return p;
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial struct LightShadowCasterCullingInfo
    {
        public RangeInt splitRange;
        public BatchCullingProjectionType projectionType;
        public ushort splitExclusionMask;
    }

    public partial struct ShadowCastersCullingInfos
    {
        public Unity.Collections.NativeArray<ShadowSplitData> splitBuffer;
        public Unity.Collections.NativeArray<LightShadowCasterCullingInfo> perLightInfos;
    }

    /// <summary>What a shadow caster pass draws: a light's casters from a cull.</summary>
    public partial struct ShadowDrawingSettings : IEquatable<ShadowDrawingSettings>
    {
        CullingResults m_CullingResults;
        int m_LightIndex;
        int m_UseRenderingLayerMaskTest;
        ShadowSplitData m_SplitData;
        ShadowObjectsFilter m_ObjectsFilter;
        BatchCullingProjectionType m_ProjectionType;
        uint m_BatchLayerMask;
        int m_SplitIndex;

        public ShadowDrawingSettings(CullingResults cullingResults, int lightIndex)
        {
            m_CullingResults = cullingResults;
            m_LightIndex = lightIndex;
            m_UseRenderingLayerMaskTest = 0;
            m_SplitData = default;
            m_ObjectsFilter = ShadowObjectsFilter.AllObjects;
            m_ProjectionType = BatchCullingProjectionType.Unknown;
            m_BatchLayerMask = uint.MaxValue;
            m_SplitIndex = 0;
        }

        [Obsolete("ShadowDrawingSettings(CullingResults, int, BatchCullingProjectionType) is deprecated. Use ShadowDrawingSettings(CullingResults, int) instead.")]
        public ShadowDrawingSettings(CullingResults cullingResults, int lightIndex, BatchCullingProjectionType projectionType) : this(cullingResults, lightIndex) =>
            m_ProjectionType = projectionType;

        public CullingResults cullingResults { get => m_CullingResults; set => m_CullingResults = value; }
        public int lightIndex { get => m_LightIndex; set => m_LightIndex = value; }
        public int splitIndex { get => m_SplitIndex; set => m_SplitIndex = value; }
        public bool useRenderingLayerMaskTest { get => m_UseRenderingLayerMaskTest != 0; set => m_UseRenderingLayerMaskTest = value ? 1 : 0; }
        public uint batchLayerMask { get => m_BatchLayerMask; set => m_BatchLayerMask = value; }
        [Obsolete("ShadowDrawingSettings.splitData is deprecated. The equivalent data must be passed to ScriptableRenderContext.CullShadowCasters.")]
        public ShadowSplitData splitData { get => m_SplitData; set => m_SplitData = value; }
        public ShadowObjectsFilter objectsFilter { get => m_ObjectsFilter; set => m_ObjectsFilter = value; }
        [Obsolete("ShadowDrawingSettings.projectionType is deprecated. There is no replacement for this parameter. You don't need to set it anymore.")]
        public BatchCullingProjectionType projectionType { get => m_ProjectionType; set => m_ProjectionType = value; }

        public bool Equals(ShadowDrawingSettings other) =>
            m_CullingResults == other.m_CullingResults && m_LightIndex == other.m_LightIndex && m_SplitIndex == other.m_SplitIndex
            && m_UseRenderingLayerMaskTest == other.m_UseRenderingLayerMaskTest && m_ObjectsFilter == other.m_ObjectsFilter && m_BatchLayerMask == other.m_BatchLayerMask;
        public override bool Equals(object obj) => obj is ShadowDrawingSettings other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_CullingResults, m_LightIndex, m_SplitIndex, m_ObjectsFilter);
        public static bool operator ==(ShadowDrawingSettings left, ShadowDrawingSettings right) => left.Equals(right);
        public static bool operator !=(ShadowDrawingSettings left, ShadowDrawingSettings right) => !left.Equals(right);
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>Color targets with their load/store actions and a depth target.</summary>
    public partial struct RenderTargetBinding
    {
        RenderTargetIdentifier[] m_ColorRenderTargets;
        RenderTargetIdentifier m_DepthRenderTarget;
        RenderBufferLoadAction[] m_ColorLoadActions;
        RenderBufferStoreAction[] m_ColorStoreActions;
        RenderBufferLoadAction m_DepthLoadAction;
        RenderBufferStoreAction m_DepthStoreAction;
        RenderTargetFlags m_Flags;

        public RenderTargetBinding(RenderTargetIdentifier[] colorRenderTargets, RenderBufferLoadAction[] colorLoadActions, RenderBufferStoreAction[] colorStoreActions,
            RenderTargetIdentifier depthRenderTarget, RenderBufferLoadAction depthLoadAction, RenderBufferStoreAction depthStoreAction)
        {
            m_ColorRenderTargets = colorRenderTargets;
            m_DepthRenderTarget = depthRenderTarget;
            m_ColorLoadActions = colorLoadActions;
            m_ColorStoreActions = colorStoreActions;
            m_DepthLoadAction = depthLoadAction;
            m_DepthStoreAction = depthStoreAction;
            m_Flags = RenderTargetFlags.None;
        }

        public RenderTargetBinding(RenderTargetIdentifier colorRenderTarget, RenderBufferLoadAction colorLoadAction, RenderBufferStoreAction colorStoreAction,
            RenderTargetIdentifier depthRenderTarget, RenderBufferLoadAction depthLoadAction, RenderBufferStoreAction depthStoreAction)
            : this(new[] { colorRenderTarget }, new[] { colorLoadAction }, new[] { colorStoreAction }, depthRenderTarget, depthLoadAction, depthStoreAction) { }

        public RenderTargetIdentifier[] colorRenderTargets { get => m_ColorRenderTargets; set => m_ColorRenderTargets = value; }
        public RenderTargetIdentifier depthRenderTarget { get => m_DepthRenderTarget; set => m_DepthRenderTarget = value; }
        public RenderBufferLoadAction[] colorLoadActions { get => m_ColorLoadActions; set => m_ColorLoadActions = value; }
        public RenderBufferStoreAction[] colorStoreActions { get => m_ColorStoreActions; set => m_ColorStoreActions = value; }
        public RenderBufferLoadAction depthLoadAction { get => m_DepthLoadAction; set => m_DepthLoadAction = value; }
        public RenderBufferStoreAction depthStoreAction { get => m_DepthStoreAction; set => m_DepthStoreAction = value; }
        public RenderTargetFlags flags { get => m_Flags; set => m_Flags = value; }
    }
}
