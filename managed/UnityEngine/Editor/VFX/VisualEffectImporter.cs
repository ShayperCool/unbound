using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace UnityEditor.VFX
{
    /// <summary>The importer of Visual Effect Graph files; its .meta keeps what the graph shows
    /// in the template window.</summary>
    internal sealed partial class VisualEffectImporter : AssetImporter
    {
        string m_TemplateName = "", m_TemplateCategory = "", m_TemplateDescription = "";
        ObjectRef m_TemplateIcon, m_TemplateThumbnail;
        Texture2D m_Icon, m_Thumbnail;
        bool m_TexturesResolved;

        public VisualEffectImporter() { }

        internal override void ReadSettings(YNode body)
        {
            var template = body.Get("template");
            if (template == null) return;
            m_TemplateName = template.Str("name");
            m_TemplateCategory = template.Str("category");
            m_TemplateDescription = template.Str("description");
            m_TemplateIcon = template.Ref("icon");
            m_TemplateThumbnail = template.Ref("thumbnail");
        }

        public VFXTemplate templateProperty
        {
            get
            {
                CheckImporterState("get_templateProperty");
                if (!m_TexturesResolved)
                {
                    // The .meta's textures load when first asked for, not with every importer.
                    m_TexturesResolved = true;
                    m_Icon = Load(m_TemplateIcon);
                    m_Thumbnail = Load(m_TemplateThumbnail);
                }
                return new VFXTemplate { name = m_TemplateName, category = m_TemplateCategory, description = m_TemplateDescription, icon = m_Icon, thumbnail = m_Thumbnail };
            }
            set
            {
                CheckImporterState("set_templateProperty");
                m_TemplateName = value.name ?? "";
                m_TemplateCategory = value.category ?? "";
                m_TemplateDescription = value.description ?? "";
                m_Icon = value.icon;
                m_Thumbnail = value.thumbnail;
                m_TexturesResolved = true;
            }
        }

        static Texture2D Load(ObjectRef reference) =>
            reference.IsNull || string.IsNullOrEmpty(reference.Guid) ? null : AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, typeof(Texture2D)) as Texture2D;
    }
}

namespace Unbound.Runtime
{
    using UnityEditor;
    using UnityEditor.VFX;

    /// <summary>
    /// Imports Visual Effect Graph files (<c>.vfx</c>, and the subgraphs <c>.vfxoperator</c> and
    /// <c>.vfxblock</c>) by running the graph package's own compiler, as the Editor does:
    /// <list type="number">
    /// <item>the file's objects load (the resource and the package's graph models) and the asset
    /// object is made: a <see cref="VisualEffectAsset"/>, or a subgraph object by extension, under
    /// the file id every graph's asset has; a subgraph's import ends here;</item>
    /// <item>the package compiles the graph into the resource
    /// (<see cref="VisualEffectResource.SetRuntimeData"/>);</item>
    /// <item>each compiled shader source becomes a <see cref="Shader"/> or a
    /// <see cref="ComputeShader"/>, compiled like any other from
    /// <c>Library/Unbound/GeneratedShaders</c>;</item>
    /// <item>the asset takes the resource's settings and systems, each task with its processor: the
    /// object of its shader source, else its own object;</item>
    /// <item>each output task's shader gets a material (one per shader, set up by the package from
    /// the task's model), which becomes the task's processor.</item>
    /// </list>
    /// A graph loaded from disk is not sanitized yet, and the package compiles nothing then: it
    /// sanitizes the graph when told the asset was imported and forces a second import, which
    /// compiles. Unbound runs no post-import pass for other assets; here it tells the postprocessors
    /// of the package that registered the compile hook, and only those.
    /// Matches the Unity 6000.0.71f1 Editor (tests/projects/vfx).
    /// </summary>
    internal static class VisualEffectImports
    {
        /// <summary>The file id of the asset object in every graph file.</summary>
        internal const long AssetFileId = 8926484042661614526;

        /// <summary>The compiled-graph version from which output tasks draw with materials
        /// instead of their shaders.</summary>
        const uint MaterialsSinceVersion = 3;

        static bool s_WarnedNoPackage, s_WarnedVariantLock;

        internal static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info, Action<LoadedFile> publish)
        {
            var documents = Native.LoadDocuments(info.Guid);
            VisualEffectObject main = null;
            var file = ObjectGraphBuilder.Build(documents, info.Path, info.Guid, ObjectGraphBuilder.Kind.Asset, null, created =>
            {
                using (ObjectRegistry.AsAssets()) main = CreateObject(info.Path);
                created.Objects[AssetFileId] = main;
                created.MainObject = main;
                // The file's objects exist: the graph's models find their asset while they load.
                publish(created);
            });
            file.MainObject = main;
            var resource = FindResource(file);
            if (resource == null)
            {
                Debug.LogWarning($"Unbound: {info.Path} holds no VisualEffectResource; its asset has no compiled graph.");
                return file;
            }
            var contents = new List<Object>();
            foreach (var o in file.Objects.Values)
                if (!ReferenceEquals(o, resource) && !ReferenceEquals(o, main)) contents.Add(o);
            resource.m_Contents = contents.ToArray();
            resource.m_Object = main;
            if (main is VisualEffectAsset asset) Build(resource, asset, file);
            NotifyImported(info.Path);
            return file;
        }

