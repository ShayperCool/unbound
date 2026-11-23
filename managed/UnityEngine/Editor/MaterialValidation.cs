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
    /// render pipelines and projects). With a built-in shader editor, description-imported
    /// materials and loaded material assets keep their saved metadata until a property access
    /// or draw validates them. The Unity 6000.0.71f1 Editor's metadata-only controls retain
    /// their saved values through three updates in Play mode (tests/projects/modelimport,
    /// BuiltinBlend). Unsupported editors report on first use. Render pipelines' and
    /// projects' editors validate as the model imports (<see cref="ModelMaterialImport"/>).
    /// Loaded assets with project editors validate when Unbound loads them. The Editor
    /// can also validate them again after a script accesses them (tests/projects/materialvalidation).
    /// Validation of newly scripted materials and of built-in resources is separate.
    /// </summary>
    internal static class MaterialValidation
    {
        static readonly Dictionary<string, Type> s_Editors = new Dictionary<string, Type>(StringComparer.Ordinal);

        internal static void DeferImportedEditor(Material material)
        {
            if (Compat.DeferredBuiltinMaterialValidationMeasured && material != null)
                material.PendingEditorValidation = UsesBuiltinEditor(material.shader);
        }

        internal static bool UsesBuiltinEditor(Shader shader) =>
            SelectedEditor(shader)?.Assembly == typeof(ShaderGUI).Assembly;

        /// <summary>Validates materials in a loaded project file or schedules their first-use validation.</summary>
        internal static void ValidateLoaded(LoadedFile file)
        {
            if (file == null) return;
            HashSet<Material> seen = null;
            foreach (var obj in file.Objects.Values)
            {
                if (obj is not Material material || !(seen ??= new HashSet<Material>()).Add(material)) continue;
                if (Compat.DeferredBuiltinMaterialValidationMeasured && UsesBuiltinEditor(material.shader))
                    material.PendingEditorValidation = true;
                else
                    Validate(material);
            }
        }

        /// <param name="editorsOfTheEditor">Whether the Editor's own shader editors (Unbound's
        /// UnityEditor) validate too, or only those of render pipelines and projects.</param>
        internal static void Validate(Material material, bool editorsOfTheEditor = true)
        {
            var type = SelectedEditor(material.shader);
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

        static Type SelectedEditor(Shader shader) => shader == null ? null : EditorType(CurrentEditor(shader));

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
