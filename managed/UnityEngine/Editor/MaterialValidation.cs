using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The Editor's validation of the materials it loads: "When the user loads a Material using
    /// this ShaderGUI into memory or changes a value in the Inspector, the Editor calls this
    /// method" (Scripting API 6000.0, <see cref="ShaderGUI.ValidateMaterial"/>), with the shader's
    /// current editor: the one it names for the active render pipeline, else its
    /// <c>CustomEditor</c>, a <see cref="ShaderGUI"/> class whose full name is that name or
    /// <c>UnityEditor.</c> and that name (the Editor's own, which Unbound makes, and those of
    /// render pipelines and projects). Measured (tests/projects/materialvalidation, the Unity
    /// 6000.0.71f1 Editor): the materials of the project's material and prefab files are
    /// validated, those made by scripts and the built-in resources' are not, and by the next frame
    /// a material has what its editor's validation sets (keywords, render queue, tags, values and
    /// GI flags). Unbound validates each material once, when its file has loaded; the Editor
    /// validates it again before the next frame, and a material loaded in Play mode still reads
    /// its saved keywords, render queue, tags and GI flags until then. The materials a model's
    /// import makes are not validated on load (tests/projects/modelimport: emissive Standard ones
    /// keep GI flags the Standard editor would change); render pipelines' and projects' editors
    /// validate them as the model imports (<see cref="ModelMaterialImport"/>: URP's model
    /// materials get their blend states, queue control and GI flags, as its import postprocessing
    /// gives them).
    /// </summary>
    internal static class MaterialValidation
    {
        static readonly Dictionary<string, Type> s_Editors = new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>Validates every material of a loaded project file.</summary>
        internal static void ValidateLoaded(LoadedFile file)
        {
            if (file == null) return;
            HashSet<Material> seen = null;
            foreach (var obj in file.Objects.Values)
                if (obj is Material material && (seen ??= new HashSet<Material>()).Add(material))
                    Validate(material);
        }

        /// <param name="editorsOfTheEditor">Whether the Editor's own shader editors (Unbound's
        /// UnityEditor) validate too, or only those of render pipelines and projects.</param>
        internal static void Validate(Material material, bool editorsOfTheEditor = true)
        {
            var shader = material.shader;
            if (shader == null) return;
            var type = EditorType(CurrentEditor(shader));
            if (type == null || (!editorsOfTheEditor && type.Assembly == typeof(ShaderGUI).Assembly)) return;
            var method = type.GetMethod(nameof(ShaderGUI.ValidateMaterial), BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(Material) }, null);
            // An editor without its own validation validates nothing (ShaderGUI's does nothing).
            if (method == null || method.DeclaringType == typeof(ShaderGUI)) return;
            if (method.IsDefined(typeof(StubAttribute), false))
            {
                Stubs.Ignored(type.FullName + ".ValidateMaterial",
                    "is not implemented: materials of its shaders keep their saved keywords, render queue and tags when they load");
                return;
            }
            try
            {
                var editor = (ShaderGUI)Activator.CreateInstance(type, nonPublic: true);
                editor.ValidateMaterial(material);
            }
            catch (Exception e)
            {
                Debug.LogException(e is TargetInvocationException t ? t.InnerException ?? e : e);
            }
        }

        /// <summary>The shader's editor for the active render pipeline, else its own.</summary>
        static string CurrentEditor(Shader shader)
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
                foreach (var (editor, pipelineType) in shader.m_PipelineEditors)
                    if (pipeline.GetType().FullName == pipelineType || pipeline.GetType().Name == pipelineType)
                        return editor;
            return shader.m_CustomEditor;
        }

        static Type EditorType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_Editors.TryGetValue(name, out var cached)) return cached;
            var prefixed = "UnityEditor." + name;
            Type found = Find(typeof(ShaderGUI).Assembly, name, prefixed);
            if (found == null)
                foreach (var assembly in GameDomain.Assemblies())
                    if (assembly != typeof(ShaderGUI).Assembly && (found = Find(assembly, name, prefixed)) != null)
                        break;
            return s_Editors[name] = found;
        }

        static Type Find(Assembly assembly, string name, string prefixed)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            foreach (var type in types)
                if (!type.IsAbstract && typeof(ShaderGUI).IsAssignableFrom(type) && (type.FullName == name || type.FullName == prefixed))
                    return type;
            return null;
        }
    }
}
