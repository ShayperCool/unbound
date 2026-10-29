using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Shader
    {
        public LocalKeywordSpace keywordSpace => new LocalKeywordSpace(GetInstanceID());

        public static void EnableKeyword(in GlobalKeyword keyword) => EnableKeyword(keyword.name);
        public static void DisableKeyword(in GlobalKeyword keyword) => DisableKeyword(keyword.name);

        public static void SetKeyword(in GlobalKeyword keyword, bool value)
        {
            if (value) EnableKeyword(keyword.name);
            else DisableKeyword(keyword.name);
        }

        public static bool IsKeywordEnabled(in GlobalKeyword keyword) => IsKeywordEnabled(keyword.name);

        public static GlobalKeyword[] enabledGlobalKeywords
        {
            get
            {
                var list = new List<GlobalKeyword>();
                foreach (var k in s_GlobalKeywords) list.Add(new GlobalKeyword(k));
                return list.ToArray();
            }
        }

        public static GlobalKeyword[] globalKeywords => enabledGlobalKeywords;
    }

    public partial class Material
    {
        public void EnableKeyword(in LocalKeyword keyword) => EnableKeyword(keyword.name);
        public void DisableKeyword(in LocalKeyword keyword) => DisableKeyword(keyword.name);

        public void SetKeyword(in LocalKeyword keyword, bool value)
        {
            if (value) EnableKeyword(keyword.name);
            else DisableKeyword(keyword.name);
        }

        public bool IsKeywordEnabled(in LocalKeyword keyword) => IsKeywordEnabled(keyword.name);

        public LocalKeyword[] enabledKeywords
        {
            get
            {
                var list = new List<LocalKeyword>();
                foreach (var k in m_Keywords) list.Add(new LocalKeyword(shader, k));
                return list.ToArray();
            }
            set
            {
                m_Keywords.Clear();
                if (value != null) foreach (var k in value) if (k.isValid) m_Keywords.Add(k.name);
                MarkDirty();
            }
        }
    }

    public sealed partial class ComputeShader
    {
        public LocalKeywordSpace keywordSpace => new LocalKeywordSpace(GetInstanceID());

        public void EnableKeyword(in LocalKeyword keyword) => EnableKeyword(keyword.name);
        public void DisableKeyword(in LocalKeyword keyword) => DisableKeyword(keyword.name);

        public void SetKeyword(in LocalKeyword keyword, bool value)
        {
            if (value) EnableKeyword(keyword.name);
            else DisableKeyword(keyword.name);
        }

        public bool IsKeywordEnabled(in LocalKeyword keyword) => IsKeywordEnabled(keyword.name);

        public LocalKeyword[] enabledKeywords
        {
            get
            {
                var list = new List<LocalKeyword>();
                foreach (var k in shaderKeywords) list.Add(new LocalKeyword(this, k));
                return list.ToArray();
            }
            set
            {
                var names = new List<string>();
                if (value != null) foreach (var k in value) if (k.isValid) names.Add(k.name);
                shaderKeywords = names.ToArray();
            }
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial class CommandBuffer
    {
        public void EnableKeyword(in GlobalKeyword keyword) { var name = keyword.name; Add(c => Shader.EnableKeyword(name)); }
        public void DisableKeyword(in GlobalKeyword keyword) { var name = keyword.name; Add(c => Shader.DisableKeyword(name)); }

        public void SetKeyword(in GlobalKeyword keyword, bool value)
        {
            var name = keyword.name;
            Add(c => { if (value) Shader.EnableKeyword(name); else Shader.DisableKeyword(name); });
        }

        public void EnableKeyword(Material material, in LocalKeyword keyword) { var name = keyword.name; Add(c => material.EnableKeyword(name)); }
        public void DisableKeyword(Material material, in LocalKeyword keyword) { var name = keyword.name; Add(c => material.DisableKeyword(name)); }

        public void SetKeyword(Material material, in LocalKeyword keyword, bool value)
        {
            var name = keyword.name;
            Add(c => { if (value) material.EnableKeyword(name); else material.DisableKeyword(name); });
        }

        public void EnableKeyword(ComputeShader computeShader, in LocalKeyword keyword) { var name = keyword.name; Add(c => computeShader.EnableKeyword(name)); }
        public void DisableKeyword(ComputeShader computeShader, in LocalKeyword keyword) { var name = keyword.name; Add(c => computeShader.DisableKeyword(name)); }

        public void SetKeyword(ComputeShader computeShader, in LocalKeyword keyword, bool value)
        {
            var name = keyword.name;
            Add(c => { if (value) computeShader.EnableKeyword(name); else computeShader.DisableKeyword(name); });
        }

        [Unbound.EditorFaithful("profiler sample: nothing a game observes (no profiler is attached)")]
        public void BeginSample(Unity.Profiling.ProfilerMarker marker) { }
        [Unbound.EditorFaithful("profiler sample: nothing a game observes (no profiler is attached)")]
        public void EndSample(Unity.Profiling.ProfilerMarker marker) { }

        /// <summary>When executed, the texture's <c>updateCount</c> goes up (only a texture object
        /// has one to read; temporaries and built-in targets have none).</summary>
        public void IncrementUpdateCount(RenderTargetIdentifier dest) => Add(c =>
        {
            if (dest.InstanceID != 0 && Unbound.Runtime.ObjectRegistry.FindById(dest.InstanceID) is Texture texture)
                texture.IncrementUpdateCount();
        });

        public void SetShadowSamplingMode(RenderTargetIdentifier shadowmap, ShadowSamplingMode mode) =>
            Unbound.Runtime.Stubs.Ignored("CommandBuffer.SetShadowSamplingMode", "is not honoured yet: the shadow map keeps its sampling mode");

        /// <summary>Draws a renderer list (made by ScriptableRenderContext.CreateRendererList).</summary>
        public void DrawRendererList(RendererList rendererList) => Add(c => Unbound.Runtime.RendererLists.Draw(rendererList));

        public void SetupCameraProperties(Camera camera) => Add(c => new ScriptableRenderContext().SetupCameraProperties(camera));

        public void InvokeOnRenderObjectCallbacks() => Add(c => Unbound.Runtime.FrameBuilder.InvokeOnRenderObject());

        /// <summary>Several color targets (only the first is drawn into yet) and a depth target.</summary>
        public void SetRenderTarget(RenderTargetBinding binding, int mipLevel, CubemapFace cubemapFace, int depthSlice)
        {
            var colors = binding.colorRenderTargets;
            if (colors == null || colors.Length == 0) SetRenderTarget(binding.depthRenderTarget, mipLevel, cubemapFace, depthSlice);
            else SetRenderTarget(colors[0], binding.depthRenderTarget, mipLevel, cubemapFace, depthSlice);
        }

        public void SetRenderTarget(RenderTargetBinding binding) => SetRenderTarget(binding, 0, CubemapFace.Unknown, 0);

        // ------------------------------------------------------------ render passes

        public void BeginRenderPass(int width, int height, int volumeDepth, int samples, Unity.Collections.NativeArray<AttachmentDescriptor> attachments, int depthAttachmentIndex,
            Unity.Collections.NativeArray<SubPassDescriptor> subPasses, System.ReadOnlySpan<byte> debugNameUtf8)
        {
            // The arrays are the caller's (often temporary): the command keeps copies.
            var a = new Unity.Collections.NativeArray<AttachmentDescriptor>(attachments.ToArray(), Unity.Collections.Allocator.Persistent);
            var s = new Unity.Collections.NativeArray<SubPassDescriptor>(subPasses.ToArray(), Unity.Collections.Allocator.Persistent);
            Add(c =>
            {
                m_SubPasses = s;
                m_SubPass = 0;
                Unbound.Runtime.RenderPasses.Begin(a, depthAttachmentIndex, s, width, height);
            });
            m_OwnedArrays.Add(a);
            m_OwnedSubPasses.Add(s);
        }

        public void BeginRenderPass(int width, int height, int volumeDepth, int samples, Unity.Collections.NativeArray<AttachmentDescriptor> attachments, int depthAttachmentIndex,
            Unity.Collections.NativeArray<SubPassDescriptor> subPasses) =>
            BeginRenderPass(width, height, volumeDepth, samples, attachments, depthAttachmentIndex, subPasses, default);

        public void BeginRenderPass(int width, int height, int samples, Unity.Collections.NativeArray<AttachmentDescriptor> attachments, int depthAttachmentIndex,
            Unity.Collections.NativeArray<SubPassDescriptor> subPasses) =>
            BeginRenderPass(width, height, 1, samples, attachments, depthAttachmentIndex, subPasses, default);

        public void BeginRenderPass(int width, int height, int samples, Unity.Collections.NativeArray<AttachmentDescriptor> attachments, int depthAttachmentIndex,
            Unity.Collections.NativeArray<SubPassDescriptor> subPasses, System.ReadOnlySpan<byte> debugNameUtf8) =>
            BeginRenderPass(width, height, 1, samples, attachments, depthAttachmentIndex, subPasses, default);

        public void NextSubPass() => Add(c => Unbound.Runtime.RenderPasses.Next(m_SubPasses, ref m_SubPass));

        public void EndRenderPass() => Add(c => Unbound.Runtime.RenderPasses.End());

        Unity.Collections.NativeArray<SubPassDescriptor> m_SubPasses;
        int m_SubPass;
        readonly List<Unity.Collections.NativeArray<AttachmentDescriptor>> m_OwnedArrays = new List<Unity.Collections.NativeArray<AttachmentDescriptor>>();
        readonly List<Unity.Collections.NativeArray<SubPassDescriptor>> m_OwnedSubPasses = new List<Unity.Collections.NativeArray<SubPassDescriptor>>();

        /// <summary>Frees the arrays render pass commands keep (on Clear and Dispose).</summary>
        void ReleaseOwnedArrays()
        {
            foreach (var a in m_OwnedArrays) if (a.IsCreated) a.Dispose();
            foreach (var s in m_OwnedSubPasses) if (s.IsCreated) s.Dispose();
            m_OwnedArrays.Clear();
            m_OwnedSubPasses.Clear();
        }
    }
}
