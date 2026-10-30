using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    // Cubemap renders (Camera.RenderToCubemap, realtime reflection probes): the camera renders each
    // face from its position with a 90 degree view along the world's axes, into the face of a cube
    // render texture (the faces laid out as the point lights' shadow cubes are).
    internal static partial class FrameBuilder
    {
        /// <summary>The view of a cube face from a point (the faces' axes and handedness as the
        /// point light shadow cubes have them).</summary>
        internal static Matrix4x4 CubeFaceView(int face, Vector3 p)
        {
            var (f, up) = s_CubeFaces[face];
            var side = Vector3.Cross(f, up).normalized;
            var u = Vector3.Cross(side, f);
            return new Matrix4x4(
                new Vector4(side.x, u.x, -f.x, 0f),
                new Vector4(side.y, u.y, -f.y, 0f),
                new Vector4(side.z, u.z, -f.z, 0f),
                new Vector4(-Vector3.Dot(side, p), -Vector3.Dot(u, p), Vector3.Dot(f, p), 1f));
        }

        /// <summary>Renders the camera's view of the faces in <paramref name="faceMask"/> into a
        /// cube render texture, from <paramref name="position"/>, with its clearing, culling mask
        /// and clip planes; its own matrices, target and viewport are left as they were.</summary>
        internal static void RenderCubeFaces(Camera camera, Vector3 position, RenderTexture cube, int faceMask)
        {
            int size = Math.Max(1, cube.width);
            var descriptor = new RenderTextureDescriptor(size, size, cube.graphicsFormat, 24) { sRGB = cube.sRGB };
            var face = RenderTexture.GetTemporary(descriptor);
            var state = camera.SaveRenderState();
            bool inverted = GL.invertCulling;
            try
            {
                camera.targetTexture = face;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.projectionMatrix = Matrix4x4.Perspective(90f, 1f, camera.nearClipPlane, camera.farClipPlane);
                // The faces are stored the other way up: their culling turns around.
                GL.invertCulling = !inverted;
                for (int i = 0; i < 6; i++)
                {
                    if ((faceMask & (1 << i)) == 0) continue;
                    camera.worldToCameraMatrix = CubeFaceView(i, position);
                    RenderCamera(camera);
                    Graphics.CopyTexture(face, 0, 0, cube, i, 0);
                }
            }
            finally
            {
                GL.invertCulling = inverted;
                camera.RestoreRenderState(state);
                RenderTexture.ReleaseTemporary(face);
            }
        }
    }
}

namespace UnityEngine
{
    using Unbound.Runtime;

    public sealed partial class Camera
    {
        internal (RenderTexture Target, RenderBuffer[] Buffers, RenderBuffer Depth, Rect Rect, bool View, Matrix4x4 ViewMatrix, bool Projection, Matrix4x4 ProjectionMatrix) SaveRenderState() =>
            (m_TargetTexture, m_TargetColorBuffers, m_TargetDepthBuffer, m_NormalizedViewPortRect, m_HasCustomWorldToCamera, m_CustomWorldToCamera, m_HasCustomProjection, m_CustomProjection);

        internal void RestoreRenderState((RenderTexture Target, RenderBuffer[] Buffers, RenderBuffer Depth, Rect Rect, bool View, Matrix4x4 ViewMatrix, bool Projection, Matrix4x4 ProjectionMatrix) s)
        {
            (m_TargetTexture, m_TargetColorBuffers, m_TargetDepthBuffer, m_NormalizedViewPortRect) = (s.Target, s.Buffers, s.Depth, s.Rect);
            (m_HasCustomWorldToCamera, m_CustomWorldToCamera, m_HasCustomProjection, m_CustomProjection) = (s.View, s.ViewMatrix, s.Projection, s.ProjectionMatrix);
        }

        public bool RenderToCubemap(Cubemap cubemap) => RenderToCubemap(cubemap, 63);

        /// <summary>Renders the faces in the mask from the camera's position (its rotation does not
        /// matter) into a cubemap's pixels (measured: tests/projects/builtinfx cubemap_texture_*).</summary>
        public bool RenderToCubemap(Cubemap cubemap, int faceMask)
        {
            if (cubemap == null) return false;
            var cube = RenderTexture.GetTemporary(new RenderTextureDescriptor(cubemap.width, cubemap.width, RenderTextureFormat.ARGB32, 24)
            {
                dimension = TextureDimension.Cube,
                // The faces are stored sRGB-encoded in a linear project, whatever the cubemap's own
                // color space (measured: tests/projects/builtinfx cubemap_texture_*).
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
            });
            var face = RenderTexture.GetTemporary(cube.descriptor.width, cube.descriptor.height, 0, RenderTextureFormat.ARGB32,
                cube.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            try
            {
                if (GraphicsSettings.currentRenderPipeline == null) FrameBuilder.EmitLightingGlobals();
                FrameBuilder.RenderCubeFaces(this, transform.position, cube, faceMask);
                var read = new Texture2D(cubemap.width, cubemap.width, TextureFormat.RGBA32, false, !cube.sRGB);
                for (int i = 0; i < 6; i++)
                {
                    if ((faceMask & (1 << i)) == 0) continue;
                    Graphics.CopyTexture(cube, i, 0, face, 0, 0);
                    RenderTexture.active = face;
                    read.ReadPixels(new Rect(0, 0, cubemap.width, cubemap.width), 0, 0);
                    cubemap.SetPixels(read.GetPixels(), (CubemapFace)i);
                }
                Object.DestroyImmediate(read);
                cubemap.Apply();
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(face);
                RenderTexture.ReleaseTemporary(cube);
            }
            return true;
        }

        public bool RenderToCubemap(RenderTexture cubemap) => RenderToCubemap(cubemap, 63);

        /// <summary>Renders the faces in the mask into a cube render texture; false for any other
        /// texture (measured: a 2D one).</summary>
        public bool RenderToCubemap(RenderTexture cubemap, int faceMask)
        {
            if (cubemap == null || cubemap.dimension != TextureDimension.Cube) return false;
            cubemap.Create();
            if (GraphicsSettings.currentRenderPipeline == null) FrameBuilder.EmitLightingGlobals();
            var active = RenderTexture.active;
            FrameBuilder.RenderCubeFaces(this, transform.position, cubemap, faceMask);
            RenderTexture.active = active;
            return true;
        }

        /// <summary>A 360 capture's eye: the Editor has no stereo, so both eyes render the mono
        /// view.</summary>
        public bool RenderToCubemap(RenderTexture cubemap, int faceMask, MonoOrStereoscopicEye stereoEye) => RenderToCubemap(cubemap, faceMask);
    }
}
