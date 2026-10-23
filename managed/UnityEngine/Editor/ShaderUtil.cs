using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityEditor
{
    public sealed partial class ShaderUtil
    {
        /// <summary>A shader from ShaderLab source, as a .shader asset would import: its declared
        /// name, render queue and properties (Unbound does not compile ShaderLab).</summary>
        public static Shader CreateShaderAsset(string source) => CreateShaderAsset(source, true);

        public static Shader CreateShaderAsset(string source, bool compileInitialShaderVariants)
        {
            if (source == null) throw new System.ArgumentNullException(nameof(source));
            var shader = Shader.Create(ShaderLab.Name(source) ?? "", ShaderLab.Queue(source));
            Fill(shader, source);
            return shader;
        }

        public static Shader CreateShaderAsset(AssetImportContext context, string source, bool compileInitialShaderVariants) =>
            CreateShaderAsset(source, compileInitialShaderVariants);

        public static void UpdateShaderAsset(Shader shader, string source) => UpdateShaderAsset(shader, source, true);

        public static void UpdateShaderAsset(Shader shader, string source, bool compileInitialShaderVariants)
        {
            if (shader == null) throw new System.ArgumentNullException(nameof(shader));
            if (source == null) throw new System.ArgumentNullException(nameof(source));
            shader.m_Name = ShaderLab.Name(source) ?? shader.m_Name;
            shader.m_RenderQueue = ShaderLab.Queue(source);
            Fill(shader, source);
        }

        public static void UpdateShaderAsset(AssetImportContext context, Shader shader, string source, bool compileInitialShaderVariants) =>
            UpdateShaderAsset(shader, source, compileInitialShaderVariants);

        static void Fill(Shader shader, string source)
        {
            shader.m_Properties.Clear();
            shader.m_Properties.AddRange(ShaderLab.Properties(source));
            Shader.Register(shader);
        }
    }
}
