using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>A PrefabInstance of a loaded file (or one PrefabUtility.InstantiatePrefab made): the
    /// prefab it instantiates and the properties it modifies.</summary>
    internal sealed class PrefabInstanceData
    {
        internal string SourceGuid;
        internal bool InAsset;
        /// <summary>The modified properties: the source object they target and the property path.</summary>
        internal readonly List<(string Guid, long FileId, string Path)> Modifications = new List<(string, long, string)>();
        Object m_Handle;

        /// <summary>The object PrefabUtility.GetPrefabInstanceHandle answers: an object of no
        /// engine class (as the Editor's, measured: tests/projects/editorcallbacks).</summary>
        internal Object Handle
        {
            get
            {
                if (m_Handle is null)
                {
                    using (InAsset ? ObjectRegistry.AsAssets() : default)
                        m_Handle = new Object { IsPersistentAsset = InAsset };
                }
                return m_Handle;
            }
        }

        internal bool Modifies(string guid, long fileId, string path)
        {
            foreach (var m in Modifications)
            {
                if (m.FileId != fileId || !string.Equals(m.Guid, guid, StringComparison.OrdinalIgnoreCase)) continue;
                if (m.Path.Length == path.Length ? m.Path == path : m.Path.Length > path.Length && m.Path[path.Length] == '.' && m.Path.StartsWith(path, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Where an object of a prefab instance comes from.</summary>
    internal sealed class PrefabLink
    {
        internal string SourceGuid;
        internal long SourceFileId;
        internal PrefabInstanceData Instance;
    }

    /// <summary>
    /// The prefab instance links the Editor keeps: an instance's objects know the object of the
    /// source prefab they come from and their PrefabInstance, from the documents of their file
    /// (<c>m_CorrespondingSourceObject</c>, <c>m_PrefabInstance</c>). Prefab assets keep them for
    /// their nested instances and variants; scenes opened in Edit mode for their instances. The
    /// scenes Play mode loads (or reloads on entering it) keep none, and clones made by
    /// Object.Instantiate have none (measured: tests/projects/editorcallbacks).
    /// </summary>
    internal static class PrefabLinks
    {
        static readonly ConditionalWeakTable<Object, PrefabLink> s_Links = new ConditionalWeakTable<Object, PrefabLink>();

        internal static PrefabLink Of(Object o) => o is not null && s_Links.TryGetValue(o, out var link) ? link : null;

        internal static void Unlink(Object o) => s_Links.Remove(o);

        internal static void Set(Object o, PrefabLink link)
        {
            s_Links.Remove(o);
            s_Links.Add(o, link);
        }

        /// <summary>The PrefabInstance documents of a file, by file id.</summary>
        internal static Dictionary<long, PrefabInstanceData> Instances(List<YDocument> documents, bool inAsset)
        {
            Dictionary<long, PrefabInstanceData> instances = null;
            foreach (var doc in documents)
            {
                if (doc.Stripped || doc.ClassId != EngineTypes.PrefabInstanceId) continue;
                var data = new PrefabInstanceData { SourceGuid = doc.Body.Ref("m_SourcePrefab").Guid, InAsset = inAsset };
                foreach (var m in doc.Body.Get("m_Modification")?.Get("m_Modifications")?.Seq() ?? Array.Empty<YNode>())
                {
                    var target = m.Ref("target");
                    if (!target.IsNull) data.Modifications.Add((target.Guid, target.FileId, m.Str("propertyPath")));
                }
                (instances ??= new Dictionary<long, PrefabInstanceData>())[doc.FileId] = data;
            }
            return instances;
        }

        /// <summary>Links the object a document made to its source and instance.</summary>
        internal static void Link(Object o, YNode body, Dictionary<long, PrefabInstanceData> instances)
        {
            var instance = body.Ref("m_PrefabInstance");
            if (instance.IsNull || !instance.IsLocal || !instances.TryGetValue(instance.FileId, out var data)) return;
            var source = body.Ref("m_CorrespondingSourceObject");
            Set(o, new PrefabLink { SourceGuid = source.Guid ?? data.SourceGuid, SourceFileId = source.FileId, Instance = data });
        }

        /// <summary>The object of the source prefab an instance's object comes from.</summary>
        internal static Object Source(Object o)
        {
            var link = Of(o);
            if (link == null || link.SourceGuid == null) return null;
            return AssetDatabaseRuntime.Load(link.SourceGuid, link.SourceFileId, o.GetType());
        }
    }
}
