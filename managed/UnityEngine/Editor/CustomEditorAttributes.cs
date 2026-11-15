using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
    // The reflected cache is also the source used by Editor.CreateEditor.
    internal partial class CustomEditorAttributes
    {
        static readonly Lazy<CustomEditorAttributes> k_Instance = new Lazy<CustomEditorAttributes>(() => new CustomEditorAttributes());
        readonly CustomEditorCache m_Cache = new CustomEditorCache();
        readonly Func<List<MonoEditorType>, Type>[] m_GetEditorWhenBuiltinEnabled;
        readonly Func<List<MonoEditorType>, Type>[] m_GetEditorWhenSRPEnabled;

        static CustomEditorAttributes instance
        {
            get
            {
                var value = k_Instance.Value;
                if (value.m_Cache.DomainChanged) value.Initialize();
                return value;
            }
        }

        CustomEditorAttributes()
        {
            m_GetEditorWhenBuiltinEnabled = new Func<List<MonoEditorType>, Type>[] { FindUnrestricted, FindUnsupported };
            m_GetEditorWhenSRPEnabled = new Func<List<MonoEditorType>, Type>[] { FindPipeline, FindUnrestricted, FindUnsupported };
            Initialize();
        }

        internal readonly partial struct MonoEditorType
        {
            public readonly Type inspectorType;
            public readonly Type[] supportedRenderPipelineTypes;
            public readonly bool editorForChildClasses;
            public readonly bool isFallback;

            public MonoEditorType(Type inspectorType, Type[] supportedRenderPipelineTypes, bool editorForChildClasses, bool isFallback)
            {
                this.inspectorType = inspectorType;
                this.supportedRenderPipelineTypes = supportedRenderPipelineTypes;
                this.editorForChildClasses = editorForChildClasses;
                this.isFallback = isFallback;
            }
        }

        struct MonoEditorTypeStorage
        {
            public List<MonoEditorType> customEditors;
            public List<MonoEditorType> customEditorsMultiEdition;
            public MonoEditorTypeStorage()
            {
                customEditors = new List<MonoEditorType>();
                customEditorsMultiEdition = new List<MonoEditorType>();
            }
        }

        sealed class CustomEditorCache
        {
            readonly Dictionary<Type, MonoEditorTypeStorage> m_CustomEditorCache = new Dictionary<Type, MonoEditorTypeStorage>();
            IReadOnlyList<Assembly> m_Assemblies;
            int m_Generation;
            int m_AssemblyCount;
            internal bool DomainChanged => !ReferenceEquals(m_Assemblies, GameDomain.Assemblies())
                || m_Generation != GameDomain.Generation || m_AssemblyCount != GameDomain.Assemblies().Count;

            internal void Clear()
            {
                m_CustomEditorCache.Clear();
                m_Assemblies = GameDomain.Assemblies();
                m_Generation = GameDomain.Generation;
                m_AssemblyCount = m_Assemblies.Count;
            }

            internal void Add(Type target, MonoEditorType entry, bool multi)
            {
                if (!m_CustomEditorCache.TryGetValue(target, out var storage))
                {
                    storage = new MonoEditorTypeStorage();
                    m_CustomEditorCache.Add(target, storage);
                }
                storage.customEditors.Add(entry);
                storage.customEditors.Sort(Compare);
                if (multi)
                {
                    storage.customEditorsMultiEdition.Add(entry);
                    storage.customEditorsMultiEdition.Sort(Compare);
                }
            }

            static int Compare(MonoEditorType a, MonoEditorType b)
            {
                bool builtInA = a.inspectorType.Assembly == typeof(Editor).Assembly;
                bool builtInB = b.inspectorType.Assembly == typeof(Editor).Assembly;
                if (builtInA != builtInB) return builtInA ? 1 : -1;
                return string.CompareOrdinal(a.inspectorType.FullName, b.inspectorType.FullName);
            }

            internal List<MonoEditorType> Get(Type type, bool multi)
            {
                if (!m_CustomEditorCache.TryGetValue(type, out var storage)
                    && (!type.IsGenericType || !m_CustomEditorCache.TryGetValue(type.GetGenericTypeDefinition(), out storage))) return null;
                return multi ? storage.customEditorsMultiEdition : storage.customEditors;
            }
        }

        internal static Type FindCustomEditorType(object obj, bool multiEdit) =>
            obj == null ? null : FindCustomEditorTypeByType(obj.GetType(), multiEdit);

        internal static Type FindCustomEditorTypeByType(Type type, bool multiEdit)
        {
            if (type == null) return null;
            var registry = instance;
            for (int pass = 0; pass < 2; pass++)
                for (var inspected = type; inspected != null; inspected = inspected.BaseType)
                {
                    var entries = registry.m_Cache.Get(inspected, multiEdit);
                    if (entries == null) continue;
                    var candidates = new List<MonoEditorType>();
                    foreach (var entry in entries)
                        if ((inspected == type || entry.editorForChildClasses) && entry.isFallback == (pass == 1)) candidates.Add(entry);
                    if (candidates.Count == 0) continue;
                    var strategies = GraphicsSettings.isScriptableRenderPipelineEnabled
                        ? registry.m_GetEditorWhenSRPEnabled : registry.m_GetEditorWhenBuiltinEnabled;
                    foreach (var strategy in strategies)
                    {
                        var editor = strategy(candidates);
                        if (editor != null) return editor;
                    }
                }
            return null;
        }

        static Type FindUnrestricted(List<MonoEditorType> entries)
        {
            foreach (var entry in entries) if (entry.supportedRenderPipelineTypes == null) return entry.inspectorType;
            return null;
        }

        static Type FindPipeline(List<MonoEditorType> entries)
        {
            Type inherited = null;
            foreach (var entry in entries)
            {
                if (entry.supportedRenderPipelineTypes == null) continue;
                var mode = SupportedOnRenderPipelineAttribute.GetSupportedMode(entry.supportedRenderPipelineTypes, GraphicsSettings.currentRenderPipelineAssetType);
                if (mode == SupportedOnRenderPipelineAttribute.SupportedMode.Supported) return entry.inspectorType;
                if (mode == SupportedOnRenderPipelineAttribute.SupportedMode.SupportedByBaseClass) inherited = entry.inspectorType;
            }
            return inherited;
        }

        static Type FindUnsupported(List<MonoEditorType> entries) => typeof(Editor).Assembly.GetType("UnityEditor.NotSupportedOnRenderPipelineInspector");

        internal static void Rebuild() => instance.Initialize();

        void Initialize()
        {
            m_Cache.Clear();
            foreach (var type in TypeCache.GetTypesWithAttribute<CustomEditor>())
            {
                var attribute = type.GetCustomAttribute<CustomEditor>(false);
                if (attribute?.m_InspectedType == null || !type.IsSubclassOf(typeof(Editor))) continue;
                var supported = type.GetCustomAttribute<SupportedOnRenderPipelineAttribute>(false);
                Type[] pipelines = supported?.renderPipelineTypes;
                if (supported != null)
                {
                    if (pipelines == null) continue;
                    var unique = new List<Type>();
                    foreach (var pipeline in pipelines) if (pipeline != null && !unique.Contains(pipeline)) unique.Add(pipeline);
                    pipelines = unique.Count == 0 ? null : unique.ToArray();
                }
                m_Cache.Add(attribute.m_InspectedType,
                    new MonoEditorType(type, pipelines, attribute.m_EditorForChildClasses, attribute.isFallback),
                    type.IsDefined(typeof(CanEditMultipleObjects), false));
            }
        }
    }
}
