using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

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

        /// <summary>A shader an importer generates (Shader Graph): as from source, and its ShaderLab
        /// kept in <c>Library/Unbound/GeneratedShaders</c> (includes made project paths), where the
        /// renderer finds it by name and compiles it like a <c>.shader</c> asset.</summary>
        public static Shader CreateShaderAsset(AssetImportContext context, string source, bool compileInitialShaderVariants)
        {
            if (source == null) throw new System.ArgumentNullException(nameof(source));
            GeneratedShaders.Write(context?.assetPath ?? "", source, ".shader");
            return CreateShaderAsset(source, compileInitialShaderVariants);
        }

        public static void UpdateShaderAsset(Shader shader, string source) => UpdateShaderAsset(shader, source, true);

        public static void UpdateShaderAsset(Shader shader, string source, bool compileInitialShaderVariants)
        {
            if (shader == null) throw new System.ArgumentNullException(nameof(shader));
            if (source == null) throw new System.ArgumentNullException(nameof(source));
            shader.m_Name = ShaderLab.Name(source) ?? shader.m_Name;
            shader.m_RenderQueue = ShaderLab.Queue(source);
            Fill(shader, source);
        }

        public static void UpdateShaderAsset(AssetImportContext context, Shader shader, string source, bool compileInitialShaderVariants)
        {
            if (source != null) GeneratedShaders.Write(context?.assetPath ?? "", source, ".shader");
            UpdateShaderAsset(shader, source, compileInitialShaderVariants);
        }

        static void Fill(Shader shader, string source)
        {
            shader.m_Properties.Clear();
            shader.m_Properties.AddRange(ShaderLab.Properties(source));
            shader.PropertiesChanged();
            Shader.Register(shader);
        }

        /// <summary>
        /// A compute shader from source text, as an importer makes one (Shader Graph, the unified
        /// ray tracing shaders): the source is kept in <c>Library/Unbound/GeneratedShaders</c>, its
        /// includes relative to the importing asset made project paths, and its kernels compile from
        /// there like a <c>.compute</c> asset's.
        /// </summary>
        public static ComputeShader CreateComputeShaderAsset(AssetImportContext context, string source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var assetPath = context?.assetPath ?? "";
            var file = GeneratedShaders.Write(assetPath, source, ".compute");
            var name = assetPath.Length > 0 ? Path.GetFileNameWithoutExtension(assetPath) : "ComputeShader";
            return ComputeShader.FromFile(file, name, File.ReadAllText(file));
        }

        /// <summary>A ray tracing shader from source. The OpenGLCore device has no ray tracing
        /// (<see cref="SystemInfo.supportsRayTracingShaders"/> is false), so it exists to be
        /// referenced and is never dispatched.</summary>
        public static RayTracingShader CreateRayTracingShaderAsset(AssetImportContext context, string source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var assetPath = context?.assetPath ?? "";
            GeneratedShaders.Write(assetPath, source, ".raytrace");
            return RayTracingShader.Create(assetPath.Length > 0 ? Path.GetFileNameWithoutExtension(assetPath) : "RayTracingShader");
        }

        const string NoMessages = "is not known: shaders compile when they first draw, so no compiler messages are reported";

        // Shaders compile when they first draw, so an import knows of no compiler messages (a
        // shader the Editor compiles without any, as the Standard shader, answers the same:
        // tests/projects/editor shader.messages).

        public static int GetShaderMessageCount(Shader s)
        {
            if (!ScriptedImports.Importing) Stubs.Ignored("ShaderUtil.GetShaderMessageCount", NoMessages);
            return 0;
        }

        public static ShaderMessage[] GetShaderMessages(Shader s)
        {
            if (!ScriptedImports.Importing) Stubs.Ignored("ShaderUtil.GetShaderMessages", NoMessages);
            return Array.Empty<ShaderMessage>();
        }

        public static ShaderMessage[] GetShaderMessages(Shader s, ShaderCompilerPlatform platform)
        {
            if (!ScriptedImports.Importing) Stubs.Ignored("ShaderUtil.GetShaderMessages", NoMessages);
            return Array.Empty<ShaderMessage>();
        }

        [Unbound.EditorFaithful("no compiler messages are kept, so there are none to clear")]
        public static void ClearShaderMessages(Shader s) { }

        public static int GetComputeShaderMessageCount(ComputeShader s)
        {
            Stubs.Ignored("ShaderUtil.GetComputeShaderMessageCount", NoMessages);
            return 0;
        }

        public static ShaderMessage[] GetComputeShaderMessages(ComputeShader s)
        {
            Stubs.Ignored("ShaderUtil.GetComputeShaderMessages", NoMessages);
            return Array.Empty<ShaderMessage>();
        }

        public static int GetRayTracingShaderMessageCount(RayTracingShader s)
        {
            Stubs.Ignored("ShaderUtil.GetRayTracingShaderMessageCount", NoMessages);
            return 0;
        }

        public static ShaderMessage[] GetRayTracingShaderMessages(RayTracingShader s)
        {
            Stubs.Ignored("ShaderUtil.GetRayTracingShaderMessages", NoMessages);
            return Array.Empty<ShaderMessage>();
        }

        public static bool ShaderHasError(Shader shader)
        {
            if (!ScriptedImports.Importing) Stubs.Ignored("ShaderUtil.ShaderHasError", NoMessages);
            return false;
        }

        public static bool ShaderHasWarnings(Shader shader)
        {
            if (!ScriptedImports.Importing) Stubs.Ignored("ShaderUtil.ShaderHasWarnings", NoMessages);
            return false;
        }

        [Unbound.EditorFaithful("no compiler errors are kept, so there are none to clear")]
        public static void ClearShaderErrors(Shader s) { }
    }

    /// <summary>Default textures of a shader's texture properties, which materials made with it
    /// start with (Shader Graph sets them from its graph).</summary>
    public sealed partial class EditorMaterialUtility
    {
        public static void SetShaderDefaults(Shader shader, string[] name, Texture[] textures) => Set(shader, name, textures, false);

        public static void SetShaderNonModifiableDefaults(Shader shader, string[] name, Texture[] textures) => Set(shader, name, textures, true);

        static void Set(Shader shader, string[] names, Texture[] textures, bool nonModifiable)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (names == null || textures == null) return;
            if (names.Length != textures.Length) throw new ArgumentException("The names and textures arrays must have the same length.");
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i])) continue;
                shader.m_DefaultTextures[names[i]] = textures[i];
                if (nonModifiable) shader.m_NonModifiableTextures[names[i]] = textures[i];
            }
        }
    }
}

