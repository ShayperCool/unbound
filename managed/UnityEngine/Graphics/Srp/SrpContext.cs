using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;

namespace UnityEngine.Rendering
{
    public partial struct LODParameters : IEquatable<LODParameters>
    {
        int m_IsOrthographic;
        Vector3 m_CameraPosition;
        float m_FieldOfView;
        float m_OrthoSize;
        int m_CameraPixelHeight;

        public bool isOrthographic { get => m_IsOrthographic != 0; set => m_IsOrthographic = value ? 1 : 0; }
        public Vector3 cameraPosition { get => m_CameraPosition; set => m_CameraPosition = value; }
        public float fieldOfView { get => m_FieldOfView; set => m_FieldOfView = value; }
        public float orthoSize { get => m_OrthoSize; set => m_OrthoSize = value; }
        public int cameraPixelHeight { get => m_CameraPixelHeight; set => m_CameraPixelHeight = value; }

        public bool Equals(LODParameters other) =>
            m_IsOrthographic == other.m_IsOrthographic && m_CameraPosition == other.m_CameraPosition && m_FieldOfView == other.m_FieldOfView
            && m_OrthoSize == other.m_OrthoSize && m_CameraPixelHeight == other.m_CameraPixelHeight;
        public override bool Equals(object obj) => obj is LODParameters other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_IsOrthographic, m_CameraPosition, m_FieldOfView, m_OrthoSize, m_CameraPixelHeight);
        public static bool operator ==(LODParameters left, LODParameters right) => left.Equals(right);
        public static bool operator !=(LODParameters left, LODParameters right) => !left.Equals(right);
    }

    /// <summary>What a camera culls with: its matrix, planes, mask and shadow distance.</summary>
    public partial struct ScriptableCullingParameters : IEquatable<ScriptableCullingParameters>
    {
        public static readonly int maximumCullingPlaneCount = 10;
        public static readonly int layerCount = 32;

        LODParameters m_LODParameters;
        Plane m_Plane0, m_Plane1, m_Plane2, m_Plane3, m_Plane4, m_Plane5, m_Plane6, m_Plane7, m_Plane8, m_Plane9;
        int m_CullingPlaneCount;
        uint m_CullingMask;
        ulong m_SceneMask;
        internal unsafe fixed float m_LayerFarCullDistances[32];
        Matrix4x4 m_CullingMatrix;
        Vector3 m_Origin;
        float m_ShadowDistance;
        float m_ShadowNearPlaneOffset;
        CullingOptions m_CullingOptions;
        ReflectionProbeSortingCriteria m_ReflectionProbeSortingCriteria;
        float m_AccurateOcclusionThreshold;
        int m_MaximumPortalCullingJobs;
        Matrix4x4 m_StereoViewMatrix;
        Matrix4x4 m_StereoProjectionMatrix;
        float m_StereoSeparationDistance;
        int m_MaximumVisibleLights;
        bool m_ConservativeEnclosingSphere;
        int m_NumIterationsEnclosingSphere;
        /// <summary>The camera the parameters are for (Unbound's culling reads it).</summary>
        internal int m_CameraInstanceId;

        public int maximumVisibleLights { get => m_MaximumVisibleLights; set => m_MaximumVisibleLights = value; }
        public bool conservativeEnclosingSphere { get => m_ConservativeEnclosingSphere; set => m_ConservativeEnclosingSphere = value; }
        public int numIterationsEnclosingSphere { get => m_NumIterationsEnclosingSphere; set => m_NumIterationsEnclosingSphere = value; }

        public int cullingPlaneCount
        {
            get => m_CullingPlaneCount;
            set
            {
                if (value < 0 || value > 10) throw new ArgumentOutOfRangeException(nameof(value), "Value should range from 0 to 10.");
                m_CullingPlaneCount = value;
            }
        }

        public bool isOrthographic { get => m_LODParameters.isOrthographic; set => m_LODParameters.isOrthographic = value; }
        public LODParameters lodParameters { get => m_LODParameters; set => m_LODParameters = value; }
        public uint cullingMask { get => m_CullingMask; set => m_CullingMask = value; }
        public Matrix4x4 cullingMatrix { get => m_CullingMatrix; set => m_CullingMatrix = value; }
        public Vector3 origin { get => m_Origin; set => m_Origin = value; }
        public float shadowDistance { get => m_ShadowDistance; set => m_ShadowDistance = value; }
        public float shadowNearPlaneOffset { get => m_ShadowNearPlaneOffset; set => m_ShadowNearPlaneOffset = value; }
        public CullingOptions cullingOptions { get => m_CullingOptions; set => m_CullingOptions = value; }
        public ReflectionProbeSortingCriteria reflectionProbeSortingCriteria { get => m_ReflectionProbeSortingCriteria; set => m_ReflectionProbeSortingCriteria = value; }
        public Matrix4x4 stereoViewMatrix { get => m_StereoViewMatrix; set => m_StereoViewMatrix = value; }
        public Matrix4x4 stereoProjectionMatrix { get => m_StereoProjectionMatrix; set => m_StereoProjectionMatrix = value; }
        public float stereoSeparationDistance { get => m_StereoSeparationDistance; set => m_StereoSeparationDistance = value; }
        public float accurateOcclusionThreshold { get => m_AccurateOcclusionThreshold; set => m_AccurateOcclusionThreshold = value; }

        public int maximumPortalCullingJobs
        {
            get => m_MaximumPortalCullingJobs;
            set
            {
                if (value < 1 || value > 16) throw new ArgumentOutOfRangeException($"{nameof(maximumPortalCullingJobs)} was {value}, but must be in range 1 to 16");
                m_MaximumPortalCullingJobs = value;
            }
        }

        public static int cullingJobsLowerLimit => 1;
        public static int cullingJobsUpperLimit => 16;

        public unsafe float GetLayerCullingDistance(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= 32) throw new ArgumentOutOfRangeException(nameof(layerIndex), "Layer index should range from 0 to 32.");
            fixed (float* d = m_LayerFarCullDistances) return d[layerIndex];
        }

        public unsafe void SetLayerCullingDistance(int layerIndex, float distance)
        {
            if (layerIndex < 0 || layerIndex >= 32) throw new ArgumentOutOfRangeException(nameof(layerIndex), "Layer index should range from 0 to 32.");
            fixed (float* d = m_LayerFarCullDistances) d[layerIndex] = distance;
        }

        public unsafe Plane GetCullingPlane(int index)
        {
            if (index < 0 || index >= m_CullingPlaneCount) throw new ArgumentOutOfRangeException(nameof(index), $"Index should range from 0 to {m_CullingPlaneCount}.");
            fixed (Plane* p = &m_Plane0) return p[index];
        }

        public unsafe void SetCullingPlane(int index, Plane plane)
        {
            if (index < 0 || index >= m_CullingPlaneCount) throw new ArgumentOutOfRangeException(nameof(index), $"Index should range from 0 to {m_CullingPlaneCount}.");
            fixed (Plane* p = &m_Plane0) p[index] = plane;
        }

        public bool Equals(ScriptableCullingParameters other) =>
            m_LODParameters == other.m_LODParameters && m_CullingPlaneCount == other.m_CullingPlaneCount && m_CullingMask == other.m_CullingMask
            && m_CullingMatrix == other.m_CullingMatrix && m_Origin == other.m_Origin && m_ShadowDistance == other.m_ShadowDistance
            && m_CullingOptions == other.m_CullingOptions && m_CameraInstanceId == other.m_CameraInstanceId;
        public override bool Equals(object obj) => obj is ScriptableCullingParameters other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_LODParameters, m_CullingPlaneCount, m_CullingMask, m_CullingMatrix, m_Origin, m_ShadowDistance, m_CullingOptions);
        public static bool operator ==(ScriptableCullingParameters left, ScriptableCullingParameters right) => left.Equals(right);
        public static bool operator !=(ScriptableCullingParameters left, ScriptableCullingParameters right) => !left.Equals(right);
    }

    /// <summary>What a cull found: the visible lights, reflection probes and (for renderer lists)
    /// renderers.</summary>
    public partial struct CullingResults : IEquatable<CullingResults>
    {
        internal IntPtr ptr;

        internal CullingData Data => Culling.Get(ptr);

        public NativeArray<VisibleLight> visibleLights => Data?.Lights ?? default;
        public NativeArray<VisibleLight> visibleOffscreenVertexLights => Data?.OffscreenVertexLights ?? default;
        public NativeArray<VisibleReflectionProbe> visibleReflectionProbes => Data?.Probes ?? default;

        public int lightIndexCount => 0;
        public int reflectionProbeIndexCount => 0;
        public int lightAndReflectionProbeIndexCount => 0;

        public void FillLightAndReflectionProbeIndices(ComputeBuffer computeBuffer) { }
        public void FillLightAndReflectionProbeIndices(GraphicsBuffer buffer) { }

        public NativeArray<int> GetLightIndexMap(Allocator allocator)
        {
            var data = Data;
            int n = data?.Lights.Length ?? 0;
            var map = new NativeArray<int>(n, allocator);
            for (int i = 0; i < n; i++) map[i] = data.LightIndexMap != null && i < data.LightIndexMap.Length ? data.LightIndexMap[i] : i;
            return map;
        }

        public void SetLightIndexMap(NativeArray<int> lightIndexMap)
        {
            var data = Data;
            if (data != null) data.LightIndexMap = lightIndexMap.ToArray();
        }

        public NativeArray<int> GetReflectionProbeIndexMap(Allocator allocator)
        {
            int n = Data?.Probes.Length ?? 0;
            var map = new NativeArray<int>(n, allocator);
            for (int i = 0; i < n; i++) map[i] = i;
            return map;
        }

        public void SetReflectionProbeIndexMap(NativeArray<int> lightIndexMap) { }

        public bool GetShadowCasterBounds(int lightIndex, out Bounds outBounds)
        {
            outBounds = default;
            var data = Data;
            if (data == null || data.Renderers.Count == 0) return false;
            bool any = false;
            foreach (var r in data.Renderers)
            {
                if (!r || r.shadowCastingMode == ShadowCastingMode.Off) continue;
                if (!any) { outBounds = r.bounds; any = true; }
                else outBounds.Encapsulate(r.bounds);
            }
            return any;
        }

        public bool ComputeSpotShadowMatricesAndCullingPrimitives(int activeLightIndex, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData shadowSplitData)
        {
            viewMatrix = projMatrix = Matrix4x4.identity;
            shadowSplitData = default;
            var light = Data != null && activeLightIndex >= 0 && activeLightIndex < Data.Lights.Length ? Data.Lights[activeLightIndex] : default;
            if (light.lightType != LightType.Spot) return false;
            var world = light.localToWorldMatrix;
            viewMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) * world.inverse;
            projMatrix = Matrix4x4.Perspective(light.spotAngle, 1f, 0.05f, light.range);
            shadowSplitData.cullingSphere = new Vector4(world.m03, world.m13, world.m23, light.range);
            return true;
        }

        public bool ComputePointShadowMatricesAndCullingPrimitives(int activeLightIndex, CubemapFace cubemapFace, float fovBias, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData shadowSplitData)
        {
            viewMatrix = projMatrix = Matrix4x4.identity;
            shadowSplitData = default;
            return false;
        }

        public bool ComputeDirectionalShadowMatricesAndCullingPrimitives(int activeLightIndex, int splitIndex, int splitCount, Vector3 splitRatio, int shadowResolution,
            float shadowNearPlaneOffset, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData shadowSplitData)
        {
            viewMatrix = projMatrix = Matrix4x4.identity;
            shadowSplitData = default;
            return false;
        }

        internal void Validate() { }

        public bool Equals(CullingResults other) => ptr == other.ptr;
        public override bool Equals(object obj) => obj is CullingResults other && Equals(other);
        public override int GetHashCode() => ptr.GetHashCode();
        public static bool operator ==(CullingResults left, CullingResults right) => left.Equals(right);
        public static bool operator !=(CullingResults left, CullingResults right) => !left.Equals(right);
    }

    public partial struct ShadowSplitData : IEquatable<ShadowSplitData>
    {
        public static readonly int maximumCullingPlaneCount = 10;

        int m_CullingPlaneCount;
        Plane m_Plane0, m_Plane1, m_Plane2, m_Plane3, m_Plane4, m_Plane5, m_Plane6, m_Plane7, m_Plane8, m_Plane9;
        Vector4 m_CullingSphere;
        float m_ShadowCascadeBlendCullingFactor;
        float m_CullingNearPlane;
        Matrix4x4 m_CullingMatrix;

        public int cullingPlaneCount
        {
            get => m_CullingPlaneCount;
            set
            {
                if (value < 0 || value > 10) throw new ArgumentException("Value should range from 0 to 10.");
                m_CullingPlaneCount = value;
            }
        }

        public Vector4 cullingSphere { get => m_CullingSphere; set => m_CullingSphere = value; }
        public Matrix4x4 cullingMatrix { get => m_CullingMatrix; set => m_CullingMatrix = value; }
        public float cullingNearPlane { get => m_CullingNearPlane; set => m_CullingNearPlane = value; }

        public float shadowCascadeBlendCullingFactor
        {
            get => m_ShadowCascadeBlendCullingFactor;
            set
            {
                if (value < 0f || value > 1f) throw new ArgumentException($"Value should range from 0 to 1, but was {value}.");
                m_ShadowCascadeBlendCullingFactor = value;
            }
        }

        public unsafe Plane GetCullingPlane(int index)
        {
            if (index < 0 || index >= m_CullingPlaneCount) throw new ArgumentException("index", $"Index should be at least 0 and less than cullingPlaneCount ({m_CullingPlaneCount}).");
            fixed (Plane* p = &m_Plane0) return p[index];
        }

        public unsafe void SetCullingPlane(int index, Plane plane)
        {
            if (index < 0 || index >= m_CullingPlaneCount) throw new ArgumentException("index", $"Index should be at least 0 and less than cullingPlaneCount ({m_CullingPlaneCount}).");
            fixed (Plane* p = &m_Plane0) p[index] = plane;
        }

        public bool Equals(ShadowSplitData other) =>
            m_CullingPlaneCount == other.m_CullingPlaneCount && m_CullingSphere == other.m_CullingSphere
            && m_ShadowCascadeBlendCullingFactor == other.m_ShadowCascadeBlendCullingFactor && m_CullingNearPlane == other.m_CullingNearPlane
            && m_CullingMatrix == other.m_CullingMatrix;
        public override bool Equals(object obj) => obj is ShadowSplitData other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_CullingPlaneCount, m_CullingSphere, m_ShadowCascadeBlendCullingFactor, m_CullingNearPlane, m_CullingMatrix);
        public static bool operator ==(ShadowSplitData left, ShadowSplitData right) => left.Equals(right);
        public static bool operator !=(ShadowSplitData left, ShadowSplitData right) => !left.Equals(right);
    }

    /// <summary>A global shader keyword by name.</summary>
    public readonly partial struct GlobalKeyword
    {
        internal readonly string m_Name;
        internal readonly uint m_Index;

        public GlobalKeyword(string name)
        {
            m_Name = name;
            m_Index = Shader.KeywordIndex(name);
        }

        public string name => m_Name;

        public static GlobalKeyword Create(string name) => new GlobalKeyword(name);

        public override string ToString() => m_Name;
    }

    /// <summary>A shader's (or compute shader's) keyword space: its declared keywords.</summary>
    public readonly partial struct LocalKeywordSpace : IEquatable<LocalKeywordSpace>
    {
        readonly IntPtr m_KeywordSpace;

        internal LocalKeywordSpace(int id) => m_KeywordSpace = (IntPtr)id;

        public LocalKeyword[] keywords => Array.Empty<LocalKeyword>();
        public string[] keywordNames => Array.Empty<string>();
        public uint keywordCount => 0;

        public LocalKeyword FindKeyword(string name) => new LocalKeyword(this, name);

        public override bool Equals(object o) => o is LocalKeywordSpace other && Equals(other);
        public bool Equals(LocalKeywordSpace rhs) => m_KeywordSpace == rhs.m_KeywordSpace;
        public static bool operator ==(LocalKeywordSpace lhs, LocalKeywordSpace rhs) => lhs.Equals(rhs);
        public static bool operator !=(LocalKeywordSpace lhs, LocalKeywordSpace rhs) => !lhs.Equals(rhs);
        public override int GetHashCode() => m_KeywordSpace.GetHashCode();
    }

    /// <summary>A keyword of a shader or compute shader, by name.</summary>
    public readonly partial struct LocalKeyword : IEquatable<LocalKeyword>
    {
        internal readonly LocalKeywordSpace m_SpaceInfo;
        internal readonly string m_Name;
        internal readonly uint m_Index;

        internal LocalKeyword(LocalKeywordSpace space, string name)
        {
            m_SpaceInfo = space;
            m_Name = name;
            m_Index = Shader.KeywordIndex(name);
        }

        public LocalKeyword(Shader shader, string name) : this(new LocalKeywordSpace(shader != null ? shader.GetInstanceID() : 0), name) { }

        public LocalKeyword(ComputeShader shader, string name) : this(new LocalKeywordSpace(shader != null ? shader.GetInstanceID() : 0), name) { }

        public string name => m_Name;
        public bool isDynamic => false;
        public bool isOverridable => false;
        public bool isValid => !string.IsNullOrEmpty(m_Name);
        public ShaderKeywordType type => ShaderKeywordType.UserDefined;

        public override string ToString() => m_Name;
        public override bool Equals(object o) => o is LocalKeyword other && Equals(other);
        public bool Equals(LocalKeyword rhs) => m_SpaceInfo == rhs.m_SpaceInfo && m_Name == rhs.m_Name;
        public static bool operator ==(LocalKeyword lhs, LocalKeyword rhs) => lhs.Equals(rhs);
        public static bool operator !=(LocalKeyword lhs, LocalKeyword rhs) => !lhs.Equals(rhs);
        public override int GetHashCode() => HashCode.Combine(m_SpaceInfo, m_Name);
    }

    /// <summary>
    /// What a scriptable pipeline renders through: it culls, makes renderer lists, executes command
    /// buffers and sets up cameras. Unbound records its work onto the rendering command stream as it
    /// is asked, so Submit has nothing left to send.
    /// </summary>
    public partial struct ScriptableRenderContext : IEquatable<ScriptableRenderContext>
    {
        IntPtr m_Ptr;

        static int s_Next;
        static List<Camera> s_Cameras = new List<Camera>();
        static Camera s_Camera;

        internal static ScriptableRenderContext Begin(List<Camera> cameras = null)
        {
            s_Cameras = cameras ?? new List<Camera>();
            s_Camera = null;
            return new ScriptableRenderContext { m_Ptr = (IntPtr)(++s_Next) };
        }

        /// <summary>The camera the last SetupCameraProperties set up.</summary>
        internal static Camera CurrentCamera => s_Camera;

        internal void GetCameras(List<Camera> results)
        {
            results.Clear();
            results.AddRange(s_Cameras);
        }

        public void Submit() { }

        public bool SubmitForRenderPassValidation() => true;

        public bool HasInvokeOnRenderObjectCallbacks() => Lifecycle.RenderObjectBehaviours.Count > 0;

        public void InvokeOnRenderObjectCallback() => FrameBuilder.InvokeOnRenderObject();

        public void ExecuteCommandBuffer(CommandBuffer commandBuffer)
        {
            if (commandBuffer == null) throw new ArgumentNullException(nameof(commandBuffer));
            commandBuffer.ExecuteIn(new CommandBuffer.Context { Camera = s_Camera, CameraTarget = s_Camera != null ? s_Camera.targetTexture : null });
        }

        public void ExecuteCommandBufferAsync(CommandBuffer commandBuffer, ComputeQueueType queueType) => ExecuteCommandBuffer(commandBuffer);

        /// <summary>The camera's matrices and built-in camera variables for what draws next.</summary>
        public void SetupCameraProperties(Camera camera, [Internal.DefaultValue("false")] bool stereoSetup = false) => SetupCameraProperties(camera, stereoSetup, 0);

        public void SetupCameraProperties(Camera camera, bool stereoSetup, int eye)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            s_Camera = camera;
            Camera.current = camera;
            GfxStream.Camera(camera.worldToCameraMatrix, camera.projectionMatrix);
        }

        public void StereoEndRender(Camera camera) { }
        public void StereoEndRender(Camera camera, int eye) { }
        public void StereoEndRender(Camera camera, int eye, bool isFinalPass) { }
        public void StartMultiEye(Camera camera) { }
        public void StartMultiEye(Camera camera, int eye) { }
        public void StopMultiEye(Camera camera) { }

        public void DrawSkybox(Camera camera) { }
        public void DrawUIOverlay(Camera camera) => RendererLists.DrawUIOverlay(camera);
        public void DrawGizmos(Camera camera, GizmoSubset gizmoSubset) { }
        public void DrawWireOverlay(Camera camera) { }

        public CullingResults Cull(ref ScriptableCullingParameters parameters) => new CullingResults { ptr = Culling.Cull(ref parameters) };

        public void CullShadowCasters(CullingResults cullingResults, ShadowCastersCullingInfos infos) { }

        public RendererList CreateRendererList(RendererUtils.RendererListDesc desc)
        {
            var p = RendererUtils.RendererListDesc.ConvertToParameters(desc);
            return CreateRendererList(ref p);
        }

        public RendererList CreateRendererList(ref RendererListParams param) => RendererLists.Create(param);

        public RendererList CreateShadowRendererList(ref ShadowDrawingSettings settings) => RendererLists.Empty();

        public RendererList CreateSkyboxRendererList(Camera camera, Matrix4x4 projectionMatrixL, Matrix4x4 viewMatrixL, Matrix4x4 projectionMatrixR, Matrix4x4 viewMatrixR) =>
            RendererLists.Skybox(camera);
        public RendererList CreateSkyboxRendererList(Camera camera, Matrix4x4 projectionMatrix, Matrix4x4 viewMatrix) => RendererLists.Skybox(camera);
        public RendererList CreateSkyboxRendererList(Camera camera) => RendererLists.Skybox(camera);

        public RendererList CreateGizmoRendererList(Camera camera, GizmoSubset gizmoSubset) => RendererLists.Empty();
        public RendererList CreateUIOverlayRendererList(Camera camera) => RendererLists.UIOverlay(camera);
        public RendererList CreateUIOverlayRendererList(Camera camera, UISubset uiSubset) => RendererLists.UIOverlay(camera);
        public RendererList CreateWireOverlayRendererList(Camera camera) => RendererLists.Empty();

        public void PrepareRendererListsAsync(List<RendererList> rendererLists) { }

        public RendererListStatus QueryRendererListStatus(RendererList rendererList) => RendererLists.Status(rendererList);

        public void DrawRenderers(CullingResults cullingResults, ref DrawingSettings drawingSettings, ref FilteringSettings filteringSettings)
        {
            var list = RendererLists.Create(new RendererListParams(cullingResults, drawingSettings, filteringSettings));
            RendererLists.Draw(list);
        }

        public void DrawRenderers(CullingResults cullingResults, ref DrawingSettings drawingSettings, ref FilteringSettings filteringSettings, ref RenderStateBlock stateBlock)
        {
            var p = new RendererListParams(cullingResults, drawingSettings, filteringSettings);
            RendererLists.Draw(RendererLists.Create(p, stateBlock));
        }

        public void DrawShadows(ref ShadowDrawingSettings settings) { }

        public void BeginRenderPass(int width, int height, int volumeDepth, int samples, NativeArray<AttachmentDescriptor> attachments, [Internal.DefaultValue("-1")] int depthAttachmentIndex = -1) =>
            RenderPasses.Begin(attachments, depthAttachmentIndex, width, height);

        public void BeginRenderPass(int width, int height, int samples, NativeArray<AttachmentDescriptor> attachments, [Internal.DefaultValue("-1")] int depthAttachmentIndex = -1) =>
            RenderPasses.Begin(attachments, depthAttachmentIndex, width, height);

        public void BeginSubPass(NativeArray<int> colors, NativeArray<int> inputs, bool isDepthReadOnly, bool isStencilReadOnly) => RenderPasses.SubPass(colors.ToArray());
        public void BeginSubPass(NativeArray<int> colors, NativeArray<int> inputs, [Internal.DefaultValue("false")] bool isDepthStencilReadOnly = false) => RenderPasses.SubPass(colors.ToArray());
        public void BeginSubPass(NativeArray<int> colors, bool isDepthReadOnly, bool isStencilReadOnly) => RenderPasses.SubPass(colors.ToArray());
        public void BeginSubPass(NativeArray<int> colors, [Internal.DefaultValue("false")] bool isDepthStencilReadOnly = false) => RenderPasses.SubPass(colors.ToArray());
        public void EndSubPass() { }
        public void EndRenderPass() => RenderPasses.End();

        public bool Equals(ScriptableRenderContext other) => m_Ptr == other.m_Ptr;
        public override bool Equals(object obj) => obj is ScriptableRenderContext other && Equals(other);
        public override int GetHashCode() => m_Ptr.GetHashCode();
        public static bool operator ==(ScriptableRenderContext left, ScriptableRenderContext right) => left.Equals(right);
        public static bool operator !=(ScriptableRenderContext left, ScriptableRenderContext right) => !left.Equals(right);
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>A cull's results, kept for the frame.</summary>
    internal sealed class CullingData
    {
        internal Camera Camera;
        internal ScriptableCullingParameters Parameters;
        internal readonly List<Renderer> Renderers = new List<Renderer>();
        internal NativeArray<VisibleLight> Lights;
        internal NativeArray<VisibleLight> OffscreenVertexLights;
        internal NativeArray<VisibleReflectionProbe> Probes;
        internal int[] LightIndexMap;

        internal void Dispose()
        {
            if (Lights.IsCreated) Lights.Dispose();
            if (OffscreenVertexLights.IsCreated) OffscreenVertexLights.Dispose();
            if (Probes.IsCreated) Probes.Dispose();
        }
    }

    /// <summary>Culling: the renderers a camera's mask sees and the enabled lights (directional ones
    /// first, brightest first), kept until the frame ends.</summary>
    internal static class Culling
    {
        static readonly Dictionary<IntPtr, CullingData> s_Results = new Dictionary<IntPtr, CullingData>();
        static int s_Next;

        internal static CullingData Get(IntPtr id) => s_Results.TryGetValue(id, out var data) ? data : null;

        internal static IntPtr Cull(ref ScriptableCullingParameters parameters)
        {
            var camera = ObjectRegistry.FindById(parameters.m_CameraInstanceId) as Camera;
            var data = new CullingData { Camera = camera, Parameters = parameters };
            uint mask = parameters.cullingMask;
            foreach (var renderer in RenderRegistry.Renderers)
            {
                if (!renderer || renderer.forceRenderingOff || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if ((mask & (1u << renderer.gameObject.layer)) == 0) continue;
                data.Renderers.Add(renderer);
            }
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var lights = new List<Light>();
            foreach (var l in Light.s_Enabled)
                if (l && l.isActiveAndEnabled && (mask & (1u << l.gameObject.layer)) != 0) lights.Add(l);
            lights.Sort((a, b) =>
            {
                int directional = (b.type == LightType.Directional).CompareTo(a.type == LightType.Directional);
                if (directional != 0) return directional;
                return (b.intensity * b.color.grayscale).CompareTo(a.intensity * a.color.grayscale);
            });
            int max = parameters.maximumVisibleLights > 0 ? parameters.maximumVisibleLights : lights.Count;
            if (lights.Count > max) lights.RemoveRange(max, lights.Count - max);
            data.Lights = new NativeArray<VisibleLight>(lights.Count, Allocator.Persistent);
            for (int i = 0; i < lights.Count; i++)
            {
                var rect = camera != null ? new Rect(0, 0, camera.pixelWidth, camera.pixelHeight) : default;
                data.Lights[i] = new VisibleLight(lights[i], rect, linear, lights[i].type != LightType.Directional, false);
            }
            data.OffscreenVertexLights = new NativeArray<VisibleLight>(0, Allocator.Persistent);
            data.Probes = new NativeArray<VisibleReflectionProbe>(0, Allocator.Persistent);
            var id = (IntPtr)(++s_Next);
            s_Results[id] = data;
            return id;
        }

        /// <summary>Frees the frame's culling results (each frame's end).</summary>
        internal static void EndFrame()
        {
            foreach (var data in s_Results.Values) data.Dispose();
            s_Results.Clear();
            RendererLists.EndFrame();
        }
    }

    /// <summary>Renderer lists: what a pipeline asked to draw, drawn when a command buffer draws
    /// the list (the draws pick passes by LightMode, sorted as the drawing settings say).</summary>
    internal static class RendererLists
    {
        enum Kind { Renderers, Skybox, UIOverlay, Empty }

        sealed class Entry
        {
            internal Kind Kind;
            internal RendererListParams Params;
            internal RenderStateBlock? StateBlock;
            internal Camera Camera;
        }

        static readonly List<Entry> s_Lists = new List<Entry>();
        static bool s_WarnedStateBlock;

        static RendererList Add(Entry entry)
        {
            s_Lists.Add(entry);
            return new RendererList(UIntPtr.Zero, (uint)(s_Lists.Count - 1));
        }

        internal static bool IsValid(RendererList list) => list.index < s_Lists.Count;

        internal static RendererList Empty() => Add(new Entry { Kind = Kind.Empty });
        internal static RendererList Skybox(Camera camera) => Add(new Entry { Kind = Kind.Skybox, Camera = camera });
        internal static RendererList UIOverlay(Camera camera) => Add(new Entry { Kind = Kind.UIOverlay, Camera = camera });

        internal static RendererList Create(RendererListParams p, RenderStateBlock? stateBlock = null)
        {
            if (p == RendererListParams.Invalid) return RendererList.nullRendererList;
            if (!stateBlock.HasValue && p.stateBlocks.HasValue && p.stateBlocks.Value.IsCreated && p.stateBlocks.Value.Length > 0)
                stateBlock = p.stateBlocks.Value[0];
            return Add(new Entry { Kind = Kind.Renderers, Params = p, StateBlock = stateBlock });
        }

        internal static RendererListStatus Status(RendererList list)
        {
            if (list.index >= s_Lists.Count) return RendererListStatus.kRendererListInvalid;
            var e = s_Lists[(int)list.index];
            if (e.Kind == Kind.Empty) return RendererListStatus.kRendererListEmpty;
            if (e.Kind == Kind.Renderers && (e.Params.cullingResults.Data?.Renderers.Count ?? 0) == 0) return RendererListStatus.kRendererListEmpty;
            return RendererListStatus.kRendererListPopulated;
        }

        internal static void EndFrame() => s_Lists.Clear();

        internal static void DrawUIOverlay(Camera camera) => FrameBuilder.DrawOverlayCanvases(camera);

        /// <summary>Draws a list now (on the stream).</summary>
        internal static void Draw(RendererList list)
        {
            if (list.index >= s_Lists.Count) return;
            var e = s_Lists[(int)list.index];
            switch (e.Kind)
            {
                case Kind.Renderers: DrawRenderers(e); break;
                case Kind.UIOverlay: DrawUIOverlay(e.Camera); break;
                case Kind.Skybox: FrameBuilder.DrawSkybox(e.Camera); break;
            }
        }

        static void DrawRenderers(Entry e)
        {
            var data = e.Params.cullingResults.Data;
            if (data == null || data.Camera == null) return;
            if (e.StateBlock.HasValue && e.StateBlock.Value.mask != RenderStateMask.Nothing && !s_WarnedStateBlock)
            {
                s_WarnedStateBlock = true;
                Debug.LogWarning("Unbound: render state overrides (RenderStateBlock) of renderer lists are not applied yet.");
            }
            var drawing = e.Params.drawSettings;
            var filtering = e.Params.filteringSettings;
            var passNames = drawing.PassNames();
            FrameBuilder.DrawRendererList(data, drawing, filtering, passNames);
        }
    }

    /// <summary>Render passes (BeginRenderPass/subpasses): each subpass draws into its first color
    /// attachment and the depth attachment, cleared as their load actions say.</summary>
    internal static class RenderPasses
    {
        static AttachmentDescriptor[] s_Attachments = Array.Empty<AttachmentDescriptor>();
        static int s_Depth = -1;
        static readonly HashSet<int> s_Cleared = new HashSet<int>();
        static bool s_WarnedMrt;

        internal static void Begin(NativeArray<AttachmentDescriptor> attachments, int depthAttachmentIndex, int width, int height)
        {
            s_Attachments = attachments.ToArray();
            s_Depth = depthAttachmentIndex;
            s_Cleared.Clear();
        }

        internal static void Begin(NativeArray<AttachmentDescriptor> attachments, int depthAttachmentIndex, NativeArray<SubPassDescriptor> subPasses, int width, int height)
        {
            Begin(attachments, depthAttachmentIndex, width, height);
            if (subPasses.IsCreated && subPasses.Length > 0) SubPass(Indices(subPasses[0].colorOutputs));
        }

        static int[] Indices(AttachmentIndexArray a)
        {
            var result = new int[a.Length];
            for (int i = 0; i < a.Length; i++) result[i] = a[i];
            return result;
        }

        internal static void Next(NativeArray<SubPassDescriptor> subPasses, ref int index)
        {
            index++;
            if (subPasses.IsCreated && index < subPasses.Length) SubPass(Indices(subPasses[index].colorOutputs));
        }

        internal static void SubPass(int[] colors)
        {
            if (colors.Length > 1 && !s_WarnedMrt)
            {
                s_WarnedMrt = true;
                Debug.LogWarning("Unbound: subpasses with several color outputs are not supported yet; the first is drawn into.");
            }
            ulong color = 0, depth = GfxStream.NoTexture;
            Texture colorTexture = null;
            var context = new CommandBuffer.Context { Camera = ScriptableRenderContext.CurrentCamera, CameraTarget = ScriptableRenderContext.CurrentCamera != null ? ScriptableRenderContext.CurrentCamera.targetTexture : null };
            int colorIndex = colors.Length > 0 ? colors[0] : -1;
            if (colorIndex >= 0 && colorIndex < s_Attachments.Length)
            {
                colorTexture = CommandBuffer.Resolve(s_Attachments[colorIndex].loadStoreTarget, context);
                color = TextureHandles.Of(colorTexture);
            }
            else if (s_Depth >= 0 && s_Depth < s_Attachments.Length)
            {
                // A depth-only pass draws into the depth attachment's own texture.
                colorTexture = CommandBuffer.Resolve(s_Attachments[s_Depth].loadStoreTarget, context);
                color = TextureHandles.Of(colorTexture);
            }
            if (s_Depth >= 0 && s_Depth < s_Attachments.Length)
                depth = TextureHandles.Of(CommandBuffer.Resolve(s_Attachments[s_Depth].loadStoreTarget, context));
            RenderTexture.SetActiveQuietly(colorTexture as RenderTexture);
            GfxStream.SetTarget(color, depth == color ? 0 : depth);
            byte flags = 0;
            var clearColor = Color.black;
            float clearDepth = 1f;
            uint clearStencil = 0;
            if (colorIndex >= 0 && colorIndex < s_Attachments.Length && s_Attachments[colorIndex].loadAction == RenderBufferLoadAction.Clear && s_Cleared.Add(colorIndex))
            {
                flags |= GfxStream.ClearColor;
                clearColor = s_Attachments[colorIndex].clearColor;
            }
            if (s_Depth >= 0 && s_Depth < s_Attachments.Length && s_Attachments[s_Depth].loadAction == RenderBufferLoadAction.Clear && s_Cleared.Add(s_Depth))
            {
                flags |= GfxStream.ClearDepth | GfxStream.ClearStencil;
                clearDepth = s_Attachments[s_Depth].clearDepth;
                clearStencil = s_Attachments[s_Depth].clearStencil;
            }
            if (flags != 0) GfxStream.Clear(flags, clearColor, clearDepth, clearStencil);
        }

        internal static void End()
        {
            s_Attachments = Array.Empty<AttachmentDescriptor>();
            s_Depth = -1;
        }
    }
}
