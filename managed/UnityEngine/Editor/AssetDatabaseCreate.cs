using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// Assets made from live objects (matches the Unity 6000.0.71f1 Editor in Edit and Play mode,
    /// tests/projects/projectwrites 04.create): CreateAsset writes the file and its <c>.meta</c>
    /// at once (also between StartAssetEditing and StopAssetEditing), the object becomes the
    /// asset's main object, named after the file, with file id 11400000; AddObjectToAsset adds an
    /// object under a random 64-bit file id, written into the file by the next SaveAssets, the
    /// documents in ascending file id order. Unbound writes script objects (ScriptableObject,
    /// MonoBehaviour) this way; other objects are refused with an error.
    /// </summary>
    public sealed partial class AssetDatabase
    {
        /// <summary>A ScriptableObject's (or MonoBehaviour's) file id as a main object.</summary>
        const long MainScriptFileId = 11400000;

        public static void CreateAsset(Object asset, string path)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            path = Normalize(path);
            if (asset.IsPersistentAsset)
            {
                Debug.LogError($"Couldn't create asset file because the {asset.GetType().Name} '{asset.name}' is already an asset at '{GetAssetPath(asset)}'.");
                return;
            }
            var folder = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || ProjectFolder(folder) == null || string.IsNullOrEmpty(Path.GetExtension(path)))
            {
                Debug.LogError($"Couldn't create asset file at '{path}': its folder does not exist or it has no extension.");
                return;
            }
            if (!Editable(path))
            {
                Refused("AssetDatabase.CreateAsset", path);
                return;
            }
            if (!SerializedTree.IsScript(asset))
            {
                Debug.LogError($"Unbound does not write {asset.GetType().Name} assets yet: '{path}' is not created.");
                return;
            }
            // An asset already at the path is replaced.
            if (Exists(path)) Remove(path, trash: false);
            asset.name = Path.GetFileNameWithoutExtension(path);
            var created = new CreatedAsset { Path = path, Main = asset };
            created.Objects[asset] = MainScriptFileId;
            File.WriteAllText(FileOf(path), created.Text());
            var guid = ImportOne(path, MainScriptFileId);
            created.File = AssetDatabaseRuntime.Adopt(guid, path, asset, MainScriptFileId);
            s_Created[guid] = created;
        }

        public static void AddObjectToAsset(Object objectToAdd, string path) =>
            AddObjectToAsset(objectToAdd, LoadMainAssetAtPath(Normalize(path)));

        public static void AddObjectToAsset(Object objectToAdd, Object assetObject)
        {
            if (objectToAdd == null) throw new ArgumentNullException(nameof(objectToAdd));
            if (assetObject == null || !assetObject.IsPersistentAsset)
            {
                Debug.LogError("AddObjectToAsset can only be used with an asset already in the project.");
                return;
            }
            if (objectToAdd.IsPersistentAsset)
            {
                Debug.LogError($"Couldn't add object to asset file because the {objectToAdd.GetType().Name} '{objectToAdd.name}' is already an asset at '{GetAssetPath(objectToAdd)}'.");
                return;
            }
            var guid = AssetPathToGUID(GetAssetPath(assetObject));
            if (!s_Created.TryGetValue(guid, out var created))
            {
                Debug.LogError($"Unbound adds objects only to assets it created in this session: '{GetAssetPath(assetObject)}' is left as it is.");
                return;
            }
            if (!SerializedTree.IsScript(objectToAdd))
            {
                Debug.LogError($"Unbound does not write {objectToAdd.GetType().Name} objects into assets yet: it is not added to '{created.Path}'.");
                return;
            }
            long id;
            do id = System.Random.Shared.NextInt64(long.MinValue, long.MaxValue);
            while (id == 0 || created.Objects.ContainsValue(id));
            created.Objects[objectToAdd] = id;
            AssetDatabaseRuntime.AddObject(created.File, objectToAdd, id);
            created.Unsaved = true;
        }

        /// <summary>The assets CreateAsset made in this session, by GUID.</summary>
        static readonly Dictionary<string, CreatedAsset> s_Created = new Dictionary<string, CreatedAsset>(StringComparer.OrdinalIgnoreCase);

        sealed class CreatedAsset
        {
            internal string Path;
            internal Object Main;
            internal LoadedFile File;
            internal readonly Dictionary<Object, long> Objects = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            /// <summary>Objects were added since the file was written.</summary>
            internal bool Unsaved;

            internal string Text() =>
                UnityYamlWriter.Write(Objects.Where(p => p.Key != null).OrderBy(p => p.Value).Select(p => p.Key).ToArray(), Objects);
        }

        /// <summary>SaveAssets for the assets CreateAsset made: each one with objects added or
        /// marked dirty since it was written is written again.</summary>
        static void SaveCreated()
        {
            foreach (var pair in s_Created.ToList())
            {
                var created = pair.Value;
                if (created.Main == null || !System.IO.File.Exists(FileOf(created.Path)))
                {
                    s_Created.Remove(pair.Key);
                    continue;
                }
                if (!created.Unsaved && !created.Objects.Keys.Any(o => o != null && EditorUtility.IsDirty(o))) continue;
                System.IO.File.WriteAllText(FileOf(created.Path), created.Text());
                created.Unsaved = false;
                foreach (var o in created.Objects.Keys) if (o != null) EditorUtility.ClearDirty(o);
            }
        }
    }
}
