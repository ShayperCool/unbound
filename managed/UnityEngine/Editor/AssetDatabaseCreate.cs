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
    /// asset's main object, named after the file, with its class's main file id (the class id
    /// times 100000: 11400000 for a script object, 2100000 for a material); AddObjectToAsset adds
    /// an object under a random 64-bit file id, written into the file by the next SaveAssets, the
    /// documents in ascending file id order. Unbound writes script objects (ScriptableObject,
    /// MonoBehaviour), materials, physics materials, readable meshes and textures and animation
    /// clips made by script this way (tests/projects/scenesave create-assets, engine-assets,
    /// clip-assets); other objects, and those of these it cannot write yet
    /// (<see cref="Unbound.Runtime.UnityYamlWriter.WritesNew"/>), are refused with an error.
    /// </summary>
    public sealed partial class AssetDatabase
    {
        /// <summary>A main object's file id is its class id times this.</summary>
        const long MainFileIdPerClassId = 100000;

        static long MainFileId(Object o) => EngineTypes.EngineClassIdOf(o.GetType()) * MainFileIdPerClassId;

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
            if (!UnityYamlWriter.WritesNew(asset))
            {
                var reason = Unwritable(asset);
                Debug.LogError(reason != null
                    ? $"Unbound does not write this {asset.GetType().Name} yet: {reason}; '{path}' is not created."
                    : $"Unbound does not write {asset.GetType().Name} assets yet: '{path}' is not created.");
                return;
            }
            // An asset already at the path is replaced.
            if (Exists(path)) Remove(path, trash: false);
            asset.name = Path.GetFileNameWithoutExtension(path);
            var created = new CreatedAsset { Path = path, Main = asset };
            long mainId = MainFileId(asset);
            created.Objects[asset] = mainId;
            File.WriteAllText(FileOf(path), created.Text());
            var guid = ImportOne(path, mainId);
            created.File = AssetDatabaseRuntime.Adopt(guid, path, asset, mainId);
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
            if (!UnityYamlWriter.WritesNew(objectToAdd))
            {
                var reason = Unwritable(objectToAdd);
                Debug.LogError(reason != null
                    ? $"Unbound does not write this {objectToAdd.GetType().Name} yet: {reason}; it is not added to '{created.Path}'."
                    : $"Unbound does not write {objectToAdd.GetType().Name} objects into assets yet: it is not added to '{created.Path}'.");
                return;
            }
            long id;
            do id = System.Random.Shared.NextInt64(long.MinValue, long.MaxValue);
            while (id == 0 || created.Objects.ContainsValue(id));
            created.Objects[objectToAdd] = id;
            AssetDatabaseRuntime.AddObject(created.File, objectToAdd, id);
            created.Unsaved = true;
        }

        /// <summary>Whether the object is part of an asset CreateAsset made in this session (one Unbound
        /// writes whole from its objects).</summary>
        /// <summary>Why an object of a class Unbound writes cannot be written yet (null otherwise).</summary>
        static string Unwritable(Object o) =>
            o is AnimationClip clip ? AnimationClipFiles.Unwritable(clip)
            : ControllerFiles.Writes(o) ? ControllerFiles.Unwritable(o)
            : null;

        internal static bool CreatedThisSession(Object o)
        {
            foreach (var created in s_Created.Values)
                if (created.Objects.ContainsKey(o)) return true;
            return false;
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

        /// <summary>The saves of the assets CreateAsset made (<see cref="AssetFiles.SaveDirty"/>):
        /// each one with objects added or marked dirty since it was written is written again
        /// (<paramref name="only"/>: the one holding that object).</summary>
        internal static void SaveCreated(Object only = null)
        {
            foreach (var pair in s_Created.ToList())
            {
                var created = pair.Value;
                if (created.Main == null || !System.IO.File.Exists(FileOf(created.Path)))
                {
                    s_Created.Remove(pair.Key);
                    continue;
                }
                if (only is not null && !created.Objects.ContainsKey(only)) continue;
                if (!created.Unsaved && !created.Objects.Keys.Any(o => o != null && EditorUtility.IsDirty(o))) continue;
                string text;
                try { text = created.Text(); }
                catch (NotSupportedException e)
                {
                    Debug.LogError($"Unbound: {created.Path} is not saved: {e.Message}");
                    continue;
                }
                SceneFiles.WriteFile(FileOf(created.Path), text);
                created.Unsaved = false;
                foreach (var o in created.Objects.Keys) if (o != null) EditorUtility.ClearDirty(o);
            }
        }
    }
}
