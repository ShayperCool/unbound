using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace UnityEditor.VFX
{
    /// <summary>A subgraph asset: what a <c>.vfxoperator</c> or <c>.vfxblock</c> file imports to.</summary>
    [ClassId(994735392)]
    internal abstract partial class VisualEffectSubgraph : VisualEffectObject
    {
        protected VisualEffectSubgraph() { }
    }

    [ClassId(994735403)]
    internal partial class VisualEffectSubgraphOperator : VisualEffectSubgraph
    {
        public const string Extension = ".vfxoperator";

        public VisualEffectSubgraphOperator() { }
    }

    [ClassId(994735404)]
    internal partial class VisualEffectSubgraphBlock : VisualEffectSubgraph
    {
        public const string Extension = ".vfxblock";

        public VisualEffectSubgraphBlock() { }
    }

    /// <summary>
    /// The object of a graph file that owns the graph (the package's models) and the asset's
    /// settings. The package's compiler hands it the compiled graph
    /// (<see cref="SetRuntimeData"/>); the import copies that to the file's asset
    /// (<see cref="VisualEffectImports"/>). Unbound never writes a graph file: the members that
    /// save one are not implemented.
    /// </summary>
    [ClassId(2058629511)]
    internal partial class VisualEffectResource : Object, ISerializedAsset
    {
        public const string Extension = ".vfx";
        public const int CurrentVersion = 1;
        public const uint uncompiledVersion = 0U;
        public const uint defaultVersion = 1U;

        internal static Func<string, string[]> onAddResourceDependencies;
        internal static Action<VisualEffectResource> onCompileResource;
        internal static Action<VisualEffectResource, Material, Object> onSetupMaterial;

        ScriptableObject m_Graph;
        internal VisualEffectSettings m_Settings = VisualEffectSettings.Default;
        VFXShaderSourceDesc[] m_ShaderSources = Array.Empty<VFXShaderSourceDesc>();
        bool m_CompileInitialVariants;
        readonly List<string> m_SourceDependencies = new List<string>();
        readonly List<string> m_ImportDependencies = new List<string>();

        /// <summary>The graph as last compiled; <see cref="VisualEffectCompiledData.Empty"/>
        /// while there is none.</summary>
        internal VisualEffectCompiledData m_Data = VisualEffectCompiledData.Empty;

        /// <summary>The model each task of <see cref="m_Data"/> was compiled from, per system.</summary>
        internal Object[][] m_Models = Array.Empty<Object[]>();

        /// <summary>The asset object of this resource's file, set by the import.</summary>
        internal VisualEffectObject m_Object;

        /// <summary>Every object the file serializes besides the resource itself.</summary>
        internal Object[] m_Contents = Array.Empty<Object>();

        public VisualEffectResource() { }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Graph = resolver.Resolve(body.Ref("m_Graph"), typeof(ScriptableObject)) as ScriptableObject;
            var infos = body.Get("m_Infos");
            if (infos == null) return;
            var renderer = infos.Get("m_RendererSettings");
            if (renderer != null)
            {
                m_Settings.Renderer = new VFXRendererSettings
                {
                    motionVectorGenerationMode = (MotionVectorGenerationMode)renderer.Int("motionVectorGenerationMode"),
                    shadowCastingMode = (ShadowCastingMode)renderer.Int("shadowCastingMode"),
                    rayTracingMode = (RayTracingAccelerationStructure.RayTracingModeMask)renderer.Int("rayTracingMode"),
                    receiveShadows = renderer.Bool("receiveShadows"),
                    reflectionProbeUsage = (ReflectionProbeUsage)renderer.Int("reflectionProbeUsage"),
                    lightProbeUsage = (LightProbeUsage)renderer.Int("lightProbeUsage"),
                };
            }
            m_Settings.CullingFlags = (VFXCullingFlags)infos.Int("m_CullingFlags", (int)VFXCullingFlags.CullDefault);
            m_Settings.UpdateMode = (VFXUpdateMode)infos.Int("m_UpdateMode");
            m_Settings.PreWarmDeltaTime = infos.Float("m_PreWarmDeltaTime", 0.05f);
            m_Settings.PreWarmStepCount = (uint)infos.Long("m_PreWarmStepCount");
            m_Settings.InitialEventName = infos.Str("m_InitialEventName", VisualEffectAsset.PlayEventName);
            m_Settings.InstancingMode = (VFXInstancingMode)infos.Int("m_InstancingMode");
            m_Settings.InstancingCapacity = (uint)infos.Long("m_InstancingCapacity", 64);
        }

        public ScriptableObject graph
        {
            get => m_Graph;
            set => m_Graph = value;
        }

        public VisualEffectObject visualEffectObject => m_Object;

        public VisualEffectAsset asset => m_Object as VisualEffectAsset;

        public VisualEffectSubgraph subgraph => m_Object as VisualEffectSubgraph;

        public bool isSubgraph => m_Object is VisualEffectSubgraph;

        public VFXRendererSettings rendererSettings
        {
            get => m_Settings.Renderer;
            set => m_Settings.Renderer = value;
        }

        public VFXUpdateMode updateMode
        {
            get => m_Settings.UpdateMode;
            set => m_Settings.UpdateMode = value;
        }

        public float preWarmDeltaTime
        {
            get => m_Settings.PreWarmDeltaTime;
            set => m_Settings.PreWarmDeltaTime = value;
        }

        public uint preWarmStepCount
        {
            get => m_Settings.PreWarmStepCount;
            set => m_Settings.PreWarmStepCount = value;
        }

        public string initialEventName
        {
            get => m_Settings.InitialEventName;
            set => m_Settings.InitialEventName = value;
        }

        public VFXCullingFlags cullingFlags
        {
            get => m_Settings.CullingFlags;
            set => m_Settings.CullingFlags = value;
        }

        public VFXInstancingMode instancingMode
        {
            get => m_Settings.InstancingMode;
            set => m_Settings.InstancingMode = value;
        }

        public uint instancingCapacity
        {
            get => m_Settings.InstancingCapacity;
            set => m_Settings.InstancingCapacity = value;
        }

        public VFXShaderSourceDesc[] shaderSources
        {
            get => m_ShaderSources;
            set => m_ShaderSources = value ?? Array.Empty<VFXShaderSourceDesc>();
        }

        public bool compileInitialVariants
        {
            get => m_CompileInitialVariants;
            set => m_CompileInitialVariants = value;
        }

        public int GetShaderSourceCount() => m_ShaderSources.Length;

        public string GetShaderSourceName(int index) => m_ShaderSources[index].name;

        public string GetShaderSource(int index) => m_ShaderSources[index].source;

        public static VisualEffectResource GetResourceAtPath(string path) => VisualEffectImports.ResourceAtPath(path);

        public Object[] GetContents() => (Object[])m_Contents.Clone();

        /// <summary>Makes a set of objects the file's contents. They become part of the asset
        /// in memory; the file on disk stays as it is (Unbound writes no graph file).</summary>
        public void SetContents(Object[] dependencies)
        {
            var contents = new List<Object>();
            if (dependencies != null)
                foreach (var o in dependencies)
                    if (o != null && !ReferenceEquals(o, this))
                    {
                        o.IsPersistentAsset = true;
                        contents.Add(o);
                    }
            m_Contents = contents.ToArray();
        }

        internal IReadOnlyList<string> SourceDependencies => m_SourceDependencies;

        public void ClearSourceDependencies() => m_SourceDependencies.Clear();

        public void AddSourceDependency(string dep) => m_SourceDependencies.Add(dep);

        public void ClearImportDependencies() => m_ImportDependencies.Clear();

        public void AddImportDependency(string dep) => m_ImportDependencies.Add(dep);

        internal static string[] AddResourceDependencies(string assetPath) =>
            onAddResourceDependencies?.Invoke(assetPath) ?? Array.Empty<string>();

        /// <summary>Asks the graph package to compile this resource's graph.</summary>
        internal void CompileResource() => onCompileResource?.Invoke(this);

        /// <summary>Asks the graph package to set up an output's material from its model.</summary>
        internal void SetupMaterial(Material material, Object model) => onSetupMaterial?.Invoke(this, material, model);

        public void ClearRuntimeData()
        {
            m_Data = VisualEffectCompiledData.Empty;
            m_Models = Array.Empty<Object[]>();
            m_ShaderSources = Array.Empty<VFXShaderSourceDesc>();
        }

        public void MarkRuntimeVersion() { }

        /// <summary>
        /// Takes a compiled graph from the package's compiler. As the Editor, it first drops the
        /// previous one and refuses (ArgumentException, nothing kept) systems whose layers
        /// decrease and an expression sheet with an invalid expression; events naming a name
        /// twice are dropped with an ArgumentException, the rest of the graph kept.
        /// </summary>
        public void SetRuntimeData(VFXExpressionSheet sheet, VFXEditorSystemDesc[] systemDesc, VFXEventDesc[] eventDesc, VFXGPUBufferDesc[] gpuBufferDesc,
            VFXCPUBufferDesc[] cpuBufferDesc, VFXTemporaryGPUBufferDesc[] temporaryBufferDesc, VFXShaderSourceDesc[] shaderSourceDesc,
            ShadowCastingMode shadowCastingMode, MotionVectorGenerationMode motionVectorGenerationMode, VFXInstancingDisabledReason instancingDisabledReason,
            uint version = defaultVersion)
        {
            ClearRuntimeData();
            systemDesc ??= Array.Empty<VFXEditorSystemDesc>();
            var expressions = sheet.expressions ?? Array.Empty<VFXExpressionDesc>();
            var perEvent = sheet.expressionsPerSpawnEventAttribute ?? Array.Empty<VFXExpressionDesc>();

            uint layer = 0;
            foreach (var system in systemDesc)
            {
                if (system.layer == NoLayer) continue;
                if (system.layer < layer) throw new ArgumentException("System in wrong order: " + system.name);
                layer = system.layer;
            }
            int total = expressions.Length + perEvent.Length;
            foreach (var expression in expressions)
                if (!ValidExpression(expression, expressions, perEvent, total)) throw new ArgumentException("Invalid expression sheet");
            foreach (var expression in perEvent)
                if (!ValidExpression(expression, expressions, perEvent, total)) throw new ArgumentException("Invalid expression sheet");

            var data = new VisualEffectCompiledData
            {
                Expressions = expressions,
                ExpressionsPerSpawnEventAttribute = perEvent,
                Values = sheet.values ?? Array.Empty<VFXExpressionValueContainerDesc>(),
                Exposed = sheet.exposed ?? Array.Empty<VFXExposedMapping>(),
                Systems = systemDesc,
                Events = eventDesc ?? Array.Empty<VFXEventDesc>(),
                GpuBuffers = gpuBufferDesc ?? Array.Empty<VFXGPUBufferDesc>(),
                CpuBuffers = cpuBufferDesc ?? Array.Empty<VFXCPUBufferDesc>(),
                TemporaryBuffers = temporaryBufferDesc ?? Array.Empty<VFXTemporaryGPUBufferDesc>(),
                ShadowCastingMode = shadowCastingMode,
                MotionVectorGenerationMode = motionVectorGenerationMode,
                InstancingDisabledReason = instancingDisabledReason,
                CompiledVersion = version,
            };
            var models = new Object[systemDesc.Length][];
            for (int s = 0; s < systemDesc.Length; s++)
            {
                var tasks = systemDesc[s].tasks ?? Array.Empty<VFXEditorTaskDesc>();
                models[s] = new Object[tasks.Length];
                for (int t = 0; t < tasks.Length; t++) models[s][t] = tasks[t].model;
            }
            m_ShaderSources = shaderSourceDesc ?? Array.Empty<VFXShaderSourceDesc>();
            m_Models = models;
            m_Data = data;

            string repeated = null;
            for (int i = 0; i < data.Events.Length && repeated == null; i++)
                for (int j = i + 1; j < data.Events.Length; j++)
                    if (Shader.PropertyToID(data.Events[i].name) == Shader.PropertyToID(data.Events[j].name))
                    {
                        repeated = data.Events[i].name;
                        break;
                    }
            if (repeated != null)
            {
                data.Events = Array.Empty<VFXEventDesc>();
                throw new ArgumentException($"Event named '{repeated}' is referenced twice");
            }
        }

        /// <summary>The layer of a system that takes no part in the layer order.</summary>
        const uint NoLayer = uint.MaxValue;

        /// <summary>The words of an ExtractComponent expression holding the component taken and
        /// the size of its parent's type (an operation's own operands fill the last words).</summary>
        const int ExtractComponentIndex = 2, ExtractParentSize = 3;

        /// <summary>The most components a vector expression has.</summary>
        const int MaxComponents = 4;

        /// <summary>Whether an expression is one the engine can evaluate: a known operation with
        /// a result type (None, the empty expression, has none), parents inside the sheet, a
        /// component inside its parent's size, and a curve or gradient where the operation
        /// samples one. Each failure is logged as the Editor words it.</summary>
        static bool ValidExpression(VFXExpressionDesc expression, VFXExpressionDesc[] expressions, VFXExpressionDesc[] perEvent, int total)
        {
            var op = expression.op;
            var data = expression.data ?? Array.Empty<int>();
            uint Word(int i) => i < data.Length ? unchecked((uint)data[i]) : uint.MaxValue;
            int parents = VFXExpressionHelper.ParentCount(op);
            if (parents < 0)
            {
                Debug.LogError($"Invalid VFX expression opcode: {(uint)op}");
                return false;
            }
            if (op != VFXExpressionOperation.None)
            {
                var type = VFXExpressionHelper.TypeOf(op, data);
                if (type == VFXValueType.None || type > VFXValueType.SkinnedMeshRenderer)
                {
                    Debug.LogError($"Invalid VFX expression return type: {(uint)type} for expression {(uint)op}");
                    return false;
                }
            }
            for (int i = 0; i < parents; i++)
            {
                if (Word(i) >= (uint)total)
                {
                    Debug.LogError($"Invalid VFX expression parent index: {Word(i)} for expression {(uint)op}");
                    return false;
                }
            }
            if (op == VFXExpressionOperation.ExtractComponent)
            {
                if (Word(ExtractComponentIndex) < Word(ExtractParentSize) && Word(ExtractParentSize) <= MaxComponents) return true;
                Debug.LogError("Invalid VFX expression extract operands");
                return false;
            }
            if (op == VFXExpressionOperation.SampleCurve || op == VFXExpressionOperation.SampleGradient)
            {
                int index = data[0];
                var sampled = index < expressions.Length
                    ? VFXExpressionHelper.TypeOf(expressions[index].op, expressions[index].data)
                    : VFXExpressionHelper.TypeOf(perEvent[index - expressions.Length].op, perEvent[index - expressions.Length].data);
                bool curve = op == VFXExpressionOperation.SampleCurve;
                if (sampled != (curve ? VFXValueType.Curve : VFXValueType.ColorGradient))
                {
                    Debug.LogError($"Invalid VFX parent for sample {(curve ? "Curve" : "Gradient")} : {(uint)sampled}");
                    return false;
                }
            }
            return true;
        }

        /// <summary>The index of the compiled shader source an object of the asset was made
        /// from (a compute shader, an output's shader or its material), -1 for another object.</summary>
        public int GetShaderIndex(Object shader) => VisualEffectImports.ShaderIndex(this, shader);

        /// <summary>The material the import made for an output model, null without one.</summary>
        public Material FindMaterial(ScriptableObject model)
        {
            if (model == null) return null;
            for (int s = 0; s < m_Models.Length; s++)
                for (int t = 0; t < m_Models[s].Length; t++)
                    if (ReferenceEquals(m_Models[s][t], model) && asset != null && s < asset.m_Data.Processors.Length
                        && asset.m_Data.Processors[s][t] is Material material)
                        return material;
            return null;
        }
    }
}
