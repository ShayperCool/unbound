using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

// What the Visual Effect Graph package's compiler hands the engine: the descriptions of a
// compiled graph (expressions, values, systems, tasks, buffers, events, shader sources), as the
// Unity 6000.0 reference assemblies declare them.
namespace UnityEditor.VFX
{
    /// <summary>A named index: a buffer, value or parameter a system or task uses.</summary>
    internal partial struct VFXMapping
    {
        public string name;
        public int index;

        public VFXMapping(string name, int index)
        {
            this.name = name;
            this.index = index;
        }
    }

    /// <summary>A temporary buffer a task uses: which past frame of it, and whether per camera.</summary>
    internal partial struct VFXMappingTemporary
    {
        public VFXMapping mapping;
        public uint pastFrameIndex;
        public bool perCameraBuffer;

        public VFXMappingTemporary(VFXMapping mapping, uint pastFrameIndex, bool perCameraBuffer)
        {
            this.mapping = mapping;
            this.pastFrameIndex = pastFrameIndex;
            this.perCameraBuffer = perCameraBuffer;
        }
    }

    /// <summary>Where an element lives in a buffer: its bucket, the structure's size and its offset.</summary>
    internal partial struct VFXLayoutOffset
    {
        public uint bucket;
        public uint structure;
        public uint element;
    }

    /// <summary>A named element of a buffer's layout.</summary>
    internal partial struct VFXLayoutElementDesc
    {
        public string name;
        public VFXValueType type;
        public VFXLayoutOffset offset;
    }

    internal partial struct VFXGPUBufferDesc
    {
        public VFXLayoutElementDesc[] layout;
        public uint capacity;
        public GraphicsBuffer.Target target;
        public uint size;
        public uint stride;
        public ComputeBufferMode mode;
    }

    internal partial struct VFXTemporaryGPUBufferDesc
    {
        public VFXGPUBufferDesc desc;
        public uint frameCount;
    }

    internal partial struct VFXCPUBufferDesc
    {
        public VFXLayoutElementDesc[] layout;
        public uint capacity;
        public uint stride;
        public VFXCPUBufferData initialData;
    }

    /// <summary>The initial contents of a CPU buffer: 32-bit words in the order pushed.</summary>
    internal sealed partial class VFXCPUBufferData : IDisposable
    {
        internal readonly System.Collections.Generic.List<uint> m_Words = new System.Collections.Generic.List<uint>();

        public VFXCPUBufferData() { }

        public void Dispose() { }

        public void PushUInt(uint v) => m_Words.Add(v);

        public void PushInt(int v) => m_Words.Add(unchecked((uint)v));

        public void PushFloat(float v) => m_Words.Add(BitConverter.SingleToUInt32Bits(v));

        public void PushBool(bool v) => m_Words.Add(v ? 1u : 0u);
    }

    internal partial struct VFXInstanceSplitDesc
    {
        public uint[] values;
    }

    /// <summary>A task of a system: one stage (a spawner block, an init, update or output pass)
    /// with what it reads. Its processor is the shader source it compiles to, or an object.</summary>
    internal partial struct VFXEditorTaskDesc
    {
        public VFXTaskType type;
        public VFXMapping[] buffers;
        public VFXMappingTemporary[] temporaryBuffers;
        public VFXMapping[] values;
        public VFXMapping[] parameters;
        Object processor;
        public uint instanceSplitIndex;
        int m_ShaderSourceIndex;
        public Object model;
        public bool usesMaterialVariant;

        /// <summary>The task's processor when it is an object (a spawner's callbacks); setting
        /// it clears the shader source.</summary>
        public Object externalProcessor
        {
            get => m_ShaderSourceIndex < 0 ? processor : null;
            set
            {
                m_ShaderSourceIndex = -1;
                processor = value;
            }
        }

        /// <summary>The task's processor as an index into the compiled shader sources; setting
        /// it clears the object.</summary>
        public int shaderSourceIndex
        {
            get => processor == null ? m_ShaderSourceIndex : -1;
            set
            {
                processor = null;
                m_ShaderSourceIndex = value;
            }
        }
    }

    internal partial struct VFXEditorSystemDesc
    {
        public VFXSystemType type;
        public VFXSystemFlag flags;
        public uint capacity;
        public uint layer;
        public string name;
        public VFXMapping[] buffers;
        public VFXMapping[] values;
        public VFXEditorTaskDesc[] tasks;
        public VFXInstanceSplitDesc[] instanceSplitDescs;
    }

    internal partial struct VFXEventDesc
    {
        public string name;
        public uint[] startSystems;
        public uint[] stopSystems;
        public uint[] initSystems;
    }

    internal partial struct VFXExposedMapping
    {
        public VFXMapping mapping;
        public VFXSpace space;
    }

    /// <summary>An expression: its operation and four words (parents, a type, a component...).</summary>
    internal partial struct VFXExpressionDesc
    {
        public VFXExpressionOperation op;
        public int[] data;
    }

    internal abstract partial class VFXExpressionValueContainerDesc
    {
        public uint expressionIndex;

        protected VFXExpressionValueContainerDesc() { }
    }

    internal partial class VFXExpressionValueContainerDesc<T> : VFXExpressionValueContainerDesc
    {
        public T value;

        public VFXExpressionValueContainerDesc() { }
    }

    internal partial class VFXExpressionObjectValueContainerDesc<T> : VFXExpressionValueContainerDesc
    {
        public int instanceID;

        public VFXExpressionObjectValueContainerDesc() { }
    }

    internal partial struct VFXExpressionSheet
    {
        public VFXExpressionDesc[] expressions;
        public VFXExpressionDesc[] expressionsPerSpawnEventAttribute;
        public VFXExpressionValueContainerDesc[] values;
        public VFXExposedMapping[] exposed;
    }

    internal partial struct VFXRendererSettings
    {
        public MotionVectorGenerationMode motionVectorGenerationMode;
        public ShadowCastingMode shadowCastingMode;
        public RayTracingAccelerationStructure.RayTracingModeMask rayTracingMode;
        public bool receiveShadows;
        public ReflectionProbeUsage reflectionProbeUsage;
        public LightProbeUsage lightProbeUsage;
    }

    internal partial struct VFXShaderSourceDesc
    {
        public bool compute;
        public string name;
        public string source;
    }

    /// <summary>What a graph shows in the template window (kept in its .meta).</summary>
    internal partial struct VFXTemplate
    {
        public string name;
        public string category;
        public string description;
        public Texture2D icon;
        public Texture2D thumbnail;
    }
}
