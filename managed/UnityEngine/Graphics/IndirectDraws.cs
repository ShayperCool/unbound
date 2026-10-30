using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>How a draw runs: <see cref="Instances"/> instances (SV_InstanceID 0..n), or with its
    /// counts read on the GPU from an arguments buffer (<see cref="Args"/>, a buffer handle) at
    /// <see cref="Offset"/> bytes: indexed (DrawMeshInstancedIndirect: index count per instance,
    /// instance count, start index, base vertex, start instance) or not (DrawProceduralIndirect:
    /// vertex count per instance, instance count, start vertex, start instance).</summary>
    internal struct DrawSetup
    {
        internal uint Instances;
        internal ulong Args;
        internal uint Offset;
        internal bool Indexed;
    }

    internal static class IndirectArgs
    {
        internal static ulong Of(ComputeBuffer buffer) =>
            buffer != null ? buffer.Handle : throw new ArgumentNullException("bufferWithArgs");

        internal static ulong Of(GraphicsBuffer buffer) =>
            buffer != null ? buffer.Handle : throw new ArgumentNullException("bufferWithArgs");

        internal static Dictionary<int, object> Values(MaterialPropertyBlock properties) =>
            properties != null && !properties.isEmpty ? new Dictionary<int, object>(properties.m_Values) : null;

        internal static void Mesh(Mesh mesh, int submeshIndex, Material material, int shaderPass, DrawSetup setup, Dictionary<int, object> values)
        {
            int submesh = Math.Min(Math.Max(0, submeshIndex), Math.Max(0, mesh.subMeshCount - 1));
            GfxStream.DrawSetup(setup);
            GfxStream.Draw(mesh.Sync(), submesh, material.Sync(), shaderPass < 0 ? GfxStream.AllPasses : shaderPass, Matrix4x4.identity, Color.white, properties: values);
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial class CommandBuffer
    {
        void ProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ulong args, int argsOffset, MaterialPropertyBlock properties)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var values = IndirectArgs.Values(properties);
            Add(c => ProceduralMeshes.DrawIndirect(matrix, material, shaderPass, topology, args, argsOffset, values));
        }

        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            ProceduralIndirect(matrix, material, shaderPass, topology, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, null);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, 0, null);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            ProceduralIndirect(matrix, material, shaderPass, topology, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, null);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, 0, null);

        void MeshIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ulong args, int argsOffset, MaterialPropertyBlock properties)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            var values = IndirectArgs.Values(properties);
            var setup = new DrawSetup { Instances = 1, Args = args, Offset = (uint)argsOffset, Indexed = true };
            Add(c => IndirectArgs.Mesh(mesh, submeshIndex, material, shaderPass, setup, values));
        }

        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            MeshIndirect(mesh, submeshIndex, material, shaderPass, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ComputeBuffer bufferWithArgs, int argsOffset) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, null);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ComputeBuffer bufferWithArgs) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, 0, null);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            MeshIndirect(mesh, submeshIndex, material, shaderPass, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, GraphicsBuffer bufferWithArgs, int argsOffset) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, null);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, GraphicsBuffer bufferWithArgs) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, 0, null);

        /// <summary>Instances of a mesh whose per-instance data the shader reads itself (SV_InstanceID).</summary>
        public void DrawMeshInstancedProcedural(Mesh mesh, int submeshIndex, Material material, int shaderPass, int count, MaterialPropertyBlock properties = null)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (count <= 0) return;
            var values = IndirectArgs.Values(properties);
            var setup = new DrawSetup { Instances = (uint)count };
            Add(c => IndirectArgs.Mesh(mesh, submeshIndex, material, shaderPass, setup, values));
        }
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        static void QueueMesh(Mesh mesh, int submeshIndex, Material material, MaterialPropertyBlock properties, int layer, Camera camera, DrawSetup setup,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            Immediate.Queue(mesh, Matrix4x4.identity, material, layer, camera, submeshIndex, properties, setup, ImmediateLighting.Of(castShadows, receiveShadows, lightProbeUsage));
        }

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage, LightProbeProxyVolume lightProbeProxyVolume) =>
            QueueMesh(mesh, submeshIndex, material, properties, layer, camera, new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset, Indexed = true },
                castShadows, receiveShadows, lightProbeUsage);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage, LightProbeProxyVolume lightProbeProxyVolume) =>
            QueueMesh(mesh, submeshIndex, material, properties, layer, camera, new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset, Indexed = true },
                castShadows, receiveShadows, lightProbeUsage);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, ComputeBuffer bufferWithArgs, int argsOffset = 0, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, bounds, bufferWithArgs, argsOffset, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage, null);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, GraphicsBuffer bufferWithArgs, int argsOffset = 0, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, bounds, bufferWithArgs, argsOffset, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage, null);

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueMesh(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, material, properties, layer, camera,
                new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset }, castShadows, receiveShadows);

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueMesh(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, material, properties, layer, camera,
                new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset }, castShadows, receiveShadows);

        /// <summary>A procedural draw now, with the pass Material.SetPass set, into the active target.</summary>
        public static void DrawProceduralNow(MeshTopology topology, int vertexCount, int instanceCount = 1)
        {
            if (vertexCount <= 0 || instanceCount <= 0) return;
            if (instanceCount > 1) GfxStream.DrawSetup(new DrawSetup { Instances = (uint)instanceCount });
            Immediate.DrawNow(ProceduralMeshes.Get(topology, vertexCount), 0, Matrix4x4.identity);
        }

        public static void DrawProceduralIndirectNow(MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset = 0) =>
            ProceduralIndirectNow(topology, IndirectArgs.Of(bufferWithArgs), argsOffset);

        public static void DrawProceduralIndirectNow(MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset = 0) =>
            ProceduralIndirectNow(topology, IndirectArgs.Of(bufferWithArgs), argsOffset);

        static void ProceduralIndirectNow(MeshTopology topology, ulong args, int argsOffset)
        {
            GfxStream.DrawSetup(new DrawSetup { Instances = 1, Args = args, Offset = (uint)argsOffset });
            Immediate.DrawNow(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, Matrix4x4.identity);
        }

        public static void DrawMeshInstancedProcedural(Mesh mesh, int submeshIndex, Material material, Bounds bounds, int count, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null,
            LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes, LightProbeProxyVolume lightProbeProxyVolume = null)
        {
            if (count <= 0) return;
            QueueMesh(mesh, submeshIndex, material, properties, layer, camera, new DrawSetup { Instances = (uint)count }, castShadows, receiveShadows, lightProbeUsage);
        }
    }
}

