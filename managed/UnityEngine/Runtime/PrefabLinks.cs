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
        /// <summary>The file id of its PrefabInstance document (0 for an instance no file holds).</summary>
        internal long FileId;
        internal string SourceGuid;
        internal bool InAsset;
        /// <summary>The engine made the instance's objects as placeholders: its prefab is missing or
        /// could not load (a variant whose parent is missing).</summary>
        internal bool SourceMissing;
        /// <summary>The PrefabInstance's modifications as its file has them, in order: the source
        /// object they target, the property path, the value as text, an object reference as read.</summary>
        internal readonly List<(ObjectRef Target, string Path, string Value, ObjectRef Reference)> Stored = new List<(ObjectRef, string, string, ObjectRef)>();
        /// <summary>The objects modifications set by PrefabUtility.SetPropertyModifications reference,
        /// by target file id and path (a scene object has no file id before its scene is saved).</summary>
        internal readonly Dictionary<(long TargetFileId, string Path), Object> ReferenceObjects = new Dictionary<(long, string), Object>();
        /// <summary>The stored modifications were changed since the file was read (applied, reverted
        /// or set): a save writes them, not the file's list.</summary>
        internal bool Edited;
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

        /// <summary>Orders the modifications by the file id of their target, keeping the order of
        /// those of one target, as the Editor's PrefabInstance documents have them (every capture of
        /// tests/projects/scenesave; a fresh instance's order in memory is unmeasured).</summary>
        internal void OrderByTarget()
        {
            var ordered = new List<(ObjectRef, string, string, ObjectRef)>(Stored.Count);
            ordered.AddRange(System.Linq.Enumerable.OrderBy(Stored, m => m.Target.FileId));
            Stored.Clear();
            Stored.AddRange(ordered);
        }

        internal bool Modifies(string guid, long fileId, string path)
        {
            foreach (var stored in Stored)
            {
                if (stored.Target.IsNull) continue;
                var m = (Guid: stored.Target.Guid ?? SourceGuid, stored.Target.FileId, stored.Path);
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
        /// <summary>The PrefabInstance field the engine sets on an instance of a missing prefab: the
        /// name its root shows.</summary>
        internal const string MissingPrefabName = "m_UnboundMissingPrefabName";
        /// <summary>The PrefabInstance field naming such an instance's root GameObject.</summary>
        internal const string MissingPrefabRoot = "m_UnboundMissingPrefabRoot";

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
                var data = ReadInstance(doc.Body, inAsset);
                data.FileId = doc.FileId;
                (instances ??= new Dictionary<long, PrefabInstanceData>())[doc.FileId] = data;
            }
            return instances;
        }

        internal static PrefabInstanceData ReadInstance(YNode body, bool inAsset)
        {
            var data = new PrefabInstanceData
            {
                SourceGuid = body.Ref("m_SourcePrefab").Guid,
                InAsset = inAsset,
                SourceMissing = body.TryGet(MissingPrefabName, out _),
            };
            foreach (var m in body.Get("m_Modification")?.Get("m_Modifications")?.Seq() ?? Array.Empty<YNode>())
            {
                // Every entry, one whose target is gone included (it is listed with none).
                data.Stored.Add((m.Ref("target"), m.Str("propertyPath"), m.Str("value"), m.Ref("objectReference")));
            }
            return data;
        }

        /// <summary>Links the object a document made to its source and instance.</summary>
        internal static void Link(Object o, YNode body, Dictionary<long, PrefabInstanceData> instances)
        {
            var link = ReadLink(body, instances);
            if (link != null) Set(o, link);
        }

        internal static PrefabLink ReadLink(YNode body, Dictionary<long, PrefabInstanceData> instances)
        {
            var instance = body.Ref("m_PrefabInstance");
            if (instance.IsNull || !instance.IsLocal || !instances.TryGetValue(instance.FileId, out var data)) return null;
            var source = body.Ref("m_CorrespondingSourceObject");
            return new PrefabLink { SourceGuid = source.Guid ?? data.SourceGuid, SourceFileId = source.FileId, Instance = data };
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
