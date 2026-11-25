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
    /// render pipelines and projects). The Unity 6000.0.71f1 Editor validates a loaded material
    /// asset when it is first used, not as it loads (<see cref="PendingValidation"/>); materials a
    /// model's import makes with a built-in shader editor wait for their first use too, and
    /// render pipelines' and projects' editors validate those as the model imports
    /// (<see cref="ModelMaterialImport"/>). Unsupported editors report on first use. Validation of
    /// newly scripted materials and of built-in resources is separate.
    /// </summary>
    internal static class MaterialValidation
    {
        static readonly Dictionary<string, Type> s_Editors = new Dictionary<string, Type>(StringComparer.Ordinal);

        internal static void DeferImportedEditor(Material material)
        {
            if (Compat.DeferredMaterialValidationMeasured && material != null)
                material.PendingEditorValidation = UsesBuiltinEditor(material.shader) ? PendingValidation.Imported : PendingValidation.None;
        }

        internal static bool UsesBuiltinEditor(Shader shader) =>
            SelectedEditor(shader)?.Assembly == typeof(ShaderGUI).Assembly;

        /// <summary>Schedules the first-use validation of the materials in a loaded project file.
        /// A release whose timing is unmeasured validates them as they load, leaving them clean.</summary>
        internal static void ValidateLoaded(LoadedFile file)
        {
            if (file == null) return;
            HashSet<Material> seen = null;
            foreach (var obj in file.Objects.Values)
            {
                if (obj is not Material material || !(seen ??= new HashSet<Material>()).Add(material)) continue;
                if (Compat.DeferredMaterialValidationMeasured)
                    material.PendingEditorValidation = PendingValidation.Loaded;
                else
                    Validate(material, keepClean: true);
            }
        }

        /// <summary>
        /// Play mode starts in an Editor that has drawn its views (as for the pipeline's shader tag,
        /// <see cref="UnityEngine.Rendering.RenderPipelineManager.ActivateConfiguredPipelineTag"/>):
        /// an Edit mode render validates the materials it draws, which a Play mode draw does not, so
        /// the materials the startup scene's renderers draw were validated before Play mode (matches
        /// the Unity 6000.0.71f1 Editor, tests/projects/graphqueue scene.start;
        /// tests/projects/urpwrites played.LitStaleDrawn). Which renderers a real Editor's views
        /// reach is unmeasured (Unbound: every active, enabled renderer of the scene), as are other
        /// drawn materials (skybox, UI, terrain).
        /// </summary>
        internal static void ValidateDrawnBeforePlay(SceneData scene)
        {
            if (!Compat.DeferredMaterialValidationMeasured || scene == null) return;
            foreach (var root in scene.Roots.ToArray())
            {
                if (root == null) continue;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled || !ActiveInScene(renderer.transform)) continue;
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null) material.ValidateDeferredEditor();
                }
            }
        }

        /// <summary>Whether the object and its parents are active as saved (before the scene's
        /// objects wake, activeInHierarchy is still false).</summary>
        static bool ActiveInScene(Transform transform)
        {
            for (var t = transform; t != null; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        /// <param name="editorsOfTheEditor">Whether the Editor's own shader editors (Unbound's
        /// UnityEditor) validate too, or only those of render pipelines and projects.</param>
        /// <param name="keepClean">The validation of a material as it loads, for a release whose
        /// timing is unmeasured: its changes leave a clean asset clean. A validation on first use
        /// that changes the material dirties it (tests/projects/urpwrites access.StandardStale.dirty,
        /// read.LitStaleRead.dirty), as do the changes that cause one.</param>
        internal static void Validate(Material material, bool editorsOfTheEditor = true, bool keepClean = false)
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
            bool wasDirty = EditorUtility.IsDirty(material);
            int dirtyCount = EditorUtility.GetDirtyCount(material);
            // A validation that leaves the material's data as it was leaves a clean asset clean
            // (tests/projects/urpwrites read.LitCleanRead.dirty).
            string before = wasDirty || keepClean ? null : MaterialTypeTree.Snapshot(material);
            try
            {
                var editor = (ShaderGUI)Activator.CreateInstance(type, nonPublic: true);
                editor.ValidateMaterial(material);
            }
            catch (Exception e)
            {
                Debug.LogException(e is TargetInvocationException t ? t.InnerException ?? e : e);
            }
            finally
            {
                if (!wasDirty && (keepClean || before == MaterialTypeTree.Snapshot(material)))
                    EditorUtility.RestoreClean(material, dirtyCount);
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

    /// <summary>A validation by its shader's editor that a material is owed (the Unity 6000.0.71f1
    /// Editor's first-use validation). A float written by the access that validates is seen by the
    /// validation; the material then reads and draws with the value written while its data holds
    /// the validation's (<see cref="Material.SheetFloat"/>).</summary>
    internal enum PendingValidation : byte
    {
        None,
        /// <summary>A material a model's import made, with a built-in shader editor: its first
        /// property access, SetPass or draw validates it (tests/projects/modelimport BuiltinBlend).</summary>
        Imported,
        /// <summary>A material asset loaded from its file, with any editor: its first property value
        /// access through the Material API (Get*, Set*, Has*, HasProperty, <c>color</c>,
        /// <c>mainTexture</c>), SetPass or a shader set validates it; reading its keywords, render
        /// queue, tags or passes, changing keywords or passes, copies and drawing do not
        /// (tests/projects/urpwrites with URP, tests/projects/materialvalidation Later* with the
        /// built-in pipeline). The Editor can validate it again at a later access
        /// (tests/projects/materialvalidation ProjectGui._Value); Unbound validates it once.</summary>
        Loaded,
    }
}