namespace UnityEngine
{
    /// <summary>A volume of light probes sampled as a 3D texture. Unbound keeps its settings and
    /// lights its renderers with the probes interpolated at their centers, as Blend Probes does
    /// (the same where the probes are uniform, as with the ambient probe alone; measured:
    /// tests/projects/builtinfx lppv).</summary>
    [ClassId(259)]
    public sealed partial class LightProbeProxyVolume : Behaviour
    {
        BoundingBoxMode m_BoundingBoxMode = BoundingBoxMode.AutomaticLocal;
        ResolutionMode m_ResolutionMode = ResolutionMode.Automatic;
        ProbePositionMode m_ProbePositionMode = ProbePositionMode.CellCorner;
        RefreshMode m_RefreshMode = RefreshMode.Automatic;
        QualityMode m_QualityMode = QualityMode.Normal;
        DataFormat m_DataFormat = DataFormat.HalfFloat;
        float m_ProbeDensity = 0.25f;
        int m_ResolutionX = 1, m_ResolutionY = 1, m_ResolutionZ = 1;
        Vector3 m_BoundingBoxSize = Vector3.one, m_BoundingBoxOrigin;

        public LightProbeProxyVolume() { }

        public enum ResolutionMode { Automatic = 0, Custom = 1 }
        public enum BoundingBoxMode { AutomaticLocal = 0, AutomaticWorld = 1, Custom = 2 }
        public enum ProbePositionMode { CellCorner = 0, CellCenter = 1 }
        public enum RefreshMode { Automatic = 0, EveryFrame = 1, ViaScripting = 2 }
        public enum QualityMode { Low = 0, Normal = 1 }
        public enum DataFormat { HalfFloat = 0, Float = 1 }

        /// <summary>The Editor's OpenGL Core device supports proxy volumes (measured).</summary>
        [Unbound.EditorFaithful("the Editor's OpenGL Core device supports proxy volumes (measured: tests/projects/builtinfx lppv)")]
        public static bool isFeatureSupported => true;

