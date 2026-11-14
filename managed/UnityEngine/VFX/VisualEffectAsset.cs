using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;

namespace UnityEngine.VFX
{
    /// <summary>What a Visual Effect Graph file imports to: an effect asset or a subgraph.</summary>
    [ClassId(2059678085)]
    public abstract partial class VisualEffectObject : Object
    {
        protected VisualEffectObject() { }
    }

    /// <summary>An exposed property of an asset: its name and the type its value has.</summary>
    public partial struct VFXExposedProperty
    {
        public string name;
        public Type type;
    }

    /// <summary>
    /// A compiled Visual Effect Graph: what the graph package's compiler produced from the
    /// <c>.vfx</c> file (<see cref="VisualEffectCompiledData"/>) and the asset's settings.
    /// </summary>
    [ClassId(2058629509)]
    public partial class VisualEffectAsset : VisualEffectObject
    {
        public const string PlayEventName = "OnPlay";
        public const string StopEventName = "OnStop";
        public static readonly int PlayEventID = Shader.PropertyToID(PlayEventName);
        public static readonly int StopEventID = Shader.PropertyToID(StopEventName);

        /// <summary>The version of the runtime data layout a compiled asset is marked with
        /// (Unity 6000.0.71f1; the package compares it with the project's VFXManager settings).</summary>
        internal const uint RuntimeDataVersion = 38;

        internal VisualEffectSettings m_Settings = VisualEffectSettings.Default;
        internal VisualEffectCompiledData m_Data = VisualEffectCompiledData.Empty;

        /// <summary>Per system and task: the compiled shader source its processor was made
        /// from, -1 for a task with an object of its own.</summary>
        internal int[][] m_ShaderSourceIndices = Array.Empty<int[]>();

        /// <summary>The objects the import made for this asset (shaders, compute shaders,
        /// materials): the next import replaces them.</summary>
        internal Object[] m_Imported = Array.Empty<Object>();

        public VisualEffectAsset() { }

        internal static uint currentRuntimeDataVersion => RuntimeDataVersion;

        internal uint GetCompilationVersion() => m_Data.CompiledVersion;

        public TextureDimension GetTextureDimension(string name) => GetTextureDimension(Shader.PropertyToID(name));

        /// <summary>The dimension of an exposed texture; Unknown when the asset exposes no
        /// texture of that name.</summary>
        public TextureDimension GetTextureDimension(int nameID)
        {
            int index = m_Data.ExposedIndex(nameID);
            return index < 0 ? TextureDimension.Unknown : VFXExpressionHelper.GetTextureDimension(m_Data.ExposedType(index));
        }

        public VFXSpace GetExposedSpace(string name) => GetExposedSpace(Shader.PropertyToID(name));

        /// <summary>The space of an exposed spaceable property; None for other properties and
        /// unknown names.</summary>
        public VFXSpace GetExposedSpace(int nameID)
        {
            int index = m_Data.ExposedIndex(nameID);
            return index < 0 ? VFXSpace.None : m_Data.Exposed[index].space;
        }

        /// <summary>The asset's exposed properties, in the compiled sheet's order, each with the
        /// managed type of its value.</summary>
        public void GetExposedProperties(List<VFXExposedProperty> exposedProperties)
        {
            if (exposedProperties == null) throw new ArgumentNullException(nameof(exposedProperties));
            exposedProperties.Clear();
            for (int i = 0; i < m_Data.Exposed.Length; i++)
            {
                exposedProperties.Add(new VFXExposedProperty
                {
                    name = m_Data.Exposed[i].mapping.name,
                    type = VisualEffectCompiledData.ManagedType(m_Data.ExposedType(i)),
                });
            }
        }

        /// <summary>The names of the events the asset's systems answer to.</summary>
        public void GetEvents(List<string> names)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            names.Clear();
            foreach (var e in m_Data.Events) names.Add(e.name);
        }

        internal bool HasSystem(int nameID) => m_Data.SystemIndex(nameID) >= 0;

        internal void GetSystemNames(List<string> names) => m_Data.SystemNames(names, null);

        internal void GetParticleSystemNames(List<string> names) => m_Data.SystemNames(names, VFXSystemType.Particle);

        internal void GetOutputEventNames(List<string> names) => m_Data.SystemNames(names, VFXSystemType.OutputEvent);

        internal void GetSpawnSystemNames(List<string> names) => m_Data.SystemNames(names, VFXSystemType.Spawner);
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using UnityEngine.VFX;

    /// <summary>The settings a graph file keeps for its asset (its resource's <c>m_Infos</c>).</summary>
    internal struct VisualEffectSettings
    {
        internal VFXRendererSettings Renderer;
        internal VFXCullingFlags CullingFlags;
        internal VFXUpdateMode UpdateMode;
        internal float PreWarmDeltaTime;
        internal uint PreWarmStepCount;
        internal string InitialEventName;
        internal VFXInstancingMode InstancingMode;
        internal uint InstancingCapacity;

        /// <summary>A new resource's settings, as the Unity 6000.0.71f1 Editor creates one.</summary>
        internal static VisualEffectSettings Default => new VisualEffectSettings
        {
            CullingFlags = VFXCullingFlags.CullDefault,
            PreWarmDeltaTime = 0.05f,
            InitialEventName = VisualEffectAsset.PlayEventName,
            InstancingCapacity = 64,
        };
    }

