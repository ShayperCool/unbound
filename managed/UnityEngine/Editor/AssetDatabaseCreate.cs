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
    /// documents in ascending file id order; RemoveObjectFromAsset takes an object out
    /// again (14.remove). Unbound writes script objects (ScriptableObject,
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
            AssetModificationProcessorInternal.OnWillCreateAsset(path);
            // An asset already at the path is replaced.
            if (Exists(path)) Remove(path, trash: false);
            asset.name = Path.GetFileNameWithoutExtension(path);
            var created = new CreatedAsset { Path = path, Main = asset };
            long mainId = MainFileId(asset);
            created.Objects[asset] = mainId;
            File.WriteAllText(FileOf(path), created.Text());
            Track(ImportOne(path, mainId), created);
        }

        /// <summary>A written asset under its GUID: its main object is the file's from here on,
        /// and the saves keep the file in step with its objects.</summary>
        static void Track(string guid, CreatedAsset created)
        {
            created.File = AssetDatabaseRuntime.Adopt(guid, created.Path, created.Main, created.Objects[created.Main]);
            s_Created[guid] = created;
        }

        /// <summary>CreateAsset past its checks, file write and import, for a file the database
        /// lists under this GUID already (the managed tests, which have no importer).</summary>
        internal static void TrackCreated(string guid, string path, Object asset)
        {
            var created = new CreatedAsset { Path = path, Main = asset };
            created.Objects[asset] = MainFileId(asset);
            Track(guid, created);
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
            if (!s_Created.TryGetValue(guid, out var created) && (created = Adopt(guid)) == null)
            {
                Debug.LogError($"Unbound adds objects only to assets it created in this session or native assets in Unity's text serialization whose objects it all writes: '{GetAssetPath(assetObject)}' is left as it is.");
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
            created.Added.Add(objectToAdd);
            EditorUtility.SetDirty(objectToAdd);
            created.Unsaved = true;
        }

        /// <summary>A native asset read from its file (a controller, say) that objects are added to:
        /// written whole from its objects from then on, as one CreateAsset made, when it is the
        /// project's, in Unity's text serialization and Unbound writes every object of it (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/scenesave controller-additions); null otherwise.</summary>
        static CreatedAsset Adopt(string guid)
        {
            var info = AssetDatabaseRuntime.Info(guid);
            var loaded = AssetDatabaseRuntime.LoadFile(guid);
            if (info == null || loaded == null || info.Kind != "yaml" || string.IsNullOrEmpty(info.File)) return null;
            if (!Editable(info.Path) || IsImmutablePackagePath(info.Path) || !File.Exists(info.File)) return null;
            string text = File.ReadAllText(info.File);
            if (!text.StartsWith(UnityYamlText.Preamble, StringComparison.Ordinal)
                && !text.StartsWith(UnityYamlText.Preamble.Replace("\n", "\r\n"), StringComparison.Ordinal)) return null;
            if (loaded.Objects.Values.Any(o => o is not null && !UnityYamlWriter.WritesNew(o))) return null;
            var created = new CreatedAsset { Path = info.Path, Main = loaded.MainObject, File = loaded };
            foreach (var pair in loaded.Objects)
                if (pair.Value is not null) created.Objects[pair.Value] = pair.Key;
            s_Created[guid] = created;
            return created;
        }

        /// <summary>Whether the object was added to an asset that has not been saved since: it is
        /// of the asset at once (path, Contains, LoadAllAssetsAtPath) and dirty, and counts as a
        /// sub-asset from the save on (tests/projects/projectwrites 14.remove.readded.*).</summary>
        static bool AddedUnsaved(Object o)
        {
            foreach (var created in s_Created.Values)
                if (created.Added.Contains(o)) return true;
            return false;
        }

        /// <summary>Whether the asset's main object was taken out and no save gave it another yet:
        /// LoadMainAssetAtPath answers null then (tests/projects/projectwrites
        /// 14.remove.main.mainAsset).</summary>
        static bool MainTakenOut(string path)
        {
            // Only assets made in this session can be so: no lookup for any other project.
            if (s_Created.Count == 0) return false;
            return s_Created.TryGetValue(AssetPathToGUID(path), out var created) && created.MainRemoved;
        }

        /// <summary>Takes an object out of its asset: at once it is an object of no asset, alive
        /// and unchanged, and the asset's listing is without it; the file loses its document with
        /// the next SaveAssets, where references to it are written as none. An object that is no
        /// part of an asset is left alone without a word. The main object can be taken out too:
        /// the asset then has no main object until the save, which makes the first object left
        /// the main one and says when its name is not the file's (matches the Unity 6000.0.71f1
        /// Editor in Edit and Play mode, tests/projects/projectwrites 14.remove). Like
        /// AddObjectToAsset, for the assets CreateAsset made in this session.</summary>
        public static void RemoveObjectFromAsset(Object objectToRemove)
        {
            if (objectToRemove == null) throw new ArgumentNullException(nameof(objectToRemove));
            if (!objectToRemove.IsPersistentAsset) return;
            foreach (var created in s_Created.Values)
            {
                if (!created.Objects.TryGetValue(objectToRemove, out long id)) continue;
                created.Objects.Remove(objectToRemove);
                created.Added.Remove(objectToRemove);
                AssetDatabaseRuntime.RemoveObject(created.File, objectToRemove, id);
                if (ReferenceEquals(created.Main, objectToRemove))
                {
                    created.Main = null;
                    created.MainRemoved = true;
                }
                created.Unsaved = true;
                return;
            }
            Debug.LogError($"Unbound removes objects only from assets it created in this session: '{GetAssetPath(objectToRemove)}' is left as it is.");
        }

        /// <summary>Why a clip Unbound writes cannot be written yet (null otherwise).</summary>
        static string Unwritable(Object o) =>
            o is AnimationClip clip ? AnimationClipFiles.Unwritable(clip)
            : null;

        /// <summary>Whether the object is part of an asset CreateAsset made in this session (one Unbound
        /// writes whole from its objects).</summary>
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
            /// <summary>Objects were added or removed since the file was written.</summary>
            internal bool Unsaved;
            /// <summary>The main object was taken out of the asset (not destroyed): the next save
            /// gives it another.</summary>
            internal bool MainRemoved;
            /// <summary>The objects added since the last save.</summary>
            internal readonly HashSet<Object> Added = new HashSet<Object>(ReferenceEqualityComparer.Instance);

            /// <summary>After the main object was taken out: the object of the lowest file id left
            /// becomes the main one, with a warning when its name is not the file's; false, with an
            /// error, when no object is left (what the Editor writes then, and which of several objects
            /// left it picks, is not measured).</summary>
            internal bool NewMain()
            {
                var left = Objects.Where(p => p.Key != null).OrderBy(p => p.Value).Select(p => p.Key).FirstOrDefault();
                if (left == null)
                {
                    Debug.LogError($"{Log.DiagnosticPrefix}'{Path}' has no object left; it is not saved.");
                    return false;
                }
                Main = left;
                File.MainObject = left;
                MainRemoved = false;
                var fileName = System.IO.Path.GetFileNameWithoutExtension(Path);
                if (left.name != fileName) Debug.LogWarning($"Main Object Name '{left.name}' does not match filename '{fileName}'");
                return true;
            }

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
                if ((created.Main == null && !created.MainRemoved) || !System.IO.File.Exists(FileOf(created.Path)))
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
                if (created.MainRemoved && !created.NewMain()) continue;
                SceneFiles.WriteFile(FileOf(created.Path), text);
                created.Unsaved = false;
                created.Added.Clear();
                foreach (var o in created.Objects.Keys) if (o != null) EditorUtility.ClearDirty(o);
            }
        }
    }
}