        public BoundingBoxMode boundingBoxMode { get => m_BoundingBoxMode; set => m_BoundingBoxMode = value; }
        public ResolutionMode resolutionMode { get => m_ResolutionMode; set => m_ResolutionMode = value; }
        public ProbePositionMode probePositionMode { get => m_ProbePositionMode; set => m_ProbePositionMode = value; }
        public RefreshMode refreshMode { get => m_RefreshMode; set => m_RefreshMode = value; }
        public QualityMode qualityMode { get => m_QualityMode; set => m_QualityMode = value; }
        public DataFormat dataFormat { get => m_DataFormat; set => m_DataFormat = value; }
        public float probeDensity { get => m_ProbeDensity; set => m_ProbeDensity = Mathf.Clamp(value, 0.0001f, 1024f); }
        public Vector3 sizeCustom { get => m_BoundingBoxSize; set => m_BoundingBoxSize = value; }
        public Vector3 originCustom { get => m_BoundingBoxOrigin; set => m_BoundingBoxOrigin = value; }

        /// <summary>The grid's cells per axis: set, or (Automatic) the bounds' size times the
        /// density, a power of two from 2 to 32 (measured: a unit sphere's is 2).</summary>
        public int gridResolutionX { get => Resolution(m_ResolutionX, 0); set => m_ResolutionX = Mathf.Clamp(value, 1, 32); }
        public int gridResolutionY { get => Resolution(m_ResolutionY, 1); set => m_ResolutionY = Mathf.Clamp(value, 1, 32); }
        public int gridResolutionZ { get => Resolution(m_ResolutionZ, 2); set => m_ResolutionZ = Mathf.Clamp(value, 1, 32); }

        int Resolution(int custom, int axis)
        {
            if (m_ResolutionMode == ResolutionMode.Custom) return custom;
            float size = boundsGlobal.size[axis];
            return Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(size * m_ProbeDensity)), 2, 32);
        }

        /// <summary>The volume's world bounds: its renderer's (automatic local and world), or the
        /// custom box around the transform.</summary>
        public Bounds boundsGlobal
        {
            get
            {
                if (m_BoundingBoxMode == BoundingBoxMode.Custom)
                    return new Bounds(transform.TransformPoint(m_BoundingBoxOrigin), Vector3.Scale(m_BoundingBoxSize, transform.lossyScale));
                var renderer = m_GameObject != null ? m_GameObject.GetComponent<Renderer>() : null;
                return renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.zero);
            }
        }

        [Unbound.EditorFaithful("Unbound samples the probes when it draws: there is no volume texture to refresh")]
        public void Update() { }

        internal override void CopyFrom(Component source, System.Func<Object, Object> remap)
        {
            var o = (LightProbeProxyVolume)source;
            (m_BoundingBoxMode, m_ResolutionMode, m_ProbePositionMode, m_RefreshMode, m_QualityMode, m_DataFormat) =
                (o.m_BoundingBoxMode, o.m_ResolutionMode, o.m_ProbePositionMode, o.m_RefreshMode, o.m_QualityMode, o.m_DataFormat);
            (m_ProbeDensity, m_ResolutionX, m_ResolutionY, m_ResolutionZ, m_BoundingBoxSize, m_BoundingBoxOrigin) =
                (o.m_ProbeDensity, o.m_ResolutionX, o.m_ResolutionY, o.m_ResolutionZ, o.m_BoundingBoxSize, o.m_BoundingBoxOrigin);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_BoundingBoxMode = (BoundingBoxMode)body.Int("m_BoundingBoxMode");
            m_ResolutionMode = (ResolutionMode)body.Int("m_ResolutionMode");
            m_ProbePositionMode = (ProbePositionMode)body.Int("m_ProbePositionMode");
            m_RefreshMode = (RefreshMode)body.Int("m_RefreshMode");
            m_QualityMode = (QualityMode)body.Int("m_QualityMode", (int)QualityMode.Normal);
            m_DataFormat = (DataFormat)body.Int("m_DataFormat", (int)DataFormat.HalfFloat);
            m_ProbeDensity = body.Float("m_ProbeDensity", 0.25f);
            m_ResolutionX = body.Int("m_ResolutionX", 1);
            m_ResolutionY = body.Int("m_ResolutionY", 1);
            m_ResolutionZ = body.Int("m_ResolutionZ", 1);
            m_BoundingBoxSize = body.Get("m_BoundingBoxSize") != null ? body.Vector3("m_BoundingBoxSize") : Vector3.one;
            m_BoundingBoxOrigin = body.Vector3("m_BoundingBoxOrigin");
        }
    }
}
