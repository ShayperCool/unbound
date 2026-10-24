using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// A stable identifier of an object across sessions:
    /// <c>GlobalObjectId_V1-{type}-{asset GUID}-{file id}-{prefab id}</c> with type 1 for imported
    /// assets, 2 for scene objects (the scene's GUID and the object's file id in it), 3 for
    /// Unity's own serialized assets, 4 for built-in ones, and 0 (all zeros) for objects saved
    /// nowhere (tests/projects/editor measured them).
    /// </summary>
    public partial struct GlobalObjectId : IEquatable<GlobalObjectId>, IComparable<GlobalObjectId>
    {
        GUID m_UnboundGuid;
        ulong m_UnboundObject;
        ulong m_UnboundPrefab;
        int m_UnboundType;

        public ulong targetObjectId => m_UnboundObject;
        public ulong targetPrefabId => m_UnboundPrefab;
        public GUID assetGUID => m_UnboundGuid;
        public int identifierType => m_UnboundType;

        public static GlobalObjectId GetGlobalObjectIdSlow(Object targetObject)
        {
            var id = new GlobalObjectId { m_UnboundGuid = new GUID("") };
            if (targetObject == null) return id;
            if (AssetDatabaseRuntime.TryGetLocation(targetObject, out var guid, out var fileId))
            {
                id.m_UnboundGuid = new GUID(guid);
                id.m_UnboundObject = unchecked((ulong)fileId);
                id.m_UnboundType = AssetType(guid);
                return id;
            }
            if (SceneRegistry.TryGetFileId(targetObject, out var scenePath, out var sceneFileId))
            {
                var sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
                if (!string.IsNullOrEmpty(sceneGuid))
                {
                    id.m_UnboundGuid = new GUID(sceneGuid);
                    id.m_UnboundObject = unchecked((ulong)sceneFileId);
                    id.m_UnboundType = 2;
                }
            }
            return id;
        }

        public static GlobalObjectId GetGlobalObjectIdSlow(int instanceId) => GetGlobalObjectIdSlow(ObjectRegistry.FindById(instanceId));

        public static void GetGlobalObjectIdsSlow(Object[] objects, GlobalObjectId[] outputIdentifiers)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            if (outputIdentifiers == null) throw new ArgumentNullException(nameof(outputIdentifiers));
            if (objects.Length != outputIdentifiers.Length) throw new ArgumentException("Size mismatch between objects and outputIdentifiers.");
            for (int i = 0; i < objects.Length; i++) outputIdentifiers[i] = GetGlobalObjectIdSlow(objects[i]);
        }

        public static void GetGlobalObjectIdsSlow(int[] instanceIds, GlobalObjectId[] outputIdentifiers)
        {
            if (instanceIds == null) throw new ArgumentNullException(nameof(instanceIds));
            if (outputIdentifiers == null) throw new ArgumentNullException(nameof(outputIdentifiers));
            if (instanceIds.Length != outputIdentifiers.Length) throw new ArgumentException("Size mismatch between instanceIds and outputIdentifiers.");
            for (int i = 0; i < instanceIds.Length; i++) outputIdentifiers[i] = GetGlobalObjectIdSlow(instanceIds[i]);
        }

        static int AssetType(string guid)
        {
            if (guid.StartsWith("0000000000000000", StringComparison.Ordinal)) return 4;
            try
            {
                var kind = AssetDatabaseRuntime.Info(guid)?.Kind;
                return kind == "yaml" || kind == "prefab" ? 3 : 1;
            }
            catch (Exception)
            {
                return 3;
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "GlobalObjectId_V1-{0}-{1}-{2}-{3}", m_UnboundType, m_UnboundGuid, m_UnboundObject, m_UnboundPrefab);

        public bool Equals(GlobalObjectId other) =>
            m_UnboundType == other.m_UnboundType && m_UnboundGuid.Equals(other.m_UnboundGuid)
            && m_UnboundObject == other.m_UnboundObject && m_UnboundPrefab == other.m_UnboundPrefab;



        public int CompareTo(GlobalObjectId other)
        {
            if (m_UnboundType != other.m_UnboundType) return m_UnboundType.CompareTo(other.m_UnboundType);
            int c = m_UnboundGuid.CompareTo(other.m_UnboundGuid);
            if (c != 0) return c;
            if (m_UnboundObject != other.m_UnboundObject) return m_UnboundObject.CompareTo(other.m_UnboundObject);
            return m_UnboundPrefab.CompareTo(other.m_UnboundPrefab);
        }

        /// <summary>Parses the string form; an all-zero GUID does not parse (measured).</summary>
        public static bool TryParse(string stringValue, out GlobalObjectId id)
        {
            id = default;
            if (stringValue == null) return false;
            var parts = stringValue.Split('-');
            if (parts.Length != 5 || parts[0] != "GlobalObjectId_V1") return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var type)
                || parts[2].Length != 32 || new GUID(parts[2]).Empty()
                || !ulong.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var target)
                || !ulong.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefab))
                return false;
            id = new GlobalObjectId { m_UnboundType = type, m_UnboundGuid = new GUID(parts[2]), m_UnboundObject = target, m_UnboundPrefab = prefab };
            return true;
        }

        public static Object GlobalObjectIdentifierToObjectSlow(GlobalObjectId id)
        {
            var guid = id.m_UnboundGuid.ToString();
            long fileId = unchecked((long)id.m_UnboundObject);
            switch (id.m_UnboundType)
            {
                case 1:
                case 3:
                case 4:
                    return AssetDatabaseRuntime.Load(guid, fileId, typeof(Object));
                case 2:
                    return SceneRegistry.FindByFileId(AssetDatabase.GUIDToAssetPath(guid), fileId);
                default:
                    return null;
            }
        }

        public static void GlobalObjectIdentifiersToObjectsSlow(GlobalObjectId[] identifiers, Object[] outputObjects)
        {
            if (identifiers == null) throw new ArgumentNullException(nameof(identifiers));
            if (outputObjects == null) throw new ArgumentNullException(nameof(outputObjects));
            if (identifiers.Length != outputObjects.Length) throw new ArgumentException("Size mismatch between identifiers and outputObjects.");
            for (int i = 0; i < identifiers.Length; i++) outputObjects[i] = GlobalObjectIdentifierToObjectSlow(identifiers[i]);
        }

        public static int GlobalObjectIdentifierToInstanceIDSlow(GlobalObjectId id)
        {
            var o = GlobalObjectIdentifierToObjectSlow(id);
            return o != null ? o.GetInstanceID() : 0;
        }

        public static void GlobalObjectIdentifiersToInstanceIDsSlow(GlobalObjectId[] identifiers, int[] outputInstanceIDs)
        {
            if (identifiers == null) throw new ArgumentNullException(nameof(identifiers));
            if (outputInstanceIDs == null) throw new ArgumentNullException(nameof(outputInstanceIDs));
            if (identifiers.Length != outputInstanceIDs.Length) throw new ArgumentException("Size mismatch between identifiers and outputInstanceIDs.");
            for (int i = 0; i < identifiers.Length; i++) outputInstanceIDs[i] = GlobalObjectIdentifierToInstanceIDSlow(identifiers[i]);
        }
    }
}
