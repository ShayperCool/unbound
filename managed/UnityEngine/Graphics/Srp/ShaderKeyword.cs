using System;
using Unbound.Runtime;

namespace UnityEngine.Rendering
{
    /// <summary>The measured legacy global-keyword constructor and type query. Local/compute and
    /// other legacy operations remain generated stubs; this is not a complete legacy API.</summary>
    public partial struct ShaderKeyword
    {
        // Same fields, types and order as the generated declaration, moved here so no-stubs
        // projection can compile these two members without changing the struct's layout.
        internal string m_Name;
        internal uint m_Index;
        internal bool m_IsLocal;
        internal bool m_IsCompute;
        internal bool m_IsValid;

        /// <summary>The global keyword of the name, made when there is none, and valid either
        /// way (the release's constructor; tests/projects/srpmembers keyword.global, keyword.valid).</summary>
        public ShaderKeyword(string keywordName)
        {
            this = default;
            if (Compat.ShaderKeywordsMeasured)
            {
                m_Name = keywordName;
                m_Index = Shader.RegisterGlobalKeyword(keywordName);
                m_IsValid = true;
                return;
            }
            // Other releases keep their unmeasured registry: only an existing global keyword.
            uint index = Shader.GlobalKeywordIndex(keywordName);
            if (index != uint.MaxValue)
            {
                m_Name = keywordName;
                m_Index = index;
                m_IsValid = true;
                return;
            }
            Stubs.NotImplemented("UnityEngine.Rendering.ShaderKeyword.ctor");
        }

        /// <summary>A keyword of the shader's own keyword space, valid when the shader declares
        /// it (the release's constructor; srpmembers keyword.shaderLocal).</summary>
        public ShaderKeyword(Shader shader, string keywordName)
        {
            this = default;
            m_Name = keywordName;
            int index = shader.keywordSpace.IndexOf(keywordName);
            m_Index = index < 0 ? uint.MaxValue : (uint)index;
            m_IsValid = index >= 0;
            m_IsLocal = true;
        }

        public string name => m_Name;

        public bool IsValid() => m_IsValid;

        public static ShaderKeywordType GetGlobalKeywordType(ShaderKeyword index)
        {
            if (Compat.ShaderKeywordsMeasured && index.m_IsValid && !index.m_IsLocal && !index.m_IsCompute)
                return Array.IndexOf(Compat.BuiltinGlobalKeywords, index.m_Name) >= 0
                    ? ShaderKeywordType.BuiltinDefault : ShaderKeywordType.UserDefined;
            Stubs.NotImplemented("UnityEngine.Rendering.ShaderKeyword.GetGlobalKeywordType");
            return default;
        }
    }

    /// <summary>
    /// A keyword set made with <c>new</c> has no keyword state: every operation logs the Editor's
    /// two errors and changes nothing, and no keyword reads as enabled (the Unity 6000.0.71f1
    /// Editor at runtime, tests/projects/srpmembers keyword.set, keyword.setBuiltin,
    /// log.keywords, measured through the ShaderKeyword overloads; the GlobalKeyword and
    /// LocalKeyword IsEnabled overloads follow the same rule, unmeasured). Sets with state come
    /// from shader build callbacks, which Unbound does not run.
    /// </summary>
    public partial struct ShaderKeywordSet
    {
        // The release's fields (its layout); the state stays null in a set made with new.
#pragma warning disable CS0169
        IntPtr m_KeywordState;
        IntPtr m_Shader;
        IntPtr m_ComputeShader;
        ulong m_StateIndex;
#pragma warning restore CS0169

        static void Unsupported()
        {
            UnityEngine.Debug.LogError("ShaderKeywordSet cannot be used outside of IPreprocessShaders or IPreprocessComputeShaders.");
            UnityEngine.Debug.LogError("ShaderKeywordSet has been created manually. This is not supported. Operations on this object will have no effect.");
        }

        public bool IsEnabled(ShaderKeyword keyword)
        {
            Unsupported();
            return false;
        }

        public bool IsEnabled(GlobalKeyword keyword)
        {
            Unsupported();
            return false;
        }

        public bool IsEnabled(LocalKeyword keyword)
        {
            Unsupported();
            return false;
        }

        public void Enable(ShaderKeyword keyword) => Unsupported();

        public void Disable(ShaderKeyword keyword) => Unsupported();

        public ShaderKeyword[] GetShaderKeywords()
        {
            Unsupported();
            return Array.Empty<ShaderKeyword>();
        }
    }
}
