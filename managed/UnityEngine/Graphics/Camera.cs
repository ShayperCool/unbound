using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A view into the scene: its matrices, clearing and the space conversions games use.</summary>
    [ClassId(20)]
    public sealed partial class Camera : Behaviour
    {
        public delegate void CameraCallback(Camera cam);

        public static CameraCallback onPreCull;
        public static CameraCallback onPreRender;
        public static CameraCallback onPostRender;

        public enum MonoOrStereoscopicEye
        {
            Left = 0,
            Right = 1,
            Mono = 2,
        }

        public enum StereoscopicEye
        {
            Left = 0,
            Right = 1,
        }

        public enum GateFitMode
        {
            Vertical = 1,
            Horizontal = 2,
            Fill = 3,
            Overscan = 4,
            None = 0,
        }

        internal static readonly List<Camera> s_Enabled = new List<Camera>();

        CameraClearFlags m_ClearFlags = CameraClearFlags.Skybox;
        Color m_BackgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);
        Rect m_NormalizedViewPortRect = new Rect(0, 0, 1, 1);
        float m_NearClipPlane = 0.3f;
        float m_FarClipPlane = 1000f;
        float m_FieldOfView = 60f;
        bool m_UsePhysicalProperties;
        float m_FocalLength = 50f;
        Vector2 m_SensorSize = new Vector2(36f, 24f);
        Vector2 m_LensShift;
        GateFitMode m_GateFit = GateFitMode.Horizontal;
        bool m_Orthographic;
        float m_OrthographicSize = 5f;
        float m_Depth;
        int m_CullingMask = -1;
        RenderTexture m_TargetTexture;
        int m_TargetDisplay;
        bool m_AllowHDR = true;
        bool m_AllowMSAA = true;
        float m_Aspect = -1f;
        bool m_HasCustomProjection;
        Matrix4x4 m_CustomProjection;
        bool m_HasCustomWorldToCamera;
        Matrix4x4 m_CustomWorldToCamera;
        OpaqueSortMode m_OpaqueSortMode;
        TransparencySortMode m_TransparencySortMode;
        Vector3 m_TransparencySortAxis = new Vector3(0, 0, 1);
        DepthTextureMode m_DepthTextureMode;
        CameraType m_CameraType = CameraType.Game;
        int m_EventMask = -1;

        public Camera() { }

        internal override void OnEngineEnable()
        {
            if (!s_Enabled.Contains(this)) s_Enabled.Add(this);
        }

        internal override void OnEngineDisable() => s_Enabled.Remove(this);

        internal override void OnEngineDestroy() => s_Enabled.Remove(this);

        // ------------------------------------------------------------------ statics

        /// <summary>The first enabled camera tagged "MainCamera".</summary>
        public static Camera main
        {
            get
            {
                Camera best = null;
                foreach (var c in s_Enabled)
                {
                    if (!c.isActiveAndEnabled || c.m_GameObject.m_Tag != "MainCamera") continue;
                    if (best == null || c.m_Depth < best.m_Depth) best = c;
                }
                return best;
            }
        }

        public static Camera current { get; internal set; }

        public static int allCamerasCount => CollectActive().Count;

        public static Camera[] allCameras => CollectActive().ToArray();

        public static int GetAllCameras(Camera[] cameras)
        {
            var list = CollectActive();
            int n = Math.Min(cameras.Length, list.Count);
            for (int i = 0; i < n; i++) cameras[i] = list[i];
            return n;
        }

        /// <summary>The enabled cameras by depth, as Unity lists them (measured:
        /// tests/projects/messages, allCameras in depth order whatever the order they were made or
        /// enabled in); equal depths keep their enabling order.</summary>
        internal static List<Camera> CollectActive()
        {
            var list = new List<Camera>();
            foreach (var c in s_Enabled)
            {
                if (!c || !c.isActiveAndEnabled) continue;
                int at = list.Count;
                while (at > 0 && list[at - 1].m_Depth > c.m_Depth) at--;
                list.Insert(at, c);
            }
            return list;
        }

        // ------------------------------------------------------------------ properties

        public CameraClearFlags clearFlags { get => m_ClearFlags; set => m_ClearFlags = value; }
        public Color backgroundColor { get => m_BackgroundColor; set => m_BackgroundColor = value; }
        public float nearClipPlane { get => m_NearClipPlane; set => m_NearClipPlane = value; }
        public float farClipPlane { get => m_FarClipPlane; set => m_FarClipPlane = value; }
        /// <summary>The vertical field of view. A physical camera keeps it from its focal length
        /// and sensor height (setting it sets the focal length); otherwise the two are independent
        /// (measured on the 6000.0 Editor).</summary>
        public float fieldOfView
        {
            get => m_FieldOfView;
            set
            {
                m_FieldOfView = Mathf.Clamp(value, 1e-5f, 179f);
                if (m_UsePhysicalProperties) m_FocalLength = FieldOfViewToFocalLength(m_FieldOfView, m_SensorSize.y);
            }
        }
        public bool orthographic { get => m_Orthographic; set => m_Orthographic = value; }
        public float orthographicSize { get => m_OrthographicSize; set => m_OrthographicSize = value; }
        public float depth { get => m_Depth; set => m_Depth = value; }
        public int cullingMask { get => m_CullingMask; set => m_CullingMask = value; }
        public int eventMask { get => m_EventMask; set => m_EventMask = value; }
        public RenderTexture targetTexture { get => m_TargetTexture; set => m_TargetTexture = value; }
        public RenderTexture activeTexture => m_TargetTexture;
        public int targetDisplay { get => m_TargetDisplay; set => m_TargetDisplay = value; }
        public bool allowHDR { get => m_AllowHDR; set => m_AllowHDR = value; }
        public bool allowMSAA { get => m_AllowMSAA; set => m_AllowMSAA = value; }
        public bool allowDynamicResolution { get; set; }
        public bool forceIntoRenderTexture { get; set; }
        public bool useOcclusionCulling { get; set; } = true;
        public bool usePhysicalProperties
        {
            get => m_UsePhysicalProperties;
            set
            {
                m_UsePhysicalProperties = value;
                SyncPhysicalFieldOfView();
            }
        }
        public float focalLength
        {
            get => m_FocalLength;
            set
            {
                m_FocalLength = value;
                SyncPhysicalFieldOfView();
            }
        }
        public Vector2 sensorSize
        {
            get => m_SensorSize;
            set
            {
                m_SensorSize = value;
                SyncPhysicalFieldOfView();
            }
        }
        public Vector2 lensShift { get => m_LensShift; set => m_LensShift = value; }
        public GateFitMode gateFit { get => m_GateFit; set => m_GateFit = value; }

        void SyncPhysicalFieldOfView()
        {
            if (m_UsePhysicalProperties) m_FieldOfView = FocalLengthToFieldOfView(m_FocalLength, m_SensorSize.y);
        }
        public bool layerCullSpherical { get; set; }
        public float[] layerCullDistances { get; set; } = new float[32];
        public Matrix4x4 cullingMatrix
        {
            get => m_CullingMatrix ?? projectionMatrix * worldToCameraMatrix;
            set => m_CullingMatrix = value;
        }
        Matrix4x4? m_CullingMatrix;
        public RenderingPath renderingPath { get; set; } = RenderingPath.UsePlayerSettings;
        public RenderingPath actualRenderingPath => RenderingPath.Forward;
        public DepthTextureMode depthTextureMode { get => m_DepthTextureMode; set => m_DepthTextureMode = value; }
        public bool clearStencilAfterLightingPass { get; set; }
        public OpaqueSortMode opaqueSortMode { get => m_OpaqueSortMode; set => m_OpaqueSortMode = value; }
        public TransparencySortMode transparencySortMode { get => m_TransparencySortMode; set => m_TransparencySortMode = value; }
        public Vector3 transparencySortAxis { get => m_TransparencySortAxis; set => m_TransparencySortAxis = value; }
        public CameraType cameraType { get => m_CameraType; set => m_CameraType = value; }
        public ulong overrideSceneCullingMask { get; set; }
        public float stereoSeparation { get; set; } = 0.022f;
        public float stereoConvergence { get; set; } = 10f;
        [Unbound.EditorFaithful("no XR device in the Editor: stereo is never enabled (tests/projects/xr)")]
        public bool stereoEnabled => false;
        public SceneManagement.Scene scene { get; set; }

        public Rect rect
        {
            get => m_NormalizedViewPortRect;
            set => m_NormalizedViewPortRect = value;
        }

        /// <summary>The viewport in pixels of the target (the screen or the target texture).</summary>
        public Rect pixelRect
        {
            get
            {
                float w = TargetWidth, h = TargetHeight;
                var r = m_NormalizedViewPortRect;
                float x0 = Mathf.Clamp01(r.x), y0 = Mathf.Clamp01(r.y);
                float x1 = Mathf.Clamp01(r.xMax), y1 = Mathf.Clamp01(r.yMax);
                return new Rect(x0 * w, y0 * h, Math.Max(0f, x1 - x0) * w, Math.Max(0f, y1 - y0) * h);
            }
            set
            {
                float w = TargetWidth, h = TargetHeight;
                m_NormalizedViewPortRect = new Rect(value.x / w, value.y / h, value.width / w, value.height / h);
            }
        }

        float TargetWidth => m_TargetTexture != null ? m_TargetTexture.width : Screen.width;
        float TargetHeight => m_TargetTexture != null ? m_TargetTexture.height : Screen.height;

        public int pixelWidth => (int)pixelRect.width;
        public int pixelHeight => (int)pixelRect.height;
        public int scaledPixelWidth => pixelWidth;
        public int scaledPixelHeight => pixelHeight;

        public float aspect
        {
            get
            {
                if (m_Aspect > 0f) return m_Aspect;
                var r = pixelRect;
                return r.height > 0f ? r.width / r.height : 1f;
            }
            set => m_Aspect = value;
        }

        public void ResetAspect() => m_Aspect = -1f;

        // ------------------------------------------------------------------ matrices

        /// <summary>World to camera space: right-handed, the camera looking down -Z (as OpenGL).</summary>
        public Matrix4x4 worldToCameraMatrix
        {
            get
            {
                if (m_HasCustomWorldToCamera) return m_CustomWorldToCamera;
                var t = transform;
                var cameraToWorld = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                return Matrix4x4.Scale(new Vector3(1, 1, -1)) * cameraToWorld.inverse;
            }
            set
            {
                m_CustomWorldToCamera = value;
                m_HasCustomWorldToCamera = true;
            }
        }

        public Matrix4x4 cameraToWorldMatrix => worldToCameraMatrix.inverse;

        public void ResetWorldToCameraMatrix() => m_HasCustomWorldToCamera = false;

        public Matrix4x4 projectionMatrix
        {
            get
            {
                if (m_HasCustomProjection) return m_CustomProjection;
                if (m_Orthographic)
                {
                    float h = m_OrthographicSize, w = h * aspect;
                    return Matrix4x4.Ortho(-w, w, -h, h, m_NearClipPlane, m_FarClipPlane);
                }
                if (m_UsePhysicalProperties)
                {
                    CalculateProjectionMatrixFromPhysicalProperties(out var physical, m_FocalLength, m_SensorSize, m_LensShift,
                        m_NearClipPlane, m_FarClipPlane, new GateFitParameters(m_GateFit, aspect));
                    return physical;
                }
                return Matrix4x4.Perspective(m_FieldOfView, aspect, m_NearClipPlane, m_FarClipPlane);
            }
            set
            {
                m_CustomProjection = value;
                m_HasCustomProjection = true;
            }
        }

        public Matrix4x4 nonJitteredProjectionMatrix { get => projectionMatrix; set => projectionMatrix = value; }
        public Matrix4x4 previousViewProjectionMatrix => projectionMatrix * worldToCameraMatrix;
        public bool useJitteredProjectionMatrixForTransparentRendering { get; set; }

        public void ResetProjectionMatrix() => m_HasCustomProjection = false;
        public void ResetCullingMatrix() => m_CullingMatrix = null;
        [Unbound.EditorFaithful("no replacement shader is ever applied (SetReplacementShader warns), so there is nothing to reset")]
        public void ResetReplacementShader() { }
        public void ResetTransparencySortSettings()
        {
            m_TransparencySortMode = TransparencySortMode.Default;
            m_TransparencySortAxis = new Vector3(0, 0, 1);
        }

        public Matrix4x4 GetStereoViewMatrix(StereoscopicEye eye) => worldToCameraMatrix;
        public Matrix4x4 GetStereoProjectionMatrix(StereoscopicEye eye) => projectionMatrix;

        /// <summary>A gate fit's parameters: the mode and the aspect of the image to fit.</summary>
        public struct GateFitParameters
        {
            public GateFitParameters(GateFitMode mode, float aspect)
            {
                this.mode = mode;
                this.aspect = aspect;
            }

            public GateFitMode mode { get; set; }
            public float aspect { get; set; }
        }

        /// <summary>The axis a gate fit keeps the sensor's size along: Fill fits the sensor's
        /// shorter side relative to the image, Overscan its longer one; None stretches both.</summary>
        static GateFitMode ResolveGateFit(GateFitMode mode, float aspect, Vector2 sensorSize)
        {
            bool wider = aspect > sensorSize.x / sensorSize.y;
            return mode switch
            {
                GateFitMode.Fill => wider ? GateFitMode.Horizontal : GateFitMode.Vertical,
                GateFitMode.Overscan => wider ? GateFitMode.Vertical : GateFitMode.Horizontal,
                _ => mode,
            };
        }

        /// <summary>The lens shift in units of the fitted image: the fitted axis keeps the shift,
        /// the other scales by how much of the sensor the image covers along it. None reports the
        /// vertical fit's shift (measured on the 6000.0 Editor).</summary>
        static Vector2 GateFittedLensShift(GateFitMode mode, float aspect, Vector2 sensorSize, Vector2 lensShift)
        {
            float sensorAspect = sensorSize.x / sensorSize.y;
            return ResolveGateFit(mode, aspect, sensorSize) == GateFitMode.Horizontal
                ? new Vector2(lensShift.x, lensShift.y * aspect / sensorAspect)
                : new Vector2(lensShift.x * sensorAspect / aspect, lensShift.y);
        }

        /// <summary>A physical camera's projection (measured on the 6000.0 Editor): the fitted
        /// axis spans the sensor at the focal length, the other follows the aspect; None maps the
        /// sensor onto the image as is. The lens shift moves the image by twice its fitted value
        /// in clip space (None: by twice the shift itself).</summary>
        public static void CalculateProjectionMatrixFromPhysicalProperties(out Matrix4x4 output, float focalLength, Vector2 sensorSize, Vector2 lensShift,
            float nearClip, float farClip, GateFitParameters gateFitParameters = default)
        {
            float aspect = gateFitParameters.aspect;
            var mode = ResolveGateFit(gateFitParameters.mode, aspect, sensorSize);
            float x, y;
            Vector2 shift;
            switch (mode)
            {
                case GateFitMode.Vertical:
                    y = 2f * focalLength / sensorSize.y;
                    x = y / aspect;
                    shift = GateFittedLensShift(mode, aspect, sensorSize, lensShift);
                    break;
                case GateFitMode.Horizontal:
                    x = 2f * focalLength / sensorSize.x;
                    y = x * aspect;
                    shift = GateFittedLensShift(mode, aspect, sensorSize, lensShift);
                    break;
                default:
                    x = 2f * focalLength / sensorSize.x;
                    y = 2f * focalLength / sensorSize.y;
                    shift = lensShift;
                    break;
            }
            output = Matrix4x4.zero;
            output.m00 = x;
            output.m11 = y;
            output.m02 = 2f * shift.x;
            output.m12 = 2f * shift.y;
            output.m22 = -(farClip + nearClip) / (farClip - nearClip);
            output.m23 = -2f * farClip * nearClip / (farClip - nearClip);
            output.m32 = -1f;
        }

        /// <summary>The vertical field of view after the gate fit (the set one when not physical).</summary>
        public float GetGateFittedFieldOfView()
        {
            if (!m_UsePhysicalProperties) return m_FieldOfView;
            CalculateProjectionMatrixFromPhysicalProperties(out var m, m_FocalLength, m_SensorSize, m_LensShift, m_NearClipPlane, m_FarClipPlane,
                new GateFitParameters(m_GateFit, aspect));
            return 2f * Mathf.Atan(1f / m.m11) * Mathf.Rad2Deg;
        }

        public Vector2 GetGateFittedLensShift() =>
            m_UsePhysicalProperties ? GateFittedLensShift(m_GateFit, aspect, m_SensorSize, m_LensShift) : m_LensShift;

        public static float FieldOfViewToFocalLength(float fieldOfView, float sensorSize) => sensorSize * 0.5f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
        public static float FocalLengthToFieldOfView(float focalLength, float sensorSize) => 2f * Mathf.Atan(sensorSize * 0.5f / focalLength) * Mathf.Rad2Deg;
        public static float HorizontalToVerticalFieldOfView(float horizontalFieldOfView, float aspectRatio) =>
            2f * Mathf.Atan(Mathf.Tan(horizontalFieldOfView * 0.5f * Mathf.Deg2Rad) / aspectRatio) * Mathf.Rad2Deg;
        public static float VerticalToHorizontalFieldOfView(float verticalFieldOfView, float aspectRatio) =>
            2f * Mathf.Atan(Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad) * aspectRatio) * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ space conversion

        public Vector3 WorldToScreenPoint(Vector3 position) => WorldToScreenPoint(position, MonoOrStereoscopicEye.Mono);

        public Vector3 WorldToScreenPoint(Vector3 position, MonoOrStereoscopicEye eye)
        {
            var viewport = WorldToViewportPoint(position);
            var r = pixelRect;
            return new Vector3(r.x + viewport.x * r.width, r.y + viewport.y * r.height, viewport.z);
        }

        public Vector3 WorldToViewportPoint(Vector3 position) => WorldToViewportPoint(position, MonoOrStereoscopicEye.Mono);

        public Vector3 WorldToViewportPoint(Vector3 position, MonoOrStereoscopicEye eye)
        {
            var view = worldToCameraMatrix.MultiplyPoint(position);
            var clip = projectionMatrix * new Vector4(view.x, view.y, view.z, 1f);
            if (Mathf.Approximately(clip.w, 0f)) return new Vector3(0.5f, 0.5f, -view.z);
            return new Vector3(clip.x / clip.w * 0.5f + 0.5f, clip.y / clip.w * 0.5f + 0.5f, -view.z);
        }

        public Vector3 ViewportToWorldPoint(Vector3 position) => ViewportToWorldPoint(position, MonoOrStereoscopicEye.Mono);

        public Vector3 ViewportToWorldPoint(Vector3 position, MonoOrStereoscopicEye eye)
        {
            // position.z is the distance from the camera along its forward axis.
            var invProjection = projectionMatrix.inverse;
            var ndc = new Vector3(position.x * 2f - 1f, position.y * 2f - 1f, 0f);
            Vector3 viewPoint;
            if (m_Orthographic)
            {
                var p = invProjection.MultiplyPoint(ndc);
                viewPoint = new Vector3(p.x, p.y, -position.z);
            }
            else
            {
                var near = invProjection.MultiplyPoint(new Vector3(ndc.x, ndc.y, -1f));
                var dir = near / -near.z;
                viewPoint = dir * position.z;
            }
            return cameraToWorldMatrix.MultiplyPoint(viewPoint);
        }

        public Vector3 ScreenToWorldPoint(Vector3 position) => ScreenToWorldPoint(position, MonoOrStereoscopicEye.Mono);

        public Vector3 ScreenToWorldPoint(Vector3 position, MonoOrStereoscopicEye eye) => ViewportToWorldPoint(ScreenToViewportPoint(position), eye);

        public Vector3 ScreenToViewportPoint(Vector3 position)
        {
            var r = pixelRect;
            return new Vector3(r.width > 0 ? (position.x - r.x) / r.width : 0f, r.height > 0 ? (position.y - r.y) / r.height : 0f, position.z);
        }

        public Vector3 ViewportToScreenPoint(Vector3 position)
        {
            var r = pixelRect;
            return new Vector3(r.x + position.x * r.width, r.y + position.y * r.height, position.z);
        }

        public Ray ViewportPointToRay(Vector3 pos) => ViewportPointToRay(pos, MonoOrStereoscopicEye.Mono);

        public Ray ViewportPointToRay(Vector3 pos, MonoOrStereoscopicEye eye)
        {
            var near = ViewportToWorldPoint(new Vector3(pos.x, pos.y, m_NearClipPlane));
            if (m_Orthographic) return new Ray(near, transform.forward);
            var origin = transform.position;
            return new Ray(origin, near - origin);
        }

        public Ray ScreenPointToRay(Vector3 pos) => ViewportPointToRay(ScreenToViewportPoint(pos));

        public Ray ScreenPointToRay(Vector3 pos, MonoOrStereoscopicEye eye) => ViewportPointToRay(ScreenToViewportPoint(pos), eye);

        public void CalculateFrustumCorners(Rect viewport, float z, MonoOrStereoscopicEye eye, Vector3[] outCorners)
        {
            if (outCorners == null || outCorners.Length < 4) throw new ArgumentException("outCorners minimum size is 4", nameof(outCorners));
            var corners = new[] { new Vector2(viewport.xMin, viewport.yMin), new Vector2(viewport.xMin, viewport.yMax), new Vector2(viewport.xMax, viewport.yMax), new Vector2(viewport.xMax, viewport.yMin) };
            for (int i = 0; i < 4; i++)
            {
                var world = ViewportToWorldPoint(new Vector3(corners[i].x, corners[i].y, z));
                outCorners[i] = transform.InverseTransformPoint(world);
            }
        }

        // ------------------------------------------------------------------ rendering control

        /// <summary>Renders the camera now, into its target texture (the screen when none), as the
        /// frame's rendering does; it works on a disabled camera too.</summary>
        public void Render()
        {
            // A scriptable pipeline renders it as one of its cameras.
            if (GraphicsSettings.currentRenderPipeline != null && RenderPipelineManager.Render(new List<Camera> { this })) return;
            // A render outside the frame's (from Start, say) sets up the lighting environment too.
            FrameBuilder.EmitLightingGlobals();
            FrameBuilder.RenderCamera(this);
        }

        public bool TryGetCullingParameters(out ScriptableCullingParameters cullingParameters) => TryGetCullingParameters(false, out cullingParameters);

        /// <summary>The camera's culling parameters: its view-projection, frustum planes, mask,
        /// layer distances and the shadow distance. False for an empty viewport.</summary>
        public bool TryGetCullingParameters(bool stereoAware, out ScriptableCullingParameters cullingParameters)
        {
            cullingParameters = default;
            if (pixelWidth <= 0 || pixelHeight <= 0) return false;
            var t = transform;
            var matrix = projectionMatrix * worldToCameraMatrix;
            cullingParameters.m_CameraInstanceId = GetInstanceID();
            cullingParameters.cullingMask = (uint)cullingMask;
            cullingParameters.cullingMatrix = matrix;
            cullingParameters.origin = t.position;
            cullingParameters.lodParameters = new LODParameters
            {
                isOrthographic = orthographic,
                cameraPosition = t.position,
                fieldOfView = fieldOfView,
                orthoSize = orthographicSize,
                cameraPixelHeight = pixelHeight,
            };
            var planes = FrustumPlanes(matrix);
            cullingParameters.cullingPlaneCount = planes.Length;
            for (int i = 0; i < planes.Length; i++) cullingParameters.SetCullingPlane(i, planes[i]);
            var distances = layerCullDistances;
            for (int i = 0; i < 32; i++) cullingParameters.SetLayerCullingDistance(i, distances != null && i < distances.Length && distances[i] > 0 ? distances[i] : farClipPlane);
            cullingParameters.shadowDistance = QualitySettings.shadowDistance;
            cullingParameters.cullingOptions = CullingOptions.NeedsLighting | CullingOptions.NeedsReflectionProbes | CullingOptions.ShadowCasters
                | (useOcclusionCulling ? CullingOptions.OcclusionCull : CullingOptions.None);
            cullingParameters.reflectionProbeSortingCriteria = ReflectionProbeSortingCriteria.ImportanceThenSize;
            cullingParameters.accurateOcclusionThreshold = -1f;
            cullingParameters.maximumPortalCullingJobs = 16;
            cullingParameters.maximumVisibleLights = -1;
            cullingParameters.numIterationsEnclosingSphere = 64;
            cullingParameters.stereoViewMatrix = worldToCameraMatrix;
            cullingParameters.stereoProjectionMatrix = projectionMatrix;
            return true;
        }

        /// <summary>The planes of a view-projection's frustum (left, right, bottom, top, near, far),
        /// normals inward.</summary>
        internal static Plane[] FrustumPlanes(in Matrix4x4 m)
        {
            var planes = new Plane[6];
            Vector4 row0 = m.GetRow(0), row1 = m.GetRow(1), row2 = m.GetRow(2), row3 = m.GetRow(3);
            Vector4[] coefficients = { row3 + row0, row3 - row0, row3 + row1, row3 - row1, row3 + row2, row3 - row2 };
            for (int i = 0; i < 6; i++)
            {
                var c = coefficients[i];
                var normal = new Vector3(c.x, c.y, c.z);
                float length = normal.magnitude;
                planes[i] = length > 0 ? new Plane(normal / length, c.w / length) : new Plane(Vector3.up, 0);
            }
            return planes;
        }

        readonly Dictionary<CameraEvent, List<CommandBuffer>> m_CommandBuffers = new Dictionary<CameraEvent, List<CommandBuffer>>();

        internal List<CommandBuffer> CommandBuffersAt(CameraEvent evt) => m_CommandBuffers.TryGetValue(evt, out var list) ? list : null;

        public int commandBufferCount
        {
            get
            {
                int n = 0;
                foreach (var list in m_CommandBuffers.Values) n += list.Count;
                return n;
            }
        }

        public void AddCommandBuffer(CameraEvent evt, CommandBuffer buffer)
        {
            if (buffer == null) throw new NullReferenceException("buffer is null");
            if (!m_CommandBuffers.TryGetValue(evt, out var list)) m_CommandBuffers[evt] = list = new List<CommandBuffer>();
            list.Add(buffer);
        }

        public void AddCommandBufferAsync(CameraEvent evt, CommandBuffer buffer, ComputeQueueType queueType) => AddCommandBuffer(evt, buffer);

        public void RemoveCommandBuffer(CameraEvent evt, CommandBuffer buffer)
        {
            if (m_CommandBuffers.TryGetValue(evt, out var list)) list.Remove(buffer);
        }

        public void RemoveCommandBuffers(CameraEvent evt) => m_CommandBuffers.Remove(evt);

        public void RemoveAllCommandBuffers() => m_CommandBuffers.Clear();

        public CommandBuffer[] GetCommandBuffers(CameraEvent evt) => m_CommandBuffers.TryGetValue(evt, out var list) ? list.ToArray() : new CommandBuffer[0];

        public void RenderWithShader(Shader shader, string replacementTag) => Stubs.Ignored("Camera.RenderWithShader", "is not drawn yet");

        public void SetReplacementShader(Shader shader, string replacementTag) => Stubs.Ignored("Camera.SetReplacementShader", "is not honoured yet: the camera draws its normal shaders");

        /// <summary>This camera takes the other's settings, and also its transform's world position
        /// and rotation, local scale and its GameObject's layer (measured on the 6000.0 Editor).</summary>
        public void CopyFrom(Camera other)
        {
            if (other == null) return;
            CopySettings(other);
            var from = other.transform;
            var to = transform;
            to.SetPositionAndRotation(from.position, from.rotation);
            to.localScale = from.localScale;
            gameObject.layer = other.gameObject.layer;
        }

        void CopySettings(Camera other)
        {
            m_ClearFlags = other.m_ClearFlags;
            m_BackgroundColor = other.m_BackgroundColor;
            m_NormalizedViewPortRect = other.m_NormalizedViewPortRect;
            m_NearClipPlane = other.m_NearClipPlane;
            m_FarClipPlane = other.m_FarClipPlane;
            m_FieldOfView = other.m_FieldOfView;
            m_UsePhysicalProperties = other.m_UsePhysicalProperties;
            m_FocalLength = other.m_FocalLength;
            m_SensorSize = other.m_SensorSize;
            m_LensShift = other.m_LensShift;
            m_GateFit = other.m_GateFit;
            m_Orthographic = other.m_Orthographic;
            m_OrthographicSize = other.m_OrthographicSize;
            m_Depth = other.m_Depth;
            m_CullingMask = other.m_CullingMask;
            m_TargetTexture = other.m_TargetTexture;
            m_TargetDisplay = other.m_TargetDisplay;
            m_AllowHDR = other.m_AllowHDR;
            m_AllowMSAA = other.m_AllowMSAA;
            m_DepthTextureMode = other.m_DepthTextureMode;
            m_EventMask = other.m_EventMask;
            m_OpaqueSortMode = other.m_OpaqueSortMode;
            m_TransparencySortMode = other.m_TransparencySortMode;
            useOcclusionCulling = other.useOcclusionCulling;
            allowDynamicResolution = other.allowDynamicResolution;
            renderingPath = other.renderingPath;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var other = (Camera)source;
            CopySettings(other);
            m_TargetTexture = remap(other.m_TargetTexture) as RenderTexture;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_ClearFlags = (CameraClearFlags)body.Int("m_ClearFlags", 1);
            m_BackgroundColor = body.Color("m_BackGroundColor", m_BackgroundColor);
            var rect = body.Get("m_NormalizedViewPortRect");
            if (rect != null) m_NormalizedViewPortRect = new Rect(rect.Float("x"), rect.Float("y"), rect.Float("width", 1), rect.Float("height", 1));
            m_NearClipPlane = body.Float("near clip plane", 0.3f);
            m_FarClipPlane = body.Float("far clip plane", 1000f);
            m_FieldOfView = body.Float("field of view", 60f);
            // m_projectionMatrixMode 2 is "physical properties based".
            m_UsePhysicalProperties = body.Int("m_projectionMatrixMode", 1) == 2;
            m_FocalLength = body.Float("m_FocalLength", 50f);
            if (body.Get("m_SensorSize") != null) m_SensorSize = body.Vector2("m_SensorSize");
            m_LensShift = body.Vector2("m_LensShift");
            m_GateFit = (GateFitMode)body.Int("m_GateFitMode", (int)GateFitMode.Horizontal);
            SyncPhysicalFieldOfView();
            m_Orthographic = body.Bool("orthographic");
            m_OrthographicSize = body.Float("orthographic size", 5f);
            m_Depth = body.Float("m_Depth");
            m_CullingMask = (int)(body.Get("m_CullingMask")?.Long("m_Bits", -1) ?? -1);
            var target = body.Ref("m_TargetTexture");
            m_TargetTexture = target.IsNull ? null : resolver.Resolve(target, typeof(RenderTexture)) as RenderTexture;
            m_TargetDisplay = body.Int("m_TargetDisplay");
            m_AllowHDR = body.Bool("m_HDR", true);
            m_AllowMSAA = body.Bool("m_AllowMSAA", true);
        }
    }

}
