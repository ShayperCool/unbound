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
        /// <summary>Per CPU buffer: its initial contents, 32-bit words (the compiler's own copy
        /// may be released once it handed the graph over).</summary>
        internal uint[][] CpuInitialWords = Array.Empty<uint[]>();
        internal VFXTemporaryGPUBufferDesc[] TemporaryBuffers = Array.Empty<VFXTemporaryGPUBufferDesc>();
        internal ShadowCastingMode ShadowCastingMode;
        internal MotionVectorGenerationMode MotionVectorGenerationMode;
        internal VFXInstancingDisabledReason InstancingDisabledReason;
        internal uint CompiledVersion;

        int[] m_ExposedIds = Array.Empty<int>();
        int[] m_SystemIds = Array.Empty<int>();

        /// <summary>Per expression (the per-event ones after the others): its value type.</summary>
        internal VFXValueType[] ExpressionTypes = Array.Empty<VFXValueType>();

        /// <summary>Per expression: where an effect keeps its value (<see cref="VisualEffectValues"/>):
        /// the first of its 32-bit words, or its object slot for an object type; -1 without a value.</summary>
        internal int[] ValueOffsets = Array.Empty<int>();

        /// <summary>Per expression: the index of its constant in <see cref="Values"/>, -1 for a computed one.</summary>
        internal int[] ConstantOf = Array.Empty<int>();

        /// <summary>How many words and object slots the values of all expressions take.</summary>
        internal int WordCount, ObjectCount;

        /// <summary>The layout of the events' attributes: the first CPU buffer, by attribute name id.</summary>
        internal int[] EventAttributeIds = Array.Empty<int>();

        /// <summary>Resolves the names lookups go by and lays out the expression values; called
        /// once the arrays are set.</summary>
        internal void Seal()
        {
            m_ExposedIds = new int[Exposed.Length];
            for (int i = 0; i < Exposed.Length; i++) m_ExposedIds[i] = Shader.PropertyToID(Exposed[i].mapping.name);
            m_SystemIds = new int[Systems.Length];
            for (int i = 0; i < Systems.Length; i++) m_SystemIds[i] = Shader.PropertyToID(Systems[i].name);

            int count = Expressions.Length + ExpressionsPerSpawnEventAttribute.Length;
            ExpressionTypes = new VFXValueType[count];
            ValueOffsets = new int[count];
            ConstantOf = new int[count];
            WordCount = ObjectCount = 0;
            for (int i = 0; i < count; i++)
            {
                var type = ExpressionTypes[i] = ExpressionType(i);
                ConstantOf[i] = -1;
                if (type == VFXValueType.None || type > VFXValueType.SkinnedMeshRenderer || type == VFXValueType.Spline) ValueOffsets[i] = -1;
                else if (IsObjectType(type)) ValueOffsets[i] = ObjectCount++;
                else
                {
                    ValueOffsets[i] = WordCount;
                    WordCount += VFXExpressionHelper.GetSizeOfType(type);
                }
            }
            for (int i = 0; i < Values.Length; i++)
                if (Values[i] != null && Values[i].expressionIndex < (uint)count) ConstantOf[Values[i].expressionIndex] = i;

            var layout = CpuBuffers.Length > 0 ? CpuBuffers[0].layout : null;
            EventAttributeIds = new int[layout?.Length ?? 0];
            for (int i = 0; i < EventAttributeIds.Length; i++) EventAttributeIds[i] = Shader.PropertyToID(layout[i].name);
        }

        /// <summary>Whether a value of a type is an object (a texture, mesh, renderer, buffer,
        /// curve or gradient) rather than 32-bit words.</summary>
        internal static bool IsObjectType(VFXValueType type) =>
            IsTextureType(type) || type == VFXValueType.Curve || type == VFXValueType.ColorGradient || type == VFXValueType.Mesh
            || type == VFXValueType.Buffer || type == VFXValueType.SkinnedMeshRenderer;

        /// <summary>The types a texture property can have: the texture dimensions and a camera buffer.</summary>
        internal static bool IsTextureType(VFXValueType type) => type >= VFXValueType.Texture2D && type <= VFXValueType.CameraBuffer;

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

        /// <summary>The index of the exposed property an effect's sheet entry stands for is its
        /// index here: a sheet lists the exposed properties in this order.</summary>
        internal int ExposedCount => Exposed.Length;

        internal int ExposedId(int exposedIndex) => m_ExposedIds[exposedIndex];

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
                case VFXValueType.Texture2D:
                case VFXValueType.Texture2DArray:
                case VFXValueType.Texture3D:
                case VFXValueType.TextureCube:
                case VFXValueType.TextureCubeArray:
                    // Every texture property lists as a Texture; GetTextureDimension tells them apart.
                    return typeof(Texture);
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