        /// <summary>The asset object of a graph file, by its extension.</summary>
        static VisualEffectObject CreateObject(string path)
        {
            VisualEffectObject main;
            if (path.EndsWith(VisualEffectSubgraphOperator.Extension, StringComparison.OrdinalIgnoreCase)) main = new VisualEffectSubgraphOperator();
            else if (path.EndsWith(VisualEffectSubgraphBlock.Extension, StringComparison.OrdinalIgnoreCase)) main = new VisualEffectSubgraphBlock();
            else main = new VisualEffectAsset();
            main.m_Name = Path.GetFileNameWithoutExtension(path);
            main.IsPersistentAsset = true;
            return main;
        }

        static VisualEffectResource FindResource(LoadedFile file)
        {
            foreach (var o in file.Objects.Values)
                if (o is VisualEffectResource resource) return resource;
            return null;
        }

        /// <summary>The resource of the graph file at a path; null for another asset.</summary>
        internal static VisualEffectResource ResourceAtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || !IsGraphPath(path)) return null;
            var file = AssetDatabaseRuntime.LoadFile(guid);
            return file != null ? FindResource(file) : null;
        }

        internal static bool IsGraphPath(string path) =>
            path.EndsWith(VisualEffectResource.Extension, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(VisualEffectSubgraphOperator.Extension, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(VisualEffectSubgraphBlock.Extension, StringComparison.OrdinalIgnoreCase);

        /// <summary>A forced re-import of a loaded graph file (<c>AssetDatabase.ImportAsset</c>
        /// with <c>ForceUpdate</c>): the graph compiles again into the same asset object. False
        /// for a path that is not a loaded graph (it compiles when it loads).</summary>
        internal static bool Reimport(string path)
        {
            if (string.IsNullOrEmpty(path) || !IsGraphPath(path)) return false;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || !AssetDatabaseRuntime.IsMainLoaded(guid)) return false;
            var file = AssetDatabaseRuntime.LoadFile(guid);
            var resource = file != null ? FindResource(file) : null;
            if (resource == null) return false;
            if (resource.m_Object is VisualEffectAsset asset)
            {
                Build(resource, asset, file);
                AssetDatabaseRuntime.RefreshObjects(file);
            }
            return true;
        }

        /// <summary>Compiles the resource's graph and gives the asset the result: steps 2 to 5 of
        /// the import.</summary>
        static void Build(VisualEffectResource resource, VisualEffectAsset asset, LoadedFile file)
        {
            if (VisualEffectResource.onCompileResource == null)
            {
                if (!s_WarnedNoPackage)
                {
                    s_WarnedNoPackage = true;
                    Debug.LogWarning($"Unbound: the Visual Effect Graph package registered no compiler (com.unity.visualeffectgraph is not among the project's packages or its editor assembly did not load): {file.Path} and the project's other graphs have no compiled systems.");
                }
            }
            else
            {
                using (ScriptedImports.Scope())
                using (ObjectRegistry.AsAssets())
                {
                    try { resource.CompileResource(); }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        resource.ClearRuntimeData();
                    }
                }
            }

            foreach (var previous in asset.m_Imported)
            {
                if (previous == null) continue;
                RemoveObject(file, previous);
                Object.DestroyImmediate(previous, true);
            }

            var sources = resource.shaderSources;
            var shaders = new Object[sources.Length];
            var imported = new List<Object>();
            using (ScriptedImports.Scope())
            using (ObjectRegistry.AsAssets())
            {
                var context = new AssetImportContext(file.Path);
                for (int i = 0; i < sources.Length; i++)
                {
                    try
                    {
                        if (sources[i].compute)
                        {
                            var generated = GeneratedShaders.Write(file.Path, sources[i].source ?? "", ".compute");
                            shaders[i] = ComputeShader.FromFile(generated, sources[i].name, File.ReadAllText(generated));
                        }
                        else
                        {
                            shaders[i] = ShaderUtil.CreateShaderAsset(context, sources[i].source ?? "", resource.compileInitialVariants);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Unbound: {file.Path}: the generated shader '{sources[i].name}' cannot be imported: {e.Message}");
                        continue;
                    }
                    shaders[i].IsPersistentAsset = true;
                    AddObject(file, sources[i].name, shaders[i]);
                    imported.Add(shaders[i]);
                }

                var compiled = resource.m_Data;
                var data = new VisualEffectCompiledData
                {
                    Expressions = compiled.Expressions,
                    ExpressionsPerSpawnEventAttribute = compiled.ExpressionsPerSpawnEventAttribute,
                    Values = compiled.Values,
                    Exposed = compiled.Exposed,
                    Systems = compiled.Systems,
                    Events = compiled.Events,
                    GpuBuffers = compiled.GpuBuffers,
                    CpuBuffers = compiled.CpuBuffers,
                    TemporaryBuffers = compiled.TemporaryBuffers,
                    ShadowCastingMode = compiled.ShadowCastingMode,
                    MotionVectorGenerationMode = compiled.MotionVectorGenerationMode,
                    InstancingDisabledReason = compiled.InstancingDisabledReason,
                    CompiledVersion = compiled.CompiledVersion,
                    Processors = new Object[compiled.Systems.Length][],
                };
                var sourceIndices = new int[compiled.Systems.Length][];
                for (int s = 0; s < compiled.Systems.Length; s++)
                {
                    var tasks = compiled.Systems[s].tasks ?? Array.Empty<VFXEditorTaskDesc>();
                    data.Processors[s] = new Object[tasks.Length];
                    sourceIndices[s] = new int[tasks.Length];
                    for (int t = 0; t < tasks.Length; t++)
                    {
                        int index = tasks[t].shaderSourceIndex;
                        sourceIndices[s][t] = index;
                        data.Processors[s][t] = index >= 0 ? (index < shaders.Length ? shaders[index] : null) : tasks[t].externalProcessor;
                    }
                }

                if (compiled.CompiledVersion >= MaterialsSinceVersion)
                {
                    bool instancingAllowed = resource.m_Settings.InstancingMode != VFXInstancingMode.Disabled;
                    var materials = new Dictionary<Shader, Material>();
                    for (int s = 0; s < compiled.Systems.Length; s++)
                    {
                        var tasks = compiled.Systems[s].tasks ?? Array.Empty<VFXEditorTaskDesc>();
                        bool instanced = instancingAllowed && (compiled.Systems[s].flags & VFXSystemFlag.SystemUsesInstancedRendering) != 0;
                        for (int t = 0; t < tasks.Length; t++)
                        {
                            if (!(data.Processors[s][t] is Shader shader)) continue;
                            if (!materials.TryGetValue(shader, out var material))
                            {
                                material = new Material(shader) { m_Name = shader.m_Name, IsPersistentAsset = true };
                                if (tasks[t].usesMaterialVariant)
                                {
                                    // The shader's own state lives in a parent the variant inherits from.
                                    var parent = new Material(shader) { m_Name = shader.m_Name + "_Parent", IsPersistentAsset = true, enableInstancing = instanced };
                                    material.parent = parent;
                                    if (!s_WarnedVariantLock)
                                    {
                                        s_WarnedVariantLock = true;
                                        Debug.LogWarning($"Unbound: {file.Path}: the output '{shader.m_Name}' draws with a material variant; the Editor locks GPU instancing in the variant's parent, which Unbound does not do yet (a script changing the variant's enableInstancing takes effect here).");
                                    }
                                    AddObject(file, parent.m_Name, parent);
                                    imported.Add(parent);
                                }
                                else
                                {
                                    material.enableInstancing = instanced;
                                }
                                AddObject(file, material.m_Name, material);
                                imported.Add(material);
                                try { resource.SetupMaterial(material, tasks[t].model); }
                                catch (Exception e) { Debug.LogException(e); }
                                materials[shader] = material;
                            }
                            data.Processors[s][t] = material;
                        }
                    }
                }

                data.Seal();
                asset.m_Settings = resource.m_Settings;
                asset.m_Data = data;
                asset.m_ShaderSourceIndices = sourceIndices;
                asset.m_Imported = imported.ToArray();
            }
        }

        static void AddObject(LoadedFile file, string identifier, Object obj)
        {
            long id = ScriptedImports.FileId(identifier, obj);
            // Two objects of one class and name: the later keeps the next free id.
            while (file.Objects.ContainsKey(id)) id++;
            file.Objects[id] = obj;
        }

        static void RemoveObject(LoadedFile file, Object obj)
        {
            long found = 0;
            bool any = false;
            foreach (var pair in file.Objects)
                if (ReferenceEquals(pair.Value, obj))
                {
                    found = pair.Key;
                    any = true;
                    break;
                }
            if (any) file.Objects.Remove(found);
        }

        /// <summary>The index of the shader source a task's processor in the asset was made from:
        /// that of the first task (systems, then tasks, in order) whose processor is the object,
        /// -1 when that task has an object of its own or no task has the object.</summary>
        internal static int ShaderIndex(VisualEffectResource resource, Object obj)
        {
            var asset = resource.asset;
            if (asset == null || obj == null) return -1;
            var processors = asset.m_Data.Processors;
            for (int s = 0; s < processors.Length; s++)
                for (int t = 0; t < processors[s].Length; t++)
                    if (ReferenceEquals(processors[s][t], obj)) return asset.m_ShaderSourceIndices[s][t];
            return -1;
        }

        /// <summary>Tells the postprocessors of the package that compiles graphs that the asset
        /// was imported (see the class summary).</summary>
        static void NotifyImported(string path)
        {
            var compile = VisualEffectResource.onCompileResource;
            if (compile == null) return;
            var assembly = compile.Method.DeclaringType?.Assembly;
            if (assembly != null) AssetPostprocessing.ImportedAssets(assembly, new[] { path });
        }
    }
}
