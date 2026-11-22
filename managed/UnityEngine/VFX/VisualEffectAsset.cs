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

        /// <summary>An asset is made by an import; one made with <c>new</c> has no engine
        /// object behind it, as in the Editor.</summary>
        public VisualEffectAsset()
        {
            if (Compat.UncreatedObjectWrappersMeasured && !ObjectConstruction.TakeBacking(this)) MarkUncreated();
        }

        /// <summary>A new, empty asset object (what an import fills).</summary>
        internal static VisualEffectAsset Create() => (VisualEffectAsset)ObjectConstruction.CreateWithBacking(typeof(VisualEffectAsset));

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

        /// <summary>The settings as a graph file or a built asset keeps them (its <c>m_Infos</c>).</summary>
        internal static VisualEffectSettings Read(YNode infos)
        {
            var settings = Default;
            var renderer = infos.Get("m_RendererSettings");
            if (renderer != null)
            {
                settings.Renderer = new VFXRendererSettings
                {
                    motionVectorGenerationMode = (MotionVectorGenerationMode)renderer.Int("motionVectorGenerationMode"),
                    shadowCastingMode = (ShadowCastingMode)renderer.Int("shadowCastingMode"),
                    rayTracingMode = (RayTracingAccelerationStructure.RayTracingModeMask)renderer.Int("rayTracingMode"),
                    receiveShadows = renderer.Bool("receiveShadows"),
                    reflectionProbeUsage = (ReflectionProbeUsage)renderer.Int("reflectionProbeUsage"),
                    lightProbeUsage = (LightProbeUsage)renderer.Int("lightProbeUsage"),
                };
            }
            settings.CullingFlags = (VFXCullingFlags)infos.Int("m_CullingFlags", (int)VFXCullingFlags.CullDefault);
            settings.UpdateMode = (VFXUpdateMode)infos.Int("m_UpdateMode");
            settings.PreWarmDeltaTime = infos.Float("m_PreWarmDeltaTime", 0.05f);
            settings.PreWarmStepCount = (uint)infos.Long("m_PreWarmStepCount");
            settings.InitialEventName = infos.Str("m_InitialEventName", VisualEffectAsset.PlayEventName);
            settings.InstancingMode = (VFXInstancingMode)infos.Int("m_InstancingMode");
            settings.InstancingCapacity = (uint)infos.Long("m_InstancingCapacity", 64);
            return settings;
        }

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
        /// <summary>Per system and task: what an output's generated shader does that is not
        /// drawn yet (<see cref="Unbound.Runtime.VFXOutput.SourceRefusal"/>), null when
        /// nothing; empty for data that was not imported from a graph.</summary>
        internal string[][] OutputRefusals = Array.Empty<string[]>();
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
        /// <summary>Per event: its name's id.</summary>
        internal int[] EventIds = Array.Empty<int>();

        /// <summary>Per expression (the per-event ones after the others): its value type.</summary>
        internal VFXValueType[] ExpressionTypes = Array.Empty<VFXValueType>();

        /// <summary>Per expression: where an effect keeps its value (<see cref="VisualEffectValues"/>):
        /// the first of its 32-bit words, or its object slot for an object type; -1 without a value.</summary>
        internal int[] ValueOffsets = Array.Empty<int>();

        /// <summary>Per expression: the index of its constant in <see cref="Values"/>, -1 for a computed one.</summary>
        internal int[] ConstantOf = Array.Empty<int>();

        /// <summary>Per expression: its operation, and its four operands (parents, a type, a
        /// component, as the operation takes them).</summary>
        internal VFXExpressionOperation[] Operations = Array.Empty<VFXExpressionOperation>();
        internal int[] Operands = Array.Empty<int>();

        /// <summary>How many words and object slots the values of all expressions take.</summary>
        internal int WordCount, ObjectCount;
        /// <summary>Per expression: the place of a baked curve or gradient in the effect's
        /// texture (<see cref="Unbound.Runtime.VFXBakedTexture"/>), -1 for any other.</summary>
        internal int[] BakePlaces = Array.Empty<int>();
        /// <summary>How many curves and gradients the graph bakes.</summary>
        internal int BakedCurves, BakedGradients;

        /// <summary>Whether the sheet reads the main camera, and which of its buffers.</summary>
        internal bool ReadsMainCamera;
        internal VFXCameraBufferTypes CameraBuffers;

        /// <summary>The layout of the events' attributes: the first CPU buffer, by attribute name id.</summary>
        internal int[] EventAttributeIds = Array.Empty<int>();

        /// <summary>Resolves the names lookups go by and lays out the expression values; called
        /// once the arrays are set.</summary>
        internal void Seal()
        {
            // A graph may leave a name out (a system without one): it answers to the empty name.
            m_ExposedIds = new int[Exposed.Length];
            for (int i = 0; i < Exposed.Length; i++) m_ExposedIds[i] = Shader.PropertyToID(Exposed[i].mapping.name ?? "");
            m_SystemIds = new int[Systems.Length];
            for (int i = 0; i < Systems.Length; i++) m_SystemIds[i] = Shader.PropertyToID(Systems[i].name ?? "");
            EventIds = new int[Events.Length];
            for (int i = 0; i < Events.Length; i++) EventIds[i] = Shader.PropertyToID(Events[i].name ?? "");

            int count = Expressions.Length + ExpressionsPerSpawnEventAttribute.Length;
            ExpressionTypes = new VFXValueType[count];
            ValueOffsets = new int[count];
            ConstantOf = new int[count];
            WordCount = ObjectCount = 0;
            Operations = new VFXExpressionOperation[count];
            Operands = new int[count * 4];
            for (int i = 0; i < count; i++)
            {
                var desc = i < Expressions.Length ? Expressions[i] : ExpressionsPerSpawnEventAttribute[i - Expressions.Length];
                Operations[i] = desc.op;
                for (int k = 0; k < 4; k++) Operands[i * 4 + k] = desc.data != null && k < desc.data.Length ? desc.data[k] : -1;
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
            PlaceBakes(count);
            ReadsMainCamera = false;
            CameraBuffers = VFXCameraBufferTypes.None;
            for (int i = 0; i < Expressions.Length; i++)
            {
                var op = Operations[i];
                if (op < VFXExpressionOperation.ExtractMatrixFromMainCamera || op > VFXExpressionOperation.GetOrthographicSizeFromMainCamera) continue;
                ReadsMainCamera = true;
                // The kind of buffer is the operation's fourth operand.
                if (op == VFXExpressionOperation.GetBufferFromMainCamera) CameraBuffers |= (VFXCameraBufferTypes)Operands[i * 4 + 3];
            }

            var layout = CpuBuffers.Length > 0 ? CpuBuffers[0].layout : null;
            EventAttributeIds = new int[layout?.Length ?? 0];
            for (int i = 0; i < EventAttributeIds.Length; i++) EventAttributeIds[i] = Shader.PropertyToID(layout[i].name);
        }

        /// <summary>Gives each baked curve and gradient its place, in the order of the
        /// expressions: first the ones whose curve is a value of the graph that is not
        /// exposed (the same for every effect of the asset), then the ones an effect can have
        /// of its own (an exposed curve, or one that is computed). The layout is Unbound's:
        /// the programs read a place only through the words baked for it, and each effect
        /// has a texture of its own, so any layout without two bakes in one place draws the
        /// same. Only the sheet's own expressions are placed; a bake among the ones
        /// evaluated per spawn event is refused (<see cref="UnplacedBake"/>).</summary>
        void PlaceBakes(int count)
        {
            BakePlaces = new int[count];
            Array.Fill(BakePlaces, -1);
            var own = new bool[count];
            int sharedCurves = 0, ownCurves = 0, sharedGradients = 0, ownGradients = 0;
            // The sheet's own expressions; the ones evaluated per spawn event bake nothing.
            int sheet = Math.Min(count, Expressions.Length);
            for (int i = 0; i < sheet; i++)
            {
                bool curve = Operations[i] == VFXExpressionOperation.BakeCurve;
                if (!curve && Operations[i] != VFXExpressionOperation.BakeGradient) continue;
                int parent = Operands[i * 4];
                own[i] = !((uint)parent < (uint)count && Operations[parent] == VFXExpressionOperation.Value) || IsExposed(parent);
                BakePlaces[i] = curve ? (own[i] ? ownCurves++ : sharedCurves++) : (own[i] ? ownGradients++ : sharedGradients++);
            }
            for (int i = 0; i < sheet; i++)
                if (own[i]) BakePlaces[i] += Operations[i] == VFXExpressionOperation.BakeCurve ? sharedCurves : sharedGradients;
            BakedCurves = sharedCurves + ownCurves;
            BakedGradients = sharedGradients + ownGradients;
        }

        /// <summary>Why a built asset's graph does not run: its programs were built for a
        /// graphics device and the project has no graph that compiles to the same graph.</summary>
        internal string BuiltRefusal;

        /// <summary>Whether two compiled graphs are the same graph (the programs of one run the
        /// other): the same expressions, systems with the same tasks and mappings, events, buffers
        /// and exposed names, and the same compiled version. The constants may differ.</summary>
        internal bool SameGraph(VisualEffectCompiledData other)
        {
            if (CompiledVersion != other.CompiledVersion || !SameExpressions(Expressions, other.Expressions)
                || !SameExpressions(ExpressionsPerSpawnEventAttribute, other.ExpressionsPerSpawnEventAttribute)
                || Systems.Length != other.Systems.Length || Events.Length != other.Events.Length || GpuBuffers.Length != other.GpuBuffers.Length
                || CpuBuffers.Length != other.CpuBuffers.Length || Exposed.Length != other.Exposed.Length)
                return false;
            for (int i = 0; i < Exposed.Length; i++)
                if (Exposed[i].mapping.name != other.Exposed[i].mapping.name || Exposed[i].mapping.index != other.Exposed[i].mapping.index
                    || Exposed[i].space != other.Exposed[i].space)
                    return false;
            for (int i = 0; i < Events.Length; i++)
                if (Events[i].name != other.Events[i].name) return false;
            for (int i = 0; i < GpuBuffers.Length; i++)
                if (!SameBuffer(GpuBuffers[i], other.GpuBuffers[i])) return false;
            for (int i = 0; i < CpuBuffers.Length; i++)
                if (CpuBuffers[i].capacity != other.CpuBuffers[i].capacity || CpuBuffers[i].stride != other.CpuBuffers[i].stride
                    || !SameLayout(CpuBuffers[i].layout, other.CpuBuffers[i].layout))
                    return false;
            if (TemporaryBuffers.Length != other.TemporaryBuffers.Length) return false;
            for (int i = 0; i < TemporaryBuffers.Length; i++)
                if (TemporaryBuffers[i].frameCount != other.TemporaryBuffers[i].frameCount || !SameBuffer(TemporaryBuffers[i].desc, other.TemporaryBuffers[i].desc)) return false;
            for (int s = 0; s < Systems.Length; s++)
            {
                var a = Systems[s];
                var b = other.Systems[s];
                if (a.type != b.type || a.flags != b.flags || a.capacity != b.capacity || a.layer != b.layer || a.name != b.name
                    || !SameMappings(a.buffers, b.buffers) || !SameMappings(a.values, b.values))
                    return false;
                var ta = a.tasks ?? Array.Empty<VFXEditorTaskDesc>();
                var tb = b.tasks ?? Array.Empty<VFXEditorTaskDesc>();
                if (ta.Length != tb.Length) return false;
                for (int t = 0; t < ta.Length; t++)
                {
                    if (ta[t].type != tb[t].type || ta[t].instanceSplitIndex != tb[t].instanceSplitIndex || !SameMappings(ta[t].buffers, tb[t].buffers)
                        || !SameMappings(ta[t].values, tb[t].values) || !SameMappings(ta[t].parameters, tb[t].parameters))
                        return false;
                    var pa = ta[t].temporaryBuffers ?? Array.Empty<VFXMappingTemporary>();
                    var pb = tb[t].temporaryBuffers ?? Array.Empty<VFXMappingTemporary>();
                    if (pa.Length != pb.Length) return false;
                    for (int i = 0; i < pa.Length; i++)
                        if (pa[i].mapping.name != pb[i].mapping.name || pa[i].mapping.index != pb[i].mapping.index
                            || pa[i].pastFrameIndex != pb[i].pastFrameIndex || pa[i].perCameraBuffer != pb[i].perCameraBuffer)
                            return false;
                }
                var sa = a.instanceSplitDescs ?? Array.Empty<VFXInstanceSplitDesc>();
                var sb = b.instanceSplitDescs ?? Array.Empty<VFXInstanceSplitDesc>();
                if (sa.Length != sb.Length) return false;
                for (int i = 0; i < sa.Length; i++)
                    if (!(sa[i].values ?? Array.Empty<uint>()).AsSpan().SequenceEqual(sb[i].values ?? Array.Empty<uint>())) return false;
            }
            return true;
        }

        static bool SameExpressions(VFXExpressionDesc[] a, VFXExpressionDesc[] b)
        {
            a ??= Array.Empty<VFXExpressionDesc>();
            b ??= Array.Empty<VFXExpressionDesc>();
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].op != b[i].op) return false;
                for (int k = 0; k < 4; k++)
                    if ((a[i].data != null && k < a[i].data.Length ? a[i].data[k] : -1) != (b[i].data != null && k < b[i].data.Length ? b[i].data[k] : -1)) return false;
            }
            return true;
        }

        static bool SameBuffer(in VFXGPUBufferDesc a, in VFXGPUBufferDesc b) =>
            a.target == b.target && a.size == b.size && a.stride == b.stride && a.capacity == b.capacity && a.mode == b.mode && SameLayout(a.layout, b.layout);

        static bool SameLayout(VFXLayoutElementDesc[] a, VFXLayoutElementDesc[] b)
        {
            a ??= Array.Empty<VFXLayoutElementDesc>();
            b ??= Array.Empty<VFXLayoutElementDesc>();
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].name != b[i].name || a[i].type != b[i].type || a[i].offset.bucket != b[i].offset.bucket
                    || a[i].offset.structure != b[i].offset.structure || a[i].offset.element != b[i].offset.element)
                    return false;
            return true;
        }

        static bool SameMappings(VFXMapping[] a, VFXMapping[] b)
        {
            a ??= Array.Empty<VFXMapping>();
            b ??= Array.Empty<VFXMapping>();
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].name != b[i].name || a[i].index != b[i].index) return false;
            return true;
        }

        /// <summary>Whether a bake has no place: the effect does not run then, and says so.</summary>
        internal bool UnplacedBake
        {
            get
            {
                for (int i = 0; i < Operations.Length; i++)
                    if ((Operations[i] == VFXExpressionOperation.BakeCurve || Operations[i] == VFXExpressionOperation.BakeGradient) && BakePlaces[i] < 0) return true;
                return false;
            }
        }

        bool IsExposed(int expression)
        {
            foreach (var exposed in Exposed)
                if (exposed.mapping.index == expression) return true;
            return false;
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
