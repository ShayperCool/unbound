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

        public ShaderKeyword(string keywordName)
        {
            this = default;
            if (Compat.ShaderKeywordsMeasured)
            {
                uint index = Shader.GlobalKeywordIndex(keywordName);
                if (index != uint.MaxValue)
                {
                    m_Name = keywordName;
                    m_Index = index;
                    m_IsValid = true;
                    return;
                }
            }
            // Creating an unregistered name through this legacy API is unmeasured. Preserve its
            // prior explicit unsupported/default path, rather than guessing a registration rule.
            Stubs.NotImplemented("UnityEngine.Rendering.ShaderKeyword.ctor");
        }

        public static ShaderKeywordType GetGlobalKeywordType(ShaderKeyword index)
        {
            if (Compat.ShaderKeywordsMeasured && index.m_IsValid && !index.m_IsLocal && !index.m_IsCompute)
                return Array.IndexOf(Compat.BuiltinGlobalKeywords, index.m_Name) >= 0
                    ? ShaderKeywordType.BuiltinDefault : ShaderKeywordType.UserDefined;
            Stubs.NotImplemented("UnityEngine.Rendering.ShaderKeyword.GetGlobalKeywordType");
            return default;
        }
    }
}
