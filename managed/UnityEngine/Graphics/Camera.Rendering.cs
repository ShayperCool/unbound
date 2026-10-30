using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    // The camera's rendering controls beyond Render: oblique projections, target buffers, renders
    // that leave their target active, replacement shaders, cubemap renders and the rendering path.
    public sealed partial class Camera
    {
        /// <summary>GraphicsSettings' default rendering path (cameras on UsePlayerSettings).</summary>
        internal static RenderingPath s_DefaultRenderingPath = RenderingPath.Forward;

        /// <summary>The path the camera renders with (measured: tests/projects/builtinfx): its own or
        /// the project's; the legacy deferred lighting path is gone (forward), deferred shading
        /// needs a perspective camera (forward otherwise).</summary>
        public RenderingPath actualRenderingPath
        {
            get
            {
                var path = m_RenderingPath == RenderingPath.UsePlayerSettings ? s_DefaultRenderingPath : m_RenderingPath;
                return path switch
                {
                    RenderingPath.VertexLit => RenderingPath.VertexLit,
                    RenderingPath.DeferredShading when !m_Orthographic => RenderingPath.DeferredShading,
                    _ => RenderingPath.Forward,
                };
            }
        }

        /// <summary>The projection with its near plane moved onto a camera-space plane (Eric
        /// Lengyel's oblique near plane, as Unity computes it); the camera's own is unchanged.</summary>
        public Matrix4x4 CalculateObliqueMatrix(Vector4 clipPlane)
        {
            var projection = projectionMatrix;
            var q = projection.inverse * new Vector4(Sign(clipPlane.x), Sign(clipPlane.y), 1f, 1f);
            var c = clipPlane * (2f / Vector4.Dot(clipPlane, q));
            projection.m20 = c.x - projection.m30;
            projection.m21 = c.y - projection.m31;
            projection.m22 = c.z - projection.m32;
            projection.m23 = c.w - projection.m33;
            return projection;
        }

        static float Sign(float x) => x > 0f ? 1f : x < 0f ? -1f : 0f;

        RenderBuffer[] m_TargetColorBuffers;
        RenderBuffer m_TargetDepthBuffer;

        /// <summary>Renders into a render texture's color and depth buffers instead of the target
        /// texture (which becomes none); the viewport follows the buffers' size.</summary>
        public void SetTargetBuffers(RenderBuffer colorBuffer, RenderBuffer depthBuffer) => SetTargetBuffers(new[] { colorBuffer }, depthBuffer);

        /// <summary>Renders into several color buffers at once (the shaders' outputs in order) and
        /// one depth buffer.</summary>
        public void SetTargetBuffers(RenderBuffer[] colorBuffer, RenderBuffer depthBuffer)
        {
            m_TargetTexture = null;
            m_TargetColorBuffers = colorBuffer != null && colorBuffer.Length > 0 ? (RenderBuffer[])colorBuffer.Clone() : null;
            m_TargetDepthBuffer = depthBuffer;
        }

        /// <summary>The render texture of the first target buffer (null: none set, the screen).</summary>
        internal RenderTexture BufferTarget => m_TargetColorBuffers != null ? m_TargetColorBuffers[0].Texture : null;

        /// <summary>Whether the camera draws into a texture (its target texture or buffers).</summary>
        internal bool RendersOffscreen => m_TargetTexture != null || BufferTarget != null;

        /// <summary>The target buffers' color and depth handles (null when none are set).</summary>
        internal (ulong[] Colors, ulong Depth)? TargetBufferHandles()
        {
            if (m_TargetColorBuffers == null || BufferTarget == null) return null;
            var colors = new ulong[m_TargetColorBuffers.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = m_TargetColorBuffers[i].Handle;
            var depth = m_TargetDepthBuffer.Texture;
            ulong depthHandle = depth == null ? GfxStream.NoTexture : depth == BufferTarget ? 0 : depth.EnsureCreated();
            return (colors, depthHandle);
        }

        /// <summary>Renders as <see cref="Render"/> does, but leaves the camera's target active
        /// (measured: tests/projects/builtinfx dont_restore).</summary>
        public void RenderDontRestore()
        {
            if (GraphicsSettings.currentRenderPipeline != null && RenderPipelineManager.Render(new List<Camera> { this })) return;
            FrameBuilder.EmitLightingGlobals();
            FrameBuilder.RenderCamera(this, restoreActive: false);
        }

        internal Shader m_ReplacementShader;
        internal string m_ReplacementTag;

        /// <summary>From now on the camera draws each object with the replacement shader's SubShader
        /// whose <paramref name="replacementTag"/> tag has the value the object's shader has (objects
        /// without a match are not drawn); with no tag, every object with the shader's first
        /// SubShader. The objects' own material properties apply.</summary>
        public void SetReplacementShader(Shader shader, string replacementTag)
        {
            m_ReplacementShader = shader;
            m_ReplacementTag = replacementTag ?? "";
        }

        public void ResetReplacementShader()
        {
            m_ReplacementShader = null;
            m_ReplacementTag = null;
        }

        /// <summary>Renders once with a replacement shader (as <see cref="SetReplacementShader"/>),
        /// keeping the camera's own setting.</summary>
        public void RenderWithShader(Shader shader, string replacementTag)
        {
            var (savedShader, savedTag) = (m_ReplacementShader, m_ReplacementTag);
            m_ReplacementShader = shader;
            m_ReplacementTag = replacementTag ?? "";
            try { Render(); }
            finally { (m_ReplacementShader, m_ReplacementTag) = (savedShader, savedTag); }
        }
    }
}
