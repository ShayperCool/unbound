using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Shader
    {
        public LocalKeywordSpace keywordSpace => new LocalKeywordSpace(GetInstanceID());

        // A GlobalKeyword that names no global keyword stands for nothing (canonical
        // global.IsKeywordEnabled(GLOBAL_A): enabling it leaves the name disabled).
        public static void EnableKeyword(in GlobalKeyword keyword)
        {
            if (keyword.m_Index != uint.MaxValue) EnableKeyword(keyword.name);
        }

        public static void DisableKeyword(in GlobalKeyword keyword)
        {
            if (keyword.m_Index != uint.MaxValue) DisableKeyword(keyword.name);
        }

        public static void SetKeyword(in GlobalKeyword keyword, bool value)
        {
            if (value) EnableKeyword(keyword);
            else DisableKeyword(keyword);
        }

        public static bool IsKeywordEnabled(in GlobalKeyword keyword) => keyword.m_Index != uint.MaxValue && IsKeywordEnabled(keyword.name);

        /// <summary>The enabled global keywords, in the order the keywords came to exist (the
        /// canonical records them sorted: Unity's registry order is not measured). None is enabled
        /// by this side at startup: which ones the engine enables depends on quality, pipeline and
        /// context.</summary>
        public static GlobalKeyword[] enabledGlobalKeywords
        {
            get
            {
                var list = new List<GlobalKeyword>();
                if (Unbound.Runtime.Compat.BuiltinGlobalKeywords == null)
                {
                    // Not measured: the name-based answer.
                    foreach (var k in s_GlobalKeywords) list.Add(new GlobalKeyword(k));
                    return list.ToArray();
                }
                var names = GlobalKeywordNames();
                foreach (var k in names) if (s_GlobalKeywords.Contains(k)) list.Add(GlobalKeyword.Existing(k));
                foreach (var k in s_GlobalKeywords) if (!names.Contains(k)) list.Add(GlobalKeyword.Existing(k));
                return list.ToArray();
            }
        }

        /// <summary>Every global keyword: the engine's built-in ones, then those made by
        /// <see cref="GlobalKeyword.Create"/> or by enabling a name (canonical global.all.*,
        /// global.create.*). Not measured for the release: the enabled ones, as before.</summary>
        public static GlobalKeyword[] globalKeywords
        {
            get
            {
                if (Unbound.Runtime.Compat.BuiltinGlobalKeywords == null) return enabledGlobalKeywords;
                var names = GlobalKeywordNames();
                var list = new GlobalKeyword[names.Count];
                for (int i = 0; i < list.Length; i++) list[i] = GlobalKeyword.Existing(names[i]);
                return list;
            }
        }
    }

    public partial class Material
    {
        public void EnableKeyword(in LocalKeyword keyword) => SetKeyword(keyword, true);
        public void DisableKeyword(in LocalKeyword keyword) => SetKeyword(keyword, false);

        /// <summary>Sets a keyword of the shader's space (canonical mat.otherSpace: one of another
        /// shader's space logs an error and changes nothing; one the space lacks changes nothing).
        /// While the shader's keywords are not known: by name, as before.</summary>
        public void SetKeyword(in LocalKeyword keyword, bool value)
        {
            if (KeywordDeclarations != null)
            {
                if (keyword.m_SpaceInfo != shader.keywordSpace)
                {
                    Debug.LogError("Local keyword " + keyword.name + " comes from a different shader.");
                    return;
                }
                if (keyword.m_SpaceInfo.IndexOf(keyword.name) < 0) return;
            }
            if (value) EnableKeyword(keyword.name);
            else DisableKeyword(keyword.name);
        }

        public bool IsKeywordEnabled(in LocalKeyword keyword)
        {
            if (KeywordDeclarations == null) return IsKeywordEnabled(keyword.name);
            return keyword.m_SpaceInfo == shader.keywordSpace && keyword.m_SpaceInfo.IndexOf(keyword.name) >= 0 && IsKeywordEnabled(keyword.name);
        }

        /// <summary>The keywords of the shader's space that are on, in the space's order (canonical
        /// mat.*.enabledKeywords); while the space is not known, every name that is on, sorted.</summary>
        public LocalKeyword[] enabledKeywords
        {
            get
            {
                Inherit();
                var list = new List<LocalKeyword>();
                if (shader == null) return list.ToArray();
                var space = shader.keywordSpace;
                var declarations = KeywordDeclarations;
                if (declarations == null)
                {
                    var names = new List<string>(m_Keywords);
                    names.Sort(StringComparer.Ordinal);
                    foreach (var k in names) list.Add(new LocalKeyword(space, k, -1));
                    return list.ToArray();
                }
                for (int i = 0; i < declarations.Keywords.Count; i++)
                    if (m_Keywords.Contains(declarations.Keywords[i].Name)) list.Add(new LocalKeyword(space, declarations.Keywords[i].Name, i));
                return list.ToArray();
            }
            set
            {
                OwnKeywords();
                var declarations = KeywordDeclarations;
                if (declarations == null)
                {
                    m_Keywords.Clear();
                    m_KeywordExtras.Clear();
                    if (value != null) foreach (var k in value) if (!string.IsNullOrEmpty(k.name)) m_Keywords.Add(k.name);
                }
                else
                {
                    // Replaces the declared keywords that are on; names the shader does not declare
                    // stay (canonical mat.setEnabled.shaderKeywords).
                    m_Keywords.RemoveWhere(k => declarations.IndexOf(k) >= 0);
                    if (value != null)
                        foreach (var k in value)
                            if (k.m_SpaceInfo == shader.keywordSpace && k.m_SpaceInfo.IndexOf(k.name) >= 0) m_Keywords.Add(k.name);
                }
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

        /// <summary>The enabled keywords, in the keyword space's order (canonical
        /// compute.enabledKeywords).</summary>
        public LocalKeyword[] enabledKeywords
        {
            get
            {
                var space = keywordSpace;
                var list = new List<LocalKeyword>();
                foreach (var (name, index) in KeywordsInSpaceOrder()) list.Add(new LocalKeyword(space, name, index));
                return list.ToArray();
            }
            set
            {
                var names = new List<string>();
                if (value != null)
                    foreach (var k in value)
                        if (k.m_SpaceInfo == keywordSpace ? k.m_SpaceInfo.Has(k.name) : !string.IsNullOrEmpty(k.name)) names.Add(k.name);
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
