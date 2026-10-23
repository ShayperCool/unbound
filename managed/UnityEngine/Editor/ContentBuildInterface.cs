using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.Build.Content
{
    public enum FileType
    {
        NonAssetType = 0,
        DeprecatedCachedAssetType = 1,
        SerializedAssetType = 2,
        MetaAssetType = 3,
    }

    /// <summary>An object of an asset as the build pipeline names it: the asset's GUID and the
    /// object's local file id.</summary>
    public partial struct ObjectIdentifier : IEquatable<ObjectIdentifier>, IComparable<ObjectIdentifier>
    {
        internal GUID m_GUID;
        internal long m_LocalIdentifierInFile;
        internal FileType m_FileType;
        internal string m_FilePath;

        public GUID guid { get => m_GUID; internal set => m_GUID = value; }

        public long localIdentifierInFile { get => m_LocalIdentifierInFile; internal set => m_LocalIdentifierInFile = value; }

        public FileType fileType { get => m_FileType; internal set => m_FileType = value; }

        public string filePath { get => m_FilePath ?? ""; internal set => m_FilePath = value; }

        public override string ToString() => $"{{guid: {m_GUID}, fileID: {m_LocalIdentifierInFile}, type: {m_FileType}, path: {filePath}}}";

        public int CompareTo(ObjectIdentifier other)
        {
            int c = m_GUID.CompareTo(other.m_GUID);
            if (c != 0) return c;
            c = m_LocalIdentifierInFile.CompareTo(other.m_LocalIdentifierInFile);
            if (c != 0) return c;
            c = m_FileType.CompareTo(other.m_FileType);
            return c != 0 ? c : string.CompareOrdinal(filePath, other.filePath);
        }

        public static bool operator ==(ObjectIdentifier a, ObjectIdentifier b) => a.Equals(b);

        public static bool operator !=(ObjectIdentifier a, ObjectIdentifier b) => !a.Equals(b);

        public static bool operator <(ObjectIdentifier a, ObjectIdentifier b) => a.CompareTo(b) < 0;

        public static bool operator >(ObjectIdentifier a, ObjectIdentifier b) => a.CompareTo(b) > 0;

        public override bool Equals(object obj) => obj is ObjectIdentifier other && Equals(other);

        public bool Equals(ObjectIdentifier other) =>
            m_GUID.Equals(other.m_GUID) && m_LocalIdentifierInFile == other.m_LocalIdentifierInFile && m_FileType == other.m_FileType && filePath == other.filePath;

        public override int GetHashCode() => HashCode.Combine(m_GUID, m_LocalIdentifierInFile, m_FileType, filePath);

        /// <summary>The object, loaded from its asset.</summary>
        public static Object ToObject(ObjectIdentifier objectId)
        {
            var guid = objectId.m_GUID.ToString();
            foreach (var o in AssetDatabaseRuntime.LoadAll(guid))
                if (AssetDatabaseRuntime.TryGetLocation(o, out _, out var local) && local == objectId.m_LocalIdentifierInFile) return o;
            return null;
        }

        public static int ToInstanceID(ObjectIdentifier objectId)
        {
            var o = ToObject(objectId);
            return o != null ? o.GetInstanceID() : 0;
        }

        public static bool TryGetObjectIdentifier(Object targetObject, out ObjectIdentifier objectId)
        {
            objectId = default;
            if (targetObject == null || !AssetDatabaseRuntime.TryGetLocation(targetObject, out var guid, out var local)) return false;
            objectId = Of(guid, local);
            return true;
        }

        public static bool TryGetObjectIdentifier(int instanceID, out ObjectIdentifier objectId) =>
            TryGetObjectIdentifier(ObjectRegistry.FindById(instanceID), out objectId);

        internal static ObjectIdentifier Of(string guid, long localId)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return new ObjectIdentifier
            {
                m_GUID = new GUID(guid),
                m_LocalIdentifierInFile = localId,
                // Assets in Unity's own format are serialized files; the others are imported.
                m_FileType = IsSerializedFile(path) ? FileType.SerializedAssetType : FileType.MetaAssetType,
                m_FilePath = "",
            };
        }

        static bool IsSerializedFile(string path)
        {
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".asset":
                case ".prefab":
                case ".unity":
                case ".mat":
                case ".anim":
                case ".controller":
                case ".overridecontroller":
                case ".physicmaterial":
                case ".physicsmaterial":
                case ".physicsmaterial2d":
                case ".mask":
                case ".rendertexture":
                case ".spriteatlas":
                case ".guiskin":
                case ".mixer":
                case ".playable":
                case ".signal":
                case ".lighting":
                case ".flare":
                case ".cubemap":
                    return true;
                default:
                    return false;
            }
        }
    }

    public static partial class ContentBuildInterface
    {
        /// <summary>The objects of an asset that a player build would include: everything the
        /// asset holds (Unbound loads no editor-only objects).</summary>
        public static ObjectIdentifier[] GetPlayerObjectIdentifiersInAsset(GUID asset, BuildTarget target)
        {
            var guid = asset.ToString();
            var result = new List<ObjectIdentifier>();
            try
            {
                foreach (var o in AssetDatabaseRuntime.LoadAll(guid))
                    if (o != null && AssetDatabaseRuntime.TryGetLocation(o, out _, out var local)) result.Add(ObjectIdentifier.Of(guid, local));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the objects of asset {guid}: {e.Message}");
            }
            result.Sort();
            return result.ToArray();
        }

        public static Type GetTypeForObject(ObjectIdentifier objectID) => ObjectIdentifier.ToObject(objectID)?.GetType();

        public static Type[] GetTypeForObjects(ObjectIdentifier[] objectIDs)
        {
            if (objectIDs == null) throw new ArgumentNullException(nameof(objectIDs));
            var types = new List<Type>(objectIDs.Length);
            foreach (var id in objectIDs)
            {
                var type = GetTypeForObject(id);
                if (type != null) types.Add(type);
            }
            return types.ToArray();
        }
    }
}
