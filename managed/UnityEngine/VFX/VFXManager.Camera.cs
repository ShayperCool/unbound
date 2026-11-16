using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine.VFX
{
    /// <summary>The views of a camera a render pipeline draws (one, without XR).</summary>
    public partial struct VFXCameraXRSettings
    {
        public uint viewTotal;
        public uint viewCount;
        public uint viewOffset;
    }

    /// <summary>
    /// The main camera as effects read it, and the camera entry points a render pipeline calls.
    /// Each manager update takes the main camera (the first enabled camera tagged
    /// "MainCamera") when an enabled effect's graph reads it, with what the graph reads that
    /// does not change during the update: the camera's transform and its viewport. The buffers
    /// the graphs read of it (depth, colour) are the textures the pipeline handed over for it
    /// (<see cref="SetCameraBuffer"/>), as they were at the update. Matches the Unity
    /// 6000.0.71f1 Editor (tests/projects/vfx, <c>camera/*</c>).
    /// </summary>
    public static partial class VFXManager
    {
        /// <summary>What a graph reads of the main camera while there is none (the size of a
        /// 1920x1080 screen).</summary>
        static readonly RectInt s_NoViewport = new RectInt(0, 0, 1920, 1080);
        static readonly VFXCameraXRSettings kDefaultCameraXRSettings = new VFXCameraXRSettings { viewTotal = 1, viewCount = 1, viewOffset = 0 };

        /// <summary>The kinds of camera buffer there are, one bit each.</summary>
        const int CameraBufferKinds = 3;

        /// <summary>The main camera of the last update, null when no effect reads it or there
        /// is none, and the buffers the enabled effects' graphs read of it.</summary>
        static Camera s_MainCamera;
        static VFXCameraBufferTypes s_NeededBuffers;
        /// <summary>The textures handed over for the main camera, per kind (kept until another
        /// one is), and the size given with the last of them.</summary>
        static readonly Texture[] s_HandedBuffers = new Texture[CameraBufferKinds];
        static Vector2Int s_HandedSize;
        /// <summary>What the expressions read during an update: the buffers as they were at its
        /// start (none without a main camera), the camera's transform and viewport.</summary>
        static readonly Texture[] s_Buffers = new Texture[CameraBufferKinds];
        static Matrix4x4 s_MainCameraMatrix = Matrix4x4.identity;
        static RectInt s_MainCameraViewport = s_NoViewport;
        /// <summary>Whether the update said that a graph reads the main camera and there is none
        /// (said once until an update finds one again).</summary>
        static bool s_ToldNoMainCamera;

        /// <summary>Set by the graph package's editor when it loads.</summary>
        internal static bool activateVFX { get; set; }

        /// <summary>Takes the main camera for the update: the enabled effects' graphs say
        /// whether they read it and which of its buffers.</summary>
        internal static void UpdateMainCamera()
        {
            int count = s_Effects.Count;
            var needed = VFXCameraBufferTypes.None;
            bool read = false;
            for (int i = 0; i < count; i++)
            {
                var asset = s_Effects[i].visualEffectAsset;
                if (asset == null) continue;
                needed |= asset.m_Data.CameraBuffers;
                read |= asset.m_Data.ReadsMainCamera;
            }
            s_NeededBuffers = needed;
            s_MainCamera = read ? Camera.main : null;
            if (read)
            {
                if (s_MainCamera == null && !s_ToldNoMainCamera)
                    Debug.LogWarning("Visual effect requires the main camera but there are no active cameras with the \"MainCamera\" tag");
                s_ToldNoMainCamera = s_MainCamera == null;
            }
            if (s_MainCamera == null)
            {
                s_MainCameraMatrix = Matrix4x4.identity;
                s_MainCameraViewport = s_NoViewport;
                Array.Clear(s_Buffers);
                return;
            }
            s_MainCameraMatrix = s_MainCamera.transform.localToWorldMatrix;
            var rect = s_MainCamera.pixelRect;
            s_MainCameraViewport = new RectInt(Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
            for (int i = 0; i < CameraBufferKinds; i++)
            {
                var texture = s_HandedBuffers[i];
                s_Buffers[i] = texture ? texture : null;
            }
        }

        /// <summary>The main camera of the last update, null without one.</summary>
        internal static Camera MainCamera => s_MainCamera;
        internal static Matrix4x4 MainCameraMatrix => s_MainCameraMatrix;
        internal static RectInt MainCameraViewport => s_MainCameraViewport;
        internal static Vector2Int MainCameraScaledSize => s_HandedSize;

        /// <summary>The main camera's buffer of a kind as the update took it; none for a value
        /// that is no single kind.</summary>
        internal static Texture MainCameraBuffer(uint kind) => BufferIndex(kind, out int index) ? s_Buffers[index] : null;

        static bool BufferIndex(uint kind, out int index)
        {
            index = -1;
            if (kind - 1 >= (1u << CameraBufferKinds) - 1 || (kind & (kind - 1)) != 0) return false;
            index = System.Numerics.BitOperations.TrailingZeroCount(kind);
            return true;
        }

        static void CheckCamera(Camera cam)
        {
            Object.ThrowIfNullArgument(cam, nameof(cam));
        }

        /// <summary>The buffers the enabled effects' graphs read of a camera: some only for the
        /// main camera of the last update.</summary>
        public static VFXCameraBufferTypes IsCameraBufferNeeded(Camera cam)
        {
            CheckCamera(cam);
            return cam == s_MainCamera ? s_NeededBuffers : VFXCameraBufferTypes.None;
        }

        /// <summary>Hands over a camera's buffer of one kind, with the size of its viewport in it;
        /// taken only for the main camera of the last update, and read by the next update.</summary>
        public static void SetCameraBuffer(Camera cam, VFXCameraBufferTypes type, Texture buffer, int x, int y, int width, int height)
        {
            CheckCamera(cam);
            if (cam != s_MainCamera) return;
            if (!BufferIndex((uint)type, out int index))
                Debug.LogError($"Invalid value for bufferType: {(uint)type:x}. One and only one type bit should be set");
            else
            {
                // Taken all the same.
                if ((s_NeededBuffers & type) == 0) Debug.LogAssertion("Assertion failed on expression: 'HasFlag(flag, bufferType)'");
                s_HandedBuffers[index] = buffer;
            }
            s_HandedSize = new Vector2Int(width, height);
        }

        /// <summary>Before a camera renders. The work an effect does per camera (sorting,
        /// culling its particles) is not done yet: outputs that need it are not drawn.</summary>
        public static void PrepareCamera(Camera cam) => PrepareCamera(cam, kDefaultCameraXRSettings);

        public static void PrepareCamera(Camera cam, VFXCameraXRSettings camXRSettings) => CheckCamera(cam);

        [Obsolete("Use explicit PrepareCamera and ProcessCameraCommand instead")]
        public static void ProcessCamera(Camera cam)
        {
            PrepareCamera(cam, kDefaultCameraXRSettings);
            Internal_ProcessCameraCommand(cam, null, kDefaultCameraXRSettings, IntPtr.Zero, IntPtr.Zero);
        }

        [Obsolete("Use ProcessCameraCommand with CullingResults to allow culling of VFX per camera")]
        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd) => Internal_ProcessCameraCommand(cam, cmd, kDefaultCameraXRSettings, IntPtr.Zero, IntPtr.Zero);

        [Obsolete("Use ProcessCameraCommand with CullingResults to allow culling of VFX per camera")]
        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings) => Internal_ProcessCameraCommand(cam, cmd, camXRSettings, IntPtr.Zero, IntPtr.Zero);

        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings, CullingResults results) =>
            Internal_ProcessCameraCommand(cam, cmd, camXRSettings, results.ptr, IntPtr.Zero);

        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings, CullingResults results, CullingResults customPassResults) =>
            Internal_ProcessCameraCommand(cam, cmd, camXRSettings, results.ptr, customPassResults.ptr);

        /// <summary>The per-camera commands of the effects into a camera's command buffer: none
        /// yet (see <see cref="PrepareCamera(Camera, VFXCameraXRSettings)"/>).</summary>
        static void Internal_ProcessCameraCommand(Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings, IntPtr cullResults, IntPtr customPassCullResults) => CheckCamera(cam);
    }
}
