using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // Realtime reflection probes (measured: tests/projects/builtinfx probe.*): rendered before the
    // frame's cameras, once when enabled (OnAwake), every frame (EveryFrame) or when RenderProbe asks
    // (ViaScripting), into an HDR cube of their resolution, as a camera at the probe's position with
    // its clearing, culling mask and clip planes. A render counts as finished the frame after
    // (NoTimeSlicing), three frames after (AllFacesAtOnce) or eight (IndividualFaces); Unbound draws
    // every face at once. Nothing renders while QualitySettings.realtimeReflectionProbes is off.
    public sealed partial class ReflectionProbe
    {
        bool m_Awoken, m_Pending;
        RenderTexture m_RenderTarget;
        RenderTexture m_RealtimeTexture;
        int m_PendingId;
        readonly Dictionary<int, int> m_Finished = new Dictionary<int, int>();
        static int s_NextRenderId;
        static Camera s_Camera;
        static Cubemap s_Black;

        /// <summary>The cube a realtime probe renders into (made by its first render).</summary>
        public RenderTexture realtimeTexture { get => m_RealtimeTexture; set => m_RealtimeTexture = value; }

        /// <summary>A black cube: what a realtime probe reflects before it renders.</summary>
        internal static Cubemap Black
        {
            get
            {
                if (s_Black == null)
                {
                    using (ObjectRegistry.AsAssets())
                        s_Black = new Cubemap(1, TextureFormat.RGBA32, false) { name = "Unbound black cube", hideFlags = HideFlags.HideAndDontSave };
                    for (int f = 0; f < 6; f++) s_Black.SetPixel((CubemapFace)f, 0, 0, Color.black);
                    s_Black.Apply();
                }
                return s_Black;
            }
        }

        public int RenderProbe() => RenderProbe(null);

        /// <summary>Asks for a render (into <paramref name="targetTexture"/>, else the probe's own
        /// cube) before the next frame's cameras; the id tells when it has finished.</summary>
        public int RenderProbe(RenderTexture targetTexture)
        {
            int id = ++s_NextRenderId;
            m_RenderTarget = targetTexture;
            m_Pending = true;
            m_PendingId = id;
            return id;
        }

        public bool IsFinishedRendering(int renderId) =>
            m_Finished.TryGetValue(renderId, out int frame) && Time.frameCount >= frame;

        /// <summary>Blends two cubemaps face by face into a cube render texture with Unity's
        /// Hidden/CubeBlend (alpha 1).</summary>
        public static bool BlendCubemap(Texture src, Texture dst, float blend, RenderTexture target)
        {
            if (src == null || dst == null || target == null || target.dimension != TextureDimension.Cube) return false;
            var shader = Shader.Find("Hidden/CubeBlend");
            if (shader == null) return false;
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_TexA", src);
            material.SetTexture("_TexB", dst);
            material.SetVector("_TexA_HDR", DecodeValues(src));
            material.SetVector("_TexB_HDR", DecodeValues(dst));
            material.SetFloat("_value", blend);
            material.SetFloat("_Level", 0f);
            var active = RenderTexture.active;
            for (int face = 0; face < 6; face++)
            {
                Graphics.SetRenderTarget(target, 0, (CubemapFace)face);
                GL.PushMatrix();
                GL.LoadIdentity();
                GL.LoadProjectionMatrix(Matrix4x4.identity);
                material.SetPass(0);
                GL.Begin(GL.QUADS);
                // The face's corners in clip space, each with the direction it looks along.
                var (forward, up) = s_FaceAxes[face];
                var right = Vector3.Cross(up, forward);
                for (int c = 0; c < 4; c++)
                {
                    // Clockwise: the shader culls back faces.
                    float x = c < 2 ? -1f : 1f, y = c == 0 || c == 3 ? -1f : 1f;
                    var direction = forward + right * x - up * y;
                    GL.TexCoord3(direction.x, direction.y, direction.z);
                    GL.Vertex3(x, y, 0.5f);
                }
                GL.End();
                GL.PopMatrix();
            }
            RenderTexture.active = active;
            Object.DestroyImmediate(material);
            return true;
        }

        /// <summary>Each cube face's direction and the direction its rows go up to (OpenGL's cube
        /// map layout, rows from the top).</summary>
        static readonly (Vector3 Forward, Vector3 Up)[] s_FaceAxes =
        {
            (Vector3.right, Vector3.up), (Vector3.left, Vector3.up),
            (Vector3.up, Vector3.back), (Vector3.down, Vector3.forward),
            (Vector3.forward, Vector3.up), (Vector3.back, Vector3.up),
        };

        /// <summary>Before the frame's cameras: renders the realtime probes that are due.</summary>
        internal static void UpdateRealtime()
        {
            if (s_Enabled.Count == 0 || !QualitySettings.realtimeReflectionProbes) return;
            foreach (var probe in s_Enabled.ToArray())
            {
                if (!probe || !probe.isActiveAndEnabled || probe.m_Mode != ReflectionProbeMode.Realtime) continue;
                bool due = probe.m_Pending || probe.m_RefreshMode == ReflectionProbeRefreshMode.EveryFrame;
                if (!due) continue;
                probe.m_Pending = false;
                try { probe.Render(); }
                catch (Exception e) { Debug.LogException(e, probe); }
                if (probe.m_PendingId != 0)
                {
                    int frames = probe.m_TimeSlicingMode switch
                    {
                        ReflectionProbeTimeSlicingMode.AllFacesAtOnce => 3,
                        ReflectionProbeTimeSlicingMode.IndividualFaces => 8,
                        _ => 1,
                    };
                    probe.m_Finished[probe.m_PendingId] = Time.frameCount + frames;
                    probe.m_PendingId = 0;
                }
            }
        }

        void Render()
        {
            var target = m_RenderTarget;
            if (target == null)
            {
                int size = Mathf.Clamp(Mathf.NextPowerOfTwo(m_Resolution), 16, 2048);
                if (m_RealtimeTexture == null || m_RealtimeTexture.width != size)
                {
                    if (m_RealtimeTexture != null) Object.DestroyImmediate(m_RealtimeTexture);
                    using (ObjectRegistry.AsAssets())
                        m_RealtimeTexture = new RenderTexture(size, size, 16, m_Hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32)
                        {
                            dimension = TextureDimension.Cube,
                            useMipMap = true,
                            autoGenerateMips = true,
                            name = "Reflection probe " + name,
                            hideFlags = HideFlags.HideAndDontSave,
                        };
                    m_RealtimeTexture.Create();
                }
                target = m_RealtimeTexture;
            }
            else if (target.dimension != TextureDimension.Cube) return;
            if (s_Camera == null)
            {
                GameObject go;
                using (ObjectRegistry.AsAssets())
                    go = new GameObject("Unbound reflection probe camera") { hideFlags = HideFlags.HideAndDontSave };
                s_Camera = go.AddComponent<Camera>();
                s_Camera.enabled = false;
            }
            var camera = s_Camera;
            camera.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
            camera.clearFlags = m_ClearFlags == ReflectionProbeClearFlags.Skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = m_BackgroundColor;
            camera.cullingMask = m_CullingMask;
            camera.nearClipPlane = m_NearClip;
            camera.farClipPlane = m_FarClip;
            camera.allowHDR = m_Hdr;
            camera.allowMSAA = false;
            camera.cameraType = CameraType.Reflection;
            FrameBuilder.RenderCubeFaces(camera, transform.position, target, 63);
        }
    }
}