    /// <summary>
    /// The compiled form of a graph, as the package's compiler describes it to the engine:
    /// expressions and their constant values, exposed properties, systems with their tasks,
    /// events and buffers. Immutable once an import finished; an asset without a compiled graph
    /// has <see cref="Empty"/>.
    /// </summary>
    internal sealed class VisualEffectCompiledData
    {
        internal static readonly VisualEffectCompiledData Empty = new VisualEffectCompiledData();

        internal VFXExpressionDesc[] Expressions = Array.Empty<VFXExpressionDesc>();
        internal VFXExpressionDesc[] ExpressionsPerSpawnEventAttribute = Array.Empty<VFXExpressionDesc>();
        internal VFXExpressionValueContainerDesc[] Values = Array.Empty<VFXExpressionValueContainerDesc>();
        internal VFXExposedMapping[] Exposed = Array.Empty<VFXExposedMapping>();
        internal VFXEditorSystemDesc[] Systems = Array.Empty<VFXEditorSystemDesc>();
        /// <summary>Per system and task: the object the task runs with (a compute shader, an
        /// output's material, a spawner's callbacks), null for a task without one.</summary>
        internal Object[][] Processors = Array.Empty<Object[]>();
        internal VFXEventDesc[] Events = Array.Empty<VFXEventDesc>();
        internal VFXGPUBufferDesc[] GpuBuffers = Array.Empty<VFXGPUBufferDesc>();
        internal VFXCPUBufferDesc[] CpuBuffers = Array.Empty<VFXCPUBufferDesc>();
        internal VFXTemporaryGPUBufferDesc[] TemporaryBuffers = Array.Empty<VFXTemporaryGPUBufferDesc>();
        internal ShadowCastingMode ShadowCastingMode;
        internal MotionVectorGenerationMode MotionVectorGenerationMode;
        internal VFXInstancingDisabledReason InstancingDisabledReason;
        internal uint CompiledVersion;

        int[] m_ExposedIds = Array.Empty<int>();
        int[] m_SystemIds = Array.Empty<int>();

        /// <summary>Resolves the names lookups go by; called once the arrays are set.</summary>
        internal void Seal()
        {
            m_ExposedIds = new int[Exposed.Length];
            for (int i = 0; i < Exposed.Length; i++) m_ExposedIds[i] = Shader.PropertyToID(Exposed[i].mapping.name);
            m_SystemIds = new int[Systems.Length];
            for (int i = 0; i < Systems.Length; i++) m_SystemIds[i] = Shader.PropertyToID(Systems[i].name);
        }

        /// <summary>The index of the exposed property of a name, -1 without one.</summary>
        internal int ExposedIndex(int nameID) => Array.IndexOf(m_ExposedIds, nameID);

        /// <summary>The index of the system of a name, -1 without one.</summary>
        internal int SystemIndex(int nameID) => Array.IndexOf(m_SystemIds, nameID);

        /// <summary>The value type of an expression of either expression array (the per-event
        /// expressions continue the indices of the main ones).</summary>
        internal VFXValueType ExpressionType(int index)
        {
            if (index < 0) return VFXValueType.None;
            if (index < Expressions.Length) return VFXExpressionHelper.TypeOf(Expressions[index].op, Expressions[index].data);
            index -= Expressions.Length;
            if (index < ExpressionsPerSpawnEventAttribute.Length)
                return VFXExpressionHelper.TypeOf(ExpressionsPerSpawnEventAttribute[index].op, ExpressionsPerSpawnEventAttribute[index].data);
            return VFXValueType.None;
        }

        internal VFXValueType ExposedType(int exposedIndex) => ExpressionType(Exposed[exposedIndex].mapping.index);

        internal void SystemNames(List<string> names, VFXSystemType? type)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            names.Clear();
            foreach (var system in Systems)
                if (type == null || system.type == type.Value) names.Add(system.name);
        }

        /// <summary>The managed type an exposed property of a value type is listed with.</summary>
        internal static Type ManagedType(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.Float: return typeof(float);
                case VFXValueType.Float2: return typeof(Vector2);
                case VFXValueType.Float3: return typeof(Vector3);
                case VFXValueType.Float4: return typeof(Vector4);
                case VFXValueType.Int32: return typeof(int);
                case VFXValueType.Uint32: return typeof(uint);
                case VFXValueType.Texture2D: return typeof(Texture2D);
                case VFXValueType.Texture2DArray: return typeof(Texture2DArray);
                case VFXValueType.Texture3D: return typeof(Texture3D);
                case VFXValueType.TextureCube: return typeof(Cubemap);
                case VFXValueType.TextureCubeArray: return typeof(CubemapArray);
                case VFXValueType.Matrix4x4: return typeof(Matrix4x4);
                case VFXValueType.Curve: return typeof(AnimationCurve);
                case VFXValueType.ColorGradient: return typeof(Gradient);
                case VFXValueType.Mesh: return typeof(Mesh);
                case VFXValueType.Boolean: return typeof(bool);
                case VFXValueType.Buffer: return typeof(GraphicsBuffer);
                case VFXValueType.SkinnedMeshRenderer: return typeof(SkinnedMeshRenderer);
                default: return null;
            }
        }
    }
}
