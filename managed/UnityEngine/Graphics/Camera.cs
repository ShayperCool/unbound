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

        internal static List<Camera> CollectActive()
        {
            var list = new List<Camera>();
            foreach (var c in s_Enabled) if (c && c.isActiveAndEnabled) list.Add(c);
            return list;
        }

        // ------------------------------------------------------------------ properties

        public CameraClearFlags clearFlags { get => m_ClearFlags; set => m_ClearFlags = value; }
        public Color backgroundColor { get => m_BackgroundColor; set => m_BackgroundColor = value; }
        public float nearClipPlane { get => m_NearClipPlane; set => m_NearClipPlane = value; }
        public float farClipPlane { get => m_FarClipPlane; set => m_FarClipPlane = value; }
        public float fieldOfView { get => m_FieldOfView; set => m_FieldOfView = Mathf.Clamp(value, 1e-5f, 179f); }
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
        public bool usePhysicalProperties { get; set; }
        public bool layerCullSpherical { get; set; }
        public float[] layerCullDistances { get; set; } = new float[32];
        public Matrix4x4 cullingMatrix => projectionMatrix * worldToCameraMatrix;
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
        public bool stereoEnabled => false;
        public SceneManagement.Scene scene { get; set; }
        public Vector2 sensorSize { get; set; } = new Vector2(36f, 24f);
        public Vector2 lensShift { get; set; }
        public float focalLength { get; set; } = 50f;
        public GateFitMode gateFit { get; set; } = GateFitMode.Horizontal;

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
        public void ResetCullingMatrix() { }
        public void ResetReplacementShader() { }
        public void ResetTransparencySortSettings()
        {
            m_TransparencySortMode = TransparencySortMode.Default;
            m_TransparencySortAxis = new Vector3(0, 0, 1);
        }

        public Matrix4x4 GetStereoViewMatrix(StereoscopicEye eye) => worldToCameraMatrix;
        public Matrix4x4 GetStereoProjectionMatrix(StereoscopicEye eye) => projectionMatrix;

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

        public void Render() { }

        public void RenderWithShader(Shader shader, string replacementTag) { }

        public void SetReplacementShader(Shader shader, string replacementTag) { }

        public void CopyFrom(Camera other)
        {
            if (other == null) return;
            m_ClearFlags = other.m_ClearFlags;
            m_BackgroundColor = other.m_BackgroundColor;
            m_NormalizedViewPortRect = other.m_NormalizedViewPortRect;
            m_NearClipPlane = other.m_NearClipPlane;
            m_FarClipPlane = other.m_FarClipPlane;
            m_FieldOfView = other.m_FieldOfView;
            m_Orthographic = other.m_Orthographic;
            m_OrthographicSize = other.m_OrthographicSize;
            m_Depth = other.m_Depth;
            m_CullingMask = other.m_CullingMask;
            m_TargetTexture = other.m_TargetTexture;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var other = (Camera)source;
            CopyFrom(other);
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