namespace UnityEngine.Rendering
{
    public sealed partial class RayTracingShader : Object
    {
        RayTracingShader() { }

        internal static RayTracingShader Create(string name)
        {
            using (ObjectRegistry.AsAssets())
                return new RayTracingShader { m_Name = name, IsPersistentAsset = true };
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>Shader sources importers generate, written where the shader compiler reads files:
    /// <c>Library/Unbound/GeneratedShaders/&lt;hash&gt;&lt;extension&gt;</c>.</summary>
    internal static class GeneratedShaders
    {
        static readonly Regex s_Include = new Regex(@"^([ \t]*#[ \t]*include(?:_with_pragmas)?[ \t]+"")([^""]+)("")", RegexOptions.Multiline);

        internal static string Write(string assetPath, string source, string extension)
        {
            var text = AnchorIncludes(assetPath, source);
            string hash;
            using (var sha = SHA1.Create())
                hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(assetPath + "\n" + text))).ToLowerInvariant();
            var dir = Path.Combine(UnityEngine.ProjectInfo.ProjectPath, "Library", "Unbound", "GeneratedShaders");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, hash + extension);
            if (!File.Exists(file)) File.WriteAllText(file, text);
            return file;
        }

        /// <summary>Includes the asset names relative to its own folder, made project paths
        /// (<c>Assets/...</c>, <c>Packages/...</c>): the generated file lives elsewhere.</summary>
        static string AnchorIncludes(string assetPath, string source)
        {
            var folder = Path.GetDirectoryName(assetPath.Replace('\\', '/'))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) return source;
            return s_Include.Replace(source, m =>
            {
                var name = m.Groups[2].Value.Replace('\\', '/');
                if (name.StartsWith("Assets/", StringComparison.Ordinal) || name.StartsWith("Packages/", StringComparison.Ordinal) || Path.IsPathRooted(name))
                    return m.Value;
                var candidate = Normalize(folder + "/" + name);
                return File.Exists(UnityEditor.FileUtil.GetPhysicalPath(candidate)) || File.Exists(Path.Combine(UnityEngine.ProjectInfo.ProjectPath, candidate))
                    ? m.Groups[1].Value + candidate + m.Groups[3].Value
                    : m.Value;
            });
        }

        static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Split('/'))
            {
                if (part == "." || part.Length == 0) continue;
                if (part == ".." && parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                else parts.Add(part);
            }
            return string.Join("/", parts);
        }
    }
}
